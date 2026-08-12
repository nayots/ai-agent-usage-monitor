# Settings window and system tray — design

Date: 2026-08-12
Status: approved, not yet planned
Spec authority: `docs/PRD.md` §17 (window behaviour and tray menu), §19 (settings), §21 (layering)

## Why this increment

Every application-owned setting exists in `AppSettings` and is honoured at startup, and none of
them can be changed without hand-editing `%APPDATA%\AiUsageMonitor\settings.json`. The title bar's
`✕` ends the process, so a widget meant to sit in the corner all day has no way to get out of the
way and come back. These are the two gaps that stop the app being usable by someone who is not
holding a text editor.

Release packaging is deliberately not here — it comes last, under semantic versioning.

## 1. Settings become owned rather than captured

### The problem

`AppSettings` is an immutable record constructed once in `App.OnStartup` and handed to five
independent consumers, each of which keeps its own copy:

| Consumer | What it captured |
|---|---|
| `ThemeManager.Apply` | `Theme` |
| `ProviderRefreshService` ctor | `RefreshInterval` as `baseInterval` |
| `WidgetWindow` ctor | `AlwaysOnTop`, `RefreshInterval`, `WindowLeft`/`WindowTop` |
| `MainViewModel` ctor | `StaleAfter`, `ColorBarsByUsage` |
| `AppSettingsStore` | the file itself |

Nothing observes a change because nothing can produce one.

### The change

One owner, in `Infrastructure/Settings`:

```csharp
public sealed class SettingsService
{
    public SettingsService(AppSettingsStore store, AppSettings initial);

    public AppSettings Current { get; private set; }
    public event EventHandler<AppSettings>? Changed;

    /// Read-modify-write against Current, persist, then raise. Never takes a whole AppSettings.
    public void Update(Func<AppSettings, AppSettings> change);
}
```

`Update` takes a *function* rather than a value on purpose. Every caller therefore edits the
current state rather than a state it captured earlier, which closes a bug that is latent today and
becomes reachable the moment settings are editable: `WidgetWindow.SavePlacement` writes
`_settings with { WindowLeft, WindowTop }` where `_settings` is the startup snapshot. Once the
settings window can write, closing the widget would silently revert every change made that
session.

A failed save must not lose the in-memory change or throw into the UI. `Update` applies to
`Current` and raises `Changed` first, then persists; an `IOException` or `UnauthorizedAccessException`
is logged and swallowed, exactly as `SavePlacement` already does. The setting takes effect now and
is lost on restart — the alternative, refusing the change because a disk write failed, is worse.

### What each consumer does on `Changed`

| Setting | Applied by | How |
|---|---|---|
| `Theme` | `ThemeManager.Apply` | already live; call it again |
| `AlwaysOnTop` | `WidgetWindow` | `Topmost = value` |
| `RefreshIntervalSeconds` | `WidgetWindow`, `ProviderRefreshService` | `_poll.Interval`; `BaseInterval` becomes a settable property (it feeds only the backoff computation) |
| `StaleAfterSeconds` | `MainViewModel` | `_freshness` stops being `readonly`; rebuild the `FreshnessPolicy`, then `Tick()` |
| `ColorBarsByUsage` | `MainViewModel` → each card | see below |
| `ShowUnavailableProviders` | `MainViewModel` | see below |
| `StartWithWindows` | `StartupRegistration` | see §4 |
| `WindowLeft`/`WindowTop` | `WidgetWindow` | reset action re-centres |

**`ColorBarsByUsage`** is currently `QuotaRowViewModel.ColorBarsByUsage { get; }` — immutable, set
at construction. It stays immutable. `ProviderCardViewModel` gains a settable
`ColorBarsByUsage` whose setter rebuilds `Windows` from the retained `_snapshot` using the same
path `Apply` already uses; `MainViewModel` sets it on every card and then calls `Tick()`, which
repopulates the countdowns the rebuild cleared. Making the row's flag mutable instead would add
observable state to the one class that is currently a pure projection of a `QuotaWindow`.

**`ShowUnavailableProviders`** hides a card without removing it from `Providers`. A provider can
become `NotInstalled` or `Unsupported` *after* the first probe, so a filtered collection would have
to be recomputed on every snapshot, fighting the refresh loop for ownership of the collection. The
card exposes `IsHiddenByFilter` (state is `NotInstalled` or `Unsupported`, and the setting is off)
and the `ItemsControl.ItemContainerStyle` binds container `Visibility` to it. `MainViewModel.FooterText`
counts visible cards, not all cards.

## 2. Settings window

A `SettingsWindow` in `App/Views`, owned by the widget, single-instance (a second open focuses the
existing one), built from the existing `Tokens.xaml`/`Controls.xaml` so it themes with everything
else. Bound to a `SettingsViewModel` in `App/ViewModels` that reads `SettingsService.Current` and
calls `Update` on every change.

**Live apply. No OK/Cancel, no Apply button** — only Close. The widget is visible behind the
settings window, so the effect of a change is on screen as it is made; a commit step would add a
way to be wrong about whether a change had taken.

### Contents, against PRD §19

| §19 requirement | This increment |
|---|---|
| Start with Windows | checkbox → `StartupRegistration` (§4) |
| Always on top | checkbox |
| Light / dark / system theme | three-way selector |
| Color bars by usage | checkbox, on by default, labelled "Color bars by usage" (§16's one permitted en-US divergence) |
| Refresh behaviour | interval in seconds, clamped 15–3600 by `AppSettings.RefreshInterval` |
| Stale-data threshold | seconds, clamped 30–3600 by `AppSettings.StaleAfter` |
| Whether unavailable providers remain visible | checkbox |
| Window position and size reset | "Reset window position" button. Position only: the window is `SizeToContent="Height"` at a fixed width, so there is no user-set size to reset |
| Re-run provider discovery | "Re-check providers" button → forced refresh. Every probe re-detects installation and version on each call, so a forced refresh *is* rediscovery; a second mechanism would be a second name for the same thing |
| Open local logs | button → `explorer.exe` at `RollingFileLoggerProvider.DefaultDirectory` |
| Open diagnostics | **deferred** — there is no diagnostics window to open. The button is omitted, not disabled |
| Compact or expanded default mode | **deferred** — `AppSettings.Density` exists but nothing renders differently, so the toggle would be dead UI |

Numeric fields commit on lost focus and on Enter, not on every keystroke: a partially typed "3" in
a field on its way to "300" would otherwise be clamped to 15 and re-rendered under the cursor.

### Accessibility

Every control carries `AutomationProperties.Name`. The window is keyboard-reachable end to end
(this is the one increment where that is cheap to get right, since the widget itself is a single
scroll region). Verified in light, dark and high contrast.

## 3. Tray

### Mechanism

Hand-rolled `Shell_NotifyIcon` P/Invoke in `App/Interop/TrayIcon.cs`, alongside the existing
`DwmWindowChrome`.

Rejected alternatives: `System.Windows.Forms.NotifyIcon` needs `<UseWindowsForms>` and gives a
WinForms context menu that ignores all three palettes — unacceptable in an app whose high-contrast
support is a hard requirement. `Hardcodet.NotifyIcon.Wpf` solves the theming but buys a
third-party package for roughly 150 lines of well-documented interop, against a standing
"dependencies minimal and justified" constraint.

The context menu is therefore an ordinary WPF `ContextMenu`, themed by the existing dictionaries.

### Menu

Per PRD §17, minus the one entry with nothing behind it:

- **Open** — show and focus the widget
- **Refresh all providers** — `MainViewModel.RefreshAsync(force: true)`
- **Settings** — open or focus `SettingsWindow`
- **Exit** — the only way to end the process
- ~~Open diagnostics~~ — omitted, not disabled, for the same reason as in §2

Left-click opens/focuses. The icon's tooltip is the app name; it does **not** carry state text.

### Traps this must handle

- **`TaskbarCreated`.** Explorer restarts and every tray icon is dropped. Register the message with
  `RegisterWindowMessage("TaskbarCreated")` and re-add the icon when it arrives, or the widget
  becomes unreachable for the rest of the session with no way to exit but Task Manager.
- **No message-only window.** The icon's callback `hWnd` is the widget's own HWND, hooked through
  its `HwndSource`. A message-only window (`HWND_MESSAGE` parent) is invisible to `HWND_BROADCAST`,
  which §5 needs, and the widget's window already lives for the whole process now that `✕` hides
  rather than closes.

### Icon

New `App/Assets/app.ico` at 16/24/32/48/256, also wired as `<ApplicationIcon>` — which retires the
"no app icon" half of the release gap. The tray glyph is **static**. PRD §16.1 contemplates a tray
glyph carrying state with a shape overlay rather than colour; that is an icon-design problem and is
deferred rather than half-done.

## 4. Start with Windows

`Infrastructure/Startup/StartupRegistration.cs`:

```csharp
public sealed class StartupRegistration
{
    public StartupRegistration(string keyPath, string valueName, string? executablePath);

    public bool IsSupported { get; }   // false when executablePath is null
    public bool IsEnabled { get; }     // value exists AND equals executablePath
    public void Enable();              // write, overwriting whatever was there
    public void Disable();             // delete the value name, whatever it contained
}
```

`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `AiUsageMonitor`, data the quoted
`Environment.ProcessPath`. Per-user, no administrator rights, and removal is an exact value-name
delete — this never touches machine-wide or policy keys.

`IsEnabled` requires the stored path to equal the current executable. A user who moves or reinstalls
the app therefore sees the checkbox off, and turning it on rewrites the entry to the new location.
That is self-healing and needs no repair path; reporting "registered, but pointing somewhere else"
would be a third state the UI has to explain for no gain.

`IsSupported` is false when `Environment.ProcessPath` is null. The checkbox is then disabled with a
visible reason rather than silently doing nothing.

The key path is a constructor parameter so tests exercise the real registry code against a scratch
subkey under `HKCU` and delete it afterwards, instead of asserting against a mock of the API under
test.

## 5. Hide to tray, exit, and the second instance

### Hide

`✕` hides the window; the process keeps polling. The first time only, a tray balloon
(`NIF_INFO`) says where the widget went, gated by a new `TrayHintShown` bool in `AppSettings`.
The button's tooltip and `AutomationProperties.Name` become "Hide to tray" so the behaviour is
stated before it happens, not only after.

### Exit

`OnClosed` no longer runs on hide, so the shutdown work it owns — `SavePlacement`,
`_model.Dispose()`, timer stops, `ThemeManager.Changed` detach — moves into an explicit shutdown
path invoked by tray **Exit**, with `OnClosed` kept as a safety net for a real close (session
logoff). Both paths must be idempotent.

### Second instance

Autostart plus a hidden window makes double-launch reachable for the first time, and its failure
mode is bad: clicking the exe appears to do nothing at all.

A named mutex held for the process lifetime. The second instance broadcasts a registered window
message (`RegisterWindowMessage("AiUsageMonitor.Show")`) with `PostMessage(HWND_BROADCAST, …)` and
exits immediately; the first instance's widget receives it on its `HwndSource` hook and shows
itself. Same user, same integrity level, so no `ChangeWindowMessageFilterEx` is needed.

## 6. Testing

| Unit | How |
|---|---|
| `SettingsService` | update-persist-raise ordering; a failing store keeps the in-memory change; `Update` composes against current state, not a captured one |
| `StartupRegistration` | real registry, scratch subkey under HKCU, deleted in teardown; enable/disable/idempotence; stale path reads as disabled |
| `SettingsViewModel` | each control writes through exactly once; clamping; the reset and re-check actions call what they claim to |
| `ProviderCardViewModel.ColorBarsByUsage` | setter rebuilds rows from the retained snapshot and preserves order and labels |
| `MainViewModel` | `Changed` propagates freshness and colour; `FooterText` counts visible cards |
| `SettingsWindow` XAML | joins the existing loading tests across light, dark and high contrast |

Tray interop and the single-instance broadcast are not unit-testable and are verified by the
session owner, as the DWM chrome already is.

## 7. Out of scope

Named so they do not leak in:

- **Diagnostics window** (PRD §20) — its two entry points are omitted here, not stubbed.
- **Compact mode** rendering and its toggle.
- **State-carrying tray glyph** (PRD §16.1).
- **Release packaging, versioning and CI** — last, under semantic versioning.
- **Mica backdrop**, expanded single-provider card, provider plan metadata — unchanged from the
  live-widget increment's out-of-scope list.
