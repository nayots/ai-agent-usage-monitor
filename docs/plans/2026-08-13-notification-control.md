# Notification Control Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the fixed twelve-rung milestone ladder with one the user chooses, and add quiet hours that silence everything except a limit actually being reached.

**Architecture:** The ladder becomes a parameter rather than a constant — `QuotaMilestones` keeps its default and gains a sanitizing entry point, so a hand-edited settings file can never disable alerts by accident. Quiet hours are a pure domain value with a `Contains` test, applied by a pure filter between observation and delivery, so the watcher's edge-triggered state keeps advancing while the user is asleep.

**Tech Stack:** .NET 10, WPF, `System.Text.Json` (no new packages), xUnit.

This implements **C12** and **C13** from `docs/specs/2026-08-13-feature-inventory-and-ideas.md` §2.2.

## Global Constraints

Every task's requirements implicitly include this section.

- **Warnings are errors.** `dotnet build` must finish with `0 Warning(s)`.
- **Run build and test as separate commands, never chained.**
- **The build lock is expected, not a defect.** The user's `AiUsageMonitor.App.exe` may be running and holding `src/AiUsageMonitor.App/bin/…`, making a plain `dotnet build` fail with `MSB3021`. **Never kill that process.** Build and test with:
  - `dotnet build -p:BaseOutputPath=$env:TEMP/aium-build/`
  - `dotnet test -p:BaseOutputPath=$env:TEMP/aium-build/`
- **The domain model stays provider-neutral.** The ladder is applied to whatever windows a provider reports and knows nothing about how long any of them lasts. No property named after a plan period.
- **Missing data is `null`.** A window that reports no percentage produces no rung and no alert; that is already true and must stay true.
- **A notification carries no failure reason** (PRD §16.2), and only the 100% `LimitReached` alert makes a sound. Neither changes here.
- **The settings file is hand-editable by design.** A nonsense value must clamp or fall back, never throw and never prevent startup.
- **No administrator privileges**, no new `PackageReference`.
- **One commit per task**, created serially.

## The invariant that must not break

`UsageAlertWatcher` is **edge-triggered**: it reports a rung only when the reading crosses it. Its state therefore has to keep advancing even when nothing will be delivered — `WidgetWindow.DeliverAlerts` already observes unconditionally and delivers conditionally, and the comment there explains why. Quiet hours must be applied on the **delivery** side, exactly like the existing `NotifyOnQuotaEvents` switch. Suppressing the observation instead would bank every crossing the user slept through and release them all at 07:00.

## File Structure

**Create:**
- `src/AiUsageMonitor.Domain/QuietHours.cs` — the schedule value and its `Contains` test.
- `src/AiUsageMonitor.App/Notifications/QuietHoursFilter.cs` — pure alert filter.
- `src/AiUsageMonitor.App/ViewModels/AlertThresholdPresets.cs` — the preset lists and their labels.
- `tests/AiUsageMonitor.Domain.Tests/QuietHoursTests.cs`
- `tests/AiUsageMonitor.App.Tests/QuietHoursFilterTests.cs`
- `tests/AiUsageMonitor.App.Tests/AlertThresholdPresetsTests.cs`

**Modify:**
- `src/AiUsageMonitor.Domain/QuotaMilestones.cs`
- `src/AiUsageMonitor.Infrastructure/Settings/AppSettings.cs`
- `src/AiUsageMonitor.App/Notifications/UsageAlertWatcher.cs`
- `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs`
- `src/AiUsageMonitor.App/ViewModels/SettingsViewModel.cs`, `src/AiUsageMonitor.App/Views/SettingsWindow.xaml`
- `tests/AiUsageMonitor.Domain.Tests/…`, `tests/AiUsageMonitor.App.Tests/UsageAlertWatcherTests.cs`, `SettingsViewModelTests.cs`, `ViewLoadingTests.cs`

---

### Task 1: A ladder the user owns

**Files:**
- Modify: `src/AiUsageMonitor.Domain/QuotaMilestones.cs`
- Modify: `src/AiUsageMonitor.Infrastructure/Settings/AppSettings.cs`
- Create: `src/AiUsageMonitor.App/ViewModels/AlertThresholdPresets.cs`
- Test: `tests/AiUsageMonitor.Domain.Tests/QuotaMilestonesTests.cs` (create if absent), `tests/AiUsageMonitor.App.Tests/AlertThresholdPresetsTests.cs`, `tests/AiUsageMonitor.Infrastructure.Tests/AppSettingsStoreTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  // on QuotaMilestones — the existing Ladder property and single-argument Crossed stay exactly as
  // they are; Ladder remains the default and the fallback.
  public static int Crossed(double? usedPercent, IReadOnlyList<int> ladder);

  /// <summary>
  /// A user-supplied ladder made safe to use. Values outside 1–100 are dropped, duplicates
  /// collapse, 100 is always present, and the result is ascending. A list with no usable value at
  /// all falls back to <see cref="Ladder"/>: a hand-edited settings file must not be able to
  /// silence alerts by accident, and the notifications switch already exists for silencing them
  /// on purpose.
  /// </summary>
  public static IReadOnlyList<int> Sanitize(IReadOnlyList<int>? thresholds);

  // on AppSettings
  public IReadOnlyList<int> AlertThresholds { get; init; } = QuotaMilestones.Ladder;
  [JsonIgnore] public IReadOnlyList<int> EffectiveAlertThresholds => QuotaMilestones.Sanitize(AlertThresholds);

  // AlertThresholdPresets
  public sealed record AlertThresholdPreset(int Id, string Label, IReadOnlyList<int> Thresholds);
  public static class AlertThresholdPresets
  {
      public static IReadOnlyList<AlertThresholdPreset> All { get; }
      public static int IdFor(IReadOnlyList<int> thresholds);   // -1 when nothing matches
      public static string CustomLabel(IReadOnlyList<int> thresholds);
  }
  ```

**Requirements:**

- `Crossed(usedPercent, ladder)` behaves exactly as the existing method but against the supplied ladder: the highest rung at or below the reading, `0` when nothing is reached or the reading is null, and the top rung for readings above it. The single-argument overload delegates to it with `Ladder`.
- `Sanitize` is total: null input, empty input, all-out-of-range input each return `Ladder`. `[100]` returns `[100]`. `[95, 20, 20, 200, -3]` returns `[20, 95, 100]`.
- `AlertThresholds` serializes as a plain JSON array of numbers. **Never write `EffectiveAlertThresholds` to the file** — it is `[JsonIgnore]` and derived.
- `AlertThresholdPresets.All`, in this order, with verbatim labels:

  | Id | Label | Thresholds |
  |---|---|---|
  | `0` | `Every milestone` | `10, 20, 30, 40, 50, 60, 70, 80, 85, 90, 95, 100` |
  | `1` | `80, 90 and 100%` | `80, 90, 100` |
  | `2` | `90 and 100%` | `90, 100` |
  | `3` | `100% only` | `100` |

  Preset `0`'s list must be `QuotaMilestones.Ladder` itself, not a second copy of the same numbers.
- `IdFor` compares sequences after `Sanitize`, and returns `-1` when none match.
- `CustomLabel([75, 90, 100])` returns exactly `Custom (75, 90, 100%)` — the joined values with `, `, one `%` at the end, wrapped in `Custom (…)`.

**Acceptance criteria (tests to write):**

- `Crossed(84, [80, 90, 100])` is `80`; `Crossed(9, [10, 100])` is `0`; `Crossed(140, [90, 100])` is `100`; `Crossed(null, anything)` is `0`.
- Every `Sanitize` case listed above, asserted exactly.
- `Sanitize` output is always ascending and always contains `100`.
- `IdFor(QuotaMilestones.Ladder)` is `0`; `IdFor([90, 100])` is `2`; `IdFor([75, 100])` is `-1`.
- `CustomLabel` renders exactly as specified.
- An `AppSettings` with `AlertThresholds = [90, 100]` round-trips through `AppSettingsStore`, and the saved JSON does not contain the string `EffectiveAlertThresholds`.

- [ ] **Step 1:** Extend `QuotaMilestones`; add the settings property and the presets.
- [ ] **Step 2:** Write the tests; build and test as separate commands.
- [ ] **Step 3:** Commit — `feat: make the milestone ladder a user setting`.

---

### Task 2: Quiet hours as a value

**Files:**
- Create: `src/AiUsageMonitor.Domain/QuietHours.cs`
- Modify: `src/AiUsageMonitor.Infrastructure/Settings/AppSettings.cs`
- Test: `tests/AiUsageMonitor.Domain.Tests/QuietHoursTests.cs`, `tests/AiUsageMonitor.Infrastructure.Tests/AppSettingsStoreTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  namespace AiUsageMonitor.Domain;

  /// <summary>
  /// A daily window during which non-critical notifications are held back. Minutes from local
  /// midnight rather than a TimeOnly so the settings file stays a plain readable number, and so a
  /// value from a hand-edited file can be normalised rather than refused.
  /// </summary>
  public sealed record QuietHours(bool Enabled, int StartMinutes, int EndMinutes)
  {
      public static QuietHours Off { get; }
      public bool Contains(TimeOnly localTime);
  }

  // on AppSettings
  public bool QuietHoursEnabled { get; init; }
  public int QuietHoursStartMinutes { get; init; } = 1320;   // 22:00
  public int QuietHoursEndMinutes { get; init; } = 420;      // 07:00
  [JsonIgnore] public QuietHours QuietHours { get; }
  ```

**Requirements — `Contains`:**

- Returns `false` whenever `Enabled` is false, without looking at the times at all.
- Normalises both endpoints into `0…1439` with a positive modulo before comparing, so a hand-edited `-60` or `2000` cannot throw or produce a window that never ends.
- `start < end` → the half-open interval `[start, end)`.
- `start > end` → the wrapping window `[start, 1440) ∪ [0, end)`. 22:00–07:00 must include 23:30, 00:00 and 06:59, and exclude 07:00 and 12:00.
- `start == end` → `false`. A zero-length window reads as "not configured yet" far more often than "silence me forever", and the notifications switch already does the latter deliberately.
- `Off` is `new QuietHours(false, 1320, 420)`.

**Acceptance criteria (tests to write):**

- Disabled returns false for every time, including one inside the window.
- Non-wrapping window `[13:00, 14:00)`: 13:00 in, 13:59 in, 14:00 out, 12:59 out.
- Wrapping window `[22:00, 07:00)`: 22:00, 23:30, 00:00, 06:59 in; 07:00, 12:00, 21:59 out.
- `start == end` returns false for a time equal to both.
- `StartMinutes = -60` behaves as 23:00; `StartMinutes = 1500` behaves as 01:00.
- `AppSettings` round-trips the three new fields through `AppSettingsStore`, and the derived `QuietHours` is not in the JSON.

- [ ] **Step 1:** Add the record and the settings fields.
- [ ] **Step 2:** Write the tests; build and test.
- [ ] **Step 3:** Commit — `feat: add a quiet-hours schedule value`.

---

### Task 3: Applying both, without banking alerts

**Files:**
- Modify: `src/AiUsageMonitor.App/Notifications/UsageAlertWatcher.cs`
- Create: `src/AiUsageMonitor.App/Notifications/QuietHoursFilter.cs`
- Modify: `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs`
- Test: `tests/AiUsageMonitor.App.Tests/UsageAlertWatcherTests.cs`, `tests/AiUsageMonitor.App.Tests/QuietHoursFilterTests.cs`

**Interfaces:**
- Consumes: `QuotaMilestones.Crossed(…, ladder)` (Task 1), `QuietHours.Contains` (Task 2).
- Produces:
  ```csharp
  // on UsageAlertWatcher — replaces the current single-argument Observe.
  public IReadOnlyList<UsageAlert> Observe(IEnumerable<ProviderCardViewModel> providers, IReadOnlyList<int> ladder);

  /// <summary>
  /// Which of the observed alerts may be delivered right now. Applied after observation and before
  /// coalescing: the watcher's rungs must advance whether or not anyone is told, and a suppressed
  /// alert must not be merged into a delivered one.
  /// </summary>
  public static class QuietHoursFilter
  {
      public static IReadOnlyList<UsageAlert> Apply(
          IReadOnlyList<UsageAlert> alerts, QuietHours quietHours, TimeOnly localTime);
  }
  ```

**Requirements:**

- `Observe` uses the supplied ladder for `QuotaMilestones.Crossed`. Every caller passes `settings.EffectiveAlertThresholds`.
- The recovery branch currently hardcodes `80`. It becomes:
  ```csharp
  int recoveryRung = ladder.Contains(80) ? 80 : ladder.Min();
  ```
  and the copy uses that number: `$"{provider.DisplayName} · {window.Label} back under {recoveryRung}%"`. With the default ladder this is 80 and every existing test passes unchanged. The `previous == 100` case keeps its existing `limit reset` copy verbatim.
- `QuietHoursFilter.Apply` returns the input unchanged when `quietHours.Contains(localTime)` is false. Inside quiet hours it returns **only** the alerts whose `Kind` is `UsageAlertKind.LimitReached`.
- `WidgetWindow.DeliverAlerts` becomes: observe unconditionally with the current ladder → return early if `NotifyOnQuotaEvents` is off → filter through `QuietHoursFilter` with `TimeOnly.FromDateTime(DateTime.Now)` → `AlertBatch.Coalesce` → notify. **In that order.** Filtering after coalescing would let a suppressed milestone ride along inside a merged balloon.
- The existing comment on `DeliverAlerts` explaining "observed unconditionally, delivered conditionally" must be extended to cover quiet hours rather than replaced.
- Every existing `UsageAlertWatcher` test is updated to pass `QuotaMilestones.Ladder` and must keep asserting the same behaviour.

**Acceptance criteria (tests to write):**

- With ladder `[90, 100]`, a window moving 70% → 85% raises **nothing**; moving 85% → 92% raises one `Milestone` alert titled `… past 90%`.
- With ladder `[100]`, a window moving 50% → 99% raises nothing; 99% → 100% raises one `LimitReached`.
- With the default ladder, every existing milestone and recovery test still passes.
- With ladder `[90, 100]`, a window falling from 95% to 88% raises a recovery alert reading `back under 90%`.
- `QuietHoursFilter.Apply` inside the window keeps a `LimitReached` alert and drops `Milestone`, `Recovered`, `ProviderFailed` and `ProviderRecovered`.
- `QuietHoursFilter.Apply` outside the window, and with quiet hours disabled, returns the list unchanged (same count, same order).

- [ ] **Step 1:** Parameterise the watcher; add the filter.
- [ ] **Step 2:** Rewire `DeliverAlerts` in the stated order.
- [ ] **Step 3:** Write the tests; update the existing ones; build and test.
- [ ] **Step 4:** Commit — `feat: apply the chosen ladder and quiet hours to delivery`.

---

### Task 4: The settings window's notification section

**Files:**
- Modify: `src/AiUsageMonitor.App/ViewModels/SettingsViewModel.cs`
- Modify: `src/AiUsageMonitor.App/Views/SettingsWindow.xaml`
- Test: `tests/AiUsageMonitor.App.Tests/SettingsViewModelTests.cs`, `ViewLoadingTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–3.
- Produces:
  ```csharp
  // on SettingsViewModel
  public ObservableCollection<ChoiceViewModel> AlertThresholdChoices { get; }
  public bool QuietHoursEnabled { get; set; }
  public ObservableCollection<ChoiceViewModel> QuietHoursStarts { get; }
  public ObservableCollection<ChoiceViewModel> QuietHoursEnds { get; }
  public string QuietHoursSummaryText { get; }
  public string AlertThresholdHintText { get; }
  ```

**Requirements:**

- `AlertThresholdChoices` is built from `AlertThresholdPresets.All` using the existing `ChoiceViewModel` (group name `thresholds`). Writing a choice writes `AlertThresholds = preset.Thresholds`; reading returns `AlertThresholdPresets.IdFor(settings.Current.EffectiveAlertThresholds)`.
- When `IdFor` is `-1`, an extra choice is appended with value `-1` and label `AlertThresholdPresets.CustomLabel(...)`, so a hand-edited list is shown and stays selected rather than silently snapping to a preset. Selecting a real preset replaces it; the custom entry then disappears on the next rebuild. This mirrors what `SettingsViewModel.Durations` already does for out-of-list durations — read that method and follow it.
- `AlertThresholdHintText` is verbatim: `100% always notifies, and is the only alert that makes a sound.`
- `QuietHoursStarts` offers, in order, `18:00, 19:00, 20:00, 21:00, 22:00, 23:00` as minute values `1080, 1140, 1200, 1260, 1320, 1380`. `QuietHoursEnds` offers `05:00, 06:00, 07:00, 08:00, 09:00, 10:00` as `300, 360, 420, 480, 540, 600`. Labels come from `TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(value)).ToString("t", CultureInfo.CurrentCulture)` so a 12-hour locale reads correctly. Group names `quiet-start` and `quiet-end` — distinct, because WPF scopes radio buttons by name.
- A hand-edited value outside those six is appended and sorted, exactly as `Durations` does.
- `QuietHoursSummaryText` is verbatim `Milestone alerts are held back between <start> and <end>. Reaching 100% still notifies.` with the two labels substituted. It is shown only while quiet hours are enabled.
- The `NOTIFICATIONS` section of `SettingsWindow.xaml` gains, below the existing `Notify on quota milestones and resets` checkbox and in this order:
  - caption `Tell me when a window passes` in `BodySmallTextStyle`
  - the threshold radio group in a `WrapPanel`
  - `AlertThresholdHintText` in `CaptionTextStyle` / `TextTertiaryBrush`
  - a checkbox `Quiet hours` bound to `QuietHoursEnabled`
  - captions `From` and `To`, each above its own `WrapPanel` radio group
  - `QuietHoursSummaryText`, collapsed while quiet hours are off
- The quiet-hours controls are `IsEnabled`-bound to `QuietHoursEnabled`, and the whole block is `IsEnabled`-bound to `NotifyOnQuotaEvents` — a schedule for notifications that are switched off entirely is a control that does nothing.
- Every control carries `AutomationProperties.Name` matching its visible text.
- `OnSettingsChanged` refreshes the three new choice collections alongside the existing four.

**Acceptance criteria (tests to write):**

- Selecting the `100% only` preset writes `AlertThresholds = [100]`.
- With `AlertThresholds = [75, 90, 100]` the collection contains a choice labelled `Custom (75, 90, 100%)` and it is the selected one.
- Toggling `QuietHoursEnabled` writes through to settings and raises change notification.
- Selecting a start of `23:00` writes `QuietHoursStartMinutes = 1380`.
- A hand-edited `QuietHoursStartMinutes = 1290` appears as an extra sorted choice.
- `QuietHoursSummaryText` contains both labels and the sentence `Reaching 100% still notifies.`
- `ViewLoadingTests`: `SettingsWindow` still loads with the new controls present.

- [ ] **Step 1:** Extend `SettingsViewModel`.
- [ ] **Step 2:** Extend the `NOTIFICATIONS` XAML section.
- [ ] **Step 3:** Write the tests; build and test as separate commands.
- [ ] **Step 4:** Commit — `feat: add threshold and quiet-hours settings`.

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

- Any change to which alerts make a sound. `LimitReached` alone sounds, and that stays true both inside and outside quiet hours.
- Per-provider thresholds or per-provider quiet hours. One ladder and one schedule for the application.
- Suppressing the tray glyph or the widget's own colours during quiet hours — quiet hours are about balloons, nothing else.
