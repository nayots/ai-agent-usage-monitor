#requires -Version 5.1
# Generates src/AiUsageMonitor.App/Assets/app.ico. Re-runnable and deterministic: the .ico is
# committed, so this script exists to make the asset reproducible, not to run during a build.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$outPath  = Join-Path $repoRoot 'src\AiUsageMonitor.App\Assets\app.ico'
$outDir   = Split-Path -Parent $outPath
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

$sizes = @(16, 20, 24, 32, 48, 64, 128, 256)
$frames = New-Object System.Collections.Generic.List[byte[]]

foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.Clear([System.Drawing.Color]::Transparent)

    # The plate is filled with antialiasing OFF. GDI+ antialiases the outer edge of a rectangle
    # drawn flush to the bitmap bounds, which left the top row and left column at 50% alpha and
    # the corner pixel at 25% - one sixteenth of the 16 px frame, reading as a notch against a
    # dark taskbar. An axis-aligned rectangle gains nothing from antialiasing anyway. It goes
    # back on for the bars, whose fill widths are fractional and do benefit.

    # Palette: nayots navy-bg #04060D and ice-accent #99D1FF, per that project's BRAND.md. The
    # motif stays the app's own three bars rather than becoming the nayots monogram - the bars are
    # what this product is, and an icon that is purely the maker's mark would be identical across
    # every application the maker ships. Only the colours are borrowed, so no brand mark is
    # restyled and no brand rule is engaged. Previously #818CF8 on #18181B, an indigo that matched
    # neither the brand nor the application's own #4CC2FF accent.
    $s = $size / 16.0
    $plate = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 4, 6, 13))
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
    $g.FillRectangle($plate, 0, 0, $size, $size)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

    # Three bars at descending fill, the widget's own motif at icon scale. The track is the ice
    # accent at low alpha rather than a hardcoded grey, so it stays a lift from whatever the plate
    # is - the same reasoning TrayGlyphRenderer.TrackColor documents for the live tray glyph.
    $track = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0x44, 153, 209, 255))
    $fill  = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 153, 209, 255))
    $barHeight = [Math]::Max(1.0, 2.0 * $s)
    $left = 3.0 * $s
    $width = $size - (6.0 * $s)
    $top = 4.0 * $s
    # Cast to [float] explicitly. FillRectangle has an int and a float overload, and handing
    # PowerShell doubles makes the binder pick between them by coercion.
    foreach ($pct in @(0.85, 0.55, 0.3)) {
        $g.FillRectangle($track, [float]$left, [float]$top, [float]$width, [float]$barHeight)
        $g.FillRectangle($fill,  [float]$left, [float]$top, [float]($width * $pct), [float]$barHeight)
        $top = $top + $barHeight + (1.5 * $s)
    }

    $plate.Dispose(); $track.Dispose(); $fill.Dispose(); $g.Dispose()

    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames.Add($stream.ToArray())
    $stream.Dispose(); $bitmap.Dispose()
}

# ICO container: 6-byte header, then one 16-byte directory entry per frame, then the frame bytes.
$out = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($out)
$writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$sizes.Count)

$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = $sizes[$i]
    # 256 is written as 0: the field is one byte.
    $writer.Write([byte]($(if ($dim -ge 256) { 0 } else { $dim })))
    $writer.Write([byte]($(if ($dim -ge 256) { 0 } else { $dim })))
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([UInt16]1); $writer.Write([UInt16]32)
    $writer.Write([UInt32]$frames[$i].Length)
    $writer.Write([UInt32]$offset)
    $offset = $offset + $frames[$i].Length
}
foreach ($frame in $frames) { $writer.Write($frame) }

$writer.Flush()
[System.IO.File]::WriteAllBytes($outPath, $out.ToArray())
$writer.Dispose(); $out.Dispose()

Write-Host "Wrote $outPath ($($sizes.Count) frames)"
