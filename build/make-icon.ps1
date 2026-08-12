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
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = $size / 16.0
    $plate = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 24, 24, 27))
    $g.FillRectangle($plate, 0, 0, $size, $size)

    # Three bars at descending fill, the widget's own motif at icon scale.
    $track = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 63, 63, 70))
    $fill  = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 129, 140, 248))
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
