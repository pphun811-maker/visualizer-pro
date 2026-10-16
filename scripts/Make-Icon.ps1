$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskAssets = Join-Path $taskRoot 'src\VisualizerPro.App\Assets'
New-Item -ItemType Directory -Path $taskAssets -Force | Out-Null
$taskImages = @()
foreach ($taskSize in @(32,48,128,256)) {
    $taskBitmap = [Drawing.Bitmap]::new($taskSize,$taskSize)
    $taskGraphics = [Drawing.Graphics]::FromImage($taskBitmap)
    $taskGraphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $taskGraphics.Clear([Drawing.Color]::FromArgb(255,17,17,17))
    $taskPen = [Drawing.Pen]::new([Drawing.Color]::FromArgb(220,220,220),[single]($taskSize*0.055))
    $taskPen.StartCap = [Drawing.Drawing2D.LineCap]::Round
    $taskPen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $taskValues = @(0.25,0.47,0.73,0.58,0.85,0.4)
    for ($taskIndex=0; $taskIndex -lt $taskValues.Count; $taskIndex++) {
        $taskX = [single]($taskSize*(0.2+$taskIndex*0.12))
        $taskBottom = [single]($taskSize*0.79)
        $taskGraphics.DrawLine($taskPen,$taskX,$taskBottom,$taskX,[single]($taskBottom-$taskSize*0.64*$taskValues[$taskIndex]))
    }
    $taskStream = [IO.MemoryStream]::new()
    $taskBitmap.Save($taskStream,[Drawing.Imaging.ImageFormat]::Png)
    $taskImages += ,([pscustomobject]@{Size=$taskSize; Bytes=$taskStream.ToArray()})
    $taskStream.Dispose(); $taskPen.Dispose(); $taskGraphics.Dispose(); $taskBitmap.Dispose()
}
$taskWriter = [IO.BinaryWriter]::new([IO.File]::Create((Join-Path $taskAssets 'app.ico')))
try {
    $taskWriter.Write([uint16]0); $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]$taskImages.Count)
    $taskOffset = 6 + 16*$taskImages.Count
    foreach ($taskImage in $taskImages) {
        $taskDimension = [byte]0; if ($taskImage.Size -lt 256) { $taskDimension=[byte]$taskImage.Size }
        $taskWriter.Write($taskDimension); $taskWriter.Write($taskDimension); $taskWriter.Write([byte]0); $taskWriter.Write([byte]0)
        $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]32); $taskWriter.Write([uint32]$taskImage.Bytes.Length); $taskWriter.Write([uint32]$taskOffset)
        $taskOffset += $taskImage.Bytes.Length
    }
    foreach ($taskImage in $taskImages) { $taskWriter.Write([byte[]]$taskImage.Bytes) }
} finally { $taskWriter.Dispose() }
