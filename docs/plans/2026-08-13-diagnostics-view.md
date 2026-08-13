# Diagnostics View Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the diagnostics screen PRD §20 requires — per provider and for the application — reachable from the tray menu and the settings window, with a copyable summary that has local paths and user names masked.

**Architecture:** A new `DiagnosticsWindow` bound to a `DiagnosticsViewModel` that is a pure projection of state the application already holds: the latest `ProviderSnapshot` per card, per-provider attempt state newly exposed by `ProviderRefreshService`, and a new `EnvironmentReport` in Infrastructure that reads runtime/OS/privilege facts. The same view model renders the plain-text bundle, so what is copied is what is shown, minus redaction.

**Tech Stack:** .NET 10, WPF, `System.Text.Json` (no new packages), xUnit.

This implements **C10** from `docs/specs/2026-08-13-feature-inventory-and-ideas.md` §2.2. It does not implement X10 (§3.2) beyond the fields PRD §20 already requires; where X10's attempt-oriented fields are also §20 fields, they are here.

## Global Constraints

Every task's requirements implicitly include this section.

- **Warnings are errors.** `dotnet build` must finish with `0 Warning(s)`.
- **Run build and test as separate commands, never chained.** You are subject to a per-command time limit; a chained `build && test` spends the whole budget on one call.
- **The build lock is expected, not a defect.** The user's `AiUsageMonitor.App.exe` may be running and holding `src/AiUsageMonitor.App/bin/…`, which makes a plain `dotnet build` fail with `MSB3021`. **Never kill that process.** Build and test with an out-of-tree output path instead:
  - `dotnet build -p:BaseOutputPath=$env:TEMP/aium-build/`
  - `dotnet test -p:BaseOutputPath=$env:TEMP/aium-build/`
- **Credentials are in-memory only.** Never log, persist, cache, display, or copy a token. A token must never reach `Extra`, an exception message, or any diagnostic output. This is PRD §4.1.1 and is the single hardest constraint in this plan, because this plan builds a diagnostic dump.
- **Diagnostics must never display** (PRD §20): authentication tokens, cookies, full provider configuration contents, prompt text, repository paths unless the user explicitly chooses to include them, or raw provider messages that may contain secrets.
- **`ProviderSnapshot.Notes` may contain a local credentials file path.** It is allowed on the diagnostics screen (the user opened it deliberately, on their own machine) and must go through redaction before it reaches the clipboard. It must **never** be surfaced in the widget itself.
- **Missing data is `null` and renders as an explicit absence marker — never as `0` or an empty string that reads like a value.**
- **The domain model stays provider-neutral.** No property named after a plan period; nothing branches on which provider it is.
- **Every mechanism carries a visible tier.** A value obtained unofficially is never presented as official.
- **No administrator privileges**, and no new `PackageReference`.
- **User- and machine-agnostic.** Resolve per-user locations with `Environment.GetFolderPath`; never hardcode a user path.
- **One commit per task**, created serially.

## File Structure

**Create:**
- `src/AiUsageMonitor.Infrastructure/Diagnostics/EnvironmentReport.cs` — runtime, OS, privilege and logging facts. One responsibility: read the environment, return a record. No formatting.
- `src/AiUsageMonitor.Infrastructure/Diagnostics/StartupReport.cs` — what happened at startup, recorded by `App` and read by diagnostics.
- `src/AiUsageMonitor.Infrastructure/Diagnostics/DiagnosticRedaction.cs` — masks the user profile path and user name in any string. Pure, static, testable.
- `src/AiUsageMonitor.App/ViewModels/DiagnosticsViewModel.cs` — the projection and the bundle builder.
- `src/AiUsageMonitor.App/ViewModels/DiagnosticSection.cs` and `DiagnosticField.cs` — the label/value rows the view binds to.
- `src/AiUsageMonitor.App/Views/DiagnosticsWindow.xaml` + `.xaml.cs`.
- `tests/AiUsageMonitor.Infrastructure.Tests/DiagnosticRedactionTests.cs`
- `tests/AiUsageMonitor.Infrastructure.Tests/EnvironmentReportTests.cs`
- `tests/AiUsageMonitor.App.Tests/DiagnosticsViewModelTests.cs`

**Modify:**
- `src/AiUsageMonitor.Infrastructure/Refresh/ProviderRefreshService.cs` — expose per-provider attempt state.
- `src/AiUsageMonitor.Domain/IProviderProbe.cs` — one new mechanism fact.
- `src/AiUsageMonitor.Infrastructure/Providers/Claude/ClaudeOAuthUsageProbe.cs`, `…/Codex/CodexProbe.cs` — state that fact explicitly.
- `src/AiUsageMonitor.App/ViewModels/ProviderCardViewModel.cs` — expose the latest snapshot.
- `src/AiUsageMonitor.App/Views/WidgetWindow.xaml` — a tray menu item.
- `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs` — open the window; pass the opener to `SettingsViewModel`.
- `src/AiUsageMonitor.App/ViewModels/SettingsViewModel.cs` + `Views/SettingsWindow.xaml` — an "Open diagnostics" action.
- `src/AiUsageMonitor.App/App.xaml.cs` — register `EnvironmentReport` and `StartupReport`.
- `tests/AiUsageMonitor.Infrastructure.Tests/ProviderRefreshServiceTests.cs`, `tests/AiUsageMonitor.App.Tests/SettingsViewModelTests.cs`, `tests/AiUsageMonitor.App.Tests/ViewLoadingTests.cs` — extend.

---

### Task 1: Per-provider attempt state on the refresh service

**Files:**
- Modify: `src/AiUsageMonitor.Infrastructure/Refresh/ProviderRefreshService.cs`
- Test: `tests/AiUsageMonitor.Infrastructure.Tests/ProviderRefreshServiceTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  namespace AiUsageMonitor.Infrastructure.Refresh;

  /// <summary>
  /// What the service knows about one provider's polling history. PRD §20 requires the last
  /// discovery time, the last successful refresh, and enough about deferral to explain a card
  /// that has not moved. All of it already existed inside this service; none of it was readable.
  /// </summary>
  public sealed record ProviderActivity(
      DateTimeOffset? LastAttemptStartedAt,
      DateTimeOffset? LastCompletedAt,
      DateTimeOffset? LastSuccessAt,
      DateTimeOffset? NextAttemptAt,
      int ConsecutiveFailures,
      bool IsInFlight);

  // on ProviderRefreshService:
  public ProviderActivity ActivityFor(ProviderDescriptor provider, DateTimeOffset now);
  ```

**Requirements:**

- `ActivityFor` never throws and never returns null. A provider that has never been probed returns all-null timestamps, `ConsecutiveFailures: 0`, `IsInFlight: false`.
- `LastAttemptStartedAt` is stamped in `StartRefreshAsync` when an attempt is actually started — not when one is skipped for backoff or because another is in flight. This is PRD §20's "last discovery time".
- `LastCompletedAt` is stamped whenever an attempt publishes a snapshot, success or failure.
- `LastSuccessAt` is stamped only when the published snapshot's `RetrievedAt` is non-null. Do not infer it from the state enum.
- `NextAttemptAt` follows the existing `NextAttemptFor` semantics exactly: the scheduled instant when it is still in the future, otherwise `null`. Reuse `NextAttemptFor` rather than duplicating the comparison.
- All reads happen under the existing `_gate` lock. `ActivityFor` must be safe to call from the UI thread while a probe is in flight on another.
- `NextAttemptFor` keeps its current signature and behaviour — `ProviderCardViewModel` depends on it.

**Acceptance criteria (tests to write):**

- A provider that has never been probed returns an all-empty `ProviderActivity`.
- After one successful refresh, `LastAttemptStartedAt`, `LastCompletedAt` and `LastSuccessAt` are all set and `ConsecutiveFailures` is 0.
- After a refresh that produced an `Error` snapshot, `LastSuccessAt` stays null while `LastCompletedAt` is set, and `ConsecutiveFailures` is 1.
- After a success following two failures, `ConsecutiveFailures` is back to 0 and `LastSuccessAt` moved.
- An unforced call that is skipped because the provider is in backoff does **not** move `LastAttemptStartedAt`.

- [ ] **Step 1:** Add `ProviderActivity` and the tracking fields, keeping every mutation inside `_gate`.
- [ ] **Step 2:** Write the tests above; run `dotnet test -p:BaseOutputPath=$env:TEMP/aium-build/`.
- [ ] **Step 3:** Commit — `feat: expose per-provider attempt state for diagnostics`.

---

### Task 2: The environment, startup and redaction facts

**Files:**
- Create: `src/AiUsageMonitor.Infrastructure/Diagnostics/EnvironmentReport.cs`
- Create: `src/AiUsageMonitor.Infrastructure/Diagnostics/StartupReport.cs`
- Create: `src/AiUsageMonitor.Infrastructure/Diagnostics/DiagnosticRedaction.cs`
- Test: `tests/AiUsageMonitor.Infrastructure.Tests/DiagnosticRedactionTests.cs`
- Test: `tests/AiUsageMonitor.Infrastructure.Tests/EnvironmentReportTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  namespace AiUsageMonitor.Infrastructure.Diagnostics;

  public sealed record EnvironmentReport(
      string ApplicationVersion,
      string RuntimeVersion,
      string OperatingSystem,
      string LogDirectory,
      bool LogDirectoryWritable,
      bool IsElevated)
  {
      public static EnvironmentReport Capture();
  }

  /// <summary>What startup did, recorded once by App and never mutated afterwards.</summary>
  public sealed record StartupReport(DateTimeOffset StartedAt, string? SettingsBackupPath)
  {
      public bool SettingsWereUnreadable => SettingsBackupPath is not null;
  }

  public static class DiagnosticRedaction
  {
      public static string Redact(string text);
      public static string? Redact(string? text);
  }
  ```

**Requirements — `EnvironmentReport.Capture`:**

- `ApplicationVersion` = the entry assembly's `AssemblyInformationalVersionAttribute`, falling back to its `Version`, falling back to the literal `"unknown"`. Strip any `+<sha>` build-metadata suffix. **Do not invent a versioning scheme** — stamping a real version is a separate, later increment (C17).
- `RuntimeVersion` = `RuntimeInformation.FrameworkDescription`.
- `OperatingSystem` = `RuntimeInformation.OSDescription`.
- `LogDirectory` = `RollingFileLoggerProvider.DefaultDirectory`.
- `LogDirectoryWritable` = whether that directory exists or can be created **and** a file can be created and deleted in it. Any exception answers `false`; it must never throw.
- `IsElevated` = `new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)`, `false` on any exception. Reading this needs no elevation.
- `Capture()` must not throw for any reason. Every field falls back to a safe literal.

**Requirements — `DiagnosticRedaction.Redact`:**

- Replaces every case-insensitive occurrence of `Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)` with the literal `%USERPROFILE%`.
- Then replaces every case-insensitive occurrence of `Environment.UserName` with the literal `%USERNAME%`, but **only when `Environment.UserName` is at least 3 characters** — a two-character user name would corrupt unrelated text.
- Handles both `\` and `/` separators in the profile path: redact the path as written and with separators swapped.
- Returns the input unchanged when it contains neither. `Redact(null)` returns `null`.
- Never throws on an empty string.

**Acceptance criteria (tests to write):**

- A string containing the current user profile path comes back with `%USERPROFILE%` and without the original path, for both separator styles.
- A string containing the user name comes back with `%USERNAME%` (assert conditionally on `Environment.UserName.Length >= 3`).
- A string containing neither is returned byte-identical.
- `EnvironmentReport.Capture()` returns non-empty `ApplicationVersion`, `RuntimeVersion` and `OperatingSystem`, and does not throw.
- `Capture().ApplicationVersion` contains no `+` character.

- [ ] **Step 1:** Implement the three types.
- [ ] **Step 2:** Write the tests; run build and test as separate commands.
- [ ] **Step 3:** Commit — `feat: add environment, startup and redaction facts for diagnostics`.

---

### Task 3: One new mechanism fact, and the latest snapshot on the card

**Files:**
- Modify: `src/AiUsageMonitor.Domain/IProviderProbe.cs`
- Modify: `src/AiUsageMonitor.Infrastructure/Providers/Claude/ClaudeOAuthUsageProbe.cs`
- Modify: `src/AiUsageMonitor.Infrastructure/Providers/Codex/CodexProbe.cs`
- Modify: `src/AiUsageMonitor.App/ViewModels/ProviderCardViewModel.cs`
- Test: `tests/AiUsageMonitor.App.Tests/ProviderCardViewModelTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  // on IProviderProbe — a stable fact about the mechanism, like Mechanism and Tier.
  /// <summary>
  /// Whether reading usage through this mechanism contacts the provider's own first-party host
  /// (PRD §20). A property of the mechanism, never of the last call. Defaults to false so a probe
  /// that touches nothing but the local machine needs no ceremony; a probe that makes a network
  /// call MUST override this, and both shipped probes state it explicitly either way.
  /// </summary>
  bool MakesFirstPartyNetworkCall => false;

  // on ProviderCardViewModel
  /// <summary>
  /// The snapshot behind everything else on this card, for the diagnostics screen alone. Nothing
  /// in the widget binds to it: it carries <see cref="ProviderSnapshot.Notes"/>, which may contain
  /// a local credentials path and must never appear on an always-visible card.
  /// </summary>
  public ProviderSnapshot? LatestSnapshot { get; }
  ```

**Requirements:**

- `ClaudeOAuthUsageProbe` states `public bool MakesFirstPartyNetworkCall => true;` — it calls `api.anthropic.com`.
- `CodexProbe` states `public bool MakesFirstPartyNetworkCall => false;` — it launches a local process. (The `codex app-server` call reaches the network on OpenAI's side, but *this application* makes no network call; the field answers what this application does. Say exactly that in the XML doc so nobody "corrects" it later.)
- A default interface implementation is used specifically so the seven test stubs and the POC keep compiling unchanged. Do not add the member to any test stub.
- `LatestSnapshot` is the existing private `_snapshot` field, exposed read-only. It must not raise a property-changed notification and must not be bound in any XAML.

**Acceptance criteria (tests to write):**

- `ProviderCardViewModel.LatestSnapshot` is null before the first `Apply`, and is reference-equal to the applied snapshot afterwards.
- A grep-style assertion is not required, but confirm by inspection that no `.xaml` binds `LatestSnapshot`.

- [ ] **Step 1:** Add the interface member with its default, and the two explicit overrides.
- [ ] **Step 2:** Expose `LatestSnapshot`; add the test; build and test.
- [ ] **Step 3:** Commit — `feat: record whether a mechanism makes a first-party network call`.

---

### Task 4: The diagnostics view model and the copyable bundle

**Files:**
- Create: `src/AiUsageMonitor.App/ViewModels/DiagnosticField.cs`
- Create: `src/AiUsageMonitor.App/ViewModels/DiagnosticSection.cs`
- Create: `src/AiUsageMonitor.App/ViewModels/DiagnosticsViewModel.cs`
- Test: `tests/AiUsageMonitor.App.Tests/DiagnosticsViewModelTests.cs`

**Interfaces:**
- Consumes: `ProviderActivity` / `ActivityFor` (Task 1), `EnvironmentReport` / `StartupReport` / `DiagnosticRedaction` (Task 2), `MakesFirstPartyNetworkCall` / `LatestSnapshot` (Task 3).
- Produces:
  ```csharp
  /// <summary>One label/value pair. Value is never null: an absent fact renders as EmptyValue.</summary>
  public sealed record DiagnosticField(string Label, string Value);

  /// <summary>A titled block of fields, plus optional free-text lines rendered below them.</summary>
  public sealed class DiagnosticSection
  {
      public string Title { get; }
      public string? Subtitle { get; }
      public IReadOnlyList<DiagnosticField> Fields { get; }
      public IReadOnlyList<string> Lines { get; }
  }

  public sealed class DiagnosticsViewModel : ObservableObject
  {
      public const string EmptyValue = "—";

      public DiagnosticsViewModel(
          IReadOnlyList<ProviderCardViewModel> cards,
          IReadOnlyList<ProviderDescriptor> providers,
          ProviderRefreshService refresh,
          EnvironmentReport environment,
          StartupReport startup,
          string themeDescription,
          string displayScalingDescription,
          Func<DateTimeOffset> clock,
          Action<string> copyToClipboard,
          Action openLogs);

      public IReadOnlyList<DiagnosticSection> Sections { get; }
      public RelayCommand CopyCommand { get; }
      public RelayCommand OpenLogsCommand { get; }
      public string CopyHintText { get; }
      public string? CopyConfirmationText { get; }   // null until Copy runs
      public void Rebuild();                          // re-projects from current state
      public string BuildBundle();                    // the redacted plain text
  }
  ```

**Requirements — sections, in this order:**

1. One section per provider, in `providers` order, **including providers the user has hidden and providers that are not installed** — diagnostics exists to explain absence.
2. One final section, titled `Application`.

**Requirements — provider section fields, in this order.** Label text is verbatim; an absent value is `EmptyValue`.

| Label | Value |
|---|---|
| `Installed` | `Yes` / `No` — from `snapshot.Installed`; `EmptyValue` with no snapshot yet |
| `Executable` | `snapshot.ExecutablePath`, or `Not detected` |
| `Version` | `snapshot.Version`, or `Not reported` |
| `Connection state` | `ConnectionStateText.Label(card.State)` — the card's state, which includes freshness |
| `Freshness` | `Current` / `Stale` / `Never retrieved`, then ` · ` and the age via `RelativeTime.FormatAge` when there is a `RetrievedAt` |
| `Mechanism` | `snapshot.Mechanism`, else `descriptor.Probe.Mechanism` |
| `Mechanism tier` | `Official` or `Unofficial — undocumented, may break without notice` |
| `Update model` | `snapshot.UpdateModel` |
| `First-party network call` | `Yes — this application calls the provider's own host over TLS` / `No — this application reads the local machine only` |
| `Capabilities` | see below |
| `Last discovery` | local `g`-format instant + ` · ` + relative age, from `ProviderActivity.LastAttemptStartedAt` |
| `Last successful refresh` | same shape, from `LastSuccessAt`, else `Never` |
| `Next attempt` | local instant + ` · in ` + countdown when scheduled; `As soon as the next poll is due` when `NextAttemptAt` is null |
| `Consecutive failures` | the integer |
| `In flight` | `Yes` / `No` |
| `Last error` | `snapshot.Error`, or `None` |
| `Quota windows` | the count, e.g. `2` or `None reported` |

- `Capabilities` is derived from the latest snapshot's windows and is provider-neutral. Build it as a `; `-joined string from these three facts, in order: `reports N quota window(s)`, `reset times: reported` or `reset times: not reported`, `window durations: reported` or `window durations: not reported`. With no snapshot, `EmptyValue`.
- After the fields, `Lines` carries one line per discovered quota window, then the probe's notes:
  - Window line format: `<id> · <label> · <used>% · resets <local instant> · window <duration>` — each `· <segment>` **omitted entirely** when that value is null, never rendered as `0` or a placeholder. Append ` · partial data` when `IsPartial`. Append each `Extra` pair as ` · <key>: <value>`.
  - Notes lines are prefixed `note: ` and are taken verbatim from `snapshot.Notes`.
- A provider hidden by the user (a later increment adds that concept — if the property does not exist yet, skip this bullet) renders `Next attempt` as `Not scheduled — hidden by the user`.

**Requirements — application section fields:**

| Label | Value |
|---|---|
| `Application version` | `environment.ApplicationVersion` |
| `.NET runtime` | `environment.RuntimeVersion` |
| `Windows` | `environment.OperatingSystem` |
| `Theme` | the `themeDescription` argument |
| `Display scaling` | the `displayScalingDescription` argument |
| `Logging` | `Writing to <dir>` or `Not writing — the log folder is not writable · <dir>` |
| `Last startup` | `Succeeded · <local instant>`, and when `SettingsWereUnreadable`, append ` · the settings file could not be read and was backed up` |
| `Privileges` | `Standard user` or `Administrator` |

Its `Subtitle` is verbatim: `This application never requests administrator rights.`

**Requirements — the bundle:**

- `BuildBundle()` renders every section as plain text: the title, then `label: value` per field, then the lines, blank line between sections. First line is verbatim `Quota Monitor diagnostics`, second is the local timestamp from `clock()`.
- **The whole bundle passes through `DiagnosticRedaction.Redact` as the last step.** Not per field — once, over the finished text, so nothing added later can bypass it.
- `CopyCommand` calls `BuildBundle()`, hands it to the `copyToClipboard` delegate, then sets `CopyConfirmationText` to verbatim `Copied. Local paths are masked and no credentials are included.` and raises change notification.
- `CopyHintText` is verbatim: `Copying replaces your user folder and user name with placeholders. Credentials are never read into this screen.`

**Acceptance criteria (tests to write):**

- With two providers where one has never reported, both get a section, and the silent one's `Installed`, `Version` and `Last error` render `EmptyValue` / `Not reported` / `None` rather than blanks or zeros.
- A window with a null `UsedPercent` produces a line with **no** percent segment — assert the line does not contain `0%`.
- A window with no `ResetsAt` produces a line with no `resets` segment.
- `Mechanism tier` for an `Unofficial` probe contains the word `Unofficial`; for `Official` it is exactly `Official`.
- `First-party network call` says `Yes` for a probe whose `MakesFirstPartyNetworkCall` is true.
- `BuildBundle()` on a snapshot whose `Notes` contains the current user profile path produces text containing `%USERPROFILE%` and **not** containing the raw profile path.
- `BuildBundle()` never contains the literal string `Bearer` or `sk-ant` (guard test: construct a snapshot whose `Error` contains neither — the assertion is that no code path adds them).
- `CopyCommand.Execute(null)` sets `CopyConfirmationText` and passes a non-empty string to the delegate.
- `Rebuild()` after a card's state changes produces updated field values.

- [ ] **Step 1:** Build the three types.
- [ ] **Step 2:** Write the tests; build and test.
- [ ] **Step 3:** Commit — `feat: project provider and application diagnostics`.

---

### Task 5: The window, and the two ways in

**Files:**
- Create: `src/AiUsageMonitor.App/Views/DiagnosticsWindow.xaml`
- Create: `src/AiUsageMonitor.App/Views/DiagnosticsWindow.xaml.cs`
- Modify: `src/AiUsageMonitor.App/Views/WidgetWindow.xaml` (tray menu), `…/WidgetWindow.xaml.cs`
- Modify: `src/AiUsageMonitor.App/ViewModels/SettingsViewModel.cs`, `src/AiUsageMonitor.App/Views/SettingsWindow.xaml`
- Modify: `src/AiUsageMonitor.App/App.xaml.cs`
- Test: `tests/AiUsageMonitor.App.Tests/ViewLoadingTests.cs`, `tests/AiUsageMonitor.App.Tests/SettingsViewModelTests.cs`

**Interfaces:**
- Consumes: `DiagnosticsViewModel` (Task 4).
- Produces: `WidgetWindow.ShowDiagnostics()`; `SettingsViewModel` gains a constructor parameter `Action openDiagnostics` and a `RelayCommand OpenDiagnosticsCommand`.

**Requirements — the window:**

- `Title` verbatim: `Quota Monitor diagnostics`. `Width="520"`, `SizeToContent="Height"`, `WindowStyle="ToolWindow"`, `ResizeMode="NoResize"`, `ShowInTaskbar="False"`, `WindowStartupLocation="CenterOwner"`.
- Follows the same theming as `SettingsWindow.xaml`: `WidgetWindowBackgroundBrush`, `TextPrimaryBrush`, `WidgetFontFamily`, `UseLayoutRounding`, `SnapsToDevicePixels`.
- Content is a `ScrollViewer` over an `ItemsControl` of `Sections`; each section renders its `Title` in `SettingsSectionTextStyle`, its `Subtitle` (collapsed when null) in `CaptionTextStyle`, then its fields as a two-column grid (label in `TextTertiaryBrush`, value in `TextPrimaryBrush`, value `TextWrapping="Wrap"` and selectable via `IsHitTestVisible`), then its `Lines` in `CaptionTextStyle`.
- **Height is capped in code-behind by the work area of the screen it opens on**, exactly as `SettingsWindow.xaml.cs` already does. Read that file and follow it rather than inventing a number.
- Footer buttons using `SettingsActionButtonStyle`: `Copy diagnostics`, `Open logs folder`. Above them, `CopyHintText` in `CaptionTextStyle`/`TextTertiaryBrush`; below them, `CopyConfirmationText`, collapsed while null.
- Every interactive element carries `AutomationProperties.Name` matching its visible text.

**Requirements — the ways in:**

- `WidgetWindow.xaml`'s `TrayMenu` gains `<MenuItem Header="Diagnostics" Click="TrayDiagnostics_Click" />`, placed **after `Settings` and before the `Separator`** — PRD §17 lists the tray menu as Open, Refresh, Settings, Diagnostics, Exit.
- `SettingsWindow.xaml`'s `ACTIONS` block gains `Open diagnostics` as the **first** button, before `Re-check providers` — PRD §19 lists it among the settings actions.
- `WidgetWindow.ShowDiagnostics()` opens the window, or activates the one already open, following the exact pattern `ShowSettings()` uses: a `_diagnosticsWindow` field, `Owner = this`, `Closed` clearing the field, and `Deactivated`/`Activated` wired to `_dismiss` so an outside click takes the pair down. **This is load-bearing:** without the `_dismiss` wiring the widget stays on screen when focus leaves via the diagnostics window.
- The view model is constructed fresh each time the window opens, with `clock: () => DateTimeOffset.Now`, `copyToClipboard: text => Clipboard.SetText(text)` wrapped in a try/catch for `System.Runtime.InteropServices.ExternalException` (the clipboard can be locked by another process — a failed copy must not take the widget down), and `openLogs: OpenLogsFolder` reusing the existing method.
- `themeDescription` is `$"{settings.Current.Theme} · resolved {theme.Current}"` when a `ThemeManager` is available, else the preference alone.
- `displayScalingDescription` is derived from `VisualTreeHelper.GetDpi(this).DpiScaleX`, rendered as a percentage, e.g. `150%`; fall back to `EmptyValue` if the window has no source yet.
- `App.xaml.cs` registers `EnvironmentReport.Capture()` and a `StartupReport(DateTimeOffset.Now, loaded.CorruptBackupPath)` as singletons, and passes them into `WidgetWindow`'s constructor as optional parameters (defaulting to a captured/empty value so the existing test constructions keep compiling).

**Acceptance criteria (tests to write):**

- `ViewLoadingTests`-style test: `DiagnosticsWindow` loads with a populated `DiagnosticsViewModel` without throwing, on an STA thread using the existing `WpfFixture`.
- A test asserting the tray `ContextMenu` resource contains a menu item whose `Header` is `Diagnostics`.
- `SettingsViewModelTests`: `OpenDiagnosticsCommand.Execute(null)` invokes the delegate exactly once.
- Existing `SettingsViewModel` constructions in tests are updated for the new parameter.

- [ ] **Step 1:** Build the window and its code-behind; mirror `SettingsWindow.xaml.cs`'s height cap.
- [ ] **Step 2:** Wire the tray menu, the settings action, and `App.xaml.cs`.
- [ ] **Step 3:** Write the tests; build and test as separate commands.
- [ ] **Step 4:** Commit — `feat: add the diagnostics window`.

---

## Verification

Run as **two separate commands**, never chained:

```powershell
dotnet build -p:BaseOutputPath=$env:TEMP/aium-build/
dotnet test -p:BaseOutputPath=$env:TEMP/aium-build/
```

Expected: `0 Warning(s)`, `0 Error(s)`, and every existing test still passing (497 before this plan) plus the new ones.

If the build fails with `MSB3021` on `AiUsageMonitor.App`, the user's widget is running and holding the output directory. That is expected. Use the `BaseOutputPath` form above. **Do not stop that process.**

## Out of scope

- Stamping a real application version, tagging a release, or writing a README — that is C17, a separate increment.
- Persisting or exporting a diagnostic bundle to a file — PRD §28's "exportable, redacted diagnostic bundles" is future work; the clipboard is what §20 asks for.
- Any new provider mechanism, and any change to what either probe reads.
