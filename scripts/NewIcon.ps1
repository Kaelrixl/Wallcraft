$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$path = Join-Path $PSScriptRoot '..\Assets\Wallcraft.ico'
$images = @()
foreach ($size in @(16, 32, 48, 64, 128, 256)) {
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($size / 256.0, $size / 256.0)
    $back = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(37, 83, 195))
    $light = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(230, 241, 255))
    $front = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(75, 207, 194))
    $pen = [Drawing.Pen]::new([Drawing.Color]::White, 12)
    try {
        $graphics.FillRectangle($back, 12, 12, 232, 232)
        $graphics.DrawRectangle($pen, 40, 44, 176, 168)
        $graphics.FillEllipse($light, 158, 68, 30, 30)
        $graphics.FillPolygon($light, [Drawing.PointF[]]@([Drawing.PointF]::new(46, 196), [Drawing.PointF]::new(104, 110), [Drawing.PointF]::new(168, 196)))
        $graphics.FillPolygon($front, [Drawing.PointF[]]@([Drawing.PointF]::new(106, 206), [Drawing.PointF]::new(166, 136), [Drawing.PointF]::new(210, 180), [Drawing.PointF]::new(210, 206)))
        $stream = [IO.MemoryStream]::new()
        $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
        $images += ,$stream.ToArray()
        $stream.Dispose()
    } finally {
        $pen.Dispose(); $back.Dispose(); $light.Dispose(); $front.Dispose()
        $graphics.Dispose(); $bitmap.Dispose()
    }
}
$file = [IO.File]::Create($path)
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    $sizes = @(16, 32, 48, 64, 128, 256)
    for ($i = 0; $i -lt $images.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
} finally { $writer.Dispose() }
