param([string]$Destination = (Join-Path $PSScriptRoot '..\tools\ffmpeg'))
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$taskDest = [IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Path $taskDest -Force | Out-Null
$taskZip = Join-Path $taskDest 'download.zip'
$taskExtract = Join-Path $taskDest 'download'
Invoke-WebRequest -Uri 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-lgpl.zip' -OutFile $taskZip
Expand-Archive -LiteralPath $taskZip -DestinationPath $taskExtract -Force
$taskBin = Get-ChildItem -LiteralPath $taskExtract -Recurse -Filter ffmpeg.exe | Select-Object -First 1
if (-not $taskBin) { throw 'FFmpeg archive has no ffmpeg.exe' }
$taskPackage = Split-Path (Split-Path $taskBin.FullName -Parent) -Parent
Copy-Item -LiteralPath $taskBin.FullName -Destination (Join-Path $taskDest 'ffmpeg.exe') -Force
Copy-Item -LiteralPath (Join-Path $taskBin.DirectoryName 'ffprobe.exe') -Destination (Join-Path $taskDest 'ffprobe.exe') -Force
foreach ($taskLicense in @('LICENSE.txt','LICENSE','COPYING.LGPLv2.1','COPYING.LGPLv3')) {
    if (Test-Path -LiteralPath (Join-Path $taskPackage $taskLicense)) { Copy-Item -LiteralPath (Join-Path $taskPackage $taskLicense) -Destination $taskDest -Force }
}
$taskRootCheck = [IO.Path]::GetFullPath($taskExtract)
if (-not $taskRootCheck.StartsWith($taskDest + [IO.Path]::DirectorySeparatorChar)) { throw 'Unexpected extraction path' }
Remove-Item -LiteralPath $taskRootCheck -Recurse -Force
Remove-Item -LiteralPath $taskZip -Force
Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/FFmpeg/FFmpeg/master/COPYING.GPLv3' -OutFile (Join-Path $taskDest 'COPYING.GPLv3')
& (Join-Path $taskDest 'ffmpeg.exe') -version | Set-Content -LiteralPath (Join-Path $taskDest 'BUILD-INFO.txt')
Get-Content -LiteralPath (Join-Path $taskDest 'BUILD-INFO.txt') -TotalCount 1
