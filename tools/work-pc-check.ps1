<#
  TEMPORARY - remove before merging this branch.

  Read-only check of how Claude Code is signed in on this machine, for verifying the
  Console-account usage estimate. It prints key NAMES, file NAMES and yes/no answers only.

  It never prints a token, an API key, an email address, a name or any transcript content.
  It makes no network request and writes nothing except its own output file in %TEMP%.

  Run from the repository root:
      powershell -ExecutionPolicy Bypass -File tools/work-pc-check.ps1
  then paste back %TEMP%\work-pc-check-output.txt (the path is printed at the end).
#>

$ErrorActionPreference = 'Stop'
$out = New-Object System.Collections.Generic.List[string]
function Say([string]$line) { $out.Add($line); Write-Host $line }

Add-Type -AssemblyName System.Web.Extensions
function Read-Json([string]$path) {
    $serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
    $serializer.MaxJsonLength = [int]::MaxValue
    return $serializer.DeserializeObject([System.IO.File]::ReadAllText($path))
}

function Has-NonEmptyString($dict, [string]$key) {
    return ($dict -is [System.Collections.IDictionary]) -and $dict.ContainsKey($key) -and ($dict[$key] -is [string]) -and ($dict[$key].Length -gt 0)
}

Say "work-pc-check - $([DateTime]::UtcNow.ToString('u'))"

# --- Claude Code itself
$claude = Get-Command claude -ErrorAction SilentlyContinue
Say ("claude on PATH: {0}" -f [bool]$claude)
if ($claude) {
    try { Say ("claude --version: {0}" -f ((& claude --version 2>$null) -join ' ')) } catch { Say "claude --version: failed" }
}

# --- Environment (names only; values are never printed)
foreach ($name in 'CLAUDE_CODE_USE_BEDROCK', 'CLAUDE_CODE_USE_VERTEX', 'CLAUDE_CODE_USE_FOUNDRY',
                  'ANTHROPIC_AUTH_TOKEN', 'ANTHROPIC_API_KEY', 'ANTHROPIC_PROFILE',
                  'CLAUDE_CODE_OAUTH_TOKEN', 'CLAUDE_CONFIG_DIR', 'ANTHROPIC_CONFIG_DIR') {
    Say ("env {0} set: {1}" -f $name, (-not [string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($name))))
}

$configDir = [Environment]::GetEnvironmentVariable('CLAUDE_CONFIG_DIR')
if ([string]::IsNullOrEmpty($configDir)) {
    $claudeDir = Join-Path $env:USERPROFILE '.claude'
    $globalConfig = Join-Path $env:USERPROFILE '.claude.json'
} else {
    $claudeDir = $configDir
    $globalConfig = Join-Path $configDir '.claude.json'
}

# --- Credentials file (key names only)
$credentials = Join-Path $claudeDir '.credentials.json'
Say ("credentials file exists: {0}" -f (Test-Path $credentials))
if (Test-Path $credentials) {
    try {
        $c = Read-Json $credentials
        Say ("  top-level key names: {0}" -f (@($c.Keys) -join ', '))
        Say ("  claudeAiOauth present (subscription): {0}" -f $c.ContainsKey('claudeAiOauth'))
        Say ("  primaryApiKey present: {0}" -f (Has-NonEmptyString $c 'primaryApiKey'))
    } catch { Say ("  could not parse ({0})" -f $_.Exception.GetType().Name) }
}

# --- Global config (key names and the non-secret billing type only)
Say ("global config exists: {0}" -f (Test-Path $globalConfig))
if (Test-Path $globalConfig) {
    try {
        $g = Read-Json $globalConfig
        Say ("  primaryApiKey present: {0}" -f (Has-NonEmptyString $g 'primaryApiKey'))
        if ($g.ContainsKey('oauthAccount') -and $g['oauthAccount'] -is [System.Collections.IDictionary]) {
            $billing = $g['oauthAccount']['billingType']
            Say ("  oauthAccount.billingType: {0}" -f $(if ($billing -is [string]) { $billing } else { 'absent' }))
        } else {
            Say '  oauthAccount: absent'
        }
    } catch { Say ("  could not parse ({0})" -f $_.Exception.GetType().Name) }
}

# --- settings.json
$settings = Join-Path $claudeDir 'settings.json'
if (Test-Path $settings) {
    try {
        $s = Read-Json $settings
        Say ("settings.json apiKeyHelper present: {0}" -f (Has-NonEmptyString $s 'apiKeyHelper'))
        Say ("settings.json forceLoginMethod: {0}" -f $(if (Has-NonEmptyString $s 'forceLoginMethod') { $s['forceLoginMethod'] } else { 'absent' }))
    } catch { Say ("settings.json could not be parsed ({0})" -f $_.Exception.GetType().Name) }
} else {
    Say 'settings.json: absent'
}

# --- Anthropic profiles (the keyless Console sign-in); file names only, contents never opened
$anthropicDir = [Environment]::GetEnvironmentVariable('ANTHROPIC_CONFIG_DIR')
if ([string]::IsNullOrEmpty($anthropicDir)) { $anthropicDir = Join-Path $env:APPDATA 'Anthropic' }
Say ("Anthropic config dir exists: {0}" -f (Test-Path $anthropicDir))
if (Test-Path $anthropicDir) {
    Say ("  entries: {0}" -f (@(Get-ChildItem $anthropicDir | ForEach-Object { $_.Name }) -join ', '))
    $configs = Join-Path $anthropicDir 'configs'
    if (Test-Path $configs) {
        Say ("  configs/*.json: {0}" -f (@(Get-ChildItem $configs -Filter *.json | ForEach-Object { $_.Name }) -join ', '))
    }
}

# --- Transcripts (counts and sizes only)
$projects = Join-Path $claudeDir 'projects'
if (Test-Path $projects) {
    $monthStart = (Get-Date -Day 1).Date
    $files = @(Get-ChildItem $projects -Recurse -Filter *.jsonl -ErrorAction SilentlyContinue)
    $recent = @($files | Where-Object { $_.LastWriteTime -ge $monthStart })
    Say ("transcripts: {0} files, {1} touched this month ({2:N1} MB)" -f $files.Count, $recent.Count, (($recent | Measure-Object Length -Sum).Sum / 1MB))
} else {
    Say 'transcripts: no projects folder'
}

# Outside the repository, so the output can never be committed by accident.
$outFile = Join-Path $env:TEMP 'work-pc-check-output.txt'
[System.IO.File]::WriteAllLines($outFile, $out)
Write-Host ''
Write-Host "Saved to $outFile"
