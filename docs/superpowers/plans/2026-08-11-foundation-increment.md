# Foundation Increment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the verification spike into a tested foundation — a provider-neutral domain class library with xUnit coverage, a WPF shell proven to publish as a single self-contained executable, and shared build settings.

**Architecture:** The POC's `Domain/` folder is promoted to `src/AiUsageMonitor.Domain`, a Windows-agnostic `net10.0` class library with no dependencies beyond the BCL. `src/AiUsageMonitor.Poc` keeps its provider adapters and references the library, so the live verification harness keeps working unchanged. A new `src/AiUsageMonitor.App` WPF project exists only to prove the single-file publish path in this increment; it has no UI yet, because the design brief (`docs/design/design-prompt.md`) has not been run. `tests/AiUsageMonitor.Domain.Tests` covers the domain-layer items of PRD §25.

**Tech Stack:** C#, .NET 10 (SDK 10.0.301), WPF (`net10.0-windows`), xUnit 2.9.3, `System.Text.Json`. No other dependencies.

## Global Constraints

Every task's requirements implicitly include this section.

- Windows only. Primary shell is PowerShell 5.1 — no `&&`, no ternary, no null-coalescing. A Bash tool is also available and takes POSIX syntax.
- `dotnet build` must be warning-free. `TreatWarningsAsErrors` is `true` for every project.
- `Nullable` and `ImplicitUsings` are `enable` for every project.
- Target frameworks: `net10.0` for the domain library, the POC, and the test project. `net10.0-windows` for the WPF app only.
- **The domain model must stay generic.** No property may be named after a plan period — no `FiveHourQuota`, no `WeeklyQuota`. Quota window count, names, and durations are discovered, never assumed. (PRD §8)
- **Missing data is `null` and surfaces as `Waiting`/`Unavailable` — never as `0`.** (PRD §4.3, §13)
- **No credential may be logged, persisted, cached, displayed, or copied** — never in `Extra`, an exception message, or a diagnostic dump. (PRD §4.1.1, §23)
- Dependencies must be minimal and justified. `System.Text.Json` only. (PRD §22)
- No administrator privileges. (PRD §23)
- **Never add `PublishTrimmed`.** WPF hard-errors on it: `error NETSDK1168: WPF is not supported or recommended with trimming enabled`. Verified against SDK 10.0.301 on 2026-08-11.
- Every commit message ends with the trailer `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

## Verified publish facts

Measured on 2026-08-11, SDK 10.0.301, against a throwaway `net10.0-windows` WPF project. Do not re-derive.

| Configuration | Output | Verdict |
|---|---|---|
| `--self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true` | one `.exe`, **64.7 MB** | **Adopted** |
| the above `+ -p:PublishReadyToRun=true` | one `.exe`, 69.4 MB | Optional; +4.6 MB for faster startup. Defer. |
| `--self-contained false -p:PublishSingleFile=true` | one `.exe`, 172 KB | Fallback only; requires the .NET 10 Desktop Runtime preinstalled. |
| `-p:PublishTrimmed=true` | **build error NETSDK1168** | Impossible with WPF. |

64.7 MB is therefore the practical floor for a self-contained build. Accept it.

## PRD §25 coverage

**Covered by this plan:** generic quota normalization; percentage and raw-value formatting; quota ordering and fallback labels; reset countdown calculation; elapsed-time marker calculation; freshness and stale-state transitions; connection-state transitions.

**Deliberately deferred, with reasons:**

| §25 item | Deferred because |
|---|---|
| Settings validation and migration | No settings layer exists yet. |
| Secret redaction in diagnostics and logs | No logging or diagnostics layer exists yet. |
| View-model behavior for empty/partial/stale/error data | No view models exist yet; the design brief has not been run. |
| All integration tests | Provider adapters stay in the POC this increment. |
| All UI verification | Requires an approved design. |

---

### Task 1: Shared build settings and line-ending policy

**Files:**
- Create: `.gitattributes`
- Create: `Directory.Build.props`
- Modify: `src/AiUsageMonitor.Poc/AiUsageMonitor.Poc.csproj`

**Interfaces:**
- Consumes: nothing.
- Produces: `Directory.Build.props` at the repository root, applying `Nullable=enable`, `ImplicitUsings=enable`, and `TreatWarningsAsErrors=true` to every project created in later tasks. Later tasks must **not** repeat these three properties in individual `.csproj` files.

Git has warned about LF→CRLF conversion on every commit so far. Fixing it now stops the churn before three more projects are added.

- [ ] **Step 1: Create `.gitattributes`**

```gitattributes
* text=auto

*.cs      text diff=csharp
*.csproj  text
*.sln     text eol=crlf
*.props   text
*.json    text
*.md      text
*.ps1     text eol=crlf

*.png binary
*.ico binary
```

- [ ] **Step 2: Create `Directory.Build.props`**

Do not put `TargetFramework` here — the WPF project in Task 3 needs `net10.0-windows` while everything else needs `net10.0`.

```xml
<Project>

  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

</Project>
```

- [ ] **Step 3: Remove the now-inherited properties from the POC csproj**

Replace the contents of `src/AiUsageMonitor.Poc/AiUsageMonitor.Poc.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>AiUsageMonitor.Poc</RootNamespace>
    <AssemblyName>AiUsageMonitor.Poc</AssemblyName>
  </PropertyGroup>

</Project>
```

- [ ] **Step 4: Verify the build is still clean**

Run: `dotnet build`
Expected: `Build succeeded`, `0 Warning(s)`, `0 Error(s)`. If any warning appears, `TreatWarningsAsErrors` is not being inherited — check that `Directory.Build.props` is at the repository root, not inside `src/`.

- [ ] **Step 5: Normalize existing line endings**

```bash
git add --renormalize .
git status --short
```

Expected: some files show as modified with no content change.

- [ ] **Step 6: Commit**

```bash
git add .gitattributes Directory.Build.props src/AiUsageMonitor.Poc/AiUsageMonitor.Poc.csproj
git add --renormalize .
git commit -m "build: add shared build props and line-ending policy

Stops the LF/CRLF churn git has warned about on every commit, and
centralises Nullable, ImplicitUsings and TreatWarningsAsErrors before
three more projects are added.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: Extract the domain class library

**Files:**
- Create: `src/AiUsageMonitor.Domain/AiUsageMonitor.Domain.csproj`
- Move: `src/AiUsageMonitor.Poc/Domain/*.cs` → `src/AiUsageMonitor.Domain/*.cs` (5 files)
- Modify: `src/AiUsageMonitor.Poc/AiUsageMonitor.Poc.csproj` (add `ProjectReference`)
- Modify: `src/AiUsageMonitor.Poc/Program.cs:4`
- Modify: `src/AiUsageMonitor.Poc/Providers/Claude/ClaudeOAuthUsageProbe.cs:5`
- Modify: `src/AiUsageMonitor.Poc/Providers/Codex/CodexProbe.cs:4`
- Modify: `AiUsageMonitor.sln`

**Interfaces:**
- Consumes: `Directory.Build.props` from Task 1.
- Produces: namespace `AiUsageMonitor.Domain` containing `ConnectionState` (enum), `QuotaWindow` (record), `ProviderSnapshot` (record), `IProviderProbe` (interface), `DuckTypedQuotaExtractor` (static class). Every later task references this namespace. The old namespace `AiUsageMonitor.Poc.Domain` ceases to exist.

This is a pure move with a namespace rename. No behavior changes. The POC must produce byte-identical report output afterward.

- [ ] **Step 1: Create the library project**

```bash
cd "C:/Users/sgrig/source/repos/ai-agent-usage-monitor"
dotnet new classlib -o src/AiUsageMonitor.Domain -f net10.0
rm src/AiUsageMonitor.Domain/Class1.cs
```

- [ ] **Step 2: Replace the generated csproj**

`src/AiUsageMonitor.Domain/AiUsageMonitor.Domain.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>AiUsageMonitor.Domain</RootNamespace>
    <AssemblyName>AiUsageMonitor.Domain</AssemblyName>
  </PropertyGroup>

</Project>
```

No `PackageReference` of any kind. This library must depend on the BCL alone.

- [ ] **Step 3: Move the five source files**

```bash
git mv src/AiUsageMonitor.Poc/Domain/ConnectionState.cs         src/AiUsageMonitor.Domain/ConnectionState.cs
git mv src/AiUsageMonitor.Poc/Domain/QuotaWindow.cs             src/AiUsageMonitor.Domain/QuotaWindow.cs
git mv src/AiUsageMonitor.Poc/Domain/ProviderSnapshot.cs        src/AiUsageMonitor.Domain/ProviderSnapshot.cs
git mv src/AiUsageMonitor.Poc/Domain/IProviderProbe.cs          src/AiUsageMonitor.Domain/IProviderProbe.cs
git mv src/AiUsageMonitor.Poc/Domain/DuckTypedQuotaExtractor.cs src/AiUsageMonitor.Domain/DuckTypedQuotaExtractor.cs
rmdir src/AiUsageMonitor.Poc/Domain
```

- [ ] **Step 4: Rename the namespace in all five moved files**

In each of the five files, change the file-scoped namespace declaration from:

```csharp
namespace AiUsageMonitor.Poc.Domain;
```

to:

```csharp
namespace AiUsageMonitor.Domain;
```

- [ ] **Step 5: Update the three consumers**

In `src/AiUsageMonitor.Poc/Program.cs`, `src/AiUsageMonitor.Poc/Providers/Claude/ClaudeOAuthUsageProbe.cs`, and `src/AiUsageMonitor.Poc/Providers/Codex/CodexProbe.cs`, change:

```csharp
using AiUsageMonitor.Poc.Domain;
```

to:

```csharp
using AiUsageMonitor.Domain;
```

Those three files are the only consumers. Confirm with:

```bash
grep -rn "AiUsageMonitor.Poc.Domain" src/
```

Expected: no output.

- [ ] **Step 6: Wire up the project reference and the solution**

```bash
dotnet add src/AiUsageMonitor.Poc/AiUsageMonitor.Poc.csproj reference src/AiUsageMonitor.Domain/AiUsageMonitor.Domain.csproj
dotnet sln add src/AiUsageMonitor.Domain/AiUsageMonitor.Domain.csproj
```

- [ ] **Step 7: Verify the build and the live harness both still work**

```bash
dotnet build
dotnet run --project src/AiUsageMonitor.Poc
```

Expected: build succeeds with 0 warnings; the harness prints its provider report, both self-test sections still say `PASS: True`, and the process exits 0. The Codex probe takes ~2 s and the Claude probe ~0.5 s, both hitting the network.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "refactor: promote Domain to AiUsageMonitor.Domain class library

Pure move plus namespace rename, no behaviour change. Puts the
provider-neutral layer behind a project boundary so the WPF app and the
test project can consume it without depending on the POC harness.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: WPF shell and single-file publish validation

**Files:**
- Create: `src/AiUsageMonitor.App/AiUsageMonitor.App.csproj`
- Create: `src/AiUsageMonitor.App/App.xaml`
- Create: `src/AiUsageMonitor.App/App.xaml.cs`
- Create: `src/AiUsageMonitor.App/MainWindow.xaml`
- Create: `src/AiUsageMonitor.App/MainWindow.xaml.cs`
- Create: `build/publish.ps1`
- Modify: `AiUsageMonitor.sln`

**Interfaces:**
- Consumes: `AiUsageMonitor.Domain` from Task 2 (referenced to prove the dependency direction compiles; not yet used at runtime).
- Produces: `build/publish.ps1`, which produces exactly one `.exe` at `src/AiUsageMonitor.App/bin/Release/net10.0-windows/win-x64/publish/AiUsageMonitor.App.exe`. Later increments extend this project; no later task in *this* plan depends on it.

The window is deliberately empty. The design brief has not been run, so building any UI now would be guesswork. This task exists solely to retire the packaging risk early rather than discovering it after the UI is built.

- [ ] **Step 1: Scaffold the WPF project**

```bash
cd "C:/Users/sgrig/source/repos/ai-agent-usage-monitor"
dotnet new wpf -o src/AiUsageMonitor.App -f net10.0
dotnet sln add src/AiUsageMonitor.App/AiUsageMonitor.App.csproj
dotnet add src/AiUsageMonitor.App/AiUsageMonitor.App.csproj reference src/AiUsageMonitor.Domain/AiUsageMonitor.Domain.csproj
```

- [ ] **Step 2: Replace the generated csproj**

`src/AiUsageMonitor.App/AiUsageMonitor.App.csproj`. `RuntimeIdentifier` lives here because this application is Windows-x64 only; the self-contained and single-file switches stay on the command line so ordinary `dotnet build` runs stay fast.

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <RootNamespace>AiUsageMonitor.App</RootNamespace>
    <AssemblyName>AiUsageMonitor.App</AssemblyName>
    <DebugType>embedded</DebugType>
    <ApplicationManifest>app.manifest</ApplicationManifest>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\AiUsageMonitor.Domain\AiUsageMonitor.Domain.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Add the manifest that pins standard-user privileges**

PRD §23 forbids requesting elevation. Make that explicit in the binary rather than implicit.

`src/AiUsageMonitor.App/app.manifest`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="AiUsageMonitor.App" />

  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="asInvoker" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>

  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true</dpiAware>
    </windowsSettings>
  </application>
</assembly>
```

`PerMonitorV2` is required by PRD §17 for multi-monitor and high-DPI behavior, and is far easier to set now than to retrofit.

- [ ] **Step 4: Replace the generated window with a placeholder that states its own purpose**

`src/AiUsageMonitor.App/MainWindow.xaml`:

```xml
<Window x:Class="AiUsageMonitor.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="AI Usage Widget" Height="420" Width="360">
    <Grid>
        <TextBlock Margin="16"
                   TextWrapping="Wrap"
                   VerticalAlignment="Center"
                   Text="Shell only. No UI until the design brief in docs/design/design-prompt.md has been run and approved." />
    </Grid>
</Window>
```

Leave `MainWindow.xaml.cs`, `App.xaml`, and `App.xaml.cs` exactly as the template generated them.

- [ ] **Step 5: Create the publish script**

`build/publish.ps1`. This is PowerShell 5.1 — no `&&`, no ternary.

```powershell
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
```

- [ ] **Step 6: Run the publish and confirm a single executable**

Run: `powershell -ExecutionPolicy Bypass -File build/publish.ps1`

Expected: `Single-file publish OK`, one path ending in `AiUsageMonitor.App.exe`, and a size near 64.7 MB. The script throws if more than one `.exe` lands in the publish directory.

If it reports a size below 1 MB, `--self-contained true` was dropped and the result silently became framework-dependent — re-check the arguments.

- [ ] **Step 7: Confirm the executable actually runs**

Launch the published `.exe` by double-clicking it, or:

```bash
"src/AiUsageMonitor.App/bin/Release/net10.0-windows/win-x64/publish/AiUsageMonitor.App.exe" &
```

Expected: a 360×420 window titled "AI Usage Widget" showing the placeholder text. Close it. A single-file WPF build that compiles but fails to start is the exact failure this task exists to catch, so do not skip this step.

- [ ] **Step 8: Commit**

The repository's existing `.gitignore` already covers `[Bb]in/` and `[Oo]bj/`, so the 64.7 MB publish output will not be staged. Confirm with `git status --short` before committing.

```bash
git add -A
git commit -m "feat: add WPF shell and validate single-file publish

Empty shell only - the design brief has not been run, so any UI now
would be guesswork. Exists to retire the packaging risk early.

Verified: self-contained single-file publish yields one 64.7 MB .exe
that launches. PublishTrimmed is impossible with WPF (NETSDK1168), so
that size is the floor. Manifest pins asInvoker and PerMonitorV2 per
PRD SS17 and SS23.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: Test project and duck-typing regression coverage

**Files:**
- Create: `tests/AiUsageMonitor.Domain.Tests/AiUsageMonitor.Domain.Tests.csproj`
- Create: `tests/AiUsageMonitor.Domain.Tests/DuckTypedQuotaExtractorTests.cs`
- Delete: `tests/AiUsageMonitor.Domain.Tests/UnitTest1.cs` (template artifact)
- Modify: `AiUsageMonitor.sln`

**Interfaces:**
- Consumes: `AiUsageMonitor.Domain.DuckTypedQuotaExtractor.Extract(JsonElement)` and `QuotaWindow` from Task 2.
- Produces: the test project every later task adds files to, and a `FixturePath(string fileName)` helper returning an absolute path into the test project's output `Fixtures/` directory.

These two tests are the ones CLAUDE.md names as mandatory regression assertions. They currently exist only as `Console.WriteLine` self-tests inside `Program.cs`, which nothing enforces.

- [ ] **Step 1: Scaffold the test project**

```bash
cd "C:/Users/sgrig/source/repos/ai-agent-usage-monitor"
dotnet new xunit -o tests/AiUsageMonitor.Domain.Tests -f net10.0
rm tests/AiUsageMonitor.Domain.Tests/UnitTest1.cs
dotnet sln add tests/AiUsageMonitor.Domain.Tests/AiUsageMonitor.Domain.Tests.csproj
dotnet add tests/AiUsageMonitor.Domain.Tests/AiUsageMonitor.Domain.Tests.csproj reference src/AiUsageMonitor.Domain/AiUsageMonitor.Domain.csproj
```

The template pins xunit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1, xunit.runner.visualstudio 3.1.4, coverlet.collector 6.0.4. Keep those versions.

- [ ] **Step 2: Link the recorded fixture into the test project**

Add this `ItemGroup` to `tests/AiUsageMonitor.Domain.Tests/AiUsageMonitor.Domain.Tests.csproj`, so the repository's `fixtures/` directory stays the single source of truth rather than the JSON being duplicated into a C# constant:

```xml
  <ItemGroup>
    <None Include="..\..\fixtures\claude-statusline-sample.json"
          Link="Fixtures\claude-statusline-sample.json"
          CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 3: Write the failing tests**

`tests/AiUsageMonitor.Domain.Tests/DuckTypedQuotaExtractorTests.cs`.

Note the namespace: declaring `AiUsageMonitor.Domain.Tests` makes everything in `AiUsageMonitor.Domain` visible without a `using`.

```csharp
using System.Text.Json;
using Xunit;

namespace AiUsageMonitor.Domain.Tests;

public class DuckTypedQuotaExtractorTests
{
    /// <summary>
    /// Four windows across four different key dialects, one of them ("three_hour_nimbus")
    /// a name no provider documents. Ported from the Program.cs self-test.
    /// </summary>
    private const string MultiDialectSample =
        """
        {
          "rateLimits": {
            "five_hour": { "used_percent": 42.5, "resets_at": 1786800000 },
            "seven_day": { "utilization": 73.1, "reset": "2026-08-17T12:00:00Z" },
            "seven_day_opus": { "usedPercent": 12.0, "resetsAt": 1786900000, "windowDurationMins": 10080 },
            "three_hour_nimbus": { "percent_used": 5.5, "reset_at": 1786700000 }
          },
          "meta": { "note": "self-test sample, deliberately not a real provider shape" }
        }
        """;

    internal static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    [Fact]
    public void Extract_FindsEveryWindow_AcrossAllKeyDialects()
    {
        using JsonDocument doc = JsonDocument.Parse(MultiDialectSample);

        IReadOnlyList<QuotaWindow> windows = DuckTypedQuotaExtractor.Extract(doc.RootElement);

        Assert.Equal(4, windows.Count);
    }

    [Fact]
    public void Extract_KeepsUndocumentedWindowName_WithoutCodeChange()
    {
        using JsonDocument doc = JsonDocument.Parse(MultiDialectSample);

        IReadOnlyList<QuotaWindow> windows = DuckTypedQuotaExtractor.Extract(doc.RootElement);

        Assert.Contains(windows, w => w.Id == "three_hour_nimbus");
    }

    [Fact]
    public void Extract_ExcludesContextWindowFill_BecauseItCarriesNoResetKey()
    {
        // context_window has used_percentage but no reset-ish key. It is conversation fill,
        // not subscription quota, and must never surface as a quota window. PRD SS7.3.
        string json = File.ReadAllText(FixturePath("claude-statusline-sample.json"));
        using JsonDocument doc = JsonDocument.Parse(json);

        IReadOnlyList<QuotaWindow> windows = DuckTypedQuotaExtractor.Extract(doc.RootElement);

        Assert.DoesNotContain(windows, w => w.Id == "context_window");
    }

    [Fact]
    public void Extract_ReadsBothStatusLineWindows_FromTheRecordedSample()
    {
        string json = File.ReadAllText(FixturePath("claude-statusline-sample.json"));
        using JsonDocument doc = JsonDocument.Parse(json);

        IReadOnlyList<QuotaWindow> windows = DuckTypedQuotaExtractor.Extract(doc.RootElement);

        Assert.Equal(2, windows.Count);
        Assert.Contains(windows, w => w.Id == "five_hour");
        Assert.Contains(windows, w => w.Id == "seven_day");
    }

    [Fact]
    public void Extract_InvertsRemainingPercentages_IntoUsedPercent()
    {
        // "remaining_percentage" reports the opposite quantity and must be inverted, not copied.
        using JsonDocument doc = JsonDocument.Parse(
            """{ "some_window": { "remaining_percentage": 20, "resets_at": 1786800000 } }""");

        IReadOnlyList<QuotaWindow> windows = DuckTypedQuotaExtractor.Extract(doc.RootElement);

        QuotaWindow window = Assert.Single(windows);
        Assert.Equal(80.0, window.UsedPercent);
        Assert.Equal("remaining", window.Extra["source"]);
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/AiUsageMonitor.Domain.Tests`
Expected: **5 passed**. These characterize behavior that already works — a failure here means the Task 2 move broke something.

If `Extract_ExcludesContextWindowFill_BecauseItCarriesNoResetKey` fails with a `FileNotFoundException`, the `None Include` link in Step 2 is wrong or missing.

- [ ] **Step 5: Commit**

```bash
git add tests/ AiUsageMonitor.sln
git commit -m "test: add domain test project with duck-typing regression coverage

Promotes the two assertions CLAUDE.md names as mandatory from unenforced
Console.WriteLine self-tests into real tests: unknown window names
survive extraction, and context_window fill is excluded because it
carries no reset key.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: Quota window calculation coverage

**Files:**
- Create: `tests/AiUsageMonitor.Domain.Tests/QuotaWindowTests.cs`

**Interfaces:**
- Consumes: `QuotaWindow.TimeUntilReset(DateTimeOffset)`, `QuotaWindow.ElapsedFraction(DateTimeOffset)`, `QuotaWindow.RemainingPercent` from Task 2.
- Produces: `QuotaWindowTests.Window(...)`, a factory used by later test files:
  `internal static QuotaWindow Window(string id = "w", double? usedPercent = 50, DateTimeOffset? resetsAt = null, TimeSpan? windowDuration = null, int order = 0)`

Covers PRD §25's "Reset countdown calculation" and "Elapsed-time marker calculation". These methods exist, so most tests characterize current behavior; the value is in pinning the null and boundary cases that PRD §16 depends on.

- [ ] **Step 1: Write the tests**

`tests/AiUsageMonitor.Domain.Tests/QuotaWindowTests.cs`:

```csharp
using Xunit;

namespace AiUsageMonitor.Domain.Tests;

public class QuotaWindowTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    internal static QuotaWindow Window(
        string id = "w",
        double? usedPercent = 50,
        DateTimeOffset? resetsAt = null,
        TimeSpan? windowDuration = null,
        int order = 0) =>
        new(
            Id: id,
            Label: id,
            UsedPercent: usedPercent,
            ResetsAt: resetsAt,
            WindowDuration: windowDuration,
            Order: order,
            IsPartial: resetsAt is null || windowDuration is null,
            Extra: new Dictionary<string, string>());

    [Fact]
    public void TimeUntilReset_IsNull_WhenTheProviderSuppliedNoResetTime()
    {
        // Absence must stay absence. A null countdown is omitted, never rendered as zero.
        Assert.Null(Window(resetsAt: null).TimeUntilReset(Now));
    }

    [Fact]
    public void TimeUntilReset_ReturnsTheRemainingSpan()
    {
        QuotaWindow window = Window(resetsAt: Now.AddHours(4).AddMinutes(12));

        Assert.Equal(TimeSpan.FromMinutes(252), window.TimeUntilReset(Now));
    }

    [Fact]
    public void TimeUntilReset_ClampsToZero_WhenTheResetTimeHasPassed()
    {
        // Stale snapshots routinely carry a reset time in the past. Never show a negative countdown.
        QuotaWindow window = Window(resetsAt: Now.AddHours(-3));

        Assert.Equal(TimeSpan.Zero, window.TimeUntilReset(Now));
    }

    [Fact]
    public void ElapsedFraction_IsNull_WhenTheWindowDurationIsUnknown()
    {
        // This is the nimbus_quill case: a real live window with a percentage and nothing else.
        // PRD SS16 requires the elapsed marker to be omitted, never guessed.
        QuotaWindow window = Window(resetsAt: Now.AddHours(2), windowDuration: null);

        Assert.Null(window.ElapsedFraction(Now));
    }

    [Fact]
    public void ElapsedFraction_IsNull_WhenTheResetTimeIsUnknown()
    {
        QuotaWindow window = Window(resetsAt: null, windowDuration: TimeSpan.FromHours(5));

        Assert.Null(window.ElapsedFraction(Now));
    }

    [Fact]
    public void ElapsedFraction_IsNull_ForAZeroLengthWindow()
    {
        QuotaWindow window = Window(resetsAt: Now, windowDuration: TimeSpan.Zero);

        Assert.Null(window.ElapsedFraction(Now));
    }

    [Theory]
    [InlineData(0, 1.0)]      // reset is now: the window is fully elapsed
    [InlineData(5, 0.0)]      // reset is a full duration away: nothing elapsed
    [InlineData(1, 0.8)]
    [InlineData(4, 0.2)]
    public void ElapsedFraction_MapsResetDistanceOntoZeroToOne(int hoursUntilReset, double expected)
    {
        QuotaWindow window = Window(
            resetsAt: Now.AddHours(hoursUntilReset),
            windowDuration: TimeSpan.FromHours(5));

        Assert.Equal(expected, window.ElapsedFraction(Now)!.Value, precision: 6);
    }

    [Fact]
    public void ElapsedFraction_ClampsAboveOne_WhenTheResetTimeIsAlreadyPast()
    {
        QuotaWindow window = Window(
            resetsAt: Now.AddHours(-10),
            windowDuration: TimeSpan.FromHours(5));

        Assert.Equal(1.0, window.ElapsedFraction(Now)!.Value, precision: 6);
    }

    [Fact]
    public void ElapsedFraction_ReproducesTheVerifiedCodexState()
    {
        // Verified 2026-08-10: 100% used with only ~24% of a 7-day window elapsed.
        // The gap between fill and marker is the whole reason the marker exists (PRD SS16).
        var duration = TimeSpan.FromDays(7);
        QuotaWindow window = Window(
            usedPercent: 100,
            resetsAt: Now.Add(duration * 0.76),
            windowDuration: duration);

        Assert.Equal(0.24, window.ElapsedFraction(Now)!.Value, precision: 2);
        Assert.Equal(100.0, window.UsedPercent);
    }

    [Fact]
    public void RemainingPercent_IsNull_WhenUsageIsUnknown()
    {
        // Must never collapse to 100. Unknown usage is unknown remaining.
        Assert.Null(Window(usedPercent: null).RemainingPercent);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(28, 72)]
    [InlineData(100, 0)]
    [InlineData(120, 0)]   // provider over-reporting is clamped, not propagated
    public void RemainingPercent_IsTheClampedComplementOfUsage(double used, double expected)
    {
        Assert.Equal(expected, Window(usedPercent: used).RemainingPercent);
    }
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test tests/AiUsageMonitor.Domain.Tests`
Expected: all pass (5 from Task 4 plus 19 here).

- [ ] **Step 3: Commit**

```bash
git add tests/AiUsageMonitor.Domain.Tests/QuotaWindowTests.cs
git commit -m "test: cover countdown and elapsed-marker calculation

Pins the null and boundary cases PRD SS16 depends on: no reset time and
no duration both yield a null elapsed fraction rather than a guess, past
reset times clamp instead of going negative, and unknown usage never
collapses to a number.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: Freshness policy and connection-state transitions

**Files:**
- Create: `src/AiUsageMonitor.Domain/Freshness.cs`
- Create: `tests/AiUsageMonitor.Domain.Tests/FreshnessTests.cs`

**Interfaces:**
- Consumes: `ConnectionState` from Task 2.
- Produces:
  - `enum FreshnessState { Unknown, Fresh, Stale }`
  - `sealed record FreshnessPolicy(TimeSpan StaleAfter)` with `FreshnessState Evaluate(DateTimeOffset? retrievedAt, DateTimeOffset now)` and `static FreshnessPolicy Default` (5 minutes)
  - `static class ConnectionStateRules` with `static ConnectionState ApplyFreshness(ConnectionState state, FreshnessState freshness)`

New code, so real TDD: the test must fail before the implementation exists. Covers PRD §25's "Freshness and stale-state transitions" and "Connection-state transitions".

- [ ] **Step 1: Write the failing tests**

`tests/AiUsageMonitor.Domain.Tests/FreshnessTests.cs`:

```csharp
using Xunit;

namespace AiUsageMonitor.Domain.Tests;

public class FreshnessPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly FreshnessPolicy FiveMinutes = new(TimeSpan.FromMinutes(5));

    [Fact]
    public void Evaluate_IsUnknown_WhenNothingHasEverBeenRetrieved()
    {
        // Never retrieved is not the same as stale. A provider that has not answered yet
        // is Waiting, and must not be presented as holding out-of-date data.
        Assert.Equal(FreshnessState.Unknown, FiveMinutes.Evaluate(retrievedAt: null, Now));
    }

    [Fact]
    public void Evaluate_IsFresh_WithinTheThreshold()
    {
        Assert.Equal(FreshnessState.Fresh, FiveMinutes.Evaluate(Now.AddMinutes(-4), Now));
    }

    [Fact]
    public void Evaluate_IsFresh_ExactlyAtTheThreshold()
    {
        // The boundary belongs to Fresh: a value becomes stale when it EXCEEDS the threshold.
        Assert.Equal(FreshnessState.Fresh, FiveMinutes.Evaluate(Now.AddMinutes(-5), Now));
    }

    [Fact]
    public void Evaluate_IsStale_PastTheThreshold()
    {
        Assert.Equal(FreshnessState.Stale, FiveMinutes.Evaluate(Now.AddMinutes(-6), Now));
    }

    [Fact]
    public void Evaluate_IsFresh_WhenTheTimestampIsInTheFuture()
    {
        // Clock skew and DST shifts produce future timestamps. Never report those as stale.
        Assert.Equal(FreshnessState.Fresh, FiveMinutes.Evaluate(Now.AddMinutes(3), Now));
    }

    [Fact]
    public void Default_UsesAConservativeFiveMinuteThreshold()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), FreshnessPolicy.Default.StaleAfter);
    }
}

public class ConnectionStateRulesTests
{
    [Fact]
    public void ApplyFreshness_DemotesConnectedToStale_WhenTheDataHasAged()
    {
        Assert.Equal(
            ConnectionState.Stale,
            ConnectionStateRules.ApplyFreshness(ConnectionState.Connected, FreshnessState.Stale));
    }

    [Fact]
    public void ApplyFreshness_LeavesConnectedAlone_WhenTheDataIsFresh()
    {
        Assert.Equal(
            ConnectionState.Connected,
            ConnectionStateRules.ApplyFreshness(ConnectionState.Connected, FreshnessState.Fresh));
    }

    [Theory]
    [InlineData(ConnectionState.Error)]
    [InlineData(ConnectionState.NotInstalled)]
    [InlineData(ConnectionState.Unsupported)]
    [InlineData(ConnectionState.Unavailable)]
    [InlineData(ConnectionState.Waiting)]
    [InlineData(ConnectionState.Discovering)]
    public void ApplyFreshness_NeverOverwritesANonConnectedState(ConnectionState state)
    {
        // Age must not mask a real failure. An Error that is also old is still an Error -
        // presenting it as merely Stale would imply recoverable data exists when it does not.
        Assert.Equal(state, ConnectionStateRules.ApplyFreshness(state, FreshnessState.Stale));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/AiUsageMonitor.Domain.Tests`
Expected: **compilation failure** — `CS0246: The type or namespace name 'FreshnessPolicy' could not be found` and the same for `FreshnessState` and `ConnectionStateRules`.

- [ ] **Step 3: Write the implementation**

`src/AiUsageMonitor.Domain/Freshness.cs`:

```csharp
namespace AiUsageMonitor.Domain;

/// <summary>How current a provider snapshot is, relative to a configured threshold.</summary>
public enum FreshnessState
{
    /// <summary>No successful retrieval has happened yet. Distinct from stale: there is no data to age.</summary>
    Unknown,

    /// <summary>Retrieved within the threshold.</summary>
    Fresh,

    /// <summary>Older than the threshold. Values may still be shown, but must be marked (PRD SS18).</summary>
    Stale
}

/// <summary>
/// Decides whether a snapshot has aged past its threshold. Thresholds are configurable per
/// provider integration and conservative by default (PRD SS14).
/// </summary>
public sealed record FreshnessPolicy(TimeSpan StaleAfter)
{
    public static FreshnessPolicy Default { get; } = new(TimeSpan.FromMinutes(5));

    public FreshnessState Evaluate(DateTimeOffset? retrievedAt, DateTimeOffset now)
    {
        if (retrievedAt is not DateTimeOffset at)
        {
            return FreshnessState.Unknown;
        }

        TimeSpan age = now - at;

        // Clock skew, DST transitions and resume-from-sleep all produce future timestamps.
        // Treat them as fresh rather than reporting a negative age as stale.
        if (age < TimeSpan.Zero)
        {
            return FreshnessState.Fresh;
        }

        return age > StaleAfter ? FreshnessState.Stale : FreshnessState.Fresh;
    }
}

/// <summary>Provider-neutral rules for deriving the state a card should present.</summary>
public static class ConnectionStateRules
{
    /// <summary>
    /// Ages a <see cref="ConnectionState.Connected"/> provider into <see cref="ConnectionState.Stale"/>.
    /// Every other state is returned untouched: age must never mask a real failure, because
    /// presenting an aged Error as Stale implies recoverable data exists when it does not.
    /// </summary>
    public static ConnectionState ApplyFreshness(ConnectionState state, FreshnessState freshness) =>
        state == ConnectionState.Connected && freshness == FreshnessState.Stale
            ? ConnectionState.Stale
            : state;
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/AiUsageMonitor.Domain.Tests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/AiUsageMonitor.Domain/Freshness.cs tests/AiUsageMonitor.Domain.Tests/FreshnessTests.cs
git commit -m "feat: add freshness policy and connection-state transition rules

Unknown is kept distinct from Stale - a provider that has never answered
is Waiting, not holding old data. Future timestamps from clock skew are
treated as fresh. Ageing only demotes Connected; an aged Error stays an
Error rather than implying recoverable data exists.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 7: Display formatting

**Files:**
- Create: `src/AiUsageMonitor.Domain/QuotaFormatting.cs`
- Create: `tests/AiUsageMonitor.Domain.Tests/QuotaFormattingTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `static class QuotaFormatting` with `static string? FormatUsedPercent(double?)`, `static string? FormatRemainingPercent(double?)`, `static string? FormatCountdown(TimeSpan?)`. **Every one returns `null` for `null` input** — callers omit the element rather than substituting text.

New code, real TDD. Covers PRD §25's "Percentage and raw-value formatting". The single most important property under test is that nothing ever renders a missing value as `0`.

- [ ] **Step 1: Write the failing tests**

`tests/AiUsageMonitor.Domain.Tests/QuotaFormattingTests.cs`:

```csharp
using Xunit;

namespace AiUsageMonitor.Domain.Tests;

public class QuotaFormattingTests
{
    [Fact]
    public void FormatUsedPercent_IsNull_WhenUsageIsUnknown()
    {
        // The single most important assertion in this file. Missing data is null and
        // surfaces as Waiting/Unavailable - never as "0% used". PRD SS4.3 / SS13.
        Assert.Null(QuotaFormatting.FormatUsedPercent(null));
    }

    [Theory]
    [InlineData(0, "0% used")]
    [InlineData(28, "28% used")]
    [InlineData(100, "100% used")]
    [InlineData(42.5, "43% used")]   // away-from-zero, not banker's rounding
    [InlineData(42.4, "42% used")]
    public void FormatUsedPercent_StatesTheDirectionExplicitly(double used, string expected)
    {
        // PRD SS16: the visible percentage text must make the direction explicit.
        Assert.Equal(expected, QuotaFormatting.FormatUsedPercent(used));
    }

    [Fact]
    public void FormatRemainingPercent_IsNull_WhenUsageIsUnknown()
    {
        Assert.Null(QuotaFormatting.FormatRemainingPercent(null));
    }

    [Fact]
    public void FormatRemainingPercent_StatesTheDirectionExplicitly()
    {
        Assert.Equal("72% remaining", QuotaFormatting.FormatRemainingPercent(72));
    }

    [Fact]
    public void FormatCountdown_IsNull_WhenNoResetTimeIsKnown()
    {
        // nimbus_quill has no reset time. The countdown is omitted, not zeroed.
        Assert.Null(QuotaFormatting.FormatCountdown(null));
    }

    [Theory]
    [InlineData(0, 0, 9, 30, "9m 30s")]
    [InlineData(0, 4, 12, 0, "4h 12m")]
    [InlineData(0, 1, 0, 0, "1h 00m")]
    [InlineData(3, 4, 30, 0, "3d 04h")]
    [InlineData(5, 7, 39, 0, "5d 07h")]
    public void FormatCountdown_UsesTwoUnitsAtTheAppropriateScale(
        int days, int hours, int minutes, int seconds, string expected)
    {
        var span = new TimeSpan(days, hours, minutes, seconds);

        Assert.Equal(expected, QuotaFormatting.FormatCountdown(span));
    }

    [Fact]
    public void FormatCountdown_ClampsNegativeSpansToZero()
    {
        Assert.Equal("0m 00s", QuotaFormatting.FormatCountdown(TimeSpan.FromMinutes(-30)));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/AiUsageMonitor.Domain.Tests`
Expected: **compilation failure** — `CS0103: The name 'QuotaFormatting' does not exist in the current context`.

- [ ] **Step 3: Write the implementation**

`src/AiUsageMonitor.Domain/QuotaFormatting.cs`:

```csharp
using System.Globalization;

namespace AiUsageMonitor.Domain;

/// <summary>
/// Renders quota values as display strings. Every method returns null for null input:
/// a caller omits the element entirely rather than substituting a placeholder, because a
/// rendered "0%" or "--" is indistinguishable from real data at a glance (PRD SS4.3).
/// </summary>
public static class QuotaFormatting
{
    public static string? FormatUsedPercent(double? usedPercent) =>
        usedPercent is double v ? $"{Round(v)}% used" : null;

    public static string? FormatRemainingPercent(double? remainingPercent) =>
        remainingPercent is double v ? $"{Round(v)}% remaining" : null;

    /// <summary>
    /// Two units at the largest meaningful scale: "5d 07h", "4h 12m", "9m 30s".
    /// Negative spans clamp to zero - a stale snapshot's reset time is routinely in the past.
    /// </summary>
    public static string? FormatCountdown(TimeSpan? remaining)
    {
        if (remaining is not TimeSpan span)
        {
            return null;
        }

        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        if (span.TotalDays >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalDays}d {span.Hours:D2}h");
        }

        if (span.TotalHours >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours}h {span.Minutes:D2}m");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{span.Minutes}m {span.Seconds:D2}s");
    }

    private static string Round(double value) =>
        Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/AiUsageMonitor.Domain.Tests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/AiUsageMonitor.Domain/QuotaFormatting.cs tests/AiUsageMonitor.Domain.Tests/QuotaFormattingTests.cs
git commit -m "feat: add quota display formatting

Every formatter returns null for null input so callers omit the element
rather than substituting a placeholder - a rendered 0% is
indistinguishable from real data at a glance. Percentages state their
direction explicitly per PRD SS16.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 8: Unparseable window names keep the provider's literal token

**Files:**
- Modify: `src/AiUsageMonitor.Domain/QuotaWindow.cs` (add one property)
- Modify: `src/AiUsageMonitor.Domain/DuckTypedQuotaExtractor.cs:182-191` (the `QuotaWindow` construction) and the `Humanize` region
- Create: `tests/AiUsageMonitor.Domain.Tests/QuotaLabelTests.cs`

**Interfaces:**
- Consumes: `DuckTypedQuotaExtractor.Extract`, `QuotaWindow` from Task 2.
- Produces: `QuotaWindow` gains `bool LabelIsProviderToken` as its **last** positional record parameter, after `Extra`. Task 5's `Window(...)` factory and any other construction site must be updated to match. `DuckTypedQuotaExtractor.Humanize(string)` keeps its current signature and behavior; a new `static bool TryHumanize(string id, out string label)` decides whether humanizing is honest.

**This task changes existing behavior.** PRD §7.2 item 10 requires that a window name which does not resolve to a known duration renders as the provider's *literal token*, typographically distinguished. Today `Humanize("nimbus_quill")` returns `"nimbus quill"`, which reads as a label the application understands. It does not.

The distinction is exactly whether the name parses as `<number> <unit>`:

| Id | Parses? | Label | `LabelIsProviderToken` |
|---|---|---|---|
| `five_hour` | yes | `5 hour` | `false` |
| `seven_day_opus` | yes | `7 day (opus)` | `false` |
| `nimbus_quill` | **no** | `nimbus_quill` | **`true`** |
| `codex` | **no** | `codex` | **`true`** |

- [ ] **Step 1: Write the failing tests**

`tests/AiUsageMonitor.Domain.Tests/QuotaLabelTests.cs`:

```csharp
using System.Text.Json;
using Xunit;

namespace AiUsageMonitor.Domain.Tests;

public class QuotaLabelTests
{
    private static QuotaWindow ExtractSingle(string id)
    {
        using JsonDocument doc = JsonDocument.Parse(
            $$"""{ "{{id}}": { "used_percent": 10, "resets_at": 1786800000 } }""");

        return Assert.Single(DuckTypedQuotaExtractor.Extract(doc.RootElement));
    }

    [Fact]
    public void UnparseableName_KeepsTheProviderToken_Verbatim()
    {
        // Verified live on 2026-08-10. "Nimbus quill" would read as a feature name this
        // application recognises. It does not, and must not pretend to. PRD SS7.2 item 10.
        QuotaWindow window = ExtractSingle("nimbus_quill");

        Assert.Equal("nimbus_quill", window.Label);
        Assert.True(window.LabelIsProviderToken);
    }

    [Fact]
    public void SingleTokenName_KeepsTheProviderToken_Verbatim()
    {
        QuotaWindow window = ExtractSingle("codex");

        Assert.Equal("codex", window.Label);
        Assert.True(window.LabelIsProviderToken);
    }

    [Fact]
    public void ParseableName_IsHumanized()
    {
        QuotaWindow window = ExtractSingle("five_hour");

        Assert.Equal("5 hour", window.Label);
        Assert.False(window.LabelIsProviderToken);
    }

    [Fact]
    public void ParseableName_PreservesTrailingProviderTokens()
    {
        // The "opus" token must survive - never dropped, never reinterpreted.
        QuotaWindow window = ExtractSingle("seven_day_opus");

        Assert.Equal("7 day (opus)", window.Label);
        Assert.False(window.LabelIsProviderToken);
    }

    [Fact]
    public void TheRawIdentifierIsAlwaysPreserved_RegardlessOfLabelling()
    {
        // PRD SS7.2 item 10: the provider-supplied identifier stays available for every
        // window, so a tooltip and diagnostics can always show it.
        Assert.Equal("nimbus_quill", ExtractSingle("nimbus_quill").Id);
        Assert.Equal("five_hour", ExtractSingle("five_hour").Id);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/AiUsageMonitor.Domain.Tests --filter FullyQualifiedName~QuotaLabelTests`
Expected: **compilation failure** — `CS1061: 'QuotaWindow' does not contain a definition for 'LabelIsProviderToken'`.

- [ ] **Step 3: Add the property to `QuotaWindow`**

In `src/AiUsageMonitor.Domain/QuotaWindow.cs`, add a final positional parameter after `Extra`:

```csharp
public sealed record QuotaWindow(
    string Id,
    string Label,
    double? UsedPercent,
    DateTimeOffset? ResetsAt,
    TimeSpan? WindowDuration,
    int Order,
    bool IsPartial,
    IReadOnlyDictionary<string, string> Extra,
    bool LabelIsProviderToken = false)
```

The default of `false` keeps every existing construction site compiling. Document it directly above the record:

```csharp
/// <param name="LabelIsProviderToken">
/// True when <paramref name="Label"/> is the provider's raw identifier because the name could not
/// be resolved to a duration. The UI must render these distinctly so a provider term is never
/// mistaken for a label this application understands (PRD SS7.2 item 10).
/// </param>
```

- [ ] **Step 4: Add `TryHumanize` to the extractor**

In `src/AiUsageMonitor.Domain/DuckTypedQuotaExtractor.cs`, add this alongside the existing `Humanize`:

```csharp
    /// <summary>
    /// Humanises an id only when doing so is honest — that is, when the id parses as a recognised
    /// number-word or digit run followed by a recognised time unit. Otherwise reports failure so
    /// the caller keeps the provider's literal token. "nimbus_quill" must never become
    /// "nimbus quill", which would read as a label this application understands.
    /// </summary>
    public static bool TryHumanize(string id, out string label)
    {
        if (TryInferDurationFromName(id) is null)
        {
            label = id;
            return false;
        }

        label = Humanize(id);
        return true;
    }
```

`TryInferDurationFromName` already implements exactly the required parse rule, and already returns null for `nimbus_quill` and `codex`. Reuse it rather than writing a second parser that can drift.

- [ ] **Step 5: Use it when constructing the window**

In `TryExtractWindow`, replace the `QuotaWindow` construction (currently at lines 182–191) with:

```csharp
        bool labelIsProviderToken = !TryHumanize(id, out string label);

        window = new QuotaWindow(
            Id: id,
            Label: label,
            UsedPercent: usedPercent,
            ResetsAt: resetsAt,
            WindowDuration: windowDuration,
            Order: order,
            IsPartial: isPartial,
            Extra: extra,
            LabelIsProviderToken: labelIsProviderToken);
```

- [ ] **Step 6: Update the Task 5 test factory**

In `tests/AiUsageMonitor.Domain.Tests/QuotaWindowTests.cs`, the `Window(...)` factory constructs a `QuotaWindow` positionally. It still compiles because the new parameter is optional, so no change is required — but run the full suite to confirm.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/AiUsageMonitor.Domain.Tests`
Expected: all pass, including the earlier tests. `Extract_KeepsUndocumentedWindowName_WithoutCodeChange` from Task 4 must still pass — `three_hour_nimbus` parses (`three` → 3, `hour`), so it stays humanized as `3 hour (nimbus)` with the token preserved.

- [ ] **Step 8: Confirm the live harness still reports correctly**

Run: `dotnet run --project src/AiUsageMonitor.Poc`
Expected: the Claude provider's third window now prints its label as `nimbus_quill` rather than `nimbus quill`. Both self-test sections still say `PASS: True`.

- [ ] **Step 9: Commit**

```bash
git add src/AiUsageMonitor.Domain/QuotaWindow.cs src/AiUsageMonitor.Domain/DuckTypedQuotaExtractor.cs tests/AiUsageMonitor.Domain.Tests/QuotaLabelTests.cs
git commit -m "fix: keep the provider's literal token when a window name cannot be parsed

Humanize turned nimbus_quill into 'nimbus quill', which reads as a label
this application understands. It does not. Names now humanize only when
they parse as <number> <unit>; otherwise the raw identifier is kept and
LabelIsProviderToken is set so the UI can render it distinctly, per
PRD SS7.2 item 10. Trailing provider tokens on parseable names are still
preserved: seven_day_opus stays '7 day (opus)'.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 9: Quota ordering and label fallback

**Files:**
- Create: `src/AiUsageMonitor.Domain/QuotaOrdering.cs`
- Create: `tests/AiUsageMonitor.Domain.Tests/QuotaOrderingTests.cs`

**Interfaces:**
- Consumes: `QuotaWindow` from Task 8 (including `LabelIsProviderToken`), `QuotaWindowTests.Window(...)` from Task 5.
- Produces: `static class QuotaOrdering` with `static IReadOnlyList<QuotaWindow> InProviderOrder(IEnumerable<QuotaWindow>)` and `static string DisplayLabel(QuotaWindow)`.

New code, real TDD. Covers PRD §25's "Quota ordering and fallback labels". The ordering rule is a direct consequence of a verified observation: on the same account a `seven_day` window reset *sooner* than a `five_hour` one, so sorting by assumed duration produces a wrong order.

- [ ] **Step 1: Write the failing tests**

`tests/AiUsageMonitor.Domain.Tests/QuotaOrderingTests.cs`:

```csharp
using Xunit;

namespace AiUsageMonitor.Domain.Tests;

public class QuotaOrderingTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void InProviderOrder_PreservesProviderOrder_NotDuration()
    {
        // Verified 2026-08-10: seven_day reset SOONER than five_hour on the same account.
        // Sorting by assumed duration or by countdown would reorder them wrongly. PRD SS7.3.
        QuotaWindow fiveHour = QuotaWindowTests.Window(
            id: "five_hour", resetsAt: Now.AddHours(3).AddMinutes(12),
            windowDuration: TimeSpan.FromHours(5), order: 0);
        QuotaWindow sevenDay = QuotaWindowTests.Window(
            id: "seven_day", resetsAt: Now.AddHours(4).AddMinutes(55),
            windowDuration: TimeSpan.FromDays(7), order: 1);

        IReadOnlyList<QuotaWindow> ordered = QuotaOrdering.InProviderOrder(new[] { sevenDay, fiveHour });

        Assert.Equal(new[] { "five_hour", "seven_day" }, ordered.Select(w => w.Id).ToArray());
    }

    [Fact]
    public void InProviderOrder_IsStable_WhenOrdersCollide()
    {
        QuotaWindow first = QuotaWindowTests.Window(id: "a", order: 0);
        QuotaWindow second = QuotaWindowTests.Window(id: "b", order: 0);

        IReadOnlyList<QuotaWindow> ordered = QuotaOrdering.InProviderOrder(new[] { first, second });

        Assert.Equal(new[] { "a", "b" }, ordered.Select(w => w.Id).ToArray());
    }

    [Fact]
    public void InProviderOrder_HandlesAnEmptySequence()
    {
        // A provider reporting zero windows is a valid state, not an error. PRD SS7.2 item 11.
        Assert.Empty(QuotaOrdering.InProviderOrder(Array.Empty<QuotaWindow>()));
    }

    [Fact]
    public void DisplayLabel_UsesTheLabel_WhenOneExists()
    {
        Assert.Equal("5 hour", QuotaOrdering.DisplayLabel(QuotaWindowTests.Window(id: "five_hour") with { Label = "5 hour" }));
    }

    [Fact]
    public void DisplayLabel_FallsBackToTheIdentifier_WhenTheLabelIsBlank()
    {
        // Never render an empty row. The raw identifier is always better than nothing.
        QuotaWindow window = QuotaWindowTests.Window(id: "codex") with { Label = "   " };

        Assert.Equal("codex", QuotaOrdering.DisplayLabel(window));
    }

    [Fact]
    public void DisplayLabel_NeverInventsALabel_ForAnUnknownWindow()
    {
        QuotaWindow window = QuotaWindowTests.Window(id: "nimbus_quill")
            with { Label = "nimbus_quill", LabelIsProviderToken = true };

        Assert.Equal("nimbus_quill", QuotaOrdering.DisplayLabel(window));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/AiUsageMonitor.Domain.Tests --filter FullyQualifiedName~QuotaOrderingTests`
Expected: **compilation failure** — `CS0103: The name 'QuotaOrdering' does not exist in the current context`.

- [ ] **Step 3: Write the implementation**

`src/AiUsageMonitor.Domain/QuotaOrdering.cs`:

```csharp
namespace AiUsageMonitor.Domain;

/// <summary>Provider-neutral presentation rules for a set of quota windows.</summary>
public static class QuotaOrdering
{
    /// <summary>
    /// Returns windows in the order the provider reported them. They are never re-sorted by
    /// duration or by countdown: verification observed a seven-day window resetting sooner than a
    /// five-hour window on the same account, so any duration-derived ordering is simply wrong
    /// (PRD SS7.3). OrderBy is a stable sort, so equal Order values keep their input sequence.
    /// </summary>
    public static IReadOnlyList<QuotaWindow> InProviderOrder(IEnumerable<QuotaWindow> windows) =>
        windows.OrderBy(w => w.Order).ToList();

    /// <summary>
    /// The label to render. Falls back to the provider's raw identifier when no label was
    /// derived — never an empty string, and never an invented name (PRD SS13).
    /// </summary>
    public static string DisplayLabel(QuotaWindow window) =>
        string.IsNullOrWhiteSpace(window.Label) ? window.Id : window.Label;
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/AiUsageMonitor.Domain.Tests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/AiUsageMonitor.Domain/QuotaOrdering.cs tests/AiUsageMonitor.Domain.Tests/QuotaOrderingTests.cs
git commit -m "feat: add quota ordering and label fallback

Provider order is authoritative. Windows are never re-sorted by duration
or countdown, because a seven-day window was observed resetting sooner
than a five-hour one on the same account.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 10: Retire the inline self-tests

**Files:**
- Modify: `src/AiUsageMonitor.Poc/Program.cs` (remove `PrintDuckTypedSelfTest` and `PrintClaudeFixtureTest` and their call sites)
- Delete: `src/AiUsageMonitor.Poc/Providers/Claude/ClaudeFixtures.cs`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: the test project from Tasks 4–9.
- Produces: a POC harness that does one job — probing live providers. No later task depends on this.

The self-tests inside `Program.cs` are now duplicated by real tests that CI can enforce. Keeping both means two copies of the same assertions drifting apart. `fixtures/claude-statusline-sample.json` **stays** — it is now consumed by the test project.

- [ ] **Step 1: Remove the self-test call sites**

In `src/AiUsageMonitor.Poc/Program.cs`, delete the call to `PrintDuckTypedSelfTest()` at line 28 and the call to `PrintClaudeFixtureTest()` that follows it.

- [ ] **Step 2: Delete the self-test methods**

Delete `PrintDuckTypedSelfTest()` (lines 203–241) and `PrintClaudeFixtureTest()` (lines 243–274) in full.

- [ ] **Step 3: Delete the now-unused fixture constant**

```bash
git rm src/AiUsageMonitor.Poc/Providers/Claude/ClaudeFixtures.cs
```

- [ ] **Step 4: Remove any now-unused usings**

Run `dotnet build` and fix whatever it reports. `TreatWarningsAsErrors` turns an unused `using` into a build failure, so the compiler will find them for you. In particular `using AiUsageMonitor.Poc.Providers.Claude;` in `Program.cs` may now be unused.

- [ ] **Step 5: Verify the harness still probes live providers**

Run: `dotnet run --project src/AiUsageMonitor.Poc`
Expected: the provider report prints for both Codex and Claude Code, with no self-test sections, and the process exits 0.

- [ ] **Step 6: Update `CLAUDE.md`**

Two edits.

In the Commands section, add the test command:

```markdown
```powershell
dotnet build                                   # warnings are errors — must be clean
dotnet test                                    # domain unit tests
dotnet run --project src/AiUsageMonitor.Poc    # runs all provider probes, prints a report
powershell -File build/publish.ps1             # single self-contained .exe (~65 MB)
```
```

Then delete the sentence `There is no test project yet. When one is added, prefer \`dotnet test --filter FullyQualifiedName~<Name>\` for a single test.` and replace it with:

```markdown
Domain tests live in `tests/AiUsageMonitor.Domain.Tests`. For a single test, prefer `dotnet test --filter FullyQualifiedName~<Name>`.
```

In the Claude Code section, replace the final sentence of the statusLine paragraph — `The recorded sample and its parser test (\`ClaudeFixtures.cs\`, run from \`Program.cs\`), are kept solely as regression coverage...` — with:

```markdown
The recorded sample in `fixtures/claude-statusline-sample.json` is kept solely as regression coverage for the duck-typed extractor's `used_percentage` dialect, asserted in `DuckTypedQuotaExtractorTests`, not as evidence the mechanism is supported.
```

- [ ] **Step 7: Run everything one final time**

```bash
dotnet build
dotnet test
```

Expected: build succeeds with 0 warnings; all tests pass.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "refactor: retire inline self-tests in favour of the test project

The Console.WriteLine self-tests are now duplicated by enforced tests.
Keeping both would let two copies of the same assertions drift. The
recorded fixture stays - the test project consumes it directly.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Done when

- `dotnet build` succeeds with 0 warnings.
- `dotnet test` passes.
- `powershell -File build/publish.ps1` produces exactly one `.exe` that launches.
- `dotnet run --project src/AiUsageMonitor.Poc` still probes both live providers and exits 0.
- `git status` is clean and `git add` produces no CRLF warnings.

## Not in this increment

- Any UI. The design brief (`docs/design/design-prompt.md`) has not been run, so `AiUsageMonitor.App` stays an empty shell.
- Moving the provider adapters out of the POC into an infrastructure project. They work, they are verified, and PRD §26 step 7 places that after the MVVM foundation exists.
- MVVM, dependency injection, logging, settings storage, diagnostics — PRD §26 step 4, next increment.
- `PublishReadyToRun`. Measured at +4.6 MB for faster startup; revisit once there is a real startup path to measure.
