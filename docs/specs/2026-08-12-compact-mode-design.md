# Compact mode — design

**Status:** approved 2026-08-12.
**Implements:** PRD §17 (compact widget mode), §19 (compact-or-expanded default mode setting).
**Design source:** `docs/design/widget-states.dc.html` screen 2, `docs/design/ProviderCard.dc.html`
(`density` prop), `docs/design/QuotaRow.dc.html` (`density` prop), `docs/design/rationale.md`
("What compact sacrifices, in order"), `docs/design/tokens.md` §3.

## 1. Goal

One setting switches the widget between **Standard** and **Compact** density. Compact removes
addressing and padding, never data. Nothing that answers *"how much have I got left, and when does
it come back"* is cut.

This is a presentation-only change. It adds no provider call, no domain type, and no new persisted
value — `AppSettings.Density` and the `WidgetDensity { Normal, Compact }` enum already exist and are
currently consumed nowhere.

## 2. Scope

### In scope

Sacrifice levels 1–5 from `rationale.md`:

1. Per-card timestamp (the footer keeps one)
2. Version and plan
3. Provider monogram
4. The *Connected* status chip
5. Padding — window chrome, card, and row

Plus the settings control that selects density.

### Out of scope — deliberately deferred

**Sacrifice level 6, "rows beyond three per card collapse to *N more windows — expand*".** It is the
only level that removes data, which is why `rationale.md` puts it last, and the only one needing new
machinery: per-card expand/collapse state, an expand affordance, and a rule for whether an expansion
survives a refresh. Neither installed provider currently reports more than three windows in a card,
so it would ship as code that cannot be seen working. Revisit when a provider exceeds three windows.

Nothing else about compact is deferred.

## 3. The one deliberate deviation from the design render

**The `WINDOW / USED / RESETS IN` column captions stay in compact mode.**

`ProviderCard.dc.html` hides them when dense (`showColumns: … && !dense`). We do not, for the same
reason the captions were kept in the *default* widget during the live-widget increment
(`docs/plans/2026-08-11-live-widget.md`, Task 10): **PRD §16 requires the visible percentage text to
make its direction explicit** — "72% remaining" or "28% used". A bare `47%` under no caption does
not. Hiding the captions in compact would re-open in compact exactly the hole that decision closed
in standard.

Cost: roughly 13px per card, which compact does not recover.

Consequence for the height target: `tokens.md` §3 measures compact at **360×326**, and that
measurement assumes no captions. Our compact widget will be taller than 326 by roughly the caption
cost. **Implementation must measure and record the real height rather than assert 326.** The
governing constraint is the 520px ceiling from PRD §17, which compact stays far below either way.

This deviation must be recorded in the implementation plan and guarded by a test (§7), so that a
later tidy-up cannot silently remove the captions in compact and reintroduce the §16 violation.

## 4. What compact changes

### 4.1 Dimensions

| Element | Standard | Compact | Token pair |
|---|---|---|---|
| Title bar height | 32 | 28 | `WidgetTitleBarHeight` / `…Compact` — **both exist** |
| Footer height | 26 | 24 | `WidgetFooterHeight` / `…Compact` — **both exist** |
| Card padding | `11,8,11,9` | `9,6,9,7` | `ProviderCardPadding` / `…Compact` — **both exist** |
| Quota row padding | `0,6,0,5` | `0,4,0,4` | `QuotaRowPadding` / `…Compact` — **both exist** |
| Body padding | `10,0,10,2` | `8,0,8,2` | `WidgetBodyPadding` / `…Compact` — **new** |
| Card gap | `0,0,0,8` | `0,0,0,6` | `ProviderCardGap` / `…Compact` — **new** |

The body padding's bottom component stays `2` in both densities. It is not the design's body padding
value; the card gap supplies the rest. Standard: `2 + 8 = 10`. Compact: `2 + 6 = 8`. Both match the
design's body padding for their density. The existing comment in `WidgetWindow.xaml` explaining this
split must survive the move into tokens.

The two new token pairs replace inline literals in `WidgetWindow.xaml`. Their values are not new —
they are the values already there, plus the compact counterparts.

The widget's `Width` (360) and `MaxHeight` (520) are **unchanged** by density. The design draws both
densities at 360 wide, and the scroll ceiling is a property of the screen, not of the density.

### 4.2 Hidden in compact, unconditionally

- **The provider monogram** — the 16px chip in the card header. The name is beside it.
- **The version text** — `VersionText`, the card header's third column.

### 4.3 Hidden in compact, conditionally

**The status line** — the `StateChip` and the timestamp beside it — is hidden in compact **only when
the provider is Connected**. Every other state keeps both.

```
ShowStatusLine  =>  !IsCompact || State != ConnectionState.Connected
```

The design's own condition is `!(dense && state === "connected" && !stale)`. The trailing `&& !stale`
term is **redundant in our model and must not be transcribed**: the mockup carries `state` and
`stale` as independent props, whereas `ConnectionState.Stale` is one of the values `State` takes, so
`Connected` and `Stale` are already mutually exclusive here. `ProviderCardViewModel.IsStale` is
defined as `State == ConnectionState.Stale`.

This is the rule that makes compact safe: **silence means connected.** A card in Error, Stale,
Waiting, Unavailable, Unsupported or NotInstalled keeps its chip *and* its timestamp at compact
density, so compact never hides a problem — only the confirmation that there isn't one.

The timestamp needs no rule of its own. It lives inside the status line, so it follows it: absent
when compact and connected, present otherwise. That satisfies `rationale.md` level 1 (the footer
keeps the one remaining timestamp) while preserving "Last succeeded 12 minutes ago" on a failing
card, which is the whole question during an outage.

**When the status line is hidden, a 6px spacer takes its place** (the design's `denseSpacer`).
Without it the header sits directly on the caption row. The spacer appears exactly when the status
line does not:

```
ShowCompactSpacer  =>  !ShowStatusLine
```

### 4.4 Unchanged in compact

Everything not listed above, and specifically: the provider name, the tier badge, the stale banner,
the column captions (§3), every quota row, the bar, the elapsed marker, the percentage, the
countdown, the limit-reached indicator, the notice block, and the footer's contents.

## 5. Architecture

### 5.1 How density reaches the views

**A view-model flag, fanned out through the path the existing settings already take.**

`MainViewModel.ApplySettings(AppSettings)` already distributes `ColorBarsByUsage` and
`ShowUnavailableProviders` to every `ProviderCardViewModel`. Density joins them:

- `MainViewModel.IsCompact` — consumed by the window chrome (title bar, footer, body padding), whose
  `DataContext` is the `MainViewModel`.
- `ProviderCardViewModel.IsCompact` — consumed by the card, and by its rows.
- `QuotaRowViewModel` gains **nothing**. Its rows read the card's flag through
  `{Binding DataContext.IsCompact, RelativeSource={RelativeSource AncestorType=views:ProviderCardView}}`.

That last point is deliberate. `ProviderCardViewModel` documents `QuotaRowViewModel` as a pure
projection of one `QuotaWindow` with no observable state, and explains that `ColorBarsByUsage`
rebuilds the rows rather than mutating them precisely to keep it that way. Density is a presentation
concern that no row projection should carry, and an ancestor binding costs nothing at runtime.

A `QuotaRowView` measured with no `ProviderCardView` ancestor — which is how `ViewLoadingTests`
exercises it today — resolves that binding to nothing and keeps standard padding. Standard is the
correct fallback, and those existing tests keep passing unchanged.

#### Alternatives considered and rejected

**Overwrite the dimension tokens in `Application.Resources` when density changes.** Every
`DynamicResource` would return compact values with no binding anywhere. Rejected: it makes the XAML
lie — a token named `ProviderCardPadding` silently holding the compact value — and visibility still
needs a flag, so it adds a second mechanism instead of replacing one.

**A second card template selected by density.** Rejected: two XAML files to hold in step, and every
future card change would have to be made twice.

### 5.2 The precedence trap this work must avoid

WPF dependency-property precedence places a **local value above a style trigger**. A `DataTrigger`
that sets a property already assigned as an element attribute will parse, bind, build cleanly, and
**do nothing at runtime**, with no warning and no exception.

Today these are local attribute values on the elements compact needs to override:

| File | Element | Local value to move |
|---|---|---|
| `WidgetWindow.xaml` | title-bar `Grid` | `Height="32"` |
| `WidgetWindow.xaml` | footer `Border` | `Height="26"` |
| `WidgetWindow.xaml` | provider `ItemsControl` | `Margin="10,0,10,2"` |
| `ProviderCardView.xaml` | outer `Border` | `Padding="{DynamicResource ProviderCardPadding}"` |
| `QuotaRowView.xaml` | row `Border` | `Padding="{DynamicResource QuotaRowPadding}"` |

**Every one of these must move out of the attribute and into a `Style` `Setter`,** with the compact
value supplied by a `DataTrigger`. A `DynamicResource` is valid inside a `Setter` value, so the
tokens still resolve and still follow the theme.

The `ItemContainerStyle` card-gap `Margin` is already a `Setter` and needs only a trigger added. Its
`DataContext` is the `ProviderCardViewModel`, so it binds `IsCompact` directly.

This is the load-bearing mechanical step of the whole increment, and it is invisible to plan review:
the diff looks correct either way. It is only provable by measuring a rendered view (§7).

### 5.3 A test-only note about `DataTrigger.Value`

XAML leaves `DataTrigger.Value` as the **string** `"True"` — it cannot know the bound property's
type at parse time. WPF converts at evaluation, so the trigger fires correctly at runtime; only a
test that inspects the `Style` object sees a string. A test asserting on a trigger must compare
`trigger.Value.ToString()` against `"True"`, never `Equals(trigger.Value, true)`. This already cost
a debugging round in the settings-and-tray increment.

## 6. Settings

A **Density** radio pair in the settings window's `APPEARANCE` section, immediately after Theme:

- Label: `Density`
- Options: `Standard` (= `WidgetDensity.Normal`) and `Compact` (= `WidgetDensity.Compact`)
- Helper line: `Compact hides versions, the monogram and the connected chip`

The helper text is the design's own settings copy (`widget-states.dc.html`) and is accurate to what
this increment builds. It does not mention captions or row collapsing, neither of which compact does
here.

Built with the existing `ChoiceViewModel` and `SettingsRadioButtonStyle`, exactly as `Themes` is:
a `Densities` collection on `SettingsViewModel` reading `(int)_settings.Current.Density` and writing
`_settings.Update(s => s with { Density = (WidgetDensity)value })`, with its own `GroupName`.

`SettingsViewModel` refreshes every choice group when settings change from elsewhere; `Densities`
must be added to that refresh, alongside `Themes`, `RefreshIntervals` and `StaleThresholds`.
Otherwise the radio would not follow a density change made by any other surface.

Changing density takes effect immediately, like every other setting here. The window has
`SizeToContent="Height"`, so it re-measures itself; only `WindowLeft` and `WindowTop` are persisted,
so there is no stored height to conflict with the new one.

## 7. Verification

Presentation work in this repo is verified four ways, in increasing cost. Compact mode needs three
of them — nothing here is drawn by the shell, so no screen capture of a tray icon is involved.

### 7.1 `tests/AiUsageMonitor.App.Tests`

Measured assertions on real, arranged views. These must include:

- **Compact is shorter.** The same `ProviderCardViewModel` measured at both densities: the compact
  `DesiredSize.Height` is strictly less than the standard one. This is the assertion that fails if
  the §5.2 precedence trap is not handled — a trigger that silently does nothing produces two equal
  heights.
- **The monogram and version collapse** in compact and are visible in standard.
- **The status line collapses when Connected** in compact.
- **The status line survives a problem.** With `State` set to `Error`, and again to `Stale`, the chip
  is visible at compact density. This is the guarantee that compact never hides a fault.
- **The column captions survive compact.** Guards the §3 deviation against later tidying.
- **The window content shrinks.** The window's content measured at both densities, compact strictly
  shorter, and the real compact height recorded in the plan's completion notes.

### 7.2 Offscreen render harness

Both densities across all three palettes (Light, Dark, HighContrast), rendered to PNG and inspected.
Built as a scratchpad WPF exe with a `ProjectReference` to `AiUsageMonitor.App`, merging the theme
dictionaries in their `pack://application:,,,/AiUsageMonitor.App;component/Themes/{name}.xaml`
component form. Kept outside the repository. States worth rendering: connected, error, stale, and a
card with no windows.

### 7.3 Live launch

Flip the setting with the widget running, at both densities, and read the actual window height.
Report the measured compact height against the design's 326px, with the caption cost accounted for.

A running instance holds `bin/…/AiUsageMonitor.App.exe` and makes every build fail `MSB3026`; pass
`-p:BaseOutputPath=<scratch>/` to build and test around it rather than killing the user's window.

## 8. Constraints inherited

These bind this increment as they bind every other; none is relaxed by it.

- Warnings are errors. `dotnet build` must be clean.
- Windows-only, WPF, MVVM, `net10.0-windows`. No new `PackageReference`.
- No provider-specific branching in shared or UI code (PRD §21).
- Missing data is `null` and surfaces as `Waiting`/`Unavailable`, never as `0`.
- Every mechanism keeps its visible tier. The tier badge is not a compact sacrifice.
- No credential is logged, persisted, displayed, or copied.
- No hardcoded user paths.
- Colour never carries a state by itself, at either density.

## 9. Acceptance

- [ ] A `Density` radio pair in settings switches the widget between Standard and Compact, and the
      change is visible immediately and persists across a restart.
- [ ] At compact density: monogram and version are gone; padding is tighter at window, card and row;
      title bar is 28 and footer 24.
- [ ] At compact density a **Connected** card shows no status chip and no timestamp.
- [ ] At compact density an **Error** or **Stale** card shows both.
- [ ] Column captions are present at both densities.
- [ ] Every quota window is still drawn at both densities; no row is collapsed or truncated.
- [ ] The measured compact window height is recorded, and is below the 520px ceiling.
- [ ] `dotnet build` clean; `dotnet test` green with the new tests from §7.1.
- [ ] All three palettes inspected as rendered PNGs at both densities.
