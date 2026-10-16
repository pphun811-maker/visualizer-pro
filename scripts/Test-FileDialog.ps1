param([string]$Executable = '', [string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
if (-not $Executable) { $Executable = Join-Path $taskRoot 'artifacts\VisualizerPro-win-x64\VisualizerPro.exe' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $taskRoot ('artifacts\file-dialog-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$Executable = [IO.Path]::GetFullPath($Executable)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$taskPreviousData = $env:VISUALIZERPRO_DATA_DIR
try {
    $env:VISUALIZERPRO_DATA_DIR = Join-Path $OutputDirectory 'isolated-data'
    $taskProcess = Start-Process -FilePath $Executable -ArgumentList ('--file-dialog-test "' + $OutputDirectory + '"') -WindowStyle Hidden -PassThru
    if (-not $taskProcess.WaitForExit(45000)) {
        Stop-Process -Id $taskProcess.Id
        throw 'File dialog check exceeded 45 seconds; its test process was closed.'
    }
    if (Test-Path (Join-Path $OutputDirectory 'failure.txt')) { throw (Get-Content (Join-Path $OutputDirectory 'failure.txt') -Raw) }
    $taskReport = Get-Content (Join-Path $OutputDirectory 'file-dialog.json') -Raw | ConvertFrom-Json
    if ($taskReport.checks.Count -ne 10) { throw 'Incomplete file dialog checks.' }
    $taskReport | ConvertTo-Json -Depth 6
} finally { $env:VISUALIZERPRO_DATA_DIR = $taskPreviousData }
