@echo off
if exist "%~dp0artifacts\VisualizerPro-win-x64\VisualizerPro.exe" (
    start "" "%~dp0artifacts\VisualizerPro-win-x64\VisualizerPro.exe" %*
    exit /b 0
)
echo Build the portable package first: pwsh -File scripts/Publish.ps1
pause
