# Settings shell — one window, sidebar navigation

Date: 2026-08-13
Status: approved, not yet planned
Spec authority: `docs/PRD.md` §17 (window behaviour and tray menu), §19 (settings), §20 (diagnostics), §21 (layering)

## Why this increment

Both configuration surfaces are a single `StackPanel` inside a `ScrollViewer`, and both have grown
past the screen. Settings, at 380px wide, now runs long enough to hit the work-area cap and show its
fallback scrollbar on a 1440px-tall display — the case `SettingsWindow.xaml` describes as the one
that should not be the normal one. It takes a size-to-content pass, a work-area cap and an
on-screen clamp to keep it from hanging off the bottom of the display. Diagnostics has the same
shape and grows by a full section per provider.

Nothing is wrong with the visual language — the tokens, the chip radio buttons, the section
captions and the tier badge all stay exactly as they are. What is wrong is containment. This
increment replaces the container, not the design: a category sidebar with one page in view at a
time, the format Obsidian, VS Code and Windows Settings all use for the same reason.

Not here: any restyling, any new setting, any change to what the settings mean.

## 1. The shape

One window. `Grid`, two columns:

```
┌────────────────┬─────────────────────────────┐
│ Settings       │  Theme                      │
│   Appearance   │  ┌────────┐┌─────┐┌────┐    │
│   Window       │  │ System ││Light││Dark│    │
│   Providers    │  └────────┘└─────┘└────┘    │
│   Notifications│                             │
│   Refresh      │  Density                    │
│                │  Compact hides versions…    │
│ Diagnostics    │  ┌──────────┐┌────────┐     │
│   Claude Code  │  │ Standard ││Compact │     │
│   Codex        │  └──────────┘└────────┘     │
│   Application  │                             │
└────────────────┴─────────────────────────────┘
   176px fixed              takes the rest
```

The sidebar is a `ListBox`, so arrow keys move between pages without any key handling of our own,
and each entry carries an `AutomationProperties.Name`. Only the content pane scrolls; the sidebar
gets its own `VerticalScrollBarVisibility="Auto"` for the day a machine has enough providers to
overflow it.

`DiagnosticsWindow` is deleted. Diagnostics becomes a group of pages inside this window.

## 2. Page inventory

The **Settings** group is fixed. The **Diagnostics** group is generated: one page per entry in
`DiagnosticsViewModel.Sections`, which is one per provider plus Application.

| Group | Page | Contents |
|---|---|---|
| Settings | Appearance | Theme · Density · Mini mode + Dock · Color bars by usage |
| | Window | Pinning explainer · Show providers that are not installed · Start with Windows (+ unavailable reason) · Ctrl+Alt+Q (+ warning) · **Reset window position** |
| | Providers | Provider hint · per-provider Show / reorder / interval · **Re-check providers** |
| | Notifications | Notify toggle · milestone thresholds · quiet hours |
| | Refresh | Check providers every · Call values stale after (+ warning) |
| Diagnostics | Claude Code | its `DiagnosticSection` · redaction hint · **Copy all diagnostics** |
| | Codex | ditto |
| | Application | version / runtime / OS / theme / scaling / logging / startup / privileges · **Open logs folder** · **Copy all diagnostics** |

The ACTIONS section does not move — it dissolves. Each of its four buttons goes to the page it acts
on, which is why there is no "Actions" entry in the sidebar. "Open diagnostics" disappears entirely:
it is now navigation.

`Copy all diagnostics` is named for what it does. It copies the whole redacted bundle, every
section, not the page it sits on — so it appears on each diagnostics page, where someone reading an
error will reach for it.

The `HasPersistenceWarning` banner moves out of the Appearance content and above the content pane,
spanning that column on every page. "Your settings cannot be saved" is not an appearance fact.

There is no About page. The Application diagnostics page already is one.

## 3. Window behaviour

| | Today | After |
|---|---|---|
| Size | `Width=380`, `SizeToContent="Height"` | `740×560` default, `MinWidth=620`, `MinHeight=440` |
| Resize | `ResizeMode="NoResize"` | `ResizeMode="CanResize"` |
| Remembered | no | `AppSettings.SettingsWindowWidth` / `Height` |
| Chrome | `ToolWindow`, `ShowInTaskbar=False` | unchanged |
| Placement | `CenterOwner`, work-area cap, on-screen clamp | unchanged |

`SizeToContent` goes away, and with it the comment in `SettingsWindow.xaml` explaining why a fixed
height cap was the wrong guess — that whole problem is what this increment removes.

**The work-area `MaxHeight` cap and the `OnRenderSizeChanged` clamp both stay.** They are not
size-to-content machinery. A remembered 900px height opened later on a 768px laptop still has to be
cut down, and a window centred on a widget parked near a screen edge still has to be pulled back
inside it.

The new settings fields are `double?`, null meaning "never resized", the same nullable idiom and the
same reason as `WindowLeft`: zero is a value a user could have chosen. They are written in
`OnClosed`.

Which page opens is the caller's decision, passed at construction: `ShowSettings()` opens on
Appearance, the tray's "Open diagnostics" opens on the first diagnostics page. The selected page is
not remembered across sessions — settings is opened to change one thing, and starting on whatever
was last inspected is a worse default than starting at the top.

Focus-loss dismissal (PRD §17) is unchanged: the shell is still owned by the widget, still feeds the
same dismissal timer through `Activated`/`Deactivated`, and still closes when focus leaves the
application unless the widget is pinned.

## 4. Code shape

```
ViewModels/SettingsShellViewModel.cs      Pages, SelectedPage, Select(key)
ViewModels/SettingsPageViewModel.cs       Key, Title, GroupTitle, Content
Views/Settings/AppearancePage.xaml        ┐
Views/Settings/WindowPage.xaml            │ UserControls,
Views/Settings/ProvidersPage.xaml         │ DataContext = SettingsViewModel
Views/Settings/NotificationsPage.xaml     │
Views/Settings/RefreshPage.xaml           ┘
Views/Settings/DiagnosticsPage.xaml       DataContext = DiagnosticSection
Views/Settings/SettingsPageTemplateSelector.cs
Views/SettingsWindow.xaml(.cs)            becomes the shell
Views/DiagnosticsWindow.xaml(.cs)         deleted
```

### `SettingsViewModel` is not split

The five settings pages are `UserControl`s whose `DataContext` is the existing, unsplit
`SettingsViewModel`, with markup lifted verbatim from today's `SettingsWindow.xaml`. Every binding
path inside them is unchanged.

This is deliberate. Splitting a 447-line view model into five is the version of this change that
breaks behaviour, and it buys nothing the sidebar needs: the pages are a presentation grouping, not
five independent models. `SettingsViewModelTests` and `DiagnosticsViewModelTests` should keep
passing with one mechanical edit and no behavioural one — see §6.

The content pane is a `ContentControl` bound to `SelectedPage.Content`. Diagnostics pages resolve
through `DataTemplate DataType="{x:Type vm:DiagnosticSection}"` for free, since that type is
distinct; the five settings pages all carry the same `SettingsViewModel` instance, so a small
`SettingsPageTemplateSelector` keyed on `SettingsPageViewModel.Key` disambiguates them.

### The trap: `Sections` is replaced, and it is reachable today

`DiagnosticsViewModel.Rebuild()` does not mutate its sections — it constructs new
`DiagnosticSection` instances and replaces the whole list. `BuildBundle()` calls `Rebuild()` first,
and `Copy()` calls `BuildBundle()`.

So **pressing "Copy all diagnostics" replaces every section object while a diagnostics page is on
screen.** A page that captured its `DiagnosticSection` at construction keeps rendering the orphaned
one: no crash, no blank pane, just values that silently stop tracking. If the sidebar entries are
also built from captured sections, selection resets to the top of the list on the same click.

Diagnostics page entries must therefore hold the section *title* — the provider's display name, or
`"Application"` — and resolve the section through `DiagnosticsViewModel.Sections` each time, raising
`PropertyChanged` when the list is replaced; selection must survive that. This gets its own task and
its own test.

Today's diagnostics window is a snapshot: `Rebuild()` runs at construction and on copy, never on a
refresh tick, so values are frozen at open. **That behaviour is preserved** — making diagnostics
live is a separate decision, not part of a layout change. The indirection above exists so that
turning it on later is a subscription, not a redesign.

### `WidgetWindow` call sites

`_diagnosticsWindow` and `ShowDiagnostics()` collapse into `ShowSettings(page)`. The paired
`_settingsWindow?.Close(); _diagnosticsWindow?.Close();` in the dismissal path becomes one line, and
the comment above it about diagnostics being "the larger window… the more conspicuous thing left
behind" no longer describes anything and goes with it. The tray menu keeps both "Open settings" and
"Open diagnostics"; they now differ only in the page they open on.

### Dead constructor arguments

`SettingsViewModel` takes `openDiagnostics` and `openLogs` purely to back two ACTIONS buttons that
no longer exist — diagnostics is navigation now, and "Open logs folder" lives on the Application
page against `DiagnosticsViewModel.OpenLogsCommand`, which already exists. Both parameters and both
commands are removed rather than left wired to nothing.

The shell owns both view models and disposes `SettingsViewModel` in `OnClosed`, as
`SettingsWindow` does today.

## 5. PRD amendments

These are requirement changes, made explicitly rather than contradicted in code:

- **§19**, the paragraph beginning *"The settings window must be tall enough to show every setting it
  offers at once"* — replaced. The new rule: settings are grouped into categories reachable in one
  click from a persistent sidebar; the window opens at a deliberate size, is resizable, and
  remembers the size the user chose; only the content pane scrolls.
- **§19**, bullet *"Open diagnostics"* — reworded as navigation within the settings window.
- **§17**, *"Settings window, for configuration and diagnostics access"* — tightened to say
  diagnostics is presented inside that window rather than beside it.
- **§20** — diagnostics is presented one provider per page; the copied bundle remains whole-application
  and remains redacted.

## 6. Verification

`tests/AiUsageMonitor.App.Tests`, on the existing `WpfFixture`:

- every page `UserControl` constructs, and measures/arranges at the content pane's width;
- the shell's page inventory matches the provider list — a diagnostics page per provider plus
  Application, in provider order;
- selection survives a `Rebuild()`, and the selected diagnostics page renders the *new* section
  afterwards, not the orphaned one;
- `Select(key)` opens the requested page, including the tray's diagnostics entry point;
- a remembered size larger than the work area is clamped on open (extends `PlacementClampTests`).

`SettingsViewModelTests` changes in exactly two ways: two constructor arguments disappear from each
construction site, and `TheActionsCallWhatTheyClaimTo` loses the two commands that no longer exist.
If it needs an edit that is neither of those, the refactor has gone wrong.
`DiagnosticsViewModelTests` does not change at all.

Neither proves it looks right. A bounded smoke launch and a look at the real window close the
increment, per the project's usual practice for presentation work.

## 7. Deliberately not doing

- **A search box.** Obsidian needs one because plugins contribute dozens of pages. Eight pages do not
  justify a search index and the empty-state, match-highlight and keyboard-focus behaviour behind it.
- **Sidebar icons.** The reference has them. We have no icon set beyond the title bar's Segoe MDL2
  glyphs, and choosing eight is design work this increment was scoped to avoid. Adding them later
  costs one column in the item template.
- **A `GridSplitter`.** The sidebar holds eight short labels. It does not need to be draggable.
- **Remembering the last page** — see §3.
- **Live-updating diagnostics** — see §4.
