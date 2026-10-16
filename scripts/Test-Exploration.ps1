param([string]$Executable = '', [string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
if (-not $Executable) { $Executable = Join-Path $taskRoot 'artifacts\VisualizerPro-win-x64\VisualizerPro.exe' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $taskRoot 'artifacts\exploration' }
$Executable = [IO.Path]::GetFullPath($Executable)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$taskPreviousData = $env:VISUALIZERPRO_DATA_DIR
try {
    $env:VISUALIZERPRO_DATA_DIR = Join-Path $OutputDirectory 'isolated-data'
    $taskProcess = Start-Process -FilePath $Executable -ArgumentList ('--exploration-test "' + $OutputDirectory + '"') -WindowStyle Hidden -PassThru
    if (-not $taskProcess.WaitForExit(45000)) {
        Stop-Process -Id $taskProcess.Id
        throw 'Exploration check exceeded 45 seconds; test process closed.'
    }
    $taskReport = Get-Content -LiteralPath (Join-Path $OutputDirectory 'exploration.json') -Raw | ConvertFrom-Json
    $taskReport | ConvertTo-Json -Depth 6
    if (-not $taskReport.success) { throw 'Exploration checks failed.' }
} finally { $env:VISUALIZERPRO_DATA_DIR = $taskPreviousData }
