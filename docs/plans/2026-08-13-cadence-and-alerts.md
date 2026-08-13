# Cadence, Honesty and Alerts — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop the widget doing full-rate presentation work while nothing is on screen, force a refresh at the two moments data is definitely stale, say out loud when a provider is being deliberately skipped, stop a settings combination that parks every card in `Stale`, and fix the two notification-area cases that read wrongly — a burst of balloons and a glyph that says nothing at 100%.

**Architecture:** Five independent behaviours sharing one theme: *what the application does when no one is looking, and what it says about it.* Nothing here changes what a provider reports or how it is parsed. The one design decision worth stating up front: **provider polling cadence does not change when the widget is hidden.** Hidden-to-tray is this application's primary operating mode, not an idle state — the tray glyph and the quota notifications are the entire product in that mode, and slowing their input would make them stale precisely when they are the only output. Only the *presentation* tick slows.

**Tech Stack:** .NET 10, C#, WPF, xUnit. `Microsoft.Win32.SystemEvents` is part of the Windows Desktop shared framework and needs no `PackageReference` — verified present in the `Microsoft.WindowsDesktop.App.Ref` pack.

This increment implements **C2**, **C3**, **C4**, **C7**, **C8** and **X8** (as narrowed by **X21**) from `docs/specs/2026-08-13-feature-inventory-and-ideas.md`. It is increment 2 of 3, and assumes increment 1 (`2026-08-13-provider-boundaries-and-idle-cost.md`) has merged.

## Global Constraints

Every task's requirements implicitly include this section.

- **`dotnet build` must be clean. Warnings are errors.**
- **Run `dotnet build` and `dotnet test` as separate commands, never chained.**
- **No new `PackageReference` in any project.** If a task appears to need one, stop and report.
- **No `InternalsVisibleTo` anywhere.** Anything a test must reach is `public`.
- **Missing data is `null`, surfacing as `Waiting`/`Unavailable` — never `0`, never a placeholder.** A countdown that cannot be computed is omitted, not zeroed.
- **Any string assigned to `ProviderSnapshot.Error` is rendered verbatim on a visible card.** Treat it as UI copy.
- **A notification carries no failure reason** (PRD §16.2). It may state that a provider stopped reporting and direct the user to the widget. No error text, response body, header, path or credential may appear in a balloon — a card is read deliberately, a balloon appears unbidden over whatever the user was doing.
- **Notifications stay edge-triggered and observed even when delivery is off.** Rungs continue to advance while `NotifyOnQuotaEvents` is false, so switching it back on releases no backlog. `WidgetWindow.DeliverAlerts` already has this shape — preserve it.
- **Only the 100% `LimitReached` alert makes a sound.** Everything else is silent.
- **The domain stays provider-neutral.** No property named after a plan period; no assumption about a window's duration, count or name.
- **Windows-only, PowerShell 5.1 as the shell.** No admin privileges. Nothing machine-wide is written.
- **The application never modifies provider configuration.**
- **One commit per task**, created serially.
- **Never delete untracked files you did not create.**

## File structure

| Path | Responsibility |
|---|---|
| `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs` | **Modify.** Tick cadence, lifecycle hooks, alert delivery. Touched by tasks 1, 2 and 5 — do those serially. |
| `src/AiUsageMonitor.Infrastructure/Refresh/ProviderRefreshService.cs` | **Modify.** Expose when a provider is next eligible. |
| `src/AiUsageMonitor.App/ViewModels/MainViewModel.cs` | **Modify.** Push the next-attempt fact onto each card on tick. |
| `src/AiUsageMonitor.App/ViewModels/ProviderCardViewModel.cs` | **Modify.** Render the deferral as copy. |
| `src/AiUsageMonitor.App/Views/ProviderCardView.xaml` | **Modify.** Show it on the status line. |
| `src/AiUsageMonitor.App/ViewModels/SettingsViewModel.cs` | **Modify.** The stale-versus-interval warning. |
| `src/AiUsageMonitor.App/Views/SettingsWindow.xaml` | **Modify.** Render it. |
| `src/AiUsageMonitor.App/Notifications/AlertBatch.cs` | **Create.** The pure coalescing rule. |
| `src/AiUsageMonitor.App/ViewModels/TrayGlyphState.cs` | **Modify.** The 100% digits rule. |

---

### Task 1: Slow the presentation tick when nothing is on screen

**Files:**
- Modify: `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs`
- Test: `tests/AiUsageMonitor.App.Tests/WidgetTickCadenceTests.cs` (create)

**Background:** `_tick` runs at 1 s for the life of the process. It exists to advance visible countdown strings and elapsed markers. When the window is hidden it is still walking every card, reformatting every countdown, and reading the taskbar's theme, sixty times a minute, forever.

**Requirements:**

- Add a public static rule so the decision is testable without a window:

```csharp
public static class TickCadence
{
    public static readonly TimeSpan Visible = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan Hidden = TimeSpan.FromSeconds(5);

    public static TimeSpan For(bool isVisible);
}
```

  Put it in `src/AiUsageMonitor.App/Notifications/TickCadence.cs` alongside the other presentation-rate concerns, or in `ViewModels/` — either is fine, but it must be `public` and free of any WPF `Window` dependency.
- `WidgetWindow` sets `_tick.Interval = TickCadence.For(isVisible)` when it hides and when it shows. `HideToTray` switches to `Hidden`; `ShowFromTray` switches to `Visible` **and calls `OnTick` immediately**, so a widget that has just appeared is never showing a countdown up to five seconds out of date.
- **Do not change `_poll.Interval` or `ProviderRefreshService.BaseInterval` in either direction.** Record the reason in a comment on the cadence change: hidden-to-tray is the primary operating mode; the glyph and the quota notifications are what the application is *for* in that mode, and both are fed by provider polling. A five-second lag on a countdown string nobody can see costs nothing; a slower poll would make the only visible output stale.
- The alert observation and glyph update stay on the tick. A five-second worst-case lag between a snapshot arriving and the glyph redrawing is immaterial against a 60 s poll floor, and it keeps one path responsible for both.

**Acceptance criteria:**

- `TickCadence.For(true) == 1s`, `For(false) == 5s`.
- A test asserting the constants are what the comment claims, so a later edit that quietly raises `Hidden` to a minute fails.
- No change to `_poll` anywhere in the diff. State this explicitly in the task report.

---

### Task 2: Force a refresh on resume and unlock

**Files:**
- Modify: `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs`

**Background:** There is no power or session lifecycle hook at all. A machine that slept for six hours resumes with every card showing data from before the sleep and waits out the remaining poll interval before correcting itself — and if the provider is mid-backoff, up to eight intervals.

**Requirements:**

- Subscribe in `OnSourceInitialized`:
  - `SystemEvents.PowerModeChanged` — on `PowerModes.Resume`, force a refresh.
  - `SystemEvents.SessionSwitch` — on `SessionSwitchReason.SessionUnlock`, force a refresh.
- "Force a refresh" means `_ = _model.RefreshAsync(force: true)`, which ignores backoff — the same path the footer Refresh button uses. Also call `OnTick` so freshness and the glyph re-evaluate immediately.
- **Both handlers must be detached in `OnClosed`.** `SystemEvents` holds its subscribers in a static list; a handler left attached keeps a closed window alive for the life of the process. Use named methods, not lambdas, so they can be detached — the file already documents this trap for `ThemeManager.Changed`.
- `SystemEvents` raises on a dedicated thread, not the UI thread. Marshal through `Dispatcher.BeginInvoke` before touching any view model or timer.
- Wrap the subscription in a `try`/`catch` that logs and continues. A machine where `SystemEvents` cannot install its message window must still get a working widget — this is a convenience, never a startup dependency.

**Acceptance criteria:**

- Build clean. Subscribe and detach are symmetric and both are in the diff.
- The refresh is `force: true` — assert by reading the call, and say so in the task report.
- No `PackageReference` was added. If `Microsoft.Win32.SystemEvents` does not resolve, **stop and report** rather than adding a package.

---

### Task 3: Say when a provider is being deliberately skipped

**Files:**
- Modify: `src/AiUsageMonitor.Infrastructure/Refresh/ProviderRefreshService.cs`
- Modify: `src/AiUsageMonitor.App/ViewModels/MainViewModel.cs`
- Modify: `src/AiUsageMonitor.App/ViewModels/ProviderCardViewModel.cs`
- Modify: `src/AiUsageMonitor.App/Views/ProviderCardView.xaml`
- Test: additions to `ProviderRefreshServiceTests.cs` and `ProviderCardViewModelTests.cs`

**Background:** Backoff is correct and invisible. A provider that has failed three times is skipped for up to eight refresh intervals while its card ages silently; `Updated 14m ago` under a 60 s interval reads as a bug rather than as deliberate restraint.

**Interfaces — produces:**

```csharp
public sealed class ProviderRefreshService
{
    /// <summary>
    /// When this provider is next eligible for an unforced attempt, or null when it is not being
    /// deferred. A manual retry ignores this entirely.
    /// </summary>
    public DateTimeOffset? NextAttemptFor(ProviderDescriptor provider, DateTimeOffset now);
}

public sealed class ProviderCardViewModel
{
    public void SetNextAttempt(DateTimeOffset? nextAttempt);
    public string? NextCheckText { get; }
    public bool HasNextCheckText { get; }
}
```

**Requirements:**

- `NextAttemptFor` returns `null` unless the provider has a `Backoff` record whose `NextAttempt` is in the future. It must take the same `_gate` lock the rest of the service uses.
- `MainViewModel.Tick()` calls `card.SetNextAttempt(_refresh.NextAttemptFor(provider, now))` for each provider before ticking the card. `MainViewModel` already holds both `_refresh` and the descriptor-to-card map.
- `NextCheckText` is `"Next check in " + RelativeTime.FormatCountdown-or-equivalent`. Reuse whatever `QuotaFormatting.FormatCountdown` produces for a `TimeSpan` so the widget has one countdown format; if that helper is not reachable from this project, use `RelativeTime` and match its shape. Do not invent a third duration format.
- `NextCheckText` is non-null **only** when both are true: `SetNextAttempt` was given a future instant, **and** `State is ConnectionState.Error or ConnectionState.Unavailable`. A healthy card must never sprout a countdown to its own routine poll — that is noise, and C2 is about the silent-skip case specifically.
- Render it on the existing status line in `ProviderCardView.xaml`, after the timestamp, in the same `CaptionTextStyle` / `TextTertiaryBrush` treatment, separated by the same `·` the timestamp already uses. Bind visibility to `HasNextCheckText`.
- **Note for the reviewer:** `ProviderCardViewModel.TimestampLine`'s doc comment says the card has exactly one statement of time. That still holds for *age*. This is a statement about the future, it appears only while the card is failing, and it exists because the age line alone is what reads as a bug. Extend that doc comment to say so rather than leaving the two in apparent contradiction.

**Acceptance criteria:**

1. `NextAttemptFor` returns null for a provider that has never failed; returns a future instant after consecutive failures; returns null again once the instant has passed; returns null after a success resets the failure count.
2. A card given a future next-attempt while `State == Error` exposes `NextCheckText`; the same card while `State == Connected` exposes `null`.
3. A card given `null` exposes `null`.
4. `ViewLoadingTests` (or the existing palette-loading test for the card) still loads `ProviderCardView.xaml` in every palette — a new binding that references a missing resource key must fail a test, not only at runtime.

---

### Task 4: Stop a settings combination that parks every card in Stale

**Files:**
- Modify: `src/AiUsageMonitor.App/ViewModels/SettingsViewModel.cs`
- Modify: `src/AiUsageMonitor.App/Views/SettingsWindow.xaml`
- Test: additions to `tests/AiUsageMonitor.App.Tests/SettingsViewModelTests.cs`

**Background:** Nothing stops a 60 s stale threshold under a 300 s refresh interval. Every card then crosses the staleness threshold before its next refresh can possibly arrive, and the widget permanently looks broken with no indication that the user configured it that way.

**Decision:** *Say so, do not silently override.* The settings file is deliberately hand-editable and a value the user typed must not be quietly replaced. C4 offers "derive a floor from the interval **or** say so in the settings window" — this takes the second.

**Interfaces — produces:**

```csharp
public sealed class SettingsViewModel
{
    public bool HasStaleThresholdWarning { get; }
    public string StaleThresholdWarningText { get; }
}
```

**Requirements:**

- `HasStaleThresholdWarning` is true when `settings.StaleAfter <= settings.RefreshInterval`. Compare the **clamped** `TimeSpan` properties, not the raw second counts, so a hand-edited out-of-range value is judged on what the application will actually do.
- `StaleThresholdWarningText` is exactly: `"Cards will always look stale — this is shorter than the refresh interval."`
- Raise the property when settings change. `OnSettingsChanged` already calls `Raise(null)`, which covers it; confirm rather than assume.
- Render it in `SettingsWindow.xaml` immediately beneath the stale-threshold choices, collapsed by a `DataTrigger` on `HasStaleThresholdWarning`, using `{DynamicResource StateWarnBrush}` and `BasedOn` `CaptionTextStyle`. **Verify both resource keys exist in all three theme dictionaries before using them** — `StateWarnBrush` is used by `ProviderCardView.xaml`'s stale banner, so it should, but check.
- This is a warning, not an error. It never blocks a change and never alters a stored value.

**Acceptance criteria:**

1. Stale 60 / refresh 300 → warning shown. Stale 300 / refresh 60 → not shown. Stale 60 / refresh 60 → shown (equal is already too tight: the threshold expires exactly as the next attempt begins).
2. The warning text matches the string above character for character, em dash included.
3. `ViewLoadingTests.TheSettingsWindowRendersInEveryPalette` still passes — this is what proves the new markup actually loads.

---

### Task 5: Coalesce a burst of alerts

**Files:**
- Create: `src/AiUsageMonitor.App/Notifications/AlertBatch.cs`
- Modify: `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs`
- Test: `tests/AiUsageMonitor.App.Tests/AlertBatchTests.cs` (create)

**Background:** One window produces one alert, correctly. But a resume from sleep can cross rungs on several windows across both providers in a single tick and stack four balloons over whatever the user was doing.

**Interfaces — produces:**

```csharp
namespace AiUsageMonitor.App.Notifications;

/// <summary>
/// Turns one tick's alerts into what should actually be shown. Pure, so the rule is testable
/// without a notification area.
/// </summary>
public static class AlertBatch
{
    public static IReadOnlyList<UsageAlert> Coalesce(IReadOnlyList<UsageAlert> alerts);
}
```

**The rule:**

1. Every `UsageAlertKind.LimitReached` alert is passed through individually and unchanged, in order. These are the moment work actually stops — PRD §16.2 makes them the one exception worth hearing, and merging them away would delete the only alert that carries a sound.
2. Of the remainder: zero → nothing; exactly one → passed through unchanged; two or more → replaced by a single alert with `Kind = UsageAlertKind.Milestone`, `Title = $"{n} quota updates"`, `Text = "Open the widget for detail."`, silent.
3. `LimitReached` alerts come first in the returned list, then the remainder or its summary.

The summary deliberately carries no reason and no provider name — a balloon that appears unbidden says what happened and points at the widget, per §16.2.

**Requirements:**

- `WidgetWindow.DeliverAlerts` applies `AlertBatch.Coalesce` **after** the `NotifyOnQuotaEvents` gate, not before. The watcher's observation must stay unconditional; only delivery is affected.
- `UsageAlert.IsSilent` must keep meaning what it means today. If it is derived from `Kind`, the summary's `Milestone` kind already yields silent and nothing extra is needed — confirm rather than assume, and if `IsSilent` is a constructor value, pass `true`.

**Acceptance criteria:**

1. Empty in → empty out.
2. One milestone in → that same instance out.
3. Three milestones in → one alert out, titled `"3 quota updates"`, text `"Open the widget for detail."`, silent.
4. Two milestones plus one `LimitReached` → **two** alerts out: the `LimitReached` unchanged and first, then the `"2 quota updates"` summary.
5. Two `LimitReached` alerts and nothing else → both out, unchanged, neither summarised.
6. One milestone plus one `LimitReached` → both out unchanged (the remainder is a single alert, so no summary).
7. The `LimitReached` alert that passes through is still the sounding one — assert `IsSilent == false` on it and `true` on the summary.

---

### Task 6: Let the tray glyph say 100%

**Files:**
- Modify: `src/AiUsageMonitor.App/ViewModels/TrayGlyphState.cs`
- Test: additions to the existing `TrayGlyphState` tests

**Background:** `DigitsFor` drops any reading that renders to three characters, so at 100% the glyph shows no number — visually identical to "no data", with only the alert overlay to tell them apart. The existing doc comment gives a good reason for the rule: *"a 99.6 that rounds to 100 — no number at all beats one that claims a limit the user has not hit."* That objection is about rounding up to a limit not reached. It is not an objection to showing 100 when the reading really is 100.

`TrayGlyphRenderer.DrawDigits` already compresses a three-character reading horizontally to keep it inside the square (`horizontal = Math.Min(1d, size / bounds.Width)`), so the renderer needs no change at all.

**The rule, replacing `DigitsFor`:**

- No reading → `null` (unchanged).
- Rounds to two characters or fewer → that value (unchanged).
- Rounds to `"100"` **and the raw reading is `>= 100`** → `"100"`.
- Rounds to `"100"` but the raw reading is `< 100` → `"99"`. The reading genuinely is ninety-nine point something; ninety-nine is the honest two-character rendering of it, and it never claims a limit that has not been hit.
- Anything else rendering to three or more characters (a provider over-reporting, say `120`) → `"99"` by the same rule? **No** — return `"100"` for any raw reading `>= 100`. A provider over-reporting is still at or past its limit, and `100` is the truthful cap for a glyph. Only readings strictly below 100 that *round* to 100 fall back to `"99"`.

**Requirements:**

- Update the doc comment to record the revised reasoning; do not leave the old justification standing over new behaviour.
- Nothing else about `TrayGlyphState` changes — bars, overlay selection and `Matches` stay as they are. The alert overlay at 100% remains.

**Acceptance criteria:**

1. `null` → `null`.
2. `0` → `"0"`. `7.4` → `"7"`. `83.5` → `"84"`.
3. `100` → `"100"`.
4. `100.0001` and `120` → `"100"`.
5. `99.6` → `"99"` — the case the original comment exists for. Assert it is **not** `"100"`.
6. `99.4` → `"99"` by ordinary rounding.
7. A glyph state built from a card whose primary window is at 100 has `Digits == "100"` **and** `Overlay == TrayOverlay.Alert`.
8. `TrayGlyphRenderer.RenderBitmap` with `digits: "100"` at size 16 returns a non-null bitmap and does not throw. The renderer is unchanged, but nothing currently exercises the three-digit path end to end.

---

## Out of scope — recorded, not forgotten

| Excluded | Why |
|---|---|
| **Slowing provider polling when hidden (the second half of C3)** | **Rejected**, on X21's argument: hidden-to-tray is the primary operating mode, and the tray glyph and quota notifications — the entire product in that mode — are fed by polling. §16.2 exists precisely because the widget spends most of its life hidden. The presentation tick slows; the data does not. |
| Battery- and lock-aware poll reduction (also C3 / X8) | Same reason. With cadence unchanged there is no reduction to make visible, so X8's "keep any reduction visible in the copy" requirement is satisfied vacuously. Revisit only if measurement shows polling actually costs something. |
| Deriving a stale-threshold floor from the refresh interval | C4 offers this as the alternative to saying so. Silently overriding a hand-edited value contradicts the settings file's stated design. |
| User-configurable notification thresholds (C12), quiet hours (C13) | §2.2 new features, not §2.1 improvements. |
| A "next check" countdown on healthy cards | Noise. C2's complaint is specifically that a *skipped* poll is silent. |
| Diagnostics exposure of backoff reason (X10) | Its own increment. `NextAttemptFor` is the piece that increment will need, and it lands here. |

## Verification

**A copy of the widget is running on this machine and holds `src/AiUsageMonitor.App/bin/…`.** A plain `dotnet build` therefore fails with `MSB3021: The process cannot access the file … because it is being used by another process`. That is an environment condition, not a defect in your work, and **it is not a reason to kill the user's process.** Redirect the output instead, and run the two commands **separately**:

```powershell
dotnet build -p:BaseOutputPath=$env:TEMP/aium-build/
dotnet test  -p:BaseOutputPath=$env:TEMP/aium-build/
```

Both must be clean. State before/after test counts in the final summary, and state explicitly that `_poll.Interval` and `ProviderRefreshService.BaseInterval` are untouched.

Never terminate `AiUsageMonitor.App.exe`, and never write build output inside the repository.
