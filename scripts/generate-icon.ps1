# Generates src/Jaminator/Jaminator.ico from scratch.
# Run once when you want to regenerate the brand mark; the result is committed.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$out = Join-Path $repoRoot 'src\Jaminator\Jaminator.ico'

# Single 256x256 PNG embedded in an ICO container — modern Windows scales it
# down to 16/32/48 cleanly.
$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

# Rounded-square background with a purple-to-amber diagonal gradient.
# Inspired loosely by Jam Coding's brand palette.
$rect = New-Object System.Drawing.Rectangle 8, 8, ($size - 16), ($size - 16)
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$radius = 56
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

# Subtle inner highlight at the top
$highlight = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $rect,
    [System.Drawing.Color]::FromArgb(80, 255, 255, 255),
    [System.Drawing.Color]::FromArgb(0, 255, 255, 255),
    90)
$g.FillPath($highlight, $path)

# White "J" - bold, centered, slightly raised from baseline
$font = New-Object System.Drawing.Font 'Segoe UI', 168, ([System.Drawing.FontStyle]::Bold)
$textBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
$sf = New-Object System.Drawing.StringFormat
$sf.Alignment = [System.Drawing.StringAlignment]::Center
$sf.LineAlignment = [System.Drawing.StringAlignment]::Center
$textRect = New-Object System.Drawing.RectangleF 0, -8, $size, $size
$g.DrawString('J', $font, $textBrush, $textRect, $sf)

$g.Dispose()
$brush.Dispose()
$highlight.Dispose()
$path.Dispose()
$font.Dispose()
$textBrush.Dispose()

# PNG bytes
$ms = New-Object System.IO.MemoryStream
$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$pngBytes = $ms.ToArray()
$bmp.Dispose()
$ms.Dispose()

# Wrap in ICO header (PNG-in-ICO is supported on Vista+).
$ico = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ico
$w.Write([UInt16]0)                                  # reserved
$w.Write([UInt16]1)                                  # type = icon
$w.Write([UInt16]1)                                  # count = 1
$w.Write([Byte]0)                                    # width  (0 = 256)
$w.Write([Byte]0)                                    # height (0 = 256)
$w.Write([Byte]0)                                    # color count
$w.Write([Byte]0)                                    # reserved
$w.Write([UInt16]1)                                  # planes
$w.Write([UInt16]32)                                 # bpp
$w.Write([UInt32]$pngBytes.Length)                   # image size
$w.Write([UInt32]22)                                 # offset = header (6) + entry (16)
$w.Write($pngBytes)
$w.Flush()

[System.IO.File]::WriteAllBytes($out, $ico.ToArray())
Write-Host "Wrote $out ($($pngBytes.Length) bytes PNG)" -ForegroundColor Green
