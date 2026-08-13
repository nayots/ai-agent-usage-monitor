# Row Detail and Window Reach — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface the row facts the model already carries but never renders, put the untruncated failure reason one hover away, make saved window placement survive real multi-monitor topology and scaling changes, and give the widget a keyboard path that does not depend on a mouse finding a tray icon.

**Architecture:** Four independent behaviours. The first two are pure presentation over data that already exists — `IsPartial`, `ResetsAt`, `Extra` and the snapshot's mechanism are all populated today and read by nothing. Both are delivered through **enriched tooltips** rather than new expanders: the widget is 360 px wide and already fights for vertical space in compact density, and a per-row disclosure would cost layout everywhere to serve a question asked rarely. The last two are interop: placement moves from a bounding-box test against the whole virtual desktop to a real per-monitor work-area clamp re-applied on display and DPI changes, and a registered system-wide hotkey gives the hidden widget a keyboard entry point.

**Tech Stack:** .NET 10, C#, WPF, xUnit. No new `PackageReference`.

This increment implements **X4**, **C5**, **C6** (the remaining "Details" half), **X7** (with **X18**) and **C9** from `docs/specs/2026-08-13-feature-inventory-and-ideas.md`. It is increment 3 of 3, and assumes increments 1 and 2 have merged.

## Global Constraints

Every task's requirements implicitly include this section.

- **`dotnet build` must be clean. Warnings are errors.**
- **Run `dotnet build` and `dotnet test` as separate commands, never chained.**
- **No new `PackageReference` in any project.** If a task appears to need one, stop and report.
- **No `InternalsVisibleTo` anywhere.** Anything a test must reach is `public`.
- **Never display a credential, token, session identifier, or the contents of `ProviderSnapshot.Notes` in the widget.** `Notes` can carry a local credentials path; it belongs behind a diagnostics view that does not exist yet, not in a tooltip that appears on hover. Only `Error` — which is app-authored after the previous increment — and `Mechanism` may be surfaced.
- **Missing data is `null` and is omitted, never rendered as `0`, `"unknown"`, or an empty placeholder.** A tooltip line for a value the provider did not supply is left out entirely.
- **Unrecognised provider name tokens are preserved verbatim**, never dropped or reinterpreted.
- **Every mechanism carries a visible tier.** Nothing here may present an unofficial value as official.
- **The domain stays provider-neutral.** No property named after a plan period.
- **No administrator privileges. Nothing machine-wide is written.** The hotkey is registered per-process, not per-machine.
- **The application never modifies provider configuration, and never modifies OS settings.** A hotkey conflict is reported, never resolved by changing anything outside this process.
- **One commit per task**, created serially.
- **Never delete untracked files you did not create.**

## File structure

| Path | Responsibility |
|---|---|
| `src/AiUsageMonitor.App/ViewModels/QuotaRowViewModel.cs` | **Modify.** Compose the row detail text and the fuller accessible name. |
| `src/AiUsageMonitor.App/Views/QuotaRowView.xaml` | **Modify.** Bind the tooltip to it. |
| `src/AiUsageMonitor.App/ViewModels/ProviderNotice.cs` | **Modify.** Carry the untruncated reason alongside the bounded body. |
| `src/AiUsageMonitor.App/Views/ProviderCardView.xaml` | **Modify.** Tooltip on the notice body. |
| `src/AiUsageMonitor.App/Interop/ScreenBounds.cs` | **Modify.** Add the clamp used by placement recovery. |
| `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs` | **Modify.** Placement clamping and the hotkey. Touched by tasks 3, 4 and 5 — do those serially. |
| `src/AiUsageMonitor.Infrastructure/Settings/AppSettings.cs` | **Modify.** The hotkey setting. |
| `src/AiUsageMonitor.App/ViewModels/SettingsViewModel.cs` | **Modify.** Expose it and its failure reason. |
| `src/AiUsageMonitor.App/Views/SettingsWindow.xaml` | **Modify.** Render it. |

---

### Task 1: Row detail — partial data, the exact reset instant, the identifier and the discovered keys

**Files:**
- Modify: `src/AiUsageMonitor.App/ViewModels/QuotaRowViewModel.cs`
- Modify: `src/AiUsageMonitor.App/Views/QuotaRowView.xaml`
- Test: additions to `tests/AiUsageMonitor.App.Tests/QuotaRowViewModelTests.cs`

**Background:** `QuotaWindow` carries `IsPartial`, `ResetsAt` and `Extra`, and the snapshot carries `Mechanism`. None reaches the screen. `QuotaRowView`'s tooltip shows only `identifier: {Id}`. A user cannot currently tell whether a blank countdown means the provider reported no reset time or the row failed to render, and the `Extra` dictionary — the audit trail proving the generic extractor is working — is invisible.

**Interfaces — produces:**

```csharp
public sealed class QuotaRowViewModel : ObservableObject
{
    public QuotaRowViewModel(QuotaWindow window, bool colorBarsByUsage, string? mechanism = null);

    /// <summary>The row's full detail, one fact per line, for the tooltip. Never null.</summary>
    public string DetailText { get; }

    public bool IsPartial { get; }
}
```

`ProviderCardViewModel.RebuildWindows` passes the current snapshot's `Mechanism`. The existing two-argument construction used by tests must keep compiling — hence the default.

**The tooltip's content, in this order, omitting any line whose value is absent:**

1. `identifier: {Id}` — unchanged from today, first because it is what the existing tooltip promised.
2. `mechanism: {mechanism}` — only when a mechanism was supplied.
3. `resets at: {ResetsAt.ToLocalTime()}` formatted with `CultureInfo.CurrentCulture` as a general date/time (`"g"`). Local time, because a countdown is already on the row and the exact instant is only useful in the user's own clock.
4. `window duration: {WindowDuration}` rendered as the same duration shape the rest of the widget uses, only when known.
5. `partial data: the provider did not supply a reset time or a window duration` — only when `IsPartial`.
6. One line per `Extra` entry, as `{key}: {value}`, in the dictionary's own order.

**Requirements:**

- `DetailText` joins its lines with `Environment.NewLine`. It is computed once in the constructor — the row is a pure projection rebuilt on every snapshot, and adding observable state to it is explicitly against its design.
- Rendering `Extra` is safe **because every key and every value in it is app-selected**: `DuckTypedQuotaExtractor` writes only its own diagnostic keys (`duckTyped.percentKey`, `duckTyped.resetKey`, `duckTyped.durationKey`, `duration_source`, `source`), and `CodexProbe` writes a fixed list read field-by-field from the response (`limitId`, `slot`, `planType`, `rateLimitReachedType`, `credits.*`). Neither ever copies an arbitrary provider object in. **Preserve that property**: if a future change would put unreviewed provider data into `Extra`, this tooltip becomes a disclosure path and must be revisited. Say so in a comment.
- `AccessibleName` gains the partial fact and the exact reset instant, so a screen reader gets what the tooltip gives sighted users. Keep its existing shape and append: `", partial data"` when `IsPartial`, and `", resets at {exact}"` when `ResetsAt` is known.
- `QuotaRowView.xaml` binds `ToolTip` to `DetailText` instead of `IdentifierTooltip`. Keep `IdentifierTooltip` only if something else uses it; otherwise remove it and its test rather than leaving a dead property.

**Acceptance criteria:**

1. A fully populated window produces all six line kinds, in the stated order.
2. A window with `ResetsAt == null` omits the `resets at:` line entirely — assert the string does not contain `"resets at"`, and does not contain `"null"`, `"0"` as a standalone value, or an empty `key: ` fragment.
3. A partial window includes the partial line; a complete window does not.
4. A row constructed without a mechanism omits the `mechanism:` line.
5. `Extra` entries appear as `key: value` lines, and a window with an empty `Extra` produces none.
6. `AccessibleName` for a partial window with a known reset contains both additions and still reads as one sentence.
7. `ControlLoadingTests` / the row's existing palette-loading test still loads `QuotaRowView.xaml` in every palette.

---

### Task 2: The failure reason, one hover away

**Files:**
- Modify: `src/AiUsageMonitor.App/ViewModels/ProviderNotice.cs`
- Modify: `src/AiUsageMonitor.App/Views/ProviderCardView.xaml`
- Test: additions to `tests/AiUsageMonitor.App.Tests/ProviderNoticeTests.cs`

**Background:** `Compose` bounds the reason at 200 characters and appends `…`. The bound is right for a card, but the truncated tail is then unreachable — the full text exists only in the log, which the user has to know to suspect.

**Interfaces — produces:**

```csharp
public sealed record ProviderNotice(
    string Title,
    string Body,
    bool IsAlert,
    string? ActionText,
    string? DetailText = null);
```

**Requirements:**

- `DetailText` is the **untruncated** composed body when truncation actually occurred, and `null` otherwise. A tooltip that repeats what is already on screen is noise.
- `Compose` must not split a UTF-16 surrogate pair when it truncates. Walk back one index if `reason[maxReasonLength - 1]` is a high surrogate. Every reason is app-authored today, so this is defence rather than a live bug — but the bound is the one place a future non-ASCII message could produce a broken glyph.
- `ProviderCardView.xaml` sets `ToolTip` on the notice **body** `TextBlock`, bound to `Notice.DetailText`. A WPF `ToolTip` bound to `null` does not display, so no trigger is needed — confirm that with the loading test rather than assuming.
- Still no `Notes`, no raw body, no headers, no paths. Only the composed, app-authored reason.

**Acceptance criteria:**

1. A short reason → `Body` contains it in full and `DetailText` is `null`.
2. A 250-character reason → `Body` is the lead plus 200 characters plus `…`; `DetailText` is the lead plus all 250 characters and carries no ellipsis.
3. A `null` or whitespace reason → `Body` is the lead alone, `DetailText` is `null`.
4. A reason whose 200th character falls inside a surrogate pair truncates to 199 characters rather than producing a lone surrogate. Construct the input from an astral-plane character (an emoji) so the assertion is real.
5. `ViewLoadingTests` still renders the card in every palette.

---

### Task 3: Recover placement against real monitor work areas

**Files:**
- Modify: `src/AiUsageMonitor.App/Interop/ScreenBounds.cs`
- Modify: `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs`
- Test: `tests/AiUsageMonitor.App.Tests/PlacementClampTests.cs` (create)

**Background:** `RestorePlacement` tests the saved point against the bounding `SystemParameters.VirtualScreen` rectangle using a synthetic 100-pixel height, and centres only when there is no intersection at all. That bounding box includes the gaps between mismatched monitors, so a point in a gap passes. It also accepts a one-pixel sliver, ignores the taskbar, and interprets coordinates saved under one scaling factor as if they were DIPs under the current one.

**Interfaces — produces:**

```csharp
public static class PlacementClamp
{
    /// <summary>
    /// The window rectangle moved — never resized — so it sits wholly inside <paramref name="workArea"/>.
    /// A window larger than the work area is aligned to its top-left, so the title bar stays reachable.
    /// Pure, so the rule is testable without a monitor.
    /// </summary>
    public static Rect Fit(Rect window, Rect workArea);
}
```

Put it in `src/AiUsageMonitor.App/Interop/PlacementClamp.cs` and make it `public` — it must be reachable from the test project without `InternalsVisibleTo`. `ScreenBounds` gains nothing new if `WorkAreaFor(Window)` already suffices; it does, because the clamp runs once the window has a handle.

**Requirements:**

- **Move the clamp out of the constructor.** `RestorePlacement` currently runs before `OnSourceInitialized`, so there is no handle and therefore no way to ask which monitor the window is on. Keep the constructor's job to the minimum: if the settings carry a position, set `WindowStartupLocation = Manual`, `Left` and `Top`; otherwise `CenterScreen` as today.
- Apply the clamp in `OnContentRendered`, where the window has both a handle and a measured `ActualHeight` (the window is content-sized, so `ActualHeight` is 0 earlier). Compute `new Rect(Left, Top, ActualWidth, ActualHeight)`, fit it to `ScreenBounds.WorkAreaFor(this)`, and assign `Left`/`Top` back only if they changed.
- Re-apply on `SystemEvents.DisplaySettingsChanged` and on `Window.DpiChanged`. Marshal `DisplaySettingsChanged` to the dispatcher — like the lifecycle hooks in the previous increment, it does not arrive on the UI thread — and **detach it in `OnClosed`**, for the same static-subscriber-list reason.
- `MonitorFromWindow` with `MONITOR_DEFAULTTONEAREST` — which `ScreenBounds` already uses — resolves a window at `-30000,-30000` to the nearest real monitor, so the clamp subsumes the old gross-offscreen fallback. Delete the `VirtualScreen` intersection test; do not keep both.
- Placement is still saved on close, unchanged.

**Acceptance criteria — all against `PlacementClamp.Fit`, no window required:**

1. A window wholly inside the work area is returned unchanged.
2. A window overhanging the right edge is moved left so its right edge meets the work area's; its width is unchanged.
3. Equivalents for left, top and bottom overhang.
4. A window in a virtual-desktop gap — outside the work area entirely — is moved wholly inside.
5. A window at `-30000,-30000` lands wholly inside.
6. A window one pixel inside the right edge is moved so all of it is visible, not left as a sliver.
7. A window **taller than the work area** is aligned to the work area's top-left and keeps its size — the title bar must stay reachable, and shrinking is not this function's job.
8. A work area with a non-zero origin (a secondary monitor at `1920,0`, or a taskbar-inset top) is honoured — assert with an origin that is not `0,0`, since that is the case a naive implementation gets wrong.

---

### Task 4: A keyboard path to the widget

**Files:**
- Modify: `src/AiUsageMonitor.Infrastructure/Settings/AppSettings.cs`
- Modify: `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs`
- Modify: `src/AiUsageMonitor.App/ViewModels/SettingsViewModel.cs`
- Modify: `src/AiUsageMonitor.App/Views/SettingsWindow.xaml`
- Test: additions to `tests/AiUsageMonitor.App.Tests/SettingsViewModelTests.cs`

**Background:** A widget that hides to the tray on focus loss is reachable only by finding a 16-pixel icon with a mouse.

**Scope decision:** one fixed combination, not a capture UI. A configurable chord needs a key-capture control, a persisted key-code format, and conflict re-registration — a feature in its own right. A fixed default that can be switched off, and that says so when the OS refuses it, is the whole of what C9 asks for.

**Interfaces — produces:**

```csharp
public sealed record AppSettings
{
    /// <summary>Ctrl+Alt+Q shows or hides the widget from anywhere. On by default.</summary>
    public bool GlobalHotkeyEnabled { get; init; } = true;
}

public sealed class SettingsViewModel
{
    public bool GlobalHotkeyEnabled { get; set; }
    public string GlobalHotkeyLabel { get; }          // "Ctrl+Alt+Q"
    public string? GlobalHotkeyUnavailableReason { get; }
    public bool HasGlobalHotkeyWarning { get; }
}
```

**Requirements:**

- Register with `RegisterHotKey(hwnd, id, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_Q)` in `OnSourceInitialized` when the setting is on. `MOD_NOREPEAT` is `0x4000` and matters: without it, holding the chord fires continuously. `VK_Q` is `0x51`.
- Handle `WM_HOTKEY` (`0x0312`) in the **existing** `OnWindowMessage` hook, matching on the registration id. Do not add a second hook.
- Behaviour: if the widget is hidden, or visible but not the foreground window, show and activate it — reuse `ShowFromTray`, which already cancels a pending dismissal. If it is visible **and** foreground, `HideToTray`. The chord is a toggle, which is what makes it a path both in and out.
- `UnregisterHotKey` in `OnClosed`. Re-register or unregister when the setting changes, on the existing `OnSettingsChanged` path.
- **`RegisterHotKey` returns false when another process owns the combination.** That is an ordinary outcome, not an error: log it, leave the widget fully working, and expose `GlobalHotkeyUnavailableReason` as exactly `"Unavailable: another application already uses this shortcut."` The settings window shows the reason in place of a silently dead checkbox — the same pattern `StartWithWindowsUnavailableReason` already uses, so follow that markup.
- The window must publish the registration outcome to the settings view model. `SettingsViewModel` is constructed per settings-window opening in `WidgetWindow.ShowSettings`, so pass the current outcome in as a constructor argument rather than wiring an event.
- Do not attempt to take a combination another application owns, and do not fall back to a second chord. One combination, reported honestly.

**Acceptance criteria:**

1. `AppSettings.Default.GlobalHotkeyEnabled` is `true`, and the property round-trips through `AppSettingsStore` save/load.
2. `SettingsViewModel` with a successful registration: `HasGlobalHotkeyWarning` false, `GlobalHotkeyUnavailableReason` null, `GlobalHotkeyLabel == "Ctrl+Alt+Q"`.
3. With a failed registration: warning true, reason exactly the string above.
4. Setting `GlobalHotkeyEnabled` writes through `SettingsService.Update` like every other setting.
5. `ViewLoadingTests.TheSettingsWindowRendersInEveryPalette` still passes.
6. Build clean, and `UnregisterHotKey` appears in `OnClosed` — a hotkey outliving the process is a real leak of a global resource.

---

### Task 5: Keyboard reach inside the widget

**Files:**
- Modify: `src/AiUsageMonitor.App/Views/WidgetWindow.xaml` and `.xaml.cs`
- Modify: `src/AiUsageMonitor.App/Views/ProviderCardView.xaml` (only if a focus stop is missing)

**Background:** Arriving by hotkey is useless if nothing inside can be reached without a mouse.

**Requirements:**

- **`Esc` hides the widget to the tray**, matching the close button and Alt+F4. Handle it on the window's `PreviewKeyDown` so it works wherever focus sits. It must respect the pin: a pinned widget is exempt from dismissal, and `Esc` is a dismissal — leave a pinned widget alone, exactly as `DismissIfFocusLeftTheApplication` does.
- **On `ShowFromTray`, move focus to the footer Refresh button** after `Activate()`, so the widget is immediately operable. Use `Dispatcher.BeginInvoke` at `DispatcherPriority.Input` — focusing before the window has finished activating silently does nothing.
- **Every interactive control is a tab stop in visual order**: the title bar's pin, settings and close buttons; each card's Retry/Refresh button; the footer Refresh. The `ItemsControl` is already `Focusable="False"`, which is what keeps it from swallowing a tab — verify it still is.
- **Do not suppress `FocusVisualStyle`** on the custom button styles. If `LinkButtonStyle` or the title-bar button style sets it to `{x:Null}`, restore a visible ring using an existing theme brush; a keyboard path with no visible focus is not a keyboard path. Check all three theme dictionaries.
- No new key bindings beyond `Esc`. Tab order is a property of the visual tree, so prefer fixing the tree over hard-coding `TabIndex`.

**Acceptance criteria:**

1. Build clean; `ViewLoadingTests` still renders the widget in every palette.
2. A test asserting `Esc` on an unpinned widget hides it and on a pinned widget does not. If the existing test harness cannot raise a key event against a real window, assert the same decision through a small `public static bool ShouldDismissOnEscape(bool isPinned, bool isVisible)` helper and call that from the handler — the rule matters more than the plumbing.
3. State in the task report which controls are tab stops and whether any `FocusVisualStyle` had to be restored. This one is partly verified by eye; say plainly what was and was not proven by a test.

---

## Out of scope — recorded, not forgotten

| Excluded | Why |
|---|---|
| A per-row expander (C5's literal proposal) | The widget is 360 px wide and compact density already sacrifices metadata to fit. A tooltip carries the same facts at no layout cost. Recorded as a deliberate departure from C5's wording, not a partial delivery. |
| Showing `ProviderSnapshot.Notes` anywhere | `Notes` can contain a local credentials path. It belongs in a diagnostics view (C10 / X10) reached deliberately, not on hover. |
| A configurable hotkey chord | Needs a key-capture control and a persisted key-code format. One fixed, switchable, honestly-reported combination answers C9. |
| Resizing the window to fit a small work area | `PlacementClamp.Fit` moves, never resizes. The window is content-sized and capped at 520; changing that is a layout decision with no requirement behind it. |
| Persisting which monitor the widget was on | Placement is a point, and the clamp makes any point recoverable. Storing a monitor id adds a device-identity concept the settings file does not otherwise have. |
| Diagnostics view (C10 / X10), local history (C11 / X14), reset application data (X11), zero-provider home state (X12), restored snapshots (X13), localization (X15) | All §2.2 / §3.2 new features. This increment is §2.1 and §3.1 only. |

## Verification

**A copy of the widget is running on this machine and holds `src/AiUsageMonitor.App/bin/…`.** A plain `dotnet build` therefore fails with `MSB3021: The process cannot access the file … because it is being used by another process`. That is an environment condition, not a defect in your work, and **it is not a reason to kill the user's process.** Redirect the output instead, and run the two commands **separately**:

```powershell
dotnet build -p:BaseOutputPath=$env:TEMP/aium-build/
dotnet test  -p:BaseOutputPath=$env:TEMP/aium-build/
```

Both must be clean. State before/after test counts, and state explicitly which of Task 5's criteria were proven by a test and which by inspection.

Never terminate `AiUsageMonitor.App.exe`, and never write build output inside the repository.
