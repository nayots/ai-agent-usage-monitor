# Feature inventory and ideas

**Status:** open — §3 awaiting Codex, §4 awaiting a decision from Stoyan.
**Written:** 2026-08-13, against commit `4ef5f12`.

## Why this document exists

Two questions, one file. **What does this app actually do today?** — §1, written from the
code rather than from the PRD, so it records what shipped rather than what was specified.
**What should it do next?** — §2 and §3, filled independently by Claude and by Codex so the
same blind spot is less likely to appear in both.

§4 is where the verdicts go. Nothing in §2 or §3 is a commitment; they are candidates, and
an idea sitting here unimplemented is not a defect.

`docs/PRD.md` remains authoritative. Where an idea here contradicts it, the PRD wins until
the PRD is changed on purpose.

---

## 1. What the app does today

### 1.1 Providers and data

| Feature | Behaviour | Where |
|---|---|---|
| Claude Code provider | Reads the local OAuth token, calls Anthropic's own usage endpoint over TLS. **Unofficial**, labelled as such everywhere. | `Providers/Claude/ClaudeOAuthUsageProbe.cs` |
| Codex provider | Launches the vendored `codex.exe app-server`, speaks newline-delimited JSON-RPC over stdio, calls `account/rateLimits/read`. **Official**. | `Providers/Codex/CodexProbe.cs` |
| Executable discovery | Per-user paths resolved at runtime — no hardcoded user paths. Claude: `~/.local/bin`, npm shim, then PATH (`.exe` and `.cmd`). Codex: vendored path, then a glob, then PATH. | `ClaudeExecutableLocator.cs`, `CodexProbe.DiscoverExecutable` |
| Version reporting | `--version` per probe, prefixed with `v` only when it starts with a digit, so `codex-cli 0.144.6` is not rendered as `vcodex-cli …`. | `ProviderCardViewModel.FormatVersion` |
| Duck-typed quota extraction | Walks arbitrary JSON; any object carrying **both** a percent-ish and a reset-ish key becomes a window. Handles both Claude dialects and Codex's with no per-provider parser. | `Domain/DuckTypedQuotaExtractor.cs` |
| Generic window model | `Id, Label, UsedPercent, ResetsAt, WindowDuration, Order, IsPartial, Extra, LabelIsProviderToken`. No property is named after a plan period. | `Domain/QuotaWindow.cs` |
| Derived values | `RemainingPercent` (clamped 0–100), `TimeUntilReset` (clamped at zero), `ElapsedFraction` (only when duration *and* reset are both known). | `Domain/QuotaWindow.cs` |
| Provider order preserved | Windows render in the order the provider reported them, never re-sorted by duration or countdown — a 7-day window was observed resetting before a 5-hour one. | `Domain/QuotaOrdering.cs` |
| Unresolved labels | A window whose name resolves to no duration keeps the provider's raw token, rendered in a monospace chip so it is never mistaken for app-authored copy. | `QuotaRowViewModel.IsProviderToken` |
| Eight connection states | `NotInstalled, Discovering, Waiting, Connected, Stale, Unavailable, Unsupported, Error` — each with a word, never colour alone. | `Domain/ConnectionState.cs`, `ConnectionStateText.cs` |
| Tier badge | Official / Unofficial shown per card; unofficial carries a distinct dashed frame and mark. | `Controls/TierBadge.cs` |
| Missing data | Always `null`, surfacing as `Waiting`/`Unavailable` — never `0`, never a placeholder. | throughout |

### 1.2 Refresh, freshness and reliability

| Feature | Behaviour | Where |
|---|---|---|
| Polling | Every N seconds, default 60, clamped 15–3600. Providers probed concurrently; each card updates the moment its own provider answers. | `ProviderRefreshService`, `WidgetWindow._poll` |
| Hard timeout | 30 s per probe, enforced by **racing** the probe against the token rather than awaiting it — a probe that ignores cancellation cannot hang the cycle. | `ProviderRefreshService.RefreshAsync` |
| Backoff | Doubling per consecutive failure, capped at 8× the base interval; reset on success. `NotInstalled`/`Unsupported` are settled facts, not failures, and are never backed off. | `ProviderRefreshService.BackoffFor` |
| Manual refresh | Four entry points: footer **Refresh**, tray **Refresh all providers**, settings **Re-check providers**, and per-card **Retry now**. All ignore backoff. | `MainViewModel`, `WidgetWindow`, `ProviderNotice` |
| Failure isolation | A throwing probe yields an `Error` snapshot carrying the exception **type name only**; the full exception goes to the log. A throwing event subscriber is caught too. | `ProviderRefreshService` |
| Staleness | Threshold default 300 s, clamped 30–3600, re-evaluated every tick against the clock — so a card can go `Stale` with no new snapshot arriving (sleep/resume, backoff, a tight threshold under a slow interval). | `Domain/Freshness.cs`, `ProviderCardViewModel.Tick` |
| Single instance | Session-local mutex; a second launch broadcasts a registered window message asking the first to show itself, then exits. | `Interop/SingleInstance.cs` |
| Crash containment | Dispatcher exceptions logged and handled; domain exceptions logged as critical. A failure to create the main window is fatal by design, so no invisible process is left running. | `App.xaml.cs` |
| Corrupt settings | An unreadable settings file is moved aside with a timestamped `.corrupt` suffix and defaults are used — a bad file never blocks startup. | `AppSettingsStore.Load` |

### 1.3 The widget

| Feature | Behaviour | Where |
|---|---|---|
| Frameless window | 360 wide, height from content capped at 520, rounded corners and themed title bar via DWM, absent from the taskbar. | `WidgetWindow.xaml`, `Interop/DwmWindowChrome.cs` |
| Custom title bar | Drag from anywhere on it; pin, settings and close buttons. No minimise — with no taskbar button it could only duplicate close. | `WidgetWindow.xaml` |
| Pinning | Keeps the widget above other windows **and** exempts it from focus-loss dismissal. Session-only: deliberately not persisted. | `AppSettings.AlwaysOnTop` (`[JsonIgnore]`) |
| Focus-loss dismissal | A session-wide foreground hook plus a 150 ms debounce hides the widget and closes settings once focus leaves the process entirely — by process id, so tray menus and tooltips don't count as "elsewhere". | `WidgetWindow.WatchTheForeground` |
| Close means hide | Alt+F4, the system menu and the close button all hide to the tray. Only the tray's **Exit** ends the process. | `WidgetWindow.OnClosing` |
| Provider card | Monogram, name, version, tier badge, state chip with its word, and one time line — either `Updated {age}` or, while failing, `Last succeeded {age}`. | `Views/ProviderCardView.xaml` |
| Quota row | Label, used %, bar, countdown to reset, elapsed-time marker, and an accessible name naming all three. | `Views/QuotaRowView.xaml` |
| Bar tone | Colour by usage band (toggleable); 100% renders exhausted regardless; stale bars grey out. | `Theming/QuotaBarFillSelector.cs` |
| Empty-state notices | A written notice per state — not installed, unsupported, waiting, unavailable, error, connected-with-no-windows — with an action button only where one can actually do something. | `ViewModels/ProviderNotice.cs` |
| Compact density | Drops versions, monogram and the connected chip, and tightens every chrome dimension. | `AppSettings.Density`, `Themes/Tokens.xaml` |
| Themes | Light, Dark and High-contrast dictionaries; `System` follows the OS live, including a mid-session switch and the high-contrast accessibility flag. | `Theming/ThemeManager.cs`, `ThemeResolver.cs` |
| Placement | Position persisted between runs; a position on a monitor that has since been unplugged falls back to centring; **Reset window position** in settings. | `WidgetWindow.RestorePlacement` |
| High DPI | `PerMonitorV2` in the manifest; the settings window measures its cap against the work area of the monitor it opens on, converted to DIPs. | `app.manifest`, `Interop/ScreenBounds.cs` |

### 1.4 Notification area

| Feature | Behaviour | Where |
|---|---|---|
| Live glyph | The widget in 16 px: one bar per window across every visible card, the worst reading among **primary** windows as two digits, and an error or alert overlay. Redrawn only when the state actually differs. | `TrayGlyphState.cs`, `Interop/TrayGlyphRenderer.cs` |
| Palette follows the taskbar | Not the app theme — the glyph has to be legible against whatever the taskbar is. | `TrayGlyphPalette` |
| Context menu | Open, Refresh all providers, Settings, Exit. | `WidgetWindow.xaml` |
| First-hide hint | One balloon, once ever, saying where the window went. | `WidgetWindow.HideToTray` |

### 1.5 Quota notifications

| Feature | Behaviour | Where |
|---|---|---|
| Milestone ladder | 10–80 every ten points, then 85, 90, 95, 100 — tighter where the consequences are. | `Domain/QuotaMilestones.cs` |
| Alert kinds | Milestone, LimitReached, Recovered, ProviderFailed, ProviderRecovered. Only **LimitReached** makes a sound. | `Notifications/UsageAlert.cs` |
| Edge-triggered | Observed on every tick even when delivery is switched off, so turning notifications back on cannot release a burst of crossings the user already lived through. | `WidgetWindow.DeliverAlerts` |
| Never leaks a reason | An alert says a provider stopped reporting and nothing else; the reason stays on the card, behind a deliberate look. | `UsageAlert` doc comment |

### 1.6 Settings, logging, privacy, build

| Feature | Behaviour | Where |
|---|---|---|
| Settings file | `%APPDATA%\AiUsageMonitor\settings.json`, plain numbers for durations so it stays hand-editable. | `AppSettingsStore` |
| Live apply | No OK/Cancel/Apply — every change is already on screen behind the window. | `SettingsWindow.xaml.cs` |
| Offered settings | Theme, density, colour bars by usage, show uninstalled providers, start with Windows, quota notifications, refresh interval, stale threshold, plus three actions (re-check, reset position, open logs). | `SettingsViewModel` |
| Hand-edited values survive | A duration not in the preset list is added to the list rather than silently replaced. | `SettingsViewModel.Durations` |
| Start with Windows | A per-user `HKCU\…\Run` entry pointing at the resolved executable path; reads back from the registry, not the settings file, so a copied settings file cannot lie. Disabled with a reason when the process cannot name itself. | `Interop/StartupRegistration.cs` |
| Logging | Rolling file log at `%LOCALAPPDATA%\AiUsageMonitor\logs`, 1 MB × 5 files, whole lines only. | `Logging/RollingFileWriter.cs` |
| Credential handling | Token read into one local, used once for one header, never logged, persisted, cached, displayed or placed in `Notes`/`Extra`. Redirects disabled; one hardcoded host. | `ClaudeOAuthUsageProbe` |
| No elevation | Nothing requires administrator rights; nothing machine-wide is written. | `app.manifest`, `StartupRegistration` |
| Tests | 391 across three suites (100 domain / 111 infrastructure / 180 app), warnings as errors. | `tests/` |
| Packaging | `build/publish.ps1` produces one self-contained ~65 MB `.exe`. Never trimmed — WPF hard-errors. | `build/publish.ps1` |

### 1.7 Known gaps in what ships today

Not proposals — simply things the PRD asks for that are not built yet.

| Gap | Reference |
|---|---|
| No diagnostics view | PRD §20 |
| No "reset application settings" action | PRD §19 |
| No release process, no version anywhere in the UI, no tagged artifact | PRD §27 |
| **No `README.md` at all** — the repo has none | PRD §27 |
| No historical data of any kind; every value is instantaneous | PRD §28 |

---

## 2. Claude's proposals

Sized **S** (a sitting), **M** (a plan and a few tasks), **L** (its own increment).
"PRD" names an existing requirement; "new" means the PRD does not ask for it.

### 2.1 Improvements to what exists

| # | Proposal | Why | Size | PRD |
|---|---|---|---|---|
| C1 | **Stop spawning `--version` on every poll.** Both probes launch the provider executable for its version on each cycle — four process launches a minute, forever, for a string that changes on upgrade. Cache it against the executable path plus its last-write time. | Idle cost is the whole budget for a widget that is hidden most of the time. It is also the slowest part of the Claude probe. | S | §22 |
| C2 | **Say when the next check is, and show when one is being skipped.** A provider in backoff is silently not polled for up to 8× the interval while the card ages. `Updated 14m ago` under a 60 s interval currently reads as a bug rather than as deliberate restraint. | The behaviour is right; only its silence is wrong. | S | §24 |
| C3 | **Slow the clock when nothing is watching.** The 1 s tick and the full-rate poll both run while the widget is hidden, the session is locked, and the machine is on battery. Tick at 1 s only when visible; poll less often when hidden. | PRD §4.6 asks for calm desktop behaviour, and this is the one place the app is not calm. | M | §4.6 |
| C4 | **Reconcile the two duration settings.** Nothing stops a 60 s stale threshold under a 300 s refresh interval, which parks every card permanently in `Stale`. Either derive a floor from the interval or say so in the settings window. | A user can silently configure the app into a state where it always looks broken. | S | §19 |
| C5 | **Surface `Extra` and the mechanism per window.** The extractor discovers fields it does not model and keeps them in `Extra`, where nothing ever reads them. An expander per row — identifier, mechanism, raw keys — would make discovery inspectable. | It is the evidence that the generic model is working, currently invisible. Overlaps C10. | M | §13 |
| C6 | **Shorten error copy, keep the detail one click away.** `HTTP request failed: {ex.Message}` lands verbatim in the notice body and can run to a paragraph. A one-line cause with a **Details** affordance reads better and bounds the blast radius of text this app does not author. | Adapter error strings are already UI copy; they should be held to UI length too. | S | §10 |
| C7 | **Coalesce a burst of alerts.** One window produces one alert, correctly — but a resume from sleep can cross rungs on several windows across both providers in a single tick and stack four balloons. Merge same-tick alerts into one. | The one case where the notification design gets loud. | S | §16.2 |
| C8 | **Three digits in the tray.** At 100% the glyph shows *no* number, which is visually identical to "no data" — the alert overlay is the only difference. A full bar plus a distinct treatment would be unambiguous. | The state that matters most is the one the glyph says least about. | S | §15 |
| C9 | **A keyboard path to the widget.** No global hotkey, and no documented tab order through the cards. A hidden widget is reachable only by mouse. | Accessibility is in the definition of done; this is the largest remaining hole. | M | §27 |

### 2.2 New features

| # | Proposal | Why | Size | PRD |
|---|---|---|---|---|
| C10 | **Diagnostics view.** Per provider: mechanism string, tier, executable path, version, last error, the probe's `Notes`, and an explicit statement of what is redacted. Plus a **copy redacted bundle** action. | Already required, and it is what makes an unofficial mechanism defensible when it breaks. The `Notes` lists are fully populated and read by nothing. | M | §20 |
| C11 | **Local history and burn rate.** Sample each window locally, then show a sparkline and the one derived number that changes behaviour: *at this rate you reach 100% at 16:40*. Local file, no transmission. | The highest-value thing available without any new provider mechanism. A quota widget that cannot say "you are burning this faster than usual" is only a gauge. | L | §28 |
| C12 | **User-set thresholds.** Replace the fixed ladder with chosen ones — "tell me at 75 and 90, nothing else". | The ladder is a good default and a poor mandate; twelve rungs is a lot of balloons for a heavy user. | M | §28 |
| C13 | **Quiet hours.** Suppress non-critical alerts on a schedule; `LimitReached` still lands. | Cheap, and the obvious complement to C12. | S | new |
| C14 | **Provider ordering and per-provider visibility.** Drag to reorder; hide a provider outright. | Asked for, and it changes the tray glyph too — the digits come from the first visible provider's primary window. | S | §28 |
| C15 | **Per-provider refresh intervals.** Codex is a local process; Claude is a network call against an undocumented endpoint. One interval for both is the wrong shape. | Also the polite thing to do to an unofficial endpoint. | M | §28 |
| C16 | **A third provider.** Gemini CLI, Copilot, or whatever else is installed. The registry is a hardcoded two-element list. | The strongest possible test of "the domain model is provider-neutral" is a third provider that was not in mind when it was written. | L | §28 |
| C17 | **Release, versioning and a README.** Semantic version stamped into the assembly and shown in diagnostics, a tagged GitHub release carrying the self-contained `.exe`, and a README covering setup, the unofficial-mechanism caveat, and privacy behaviour. | The repo currently has no README and no way for anyone else to install this. Already agreed as the last increment. | M | §27 |
| C18 | **Opt-in update check.** A version check against the release feed. **Flagged, not recommended as-is:** it is an outbound call to a third party, which §23 forbids by default — it would need to be off by default, explicitly consented, and separately labelled. | Worth a decision rather than a silent omission. | M | §28 |
| C19 | **Edge-docked mini mode.** A one-line strip pinned to a screen edge: monogram, bar, percent, nothing else. | Compact density is smaller; this is *different* — the always-visible form the tray glyph currently approximates in 16 px. | L | §28 |

### 2.3 Deliberately not proposed

Settled decisions, recorded here so a fresh reader does not spend a suggestion re-opening them.

- **statusLine as a Claude Code source** — evaluated and rejected (push-only, session-only, requires editing the user's own config). PRD §11.1.
- **Any scraping, browser automation, cookie or browser-profile access** — PRD §23.
- **Telemetry or analytics of any kind**, including anonymous crash reporting.
- **Storing, caching or refreshing a provider credential** — token lifecycle stays the provider's.
- **Modifying provider configuration** without approval, preview, backup and restore — PRD §11.
- **Anything needing administrator rights.**
- **`PublishTrimmed`** — WPF hard-errors, NETSDK1168.
- **Domain properties named after plan periods** (`FiveHourQuota`, `WeeklyQuota`) — windows are discovered, never assumed.

---

## 3. Codex's proposals

> **Codex: this section is yours.** Read §1 and §2 first, then fill this section in.
>
> - Add your own **improvements to existing features** and **new features**, using the same
>   table shape as §2.1 and §2.2, numbered `X1, X2, …` so the two lists never collide.
> - Where you have read something in the code that §1 gets wrong or misses, say so in §3.3
>   rather than editing §1 — the point is two independent readings, not one merged one.
> - Duplicating a §2 idea is fine and useful: say so explicitly and add what Claude missed.
>   Independent agreement is a signal.
> - Honour §2.3. An idea that reopens a settled decision needs a new argument, not a fresh
>   proposal.
> - Ground each proposal in a file or a behaviour, not in what a widget generally ought to do.

### 3.1 Improvements to what exists

| # | Proposal | Why | Size | PRD |
|---|---|---|---|---|
| X1 | **Keep the last safe rows through a transient failure, then age them honestly.** `ProviderCardViewModel.Apply` calls `RebuildWindows` for every snapshot, and both probes return an empty `Windows` list on failure, so one dropped request immediately erases the last valid reading. Retain the last successful rows behind the `Error`/`Unavailable` notice, mark them stale, and clear them only at an explicit expiry defined by the stale-data policy or by user action. | The PRD explicitly says not to replace stale data with empty data and to preserve a valid last-known snapshot. Remembering only `_lastSuccessAt` preserves the timestamp but throws away the useful part. | M | §14, §18, §24 |
| X2 | **Make refresh single-flight per provider, with a latest-attempt-wins rule.** The poll timer, footer/tray/settings refresh, and per-card retry all start tasks independently (`WidgetWindow`, `MainViewModel.RefreshAsync`, `RetryOne`). An older slow attempt can therefore finish after a newer manual attempt and overwrite both the card and backoff state. Coalesce equivalent requests or attach monotonically increasing attempt ids and discard superseded completions. | This is a correctness issue, not only an efficiency one: the visible snapshot and failure count can move backward in time. It also prevents abandoned timeout tasks from accumulating repeated work. | M | §24 |
| X3 | **Keep provider identity and mechanism metadata stable on refresh-service failures.** `ProviderRefreshService.Failed` hardcodes `Installed: true`, `Mechanism: "unknown"`, and `Tier: Unofficial`; a service-level timeout of the official Codex probe therefore changes the badge to Unofficial. Put stable tier/mechanism/discovery facts on `ProviderDescriptor`, or combine a failure state with the last safe metadata. | The tier is a property of the mechanism, not of whether its latest call succeeded. Failure is precisely when trustworthy diagnostics matter most. | S | §4.1.1, §20 |
| X4 | **Render the partial-data flag and the exact reset instant.** `QuotaWindow.IsPartial` and `ResetsAt` are populated, but `QuotaRowViewModel` exposes neither; `QuotaRowView.xaml` uses its tooltip only for the identifier. Add a written partial indicator and a localized exact reset timestamp in the accessible tooltip/expanded detail, while continuing to omit missing values. | These are already required row semantics, and they tell the user whether a blank marker/countdown is absence or a rendering bug. | S | §15 |
| X5 | **Sanitize and bound every provider-authored error before it reaches the card.** This overlaps C6, but the boundary is wider than Claude HTTP copy: `CodexProbe` returns `ex.Message`, and its protocol-error path embeds `errorEl.GetRawText()`. Map known failures to short app-authored codes/copy, keep only redacted detail in local logs, and never render a raw protocol error object. | Provider text is unbounded and may contain paths, payload fragments, or other data inappropriate for an always-visible card. Shortening only the current Claude message misses the more dangerous path. | S | §23 |
| X6 | **Add contract tests around the two real adapters, not only their shared helpers.** No test references `ClaudeOAuthUsageProbe` or `CodexProbe` directly; the 391 tests cover the extractor, locators, registry, refresh service, and UI projections. Introduce injectable HTTP/process boundaries and fixtures for absent installs, malformed JSON/JSONL, interleaved notifications, error redaction, cancellation, version changes, and unknown/partial windows. | The code most exposed to provider drift is currently the code least exercised. `ProviderRegistryTests` proving two names and tiers does not prove either integration contract. | M | §25 |
| X7 | **Recover placement against real monitor work areas and topology changes.** `WidgetWindow.RestorePlacement` tests a saved point against the rectangular `VirtualScreen` bounds using a synthetic 100-pixel height. That admits gaps between monitors, a barely visible sliver, and coordinates interpreted under a changed DPI. Resolve the actual monitor, clamp the whole current window to its work area, and re-evaluate on display/DPI changes. | The current gross-offscreen fallback handles `-30000,-30000`, but not the ordinary ways a docked laptop's topology and scaling change. | M | §17, §24 |
| X8 | **Replace the unconditional one-second hidden tick with deadline- and lifecycle-aware work.** This overlaps C3, but hidden is the app's normal tray mode, so its provider polling should remain truthful. Stop recomputing visible countdown strings while hidden; schedule only the next staleness/alert-relevant boundary, then tick and force a refresh explicitly on resume or network recovery. Keep any battery/lock polling reduction visible in freshness and “next check” copy. | `WidgetWindow._tick` walks every card and checks the taskbar palette every second forever, while there is no power/session/network lifecycle hook. This removes idle churn without weakening the tray icon and notifications that justify background operation. | M | §4.6, §24 |
| X9 | **Tell the user when a settings change is session-only.** `SettingsService.Update` applies a change, catches save failures, and writes only a warning to the log. Surface a non-modal persistence status in Settings with retry/open-folder help; also include it in diagnostics. | Live apply currently looks successful even when the next launch will revert it. The log is discoverable only after the user already knows to suspect persistence. | S | §19, §24 |

### 3.2 New features

| # | Proposal | Why | Size | PRD |
|---|---|---|---|---|
| X10 | **Build diagnostics around attempts, not just the latest snapshot.** This agrees with C10. In addition to `Notes`, mechanism, path, and version, expose last attempt, last success, next eligible attempt/backoff reason, in-flight/timeout state, whether displayed rows come from a retained snapshot, settings/log health, and a redacted copy bundle. | Those fields answer the failure modes found in `ProviderRefreshService` and `ProviderCardViewModel`; a dump of the current snapshot alone cannot explain “why has nothing happened for 14 minutes?” | M | §20 |
| X11 | **Add a scoped “Reset application data” flow.** Preview separate choices for preferences/window state, retained snapshots/history, and logs; default to application settings only, and state explicitly that provider configuration and credentials are untouched. | The current Settings actions reset only window position. §19 requires application reset, while §23 requires stored usage to be removable and makes the ownership boundary especially important. | S | §19, §23 |
| X12 | **Give the zero-provider case one useful home state.** `ProviderRegistry` always creates two cards, and turning off `ShowUnavailableProviders` can leave a widget whose body is empty and footer says `0 providers`. Replace that composition with “No supported providers found,” a single re-check action, the categories of discovery locations checked, and a privacy/tier explanation. | A clean machine is a supported configuration, not two errors or an unexplained blank widget. The aggregated state also avoids repeating the same installation guidance per card. | S | §10, §15 |
| X13 | **Restore a redacted last-safe snapshot after restart.** Persist only allowlisted quota values/timestamps and stable mechanism metadata, never `Notes`, raw errors, credentials, provider configuration, or unreviewed `Extra`; load it as visibly stale until a live refresh succeeds, with clearing wired to X11. | Today all useful state is process-local, so a network outage or provider startup failure after reboot has less information than the previous run already verified. This is narrower than full history and directly supports unexpected-shutdown recovery. | M | §23, §24 |
| X14 | **Add local history without an exhaustion forecast.** This overlaps C11 on local sampling and sparklines, but stop at factual past readings: trend lines, min/max, change since the previous sample, retention controls, and gaps for missing/stale data. Do not show “you will reach 100% at …” unless §16 is deliberately changed. | Historical context is valuable, but the current PRD explicitly forbids projected exhaustion or an implied safe rate. A provider-neutral time series can add context without making that claim. | L | §28 |
| X15 | **Resource and pseudo-localize all user-facing copy before adding a real locale.** State labels live in `ConnectionStateText`, notices in `ProviderNotice`, alert copy in `UsageAlertWatcher`, and much more is embedded in XAML; tests also assert English strings directly. Move copy behind resources, test long/pseudo-localized labels at compact width, then add a locale only once layouts and automation names survive. | Localization is listed as future work, but the current distribution of strings makes it an architectural increment rather than a translation file. Pseudo-localization will also expose truncation and accessible-name assumptions now. | L | §28 |

### 3.3 Where Codex reads the code differently

| # | Proposal | Why | Size | PRD |
|---|---|---|---|---|
| X16 | **§1.2 omits that a failed refresh discards the last valid rows.** The timestamp survives in `_lastSuccessAt`, but `ProviderCardViewModel.RebuildWindows` replaces the window collection with the failure snapshot's empty list. X1 is the corresponding correction. | This is material because §14/§24 require preservation and because the UI copy “Last succeeded” can suggest retained data that is no longer present. | M | §14, §24 |
| X17 | **§1.1/§1.2 overstate tier stability on failures.** Probe-authored Codex snapshots remain Official, but a timeout or exception synthesized by `ProviderRefreshService.Failed` is always Unofficial with mechanism `unknown`. X3 fixes that exception. | “Official / Unofficial shown per card” is true only while the adapter itself returns the snapshot. | S | §4.1.1 |
| X18 | **§1.3 overstates unplugged-monitor recovery.** The implementation checks the bounding `SystemParameters.VirtualScreen`, not the set of monitor work areas, and considers any intersection with a 100-pixel-high placeholder sufficient. X7 covers the remaining topology/DPI cases. | The existing test proves only a far-off coordinate. It does not prove a virtual-desktop gap, a sliver, taskbar avoidance, or a scaling change. | M | §17, §24 |
| X19 | **§1.6's test count hides a provider-specific coverage gap.** There are many load-bearing tests, but none directly exercises either complete provider probe; X6 is not a request for more count, but for coverage at the drift boundary. | The PRD separately requires provider-specific integration behavior, which helper tests cannot establish. | M | §25 |
| X20 | **C1 has the right idea but the source-level process count is three per full poll, not four.** Claude launches `--version` and then makes HTTP; Codex launches `--version` and `app-server`. Cache/invalidation is still worthwhile, and independent agreement on C1 remains. | The sizing and idle-cost argument stand; only the stated evidence should be precise. | S | §22 |
| X21 | **C3 treats hidden as idle, but hidden-to-tray is the primary operating mode.** Slowing the one-second presentation tick is safe; silently slowing provider polls would make the live glyph and quota notifications less live. X8 narrows the optimization and requires any cadence change to be visible. | Background polling is not incidental here: §16.2 exists specifically because the widget spends most of its life hidden. | M | §4.6, §16.2 |
| X22 | **C11's projected exhaustion sentence conflicts with §16, and C14 misstates tray selection.** §16 says the UI must not claim when quota will run out; `TrayGlyphState.From` chooses the highest reading among every visible provider's primary window, not the first visible provider's primary window. X14 keeps the useful history half of C11. | Both proposals can survive, but their decision record should be based on the actual contract and selection rule. | L | §16, §28 |
| X23 | **§1.1 makes the Codex PATH fallback sound equivalent to the vendored-exe paths, but it returns `codex.cmd`.** `DiscoverExecutable` then passes that shim to process launches configured for direct execution with `UseShellExecute = false`, while `CLAUDE.md` explicitly says to launch the vendored executable and never the npm shim. Resolve the real executable behind a PATH installation or report the limitation, and cover it in X6. | The normal vendored path can hide a broken fallback indefinitely; there is currently no adapter test for this branch. | S | §24, §25 |

---

## 4. Decisions

For Stoyan. Nothing above is scheduled until it appears here.

| Idea | Source | Verdict | Notes |
|---|---|---|---|
| | | | |
