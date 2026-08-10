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

$publishDir = Join-Path $repoRoot "src\AiUsageMonitor.App\bin\$Configuration\net10.0-windows\win-x64\publish"
$executables = @(Get-ChildItem -Path $publishDir -Filter '*.exe')

if ($executables.Count -ne 1) {
    throw "Expected exactly one .exe in $publishDir, found $($executables.Count)."
}

$exe = $executables[0]
$sizeMb = [math]::Round($exe.Length / 1MB, 1)
Write-Host ""
Write-Host "Single-file publish OK" -ForegroundColor Green
Write-Host "  $($exe.FullName)"
Write-Host "  $sizeMb MB"
