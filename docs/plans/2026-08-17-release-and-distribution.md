# Release and Distribution Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make this application obtainable — a versioned, checksummed, self-contained `.exe` produced by CI from a tag, with the documentation, licence and privacy statement a stranger needs before they run an unsigned binary.

**Architecture:** No application code changes. The version becomes a single declared property that CI verifies a tag against; `build/publish.ps1` gains release-asset staging; two GitHub Actions workflows are added; four documents are written. Nothing new is referenced, imported, or installed.

**Tech Stack:** .NET 10 SDK, MSBuild properties, PowerShell 5.1/7, GitHub Actions on `windows-latest`, the `gh` CLI preinstalled on the runner.

**Spec:** `docs/specs/2026-08-17-release-and-distribution.md`

---

## Global Constraints

Copied from the spec and from CLAUDE.md. Every task's requirements implicitly include this section.

- **No new `PackageReference` in any project.** Not for versioning, not for release automation. The spec rejects MinVer and Nerdbank.GitVersioning by name.
- **No third-party GitHub Action** beyond `actions/checkout` and `actions/setup-dotnet`. Releases are created with the `gh` CLI, which is preinstalled on GitHub-hosted runners.
- **`dotnet build` must stay clean — warnings are errors** (`TreatWarningsAsErrors` in `Directory.Build.props`).
- **Windows only.** `net10.0-windows`, WPF, `RuntimeIdentifier=win-x64`. A Linux runner cannot build this; do not add one.
- **Never drop `--self-contained`** from `build/publish.ps1`. The existing 50 MB floor check exists to catch exactly that and must survive every edit to that file.
- **The product is called "Quota Monitor"** in everything a user sees (it is what `TrayIcon` and the diagnostics bundle already say). The assembly is `AiUsageMonitor.App`, the repository is `ai-agent-usage-monitor`. Do not rename the assembly, solution, namespaces or repository.
  > **Superseded 2026-08-18.** The product name is now **"AI Usage Monitor"**, and the release asset is `AiUsageMonitor-v<version>-win-x64.{exe,zip}`. The constraint that survives is the second sentence: the assembly, solution, namespaces and repository were *not* renamed and must not be. Releases published before this date keep their `QuotaMonitor-*` asset names, so a link to a specific older asset still resolves.
- **No credential may ever be written to a file this plan creates.** README, checklist, workflow logs — none of them. The application never logs, persists, caches, displays or copies a token, and the documentation describing that must not become the first exception.
- **Do not make the repository public.** Publication is intended, and soon — but *after* this plan, not during it. A public repository with no `LICENSE` grants no rights to anyone reading it, and a public repository with no `README` leaves the unofficial Claude mechanism and the privacy boundaries undocumented at exactly the moment strangers can read the code. Task 7 produces the checklist; the owner flips the switch afterwards.
- **Do not create a tag or a GitHub release.** Task 6 delivers the workflow; cutting `v0.1.0` is an owner-performed action documented at the end of this plan.
- **PowerShell 5.1 is the local shell**: no `&&`, no ternary, no `??`. `Set-Content -Encoding utf8` writes a BOM under 5.1 — this matters in Task 4.
- **Never round-trip a source file through `Get-Content`/`Set-Content`.** Under 5.1 that reads BOM-less UTF-8 as ANSI and silently mangles every `§` and en dash in the repository. Use the editor.

---

## File Structure

| File | Created / Modified | Responsibility |
|---|---|---|
| `Directory.Build.props` | Modify | The single declaration of the version, product name and copyright |
| `global.json` | Create | Pins the SDK feature band so local and CI agree |
| `src/AiUsageMonitor.App/AiUsageMonitor.App.csproj` | Modify | `AssemblyTitle`, so Explorer describes the download as "Quota Monitor" |
| `LICENSE` | Create | MIT grant |
| `README.md` | Create | The only document a stranger reads before running an unsigned binary |
| `build/publish.ps1` | Modify | Stages the release asset: product-named, version-stamped, checksummed |
| `.github/workflows/ci.yml` | Create | Build and test on push to `main` and on pull requests |
| `.github/workflows/release.yml` | Create | Tag-triggered: verify, test, publish, release |
| `docs/pre-publication-checklist.md` | Create | What must be checked before the repository is ever made public |
| `CLAUDE.md` | Modify | The release procedure, in the Commands section |

---

### Task 1: The version becomes a declared fact

**Files:**
- Modify: `Directory.Build.props`
- Create: `global.json`
- Modify: `src/AiUsageMonitor.App/AiUsageMonitor.App.csproj`

**Interfaces:**
- Consumes: nothing.
- Produces: an MSBuild property `VersionPrefix` = `0.1.0`, readable from `Directory.Build.props` by XPath `//VersionPrefix`. Task 4 and Task 6 both depend on that element existing with exactly that name.

**Why there is no unit test for this task.** `EnvironmentReport.CaptureApplicationVersion()` reads `Assembly.GetEntryAssembly()`. Under `dotnet test` the entry assembly is the test host or the test assembly — **never `AiUsageMonitor.App`** — so no unit test can assert the shipped application's version. That is also why the existing `EnvironmentReportTests` only asserts weak properties (non-empty, no `+`). Do not "strengthen" it into an assertion about `0.1.0`; it will pass or fail for reasons unrelated to this application. Verification here is an inspection of the built binary, in Step 3.

- [ ] **Step 1: Add the version properties**

Replace the whole of `Directory.Build.props` with:

```xml
<Project>

  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <!--
    The single declaration of this application's version.

    The release workflow verifies that a v* tag matches this value and fails before it
    builds anything when it does not. It never injects a version of its own, so a
    checkout can always name the version it is, and a diagnostics bundle pasted into a
    bug report provably names the release the user downloaded.

    0.x is deliberate and is not modesty about the code: the Claude Code mechanism is
    unofficial and undocumented and may break without notice, so a 1.0 would promise a
    stability that depends on an endpoint this project does not control.
    See docs/specs/2026-08-17-release-and-distribution.md, D1 and D2.
  -->
  <PropertyGroup>
    <VersionPrefix>0.1.0</VersionPrefix>
    <Product>Quota Monitor</Product>
    <Copyright>Copyright (c) 2026 Stoyan Grigorov</Copyright>
  </PropertyGroup>

</Project>
```

- [ ] **Step 2: Pin the SDK**

Create `global.json`:

```json
{
  "sdk": {
    "version": "10.0.301",
    "rollForward": "latestFeature"
  }
}
```

`latestFeature` accepts any 10.0.3xx or newer feature band, so an SDK patch update does not break the build, while an older installed SDK (7.0.202, 8.0.403 and 9.0.311 are all present on the author's machine) can never be selected by accident.

Then add the assembly title to `src/AiUsageMonitor.App/AiUsageMonitor.App.csproj`, inside the existing first `<PropertyGroup>`, immediately after the `<AssemblyName>` line:

```xml
    <AssemblyTitle>Quota Monitor</AssemblyTitle>
```

`AssemblyTitle` becomes the file's **Description** in Explorer and in the Windows "unknown publisher" dialog. On an unsigned download that dialog is the only identity the user gets, so it must say the product name rather than the assembly name.

- [ ] **Step 3: Build and inspect the binary**

Run:

```powershell
dotnet build --configuration Release
```

Expected: `Build succeeded`, `0 Warning(s)`, `0 Error(s)`.

Then:

```powershell
$exe = Resolve-Path 'src/AiUsageMonitor.App/bin/Release/net10.0-windows/win-x64/AiUsageMonitor.App.exe'
[System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe) | Format-List ProductName, ProductVersion, FileVersion, FileDescription
```

Expected:

```text
ProductName     : Quota Monitor
ProductVersion  : 0.1.0+<40 hex characters>
FileVersion     : 0.1.0.0
FileDescription : Quota Monitor
```

The `+<sha>` suffix is the SDK's source-revision metadata. It is expected, and both `EnvironmentReport.CaptureApplicationVersion()` and Task 4 strip it.

- [ ] **Step 4: Run the tests**

Run: `dotnet test`
Expected: PASS, 725 tests, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add Directory.Build.props global.json src/AiUsageMonitor.App/AiUsageMonitor.App.csproj
git commit -m "build: declare the application version and pin the SDK feature band"
```

---

### Task 2: Licence

**Files:**
- Create: `LICENSE`

**Interfaces:**
- Consumes: nothing.
- Produces: a file GitHub's licence detector recognises, so the repository page shows "MIT licence".

**Owner decision.** Spec D8 records that MIT is a recommendation the owner should actively confirm, not accept by default — it is a legal grant, effectively irreversible for any copy already distributed. Implement MIT; if the owner has said otherwise, that instruction wins over this task.

- [ ] **Step 1: Write the licence**

Create `LICENSE` with the standard MIT text, verbatim, with the year `2026` and the copyright holder `Stoyan Grigorov`:

```text
MIT License

Copyright (c) 2026 Stoyan Grigorov

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

The copyright line must match the `<Copyright>` property set in Task 1. If one changes, change both.

- [ ] **Step 2: Verify**

Run: `git check-ignore -v LICENSE`
Expected: **no output and exit code 1** — meaning the file is *not* ignored. (`.gitignore` contains no `LICENSE` rule, but the file has no extension and the ignore file is long; confirm rather than assume.)

- [ ] **Step 3: Commit**

```bash
git add LICENSE
git commit -m "docs: add the MIT licence"
```

---

### Task 3: README

**Files:**
- Create: `README.md`

**Interfaces:**
- Consumes: the version from Task 1 (`0.1.0`), the asset name from Task 4 (`QuotaMonitor-v0.1.0-win-x64.exe`).
- Produces: nothing consumed by later tasks.

> **This task is written by Claude, not by a delegated worker.** The README makes
> claims about the unofficial mechanism tier, about first-party network calls, and about
> what the application never does. Those claims are load-bearing — a user decides whether
> to run an unsigned binary on the strength of them — and getting one subtly wrong is
> worse than having no README. A delegated worker should skip this task and say so.

**Required content.** Every section below must be present, and every claim in it must be true of the code as it stands, not aspirational.

1. **Name and one line.** "Quota Monitor — a Windows desktop widget showing live quota for the AI coding tools installed on your machine." Note the repository name differs from the product name.
2. **Status.** Pre-1.0. Windows 11 x64. Requires no administrator rights and installs nothing.
3. **Providers**, as a table: provider, what it reads, **tier**, and the version it was verified against.

   | Provider | Mechanism | Tier | Verified against |
   |---|---|---|---|
   | Codex | `codex app-server` JSON-RPC `account/rateLimits/read` | **Official** | codex-cli 0.144.6 |
   | Claude Code | The provider's own usage endpoint, using the OAuth token already stored on your machine | **Unofficial** | Claude Code 2.1.226 |

   The Claude row must state in prose that the mechanism is **undocumented and may stop working without notice**, and that when it does the application shows an error rather than a stale or invented number.
4. **Install.** Download the `.exe` from Releases; there is no installer; run it. Then the SmartScreen paragraph: the binary is **unsigned**, Windows will show "Windows protected your PC", the user must click *More info* → *Run anyway*, and the published `SHA256` lets them verify the download first — with the exact `Get-FileHash` command.
5. **Where it puts things.** `%APPDATA%\AiUsageMonitor\settings.json` and `%LOCALAPPDATA%\AiUsageMonitor\logs`. Nothing machine-wide, nothing in the registry except the optional per-user *Start with Windows* entry under `HKCU`.
6. **Using it.** The widget, the notification-area icon and its menu, close-means-hide, pinning, compact and mini modes, quota notifications.
7. **Settings.** A short list of what is configurable, including the 60-second minimum refresh interval and why it exists (asking a provider too often gets the application throttled).
8. **Diagnostics and reporting a problem.** Settings → Diagnostics → **Copy**, which produces a redacted bundle: the user folder and user name are masked, and credentials are never read into that screen at all. Ask for that bundle in an issue.
9. **Privacy — what this application never does.** State as a list, and only what is true: no website scraping; no browser automation, cookies or browser profiles; no telemetry or analytics; no third-party transmission; the only network calls are to each provider's own host over TLS; the Claude token is read into memory, used for one request header, and never logged, persisted, cached, displayed or copied; the application never refreshes or rewrites a credential; provider configuration files are never modified.
10. **Limitations.** Windows only; x64 (runs under emulation on Windows on ARM); no historical data — every value is instantaneous; the Claude mechanism is unofficial; quota windows are whatever the provider reports, so their number and names can change without an update to this application.
11. **Building from source.** `dotnet build`, `dotnet test`, `powershell -File build/publish.ps1`, and the note that the release artifact is always the self-contained build.
12. **Licence.** MIT, linking `LICENSE`.

**What the README must not contain:** a screenshot (none exists, and one taken from the author's machine would show the author's real quota percentages), a promise of support or a release cadence, or any instruction that involves editing a provider's own configuration.

- [ ] **Step 1: Write `README.md` covering all twelve sections above**

- [ ] **Step 2: Verify every factual claim against the code**

For each claim, name the file that makes it true. Specifically re-read before asserting:
`ClaudeOAuthUsageProbe.cs` (host, header, redirects, token handling), `CodexProbe.cs` (mechanism and tier), `DiagnosticRedaction.cs` (what "redacted" actually covers), `AppSettings.cs` (`MinimumRefreshSeconds`), `StartupRegistration.cs` (the `HKCU` entry), `AppSettingsStore.cs` and `RollingFileLoggerProvider.cs` (the two paths).

- [ ] **Step 3: Check for mojibake**

Run: `git diff --stat` then open `README.md` in the editor and confirm no `Â§`, `В§` or `â€"` sequences are present. (See the PowerShell round-trip trap in the Global Constraints.)

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "docs: add the README"
```

---

### Task 4: The publish script stages a release asset

**Files:**
- Modify: `build/publish.ps1`

**Interfaces:**
- Consumes: `VersionPrefix` from Task 1, indirectly — it reads the **published binary's** product version rather than the props file, so what it stamps is what actually shipped.
- Produces: `artifacts/QuotaMonitor-v<version>-win-x64.exe` and `artifacts/QuotaMonitor-v<version>-win-x64.exe.sha256`, both consumed by Task 6.

`artifacts/` is already in `.gitignore`. Do not add it again and do not commit its contents.

- [ ] **Step 1: Append the staging block**

In `build/publish.ps1`, keep everything up to and including the existing `$minimumSizeMb` check unchanged. Replace only the final `Write-Host` block at the end of the file with:

```powershell
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

# sha256sum format: lower-case hash, two spaces, bare file name - so `sha256sum -c` and the
# README's Get-FileHash instructions both verify it as-is.
#
# ASCII is deliberate. Set-Content -Encoding utf8 emits a BOM under Windows PowerShell 5.1,
# and a BOM at the start of a checksum file makes the first line unparseable. A hash and a
# file name are ASCII by construction, so nothing is lost.
$hash = (Get-FileHash -Path $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$assetPath.sha256" -Value "$hash  $assetName" -Encoding ASCII

Write-Host ""
Write-Host "Single-file publish OK" -ForegroundColor Green
Write-Host "  $($exe.FullName)"
Write-Host "  $sizeMb MB"
Write-Host ""
Write-Host "Release assets staged" -ForegroundColor Green
Write-Host "  $assetPath"
Write-Host "  $assetPath.sha256"
Write-Host "  sha256 $hash"
```

- [ ] **Step 2: Run it**

Run: `powershell -File build/publish.ps1`

Expected, at the end:

```text
Single-file publish OK
  ...\publish\AiUsageMonitor.App.exe
  64.7 MB

Release assets staged
  ...\artifacts\QuotaMonitor-v0.1.0-win-x64.exe
  ...\artifacts\QuotaMonitor-v0.1.0-win-x64.exe.sha256
  sha256 <64 lower-case hex characters>
```

- [ ] **Step 3: Verify the checksum file round-trips**

```powershell
$asset = Get-ChildItem artifacts -Filter '*.exe'
$recorded = (Get-Content "$($asset.FullName).sha256" -Encoding ASCII) -split '\s+' | Select-Object -First 1
$actual = (Get-FileHash $asset.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
if ($recorded -ne $actual) { throw "Checksum mismatch: recorded $recorded, actual $actual" }
Write-Host "Checksum verified: $actual"
```

Expected: `Checksum verified: <hash>`, no throw.

Then confirm there is no BOM — the first byte must be a hex digit, not `0xEF`:

```powershell
'{0:X2}' -f (Get-Content "$($asset.FullName).sha256" -Encoding Byte -TotalCount 1)
```

Expected: a hex digit's ASCII code (`30`–`39` or `61`–`66`), **never** `EF`.

- [ ] **Step 4: Confirm the self-contained guard still fires**

Do not weaken it. Confirm by reading that `$minimumSizeMb = 50` and its `throw` are still present and still run **before** the staging block.

- [ ] **Step 5: Confirm nothing was staged into git**

Run: `git status --short`
Expected: `build/publish.ps1` modified, and **nothing under `artifacts/`**.

- [ ] **Step 6: Commit**

```bash
git add build/publish.ps1
git commit -m "build: stage a product-named, checksummed release asset"
```

---

### Task 5: Continuous integration

**Files:**
- Create: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: `global.json` from Task 1.
- Produces: nothing consumed by later tasks. Task 6 deliberately repeats the test run rather than depending on this workflow.

- [ ] **Step 1: Write the workflow**

Create `.github/workflows/ci.yml`:

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

permissions:
  contents: read

jobs:
  build:
    name: Build and test
    runs-on: windows-latest

    steps:
      - name: Check out the repository
        uses: actions/checkout@v4

      # global.json pins the feature band, so the runner cannot silently select a
      # different SDK from the one this repository is developed against.
      - name: Set up the .NET SDK
        uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json

      - name: Restore
        run: dotnet restore

      # TreatWarningsAsErrors is set in Directory.Build.props, so this step fails on a
      # warning. That is intentional and is the repository's standing rule.
      - name: Build
        run: dotnet build --configuration Release --no-restore

      - name: Test
        run: dotnet test --configuration Release --no-build
```

`windows-latest` is not a preference: WPF, `net10.0-windows` and `RuntimeIdentifier=win-x64` cannot build anywhere else.

- [ ] **Step 2: Validate the YAML parses**

```powershell
$path = '.github/workflows/ci.yml'
if (-not (Test-Path $path)) { throw "Missing $path" }
Write-Host "Found $path"
Get-Content $path | Select-String -Pattern 'runs-on: windows-latest'
```

Expected: the file exists and the `runs-on` line is found. (There is no YAML linter in this repository and this plan adds no dependency to get one; the real validation is Step 4.)

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: build and test on push and pull request"
```

- [ ] **Step 4: Push and confirm the run is green**

This is the only real verification a workflow file has, and it requires pushing.

```bash
git push origin main
gh run watch --exit-status
```

Expected: the `CI / Build and test` run completes successfully. If `dotnet test` fails only on the runner, do not weaken the workflow — find the environment difference, and if the difference is a provider being absent on the runner, that is a genuine test defect worth fixing (both providers must degrade to `NotInstalled` where absent).

---

### Task 6: Release workflow

**Files:**
- Create: `.github/workflows/release.yml`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: `VersionPrefix` from Task 1 (by XPath `//VersionPrefix`), the `artifacts/` output of Task 4.
- Produces: a GitHub release when a `v*` tag is pushed. **This task does not push a tag.**

- [ ] **Step 1: Write the workflow**

Create `.github/workflows/release.yml`:

```yaml
name: Release

on:
  push:
    tags:
      - 'v*'

permissions:
  contents: write

jobs:
  release:
    name: Publish the release artifact
    runs-on: windows-latest

    steps:
      - name: Check out the repository
        uses: actions/checkout@v4

      # First, before any build work. A mistyped tag then fails in seconds rather than
      # after a multi-minute self-contained publish. CI verifies the tag against the
      # declared version; it never injects a version of its own, so the number shown in
      # Settings > Diagnostics always matches the release the user downloaded.
      - name: Verify the tag matches the declared version
        shell: pwsh
        run: |
          $props = [xml](Get-Content -Raw -Encoding UTF8 'Directory.Build.props')
          $node = $props.SelectSingleNode('//VersionPrefix')
          if ($null -eq $node) {
              throw 'Directory.Build.props declares no <VersionPrefix>.'
          }

          $version = $node.InnerText.Trim()
          $expected = "v$version"
          if ($env:GITHUB_REF_NAME -ne $expected) {
              throw "Tag '$($env:GITHUB_REF_NAME)' does not match the declared version. Expected '$expected'. Either update <VersionPrefix> and retag, or delete the tag."
          }

          Write-Host "Tag and declared version agree: $expected"

      - name: Set up the .NET SDK
        uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json

      # Deliberately repeated here rather than trusting that CI already ran on this
      # commit. A tag can be pushed to a commit CI never saw.
      - name: Test
        run: dotnet test --configuration Release

      - name: Publish the single-file artifact
        shell: pwsh
        run: ./build/publish.ps1 -Configuration Release

      - name: Create the GitHub release
        shell: pwsh
        env:
          GH_TOKEN: ${{ github.token }}
        run: |
          $assets = @(Get-ChildItem -Path 'artifacts' -File)
          if ($assets.Count -ne 2) {
              throw "Expected the .exe and its .sha256 in artifacts/, found $($assets.Count)."
          }

          gh release create $env:GITHUB_REF_NAME @($assets.ForEach({ $_.FullName })) `
            --title $env:GITHUB_REF_NAME `
            --generate-notes `
            --verify-tag

          if ($LASTEXITCODE -ne 0) {
              throw "gh release create failed with exit code $LASTEXITCODE"
          }
```

`gh` is preinstalled on GitHub-hosted runners; `--generate-notes` builds the release notes from the commit log, which is already conventional-commit formatted, so no `CHANGELOG.md` is maintained by hand. `--verify-tag` refuses to create a release for a tag that does not exist.

- [ ] **Step 2: Document the release procedure**

In `CLAUDE.md`, in the `## Commands` section, immediately after the existing fenced PowerShell block, add:

````markdown
Cutting a release (the tag is the trigger — everything else is automatic):

```powershell
# 1. Bump <VersionPrefix> in Directory.Build.props and commit it.
# 2. Tag that commit and push the tag:
git tag v0.1.0
git push origin v0.1.0
```

`.github/workflows/release.yml` verifies the tag against `<VersionPrefix>` **before** it
builds, runs the tests, publishes the self-contained `.exe`, and attaches it with its
`SHA256` to a GitHub release. A tag that disagrees with the declared version fails in
seconds. The artifact is always the self-contained build — never the framework-dependent
one, which needs the .NET 10 Desktop Runtime preinstalled and so fails on exactly the
machines this application must work on.
````

- [ ] **Step 3: Verify the tag check logic locally**

The workflow cannot run without a tag, but its one piece of real logic can be exercised directly. Run:

```powershell
$props = [xml](Get-Content -Raw -Encoding UTF8 'Directory.Build.props')
$version = $props.SelectSingleNode('//VersionPrefix').InnerText.Trim()
Write-Host "Declared version: $version"
if ($version -ne '0.1.0') { throw "Expected 0.1.0, read '$version'" }
```

Expected: `Declared version: 0.1.0`, no throw. This proves the XPath finds the element that Task 1 created — the single assumption connecting the two tasks.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/release.yml CLAUDE.md
git commit -m "ci: publish a checksummed release artifact from a version tag"
```

- [ ] **Step 5: Push**

```bash
git push origin main
```

**Stop here.** Do not create the `v0.1.0` tag. Cutting the first release is an owner-performed action — see the end of this plan.

---

### Task 7: Pre-publication checklist

**Files:**
- Create: `docs/pre-publication-checklist.md`

**Interfaces:**
- Consumes: nothing.
- Produces: nothing consumed by later tasks.

The repository is **private**. Publication is intended shortly after this increment — the owner has said it can happen as soon as it is needed — so this checklist is written to be acted on immediately, not filed away. This task does not itself change visibility: it produces the review, and the owner performs the flip once Tasks 1–3 have landed and the two checks below come back clean.

- [ ] **Step 1: Run the two checks the checklist is about**

**Check A — has any credential ever been committed?** The application is built so a token cannot reach a fixture, a log or a diagnostic bundle, but that is an argument about the current code, not a statement about every commit in the history.

```powershell
git log --all -p | Select-String -Pattern 'sk-ant-|ghp_|github_pat_|accessToken|refreshToken|Bearer ' | Select-Object -First 40
```

Record what matched and why each match is benign (for example, the *string literal* `claudeAiOauth.accessToken` appears in source as the name of a JSON property to read — that is a key name, not a key). If any match is an actual secret, **stop and report it**; do not attempt to rewrite history as part of this task.

**Check B — what account data is in `fixtures/`?** `fixtures/claude-statusline-sample.json` contains the author's real quota percentages and a session cost at one point in time. Its `session_id` is already `REDACTED`. Record exactly which fields are captured, so publication is a deliberate choice rather than an accident. `fixtures/claude-usage-limits-sample.json` is documented in CLAUDE.md as synthetic — confirm that by reading it.

- [ ] **Step 2: Write the checklist**

Create `docs/pre-publication-checklist.md` recording, for each item: what was checked, the command used, the finding, and whether it blocks publication.

| Item | Blocks publication? |
|---|---|
| No credential in any commit (Check A) | Yes, if found |
| Captured account data in `fixtures/` (Check B) | Owner's decision — record what it is |
| `LICENSE` present (Task 2) | Yes |
| `README.md` present and accurate (Task 3) | Yes |
| No hardcoded user path in source or docs | Yes — the standing user-agnostic constraint |
| Repository description and topics set | No |
| Issue template asking for the redacted diagnostics bundle | No |

Add a closing line stating that the list was compiled on 2026-08-17 against the commit it is committed with, and must be re-run if publication happens materially later.

- [ ] **Step 3: Verify no hardcoded user path leaked into the new documents**

```powershell
Select-String -Path 'README.md', 'docs/pre-publication-checklist.md', 'LICENSE' -Pattern 'C:\\Users\\|sgrig' -SimpleMatch
```

Expected: no matches. A path under the author's home folder in a public README is the exact failure the user-agnostic constraint exists to prevent.

- [ ] **Step 4: Commit**

```bash
git add docs/pre-publication-checklist.md
git commit -m "docs: record what must be checked before the repository is made public"
```

---

## Deliberate omissions

Recorded so a later reader does not mistake a decision for a gap. Full reasoning is in the spec, §4.

| Omitted | Why |
|---|---|
| `CHANGELOG.md` | `--generate-notes` reads the commit log, which is already conventional-commit formatted. A hand-maintained changelog would restate it and drift. |
| Screenshots in the README | No asset exists, and one from the author's machine shows the author's real quota percentages. |
| Code signing | Needs a paid certificate and a verified identity. The README documents the SmartScreen prompt honestly instead, and the `SHA256` gives the user something real to verify. |
| An installer, `winget`, Microsoft Store | All require a signed, identity-verified publisher. Blocked by the same thing, not by effort. |
| ARM64 build | Windows on ARM runs x64 under emulation; a second RID doubles the release surface for an unmeasured audience. |
| Auto-update | PRD §28 future enhancement. It needs a release feed to discover, which does not exist until this plan ships. |
| `dependabot.yml` | Two `PackageReference`s, both Microsoft first-party. |
| An About page in the UI | The version is already in Settings → Diagnostics → *Application*, which is where a person filing a bug report looks. A 360 px widget has no room for a number that matters only when something is wrong. |
| A unit test asserting the shipped version | Impossible: under `dotnet test` the entry assembly is never `AiUsageMonitor.App`. See Task 1. |
| Making the repository public | The owner's action, and it belongs after this plan rather than in it — publishing before `LICENSE` and `README` exist grants nobody any rights and documents nothing. Task 7 prepares for it. |

## Cutting the first release — owner action, after this plan

This plan stops at "the workflow exists and is green". The release itself is outward-facing and is not an agent's call to make. When ready:

```powershell
git tag v0.1.0
git push origin v0.1.0
gh run watch --exit-status
gh release view v0.1.0
```

Then verify the acceptance the whole increment exists for: download the asset from the release page, check its `SHA256` against the published file, and confirm it runs. While the repository is private that download is authenticated to the owner — which is a real test of the artifact but **not** of the user-agnostic constraint. That constraint is only satisfied once the repository is public and someone else has run the download.

**Recommended order:** Tasks 1–7 → make the repository public (`gh repo edit nayots/ai-agent-usage-monitor --visibility public`, after the Task 7 checks come back clean) → tag `v0.1.0`. Publishing before the licence and README exist buys nothing and costs the two things above; tagging before publication produces a release only the owner can download.

## Self-review

- **Spec coverage.** D1 → Task 1. D2 → Tasks 1 and 6. D3 → no task, by decision (recorded in the omissions table). D4 → Task 3 §4 and Task 4's checksum. D5 → Tasks 5 and 6. D6 → Task 6 step ordering. D7 → Task 4. D8 → Task 2. D9 → Task 1 step 2. D10 → Task 7. Acceptance items 1–10 all map to a verification step.
- **Type consistency.** The only cross-task contract is the XML element name `VersionPrefix`, written in Task 1 and read by XPath `//VersionPrefix` in Tasks 4 (indirectly, via the binary) and 6 (directly). Task 6 step 3 exists specifically to prove that link before the workflow depends on it. The asset name `QuotaMonitor-v<version>-win-x64.exe` is produced in Task 4 and referenced in Tasks 3 and 6.
- **No placeholders.** Every file this plan creates is given in full, except `README.md`, whose content is specified section by section because it is written by Claude rather than transcribed.
