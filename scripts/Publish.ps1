param([switch]$SkipRestore)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Push-Location $taskRoot
try {
    if (-not (Test-Path 'tools\ffmpeg\ffmpeg.exe')) { & "$PSScriptRoot\Get-FFmpeg.ps1" }
    if (-not $SkipRestore) {
        dotnet restore src/VisualizerPro.App/VisualizerPro.App.csproj --configfile NuGet.Config -r win-x64
        if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
    }
    dotnet publish src/VisualizerPro.App/VisualizerPro.App.csproj --no-restore -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -o artifacts/VisualizerPro-win-x64
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    Copy-Item README.md,THIRD-PARTY-NOTICES.md,VALIDATION.md -Destination artifacts/VisualizerPro-win-x64 -Force
    $taskLicenseDest = Join-Path $taskRoot 'artifacts\VisualizerPro-win-x64\licenses'
    New-Item -ItemType Directory -Path $taskLicenseDest -Force | Out-Null
    $taskAssets = Get-Content 'src\VisualizerPro.App\obj\project.assets.json' -Raw | ConvertFrom-Json
    foreach ($taskLibrary in $taskAssets.libraries.PSObject.Properties) {
        if ($taskLibrary.Value.type -ne 'package') { continue }
        foreach ($taskFolder in $taskAssets.packageFolders.PSObject.Properties.Name) {
            $taskPackage = Join-Path $taskFolder $taskLibrary.Value.path
            if (-not (Test-Path -LiteralPath $taskPackage)) { continue }
            Get-ChildItem -LiteralPath $taskPackage -File | Where-Object { $_.Name -match '^(LICENSE|NOTICE|COPYING|THIRD.PARTY)' } | ForEach-Object {
                Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $taskLicenseDest (($taskLibrary.Name -replace '/', '-') + '-' + $_.Name)) -Force
            }
        }
    }
    Compress-Archive -Path artifacts/VisualizerPro-win-x64/* -DestinationPath artifacts/VisualizerPro-win-x64.zip -Force
    Write-Output (Join-Path $taskRoot 'artifacts\VisualizerPro-win-x64\VisualizerPro.exe')
} finally { Pop-Location }
