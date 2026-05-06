# Generates src/Jaminator/Jaminator.ico from scratch.
# Produces a multi-size ICO (16, 32, 48, 64, 128, 256) so Windows Search,
# the Start Menu, the taskbar, and Explorer all pick the right resolution
# without scaling. Run once when you want to regenerate the brand mark.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$out = Join-Path $repoRoot 'src\Jaminator\Jaminator.ico'

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

    # Margin scales with size — small icons get less padding so the J reads bigger
    $margin = [int]([Math]::Max(1, $size / 32))
    $rect = New-Object System.Drawing.Rectangle $margin, $margin, ($size - 2*$margin), ($size - 2*$margin)
    $radius = [int]($size * 0.22)

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.Left, $rect.Top, $radius, $radius, 180, 90)
    $path.AddArc($rect.Right - $radius, $rect.Top, $radius, $radius, 270, 90)
    $path.AddArc($rect.Right - $radius, $rect.Bottom - $radius, $radius, $radius, 0, 90)
    $path.AddArc($rect.Left, $rect.Bottom - $radius, $radius, $radius, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(124, 58, 237),
        [System.Drawing.Color]::FromArgb(245, 158, 11),
        45)
    $g.FillPath($brush, $path)

    if ($size -ge 32) {
        $highlight = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            $rect,
            [System.Drawing.Color]::FromArgb(80, 255, 255, 255),
            [System.Drawing.Color]::FromArgb(0, 255, 255, 255),
            90)
        $g.FillPath($highlight, $path)
        $highlight.Dispose()
    }

    # White J — proportional to size
    $fontSize = [int]($size * 0.66)
    $font = New-Object System.Drawing.Font 'Segoe UI', $fontSize, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $textBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $textRect = New-Object System.Drawing.RectangleF 0, ([float](-$size * 0.04)), ([float]$size), ([float]$size)
    $g.DrawString('J', $font, $textBrush, $textRect, $sf)

    $g.Dispose(); $brush.Dispose(); $path.Dispose(); $font.Dispose(); $textBrush.Dispose()
    return $bmp
}

# Build PNG bytes for each size
$sizes = @(16, 32, 48, 64, 128, 256)
$pngs = @{}
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs[$s] = $ms.ToArray()
    $bmp.Dispose(); $ms.Dispose()
}

# Wrap in ICO container — one ICONDIRENTRY per size, each pointing at its PNG
$ico = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ico

$count = $sizes.Count
$w.Write([UInt16]0)                  # reserved
$w.Write([UInt16]1)                  # type = icon
$w.Write([UInt16]$count)             # count

# Header (6) + per-entry header (16 * count)
$dataOffset = 6 + (16 * $count)
foreach ($s in $sizes) {
    $byteSize = if ($s -ge 256) { 0 } else { $s }
    $w.Write([Byte]$byteSize)        # width  (0 = 256)
    $w.Write([Byte]$byteSize)        # height (0 = 256)
    $w.Write([Byte]0)                # color count
    $w.Write([Byte]0)                # reserved
    $w.Write([UInt16]1)              # planes
    $w.Write([UInt16]32)             # bpp
    $w.Write([UInt32]$pngs[$s].Length)
    $w.Write([UInt32]$dataOffset)
    $dataOffset += $pngs[$s].Length
}
foreach ($s in $sizes) {
    $w.Write($pngs[$s])
}
$w.Flush()
[System.IO.File]::WriteAllBytes($out, $ico.ToArray())

$total = (Get-Item $out).Length
Write-Host "Wrote $out ($total bytes, $count sizes)" -ForegroundColor Green
