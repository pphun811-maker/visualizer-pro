$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskFfmpeg = Join-Path $taskRoot 'tools\ffmpeg\ffmpeg.exe'
$taskTest = Join-Path $taskRoot 'tests\VisualizerPro.Tests\bin\Release\net10.0\VisualizerPro.Tests.dll'
$taskOutput = Join-Path $taskRoot 'artifacts\reference'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskCases = @(
    @{Name='steady-44100'; Rate=44100; Duration=4; Expression='0.1*sin(2*PI*1000*t)|0.1*sin(2*PI*1000*t)'},
    @{Name='relative-gating'; Rate=48000; Duration=12; Expression='0.3*sin(2*PI*997*t)*(0.001+0.999*sin(PI*t/12)^12)|0.2*sin(2*PI*2003*t)*(0.001+0.999*sin(PI*t/12)^12)'},
    @{Name='intersample-peak'; Rate=48000; Duration=4; Expression='0.99*sin(2*PI*12000*t+PI/4)*(1-exp(-t*100))*(1-exp(-(4-t)*100))|0.99*sin(2*PI*12000*t+PI/4)*(1-exp(-t*100))*(1-exp(-(4-t)*100))'},
    @{Name='pink-noise'; Rate=48000; Duration=8; Filter='anoisesrc=c=pink:r=48000:a=0.25:d=8:seed=2718,pan=stereo|c0=c0|c1=c0,afade=t=in:d=0.01,afade=t=out:st=7.99:d=0.01'}
)
$taskResults = @()
foreach ($taskCase in $taskCases) {
    $taskPcm = Join-Path $taskOutput ($taskCase.Name + '.f32')
    $taskFilter = if ($taskCase.ContainsKey('Filter')) { $taskCase.Filter } else { 'aevalsrc=' + $taskCase.Expression + ':s=' + $taskCase.Rate + ':d=' + $taskCase.Duration }
    & $taskFfmpeg -hide_banner -v error -y -f lavfi -i $taskFilter -ac 2 -f f32le $taskPcm
    if ($LASTEXITCODE -ne 0) { throw 'Fixture generation failed' }
    $taskReference = (& $taskFfmpeg -hide_banner -f f32le -ar $taskCase.Rate -ac 2 -i $taskPcm -af 'ebur128=peak=true' -f null NUL 2>&1 | Out-String)
    $taskReference | Set-Content -LiteralPath (Join-Path $taskOutput ($taskCase.Name + '-ffmpeg.txt'))
    $taskMatch = [regex]::Match($taskReference, '(?s)Summary:.*?I:\s*([-\d.]+) LUFS.*?Peak:\s*([-\d.]+) dBFS')
    if (-not $taskMatch.Success) { throw 'Unable to parse FFmpeg reference summary' }
    $taskMetrics = (& dotnet $taskTest --measure $taskPcm $taskCase.Rate | ConvertFrom-Json)
    $taskDeltaI = [Math]::Abs($taskMetrics.integrated - [double]$taskMatch.Groups[1].Value)
    $taskDeltaTp = [Math]::Abs($taskMetrics.truePeak - [double]$taskMatch.Groups[2].Value)
    $taskResult = [pscustomobject]@{Name=$taskCase.Name; Integrated=$taskMetrics.integrated; ReferenceI=[double]$taskMatch.Groups[1].Value; TruePeak=$taskMetrics.truePeak; ReferenceTP=[double]$taskMatch.Groups[2].Value; Pass=($taskDeltaI -le 0.15 -and $taskDeltaTp -le 0.25)}
    $taskResults += $taskResult
}
$taskResults | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskOutput 'results.json')
$taskResults | Format-Table
if ($taskResults.Where({ -not $_.Pass }).Count -gt 0) { throw 'Reference comparison failed' }
