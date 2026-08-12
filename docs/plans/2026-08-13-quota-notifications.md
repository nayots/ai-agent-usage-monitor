# Quota Notifications Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tell the user, through the notification area, when a quota window crosses a usage milestone, when a limit is reached, when quota comes back, and when a provider stops reporting — on by default, switchable off in settings — and make a double-click on the tray icon hide the widget exactly as the title bar's close button does.

**Architecture:** A pure `QuotaMilestones` ladder lands in `Domain` (10-80 by tens, then 85/90/95/100). A `UsageAlertWatcher` in the App layer holds one small piece of state per window — the highest milestone crossed — and one per provider — whether it was working — and turns each observation of the live cards into zero or more `UsageAlert` values. Delivery is the existing `TrayIcon`, whose one-off `ShowHint` generalises into `Notify`. The watcher is driven from `WidgetWindow`'s existing one-second tick, beside the tray-glyph refresh, so no new pump is introduced.

**Tech Stack:** C# / .NET 10, WPF (`net10.0-windows`), `Shell_NotifyIcon` with `NIF_INFO`, xUnit 2.9.3. No new packages.

## Global Constraints

Every task's requirements implicitly include this section. Copied from `docs/PRD.md` and `CLAUDE.md`; these are requirements, not preferences.

- **`dotnet build` must be clean.** `TreatWarningsAsErrors=true`. A warning is a build failure.
- **`AiUsageMonitor.Domain` keeps zero `PackageReference`.**
- **No property may be named after a plan period.** No `FiveHourQuota`, no `WeeklyQuota`. Windows are discovered; their count, names and durations are never assumed. The ladder is applied to *every* window a provider reports, identified by the provider's own id and rendered with the provider's own label.
- **Missing data is `null` and surfaces as absence** — never as `0`. A window reporting no percentage produces no alert, not a 0% alert.
- **Never log, persist, cache, display or copy a provider credential.** A notification body is displayed text: it carries no error string, no response body, no header, no path from a provider response. When a provider fails, the notification says only that it failed.
- **Calm desktop behavior (PRD §4.6).** No flashing, no modal dialogs, no noisy notifications. Every alert is edge-triggered — one per crossing, never repeated — and silent, except the one that says a limit is reached.
- **No administrator privileges**, and no installer: the release artifact is a self-contained exe the user downloads and runs. This rules out the WinRT toast API, which needs a Start-menu shortcut carrying a registered AppUserModelID.
- **User- and machine-agnostic.** No hardcoded user paths.
- **The application's own copy is en-US.**

---

### Task 1: The milestone ladder

**Files:**
- Create: `src/AiUsageMonitor.Domain/QuotaMilestones.cs`
- Test: `tests/AiUsageMonitor.Domain.Tests/QuotaMilestonesTests.cs`

**Interfaces:**
- Produces: `QuotaMilestones.Ladder` (`IReadOnlyList<int>`), `QuotaMilestones.Crossed(double? usedPercent)` → `int`, the highest ladder value at or below the reading, `0` when the reading is null or below the first rung.

The ladder is `[10, 20, 30, 40, 50, 60, 70, 80, 85, 90, 95, 100]`: every ten points to eighty, every five above it, because the last fifth of a quota is where the spacing has to tighten.

- [ ] **Step 1: Write the failing test**

```csharp
[Theory]
[InlineData(null, 0)]
[InlineData(0, 0)]
[InlineData(9.9, 0)]
[InlineData(10, 10)]
[InlineData(79.9, 70)]
[InlineData(80, 80)]
[InlineData(84.9, 80)]
[InlineData(85, 85)]
[InlineData(99.9, 95)]
[InlineData(100, 100)]
[InlineData(140, 100)]
public void TheCrossedRungIsTheHighestAtOrBelowTheReading(double? used, int expected) =>
    Assert.Equal(expected, QuotaMilestones.Crossed(used));

[Fact]
public void TheLadderTightensAboveEighty()
{
    Assert.Equal([10, 20, 30, 40, 50, 60, 70, 80, 85, 90, 95, 100], QuotaMilestones.Ladder);
}
```

- [ ] **Step 2: Run it and watch it fail** — `dotnet test --filter FullyQualifiedName~QuotaMilestones`
- [ ] **Step 3: Implement** — a static class holding the array and a reverse scan.
- [ ] **Step 4: Run it and watch it pass**
- [ ] **Step 5: Commit** — `feat: add the quota milestone ladder`

---

### Task 2: The alert watcher

**Files:**
- Create: `src/AiUsageMonitor.App/Notifications/UsageAlert.cs`, `src/AiUsageMonitor.App/Notifications/UsageAlertWatcher.cs`
- Modify: `src/AiUsageMonitor.App/ViewModels/QuotaRowViewModel.cs` (expose the window's `Id` as a stable key)
- Test: `tests/AiUsageMonitor.App.Tests/UsageAlertWatcherTests.cs`

**Interfaces:**
- Consumes: `QuotaMilestones.Crossed` from Task 1.
- Produces: `record UsageAlert(UsageAlertKind Kind, string Title, string Text)`; `enum UsageAlertKind { Milestone, LimitReached, Recovered, ProviderFailed, ProviderRecovered }`; `UsageAlertWatcher.Observe(IEnumerable<ProviderCardViewModel>)` → `IReadOnlyList<UsageAlert>`.

Rules, all edge-triggered:

| Transition | Alert |
|---|---|
| First reading of a window | none — seeds silently |
| Crossed rung rises to 100 | `LimitReached` |
| Crossed rung rises below 100 | `Milestone` |
| Crossed rung falls from ≥ 80 to < 80 | `Recovered` |
| Crossed rung falls, either end on the same side of 80 | none |
| Provider working → failing | `ProviderFailed` |
| Provider failing → working | `ProviderRecovered` |
| First settled provider state | none — seeds silently |

Recovery is the **80 boundary crossed downward**, not merely a fall. 86% → 82% moves the rung from 85 to 80 and is still deep in the tight zone; announcing relief there would be wrong.

Provider health maps `Connected`/`Stale` → working, `Error`/`Unavailable` → failing, `NotInstalled`/`Unsupported` → absent (never announced, in either direction — a provider that is not on the machine is not news). A card that is not `Connected` has no window evaluated at all, so a stale reading never raises anything and never advances a rung. `Discovering` and `Waiting` do not even seed health: they mean "not known yet", and seeding on them would fire a failure alert at startup for a provider that was already broken when the widget opened.

**Copy.** The title carries the threshold; the body carries the reading. They must not be merged, because the rung is a boundary the application chose and the percentage is a number the provider reported — writing "80% used" when the provider said 81.4% would state a measurement the provider never made.

| Kind | Title | Body |
|---|---|---|
| `Milestone` | `Claude Code · 5-hour past 80%` | `81% used. Resets in 1h 12m.` |
| `LimitReached` | `Claude Code · 5-hour limit reached` | `100% used. Resets in 47m.` |
| `Recovered`, previous rung 100 | `Claude Code · 5-hour limit reset` | `12% used. Resets in 4h 58m.` |
| `Recovered`, previous rung 80–95 | `Claude Code · 5-hour back under 80%` | `62% used. Resets in 2h 3m.` |
| `ProviderFailed` | `Claude Code stopped reporting usage` | `Open the widget for the reason.` |
| `ProviderRecovered` | `Claude Code is reporting usage again` | `The numbers on the card are current.` |

Provider name and window label are `ProviderCardViewModel.DisplayName` and `QuotaRowViewModel.Label` verbatim — a provider that invents `three_hour_nimbus` gets a notification saying `three_hour_nimbus`. The percentage is `QuotaRowViewModel.UsedText`, so the toast and the row round identically. The second sentence of a body is dropped entirely when `CountdownText` is null; it is never replaced with a placeholder. `ProviderFailed` names no reason on purpose — see the credential constraint above.

- [ ] **Step 1: Write the failing tests** — one per row of the table above, plus: a jump from 12% to 92% raises exactly one alert naming 90; a null percentage raises nothing and leaves the state alone; two providers keep separate state; a window keeps its state across a provider failure.
- [ ] **Step 2: Run and watch them fail**
- [ ] **Step 3: Implement**
- [ ] **Step 4: Run and watch them pass**
- [ ] **Step 5: Commit** — `feat: raise usage alerts on milestone crossings`

---

### Task 3: Delivery through the tray icon

**Files:**
- Modify: `src/AiUsageMonitor.App/Interop/TrayIcon.cs`, `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs`
- Test: `tests/AiUsageMonitor.App.Tests/WidgetWindowTests.cs`

`ShowHint` becomes `Notify(string title, string text, bool silent)`. Two traps it must close:

- `NOTIFYICONDATA.szInfoTitle` is `ByValTStr` with `SizeConst = 64` and `szInfo` with `SizeConst = 256`. Marshalling a string that does not fit **throws**, and a provider is free to invent a window token of any length. Truncate to 63 and 255.
- `dwInfoFlags` carries `NIIF_NOSOUND` (0x10) for everything except `LimitReached`.

`WidgetWindow` owns one `UsageAlertWatcher` and calls it from `OnTick`, next to `UpdateTrayGlyph`. The tick, not the refresh event, because a card can also change state on the clock alone — and because the watcher is idempotent, so being asked more often than the data changes costs a walk over a handful of rows and raises nothing.

- [ ] **Step 1: Write the failing test** — the widget builds a watcher and raises nothing on a first observation; truncation is asserted on the formatting helper.
- [ ] **Step 2–4: Fail, implement, pass**
- [ ] **Step 5: Commit** — `feat: deliver usage alerts to the notification area`

---

### Task 4: The setting

**Files:**
- Modify: `src/AiUsageMonitor.Infrastructure/Settings/AppSettings.cs`, `src/AiUsageMonitor.App/ViewModels/SettingsViewModel.cs`, `src/AiUsageMonitor.App/Views/SettingsWindow.xaml`, `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs`
- Test: `tests/AiUsageMonitor.App.Tests/SettingsViewModelTests.cs`

`public bool NotifyOnQuotaEvents { get; init; } = true;` — on by default. A new NOTIFICATIONS section in the settings window with one checkbox, "Notify on quota milestones and resets", and a caption naming what that means. The gate lives in `WidgetWindow`, read at the moment an alert would be shown, so switching it off silences the next alert rather than the next restart.

Switching it off does not stop the watcher observing. State keeps advancing while notifications are silent, so switching it back on does not produce a backlog of alerts for crossings that happened in between.

- [ ] **Step 1: Write the failing test** — default is on; the toggle writes through; a change made elsewhere reads back.
- [ ] **Step 2–4: Fail, implement, pass**
- [ ] **Step 5: Commit** — `feat: add the quota notifications setting`

---

### Task 5: Double-click hides the widget

**Files:**
- Modify: `src/AiUsageMonitor.App/Interop/TrayIcon.cs`, `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs`

`WM_LBUTTONDBLCLK` (0x0203) raises a new `DoubleClicked` event, which `WidgetWindow` handles with `HideToTray()` — the same method `Close_Click` calls.

A double-click also delivers a `WM_LBUTTONUP` first, so `Activated` fires before `DoubleClicked`. On a visible window that is a no-op and the double-click simply hides it, which is the case the user is in. On a hidden one it shows and then hides again. Deferring the single click by `GetDoubleClickTime` would remove that flicker at the cost of half a second of lag on every ordinary click to open — the wrong trade for the common action.

- [ ] **Step 1: Commit** — `feat: hide the widget on a tray double-click`

---

### Task 6: Documentation and verification

**Files:**
- Modify: `docs/PRD.md`

§3 lists quota-threshold notifications as out of scope "except where later explicitly added"; this is that addition, so the bullet names it and §28 drops it from future work.

Verification is against the running widget, not only the suite: a real balloon raised from real provider data, and a real double-click measured on the real window.

- [ ] **Step 1: `dotnet build`, clean** — run as its own command
- [ ] **Step 2: `dotnet test`, green** — run as its own command, never chained onto the build
- [ ] **Step 3: Commit**

Steps 1 and 2 are separate shell calls deliberately. `CLAUDE.md`'s Commands section writes them for a human at a terminal; a delegate runs a chain under a per-command time limit and loses the result of a build that already succeeded.

Driving a real balloon against live provider data, and merging, stay with the session that owns the running widget.
