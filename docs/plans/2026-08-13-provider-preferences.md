# Provider Preferences Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user order the provider cards, hide a provider outright, and give a provider its own refresh interval instead of the one shared setting.

**Architecture:** Providers gain a stable settings key, separate from their display name. Three new application settings are keyed on it. Ordering is a pure function over the registry list. Cadence moves from the window's poll timer into `ProviderRefreshService`, which already owns per-provider deferral — the timer becomes a fixed short tick and the service decides, per provider, whether anything is due.

**Tech Stack:** .NET 10, WPF, `System.Text.Json` (no new packages), xUnit.

This implements **C14** and **C15** from `docs/specs/2026-08-13-feature-inventory-and-ideas.md` §2.2.

## Global Constraints

Every task's requirements implicitly include this section.

- **Warnings are errors.** `dotnet build` must finish with `0 Warning(s)`.
- **Run build and test as separate commands, never chained.**
- **The build lock is expected, not a defect.** The user's `AiUsageMonitor.App.exe` may be running and holding `src/AiUsageMonitor.App/bin/…`, making a plain `dotnet build` fail with `MSB3021`. **Never kill that process.** Build and test with:
  - `dotnet build -p:BaseOutputPath=$env:TEMP/aium-build/`
  - `dotnet test -p:BaseOutputPath=$env:TEMP/aium-build/`
- **The domain model stays provider-neutral.** No property named after a plan period; nothing branches on which provider it is. A provider key is data, never a `switch`.
- **Missing data is `null` and surfaces as `Waiting`/`Unavailable` — never as `0`.**
- **Credentials are in-memory only**; never logged, persisted, displayed or copied.
- **The settings file is hand-editable by design.** Every new setting must survive a nonsense value in that file without preventing startup: clamp or ignore, never throw, never silently rewrite a value the user typed.
- **Provider polling is what feeds the tray glyph and the notifications** — hidden-to-tray is the primary operating mode (PRD §16.2). Do not slow or skip polling for any reason other than the user's explicit choice implemented here.
- **No administrator privileges**, no new `PackageReference`.
- **One commit per task**, created serially.

## The trap that will bite

`UsageAlertWatcher` keys its per-window milestone state on the **`ProviderCardViewModel` instance** (`Dictionary<(ProviderCardViewModel, string), int>`). Reordering the `Providers` collection must therefore **reuse the existing card instances**. If a reorder recreates cards, every milestone rung resets to zero, and the next tick fires a burst of "past 90%" balloons for readings the user already saw. Reorder by moving items, never by rebuilding them.

## File Structure

**Create:**
- `src/AiUsageMonitor.Infrastructure/Providers/ProviderOrdering.cs` — pure ordering rule.
- `src/AiUsageMonitor.App/ViewModels/ProviderPreferenceViewModel.cs` — one row of the settings window's provider list.
- `tests/AiUsageMonitor.Infrastructure.Tests/ProviderOrderingTests.cs`
- `tests/AiUsageMonitor.App.Tests/ProviderPreferenceViewModelTests.cs`

**Modify:**
- `src/AiUsageMonitor.Infrastructure/Providers/ProviderDescriptor.cs`, `ProviderRegistry.cs`
- `src/AiUsageMonitor.Infrastructure/Settings/AppSettings.cs`
- `src/AiUsageMonitor.Infrastructure/Refresh/ProviderRefreshService.cs`
- `src/AiUsageMonitor.App/Notifications/TickCadence.cs`
- `src/AiUsageMonitor.App/ViewModels/MainViewModel.cs`, `ProviderCardViewModel.cs`, `SettingsViewModel.cs`
- `src/AiUsageMonitor.App/Notifications/UsageAlertWatcher.cs`
- `src/AiUsageMonitor.App/Views/SettingsWindow.xaml`, `WidgetWindow.xaml.cs`
- Existing tests that construct `ProviderDescriptor` or `SettingsViewModel`.

---

### Task 1: A stable provider key, the ordering rule, and the settings

**Files:**
- Modify: `src/AiUsageMonitor.Infrastructure/Providers/ProviderDescriptor.cs`, `ProviderRegistry.cs`
- Create: `src/AiUsageMonitor.Infrastructure/Providers/ProviderOrdering.cs`
- Modify: `src/AiUsageMonitor.Infrastructure/Settings/AppSettings.cs`
- Test: `tests/AiUsageMonitor.Infrastructure.Tests/ProviderOrderingTests.cs`, `AppSettingsStoreTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record ProviderDescriptor(string Key, string DisplayName, string Monogram, IProviderProbe Probe);

  public static class ProviderOrdering
  {
      /// <summary>
      /// The registry list rearranged to follow <paramref name="order"/>. Keys named in the order
      /// that no longer exist are ignored; providers the order does not mention keep their registry
      /// position, appended after the ones it does. Neither list is trusted: the order comes from a
      /// hand-editable settings file, and the registry changes when a build adds a provider.
      /// </summary>
      public static IReadOnlyList<ProviderDescriptor> Apply(
          IReadOnlyList<ProviderDescriptor> providers,
          IReadOnlyList<string> order);
  }

  // on AppSettings
  public IReadOnlyList<string> ProviderOrder { get; init; } = [];
  public IReadOnlyList<string> HiddenProviders { get; init; } = [];
  public IReadOnlyDictionary<string, int> ProviderRefreshSeconds { get; init; } =
      new Dictionary<string, int>();

  [JsonIgnore] public bool IsProviderHidden(string providerKey);
  [JsonIgnore] public TimeSpan RefreshIntervalFor(string providerKey);
  [JsonIgnore] public int? RefreshSecondsOverrideFor(string providerKey);
  ```

**Requirements:**

- Registry keys are exactly `"claude-code"` and `"codex"` — lower-case, hyphenated, stable, and never shown to the user. `ProviderRegistry.CreateDefault()` becomes:
  ```csharp
  new("claude-code", "Claude Code", "CC", new ClaudeOAuthUsageProbe()),
  new("codex", "Codex", "CX", new CodexProbe())
  ```
- Key comparison everywhere is `StringComparer.OrdinalIgnoreCase`. A hand-edited file with `Codex` must match `codex`.
- `IsProviderHidden` is true when `HiddenProviders` contains the key, case-insensitively.
- `RefreshSecondsOverrideFor` returns the dictionary value when present **and** non-zero; `0` and absence both mean "use the shared interval" and both return `null`.
- `RefreshIntervalFor` returns the override clamped to `[15, 3600]` seconds, or `RefreshInterval` (the existing shared property) when there is no override. Clamped for the same reason the shared one is: a hand-edited file must never poll in a tight loop.
- An empty `ProviderOrder` means "registry order" — that is the default and must not be written eagerly.
- All three new properties serialize as plain JSON so the settings file stays readable; verify a round-trip through `AppSettingsStore`.

**Acceptance criteria (tests to write):**

- `ProviderOrdering.Apply` with an order naming both keys reversed returns them reversed.
- An order naming one key returns that one first and the rest in registry order.
- An order naming a key that no longer exists ignores it and returns every real provider exactly once.
- An empty order returns the input sequence, same instances, same order.
- An order containing a duplicate key yields that provider exactly once.
- `RefreshIntervalFor` returns the shared interval for an unknown key, for a key mapped to `0`, and for an absent dictionary.
- `RefreshIntervalFor` clamps `5` up to 15 s and `99999` down to 3600 s.
- `IsProviderHidden` matches case-insensitively.
- An `AppSettings` with all three populated survives a save/load round-trip through `AppSettingsStore`.

- [ ] **Step 1:** Add the key and update the registry and every construction site (tests included).
- [ ] **Step 2:** Add `ProviderOrdering` and the settings members.
- [ ] **Step 3:** Write the tests; build and test as separate commands.
- [ ] **Step 4:** Commit — `feat: give providers a stable settings key, order and interval`.

---

### Task 2: The refresh service owns cadence

**Files:**
- Modify: `src/AiUsageMonitor.Infrastructure/Refresh/ProviderRefreshService.cs`
- Test: `tests/AiUsageMonitor.Infrastructure.Tests/ProviderRefreshServiceTests.cs`

**Interfaces:**
- Consumes: `ProviderDescriptor.Key`, `AppSettings.RefreshIntervalFor` (Task 1).
- Produces:
  ```csharp
  // replaces the single BaseInterval as the source of per-provider cadence.
  // BaseInterval stays as the fallback for a provider with no override.
  public TimeSpan BaseInterval { get; set; }

  /// <summary>
  /// Per-provider cadence overrides, keyed by <see cref="ProviderDescriptor.Key"/>. A provider not
  /// named here polls at <see cref="BaseInterval"/>. Replaced wholesale when settings change.
  /// </summary>
  public IReadOnlyDictionary<string, TimeSpan> IntervalOverrides { get; set; }

  /// <summary>
  /// Providers the user has hidden. They are not polled at all: a hidden card shows nothing, feeds
  /// no glyph bar and raises no alert, so polling it would be work with no consumer — and in the
  /// Claude Code case, an avoidable call to an undocumented endpoint.
  /// </summary>
  public IReadOnlyCollection<string> HiddenProviderKeys { get; set; }

  public TimeSpan IntervalFor(ProviderDescriptor provider);
  ```

**Requirements:**

- **`RefreshAllAsync` skips a hidden provider entirely**, whether or not `force` is true. "Refresh all" must not reach something the user hid.
- **`RefreshAsync(provider, …)` — the single-provider path — never checks hidden.** It is an explicit act, and a later increment may offer it from diagnostics.
- The cadence change, and it is the load-bearing one: in `Record`, a provider that did **not** fail now schedules `NextAttempt = now + IntervalFor(provider)` instead of `now + BackoffFor(0, …)` (which is `now`, i.e. immediately eligible). A provider that failed schedules `now + BackoffFor(failures, IntervalFor(provider))`.
- `BackoffFor` keeps its current signature, meaning and tests. Do not fold the interval into it.
- Consequence to preserve deliberately: `NextAttemptFor` now returns a future instant for healthy providers too. `ProviderCardViewModel` already gates its "Next check in …" copy on `State is Error or Unavailable`, so a healthy card must still grow no countdown. **Assert that** — it is the decision recorded for C2 and it is easy to break here.
- `IntervalFor` returns the override for the provider's key when present, else `BaseInterval`. Case-insensitive lookup.
- `IntervalOverrides` and `HiddenProviderKeys` default to empty and are never null. Assigning null must be impossible or must coerce to empty.
- All new state reads happen under the existing `_gate` where they touch `_backoff` or `_attempts`.

**Acceptance criteria (tests to write):**

- After a successful refresh with a 60 s interval, an unforced `RefreshAllAsync` 30 s later probes nothing; the same call 61 s later probes.
- With a 15 s override for one provider and a 300 s shared interval, an unforced call 20 s after success probes only the overridden provider.
- A hidden provider is not probed by `RefreshAllAsync` even with `force: true`.
- A hidden provider **is** probed by `RefreshAsync(provider, …)`.
- Backoff still doubles on repeated failure and still caps at 8× — the existing tests must pass unchanged.
- A provider whose snapshot is `NotInstalled` is re-probed at the normal interval, not on every call. (This is the regression this task most easily introduces: `Record` treats non-failures uniformly, so `NotInstalled` must pick up the interval like any other success path.)

- [ ] **Step 1:** Add the three members and change `Record`'s non-failure branch.
- [ ] **Step 2:** Write the tests; build and test.
- [ ] **Step 3:** Commit — `feat: move polling cadence into the refresh service, per provider`.

---

### Task 3: Order, hide, and everything downstream of both

**Files:**
- Modify: `src/AiUsageMonitor.App/ViewModels/MainViewModel.cs`, `ProviderCardViewModel.cs`
- Modify: `src/AiUsageMonitor.App/Notifications/UsageAlertWatcher.cs`
- Test: `tests/AiUsageMonitor.App.Tests/MainViewModelTests.cs`, `ProviderCardViewModelTests.cs`, `TrayGlyphStateTests.cs`, `UsageAlertWatcherTests.cs`

**Interfaces:**
- Consumes: `ProviderOrdering.Apply`, `AppSettings.IsProviderHidden` (Task 1).
- Produces:
  ```csharp
  // on ProviderCardViewModel
  /// <summary>
  /// The user hid this provider outright (PRD §28). Distinct from ShowWhenUnavailable, which hides
  /// only providers that are absent from the machine; this hides one that is present and working.
  /// </summary>
  public bool IsHiddenByUser { get; set; }
  ```

**Requirements:**

- `ProviderCardViewModel.IsHiddenByFilter` becomes:
  ```csharp
  IsHiddenByUser || (!ShowWhenUnavailable && State is ConnectionState.NotInstalled or ConnectionState.Unsupported)
  ```
  Setting `IsHiddenByUser` raises `IsHiddenByFilter`. Keep the property name `IsHiddenByFilter` — `TrayGlyphState`, `MainViewModel.FooterText` and the card view all read it, and they all want the same answer.
- `MainViewModel`'s constructor builds cards in `ProviderOrdering.Apply(providers, settings.ProviderOrder)` order.
- `MainViewModel.ApplySettings` re-orders `Providers` **in place**, using `ObservableCollection<T>.Move`, reusing the existing card instances. Read the trap section above before writing this. A test must prove the instances are the same objects after a reorder.
- `ApplySettings` also sets `card.IsHiddenByUser` from `settings.IsProviderHidden(key)` and raises `FooterText`.
- `MainViewModel` keeps a `Dictionary<ProviderDescriptor, ProviderCardViewModel>` as it does today; the reorder touches only the `Providers` collection.
- `UsageAlertWatcher.Observe` skips a card whose `IsHiddenByFilter` is true — **before** `ObserveProviderHealth`, so hiding a failing provider also stops its "stopped reporting" balloon.
- `TrayGlyphState.From` needs no change: it already skips `IsHiddenByFilter`. Add a test proving a user-hidden provider contributes no bars and no digits.
- `MainViewModel.FooterText` needs no change for the same reason; add a test.
- **`DiagnosticsViewModel` must learn about hiding.** The diagnostics increment landed before this one and already lists every provider including absent ones; its `Next attempt` field must now render the verbatim string `Not scheduled — hidden by the user` for a provider whose card `IsHiddenByUser` is true, because the honest explanation for a card that has not moved is now "you hid it", not "nothing is due". Add a test for it in `DiagnosticsViewModelTests`. If `DiagnosticsViewModel` does not exist in the tree, skip this bullet and say so in the report.

**Acceptance criteria (tests to write):**

- Cards are built in the settings order, and in registry order when the setting is empty.
- After `ApplySettings` with a reversed order, `Providers` is reversed **and every element is reference-equal to the corresponding card from before the reorder**.
- A user-hidden provider: `IsHiddenByFilter` is true even when it is `Connected`; the footer counts one fewer; `TrayGlyphState.From` produces no bar for it; `UsageAlertWatcher.Observe` returns nothing for it even when its usage crosses a rung.
- Un-hiding restores the card to the collection's visible count without recreating it.
- `ShowWhenUnavailable` behaviour is unchanged for a provider that is not user-hidden — the existing tests must pass untouched.

- [ ] **Step 1:** Add `IsHiddenByUser` and widen `IsHiddenByFilter`.
- [ ] **Step 2:** Order and re-order in `MainViewModel`; skip hidden in the watcher.
- [ ] **Step 3:** Write the tests; build and test.
- [ ] **Step 4:** Commit — `feat: order and hide providers`.

---

### Task 4: The settings window's provider list

**Files:**
- Create: `src/AiUsageMonitor.App/ViewModels/ProviderPreferenceViewModel.cs`
- Modify: `src/AiUsageMonitor.App/ViewModels/SettingsViewModel.cs`
- Modify: `src/AiUsageMonitor.App/Views/SettingsWindow.xaml`
- Test: `tests/AiUsageMonitor.App.Tests/ProviderPreferenceViewModelTests.cs`, `SettingsViewModelTests.cs`, `ViewLoadingTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–3.
- Produces:
  ```csharp
  public sealed class ProviderPreferenceViewModel : ObservableObject
  {
      public string Key { get; }
      public string DisplayName { get; }
      public bool IsVisible { get; set; }                       // writes through SettingsService
      public ObservableCollection<ChoiceViewModel> Intervals { get; }
      public RelayCommand MoveUpCommand { get; }
      public RelayCommand MoveDownCommand { get; }
      public bool CanMoveUp { get; }
      public bool CanMoveDown { get; }
      public void Refresh();
  }

  // on SettingsViewModel
  public ObservableCollection<ProviderPreferenceViewModel> ProviderPreferences { get; }
  public string ProviderPreferencesHintText { get; }
  ```

**Requirements:**

- `SettingsViewModel` gains a constructor parameter `IReadOnlyList<ProviderDescriptor> providers` and builds one `ProviderPreferenceViewModel` per provider, in the current effective order. Existing test constructions must be updated; give the parameter no default — a settings window with no providers is not a real state.
- `IsVisible` is the inverse of hidden. Setting it writes `HiddenProviders` through `SettingsService.Update`, adding or removing this key and leaving every other key alone.
- Move up/down rewrite `ProviderOrder` to the **full, explicit** key list in its new order — never a partial list. Writing the whole list is what makes the setting readable in the file and what makes `ProviderOrdering.Apply` deterministic.
- `CanMoveUp`/`CanMoveDown` are false at the ends of the list; the buttons bind `IsEnabled` to them.
- `Intervals` reuses `ChoiceViewModel` with these values and verbatim labels, in this order:

  | Value | Label |
  |---|---|
  | `0` | `Shared` |
  | `15` | `15s` |
  | `30` | `30s` |
  | `60` | `1m` |
  | `120` | `2m` |
  | `300` | `5m` |
  | `600` | `10m` |

  `0` is the sentinel for "use the shared interval". A hand-edited override that is not in this list is appended and sorted, exactly as `SettingsViewModel.Durations` already does for the shared settings — copy that behaviour rather than reimplementing it. `GroupName` must be unique per provider, e.g. `$"interval-{Key}"`; WPF scopes radio buttons by name, so a shared group name would make the two providers' interval choices exclusive of each other.
- The settings window gains a `PROVIDERS` section, placed **after `WINDOW` and before `NOTIFICATIONS`**, containing:
  - the caption, verbatim: `Hidden providers are not polled, and do not appear in the notification-area icon.`
  - an `ItemsControl` over `ProviderPreferences`; each row shows the display name, a `Show` checkbox, `Move up` / `Move down` buttons, and the interval radio group under a caption reading verbatim `Check this provider every`.
  - Follow the existing styles: `SettingsCheckBoxStyle`, `SettingsRadioButtonStyle`, `SettingsSectionTextStyle`, `CaptionTextStyle`. Set `Margin` through a `Style` setter, not an attribute, wherever a density trigger could apply — the existing XAML explains why.
  - Every control carries `AutomationProperties.Name` including the provider's name, e.g. `Show Claude Code`, `Move Claude Code up` — the names must be distinguishable between rows.
- `SettingsViewModel.OnSettingsChanged` must refresh the provider rows too: rebuild their order and call `Refresh()` on each choice, the same way it already handles `Themes`, `Densities`, `RefreshIntervals` and `StaleThresholds`.

**Acceptance criteria (tests to write):**

- Setting `IsVisible = false` adds the key to `HiddenProviders`; setting it back removes it and leaves the other provider's entry untouched.
- `MoveUpCommand` on the second provider writes a `ProviderOrder` naming every provider with the two swapped.
- `MoveUpCommand` is disabled on the first row, `MoveDownCommand` on the last.
- Selecting the `0` interval choice removes the override; selecting `120` writes `120` for that key only.
- A hand-edited override of `45` appears as an extra choice in that provider's list, sorted into place.
- Interval `GroupName` differs between providers.
- `ViewLoadingTests`: `SettingsWindow` still loads with the new section present.

- [ ] **Step 1:** Build `ProviderPreferenceViewModel` and wire it into `SettingsViewModel`.
- [ ] **Step 2:** Add the `PROVIDERS` XAML section.
- [ ] **Step 3:** Write the tests; build and test.
- [ ] **Step 4:** Commit — `feat: add provider ordering, visibility and interval to settings`.

---

### Task 5: The poll timer stops deciding

**Files:**
- Modify: `src/AiUsageMonitor.App/Notifications/TickCadence.cs`
- Modify: `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs`
- Test: `tests/AiUsageMonitor.App.Tests/WidgetWindowTests.cs`

**Interfaces:**
- Consumes: `IntervalOverrides`, `HiddenProviderKeys` (Task 2); `ProviderPreferences` (Task 4).
- Produces:
  ```csharp
  // on TickCadence
  /// <summary>
  /// How often the window asks the refresh service whether anything is due. Not the refresh
  /// interval: the service owns that, per provider, so this only has to be short enough that a
  /// 15-second interval is not measurably late. A tick with nothing due costs a dictionary lookup
  /// per provider and starts no work.
  /// </summary>
  public static readonly TimeSpan Poll = TimeSpan.FromSeconds(5);
  ```

**Requirements:**

- `_poll.Interval` is set once, to `TickCadence.Poll`, and is never reassigned from settings again. Delete the `_poll.Interval = settings.RefreshInterval` lines in both the constructor and `OnSettingsChanged`.
- `OnSettingsChanged` instead pushes the whole cadence picture into the service:
  ```csharp
  _refresh.BaseInterval = settings.RefreshInterval;
  _refresh.IntervalOverrides = /* every provider key with a non-null override, as TimeSpan */;
  _refresh.HiddenProviderKeys = settings.HiddenProviders;
  ```
  and the same three assignments must happen once at startup, before the first refresh, so a hidden provider is never polled even once.
- `WidgetWindow` needs the provider list to build `IntervalOverrides`. Take `IReadOnlyList<ProviderDescriptor>` as an optional constructor parameter defaulting to an empty list so the existing test constructions keep compiling, and pass the real one from `App.xaml.cs`.
- `ShowSettings()` passes the provider list into `SettingsViewModel`'s new parameter.
- Nothing about the **presentation** tick changes. `TickCadence.Visible` (1 s) and `TickCadence.Hidden` (5 s) keep their current values and meaning.

**Acceptance criteria (tests to write):**

- `TickCadence.Poll` is 5 seconds.
- A test constructing `WidgetWindow` with a settings record carrying a hidden provider and an override, then asserting the refresh service received both — via the service's public `HiddenProviderKeys` and `IntervalFor`.
- Changing the shared refresh interval through `SettingsService` updates `BaseInterval` and leaves `_poll.Interval` at `TickCadence.Poll`.

- [ ] **Step 1:** Add `TickCadence.Poll` and rework the window's wiring.
- [ ] **Step 2:** Write the tests; build and test as separate commands.
- [ ] **Step 3:** Commit — `feat: let the refresh service decide when a provider is due`.

---

## Verification

Run as **two separate commands**, never chained:

```powershell
dotnet build -p:BaseOutputPath=$env:TEMP/aium-build/
dotnet test -p:BaseOutputPath=$env:TEMP/aium-build/
```

Expected: `0 Warning(s)`, `0 Error(s)`, every existing test still passing plus the new ones.

`MSB3021` on `AiUsageMonitor.App` means the user's widget is running and holding the output directory. Expected, not a defect — use the `BaseOutputPath` form. **Do not stop that process.**

## Out of scope

- The zero-provider home state (X12, §3.2) — hiding every provider leaves the existing empty body and `0 providers` footer. Not a regression this plan introduces, and not this plan's job to fix.
- Drag-to-reorder. C14 says "drag"; this delivers move up/down buttons instead, because they are keyboard-reachable and screen-reader-nameable, which drag is not, and the list is two rows long. Record the substitution in the decision table when this lands.
- Any change to what either probe reads, or to the shared refresh/stale settings themselves.
