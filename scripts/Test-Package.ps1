$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskExe = Join-Path $taskRoot 'artifacts\VisualizerPro-win-x64\VisualizerPro.exe'
$taskOutput = Join-Path $taskRoot 'artifacts\package-tests'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null

$env:VISUALIZERPRO_DATA_DIR = Join-Path $taskOutput 'compatibility-data'
$env:VISUALIZERPRO_SOFTWARE = '1'
$taskImage = Join-Path $taskOutput 'compatibility.png'
Start-Process -FilePath $taskExe -ArgumentList ('--smoke-test "' + $taskImage + '"') -WindowStyle Hidden -Wait
if (-not (Test-Path -LiteralPath ($taskImage + '.ok'))) { throw 'Software-renderer smoke failed' }
Remove-Item Env:\VISUALIZERPRO_SOFTWARE
Write-Output 'PASS packaged software-renderer fallback'

$env:VISUALIZERPRO_DATA_DIR = Join-Path $taskOutput 'integration-data'
$taskIntegration = Join-Path $taskOutput 'integration'
Start-Process -FilePath $taskExe -ArgumentList ('--integration-test "' + $taskIntegration + '"') -WindowStyle Hidden -Wait
$taskReport = Get-Content (Join-Path $taskIntegration 'integration.json') -Raw | ConvertFrom-Json
$taskReport.results
if (-not $taskReport.success) { throw 'Packaged integration failed' }

$env:VISUALIZERPRO_DATA_DIR = Join-Path $taskOutput 'performance-data'
$taskPerformance = Join-Path $taskOutput 'performance'
Start-Process -FilePath $taskExe -ArgumentList ('--performance-test 60 "' + $taskPerformance + '"') -WindowStyle Hidden -Wait
Get-Content (Join-Path $taskPerformance 'performance.json')
