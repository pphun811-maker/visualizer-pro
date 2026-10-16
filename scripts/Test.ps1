param([switch]$Reference, [switch]$Desktop)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Push-Location $taskRoot
try {
    dotnet restore tests/VisualizerPro.Tests/VisualizerPro.Tests.csproj --configfile NuGet.Config
    if ($LASTEXITCODE -ne 0) { throw 'Test restore failed' }
    dotnet run --project tests/VisualizerPro.Tests --no-restore -c Release
    if ($LASTEXITCODE -ne 0) { throw 'DSP tests failed' }
    if ($Reference) { & "$PSScriptRoot\Test-Reference.ps1" }
    if ($Desktop) {
        dotnet restore src/VisualizerPro.App/VisualizerPro.App.csproj --configfile NuGet.Config
        dotnet build src/VisualizerPro.App/VisualizerPro.App.csproj --no-restore -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed' }
        $env:VISUALIZERPRO_DATA_DIR = Join-Path $taskRoot 'artifacts\desktop-test-data'
        $taskExe = Join-Path $taskRoot 'src\VisualizerPro.App\bin\Release\net10.0-windows10.0.19041.0\VisualizerPro.exe'
        $taskReports = Join-Path $taskRoot 'artifacts\desktop-tests'
        Start-Process -FilePath $taskExe -ArgumentList ('--integration-test "' + $taskReports + '"') -WindowStyle Hidden -Wait
        $taskReport = Get-Content (Join-Path $taskReports 'integration.json') -Raw | ConvertFrom-Json
        $taskReport.results
        if (-not $taskReport.success) { throw 'Desktop integration failed' }
    }
} finally { Pop-Location }
