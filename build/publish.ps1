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

Write-Host ""
Write-Host "Single-file publish OK" -ForegroundColor Green
Write-Host "  $($exe.FullName)"
Write-Host "  $sizeMb MB"
