#Requires -Version 5.1
<#
    Publishes the widget as a single self-contained executable.

    Verified 2026-08-11 on SDK 10.0.301: produces one ~64.7 MB .exe.
    Do NOT add -p:PublishTrimmed=true. WPF hard-errors on it (NETSDK1168).
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project  = Join-Path $repoRoot 'src\AiUsageMonitor.App\AiUsageMonitor.App.csproj'
$publishDir = Join-Path $repoRoot "src\AiUsageMonitor.App\bin\$Configuration\net10.0-windows\win-x64\publish"

# Remove any stale publish output first. Without this, a leftover artifact from an earlier,
# correctly self-contained run can satisfy the one-.exe check below even if this run's
# dotnet publish silently produced nothing (or something the checks below never actually saw).
if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force
}

dotnet publish $project `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$executables = @(Get-ChildItem -Path $publishDir -Filter '*.exe')

if ($executables.Count -ne 1) {
    throw "Expected exactly one .exe in $publishDir, found $($executables.Count)."
}

$exe = $executables[0]
$sizeMb = [math]::Round($exe.Length / 1MB, 1)

# A self-contained single-file WPF publish is tens of MB (~64.7 MB verified 2026-08-11 on SDK
# 10.0.301). If --self-contained is ever dropped from the dotnet publish arguments above, the
# result is instead a ~172 KB framework-dependent .exe: it runs fine here, on the author's
# machine, and fails on any machine without the .NET 10 Desktop Runtime preinstalled - exactly
# the release failure this application must never ship, since the release artifact has to run
# for a user who is not the author, on a machine that is not the author's. Local testing alone
# cannot catch this, because the framework-dependent build still runs on this machine.
$minimumSizeMb = 50
if ($sizeMb -lt $minimumSizeMb) {
    $message = "Published .exe is only $sizeMb MB (expected at least $minimumSizeMb MB). " +
        "This almost certainly means --self-contained was dropped, producing a " +
        "framework-dependent build that requires the .NET 10 Desktop Runtime preinstalled " +
        "on the target machine. File: $($exe.FullName)"
    throw $message
}

# --- Release asset staging -------------------------------------------------------------
# The published file is named after the assembly (AiUsageMonitor.App.exe), but the product
# is called Quota Monitor everywhere the user can see it. A download sitting in a Downloads
# folder under the assembly name is not recognisably the thing they installed, so the
# release asset carries the product name and its version instead.
#
# The version is read from the binary that was just built rather than from
# Directory.Build.props, so the name always describes what actually shipped.
$productVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe.FullName).ProductVersion
if ([string]::IsNullOrWhiteSpace($productVersion)) {
    throw "The published executable reports no product version: $($exe.FullName)"
}

# Strip the +<commit> build metadata the SDK appends. This mirrors exactly what the
# diagnostics screen shows for "Application version"
# (EnvironmentReport.CaptureApplicationVersion), so the number in a pasted bug report and
# the number in the downloaded file name are the same number.
$version = ($productVersion -split '\+', 2)[0].Trim()

$artifactsDir = Join-Path $repoRoot 'artifacts'
if (Test-Path $artifactsDir) {
    Remove-Item -Path $artifactsDir -Recurse -Force
}

New-Item -ItemType Directory -Path $artifactsDir | Out-Null

$assetName = "QuotaMonitor-v$version-win-x64.exe"
$assetPath = Join-Path $artifactsDir $assetName
Copy-Item -Path $exe.FullName -Destination $assetPath

# sha256sum format: lower-case hash, two spaces, bare file name, LF terminator - so
# `sha256sum -c` and the README's Get-FileHash instructions both verify it as-is.
#
# Written byte-for-byte with WriteAllText rather than Set-Content, because Set-Content
# terminates the line with CRLF. sha256sum then reads the trailing CR as part of the file
# name and reports "No such file or directory" for a checksum that is in fact correct -
# which is worse than no checksum file, since it looks like tampering. WriteAllText also
# emits no BOM, and a BOM would make the first line unparseable for the same tool.
$hash = (Get-FileHash -Path $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText("$assetPath.sha256", "$hash  $assetName`n")

# A zipped copy of the same two files, shipped alongside them rather than instead of them.
#
# Managed Windows machines frequently sit behind a web filter that refuses a bare .exe
# download by extension or MIME type, so the direct download is simply unavailable to some
# users. The zip is not a way around a security control - a filter that inspects archive
# contents blocks this too, and it is meant to - it is the ordinary distribution shape that
# such environments do allow through, the same one every other Windows tool ships.
#
# It also keeps the binary and its checksum together: a user who downloads one file cannot
# end up with the .exe and no way to check it. The zip's own per-entry CRC32 detects a
# truncated or corrupted download on extraction, which is why there is deliberately no
# .zip.sha256 - it would only verify a container nobody runs, while the checksum that
# matters travels inside, next to the binary it describes.
#
# Built with the ZipFile API rather than Compress-Archive: this behaves identically under
# Windows PowerShell 5.1 (used locally) and pwsh 7 (used on the runner), whereas 5.1's
# Compress-Archive is markedly slower on a file this size.
Add-Type -AssemblyName System.IO.Compression.FileSystem

$zipName = [System.IO.Path]::ChangeExtension($assetName, '.zip')
$zipPath = Join-Path $artifactsDir $zipName

$archive = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')
try {
    foreach ($entry in @($assetPath, "$assetPath.sha256")) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive,
            $entry,
            [System.IO.Path]::GetFileName($entry),
            [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally {
    $archive.Dispose()
}

$zipSizeMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)

Write-Host ""
Write-Host "Single-file publish OK" -ForegroundColor Green
Write-Host "  $($exe.FullName)"
Write-Host "  $sizeMb MB"
Write-Host ""
Write-Host "Release assets staged" -ForegroundColor Green
Write-Host "  $assetPath"
Write-Host "  $assetPath.sha256"
Write-Host "  $zipPath ($zipSizeMb MB)"
Write-Host "  sha256 $hash"
