# Generates Assets\app.ico for Sentinel X.
# Draws a shield outline (Sentinel X's dark/accent-blue theme colors) with a monitoring
# "pulse" line through the middle, and packs 16/32/48/256 px PNG frames into a single .ico
# container. ASCII-only, PowerShell 5.1 compatible.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$outDir = Join-Path $PSScriptRoot '..\src\SentinelX.App\Assets'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$icoPath = Join-Path $outDir 'app.ico'

function New-FramePng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    $bg     = [System.Drawing.Color]::FromArgb(255, 22, 27, 34)     # BgPanelBrush #161B22
    $accent = [System.Drawing.Color]::FromArgb(255, 47, 129, 247)   # AccentBrush  #2F81F7
    $pulse  = [System.Drawing.Color]::FromArgb(255, 63, 185, 80)    # AccentGreenBrush #3FB950

    $m = $size * 0.08
    $w = $size - 2 * $m

    # Shield outline: flat top, rounded shoulders, converging to a point at the bottom.
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $pts = @(
        [System.Drawing.PointF]::new($m, $m),
        [System.Drawing.PointF]::new($m + $w, $m),
        [System.Drawing.PointF]::new($m + $w, $m + $w * 0.55),
        [System.Drawing.PointF]::new($m + $w * 0.5, $m + $w),
        [System.Drawing.PointF]::new($m, $m + $w * 0.55)
    )
    $path.AddPolygon($pts)

    $fillBrush = New-Object System.Drawing.SolidBrush($bg)
    $g.FillPath($fillBrush, $path)

    $borderPen = New-Object System.Drawing.Pen($accent, [Math]::Max(1.5, $size / 12.0))
    $borderPen.LineJoin = 'Round'
    $g.DrawPath($borderPen, $path)

    # Monitoring "pulse" line through the middle (ECG-style zigzag).
    $midY = $m + $w * 0.5
    $pulsePen = New-Object System.Drawing.Pen($pulse, [Math]::Max(1.2, $size / 16.0))
    $pulsePen.StartCap = 'Round'; $pulsePen.EndCap = 'Round'; $pulsePen.LineJoin = 'Round'
    $x0 = $m + $w * 0.16
    $x1 = $m + $w * 0.36
    $x2 = $m + $w * 0.46
    $x3 = $m + $w * 0.58
    $x4 = $m + $w * 0.68
    $x5 = $m + $w * 0.84
    $pulsePts = @(
        [System.Drawing.PointF]::new($x0, $midY),
        [System.Drawing.PointF]::new($x1, $midY),
        [System.Drawing.PointF]::new($x2, $midY - $w * 0.22),
        [System.Drawing.PointF]::new($x3, $midY + $w * 0.26),
        [System.Drawing.PointF]::new($x4, $midY),
        [System.Drawing.PointF]::new($x5, $midY)
    )
    $g.DrawLines($pulsePen, $pulsePts)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = @(16, 32, 48, 256)
$frames = @()
foreach ($s in $sizes) { $frames += ,(New-FramePng $s) }

# ---- Pack ICO container (PNG frames, valid on Windows Vista+) ----
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([UInt16]0)                # reserved
$bw.Write([UInt16]1)                # type: icon
$bw.Write([UInt16]$sizes.Count)     # image count

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $len = $frames[$i].Length
    $bw.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))  # width  (0 = 256)
    $bw.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))  # height
    $bw.Write([Byte]0)              # palette colors
    $bw.Write([Byte]0)              # reserved
    $bw.Write([UInt16]1)            # planes
    $bw.Write([UInt16]32)           # bpp
    $bw.Write([UInt32]$len)         # data size
    $bw.Write([UInt32]$offset)      # data offset
    $offset += $len
}
foreach ($f in $frames) { $bw.Write($f) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())
$bw.Dispose(); $ms.Dispose()

Write-Output ("Icon written: {0} ({1} bytes)" -f $icoPath, (Get-Item $icoPath).Length)
