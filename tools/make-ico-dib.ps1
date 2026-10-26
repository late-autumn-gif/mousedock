param(
    [Parameter(Mandatory = $true)][string]$SourcePng,
    [Parameter(Mandatory = $true)][string]$OutIco,
    [int]$CropPercent = 74,
    [int]$CornerPercent = 22
)

Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Bitmap]::new([string](Resolve-Path $SourcePng))
$side = [int]($src.Width * $CropPercent / 100.0)
$ox = [int](($src.Width - $side) / 2)
$oy = [int](($src.Height - $side) / 2)

function New-RoundedBitmap([int]$size) {
    $bmp = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $r = [int]($size * $CornerPercent / 100.0); $d = $r * 2
    $p = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $p.AddArc(0, 0, $d, $d, 180, 90); $p.AddArc($size - $d, 0, $d, $d, 270, 90)
    $p.AddArc($size - $d, $size - $d, $d, $d, 0, 90); $p.AddArc(0, $size - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $g.SetClip($p)
    $g.DrawImage($src, [System.Drawing.Rectangle]::new(0, 0, $size, $size),
        [System.Drawing.Rectangle]::new($ox, $oy, $side, $side), [System.Drawing.GraphicsUnit]::Pixel)
    $g.ResetClip(); $g.Dispose(); $p.Dispose()
    return $bmp
}

# 32bpp DIB 帧：老式图标读取器（Winstep Nexus 之类）只认 BMP 帧，不认 PNG 帧
function ConvertTo-DibFrame([System.Drawing.Bitmap]$bmp) {
    $size = $bmp.Width
    $ms = [System.IO.MemoryStream]::new()
    $bw = [System.IO.BinaryWriter]::new($ms)

    # BITMAPINFOHEADER
    $bw.Write([UInt32]40)
    $bw.Write([Int32]$size)
    $bw.Write([Int32]($size * 2))     # XOR + AND 高度
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]0)
    $bw.Write([UInt32]($size * $size * 4))
    $bw.Write([Int32]0); $bw.Write([Int32]0)
    $bw.Write([UInt32]0); $bw.Write([UInt32]0)

    # XOR 位图（BGRA，自下而上）
    for ($y = $size - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $size; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
        }
    }

    # AND 掩码（1bpp，每行 4 字节对齐）——32bpp 用 alpha 通道，这里全 0
    $maskRow = [int]([Math]::Ceiling($size / 8.0))
    if ($maskRow % 4 -ne 0) { $maskRow += (4 - $maskRow % 4) }
    $zeros = New-Object byte[] $maskRow
    for ($y = 0; $y -lt $size; $y++) { $bw.Write($zeros) }

    $bw.Flush()
    return ,([byte[]]$ms.ToArray())
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = @()
foreach ($s in $sizes) {
    $bmp = New-RoundedBitmap $s
    if ($s -ge 256) {
        # 256 用 PNG 压缩，避免文件过大（Windows 支持；不支持的读取器会回退到 128）
        $ms = [System.IO.MemoryStream]::new()
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $frames += , @{ Size = $s; Bytes = [byte[]]$ms.ToArray(); Png = $true }
        $ms.Dispose()
    } else {
        $bytes = ConvertTo-DibFrame $bmp
        $frames += , @{ Size = $s; Bytes = [byte[]]$bytes; Png = $false }
    }
    $bmp.Dispose()
}
$src.Dispose()

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
foreach ($f in $frames) { $bw.Write([byte[]]$f.Bytes) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($OutIco, $out.ToArray())
$bw.Dispose(); $out.Dispose()

Write-Host ("DIB ICO: {0} ({1} bytes, {2} frames)" -f $OutIco, (Get-Item $OutIco).Length, $frames.Count)
