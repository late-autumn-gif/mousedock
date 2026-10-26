param(
    [Parameter(Mandatory = $true)][string]$SourcePng,
    [Parameter(Mandatory = $true)][string]$OutIco,
    [int]$CropPercent = 74,      # 主体（深色圆角方块）大致占画面的比例
    [int]$CornerPercent = 22     # 圆角半径占裁切边长的比例
)

Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Bitmap]::new([string](Resolve-Path $SourcePng))

$side = [int]($src.Width * $CropPercent / 100.0)
$ox = [int](($src.Width - $side) / 2)
$oy = [int](($src.Height - $side) / 2)

$radius = [int]($side * $CornerPercent / 100.0)
$d = $radius * 2

$masked = [System.Drawing.Bitmap]::new($side, $side, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($masked)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.Clear([System.Drawing.Color]::Transparent)

$path = [System.Drawing.Drawing2D.GraphicsPath]::new()
$path.AddArc(0, 0, $d, $d, 180, 90)
$path.AddArc($side - $d, 0, $d, $d, 270, 90)
$path.AddArc($side - $d, $side - $d, $d, $d, 0, 90)
$path.AddArc(0, $side - $d, $d, $d, 90, 90)
$path.CloseFigure()

$g.SetClip($path)
$g.DrawImage($src, [System.Drawing.Rectangle]::new(0, 0, $side, $side),
    [System.Drawing.Rectangle]::new($ox, $oy, $side, $side), [System.Drawing.GraphicsUnit]::Pixel)
$g.ResetClip()
$g.Dispose()
$path.Dispose()
$src.Dispose()

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = @()
foreach ($size in $sizes) {
    $bmp = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gg = [System.Drawing.Graphics]::FromImage($bmp)
    $gg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $gg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $gg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $gg.Clear([System.Drawing.Color]::Transparent)
    $gg.DrawImage($masked, 0, 0, $size, $size)
    $gg.Dispose()
    $ms = [System.IO.MemoryStream]::new()
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $frames += , @{ Size = $size; Bytes = $ms.ToArray() }
    $ms.Dispose()
}

# 附带一张 256 PNG 给 WPF 窗口用
$pngOut = [System.IO.Path]::ChangeExtension($OutIco, ".png")
$big = [System.Drawing.Bitmap]::new(256, 256, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$gb = [System.Drawing.Graphics]::FromImage($big)
$gb.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$gb.Clear([System.Drawing.Color]::Transparent)
$gb.DrawImage($masked, 0, 0, 256, 256)
$gb.Dispose()
$big.Save($pngOut, [System.Drawing.Imaging.ImageFormat]::Png)
$big.Dispose()
$masked.Dispose()

$out = [System.IO.MemoryStream]::new()
$bw = [System.IO.BinaryWriter]::new($out)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim)
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$f.Bytes.Length); $bw.Write([UInt32]$offset)
    $offset += $f.Bytes.Length
}
foreach ($f in $frames) { $bw.Write($f.Bytes) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($OutIco, $out.ToArray())
$bw.Dispose(); $out.Dispose()

Write-Host ("ICO: {0} ({1} bytes, {2} frames)  PNG: {3}" -f $OutIco, (Get-Item $OutIco).Length, $frames.Count, $pngOut)
