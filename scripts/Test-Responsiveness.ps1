param([string]$Executable = '', [string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
if (-not $Executable) { $Executable = Join-Path $taskRoot 'artifacts\VisualizerPro-win-x64\VisualizerPro.exe' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $taskRoot ('artifacts\responsiveness-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$Executable = [IO.Path]::GetFullPath($Executable)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$taskPreviousData = $env:VISUALIZERPRO_DATA_DIR
try {
    $env:VISUALIZERPRO_DATA_DIR = Join-Path $OutputDirectory 'isolated-data'
    $taskProcess = Start-Process -FilePath $Executable -ArgumentList ('--responsiveness-test "' + $OutputDirectory + '"') -WindowStyle Hidden -PassThru
    if (-not $taskProcess.WaitForExit(45000)) {
        Stop-Process -Id $taskProcess.Id
        throw 'Responsiveness check exceeded 45 seconds; its test process was closed.'
    }
    $taskReport = Get-Content -LiteralPath (Join-Path $OutputDirectory 'responsiveness.json') -Raw | ConvertFrom-Json
    if (-not $taskReport.startupFits -or -not $taskReport.restoredFits) { throw 'Fullscreen does not match the monitor bounds.' }
    foreach ($taskPhase in $taskReport.phases | Where-Object name -In 'stable','recovered') {
        if ($taskPhase.spectrum.count -lt 10 -or $taskPhase.spectrum.p50Ms -ge 80) { throw "Renderer stopped or fell to idle refresh in $($taskPhase.name)." }
    }
    $taskReport | ConvertTo-Json -Depth 6
} finally { $env:VISUALIZERPRO_DATA_DIR = $taskPreviousData }
