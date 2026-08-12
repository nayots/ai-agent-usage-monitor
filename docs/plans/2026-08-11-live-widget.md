# Live Widget Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the token gallery with the real default widget — a 360px-wide window showing one provider card per discovered provider, each with its live quota rows, fed by the Claude Code and Codex probes that until now only ran in the console POC.

**Architecture:** The two provider adapters move out of `AiUsageMonitor.Poc` into `AiUsageMonitor.Infrastructure` (PRD §21 puts provider integrations in the infrastructure layer); the POC keeps working by referencing them. A `ProviderRefreshService` polls every registered probe concurrently, isolated, timeout-bounded and with backoff, and raises one event per provider as each answers. A hand-rolled MVVM layer in `AiUsageMonitor.App` (`MainViewModel` → `ProviderCardViewModel` → `QuotaRowViewModel`) turns each `ProviderSnapshot` into display strings, and three XAML views render them using the controls and theme tokens the previous increment shipped. A new `net10.0-windows` test project covers the view models **and loads the real XAML**, closing the gap that let a launch-breaking defect ship green.

**Tech Stack:** C# / .NET 10, WPF (`net10.0-windows`), `System.Text.Json`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, xUnit 2.9.3. No new third-party packages — the MVVM primitives are ~60 lines and hand-rolled, matching how the settings store, logger and theming were done.

## Global Constraints

Every task's requirements implicitly include this section. These are copied from `docs/PRD.md`, `CLAUDE.md` and `docs/design/tokens.md`; they are requirements, not preferences.

- **`dotnet build` must be clean.** `TreatWarningsAsErrors=true` is set in `Directory.Build.props`. A warning is a build failure.
- **`AiUsageMonitor.Domain` keeps zero `PackageReference`.** It is provider-neutral: models, states, extraction, formatting. Nothing else.
- **Never add `PublishTrimmed`.** WPF hard-errors with NETSDK1168.
- **No hardcoded user paths.** Resolve per-user locations at runtime via `Environment.GetFolderPath`. Someone who is not the author, on a machine that is not the author's, must be able to run the release artifact.
- **No administrator privileges**, ever, for any operation.
- **Missing data is `null` and surfaces as absence** — never as `0`, never as `--`, never as an em-dash. A rendered placeholder is indistinguishable from real data at a glance (PRD §4.3).
- **No property may be named after a plan period.** No `FiveHourQuota`, no `WeeklyQuota`. Quota windows are discovered; their count, names and durations are never assumed.
- **Never log, persist, cache, display or copy a provider credential.** Never place one in `Extra`, an exception message, a log line or a diagnostic dump. This application never refreshes or rewrites a credential — token lifecycle stays the provider's job.
- **No website scraping, browser automation, cookie or browser-profile access, telemetry, analytics, or third-party transmission.** The only permitted network destination is a provider's own first-party host, already hardcoded in its probe.
- **Every mechanism carries a visible tier.** A value obtained through an unofficial mechanism must never be presented as official. Claude Code is **Unofficial**; Codex is **Official**.
- **One provider failing must never affect the other or crash the process.** Provider operations are async, cancellable and timeout-bounded.
- **Use `System.Text.Json`.** Keep dependencies minimal and justified.
- **Token values are transcribed verbatim from `docs/design/tokens.md`.** Never re-derive a colour, size, radius or spacing value. If a value is not in `tokens.md` or in `src/AiUsageMonitor.App/Themes/Tokens.xaml`, it is not a token — ask rather than invent.
- **No Claude Design markup is compiled into the application.** `docs/design/*.html` is a visual reference that engineering reads; it is never a source of shipping code.
- **The application's own copy is en-US.** The setting is `ColorBarsByUsage`, labelled "Color bars by usage". The design render spells it "Colour"; that one word is the only permitted copy divergence.
- **Windows-only.** The primary shell is PowerShell 5.1 — no `&&`, no ternary. A Bash tool is also available and takes POSIX syntax.

## Reference reading

Read these before the task that needs them. Do not re-derive anything they record.

| When | Read |
|---|---|
| Any task | `CLAUDE.md` — architecture rules, provider mechanisms, hard constraints |
| Tasks 1–2 | `docs/PRD.md` §11 (Claude Code), §12 (Codex), §21 (architecture) |
| Task 4 | `docs/PRD.md` §14 (refresh and freshness), §24 (reliability and error recovery) |
| Tasks 7–9 | `docs/PRD.md` §9, §15 (UI requirements), §16 + §16.1 (bars, markers, tone), §18 (stale) |
| Tasks 10–11 | `docs/design/tokens.md` (all of it), `docs/design/rationale.md`, and `docs/design/ProviderCard.dc.html` + `QuotaRow.dc.html` for structure |
| Task 6 | `docs/PRD.md` §25 (testing requirements) |

## File Structure

| File | Responsibility |
|---|---|
| `src/AiUsageMonitor.Infrastructure/Providers/ProcessRunner.cs` | Moved from POC. Launches a provider executable directly with a hard timeout. |
| `src/AiUsageMonitor.Infrastructure/Providers/Codex/CodexProbe.cs` | Moved from POC. Codex JSON-RPC probe. |
| `src/AiUsageMonitor.Infrastructure/Providers/Claude/ClaudeOAuthUsageProbe.cs` | Moved from POC, plus executable discovery and version. |
| `src/AiUsageMonitor.Infrastructure/Providers/Claude/ClaudeExecutableLocator.cs` | Ordered candidate paths for `claude.exe`, pure and testable. |
| `src/AiUsageMonitor.Infrastructure/Providers/ProviderDescriptor.cs` | Display identity (name, monogram) paired with a probe. |
| `src/AiUsageMonitor.Infrastructure/Providers/ProviderRegistry.cs` | The list of providers this build knows about, in card order. |
| `src/AiUsageMonitor.Infrastructure/Refresh/ProviderRefreshService.cs` | Concurrent, isolated, timeout-bounded polling with per-provider backoff. |
| `src/AiUsageMonitor.Domain/RelativeTime.cs` | "12s ago" / "6 minutes ago" age formatting. |
| `src/AiUsageMonitor.App/ViewModels/ObservableObject.cs` | Minimal `INotifyPropertyChanged` base. |
| `src/AiUsageMonitor.App/ViewModels/RelayCommand.cs` | Minimal `ICommand`. |
| `src/AiUsageMonitor.App/ViewModels/QuotaRowViewModel.cs` | One quota window as display strings. |
| `src/AiUsageMonitor.App/ViewModels/ProviderCardViewModel.cs` | One provider card: identity, state, rows, notice. |
| `src/AiUsageMonitor.App/ViewModels/ProviderNotice.cs` | The empty / waiting / unsupported / unavailable / error copy, selected from state. |
| `src/AiUsageMonitor.App/ViewModels/ConnectionStateText.cs` | `ConnectionState` → its display word. |
| `src/AiUsageMonitor.App/ViewModels/MainViewModel.cs` | The provider list, footer, refresh command, local tick. |
| `src/AiUsageMonitor.App/Views/QuotaRowView.xaml` | The three-column quota row with bar and marker. |
| `src/AiUsageMonitor.App/Views/ProviderCardView.xaml` | Header, status, stale banner, captions, rows, notice. |
| `src/AiUsageMonitor.App/Views/WidgetWindow.xaml` | Title bar, scrolling provider list, footer. Replaces `MainWindow`. |
| `src/AiUsageMonitor.App/Interop/DwmWindowChrome.cs` | Rounded corners and dark border via DWM, degrading silently. |
| `tests/AiUsageMonitor.App.Tests/` | View-model tests **and** XAML-loading tests, on a single STA thread. |

---

### Task 1: Move the provider adapters into Infrastructure

The probes are provider integrations, which PRD §21 places in the infrastructure layer. Today they live in the POC, so the widget cannot reach them without depending on a console app. Move them; the POC keeps working by referencing Infrastructure.

**Files:**
- Create: `src/AiUsageMonitor.Infrastructure/Providers/ProcessRunner.cs` (content moved from `src/AiUsageMonitor.Poc/Providers/ProcessRunner.cs`)
- Create: `src/AiUsageMonitor.Infrastructure/Providers/Codex/CodexProbe.cs` (content moved from `src/AiUsageMonitor.Poc/Providers/Codex/CodexProbe.cs`)
- Create: `src/AiUsageMonitor.Infrastructure/Providers/Claude/ClaudeOAuthUsageProbe.cs` (content moved from `src/AiUsageMonitor.Poc/Providers/Claude/ClaudeOAuthUsageProbe.cs`)
- Create: `src/AiUsageMonitor.Infrastructure/Providers/ProviderDescriptor.cs`
- Create: `src/AiUsageMonitor.Infrastructure/Providers/ProviderRegistry.cs`
- Delete: `src/AiUsageMonitor.Poc/Providers/ProcessRunner.cs`, `src/AiUsageMonitor.Poc/Providers/Codex/CodexProbe.cs`, `src/AiUsageMonitor.Poc/Providers/Claude/ClaudeOAuthUsageProbe.cs`
- Modify: `src/AiUsageMonitor.Poc/AiUsageMonitor.Poc.csproj`, `src/AiUsageMonitor.Poc/Program.cs`
- Test: `tests/AiUsageMonitor.Infrastructure.Tests/ProviderRegistryTests.cs`

**Interfaces:**
- Consumes: `IProviderProbe`, `ProviderSnapshot` from `AiUsageMonitor.Domain`.
- Produces: `ProviderDescriptor(string DisplayName, string Monogram, IProviderProbe Probe)`; `ProviderRegistry.CreateDefault()` returning `IReadOnlyList<ProviderDescriptor>`. Tasks 4, 9 and 11 consume both.

- [ ] **Step 1: Move the three files, changing only the namespace**

Move each file's *content* verbatim. The `namespace` line is the only edit permitted in this step.

| File | Old namespace | New namespace |
|---|---|---|
| `ProcessRunner.cs` | `AiUsageMonitor.Poc.Providers` | `AiUsageMonitor.Infrastructure.Providers` |
| `Codex/CodexProbe.cs` | `AiUsageMonitor.Poc.Providers.Codex` | `AiUsageMonitor.Infrastructure.Providers.Codex` |
| `Claude/ClaudeOAuthUsageProbe.cs` | `AiUsageMonitor.Poc.Providers.Claude` | `AiUsageMonitor.Infrastructure.Providers.Claude` |

`ProcessRunner` is `internal static` and must stay that way. Its only callers are `CodexProbe` today and `ClaudeExecutableLocator` after Task 2 — both land in this same assembly, and the POC never calls it. Do not widen it to `public`. `CodexProbe` and `ClaudeOAuthUsageProbe` are already `public` and must stay so: the POC calls them across the assembly boundary now.

Do **not** otherwise reword, reformat, or "improve" the moved code in this step. Its comments record empirically verified provider behaviour and are load-bearing.

- [ ] **Step 2: Add the descriptor**

Create `src/AiUsageMonitor.Infrastructure/Providers/ProviderDescriptor.cs`:

```csharp
using AiUsageMonitor.Domain;

namespace AiUsageMonitor.Infrastructure.Providers;

/// <summary>
/// A provider's display identity paired with the probe that speaks to it. The monogram is
/// registered explicitly rather than derived: initials would give "Codex" a single "C", and the
/// approved design uses "CX". Adding a provider is one entry in <see cref="ProviderRegistry"/>
/// plus its probe — no change to any view or view model (PRD §21).
/// </summary>
public sealed record ProviderDescriptor(string DisplayName, string Monogram, IProviderProbe Probe);
```

- [ ] **Step 3: Add the registry**

Create `src/AiUsageMonitor.Infrastructure/Providers/ProviderRegistry.cs`:

```csharp
using AiUsageMonitor.Infrastructure.Providers.Claude;
using AiUsageMonitor.Infrastructure.Providers.Codex;

namespace AiUsageMonitor.Infrastructure.Providers;

/// <summary>Every provider this build knows how to probe, in the order their cards are laid out.</summary>
public static class ProviderRegistry
{
    public static IReadOnlyList<ProviderDescriptor> CreateDefault() =>
    [
        new("Claude Code", "CC", new ClaudeOAuthUsageProbe()),
        new("Codex", "CX", new CodexProbe())
    ];
}
```

- [ ] **Step 4: Align the Claude probe's name with its display name**

In `ClaudeOAuthUsageProbe`, change:

```csharp
public string Name => "Claude Code (OAuth usage endpoint)";
```

to:

```csharp
public string Name => "Claude Code";
```

The mechanism detail is not lost — it already lives in the `Mechanism` constant, which is what the tier badge and diagnostics read. Change nothing else.

- [ ] **Step 5: Point the POC at Infrastructure**

In `src/AiUsageMonitor.Poc/AiUsageMonitor.Poc.csproj`, add to the existing `ItemGroup` holding `ProjectReference`:

```xml
<ProjectReference Include="..\AiUsageMonitor.Infrastructure\AiUsageMonitor.Infrastructure.csproj" />
```

In `src/AiUsageMonitor.Poc/Program.cs`, change the two provider `using` directives to the new namespaces:

```csharp
using AiUsageMonitor.Infrastructure.Providers.Claude;
using AiUsageMonitor.Infrastructure.Providers.Codex;
```

Change nothing else in `Program.cs`. The POC must still run and print the same report — it is the live harness that proves the mechanisms work.

- [ ] **Step 6: Write the drift test**

Create `tests/AiUsageMonitor.Infrastructure.Tests/ProviderRegistryTests.cs`:

```csharp
using AiUsageMonitor.Infrastructure.Providers;

namespace AiUsageMonitor.Infrastructure.Tests;

public class ProviderRegistryTests
{
    [Fact]
    public void RegistersBothProviders()
    {
        IReadOnlyList<ProviderDescriptor> providers = ProviderRegistry.CreateDefault();

        Assert.Equal(["Claude Code", "Codex"], providers.Select(p => p.DisplayName));
    }

    [Fact]
    public void EveryDescriptorMatchesItsProbesOwnName()
    {
        // A descriptor whose display name has drifted from the probe's would put one name on the
        // card and a different one in diagnostics for the same provider.
        foreach (ProviderDescriptor provider in ProviderRegistry.CreateDefault())
        {
            Assert.Equal(provider.DisplayName, provider.Probe.Name);
        }
    }

    [Fact]
    public void EveryDescriptorHasAMonogram()
    {
        foreach (ProviderDescriptor provider in ProviderRegistry.CreateDefault())
        {
            Assert.False(string.IsNullOrWhiteSpace(provider.Monogram));
        }
    }
}
```

- [ ] **Step 7: Build and test**

Run: `dotnet build`
Expected: 0 warnings, 0 errors.

Run: `dotnet test`
Expected: all existing tests plus 3 new ones pass.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "refactor: move provider adapters into the infrastructure layer"
```

---

### Task 2: Discover the Claude Code executable and its version

Today the Claude probe treats "the credentials file exists" as "installed", which is wrong in both directions: a machine that once signed in but has since uninstalled reads as installed, and the card can never show a version. Claude Code ships a real executable and `claude --version` is an official local mechanism (PRD §5).

Verified on Windows 11 for Claude Code 2.1.227: `%USERPROFILE%\.local\bin\claude.exe --version` prints `2.1.227 (Claude Code)` in ~130 ms.

**Files:**
- Create: `src/AiUsageMonitor.Infrastructure/Providers/Claude/ClaudeExecutableLocator.cs`
- Modify: `src/AiUsageMonitor.Infrastructure/Providers/Claude/ClaudeOAuthUsageProbe.cs`
- Test: `tests/AiUsageMonitor.Infrastructure.Tests/ClaudeExecutableLocatorTests.cs`

**Interfaces:**
- Consumes: `ProcessRunner.RunCapturedAsync` from Task 1.
- Produces: `ClaudeExecutableLocator.CandidatePaths(...)` and `ClaudeExecutableLocator.ParseVersion(string?)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/AiUsageMonitor.Infrastructure.Tests/ClaudeExecutableLocatorTests.cs`:

```csharp
using AiUsageMonitor.Infrastructure.Providers.Claude;

namespace AiUsageMonitor.Infrastructure.Tests;

public class ClaudeExecutableLocatorTests
{
    [Fact]
    public void NativeInstallLocationIsCheckedFirst()
    {
        IReadOnlyList<string> candidates = ClaudeExecutableLocator.CandidatePaths(
            userProfile: @"C:\Users\someone",
            appData: @"C:\Users\someone\AppData\Roaming",
            pathEnvironment: null);

        Assert.Equal(@"C:\Users\someone\.local\bin\claude.exe", candidates[0]);
    }

    [Fact]
    public void NpmGlobalShimIsCheckedAfterTheNativeInstall()
    {
        IReadOnlyList<string> candidates = ClaudeExecutableLocator.CandidatePaths(
            userProfile: @"C:\Users\someone",
            appData: @"C:\Users\someone\AppData\Roaming",
            pathEnvironment: null);

        Assert.Equal(@"C:\Users\someone\AppData\Roaming\npm\claude.cmd", candidates[1]);
    }

    [Fact]
    public void EveryPathDirectoryContributesBothExecutableForms()
    {
        IReadOnlyList<string> candidates = ClaudeExecutableLocator.CandidatePaths(
            userProfile: @"C:\Users\someone",
            appData: @"C:\Users\someone\AppData\Roaming",
            pathEnvironment: @"C:\tools;;  ;C:\other");

        Assert.Contains(@"C:\tools\claude.exe", candidates);
        Assert.Contains(@"C:\tools\claude.cmd", candidates);
        Assert.Contains(@"C:\other\claude.exe", candidates);
        Assert.Contains(@"C:\other\claude.cmd", candidates);
    }

    [Fact]
    public void BlankPathEntriesAreSkipped()
    {
        IReadOnlyList<string> candidates = ClaudeExecutableLocator.CandidatePaths(
            userProfile: @"C:\Users\someone",
            appData: @"C:\Users\someone\AppData\Roaming",
            pathEnvironment: @";  ;");

        Assert.Equal(2, candidates.Count);
    }

    [Theory]
    [InlineData("2.1.227 (Claude Code)\r\n", "2.1.227")]
    [InlineData("2.1.227\n", "2.1.227")]
    [InlineData("  2.1.227 (Claude Code)  ", "2.1.227")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void VersionIsTheFirstTokenOfTheFirstLine(string? stdout, string? expected) =>
        Assert.Equal(expected, ClaudeExecutableLocator.ParseVersion(stdout));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~ClaudeExecutableLocatorTests`
Expected: FAIL — `ClaudeExecutableLocator` does not exist.

- [ ] **Step 3: Write the locator**

Create `src/AiUsageMonitor.Infrastructure/Providers/Claude/ClaudeExecutableLocator.cs`:

```csharp
namespace AiUsageMonitor.Infrastructure.Providers.Claude;

/// <summary>
/// Finds the local Claude Code executable. The candidate list is pure and separately testable
/// because the ordering is the part that can silently regress; only <see cref="Locate"/> touches
/// the filesystem. Every location is resolved per-user at runtime — the release artifact has to
/// run on a machine that is not the author's.
/// </summary>
public static class ClaudeExecutableLocator
{
    /// <summary>
    /// Candidates in priority order: the native installer's location first (verified on Windows 11
    /// for Claude Code 2.1.227), then the npm global shim, then PATH. Both executable forms are
    /// tried for PATH entries because an npm install puts a <c>.cmd</c> shim there, not an
    /// <c>.exe</c>.
    /// </summary>
    public static IReadOnlyList<string> CandidatePaths(string userProfile, string appData, string? pathEnvironment)
    {
        List<string> candidates =
        [
            Path.Combine(userProfile, ".local", "bin", "claude.exe"),
            Path.Combine(appData, "npm", "claude.cmd")
        ];

        foreach (string directory in (pathEnvironment ?? string.Empty).Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            candidates.Add(Path.Combine(directory.Trim(), "claude.exe"));
            candidates.Add(Path.Combine(directory.Trim(), "claude.cmd"));
        }

        return candidates;
    }

    /// <summary>The first candidate that exists, or null when Claude Code is not installed here.</summary>
    public static string? Locate()
    {
        IReadOnlyList<string> candidates = CandidatePaths(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetEnvironmentVariable("PATH"));

        foreach (string candidate in candidates)
        {
            try
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An unreadable PATH entry is not a reason to stop looking at the rest.
            }
        }

        return null;
    }

    /// <summary>
    /// "2.1.227 (Claude Code)" -> "2.1.227". The first whitespace-delimited token of the first
    /// line, or null: an unparseable banner leaves the version absent rather than displaying
    /// whatever the executable happened to print.
    /// </summary>
    public static string? ParseVersion(string? standardOutput)
    {
        if (string.IsNullOrWhiteSpace(standardOutput))
        {
            return null;
        }

        string firstLine = standardOutput.Split('\n')[0].Trim();
        string[] tokens = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length == 0 ? null : tokens[0];
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~ClaudeExecutableLocatorTests`
Expected: PASS (10 cases).

- [ ] **Step 5: Use the locator in the probe**

In `ClaudeOAuthUsageProbe`, add a version timeout constant beside the existing constants:

```csharp
private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(10);
```

Then change the beginning of `ProbeAsync` so that discovery happens *before* anything else — in particular before the credential file is opened. An uninstalled provider must produce `NotInstalled` without reading a credential at all:

```csharp
public async Task<ProviderSnapshot> ProbeAsync(CancellationToken ct)
{
    var notes = new List<string>();

    string? exePath = ClaudeExecutableLocator.Locate();
    if (exePath is null)
    {
        notes.Add("No local claude executable found (checked %USERPROFILE%\\.local\\bin, the npm global shim, and PATH).");
        return new ProviderSnapshot(
            ProviderName: Name,
            Installed: false,
            Version: null,
            ExecutablePath: null,
            State: ConnectionState.NotInstalled,
            Mechanism: "no local claude executable found",
            Tier: MechanismTier.Unofficial,
            UpdateModel: "unavailable",
            Windows: [],
            RetrievedAt: null,
            Error: null,
            Notes: notes);
    }

    string? version = await TryGetVersionAsync(exePath, ct, notes).ConfigureAwait(false);

    string credentialsPath = GetCredentialsPath();
    bool credentialsFileExists = File.Exists(credentialsPath);

    // ... existing body from `string? token = ReadAccessToken(...)` onward, unchanged
```

Add the version helper, modelled on the Codex probe's:

```csharp
private static async Task<string?> TryGetVersionAsync(string exePath, CancellationToken ct, List<string> notes)
{
    try
    {
        (int exitCode, string stdOut, _) =
            await ProcessRunner.RunCapturedAsync(exePath, "--version", VersionTimeout, ct).ConfigureAwait(false);

        string? version = exitCode == 0 ? ClaudeExecutableLocator.ParseVersion(stdOut) : null;
        notes.Add(version is null
            ? $"claude --version exited {exitCode} without a parseable version."
            : $"Version reported by the official `claude --version` command: {version}.");
        return version;
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
        // ProcessRunner signals its OWN timeout as OperationCanceledException - it links the
        // caller's token to a CancelAfter(VersionTimeout) source. A slow or hung executable is
        // not a shutdown: the version is simply unknown, and the quota read below must still
        // happen. Letting this escape would throw the whole probe out, which breaks the
        // timeout-bounded and one-provider-cannot-affect-the-other constraints (PRD §24).
        notes.Add($"claude --version did not complete within {VersionTimeout.TotalSeconds:0}s.");
        return null;
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        // Caller-requested cancellation is deliberately NOT caught here: it propagates so the
        // refresh service can tell shutdown apart from a provider failure.
        notes.Add($"claude --version failed: {ex.Message}");
        return null;
    }
}
```

- [ ] **Step 6: Carry the executable path and version into every snapshot**

The private `Snapshot(...)` helper hardcodes `Version: null` and `ExecutablePath: null`. Give it both as parameters and pass them at each of its call sites:

```csharp
private ProviderSnapshot Snapshot(
    bool installed,
    string? version,
    string? executablePath,
    ConnectionState state,
    IReadOnlyList<QuotaWindow> windows,
    DateTimeOffset? retrievedAt,
    string? error,
    List<string> notes) =>
    new(
        ProviderName: Name,
        Installed: installed,
        Version: version,
        ExecutablePath: executablePath,
        State: state,
        Mechanism: Mechanism,
        Tier: MechanismTier.Unofficial,
        UpdateModel: UpdateModel,
        Windows: windows,
        RetrievedAt: retrievedAt,
        Error: error,
        Notes: notes);
```

- [ ] **Step 7: Give the missing-credential path a stated reason**

The "installed but never signed in" case currently returns `Unavailable` with `Error: null`, which leaves the card with no explanation to show. PRD §18 requires the availability reason when it can be shown safely. Change that one return to:

```csharp
    if (token is null)
    {
        // Missing file / missing claudeAiOauth.accessToken -> Unavailable, never an exception.
        return Snapshot(
            installed: true,
            version,
            exePath,
            ConnectionState.Unavailable,
            [],
            null,
            "Claude Code is installed but has not stored a sign-in on this machine.",
            notes);
    }
```

This string states a state, not a secret. It must not name the credential file's path — `notes` already records that, and notes are diagnostics, not card copy.

- [ ] **Step 8: Build and test**

Run: `dotnet build`
Expected: 0 warnings, 0 errors.

Run: `dotnet test`
Expected: all pass.

- [ ] **Step 9: Verify against the real installation**

Run: `dotnet run --project src/AiUsageMonitor.Poc`
Expected: the Claude Code section reports `Installed: True`, a version like `2.1.227`, an executable path, and the same quota windows it reported before this task. If Claude Code is not installed on the machine running this, expect `NotInstalled` with no credential read — that is also a pass.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat: discover the Claude Code executable and read its version"
```

---

### Task 3: Relative age formatting

Cards show "Updated 12s ago" and stale banners show "Last successful update 6 minutes ago". That is one formatting rule, provider-neutral, and it belongs beside the other quota formatting in the domain.

**Files:**
- Create: `src/AiUsageMonitor.Domain/RelativeTime.cs`
- Test: `tests/AiUsageMonitor.Domain.Tests/RelativeTimeTests.cs`

**Interfaces:**
- Produces: `RelativeTime.FormatAge(TimeSpan?)` returning `string?`. Tasks 8 and 9 consume it.

- [ ] **Step 1: Write the failing tests**

Create `tests/AiUsageMonitor.Domain.Tests/RelativeTimeTests.cs`:

```csharp
using AiUsageMonitor.Domain;

namespace AiUsageMonitor.Domain.Tests;

public class RelativeTimeTests
{
    [Fact]
    public void NullAgeFormatsAsNullSoTheCallerOmitsTheElement() =>
        Assert.Null(RelativeTime.FormatAge(null));

    [Theory]
    [InlineData(0, "0s ago")]
    [InlineData(12, "12s ago")]
    [InlineData(59, "59s ago")]
    public void SecondsUnderAMinute(int seconds, string expected) =>
        Assert.Equal(expected, RelativeTime.FormatAge(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(60, "1 minute ago")]
    [InlineData(119, "1 minute ago")]
    [InlineData(360, "6 minutes ago")]
    [InlineData(3599, "59 minutes ago")]
    public void MinutesUnderAnHour(int seconds, string expected) =>
        Assert.Equal(expected, RelativeTime.FormatAge(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(1, "1 hour ago")]
    [InlineData(2, "2 hours ago")]
    [InlineData(23, "23 hours ago")]
    public void HoursUnderADay(int hours, string expected) =>
        Assert.Equal(expected, RelativeTime.FormatAge(TimeSpan.FromHours(hours)));

    [Theory]
    [InlineData(24, "1 day ago")]
    [InlineData(72, "3 days ago")]
    public void DaysAndAbove(int hours, string expected) =>
        Assert.Equal(expected, RelativeTime.FormatAge(TimeSpan.FromHours(hours)));

    [Fact]
    public void FutureAgesClampToZeroRatherThanRenderingNegative()
    {
        // Clock skew, DST transitions and resume-from-sleep all produce a future timestamp.
        Assert.Equal("0s ago", RelativeTime.FormatAge(TimeSpan.FromSeconds(-30)));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~RelativeTimeTests`
Expected: FAIL — `RelativeTime` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/AiUsageMonitor.Domain/RelativeTime.cs`:

```csharp
using System.Globalization;

namespace AiUsageMonitor.Domain;

/// <summary>
/// Renders how long ago something happened. Returns null for a null age so a caller omits the
/// element entirely rather than rendering a placeholder that reads like data (PRD §4.3).
/// </summary>
public static class RelativeTime
{
    public static string? FormatAge(TimeSpan? age)
    {
        if (age is not TimeSpan span)
        {
            return null;
        }

        // Clock skew, DST transitions and resume-from-sleep all produce future timestamps.
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        if (span.TotalMinutes < 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalSeconds}s ago");
        }

        if (span.TotalHours < 1)
        {
            return Plural((int)span.TotalMinutes, "minute");
        }

        return span.TotalDays < 1
            ? Plural((int)span.TotalHours, "hour")
            : Plural((int)span.TotalDays, "day");
    }

    private static string Plural(int count, string unit) => count == 1
        ? string.Create(CultureInfo.InvariantCulture, $"1 {unit} ago")
        : string.Create(CultureInfo.InvariantCulture, $"{count} {unit}s ago");
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~RelativeTimeTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add relative age formatting to the domain"
```

---

### Task 4: The provider refresh service

Polling is the update model for both providers (verified — Codex's `accountRateLimitsUpdated` notifications only fire during an active model turn, so they are useless to an observer). This service is the one place that decides when to ask, how long to wait, and what to do when a provider keeps failing.

**Files:**
- Create: `src/AiUsageMonitor.Infrastructure/Refresh/ProviderRefreshService.cs`
- Test: `tests/AiUsageMonitor.Infrastructure.Tests/ProviderRefreshServiceTests.cs`

**Interfaces:**
- Consumes: `ProviderDescriptor`, `ProviderRegistry` (Task 1); `IProviderProbe`, `ProviderSnapshot`, `ConnectionState`, `MechanismTier` from the domain.
- Produces:
  - `sealed record ProviderRefreshed(ProviderDescriptor Provider, ProviderSnapshot Snapshot)`
  - `ProviderRefreshService(IReadOnlyList<ProviderDescriptor> providers, TimeSpan timeout, TimeSpan baseInterval, ILogger<ProviderRefreshService>? logger = null)`
  - `event EventHandler<ProviderRefreshed>? Refreshed`
  - `Task RefreshAllAsync(bool force, DateTimeOffset now, CancellationToken ct)`
  - `Task RefreshAsync(ProviderDescriptor provider, DateTimeOffset now, CancellationToken ct)`
  - `static TimeSpan BackoffFor(int consecutiveFailures, TimeSpan baseInterval)`

  Task 9 consumes all of these.

- [ ] **Step 1: Write the failing tests**

Create `tests/AiUsageMonitor.Infrastructure.Tests/ProviderRefreshServiceTests.cs`:

```csharp
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Refresh;

namespace AiUsageMonitor.Infrastructure.Tests;

public class ProviderRefreshServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeProbe(string name, Func<CancellationToken, Task<ProviderSnapshot>> behaviour) : IProviderProbe
    {
        public int Calls { get; private set; }
        public string Name => name;

        public Task<ProviderSnapshot> ProbeAsync(CancellationToken ct)
        {
            Calls++;
            return behaviour(ct);
        }
    }

    private static ProviderSnapshot Snapshot(string name, ConnectionState state) => new(
        ProviderName: name,
        Installed: true,
        Version: null,
        ExecutablePath: null,
        State: state,
        Mechanism: "fake",
        Tier: MechanismTier.Official,
        UpdateModel: "pull (poll)",
        Windows: [],
        RetrievedAt: state == ConnectionState.Connected ? Now : null,
        Error: null,
        Notes: []);

    private static ProviderDescriptor Descriptor(string name, Func<CancellationToken, Task<ProviderSnapshot>> behaviour) =>
        new(name, name[..1], new FakeProbe(name, behaviour));

    private static ProviderRefreshService Service(params ProviderDescriptor[] providers) =>
        new(providers, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(60));

    [Fact]
    public async Task RaisesOneEventPerProvider()
    {
        ProviderDescriptor a = Descriptor("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Connected)));
        ProviderDescriptor b = Descriptor("Beta", _ => Task.FromResult(Snapshot("Beta", ConnectionState.Connected)));
        ProviderRefreshService service = Service(a, b);

        List<string> seen = [];
        service.Refreshed += (_, e) => { lock (seen) { seen.Add(e.Provider.DisplayName); } };

        await service.RefreshAllAsync(force: true, Now, CancellationToken.None);

        Assert.Equal(["Alpha", "Beta"], seen.Order());
    }

    [Fact]
    public async Task OneProviderThrowingDoesNotStopTheOther()
    {
        ProviderDescriptor bad = Descriptor("Alpha", _ => throw new InvalidOperationException("boom"));
        ProviderDescriptor good = Descriptor("Beta", _ => Task.FromResult(Snapshot("Beta", ConnectionState.Connected)));
        ProviderRefreshService service = Service(bad, good);

        Dictionary<string, ProviderSnapshot> results = [];
        service.Refreshed += (_, e) => { lock (results) { results[e.Provider.DisplayName] = e.Snapshot; } };

        await service.RefreshAllAsync(force: true, Now, CancellationToken.None);

        Assert.Equal(ConnectionState.Error, results["Alpha"].State);
        Assert.Equal(ConnectionState.Connected, results["Beta"].State);
    }

    [Fact]
    public async Task AThrownExceptionNeverEscapesAsAFailedTask()
    {
        ProviderDescriptor bad = Descriptor("Alpha", _ => throw new InvalidOperationException("boom"));

        await Service(bad).RefreshAllAsync(force: true, Now, CancellationToken.None);
    }

    [Fact]
    public async Task AHangingProbeIsCutOffAndReportedAsAnError()
    {
        ProviderDescriptor hanging = Descriptor("Alpha", async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Snapshot("Alpha", ConnectionState.Connected);
        });
        ProviderRefreshService service = Service(hanging);

        ProviderSnapshot? result = null;
        service.Refreshed += (_, e) => result = e.Snapshot;

        await service.RefreshAllAsync(force: true, Now, CancellationToken.None);

        Assert.Equal(ConnectionState.Error, result!.State);
        Assert.Contains("Timed out", result.Error);
    }

    [Fact]
    public async Task CallerCancellationIsNotReportedAsAProviderError()
    {
        using CancellationTokenSource cts = new();
        ProviderDescriptor slow = Descriptor("Alpha", async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Snapshot("Alpha", ConnectionState.Connected);
        });
        ProviderRefreshService service = Service(slow);

        bool raised = false;
        service.Refreshed += (_, _) => raised = true;

        Task refresh = service.RefreshAllAsync(force: true, Now, cts.Token);
        await cts.CancelAsync();
        await refresh;

        Assert.False(raised);
    }

    [Fact]
    public async Task AFailingProviderIsSkippedUntilItsBackoffExpires()
    {
        FakeProbe probe = new("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Error)));
        ProviderDescriptor descriptor = new("Alpha", "A", probe);
        ProviderRefreshService service = Service(descriptor);

        await service.RefreshAllAsync(force: false, Now, CancellationToken.None);
        Assert.Equal(1, probe.Calls);

        await service.RefreshAllAsync(force: false, Now.AddSeconds(1), CancellationToken.None);
        Assert.Equal(1, probe.Calls);

        await service.RefreshAllAsync(force: false, Now.AddSeconds(61), CancellationToken.None);
        Assert.Equal(2, probe.Calls);
    }

    [Fact]
    public async Task AManualRefreshIgnoresBackoff()
    {
        FakeProbe probe = new("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Error)));
        ProviderDescriptor descriptor = new("Alpha", "A", probe);
        ProviderRefreshService service = Service(descriptor);

        await service.RefreshAllAsync(force: false, Now, CancellationToken.None);
        await service.RefreshAllAsync(force: true, Now.AddSeconds(1), CancellationToken.None);

        Assert.Equal(2, probe.Calls);
    }

    [Fact]
    public async Task ASuccessfulRefreshClearsTheBackoff()
    {
        ConnectionState next = ConnectionState.Error;
        FakeProbe probe = new("Alpha", _ => Task.FromResult(Snapshot("Alpha", next)));
        ProviderDescriptor descriptor = new("Alpha", "A", probe);
        ProviderRefreshService service = Service(descriptor);

        await service.RefreshAllAsync(force: false, Now, CancellationToken.None);
        next = ConnectionState.Connected;
        await service.RefreshAllAsync(force: true, Now.AddSeconds(1), CancellationToken.None);

        await service.RefreshAllAsync(force: false, Now.AddSeconds(2), CancellationToken.None);
        Assert.Equal(3, probe.Calls);
    }

    [Fact]
    public void NotInstalledIsAFactNotAFailureSoItIsNeverBackedOff()
    {
        Assert.Equal(TimeSpan.Zero, ProviderRefreshService.BackoffFor(0, TimeSpan.FromSeconds(60)));
    }

    [Theory]
    [InlineData(1, 60)]
    [InlineData(2, 120)]
    [InlineData(3, 240)]
    [InlineData(4, 480)]
    [InlineData(5, 480)]
    [InlineData(9, 480)]
    public void BackoffDoublesAndThenStopsGrowing(int failures, int expectedSeconds) =>
        Assert.Equal(
            TimeSpan.FromSeconds(expectedSeconds),
            ProviderRefreshService.BackoffFor(failures, TimeSpan.FromSeconds(60)));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~ProviderRefreshServiceTests`
Expected: FAIL — `ProviderRefreshService` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/AiUsageMonitor.Infrastructure/Refresh/ProviderRefreshService.cs`:

```csharp
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiUsageMonitor.Infrastructure.Refresh;

/// <summary>One provider's answer, raised as soon as that provider answers rather than at the end of a cycle.</summary>
public sealed record ProviderRefreshed(ProviderDescriptor Provider, ProviderSnapshot Snapshot);

/// <summary>
/// Polls every registered provider. Both providers are pull-based: Codex's rate-limit
/// notifications only fire during an active model turn, which an observer never starts, and the
/// Claude Code endpoint is a request/response call. Nothing here interprets a provider's quota
/// semantics — it decides only when to ask and what to do when asking keeps failing.
/// </summary>
public sealed class ProviderRefreshService
{
    private readonly IReadOnlyList<ProviderDescriptor> _providers;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _baseInterval;
    private readonly ILogger _logger;
    private readonly Dictionary<ProviderDescriptor, Backoff> _backoff = [];
    private readonly Lock _gate = new();

    public ProviderRefreshService(
        IReadOnlyList<ProviderDescriptor> providers,
        TimeSpan timeout,
        TimeSpan baseInterval,
        ILogger<ProviderRefreshService>? logger = null)
    {
        _providers = providers;
        _timeout = timeout;
        _baseInterval = baseInterval;
        _logger = logger ?? NullLogger<ProviderRefreshService>.Instance;
    }

    /// <summary>
    /// Raised on whichever thread the probe completed on. Subscribers that touch UI state must
    /// marshal — this service knows nothing about a dispatcher.
    /// </summary>
    public event EventHandler<ProviderRefreshed>? Refreshed;

    /// <summary>
    /// Delay before a provider that has failed <paramref name="consecutiveFailures"/> times in a
    /// row is asked again: doubling, capped at 8× the base interval. PRD §24 requires repeated
    /// failures to stop aggressive retries while leaving manual refresh available.
    /// </summary>
    public static TimeSpan BackoffFor(int consecutiveFailures, TimeSpan baseInterval) =>
        consecutiveFailures <= 0
            ? TimeSpan.Zero
            : baseInterval * Math.Min(Math.Pow(2, consecutiveFailures - 1), 8);

    /// <summary>
    /// Probes every provider concurrently. Never throws: a provider that fails produces an Error
    /// snapshot, so one provider can never take down the other or the process (PRD §4.5).
    /// </summary>
    public async Task RefreshAllAsync(bool force, DateTimeOffset now, CancellationToken ct)
    {
        List<Task> running = [];

        foreach (ProviderDescriptor provider in _providers)
        {
            if (!force && IsBackedOff(provider, now))
            {
                continue;
            }

            running.Add(RefreshAsync(provider, now, ct));
        }

        await Task.WhenAll(running).ConfigureAwait(false);
    }

    /// <summary>Probes one provider, ignoring its backoff. This is what a manual retry calls.</summary>
    public async Task RefreshAsync(ProviderDescriptor provider, DateTimeOffset now, CancellationToken ct)
    {
        ProviderSnapshot snapshot;

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(_timeout);

        try
        {
            // Raced against the token rather than simply awaited. CancelAfter only *signals*
            // cancellation; a probe that never observes its token would leave a bare await pending
            // forever, making the timeout cooperative rather than real. PRD §24 asks for a bound
            // that holds regardless of how well-behaved the probe is, and this is the isolation
            // boundary for probes that do not exist yet.
            Task<ProviderSnapshot> probing = provider.Probe.ProbeAsync(linked.Token);
            Task settled = await Task
                .WhenAny(probing, Task.Delay(Timeout.InfiniteTimeSpan, linked.Token))
                .ConfigureAwait(false);

            if (!ReferenceEquals(settled, probing))
            {
                Observe(probing, provider);

                if (ct.IsCancellationRequested)
                {
                    return;
                }

                snapshot = Failed(provider, $"Timed out after {_timeout.TotalSeconds:0}s.");
            }
            else
            {
                snapshot = await probing.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The application is shutting down or the user cancelled. Not a provider failure, and
            // not something to report as one - raise nothing and leave the last snapshot standing.
            return;
        }
        catch (OperationCanceledException)
        {
            snapshot = Failed(provider, $"Timed out after {_timeout.TotalSeconds:0}s.");
        }
        catch (Exception ex)
        {
            // A probe is expected to return a state rather than throw. If one throws anyway, the
            // failure stays inside its own card.
            //
            // The split here is deliberate. The local log gets the whole exception, because that is
            // what makes a failing provider diagnosable. The card gets the type name ALONE - never
            // ex.Message - because this catch is the generic backstop for any IProviderProbe,
            // including ones not written yet, and an arbitrary message is exactly the sort of
            // string that can carry something it should not. The same rule already governs the
            // generic catch in ClaudeOAuthUsageProbe.ReadAccessToken; follow it here.
            _logger.LogWarning(ex, "The probe for {Provider} threw instead of returning a state.", provider.DisplayName);
            snapshot = Failed(provider, $"The provider probe failed unexpectedly ({ex.GetType().Name}).");
        }

        Record(provider, snapshot, now);
        RaiseRefreshed(provider, snapshot);
    }

    /// <summary>
    /// Subscribers run synchronously on the thread the probe finished on, so a subscriber that
    /// throws would propagate out through <see cref="RefreshAllAsync"/> - which documents itself as
    /// never throwing - and abort the whole cycle, taking every other provider's refresh with it.
    /// That is exactly the coupling this service exists to prevent, so a bad subscriber is
    /// contained the same way a bad probe is.
    /// </summary>
    private void RaiseRefreshed(ProviderDescriptor provider, ProviderSnapshot snapshot)
    {
        try
        {
            Refreshed?.Invoke(this, new ProviderRefreshed(provider, snapshot));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A subscriber threw while handling the refresh of {Provider}.", provider.DisplayName);
        }
    }

    /// <summary>
    /// Keeps an abandoned probe's eventual failure from surfacing as an unobserved task exception.
    /// The task is deliberately not awaited: it is abandoned precisely because it outran its bound.
    /// </summary>
    private void Observe(Task<ProviderSnapshot> abandoned, ProviderDescriptor provider) =>
        _ = abandoned.ContinueWith(
            faulted => _logger.LogWarning(
                faulted.Exception,
                "The probe for {Provider} failed after it had already been abandoned for exceeding its timeout.",
                provider.DisplayName),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private ProviderSnapshot Failed(ProviderDescriptor provider, string error) => new(
        ProviderName: provider.DisplayName,
        Installed: true,
        Version: null,
        ExecutablePath: null,
        State: ConnectionState.Error,
        Mechanism: "unknown",
        Tier: MechanismTier.Unofficial,
        UpdateModel: "pull (poll)",
        Windows: [],
        RetrievedAt: null,
        Error: error,
        Notes: []);

    private bool IsBackedOff(ProviderDescriptor provider, DateTimeOffset now)
    {
        lock (_gate)
        {
            return _backoff.TryGetValue(provider, out Backoff? state) && state.NextAttempt > now;
        }
    }

    private void Record(ProviderDescriptor provider, ProviderSnapshot snapshot, DateTimeOffset now)
    {
        // NotInstalled and Unsupported are stable facts about the machine, not failures to retry
        // more slowly - and re-checking them costs a file-existence test.
        bool failed = snapshot.State is ConnectionState.Error or ConnectionState.Unavailable;

        lock (_gate)
        {
            if (!_backoff.TryGetValue(provider, out Backoff? state))
            {
                state = new Backoff();
                _backoff[provider] = state;
            }

            state.ConsecutiveFailures = failed ? state.ConsecutiveFailures + 1 : 0;
            state.NextAttempt = now + BackoffFor(state.ConsecutiveFailures, _baseInterval);
        }
    }

    private sealed class Backoff
    {
        public int ConsecutiveFailures { get; set; }

        public DateTimeOffset NextAttempt { get; set; }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~ProviderRefreshServiceTests`
Expected: PASS.

- [ ] **Step 5: Build and run the whole suite**

Run: `dotnet build`
Expected: 0 warnings, 0 errors.

Run: `dotnet test`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add a polling provider refresh service with per-provider backoff"
```

---

### Task 5: Settings for refresh cadence and window placement

**Files:**
- Modify: `src/AiUsageMonitor.Infrastructure/Settings/AppSettings.cs`
- Test: `tests/AiUsageMonitor.Infrastructure.Tests/AppSettingsStoreTests.cs` (add cases)

**Interfaces:**
- Produces: `AppSettings.RefreshIntervalSeconds` / `AppSettings.RefreshInterval`, `AppSettings.WindowLeft`, `AppSettings.WindowTop`. Tasks 9 and 11 consume them.

- [ ] **Step 1: Write the failing tests**

Append to `tests/AiUsageMonitor.Infrastructure.Tests/AppSettingsStoreTests.cs` (inside the existing test class, matching its existing style for constructing a store over a temp directory):

```csharp
    [Fact]
    public void RefreshIntervalDefaultsToOneMinute() =>
        Assert.Equal(TimeSpan.FromMinutes(1), AppSettings.Default.RefreshInterval);

    [Theory]
    [InlineData(0, 15)]
    [InlineData(-5, 15)]
    [InlineData(14, 15)]
    [InlineData(15, 15)]
    [InlineData(90, 90)]
    [InlineData(3600, 3600)]
    [InlineData(99999, 3600)]
    public void RefreshIntervalIsClampedRatherThanRejected(int configured, int expectedSeconds)
    {
        // A hand-edited settings file must never stop the application starting, and a zero-second
        // interval would poll a provider in a tight loop.
        AppSettings settings = AppSettings.Default with { RefreshIntervalSeconds = configured };

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), settings.RefreshInterval);
    }

    [Fact]
    public void WindowPlacementDefaultsToAbsentSoTheFirstRunIsCentred()
    {
        Assert.Null(AppSettings.Default.WindowLeft);
        Assert.Null(AppSettings.Default.WindowTop);
    }

    [Fact]
    public void WindowPlacementRoundTripsThroughTheStore()
    {
        using TempDirectory directory = new();
        AppSettingsStore store = new(Path.Combine(directory.Path, "settings.json"));

        store.Save(AppSettings.Default with { WindowLeft = 1234.5, WindowTop = -20 });

        AppSettings loaded = store.Load().Settings;
        Assert.Equal(1234.5, loaded.WindowLeft);
        Assert.Equal(-20, loaded.WindowTop);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~AppSettingsStoreTests`
Expected: FAIL — the members do not exist.

- [ ] **Step 3: Add the settings**

In `src/AiUsageMonitor.Infrastructure/Settings/AppSettings.cs`, add to the `AppSettings` record, keeping the file's existing comment style:

```csharp
    /// <summary>Persisted as a plain number so the settings file stays readable and hand-editable.</summary>
    public int RefreshIntervalSeconds { get; init; } = 60;

    /// <summary>
    /// <see cref="RefreshIntervalSeconds"/> clamped to a sane range. Clamped rather than rejected
    /// for the same reason as <see cref="StaleAfter"/>: a hand-edited settings file must never
    /// prevent the application from starting. The floor exists because a provider poll spawns a
    /// process or makes a network call — polling faster than that is a cost with no benefit.
    /// </summary>
    [JsonIgnore]
    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(Math.Clamp(RefreshIntervalSeconds, 15, 3600));

    /// <summary>
    /// Last known window position, in device-independent pixels. Null until the window has been
    /// placed once. Restoring is conditional: a saved position on a monitor that no longer exists
    /// is discarded rather than used (PRD §17).
    /// </summary>
    public double? WindowLeft { get; init; }

    public double? WindowTop { get; init; }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~AppSettingsStoreTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add refresh cadence and window placement settings"
```

---

### Task 6: A test project that loads real XAML

The previous increment shipped a build where `dotnet build` was clean and 137 tests passed, and the application launched with **no window at all** — a dependency property registered through an overload that leaves its default null, throwing inside a static initializer and surfacing only as a `XamlParseException` when a view first referenced the control. No test in this repository loads XAML, so nothing could have caught it. This project is that net, and it is also where every view model from Tasks 7–9 is tested.

**Files:**
- Create: `tests/AiUsageMonitor.App.Tests/AiUsageMonitor.App.Tests.csproj`
- Create: `tests/AiUsageMonitor.App.Tests/WpfFixture.cs`
- Create: `tests/AiUsageMonitor.App.Tests/ControlLoadingTests.cs`
- Modify: `AiUsageMonitor.sln`

**Interfaces:**
- Produces: `WpfFixture` with `Invoke(Action)`, and the `[Collection("wpf")]` marker every WPF-touching test uses. Tasks 7–11 add tests to this project.

- [ ] **Step 1: Create the project**

Create `tests/AiUsageMonitor.App.Tests/AiUsageMonitor.App.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\AiUsageMonitor.App\AiUsageMonitor.App.csproj" />
  </ItemGroup>

</Project>
```

Then register it:

```bash
dotnet sln add tests/AiUsageMonitor.App.Tests/AiUsageMonitor.App.Tests.csproj
```

- [ ] **Step 2: Write the STA fixture**

WPF objects need an STA thread with a running `Dispatcher`, and a process may create only one `Application`. One fixture owns both, shared by every test in the collection.

Create `tests/AiUsageMonitor.App.Tests/WpfFixture.cs`:

```csharp
using System.Windows;
using System.Windows.Threading;

namespace AiUsageMonitor.App.Tests;

/// <summary>
/// One STA thread with a running dispatcher and one <see cref="Application"/>, shared by every
/// test that touches WPF. Both are process-wide singletons in WPF: a second Application throws,
/// and an object created on one STA thread cannot be touched from another.
/// </summary>
public sealed class WpfFixture : IDisposable
{
    private readonly Thread _thread;
    private Dispatcher _dispatcher = null!;

    public WpfFixture()
    {
        using ManualResetEventSlim ready = new();

        _thread = new Thread(() =>
        {
            _dispatcher = Dispatcher.CurrentDispatcher;

            Application application = new();
            application.Resources.MergedDictionaries.Add(Load("Themes/Tokens.xaml"));
            application.Resources.MergedDictionaries.Add(Load("Themes/Controls.xaml"));
            application.Resources.MergedDictionaries.Add(Load("Themes/Light.xaml"));

            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(30));
    }

    /// <summary>Runs <paramref name="action"/> on the STA thread, rethrowing whatever it threw.</summary>
    public void Invoke(Action action) => _dispatcher.Invoke(action);

    public void Dispose()
    {
        _dispatcher.InvokeShutdown();
        _thread.Join(TimeSpan.FromSeconds(10));
    }

    private static ResourceDictionary Load(string relativePath) => new()
    {
        Source = new Uri(
            $"pack://application:,,,/AiUsageMonitor.App;component/{relativePath}",
            UriKind.Absolute)
    };
}

[CollectionDefinition("wpf")]
public sealed class WpfCollection : ICollectionFixture<WpfFixture>;
```

- [ ] **Step 3: Write the control-loading tests**

Create `tests/AiUsageMonitor.App.Tests/ControlLoadingTests.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using AiUsageMonitor.App.Controls;
using AiUsageMonitor.Domain;

namespace AiUsageMonitor.App.Tests;

/// <summary>
/// These exist because a clean build and a green suite once shipped an application that opened no
/// window: a dependency property registered with a null default for a value type throws inside a
/// static initializer, which surfaces only when XAML first references the control.
/// </summary>
[Collection("wpf")]
public class ControlLoadingTests(WpfFixture wpf)
{
    [Fact]
    public void EveryControlTypeInitialisesAndAppliesItsStyle() => wpf.Invoke(() =>
    {
        foreach (FrameworkElement control in (FrameworkElement[])
        [
            new QuotaBar(),
            new StateGlyph(),
            new StateChip { Label = "Connected", State = ConnectionState.Connected },
            new TierBadge { Tier = MechanismTier.Unofficial }
        ])
        {
            Measured(control);
        }
    });

    [Theory]
    [InlineData("Themes/Light.xaml")]
    [InlineData("Themes/Dark.xaml")]
    [InlineData("Themes/HighContrast.xaml")]
    public void EveryThemeDictionaryLoadsAsRealXaml(string path) => wpf.Invoke(() =>
    {
        ResourceDictionary dictionary = new()
        {
            Source = new Uri($"pack://application:,,,/AiUsageMonitor.App;component/{path}", UriKind.Absolute)
        };

        Assert.NotEmpty(dictionary.Keys);
    });

    [Fact]
    public void AQuotaBarRendersEveryBandWithoutThrowing() => wpf.Invoke(() =>
    {
        foreach (double? used in (double?[])[null, 0, 25, 74, 75, 99, 100, 150])
        {
            Measured(new QuotaBar { UsedPercent = used, ElapsedFraction = 0.5, Width = 300 });
        }
    });

    /// <summary>Measure and arrange force the template to expand and OnRender to be reachable.</summary>
    internal static T Measured<T>(T element) where T : FrameworkElement
    {
        Border host = new() { Child = element };
        host.Measure(new Size(360, 520));
        host.Arrange(new Rect(0, 0, 360, 520));
        host.UpdateLayout();
        return element;
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/AiUsageMonitor.App.Tests`
Expected: PASS.

- [ ] **Step 5: Prove the net actually catches the defect it exists for**

Temporarily change one `QuotaBar` bool dependency property registration to the metadata overload that omits a default:

```csharp
public static readonly DependencyProperty IsStaleProperty = DependencyProperty.Register(
    nameof(IsStale), typeof(bool), typeof(QuotaBar),
    new FrameworkPropertyMetadata(FrameworkPropertyMetadataOptions.AffectsRender));
```

Run: `dotnet test tests/AiUsageMonitor.App.Tests`
Expected: FAIL with "Default value type does not match type of property".

**Revert the change** and re-run to confirm the suite is green again. Do not commit the temporary change.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "test: add a WPF test project that loads the real XAML"
```

---

### Task 7: MVVM primitives and the quota row view model

**Files:**
- Create: `src/AiUsageMonitor.App/ViewModels/ObservableObject.cs`
- Create: `src/AiUsageMonitor.App/ViewModels/RelayCommand.cs`
- Create: `src/AiUsageMonitor.App/ViewModels/QuotaRowViewModel.cs`
- Test: `tests/AiUsageMonitor.App.Tests/QuotaRowViewModelTests.cs`

**Interfaces:**
- Consumes: `QuotaWindow`, `QuotaOrdering`, `QuotaFormatting` from the domain.
- Produces: `ObservableObject` with `protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)` and `protected void Raise(string name)`; `RelayCommand(Action execute, Func<bool>? canExecute = null)` with `RaiseCanExecuteChanged()`; `QuotaRowViewModel(QuotaWindow window, bool colorBarsByUsage)` with `Tick(DateTimeOffset now)`. Tasks 8 and 10 consume all three.

- [ ] **Step 1: Write the failing tests**

Create `tests/AiUsageMonitor.App.Tests/QuotaRowViewModelTests.cs`:

```csharp
using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Domain;

namespace AiUsageMonitor.App.Tests;

public class QuotaRowViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private static QuotaWindow Window(
        string id = "five_hour",
        string label = "5-hour window",
        double? usedPercent = 47,
        DateTimeOffset? resetsAt = null,
        TimeSpan? duration = null,
        bool labelIsProviderToken = false) => new(
            Id: id,
            Label: label,
            UsedPercent: usedPercent,
            ResetsAt: resetsAt,
            WindowDuration: duration,
            Order: 0,
            IsPartial: resetsAt is null || duration is null,
            Extra: new Dictionary<string, string>(),
            LabelIsProviderToken: labelIsProviderToken);

    private static QuotaRowViewModel Row(QuotaWindow window, bool colorBarsByUsage = true)
    {
        QuotaRowViewModel row = new(window, colorBarsByUsage);
        row.Tick(Now);
        return row;
    }

    [Fact]
    public void CompleteRowRendersLabelPercentageAndCountdown()
    {
        QuotaRowViewModel row = Row(Window(resetsAt: Now.AddMinutes(295), duration: TimeSpan.FromHours(5)));

        Assert.Equal("5-hour window", row.Label);
        Assert.Equal("47%", row.UsedText);
        Assert.Equal("4h 55m", row.CountdownText);
        Assert.NotNull(row.ElapsedFraction);
    }

    [Fact]
    public void PartialRowShowsNoCountdownAndNoMarker()
    {
        QuotaRowViewModel row = Row(Window(id: "nimbus_quill", label: "nimbus_quill", usedPercent: 34, labelIsProviderToken: true));

        Assert.Equal("34%", row.UsedText);
        Assert.Null(row.CountdownText);
        Assert.Null(row.ElapsedFraction);
        Assert.True(row.IsProviderToken);
    }

    [Fact]
    public void AbsentUsageIsAbsentRatherThanZero()
    {
        QuotaRowViewModel row = Row(Window(usedPercent: null));

        Assert.Null(row.UsedText);
        Assert.Null(row.UsedPercent);
    }

    [Fact]
    public void TheProviderIdentifierIsAlwaysReachable()
    {
        QuotaRowViewModel row = Row(Window(id: "nimbus_quill", label: "nimbus_quill", labelIsProviderToken: true));

        Assert.Equal("identifier: nimbus_quill", row.IdentifierTooltip);
    }

    [Fact]
    public void AMarkerAppearsOnlyWhenTheDurationIsKnown()
    {
        Assert.Null(Row(Window(resetsAt: Now.AddHours(1))).ElapsedFraction);
        Assert.NotNull(Row(Window(resetsAt: Now.AddHours(1), duration: TimeSpan.FromHours(5))).ElapsedFraction);
    }

    [Fact]
    public void OneHundredPercentIsExhaustedRegardlessOfTheColourSetting()
    {
        Assert.True(Row(Window(usedPercent: 100), colorBarsByUsage: true).IsExhausted);
        Assert.True(Row(Window(usedPercent: 100), colorBarsByUsage: false).IsExhausted);
        Assert.False(Row(Window(usedPercent: 99)).IsExhausted);
    }

    [Fact]
    public void TickAdvancesTheCountdownWithoutTouchingTheProvider()
    {
        QuotaRowViewModel row = Row(Window(resetsAt: Now.AddMinutes(10), duration: TimeSpan.FromHours(1)));
        Assert.Equal("10m 00s", row.CountdownText);

        row.Tick(Now.AddMinutes(5));
        Assert.Equal("5m 00s", row.CountdownText);
    }

    [Fact]
    public void TheAccessibleNameSpellsOutTheDirectionOfThePercentage()
    {
        QuotaRowViewModel row = Row(Window(resetsAt: Now.AddMinutes(295), duration: TimeSpan.FromHours(5)));

        Assert.Equal("5-hour window, 47% used, resets in 4h 55m", row.AccessibleName);
    }

    [Fact]
    public void TheAccessibleNameSaysWhatIsMissingRatherThanImplyingZero()
    {
        QuotaRowViewModel row = Row(Window(id: "nimbus_quill", label: "nimbus_quill", usedPercent: null, labelIsProviderToken: true));

        Assert.Equal("nimbus_quill, usage not reported, no reset time reported", row.AccessibleName);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/AiUsageMonitor.App.Tests --filter FullyQualifiedName~QuotaRowViewModelTests`
Expected: FAIL — the types do not exist.

- [ ] **Step 3: Write the MVVM primitives**

Create `src/AiUsageMonitor.App/ViewModels/ObservableObject.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AiUsageMonitor.App.ViewModels;

/// <summary>
/// Minimal <see cref="INotifyPropertyChanged"/> base. Hand-rolled rather than taken from a
/// package: this is the whole of what the application needs from an MVVM framework, and
/// dependencies have to be justified by clear product value (PRD §22).
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(propertyName);
        return true;
    }

    protected void Raise([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
```

Create `src/AiUsageMonitor.App/ViewModels/RelayCommand.cs`:

```csharp
using System.Windows.Input;

namespace AiUsageMonitor.App.ViewModels;

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => execute();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
```

- [ ] **Step 4: Write the row view model**

Create `src/AiUsageMonitor.App/ViewModels/QuotaRowViewModel.cs`:

```csharp
using System.Globalization;
using AiUsageMonitor.Domain;

namespace AiUsageMonitor.App.ViewModels;

/// <summary>
/// One provider-reported quota window, as the row renders it. Nothing here interprets a
/// provider's semantics: the label, the identifier and the percentage are the provider's, and a
/// field the provider did not supply stays null so the view omits it rather than drawing a
/// placeholder (PRD §7.3).
/// </summary>
public sealed class QuotaRowViewModel : ObservableObject
{
    private readonly QuotaWindow _window;
    private string? _countdownText;
    private double? _elapsedFraction;
    private bool _isStale;

    public QuotaRowViewModel(QuotaWindow window, bool colorBarsByUsage)
    {
        _window = window;
        ColorBarsByUsage = colorBarsByUsage;
    }

    public string Label => QuotaOrdering.DisplayLabel(_window);

    /// <summary>
    /// True when the label is the provider's raw identifier because it resolved to no duration.
    /// The view renders these in a monospace chip so a provider term is never mistaken for a
    /// label this application authored (PRD §7.2 item 10).
    /// </summary>
    public bool IsProviderToken => _window.LabelIsProviderToken;

    /// <summary>The provider's identifier stays reachable for every window, resolved or not.</summary>
    public string IdentifierTooltip => $"identifier: {_window.Id}";

    public double? UsedPercent => _window.UsedPercent;

    public string? UsedText => _window.UsedPercent is double used
        ? Math.Round(used, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%"
        : null;

    /// <summary>
    /// 100% used is the exhausted treatment whether or not bar tone by usage is on, per PRD §16.1.
    /// Neither provider reports a separate rate-limit-reached flag today, so the provider's own
    /// percentage is the signal — nothing is inferred beyond it.
    /// </summary>
    public bool IsExhausted => _window.UsedPercent >= 100;

    public bool ColorBarsByUsage { get; }

    public string? CountdownText { get => _countdownText; private set => Set(ref _countdownText, value); }

    public bool HasCountdown => CountdownText is not null;

    public double? ElapsedFraction { get => _elapsedFraction; private set => Set(ref _elapsedFraction, value); }

    public bool IsStale { get => _isStale; set => Set(ref _isStale, value); }

    public string AccessibleName
    {
        get
        {
            string usage = UsedText is null ? "usage not reported" : UsedText + " used";
            string reset = CountdownText is null ? "no reset time reported" : "resets in " + CountdownText;
            return $"{Label}, {usage}, {reset}";
        }
    }

    /// <summary>
    /// Recomputes the locally derived values. Countdown and elapsed marker advance from the last
    /// known reset timestamp and never cost a provider call (PRD §14).
    /// </summary>
    public void Tick(DateTimeOffset now)
    {
        CountdownText = QuotaFormatting.FormatCountdown(_window.TimeUntilReset(now));
        ElapsedFraction = _window.ElapsedFraction(now);
        Raise(nameof(HasCountdown));
        Raise(nameof(AccessibleName));
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/AiUsageMonitor.App.Tests --filter FullyQualifiedName~QuotaRowViewModelTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add MVVM primitives and the quota row view model"
```

---

### Task 8: The provider card view model

**Files:**
- Create: `src/AiUsageMonitor.App/ViewModels/ConnectionStateText.cs`
- Create: `src/AiUsageMonitor.App/ViewModels/ProviderNotice.cs`
- Create: `src/AiUsageMonitor.App/ViewModels/ProviderCardViewModel.cs`
- Test: `tests/AiUsageMonitor.App.Tests/ProviderCardViewModelTests.cs`

**Interfaces:**
- Consumes: `QuotaRowViewModel`, `RelayCommand` (Task 7); `ProviderDescriptor` (Task 1); `RelativeTime` (Task 3); `ProviderSnapshot`, `FreshnessPolicy`, `ConnectionStateRules`, `QuotaOrdering` from the domain.
- Produces: `ProviderCardViewModel(ProviderDescriptor descriptor, bool colorBarsByUsage, Action<ProviderDescriptor> retry)` with `Apply(ProviderSnapshot, DateTimeOffset, FreshnessPolicy)` and `Tick(DateTimeOffset)`. Tasks 9 and 10 consume it.

- [ ] **Step 1: Write the failing tests**

Create `tests/AiUsageMonitor.App.Tests/ProviderCardViewModelTests.cs`:

```csharp
using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;

namespace AiUsageMonitor.App.Tests;

public class ProviderCardViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly FreshnessPolicy Policy = new(TimeSpan.FromMinutes(5));

    private sealed class SilentProbe : IProviderProbe
    {
        public string Name => "Claude Code";

        public Task<ProviderSnapshot> ProbeAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private static ProviderCardViewModel Card() =>
        new(new ProviderDescriptor("Claude Code", "CC", new SilentProbe()), colorBarsByUsage: true, _ => { });

    private static ProviderSnapshot Snapshot(
        ConnectionState state = ConnectionState.Connected,
        string? version = "2.1.227",
        IReadOnlyList<QuotaWindow>? windows = null,
        DateTimeOffset? retrievedAt = null,
        string? error = null) => new(
            ProviderName: "Claude Code",
            Installed: state != ConnectionState.NotInstalled,
            Version: version,
            ExecutablePath: null,
            State: state,
            Mechanism: "Anthropic OAuth usage endpoint (UNOFFICIAL/undocumented)",
            Tier: MechanismTier.Unofficial,
            UpdateModel: "pull (poll)",
            Windows: windows ?? [],
            RetrievedAt: retrievedAt,
            Error: error,
            Notes: []);

    private static QuotaWindow Window(string id, int order, double used) => new(
        Id: id, Label: id, UsedPercent: used, ResetsAt: null, WindowDuration: null,
        Order: order, IsPartial: true, Extra: new Dictionary<string, string>(), LabelIsProviderToken: true);

    [Fact]
    public void IdentityComesFromTheDescriptorNotTheSnapshot()
    {
        ProviderCardViewModel card = Card();

        Assert.Equal("Claude Code", card.DisplayName);
        Assert.Equal("CC", card.Monogram);
    }

    [Fact]
    public void VersionIsPrefixedOnlyWhenTheProviderReportedOne()
    {
        ProviderCardViewModel card = Card();

        card.Apply(Snapshot(retrievedAt: Now), Now, Policy);
        Assert.Equal("v2.1.227", card.VersionText);

        card.Apply(Snapshot(version: null, retrievedAt: Now), Now, Policy);
        Assert.Null(card.VersionText);
    }

    [Fact]
    public void TheTierIsAlwaysCarriedThroughFromTheSnapshot()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(retrievedAt: Now), Now, Policy);

        Assert.Equal(MechanismTier.Unofficial, card.Tier);
    }

    [Fact]
    public void WindowsKeepTheOrderTheProviderReportedThem()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(windows: [Window("c", 2, 10), Window("a", 0, 20), Window("b", 1, 30)], retrievedAt: Now), Now, Policy);

        Assert.Equal(["a", "b", "c"], card.Windows.Select(w => w.Label));
    }

    [Fact]
    public void AConnectedSnapshotOlderThanTheThresholdBecomesStale()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(windows: [Window("a", 0, 20)], retrievedAt: Now.AddMinutes(-6)), Now, Policy);

        Assert.Equal(ConnectionState.Stale, card.State);
        Assert.True(card.IsStale);
        Assert.Equal("6 minutes ago", card.StaleAgeText);
        Assert.True(card.Windows[0].IsStale);
    }

    [Fact]
    public void AgeNeverMasksARealFailure()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(state: ConnectionState.Error, retrievedAt: Now.AddHours(-2), error: "boom"), Now, Policy);

        Assert.Equal(ConnectionState.Error, card.State);
        Assert.False(card.IsStale);
    }

    [Fact]
    public void UpdatedTextIsAbsentUntilSomethingHasSucceeded()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(state: ConnectionState.Waiting, retrievedAt: null), Now, Policy);

        Assert.Null(card.UpdatedText);
    }

    [Fact]
    public void UpdatedTextTracksTheLocalClock()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(windows: [Window("a", 0, 20)], retrievedAt: Now), Now, Policy);
        Assert.Equal("Updated 0s ago", card.UpdatedText);

        card.Tick(Now.AddSeconds(12));
        Assert.Equal("Updated 12s ago", card.UpdatedText);
    }

    [Fact]
    public void ConnectedWithWindowsShowsNoNotice()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(windows: [Window("a", 0, 20)], retrievedAt: Now), Now, Policy);

        Assert.Null(card.Notice);
    }

    [Fact]
    public void ConnectedWithNoWindowsIsNeitherAnErrorNorZeroUsage()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(retrievedAt: Now), Now, Policy);

        Assert.Equal("No quota windows reported", card.Notice!.Title);
        Assert.False(card.Notice.IsAlert);
    }

    [Fact]
    public void NotInstalledKeepsItsCardAndOffersARecheck()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(state: ConnectionState.NotInstalled, version: null), Now, Policy);

        Assert.Equal("Not installed on this machine", card.Notice!.Title);
        Assert.Equal("Check again", card.Notice.ActionText);
        Assert.False(card.Notice.IsAlert);
    }

    [Fact]
    public void UnavailableCommunicatesThatThereIsNoFallback()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(state: ConnectionState.Unavailable, retrievedAt: Now.AddHours(-2), error: "Claude Code is installed but has not stored a sign-in on this machine."), Now, Policy);

        Assert.Equal("Usage can no longer be read", card.Notice!.Title);
        Assert.True(card.Notice.IsAlert);
        Assert.Contains("no second source", card.Notice.Body);
        Assert.Contains("Claude Code is installed but has not stored a sign-in", card.Notice.Body);
        Assert.Contains("2 hours ago", card.Notice.Body);
        Assert.Equal("Retry now", card.Notice.ActionText);
    }

    [Fact]
    public void ANoticeNeverInventsAnAgeThatDoesNotExist()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(state: ConnectionState.Unavailable, retrievedAt: null), Now, Policy);

        Assert.DoesNotContain("Last successful update", card.Notice!.Body);
    }

    [Fact]
    public void TheStatusRowIsShownForEveryState()
    {
        // Compact mode hides the Connected chip; the default widget never does.
        foreach (ConnectionState state in Enum.GetValues<ConnectionState>())
        {
            ProviderCardViewModel card = Card();
            card.Apply(Snapshot(state: state, retrievedAt: Now), Now, Policy);
            Assert.False(string.IsNullOrWhiteSpace(card.StateLabel));
        }
    }

    [Fact]
    public void EveryConnectionStateHasItsOwnWord()
    {
        string[] labels = Enum.GetValues<ConnectionState>().Select(ConnectionStateText.Label).ToArray();

        Assert.Equal(labels.Length, labels.Distinct().Count());
        Assert.All(labels, label => Assert.False(string.IsNullOrWhiteSpace(label)));
    }

    [Fact]
    public void RetryingAsksTheServiceForThisProviderOnly()
    {
        List<string> retried = [];
        ProviderCardViewModel card = new(
            new ProviderDescriptor("Claude Code", "CC", new SilentProbe()),
            colorBarsByUsage: true,
            descriptor => retried.Add(descriptor.DisplayName));

        card.RetryCommand.Execute(null);

        Assert.Equal(["Claude Code"], retried);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/AiUsageMonitor.App.Tests --filter FullyQualifiedName~ProviderCardViewModelTests`
Expected: FAIL — the types do not exist.

- [ ] **Step 3: Write the state vocabulary**

Create `src/AiUsageMonitor.App/ViewModels/ConnectionStateText.cs`:

```csharp
using AiUsageMonitor.Domain;

namespace AiUsageMonitor.App.ViewModels;

/// <summary>
/// The word shown beside each state's glyph. Every glyph is paired with its word everywhere it
/// appears: colour alone never communicates state (PRD §10).
/// </summary>
public static class ConnectionStateText
{
    public static string Label(ConnectionState state) => state switch
    {
        ConnectionState.NotInstalled => "Not installed",
        ConnectionState.Discovering => "Discovering",
        ConnectionState.Waiting => "Waiting",
        ConnectionState.Connected => "Connected",
        ConnectionState.Stale => "Stale",
        ConnectionState.Unavailable => "Unavailable",
        ConnectionState.Unsupported => "Unsupported",
        ConnectionState.Error => "Error",
        _ => state.ToString()
    };
}
```

- [ ] **Step 4: Write the notice selector**

Create `src/AiUsageMonitor.App/ViewModels/ProviderNotice.cs`:

```csharp
using AiUsageMonitor.Domain;

namespace AiUsageMonitor.App.ViewModels;

/// <summary>
/// What a card says when it has no quota rows to show, or has rows that cannot be trusted.
/// <paramref name="ActionText"/> is null unless there is something this build can actually do —
/// a button that does nothing is worse than no button.
/// </summary>
public sealed record ProviderNotice(string Title, string Body, bool IsAlert, string? ActionText);

public static class ProviderNoticeSelector
{
    public static ProviderNotice? For(ProviderSnapshot snapshot, ConnectionState state, DateTimeOffset now)
    {
        string? age = RelativeTime.FormatAge(
            snapshot.RetrievedAt is DateTimeOffset at ? now - at : null);

        return state switch
        {
            ConnectionState.NotInstalled => new ProviderNotice(
                "Not installed on this machine",
                "The card stays in place. Nothing is shown in place of usage.",
                IsAlert: false,
                ActionText: "Check again"),

            ConnectionState.Unsupported => new ProviderNotice(
                "Usage is not available from this version",
                "The installed version does not expose usage through a mechanism this application can verify.",
                IsAlert: false,
                ActionText: null),

            ConnectionState.Waiting => new ProviderNotice(
                "Waiting for the first usage report",
                "The provider is installed. Nothing has been reported yet.",
                IsAlert: false,
                ActionText: null),

            ConnectionState.Unavailable => new ProviderNotice(
                "Usage can no longer be read",
                Compose(
                    "The only source this provider has stopped returning usable data. There is no second source to fall back to.",
                    snapshot.Error,
                    age),
                IsAlert: true,
                ActionText: "Retry now"),

            ConnectionState.Error => new ProviderNotice(
                "The last read failed",
                Compose("The most recent attempt did not return usable data.", snapshot.Error, age),
                IsAlert: true,
                ActionText: "Retry now"),

            ConnectionState.Connected or ConnectionState.Stale when snapshot.Windows.Count == 0 => new ProviderNotice(
                "No quota windows reported",
                "The provider is installed, authenticated and reachable, and returned no windows. This is neither an error nor zero usage.",
                IsAlert: false,
                ActionText: null),

            _ => null
        };
    }

    /// <summary>
    /// Appends only what exists. A missing reason and a missing age are each simply left out —
    /// never replaced by "unknown", which reads as a value.
    /// </summary>
    private static string Compose(string lead, string? reason, string? age)
    {
        List<string> parts = [lead];

        if (!string.IsNullOrWhiteSpace(reason))
        {
            parts.Add(reason);
        }

        if (age is not null)
        {
            parts.Add($"Last successful update {age}.");
        }

        return string.Join(" ", parts);
    }
}
```

- [ ] **Step 5: Write the card view model**

Create `src/AiUsageMonitor.App/ViewModels/ProviderCardViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;

namespace AiUsageMonitor.App.ViewModels;

/// <summary>
/// One provider's card. Identity comes from the descriptor and everything else from the latest
/// snapshot; nothing here branches on which provider it is (PRD §21).
/// </summary>
public sealed class ProviderCardViewModel : ObservableObject
{
    private readonly ProviderDescriptor _descriptor;
    private readonly bool _colorBarsByUsage;
    private ProviderSnapshot? _snapshot;
    private ConnectionState _state = ConnectionState.Discovering;
    private MechanismTier _tier = MechanismTier.Unofficial;
    private string? _versionText;
    private string? _updatedText;
    private string? _staleAgeText;
    private ProviderNotice? _notice;

    public ProviderCardViewModel(ProviderDescriptor descriptor, bool colorBarsByUsage, Action<ProviderDescriptor> retry)
    {
        _descriptor = descriptor;
        _colorBarsByUsage = colorBarsByUsage;
        RetryCommand = new RelayCommand(() => retry(descriptor));
    }

    public string DisplayName => _descriptor.DisplayName;

    public string Monogram => _descriptor.Monogram;

    public RelayCommand RetryCommand { get; }

    public ObservableCollection<QuotaRowViewModel> Windows { get; } = [];

    public ConnectionState State { get => _state; private set { if (Set(ref _state, value)) { Raise(nameof(StateLabel)); Raise(nameof(IsStale)); } } }

    public string StateLabel => ConnectionStateText.Label(State);

    public bool IsStale => State == ConnectionState.Stale;

    public MechanismTier Tier { get => _tier; private set => Set(ref _tier, value); }

    public string? VersionText { get => _versionText; private set => Set(ref _versionText, value); }

    public string? UpdatedText { get => _updatedText; private set => Set(ref _updatedText, value); }

    public string? StaleAgeText { get => _staleAgeText; private set => Set(ref _staleAgeText, value); }

    public ProviderNotice? Notice { get => _notice; private set { if (Set(ref _notice, value)) { Raise(nameof(HasNotice)); } } }

    public bool HasNotice => Notice is not null;

    /// <summary>Replaces everything this card shows with the given snapshot. Never merges: a snapshot is whole.</summary>
    public void Apply(ProviderSnapshot snapshot, DateTimeOffset now, FreshnessPolicy policy)
    {
        _snapshot = snapshot;

        FreshnessState freshness = policy.Evaluate(snapshot.RetrievedAt, now);
        State = ConnectionStateRules.ApplyFreshness(snapshot.State, freshness);
        Tier = snapshot.Tier;
        VersionText = snapshot.Version is null ? null : "v" + snapshot.Version;

        Windows.Clear();
        foreach (QuotaWindow window in QuotaOrdering.InProviderOrder(snapshot.Windows))
        {
            Windows.Add(new QuotaRowViewModel(window, _colorBarsByUsage));
        }

        Tick(now);
    }

    /// <summary>Recomputes everything derived from the local clock. Costs no provider call (PRD §14).</summary>
    public void Tick(DateTimeOffset now)
    {
        if (_snapshot is not ProviderSnapshot snapshot)
        {
            return;
        }

        TimeSpan? age = snapshot.RetrievedAt is DateTimeOffset at ? now - at : null;
        UpdatedText = RelativeTime.FormatAge(age) is string formatted ? "Updated " + formatted : null;
        StaleAgeText = RelativeTime.FormatAge(age);
        Notice = ProviderNoticeSelector.For(snapshot, State, now);

        foreach (QuotaRowViewModel window in Windows)
        {
            window.IsStale = IsStale;
            window.Tick(now);
        }
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/AiUsageMonitor.App.Tests --filter FullyQualifiedName~ProviderCardViewModelTests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: add the provider card view model"
```

---

### Task 9: The main view model

**Files:**
- Create: `src/AiUsageMonitor.App/ViewModels/MainViewModel.cs`
- Test: `tests/AiUsageMonitor.App.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `ProviderCardViewModel` (Task 8); `ProviderRefreshService`, `ProviderRefreshed` (Task 4); `AppSettings` (Task 5).
- Produces: `MainViewModel(ProviderRefreshService refresh, IReadOnlyList<ProviderDescriptor> providers, AppSettings settings, Func<DateTimeOffset> clock, Action<Action>? dispatch = null)` with `Providers`, `FooterText`, `RefreshCommand`, `Task RefreshAsync(bool force)`, `Tick()`, `Dispose()`. Task 11 consumes it.

- [ ] **Step 1: Write the failing tests**

Create `tests/AiUsageMonitor.App.Tests/MainViewModelTests.cs`:

```csharp
using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Refresh;
using AiUsageMonitor.Infrastructure.Settings;

namespace AiUsageMonitor.App.Tests;

public class MainViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed class StubProbe(string name, ConnectionState state, IReadOnlyList<QuotaWindow> windows) : IProviderProbe
    {
        public string Name => name;

        public Task<ProviderSnapshot> ProbeAsync(CancellationToken ct) => Task.FromResult(new ProviderSnapshot(
            ProviderName: name,
            Installed: true,
            Version: "1.0.0",
            ExecutablePath: null,
            State: state,
            Mechanism: "stub",
            Tier: MechanismTier.Official,
            UpdateModel: "pull (poll)",
            Windows: windows,
            RetrievedAt: state == ConnectionState.Connected ? Now : null,
            Error: null,
            Notes: []));
    }

    private static QuotaWindow Window() => new(
        Id: "five_hour", Label: "5-hour window", UsedPercent: 47,
        ResetsAt: Now.AddMinutes(295), WindowDuration: TimeSpan.FromHours(5),
        Order: 0, IsPartial: false, Extra: new Dictionary<string, string>(), LabelIsProviderToken: false);

    private static (MainViewModel Model, IReadOnlyList<ProviderDescriptor> Providers) Build(params ProviderDescriptor[] providers)
    {
        ProviderRefreshService service = new(providers, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60));
        MainViewModel model = new(service, providers, AppSettings.Default, () => Now);
        return (model, providers);
    }

    [Fact]
    public void ACardExistsForEveryProviderBeforeAnythingHasBeenProbed()
    {
        (MainViewModel model, _) = Build(
            new ProviderDescriptor("Claude Code", "CC", new StubProbe("Claude Code", ConnectionState.Connected, [])),
            new ProviderDescriptor("Codex", "CX", new StubProbe("Codex", ConnectionState.Connected, [])));

        Assert.Equal(["Claude Code", "Codex"], model.Providers.Select(p => p.DisplayName));
    }

    [Fact]
    public void TheFooterCountsProvidersAndAgreesWithItself()
    {
        (MainViewModel one, _) = Build(new ProviderDescriptor("Codex", "CX", new StubProbe("Codex", ConnectionState.Connected, [])));
        Assert.Equal("1 provider", one.FooterText);

        (MainViewModel two, _) = Build(
            new ProviderDescriptor("Claude Code", "CC", new StubProbe("Claude Code", ConnectionState.Connected, [])),
            new ProviderDescriptor("Codex", "CX", new StubProbe("Codex", ConnectionState.Connected, [])));
        Assert.Equal("2 providers", two.FooterText);
    }

    [Fact]
    public async Task ARefreshRoutesEachSnapshotToItsOwnCard()
    {
        (MainViewModel model, _) = Build(
            new ProviderDescriptor("Claude Code", "CC", new StubProbe("Claude Code", ConnectionState.Connected, [Window()])),
            new ProviderDescriptor("Codex", "CX", new StubProbe("Codex", ConnectionState.NotInstalled, [])));

        await model.RefreshAsync(force: true);

        Assert.Single(model.Providers[0].Windows);
        Assert.Equal(ConnectionState.Connected, model.Providers[0].State);
        Assert.Empty(model.Providers[1].Windows);
        Assert.Equal(ConnectionState.NotInstalled, model.Providers[1].State);
    }

    [Fact]
    public async Task RefreshIsNotReentrant()
    {
        (MainViewModel model, _) = Build(
            new ProviderDescriptor("Codex", "CX", new StubProbe("Codex", ConnectionState.Connected, [])));

        Assert.True(model.RefreshCommand.CanExecute(null));

        Task refresh = model.RefreshAsync(force: true);
        await refresh;

        Assert.True(model.RefreshCommand.CanExecute(null));
        Assert.False(model.IsRefreshing);
    }

    [Fact]
    public async Task TickAdvancesEveryCardWithoutProbingAgain()
    {
        StubProbe probe = new("Codex", ConnectionState.Connected, [Window()]);
        (MainViewModel model, _) = Build(new ProviderDescriptor("Codex", "CX", probe));

        await model.RefreshAsync(force: true);
        string? before = model.Providers[0].Windows[0].CountdownText;

        model.Tick();

        Assert.Equal(before, model.Providers[0].Windows[0].CountdownText);
        Assert.Equal("Updated 0s ago", model.Providers[0].UpdatedText);
    }

    [Fact]
    public async Task DisposingCancelsInFlightWorkAndStopsRoutingSnapshots()
    {
        (MainViewModel model, _) = Build(
            new ProviderDescriptor("Codex", "CX", new StubProbe("Codex", ConnectionState.Connected, [Window()])));

        model.Dispose();

        await model.RefreshAsync(force: true);

        Assert.Empty(model.Providers[0].Windows);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/AiUsageMonitor.App.Tests --filter FullyQualifiedName~MainViewModelTests`
Expected: FAIL — `MainViewModel` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/AiUsageMonitor.App/ViewModels/MainViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Refresh;
using AiUsageMonitor.Infrastructure.Settings;

namespace AiUsageMonitor.App.ViewModels;

/// <summary>
/// The widget's root. Owns one card per registered provider and routes each snapshot to its own
/// card as it arrives, so a slow provider never delays a fast one.
/// </summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly ProviderRefreshService _refresh;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<Action> _dispatch;
    private readonly FreshnessPolicy _freshness;
    private readonly Dictionary<ProviderDescriptor, ProviderCardViewModel> _cards = [];
    private readonly CancellationTokenSource _lifetime = new();
    private bool _isRefreshing;

    /// <param name="dispatch">
    /// Marshals a snapshot onto the UI thread. Defaults to running inline, which is what tests
    /// want; the window passes its dispatcher. The refresh service raises its event on whichever
    /// thread the probe finished on and deliberately knows nothing about a dispatcher.
    /// </param>
    public MainViewModel(
        ProviderRefreshService refresh,
        IReadOnlyList<ProviderDescriptor> providers,
        AppSettings settings,
        Func<DateTimeOffset> clock,
        Action<Action>? dispatch = null)
    {
        _refresh = refresh;
        _clock = clock;
        _dispatch = dispatch ?? (action => action());
        _freshness = new FreshnessPolicy(settings.StaleAfter);

        foreach (ProviderDescriptor provider in providers)
        {
            ProviderCardViewModel card = new(provider, settings.ColorBarsByUsage, RetryOne);
            _cards[provider] = card;
            Providers.Add(card);
        }

        RefreshCommand = new RelayCommand(() => _ = RefreshAsync(force: true), () => !IsRefreshing);
        _refresh.Refreshed += OnRefreshed;
    }

    public ObservableCollection<ProviderCardViewModel> Providers { get; } = [];

    public RelayCommand RefreshCommand { get; }

    public string FooterText => Providers.Count == 1 ? "1 provider" : $"{Providers.Count} providers";

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (Set(ref _isRefreshing, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public async Task RefreshAsync(bool force)
    {
        if (_lifetime.IsCancellationRequested)
        {
            return;
        }

        IsRefreshing = true;

        try
        {
            await _refresh.RefreshAllAsync(force, _clock(), _lifetime.Token).ConfigureAwait(true);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>Advances every locally derived value: countdowns, ages, elapsed markers.</summary>
    public void Tick()
    {
        DateTimeOffset now = _clock();

        foreach (ProviderCardViewModel card in Providers)
        {
            card.Tick(now);
        }
    }

    public void Dispose()
    {
        _refresh.Refreshed -= OnRefreshed;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private void RetryOne(ProviderDescriptor provider)
    {
        if (!_lifetime.IsCancellationRequested)
        {
            _ = _refresh.RefreshAsync(provider, _clock(), _lifetime.Token);
        }
    }

    private void OnRefreshed(object? sender, ProviderRefreshed e)
    {
        if (_lifetime.IsCancellationRequested || !_cards.TryGetValue(e.Provider, out ProviderCardViewModel? card))
        {
            return;
        }

        _dispatch(() => card.Apply(e.Snapshot, _clock(), _freshness));
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/AiUsageMonitor.App.Tests --filter FullyQualifiedName~MainViewModelTests`
Expected: PASS.

- [ ] **Step 5: Build and run the whole suite**

Run: `dotnet build`
Expected: 0 warnings, 0 errors.

Run: `dotnet test`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add the main view model"
```

---

### Task 10: The quota row and provider card views

Structure comes from `docs/design/QuotaRow.dc.html` and `ProviderCard.dc.html`; every number comes from `docs/design/tokens.md` or `Themes/Tokens.xaml`. Nothing is measured off a screenshot.

**One deliberate departure from the approved render, already decided — implement it as written here.** The design moved the `Window / Used / Resets in` column captions out of the default widget into the expanded card, to hold a 420px budget. PRD §16 requires the visible percentage text to make its direction explicit ("28% used"), and a bare `47%` in an uncaptioned column does not. The captions therefore stay in the default widget. `docs/design/rationale.md` names them as the first thing to restore if the budget stretches, and it does: the measured default is 410px against a 520px ceiling. This costs ~13px per card and changes no row.

**Files:**
- Create: `src/AiUsageMonitor.App/Views/QuotaRowView.xaml` + `.xaml.cs`
- Create: `src/AiUsageMonitor.App/Views/ProviderCardView.xaml` + `.xaml.cs`
- Modify: `src/AiUsageMonitor.App/Themes/Controls.xaml`
- Test: `tests/AiUsageMonitor.App.Tests/ViewLoadingTests.cs`

**Interfaces:**
- Consumes: `QuotaRowViewModel`, `ProviderCardViewModel` as `DataContext`; the `QuotaBar`, `StateChip`, `TierBadge` controls; the token and theme dictionaries.

- [ ] **Step 1: Add the shared converter and link style**

In `src/AiUsageMonitor.App/Themes/Controls.xaml`, add these at the top of the dictionary, before the existing `QuotaBar` style. No new `xmlns` is needed: `BooleanToVisibilityConverter` lives in the default presentation namespace, and every resource key referenced below already resolves through the merged dictionaries.

```xml
  <BooleanToVisibilityConverter x:Key="BooleanToVisibility" />

  <Style x:Key="LinkButtonStyle" TargetType="Button">
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="BorderThickness" Value="0" />
    <Setter Property="Padding" Value="0" />
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="FontFamily" Value="{StaticResource WidgetFontFamily}" />
    <Setter Property="FontSize" Value="11" />
    <Setter Property="FontWeight" Value="SemiBold" />
    <Setter Property="Foreground" Value="{DynamicResource AccentTextBrush}" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Border Background="Transparent" Padding="{TemplateBinding Padding}">
            <ContentPresenter VerticalAlignment="Center" />
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <Trigger Property="IsEnabled" Value="False">
        <Setter Property="Foreground" Value="{DynamicResource TextTertiaryBrush}" />
        <Setter Property="Cursor" Value="Arrow" />
      </Trigger>
    </Style.Triggers>
  </Style>
```

- [ ] **Step 2: Write the quota row view**

Create `src/AiUsageMonitor.App/Views/QuotaRowView.xaml`. Note the column definitions: the design's `grid-template-columns:1fr 42px 62px` with an 8px `column-gap` is five WPF columns, so the gutters are exact rather than approximated with margins.

```xml
<UserControl x:Class="AiUsageMonitor.App.Views.QuotaRowView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:controls="clr-namespace:AiUsageMonitor.App.Controls"
             AutomationProperties.Name="{Binding AccessibleName}"
             ToolTip="{Binding IdentifierTooltip}">
  <UserControl.Resources>
    <Style x:Key="RowLabelStyle" TargetType="TextBlock" BasedOn="{StaticResource BodyTextStyle}">
      <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}" />
      <Setter Property="TextTrimming" Value="CharacterEllipsis" />
      <Style.Triggers>
        <DataTrigger Binding="{Binding IsStale}" Value="True">
          <Setter Property="Foreground" Value="{DynamicResource TextTertiaryBrush}" />
        </DataTrigger>
      </Style.Triggers>
    </Style>
    <Style x:Key="RowPercentStyle" TargetType="TextBlock" BasedOn="{StaticResource BodyTextStyle}">
      <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}" />
      <Setter Property="FontWeight" Value="Medium" />
      <Setter Property="TextAlignment" Value="Right" />
      <Style.Triggers>
        <DataTrigger Binding="{Binding IsStale}" Value="True">
          <Setter Property="Foreground" Value="{DynamicResource TextTertiaryBrush}" />
        </DataTrigger>
        <DataTrigger Binding="{Binding IsExhausted}" Value="True">
          <Setter Property="FontWeight" Value="SemiBold" />
        </DataTrigger>
      </Style.Triggers>
    </Style>
    <Style x:Key="RowCountdownStyle" TargetType="TextBlock" BasedOn="{StaticResource NumericTextStyle}">
      <Setter Property="Foreground" Value="{DynamicResource TextSecondaryBrush}" />
      <Setter Property="TextAlignment" Value="Right" />
      <Style.Triggers>
        <DataTrigger Binding="{Binding IsStale}" Value="True">
          <Setter Property="Foreground" Value="{DynamicResource TextTertiaryBrush}" />
        </DataTrigger>
        <DataTrigger Binding="{Binding IsExhausted}" Value="True">
          <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}" />
          <Setter Property="FontWeight" Value="SemiBold" />
        </DataTrigger>
      </Style.Triggers>
    </Style>
  </UserControl.Resources>

  <Border BorderBrush="{DynamicResource WidgetRowDividerBrush}" BorderThickness="0,1,0,0"
          Padding="{DynamicResource QuotaRowPadding}">
    <Grid>
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="8" />
        <ColumnDefinition Width="42" />
        <ColumnDefinition Width="8" />
        <ColumnDefinition Width="62" />
      </Grid.ColumnDefinitions>
      <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />
        <RowDefinition Height="Auto" />
        <RowDefinition Height="Auto" />
      </Grid.RowDefinitions>

      <!-- Style is a property element, not an attribute: setting Style= as well would be assigning the
           same property twice, which is a XAML compile error. RowLabelStyle arrives through BasedOn.
           Visibility is driven by the trigger alone - a local Visibility binding would outrank it. -->
      <TextBlock Grid.Row="0" Grid.Column="0" Text="{Binding Label}" VerticalAlignment="Center">
        <TextBlock.Style>
          <Style TargetType="TextBlock" BasedOn="{StaticResource RowLabelStyle}">
            <Setter Property="Visibility" Value="Visible" />
            <Style.Triggers>
              <DataTrigger Binding="{Binding IsProviderToken}" Value="True">
                <Setter Property="Visibility" Value="Collapsed" />
              </DataTrigger>
            </Style.Triggers>
          </Style>
        </TextBlock.Style>
      </TextBlock>

      <Border Grid.Row="0" Grid.Column="0" HorizontalAlignment="Left" VerticalAlignment="Center"
              Padding="4,1" CornerRadius="{DynamicResource RadiusChip}"
              Background="{DynamicResource WidgetTokenChipBackgroundBrush}"
              BorderBrush="{DynamicResource WidgetTokenChipStrokeBrush}" BorderThickness="1"
              Visibility="{Binding IsProviderToken, Converter={StaticResource BooleanToVisibility}}">
        <TextBlock Text="{Binding Label}" Style="{StaticResource TokenTextStyle}"
                   Foreground="{DynamicResource TextSecondaryBrush}" />
      </Border>

      <TextBlock Grid.Row="0" Grid.Column="2" VerticalAlignment="Center"
                 Style="{StaticResource RowPercentStyle}" Text="{Binding UsedText}" />

      <TextBlock Grid.Row="0" Grid.Column="4" VerticalAlignment="Center"
                 Style="{StaticResource RowCountdownStyle}" Text="{Binding CountdownText}" />

      <!-- Margin 2 rather than the design's 5: QuotaBar is 11px tall and centres its 5px track,
           so 3px of the gap is already inside the control. -->
      <controls:QuotaBar Grid.Row="1" Grid.Column="0" Grid.ColumnSpan="5" Margin="0,2,0,0"
                         UsedPercent="{Binding UsedPercent}"
                         ElapsedFraction="{Binding ElapsedFraction}"
                         ColorBarsByUsage="{Binding ColorBarsByUsage}"
                         IsStale="{Binding IsStale}" />

      <StackPanel Grid.Row="2" Grid.Column="0" Grid.ColumnSpan="5" Orientation="Horizontal" Margin="0,6,0,0"
                  Visibility="{Binding IsExhausted, Converter={StaticResource BooleanToVisibility}}">
        <TextBlock Text="&#x2715;" FontSize="10" FontWeight="Bold" VerticalAlignment="Center"
                   Foreground="{DynamicResource StateBadBrush}" />
        <TextBlock Text="Limit reached" Margin="5,0,0,0" Style="{StaticResource BodySmallTextStyle}"
                   FontWeight="SemiBold" Foreground="{DynamicResource StateBadBrush}" />
        <StackPanel Orientation="Horizontal"
                    Visibility="{Binding HasCountdown, Converter={StaticResource BooleanToVisibility}}">
          <TextBlock Text="&#x00B7; resets in" Margin="5,0,0,0" Style="{StaticResource BodySmallTextStyle}"
                     Foreground="{DynamicResource TextSecondaryBrush}" />
          <TextBlock Text="{Binding CountdownText}" Margin="5,0,0,0" Style="{StaticResource BodySmallTextStyle}"
                     FontWeight="SemiBold" Foreground="{DynamicResource TextPrimaryBrush}" />
        </StackPanel>
      </StackPanel>
    </Grid>
  </Border>
</UserControl>
```

The label and the chip are mutually exclusive and share `Grid.Column="0"`: the label is visible when `IsProviderToken` is false, the chip when it is true. That is why the label's visibility comes from a trigger while the chip's comes from a plain converter binding — the trigger is the inverted one, and `BooleanToVisibility` is not inverting.

Create `src/AiUsageMonitor.App/Views/QuotaRowView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace AiUsageMonitor.App.Views;

public partial class QuotaRowView : UserControl
{
    public QuotaRowView() => InitializeComponent();
}
```

- [ ] **Step 3: Write the provider card view**

Create `src/AiUsageMonitor.App/Views/ProviderCardView.xaml`:

```xml
<UserControl x:Class="AiUsageMonitor.App.Views.ProviderCardView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:controls="clr-namespace:AiUsageMonitor.App.Controls"
             xmlns:views="clr-namespace:AiUsageMonitor.App.Views">
  <Border Background="{DynamicResource WidgetLayerBackgroundBrush}"
          BorderBrush="{DynamicResource WidgetCardStrokeBrush}" BorderThickness="1"
          CornerRadius="{DynamicResource RadiusCard}" Padding="{DynamicResource ProviderCardPadding}">
    <StackPanel>

      <Grid>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="Auto" />
          <ColumnDefinition Width="Auto" />
          <ColumnDefinition Width="Auto" />
          <ColumnDefinition Width="*" />
          <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>

        <Border Grid.Column="0" Width="16" Height="16" CornerRadius="{DynamicResource RadiusChip}"
                Background="{DynamicResource WidgetTokenChipBackgroundBrush}"
                BorderBrush="{DynamicResource WidgetTokenChipStrokeBrush}" BorderThickness="1"
                VerticalAlignment="Center">
          <TextBlock Text="{Binding Monogram}" FontSize="8.5" FontWeight="Bold"
                     HorizontalAlignment="Center" VerticalAlignment="Center"
                     Foreground="{DynamicResource TextSecondaryBrush}" />
        </Border>

        <TextBlock Grid.Column="1" Margin="7,0,0,0" VerticalAlignment="Center"
                   Style="{StaticResource SubtitleTextStyle}" Text="{Binding DisplayName}"
                   Foreground="{DynamicResource TextPrimaryBrush}" />

        <TextBlock Grid.Column="2" Margin="7,0,0,0" VerticalAlignment="Center"
                   Style="{StaticResource CaptionTextStyle}" Text="{Binding VersionText}"
                   Foreground="{DynamicResource TextTertiaryBrush}" />

        <controls:TierBadge Grid.Column="4" VerticalAlignment="Center" Tier="{Binding Tier}" />
      </Grid>

      <StackPanel Orientation="Horizontal" Margin="0,4,0,7">
        <controls:StateChip State="{Binding State}" Label="{Binding StateLabel}" VerticalAlignment="Center" />
        <TextBlock Margin="6,0,0,0" VerticalAlignment="Center" Style="{StaticResource CaptionTextStyle}"
                   Foreground="{DynamicResource TextTertiaryBrush}">
          <Run Text="&#x00B7;" /><Run Text=" " /><Run Text="{Binding UpdatedText, Mode=OneWay}" />
        </TextBlock>
      </StackPanel>

      <Border Margin="0,0,0,6" Padding="7,5" CornerRadius="{DynamicResource RadiusControl}"
              Background="{DynamicResource WidgetLayerAltBackgroundBrush}"
              BorderBrush="{DynamicResource WidgetCardStrokeBrush}" BorderThickness="1"
              Visibility="{Binding IsStale, Converter={StaticResource BooleanToVisibility}}">
        <Grid>
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="Auto" />
          </Grid.ColumnDefinitions>
          <Rectangle Grid.Column="0" Width="7" Height="7" Margin="0,3,6,0" VerticalAlignment="Top"
                     Fill="{DynamicResource StateWarnBrush}" RenderTransformOrigin="0.5,0.5">
            <Rectangle.RenderTransform>
              <RotateTransform Angle="45" />
            </Rectangle.RenderTransform>
          </Rectangle>
          <TextBlock Grid.Column="1" TextWrapping="Wrap" Style="{StaticResource CaptionTextStyle}"
                     Foreground="{DynamicResource TextSecondaryBrush}">
            <Run Text="Values may no longer be current. Last successful update" /><Run Text=" " /><Run Text="{Binding StaleAgeText, Mode=OneWay}" /><Run Text="." />
          </TextBlock>
          <Button Grid.Column="2" Margin="6,0,0,0" VerticalAlignment="Top" Content="Refresh"
                  Style="{StaticResource LinkButtonStyle}" Command="{Binding RetryCommand}" />
        </Grid>
      </Border>

      <!-- Column captions stay in the default widget: PRD §16 requires the percentage's direction
           to be explicit, and a bare "47%" in an unlabelled column is not. See this task's note. -->
      <Grid Margin="0,0,0,3" Visibility="{Binding HasWindows, Converter={StaticResource BooleanToVisibility}}">
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="*" />
          <ColumnDefinition Width="8" />
          <ColumnDefinition Width="42" />
          <ColumnDefinition Width="8" />
          <ColumnDefinition Width="62" />
        </Grid.ColumnDefinitions>
        <TextBlock Grid.Column="0" Text="WINDOW" Style="{StaticResource CaptionMicroTextStyle}"
                   Foreground="{DynamicResource TextTertiaryBrush}" />
        <TextBlock Grid.Column="2" Text="USED" TextAlignment="Right" Style="{StaticResource CaptionMicroTextStyle}"
                   Foreground="{DynamicResource TextTertiaryBrush}" />
        <TextBlock Grid.Column="4" Text="RESETS IN" TextAlignment="Right" Style="{StaticResource CaptionMicroTextStyle}"
                   Foreground="{DynamicResource TextTertiaryBrush}" />
      </Grid>

      <ItemsControl ItemsSource="{Binding Windows}" Focusable="False">
        <ItemsControl.ItemTemplate>
          <DataTemplate>
            <views:QuotaRowView />
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>

      <Grid Margin="0,8,0,0" Visibility="{Binding HasNotice, Converter={StaticResource BooleanToVisibility}}">
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="Auto" />
          <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
        <Polygon Grid.Column="0" Points="0,9 5,0 10,9" Margin="0,3,7,0" VerticalAlignment="Top"
                 Fill="{DynamicResource StateBadBrush}"
                 Visibility="{Binding Notice.IsAlert, Converter={StaticResource BooleanToVisibility}}" />
        <StackPanel Grid.Column="1">
          <!-- Foreground is set by the setters below, never as an attribute: a locally set value
               outranks a trigger, so the alert colour would never apply. -->
          <TextBlock Text="{Binding Notice.Title}" TextWrapping="Wrap" FontSize="12" FontWeight="SemiBold"
                     FontFamily="{StaticResource WidgetFontFamily}">
            <TextBlock.Style>
              <Style TargetType="TextBlock">
                <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}" />
                <Style.Triggers>
                  <DataTrigger Binding="{Binding Notice.IsAlert}" Value="True">
                    <Setter Property="Foreground" Value="{DynamicResource StateBadBrush}" />
                  </DataTrigger>
                </Style.Triggers>
              </Style>
            </TextBlock.Style>
          </TextBlock>
          <TextBlock Text="{Binding Notice.Body}" TextWrapping="Wrap" Margin="0,2,0,0"
                     Style="{StaticResource CaptionTextStyle}"
                     Foreground="{DynamicResource TextSecondaryBrush}" />
          <!-- A notice without an action has no button: a click target with no caption is worse
               than no control at all. Waiting and Empty are the states that reach this. -->
          <Button Margin="0,7,0,0" HorizontalAlignment="Left" Padding="9,3"
                  Content="{Binding Notice.ActionText}" Command="{Binding RetryCommand}">
            <Button.Style>
              <Style TargetType="Button" BasedOn="{StaticResource LinkButtonStyle}">
                <Setter Property="Visibility" Value="Visible" />
                <Style.Triggers>
                  <DataTrigger Binding="{Binding Notice.ActionText}" Value="{x:Null}">
                    <Setter Property="Visibility" Value="Collapsed" />
                  </DataTrigger>
                </Style.Triggers>
              </Style>
            </Button.Style>
          </Button>
        </StackPanel>
      </Grid>
    </StackPanel>
  </Border>
</UserControl>
```

Create `src/AiUsageMonitor.App/Views/ProviderCardView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace AiUsageMonitor.App.Views;

public partial class ProviderCardView : UserControl
{
    public ProviderCardView() => InitializeComponent();
}
```

- [ ] **Step 4: Add `HasWindows` to the card view model**

`ProviderCardView` binds `HasWindows`. Add it to `ProviderCardViewModel`:

```csharp
    public bool HasWindows => Windows.Count > 0;
```

and raise it at the end of the rebuild loop in `Apply`:

```csharp
        Raise(nameof(HasWindows));
```

- [ ] **Step 5: Write the view-loading tests**

Create `tests/AiUsageMonitor.App.Tests/ViewLoadingTests.cs`:

```csharp
using System.Windows;
using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.App.Views;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;

namespace AiUsageMonitor.App.Tests;

[Collection("wpf")]
public class ViewLoadingTests(WpfFixture wpf)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed class SilentProbe(string name) : IProviderProbe
    {
        public string Name => name;

        public Task<ProviderSnapshot> ProbeAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private static QuotaWindow Window(double? used, bool token, bool withReset) => new(
        Id: token ? "nimbus_quill" : "five_hour",
        Label: token ? "nimbus_quill" : "5-hour window",
        UsedPercent: used,
        ResetsAt: withReset ? Now.AddHours(4) : null,
        WindowDuration: withReset ? TimeSpan.FromHours(5) : null,
        Order: 0,
        IsPartial: !withReset,
        Extra: new Dictionary<string, string>(),
        LabelIsProviderToken: token);

    private static ProviderSnapshot Snapshot(ConnectionState state, IReadOnlyList<QuotaWindow> windows) => new(
        ProviderName: "Claude Code", Installed: true, Version: "2.1.227", ExecutablePath: null,
        State: state, Mechanism: "stub", Tier: MechanismTier.Unofficial, UpdateModel: "pull (poll)",
        Windows: windows, RetrievedAt: state == ConnectionState.NotInstalled ? null : Now,
        Error: null, Notes: []);

    [Theory]
    [InlineData(47, false, true)]
    [InlineData(100, false, true)]
    [InlineData(34, true, false)]
    [InlineData(null, false, false)]
    public void EveryRowFormRendersWithoutThrowing(double? used, bool token, bool withReset) => wpf.Invoke(() =>
    {
        QuotaRowViewModel row = new(Window(used, token, withReset), colorBarsByUsage: true);
        row.Tick(Now);

        ControlLoadingTests.Measured(new QuotaRowView { DataContext = row, Width = 320 });
    });

    [Theory]
    [InlineData(ConnectionState.Connected)]
    [InlineData(ConnectionState.Stale)]
    [InlineData(ConnectionState.NotInstalled)]
    [InlineData(ConnectionState.Unavailable)]
    [InlineData(ConnectionState.Error)]
    [InlineData(ConnectionState.Waiting)]
    [InlineData(ConnectionState.Unsupported)]
    [InlineData(ConnectionState.Discovering)]
    public void EveryCardStateRendersWithoutThrowing(ConnectionState state) => wpf.Invoke(() =>
    {
        ProviderCardViewModel card = new(
            new ProviderDescriptor("Claude Code", "CC", new SilentProbe("Claude Code")),
            colorBarsByUsage: true,
            _ => { });
        card.Apply(Snapshot(state, [Window(47, false, true), Window(34, true, false)]), Now, FreshnessPolicy.Default);

        ControlLoadingTests.Measured(new ProviderCardView { DataContext = card, Width = 340 });
    });

    [Fact]
    public void ACardWithNoWindowsStillRenders() => wpf.Invoke(() =>
    {
        ProviderCardViewModel card = new(
            new ProviderDescriptor("Codex", "CX", new SilentProbe("Codex")),
            colorBarsByUsage: false,
            _ => { });
        card.Apply(Snapshot(ConnectionState.Connected, []), Now, FreshnessPolicy.Default);

        FrameworkElement view = ControlLoadingTests.Measured(new ProviderCardView { DataContext = card, Width = 340 });
        Assert.True(view.ActualHeight > 0);
    });
}
```

- [ ] **Step 6: Build and test**

Run: `dotnet build`
Expected: 0 warnings, 0 errors.

Run: `dotnet test`
Expected: all pass. A `XamlParseException` here means a resource key is wrong — read the message, do not weaken the test.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: add the quota row and provider card views"
```

---

### Task 11: The widget window

Replaces the token gallery. The window is 360px wide with a custom 32px title bar, a scrolling provider list, and a 26px footer, per `docs/design/tokens.md` §3 and screen 1 of the design.

**Files:**
- Create: `src/AiUsageMonitor.App/Interop/DwmWindowChrome.cs`
- Create: `src/AiUsageMonitor.App/Views/WidgetWindow.xaml` + `.xaml.cs`
- Delete: `src/AiUsageMonitor.App/MainWindow.xaml`, `src/AiUsageMonitor.App/MainWindow.xaml.cs`
- Modify: `src/AiUsageMonitor.App/App.xaml.cs`
- Test: `tests/AiUsageMonitor.App.Tests/WidgetWindowTests.cs`

**Interfaces:**
- Consumes: `MainViewModel` (Task 9), `ProviderRegistry` (Task 1), `ProviderRefreshService` (Task 4), `AppSettings` + `AppSettingsStore` (Task 5), `ThemeManager`.

- [ ] **Step 1: Write the DWM helper**

Create `src/AiUsageMonitor.App/Interop/DwmWindowChrome.cs`:

```csharp
using System.Runtime.InteropServices;

namespace AiUsageMonitor.App.Interop;

/// <summary>
/// Windows 11 window presentation that WPF does not expose: rounded corners and a title-bar
/// border that follows the app theme. Every call is best-effort — on a Windows build that does
/// not know an attribute, DWM returns a failure code and the window is square-cornered, which is
/// cosmetic. Nothing here requires elevation.
/// </summary>
internal static class DwmWindowChrome
{
    private const int UseImmersiveDarkMode = 20;
    private const int WindowCornerPreference = 33;
    private const int RoundedCorners = 2;

    // DllImport rather than LibraryImport: the signature is a single blittable int by reference,
    // the source generator buys nothing here, and LibraryImport would pull AllowUnsafeBlocks into
    // a project that has no other need for it.
#pragma warning disable SYSLIB1054
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
#pragma warning restore SYSLIB1054

    public static void UseRoundedCorners(IntPtr handle) => Set(handle, WindowCornerPreference, RoundedCorners);

    public static void UseDarkTitleBar(IntPtr handle, bool dark) => Set(handle, UseImmersiveDarkMode, dark ? 1 : 0);

    private static void Set(IntPtr handle, int attribute, int value)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            _ = DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Older Windows. The window still works; it is square-cornered.
        }
    }
}
```

- [ ] **Step 2: Write the window**

Create `src/AiUsageMonitor.App/Views/WidgetWindow.xaml`:

```xml
<Window x:Class="AiUsageMonitor.App.Views.WidgetWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="clr-namespace:AiUsageMonitor.App.Views"
        Title="Quota Monitor"
        Width="360" MaxHeight="520" SizeToContent="Height"
        WindowStyle="None" ResizeMode="NoResize"
        Background="{DynamicResource WidgetWindowBackgroundBrush}"
        Foreground="{DynamicResource TextPrimaryBrush}"
        FontFamily="{StaticResource WidgetFontFamily}"
        UseLayoutRounding="True" SnapsToDevicePixels="True">
  <Border BorderBrush="{DynamicResource WidgetWindowStrokeBrush}" BorderThickness="1"
          CornerRadius="{DynamicResource RadiusWindow}">
    <DockPanel>

      <Grid DockPanel.Dock="Top" Height="32" Background="Transparent"
            MouseLeftButtonDown="TitleBar_MouseLeftButtonDown">
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="*" />
          <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>
        <TextBlock Grid.Column="0" Margin="11,0,0,0" VerticalAlignment="Center" Text="Quota Monitor"
                   FontSize="11.5" FontWeight="SemiBold" Foreground="{DynamicResource TextSecondaryBrush}" />
        <StackPanel Grid.Column="1" Orientation="Horizontal" Margin="0,0,4,0">
          <Button Width="30" Height="26" Content="&#x2212;" FontSize="12" Click="Minimise_Click"
                  Style="{StaticResource LinkButtonStyle}"
                  Foreground="{DynamicResource TextSecondaryBrush}"
                  AutomationProperties.Name="Minimise" />
          <Button Width="30" Height="26" Content="&#x2715;" FontSize="11" Click="Close_Click"
                  Style="{StaticResource LinkButtonStyle}"
                  Foreground="{DynamicResource TextSecondaryBrush}"
                  AutomationProperties.Name="Close" />
        </StackPanel>
      </Grid>

      <Border DockPanel.Dock="Bottom" Height="26" BorderThickness="0,1,0,0"
              BorderBrush="{DynamicResource WidgetWindowStrokeBrush}">
        <Grid Margin="11,0">
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="Auto" />
          </Grid.ColumnDefinitions>
          <TextBlock Grid.Column="0" VerticalAlignment="Center" Text="{Binding FooterText}"
                     Style="{StaticResource CaptionTextStyle}"
                     Foreground="{DynamicResource TextTertiaryBrush}" />
          <Button Grid.Column="1" VerticalAlignment="Center" Content="Refresh"
                  Style="{StaticResource LinkButtonStyle}" Command="{Binding RefreshCommand}" />
        </Grid>
      </Border>

      <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
        <!-- Cards carry an 8px bottom margin each, so the list's own bottom margin is 2 rather
             than 10 - together they make the 10px body padding the design specifies. -->
        <ItemsControl ItemsSource="{Binding Providers}" Margin="10,0,10,2" Focusable="False">
          <ItemsControl.ItemContainerStyle>
            <Style TargetType="ContentPresenter">
              <Setter Property="Margin" Value="0,0,0,8" />
            </Style>
          </ItemsControl.ItemContainerStyle>
          <ItemsControl.ItemTemplate>
            <DataTemplate>
              <views:ProviderCardView />
            </DataTemplate>
          </ItemsControl.ItemTemplate>
        </ItemsControl>
      </ScrollViewer>
    </DockPanel>
  </Border>
</Window>
```

Create `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using AiUsageMonitor.App.Interop;
using AiUsageMonitor.App.Theming;
using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Infrastructure.Settings;
using AiUsageMonitor.Infrastructure.Theming;

namespace AiUsageMonitor.App.Views;

public partial class WidgetWindow : Window
{
    private readonly MainViewModel _model;
    private readonly AppSettings _settings;
    private readonly AppSettingsStore? _store;
    private readonly ThemeManager? _theme;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _poll = new();

    public WidgetWindow(MainViewModel model, AppSettings settings, AppSettingsStore? store = null, ThemeManager? theme = null)
    {
        _model = model;
        _settings = settings;
        _store = store;
        _theme = theme;

        InitializeComponent();
        DataContext = model;

        Topmost = settings.AlwaysOnTop;
        RestorePlacement(settings);

        _tick.Tick += (_, _) => _model.Tick();
        _poll.Interval = settings.RefreshInterval;
        _poll.Tick += (_, _) => _ = _model.RefreshAsync(force: false);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        IntPtr handle = new WindowInteropHelper(this).Handle;
        DwmWindowChrome.UseRoundedCorners(handle);
        ApplyTitleBarTheme(handle);

        if (_theme is not null)
        {
            _theme.Changed += (_, _) => ApplyTitleBarTheme(new WindowInteropHelper(this).Handle);
        }
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        _tick.Start();
        _poll.Start();
        _ = _model.RefreshAsync(force: true);
    }

    protected override void OnClosed(EventArgs e)
    {
        _tick.Stop();
        _poll.Stop();
        SavePlacement();
        _model.Dispose();
        base.OnClosed(e);
    }

    private void ApplyTitleBarTheme(IntPtr handle) =>
        DwmWindowChrome.UseDarkTitleBar(handle, _theme?.Current == ThemeVariant.Dark);

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Minimise_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void RestorePlacement(AppSettings settings)
    {
        if (settings.WindowLeft is not double left || settings.WindowTop is not double top)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        // A saved position on a monitor that has since been unplugged would put the window
        // somewhere the user cannot reach it (PRD §17). Fall back to centring rather than trusting it.
        Rect desktop = new(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        Rect proposed = new(left, top, Width, 100);

        if (!desktop.IntersectsWith(proposed))
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left;
        Top = top;
    }

    private void SavePlacement()
    {
        if (_store is null)
        {
            return;
        }

        try
        {
            _store.Save(_settings with { WindowLeft = Left, WindowTop = Top });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing a window position is not a reason to fail a shutdown.
        }
    }
}
```

- [ ] **Step 3: Wire startup**

In `src/AiUsageMonitor.App/App.xaml.cs`, register the new services and show the new window. Add these registrations before `BuildServiceProvider`:

```csharp
        services.AddSingleton<IReadOnlyList<ProviderDescriptor>>(ProviderRegistry.CreateDefault());
        services.AddSingleton(provider => new ProviderRefreshService(
            provider.GetRequiredService<IReadOnlyList<ProviderDescriptor>>(),
            timeout: TimeSpan.FromSeconds(30),
            baseInterval: loaded.Settings.RefreshInterval,
            provider.GetRequiredService<ILogger<ProviderRefreshService>>()));
```

Then replace the `new MainWindow().Show()` body inside the existing `try` with:

```csharp
            MainViewModel model = new(
                _services.GetRequiredService<ProviderRefreshService>(),
                _services.GetRequiredService<IReadOnlyList<ProviderDescriptor>>(),
                loaded.Settings,
                () => DateTimeOffset.Now,
                action => Dispatcher.Invoke(action));

            new WidgetWindow(
                model,
                loaded.Settings,
                _services.GetRequiredService<AppSettingsStore>(),
                _services.GetRequiredService<ThemeManager>()).Show();
```

Keep the existing `catch` exactly as it is — a widget that cannot show its window must log Critical and shut down rather than leave a headless process. Add the `using` directives the new types need.

- [ ] **Step 4: Delete the gallery**

Delete `src/AiUsageMonitor.App/MainWindow.xaml` and `src/AiUsageMonitor.App/MainWindow.xaml.cs`. It was scaffolding, and it is now replaced.

- [ ] **Step 5: Write the window test**

Create `tests/AiUsageMonitor.App.Tests/WidgetWindowTests.cs`:

```csharp
using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.App.Views;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Refresh;
using AiUsageMonitor.Infrastructure.Settings;

namespace AiUsageMonitor.App.Tests;

[Collection("wpf")]
public class WidgetWindowTests(WpfFixture wpf)
{
    private sealed class SilentProbe(string name) : IProviderProbe
    {
        public string Name => name;

        public Task<ProviderSnapshot> ProbeAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    [Fact]
    public void TheWindowConstructsWithoutXamlErrors() => wpf.Invoke(() =>
    {
        // Constructing the window runs InitializeComponent, which is where a bad resource key or a
        // control whose static initializer throws surfaces - the failure that once shipped green.
        IReadOnlyList<ProviderDescriptor> providers =
        [
            new("Claude Code", "CC", new SilentProbe("Claude Code")),
            new("Codex", "CX", new SilentProbe("Codex"))
        ];

        ProviderRefreshService service = new(providers, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60));
        MainViewModel model = new(service, providers, AppSettings.Default, () => DateTimeOffset.Now);

        WidgetWindow window = new(model, AppSettings.Default);

        Assert.Equal(360, window.Width);
        Assert.Equal(520, window.MaxHeight);

        model.Dispose();
    });
}
```

- [ ] **Step 6: Build and test**

Run: `dotnet build`
Expected: 0 warnings, 0 errors.

Run: `dotnet test`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: replace the gallery with the live widget window"
```

**Do not run the application.** `dotnet run --project src/AiUsageMonitor.App` opens a GUI window that never exits on its own; a background worker cannot see it and will hang. Visual verification is done by the session owner in Task 12. Report anything you could not verify.

---

### Task 12: Visual and live verification (session owner, not delegated)

- [ ] Launch the published or Debug executable, screenshot it in light and dark, and compare against screen 1 of `docs/design/widget-states.html`.
- [ ] Confirm both provider cards appear, with real percentages, real countdowns ticking, and the correct tier badge on each — Codex **Official**, Claude Code **Unofficial**.
- [ ] Confirm the elapsed marker sits inside both end caps and stays legible on the exhausted hatched fill.
- [ ] Confirm no credential, path, or token appears anywhere on screen.
- [ ] Drag the window, close it, relaunch, and confirm it reopens where it was left.
- [ ] Force an error state (rename the Codex executable directory, or move `.credentials.json` aside — restore both afterwards) and confirm the card degrades to a notice rather than fabricating a value, and that the other provider keeps working.
- [ ] Confirm the 125% / 150% display-scaling case, which is still outstanding from the previous increment.

---

## Out of scope

Deliberately not in this increment. Each is a coherent piece of work on its own, and naming them here is what stops them leaking in.

- **Compact mode.** The design's cut order is specified, and `AppSettings.Density` already exists, but nothing in this increment can reach it: with no settings window and no tray menu there is no way for a user to switch modes. It ships with the toggle.
- **The expanded single-provider card** — the back arrow, per-row duration provenance ("Duration inferred from window name"), and the `Refresh / Diagnostics / N windows reported` footer.
- **Settings window, diagnostics window, and system tray**, including the title bar's `≡` menu button. The button is omitted rather than drawn disabled — dead UI is worse than absent UI.
- **Mica backdrop.** `tokens.md` describes the window fill as Mica-backed; this increment paints the solid `WidgetWindowBackgroundBrush` token. Mica needs an extended frame and `DWMWA_SYSTEMBACKDROP_TYPE`, and it interacts with the custom title bar.
- **Provider plan metadata** (the design's "· plus" beside the Codex version). No probe reports a plan today; inventing one is forbidden.
- **`Start with Windows`, `Always on top` and `Show unavailable providers` as live toggles.** `AlwaysOnTop` is honoured from the settings file at startup; the rest wait for the settings window.
- **Keyboard-only navigation and screen-reader verification** beyond the `AutomationProperties.Name` values this plan sets. PRD §25 requires a verification pass; it needs the full window set to be worth doing once.

## Self-review

**Spec coverage.** PRD §9 (name, version, state, last update, freshness, dynamic windows, error state) — Tasks 2, 8, 10. §10 (all eight states, never colour alone) — Tasks 8, 10, and the existing `StateGlyph`. §13 (unknown labels preserved, identifier reachable) — Task 7. §14 (local countdowns, no per-second provider calls; freshness recorded) — Tasks 7, 8, 9. §15 (card contents, quota row contents) — Tasks 8, 10. §16 (bar, marker, explicit direction) — Task 10, including the column-caption decision. §16.1 (three bands, exhausted regardless of setting) — Task 7 plus the existing `QuotaBarFillSelector`. §17 (drag, remembered position, multi-monitor recovery, always-on-top) — Tasks 5, 11. §18 (stale banner, age, de-emphasis) — Tasks 8, 10. §21 (layering: providers in infrastructure, view models in presentation, no provider branches in UI) — Tasks 1, 7–10. §24 (async, cancellable, timeout-bounded, backoff, isolation) — Task 4. §25 (view-model behaviour for empty/partial/stale/error; light, dark and high-contrast dictionaries load) — Tasks 6–10.

Not covered by any task, by design: §19 settings UI, §20 diagnostics, §17 tray. All are listed in Out of scope.

**Placeholder scan.** Every code step contains the code to write, correct as written and meant to be transcribed. No step asks the implementer to repair the plan's own code, and there are no "add error handling", "similar to Task N", or "write tests for the above" steps.

**XAML property-assignment rule, applied three times.** A property set as an attribute *and* as a property element is a compile error, and a locally set value silently outranks any trigger that targets the same property. So wherever a trigger drives `Visibility` or `Foreground` — the quota row's label, the notice title, the notice action button — that property is set only inside the `Style`, with the base style arriving through `BasedOn`. The plain converter bindings elsewhere are on elements no trigger touches.

**Type consistency.** `ProviderDescriptor(DisplayName, Monogram, Probe)` is used identically in Tasks 1, 4, 8, 9, 11. `ProviderRefreshService(providers, timeout, baseInterval, logger)` and its `RefreshAllAsync(force, now, ct)` / `RefreshAsync(provider, now, ct)` signatures match between Task 4's implementation and Task 9's consumer. `QuotaRowViewModel(window, colorBarsByUsage)` matches between Tasks 7, 8 and 10. `ProviderCardViewModel(descriptor, colorBarsByUsage, retry)` matches between Tasks 8, 9 and 10. `RelativeTime.FormatAge` returns `string?` in Task 3 and is consumed as `string?` in Task 8. `HasWindows` is added in Task 10 Step 4 because Task 10's XAML binds it — it is not assumed to exist from Task 8.
