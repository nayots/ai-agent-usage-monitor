# Settings Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the two tall single-column configuration windows with one window that has a category sidebar and shows one page at a time.

**Architecture:** `SettingsWindow` becomes a two-column shell — a grouped `ListBox` of pages on the left, a `ContentControl` on the right. The five settings pages are `UserControl`s that all share the existing, unsplit `SettingsViewModel` as their `DataContext`, with markup lifted verbatim so no binding path changes. Diagnostics folds in as a generated group of pages, one per `DiagnosticSection`, and `DiagnosticsWindow` is deleted. A new `SettingsShellViewModel` owns the page inventory, the selection, and the remembered window size.

**Tech Stack:** .NET 10, WPF, xUnit. No new package references.

## Global Constraints

- `dotnet build` treats warnings as errors. Every task must end with a clean build.
- Windows-only. Primary shell is PowerShell 5.1 — **no `&&`, no ternary, no `??`**. Chain with `;` or `if ($?) { }`.
- No new `PackageReference` in any project.
- `src/AiUsageMonitor.Domain` must gain no dependency and no WPF type.
- Credentials are never logged, persisted, displayed, or placed in any diagnostic string. This plan touches no credential path; keep it that way.
- Existing code comments in this repo explain *why*, not *what*. Match that register — every comment this plan asks you to write is given verbatim; do not add narration comments of your own.
- Commit after every task, using the message given in the task's final step.

## Files

| File | Responsibility |
|---|---|
| `src/AiUsageMonitor.Infrastructure/Settings/AppSettings.cs` | *(modify)* gains `SettingsWindowWidth` / `SettingsWindowHeight` |
| `src/AiUsageMonitor.App/ViewModels/SettingsPageViewModel.cs` | *(create)* one sidebar entry: kind, title, group, and a lazily resolved body |
| `src/AiUsageMonitor.App/ViewModels/SettingsShellViewModel.cs` | *(create)* page inventory, selection, remembered size |
| `src/AiUsageMonitor.App/Views/Settings/AppearancePage.xaml` | *(create)* theme, density, mini mode, colour bars |
| `src/AiUsageMonitor.App/Views/Settings/WindowPage.xaml` | *(create)* pinning note, unavailable providers, startup, hotkey, reset position |
| `src/AiUsageMonitor.App/Views/Settings/ProvidersPage.xaml` | *(create)* per-provider visibility, order, interval, re-check |
| `src/AiUsageMonitor.App/Views/Settings/NotificationsPage.xaml` | *(create)* notify switch, thresholds, quiet hours |
| `src/AiUsageMonitor.App/Views/Settings/RefreshPage.xaml` | *(create)* refresh interval, stale threshold |
| `src/AiUsageMonitor.App/Views/Settings/DiagnosticsPage.xaml` | *(create)* renders one `DiagnosticSection` |
| `src/AiUsageMonitor.App/Views/Settings/SettingsPageTemplateSelector.cs` | *(create)* picks a page body by `SettingsPageKind` |
| `src/AiUsageMonitor.App/Views/SettingsWindow.xaml(.cs)` | *(rewrite)* the shell: sidebar, banner, content pane, diagnostics footer, size persistence |
| `src/AiUsageMonitor.App/Themes/Controls.xaml` | *(modify)* gains `SettingsNavItemStyle` and `SettingsNavGroupTextStyle` |
| `src/AiUsageMonitor.App/Views/DiagnosticsWindow.xaml(.cs)` | *(delete)* in Task 4 |
| `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs` | *(modify)* one settings window, opened on a chosen page |
| `src/AiUsageMonitor.App/ViewModels/SettingsViewModel.cs` | *(modify)* drops the two dead constructor arguments and their commands |
| `src/AiUsageMonitor.App/ViewModels/DiagnosticsViewModel.cs` | *(modify)* gains `ApplicationSectionTitle` |
| `docs/PRD.md` | *(modify)* §17, §19, §20 |

---

### Task 1: Remember the settings window's size

**Files:**
- Modify: `src/AiUsageMonitor.Infrastructure/Settings/AppSettings.cs`
- Test: `tests/AiUsageMonitor.Infrastructure.Tests/AppSettingsStoreTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `AppSettings.SettingsWindowWidth` and `AppSettings.SettingsWindowHeight`, both `double?`, both defaulting to `null`.

- [ ] **Step 1: Write the failing test**

Append to `tests/AiUsageMonitor.Infrastructure.Tests/AppSettingsStoreTests.cs`, inside the existing test class:

```csharp
    [Fact]
    public void TheSettingsWindowSizeRoundTrips()
    {
        using TempDirectory directory = new();
        AppSettingsStore store = new(directory.File("settings.json"));

        store.Save(AppSettings.Default with { SettingsWindowWidth = 900, SettingsWindowHeight = 700 });

        AppSettings loaded = store.Load().Settings;
        Assert.Equal(900, loaded.SettingsWindowWidth);
        Assert.Equal(700, loaded.SettingsWindowHeight);
    }

    [Fact]
    public void AWindowSizeIsNullUntilTheWindowHasBeenResized()
    {
        Assert.Null(AppSettings.Default.SettingsWindowWidth);
        Assert.Null(AppSettings.Default.SettingsWindowHeight);
    }
```

`TempDirectory` and its `File(name)` helper are already used throughout this file, and `System.IO` is an implicit using. No new `using` directive is needed.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/AiUsageMonitor.Infrastructure.Tests --filter FullyQualifiedName~SettingsWindow`

Expected: FAIL to compile — `AppSettings` has no `SettingsWindowWidth`.

- [ ] **Step 3: Add the two properties**

In `src/AiUsageMonitor.Infrastructure/Settings/AppSettings.cs`, immediately after the `WindowTop` property:

```csharp
    /// <summary>
    /// The size the user last dragged the settings window to, or null before they ever have. Null
    /// rather than 0 for the same reason <see cref="WindowLeft"/> is: the window has a deliberate
    /// default size, and "never resized" is a different fact from "resized to nothing".
    /// </summary>
    public double? SettingsWindowWidth { get; init; }

    /// <inheritdoc cref="SettingsWindowWidth"/>
    public double? SettingsWindowHeight { get; init; }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/AiUsageMonitor.Infrastructure.Tests --filter FullyQualifiedName~SettingsWindow`

Expected: PASS, 2 tests.

- [ ] **Step 5: Full build and suite**

Run: `dotnet build`, then `dotnet test`

Expected: clean build, all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/AiUsageMonitor.Infrastructure/Settings/AppSettings.cs tests/AiUsageMonitor.Infrastructure.Tests/AppSettingsStoreTests.cs
git commit -m "feat: remember the settings window size"
```

---

### Task 2: The shell view models

**Files:**
- Create: `src/AiUsageMonitor.App/ViewModels/SettingsPageViewModel.cs`
- Create: `src/AiUsageMonitor.App/ViewModels/SettingsShellViewModel.cs`
- Modify: `src/AiUsageMonitor.App/ViewModels/DiagnosticsViewModel.cs`
- Test: `tests/AiUsageMonitor.App.Tests/SettingsShellViewModelTests.cs`

**Interfaces:**
- Consumes: `AppSettings.SettingsWindowWidth` / `SettingsWindowHeight` from Task 1. `SettingsViewModel` and `DiagnosticsViewModel` as they exist today.
- Produces:
  - `enum SettingsPageKind { Appearance, Window, Providers, Notifications, Refresh, ProviderDiagnostics, ApplicationDiagnostics }`
  - `SettingsPageViewModel` with `Kind`, `Title`, `GroupTitle`, `Content` (`object?`), `IsDiagnostics` (`bool`), `ContentChanged()`
  - `SettingsShellViewModel(SettingsService store, SettingsViewModel settings, DiagnosticsViewModel diagnostics)` with `Settings`, `Diagnostics`, `Pages` (`IReadOnlyList<SettingsPageViewModel>`), `PagesView` (`ICollectionView`), `SelectedPage` (`SettingsPageViewModel`, two-way), `SelectFirstDiagnosticsPage()`, `RememberedWidth`/`RememberedHeight` (`double?`), `RememberSize(double, double)`, `Dispose()`
  - `DiagnosticsViewModel.ApplicationSectionTitle` (`const string`)

**Why `Content` is a function and not a captured value.** `DiagnosticsViewModel.Rebuild()` constructs new `DiagnosticSection` objects and replaces the whole list; `Copy()` calls `BuildBundle()`, which calls `Rebuild()`. So pressing "Copy all diagnostics" replaces every section while a page is on screen. A page holding the object it was built with keeps rendering it — no crash, no blank pane, just values that have silently stopped tracking. Resolving on every read, plus a `PropertyChanged` nudge when the list is replaced, is what this task exists to get right.

- [ ] **Step 1: Write the failing tests**

Create `tests/AiUsageMonitor.App.Tests/SettingsShellViewModelTests.cs`:

```csharp
using System.IO;
using System.Linq;
using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Diagnostics;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Refresh;
using AiUsageMonitor.Infrastructure.Settings;

namespace AiUsageMonitor.App.Tests;

/// <summary>
/// The shell owns navigation and nothing else. Its one piece of real behaviour is surviving a
/// rebuild of the diagnostics sections, which happens on every copy.
/// </summary>
[Collection("wpf")]
public class SettingsShellViewModelTests(WpfFixture wpf)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    private sealed class SilentProbe(string name) : IProviderProbe
    {
        public string Name => name;
        public string Mechanism => "fake";
        public MechanismTier Tier => MechanismTier.Official;

        public Task<ProviderSnapshot> ProbeAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private static IReadOnlyList<ProviderDescriptor> Providers() =>
    [
        new("claude-code", "Claude Code", "CC", new SilentProbe("Claude Code")),
        new("codex", "Codex", "CX", new SilentProbe("Codex"))
    ];

    private static SettingsShellViewModel Shell(out SettingsService store, out DiagnosticsViewModel diagnostics)
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        string path = Path.Combine(Path.GetTempPath(), "aium-shell-" + Guid.NewGuid().ToString("N"), "settings.json");
        store = new SettingsService(new AppSettingsStore(path), AppSettings.Default);

        SettingsViewModel settings = new(
            store,
            new StartupRegistration(@"Software\AiUsageMonitor\tests\Shell", "AiUsageMonitorTest", null),
            resetPosition: () => { },
            recheckProviders: () => { },
            providers: providers);

        diagnostics = new DiagnosticsViewModel(
            [],
            providers,
            new ProviderRefreshService(providers, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1)),
            new EnvironmentReport("1.0", ".NET", "Windows", "C:\\logs", true, false),
            new StartupReport(Now, null),
            "System",
            "100%",
            () => Now,
            _ => { },
            () => { });

        return new SettingsShellViewModel(store, settings, diagnostics);
    }

    [Fact]
    public void TheSidebarListsTheFiveSettingsPagesThenOnePagePerDiagnosticSection() => wpf.Invoke(() =>
    {
        SettingsShellViewModel shell = Shell(out _, out _);

        Assert.Equal(
            new[] { "Appearance", "Window", "Providers", "Notifications", "Refresh", "Claude Code", "Codex", "Application" },
            shell.Pages.Select(page => page.Title));

        Assert.Equal(
            new[] { "Settings", "Settings", "Settings", "Settings", "Settings", "Diagnostics", "Diagnostics", "Diagnostics" },
            shell.Pages.Select(page => page.GroupTitle));

        shell.Dispose();
    });

    [Fact]
    public void TheApplicationPageIsItsOwnKindSoItAloneCanOfferTheLogsFolder() => wpf.Invoke(() =>
    {
        SettingsShellViewModel shell = Shell(out _, out _);

        Assert.Equal(SettingsPageKind.ProviderDiagnostics, shell.Pages.Single(page => page.Title == "Codex").Kind);
        Assert.Equal(SettingsPageKind.ApplicationDiagnostics, shell.Pages.Single(page => page.Title == "Application").Kind);
        Assert.All(shell.Pages.Where(page => page.GroupTitle == "Diagnostics"), page => Assert.True(page.IsDiagnostics));
        Assert.All(shell.Pages.Where(page => page.GroupTitle == "Settings"), page => Assert.False(page.IsDiagnostics));

        shell.Dispose();
    });

    [Fact]
    public void ASettingsPageCarriesTheOneSharedSettingsViewModel() => wpf.Invoke(() =>
    {
        SettingsShellViewModel shell = Shell(out _, out _);

        Assert.All(
            shell.Pages.Where(page => !page.IsDiagnostics),
            page => Assert.Same(shell.Settings, page.Content));

        shell.Dispose();
    });

    [Fact]
    public void TheShellOpensOnAppearance() => wpf.Invoke(() =>
    {
        SettingsShellViewModel shell = Shell(out _, out _);

        Assert.Equal("Appearance", shell.SelectedPage.Title);

        shell.Dispose();
    });

    [Fact]
    public void SelectingTheFirstDiagnosticsPageLandsOnAProviderNotOnApplication() => wpf.Invoke(() =>
    {
        SettingsShellViewModel shell = Shell(out _, out _);

        shell.SelectFirstDiagnosticsPage();

        Assert.Equal("Claude Code", shell.SelectedPage.Title);

        shell.Dispose();
    });

    /// <summary>
    /// Rebuild replaces every DiagnosticSection instance, and Copy calls it. A page that captured
    /// its section at construction would keep rendering the orphan: no exception, no blank pane,
    /// just values that stop tracking. This is the failure this whole indirection exists to stop.
    /// </summary>
    [Fact]
    public void ADiagnosticsPageResolvesTheCurrentSectionAfterARebuild() => wpf.Invoke(() =>
    {
        SettingsShellViewModel shell = Shell(out _, out DiagnosticsViewModel diagnostics);
        SettingsPageViewModel page = shell.Pages.Single(entry => entry.Title == "Codex");

        object? before = page.Content;
        Assert.Same(diagnostics.Sections.Single(section => section.Title == "Codex"), before);

        diagnostics.Rebuild();

        Assert.NotSame(before, page.Content);
        Assert.Same(diagnostics.Sections.Single(section => section.Title == "Codex"), page.Content);

        shell.Dispose();
    });

    [Fact]
    public void ARebuildAnnouncesTheNewContentAndLeavesTheSelectionAlone() => wpf.Invoke(() =>
    {
        SettingsShellViewModel shell = Shell(out _, out DiagnosticsViewModel diagnostics);
        shell.SelectFirstDiagnosticsPage();
        SettingsPageViewModel selected = shell.SelectedPage;

        int announcements = 0;
        selected.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsPageViewModel.Content))
            {
                announcements++;
            }
        };

        diagnostics.Rebuild();

        Assert.Equal(1, announcements);
        Assert.Same(selected, shell.SelectedPage);

        shell.Dispose();
    });

    /// <summary>
    /// A ListBox writes null into SelectedItem when its selection is cleared. Taking that value
    /// would blank the content pane with no way back, so the shell refuses it.
    /// </summary>
    [Fact]
    public void ClearingTheSelectionLeavesThePageOnScreen() => wpf.Invoke(() =>
    {
        SettingsShellViewModel shell = Shell(out _, out _);
        SettingsPageViewModel before = shell.SelectedPage;

        shell.SelectedPage = null!;

        Assert.Same(before, shell.SelectedPage);

        shell.Dispose();
    });

    [Fact]
    public void TheRememberedSizeIsNullUntilOneIsWrittenAndReadsBackAfterwards() => wpf.Invoke(() =>
    {
        SettingsShellViewModel shell = Shell(out SettingsService store, out _);

        Assert.Null(shell.RememberedWidth);
        Assert.Null(shell.RememberedHeight);

        shell.RememberSize(880, 640);

        Assert.Equal(880, shell.RememberedWidth);
        Assert.Equal(640, shell.RememberedHeight);
        Assert.Equal(880, store.Current.SettingsWindowWidth);
        Assert.Equal(640, store.Current.SettingsWindowHeight);

        shell.Dispose();
    });

    [Fact]
    public void TheSidebarIsGroupedForTheListBox() => wpf.Invoke(() =>
    {
        SettingsShellViewModel shell = Shell(out _, out _);

        Assert.NotNull(shell.PagesView.GroupDescriptions);
        Assert.Equal(2, shell.PagesView.Groups.Count);

        shell.Dispose();
    });
}
```

Note the `SettingsViewModel` construction above already omits `openLogs` and `openDiagnostics`. Those arguments still exist at this point in the plan, so **add them back for now** — `openLogs: () => { }, openDiagnostics: () => { },` after `recheckProviders` — and Task 5 removes them again along with every other call site. Leaving them out here is a compile error, not a shortcut.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/AiUsageMonitor.App.Tests --filter FullyQualifiedName~SettingsShellViewModelTests`

Expected: FAIL to compile — `SettingsShellViewModel` does not exist.

- [ ] **Step 3: Name the Application section**

In `src/AiUsageMonitor.App/ViewModels/DiagnosticsViewModel.cs`, add a constant beside `EmptyValue`:

```csharp
    /// <summary>
    /// The title of the section that is about this application rather than about a provider. Named
    /// rather than repeated as a literal because the settings shell keys a page kind off it: the
    /// application page is the only one that offers the logs folder.
    /// </summary>
    public const string ApplicationSectionTitle = "Application";
```

Then in `BuildApplicationSection`, replace the literal `"Application"` first argument of the returned `DiagnosticSection` with `ApplicationSectionTitle`.

- [ ] **Step 4: Write `SettingsPageViewModel`**

Create `src/AiUsageMonitor.App/ViewModels/SettingsPageViewModel.cs`:

```csharp
namespace AiUsageMonitor.App.ViewModels;

/// <summary>Which body a sidebar entry shows. The shell's template selector switches on this.</summary>
public enum SettingsPageKind
{
    Appearance,
    Window,
    Providers,
    Notifications,
    Refresh,
    ProviderDiagnostics,
    ApplicationDiagnostics
}

/// <summary>One entry in the settings shell's sidebar, and the body it shows.</summary>
public sealed class SettingsPageViewModel(
    SettingsPageKind kind,
    string title,
    string groupTitle,
    Func<object?> content) : ObservableObject
{
    public SettingsPageKind Kind { get; } = kind;

    public string Title { get; } = title;

    public string GroupTitle { get; } = groupTitle;

    public bool IsDiagnostics =>
        Kind is SettingsPageKind.ProviderDiagnostics or SettingsPageKind.ApplicationDiagnostics;

    /// <summary>
    /// Resolved on every read, never captured. <c>DiagnosticsViewModel.Rebuild</c> replaces every
    /// section object rather than mutating it, and <c>Copy</c> calls it - so a page holding the
    /// instance it was built with would go on rendering an orphan: no exception and no blank pane,
    /// just values that have silently stopped tracking.
    /// </summary>
    public object? Content => content();

    /// <summary>Announces that <see cref="Content"/> now resolves to something else.</summary>
    public void ContentChanged() => Raise(nameof(Content));
}
```

- [ ] **Step 5: Write `SettingsShellViewModel`**

Create `src/AiUsageMonitor.App/ViewModels/SettingsShellViewModel.cs`:

```csharp
using System.ComponentModel;
using System.Windows.Data;
using AiUsageMonitor.Infrastructure.Settings;

namespace AiUsageMonitor.App.ViewModels;

/// <summary>
/// The settings window's navigation. Owns which pages exist, which one is showing, and the size the
/// window was last left at - and nothing about what any page contains.
/// </summary>
public sealed class SettingsShellViewModel : ObservableObject, IDisposable
{
    public const string SettingsGroup = "Settings";
    public const string DiagnosticsGroup = "Diagnostics";

    private readonly SettingsService _store;
    private SettingsPageViewModel _selectedPage;

    public SettingsShellViewModel(SettingsService store, SettingsViewModel settings, DiagnosticsViewModel diagnostics)
    {
        _store = store;
        Settings = settings;
        Diagnostics = diagnostics;

        List<SettingsPageViewModel> pages =
        [
            SettingsPage(SettingsPageKind.Appearance, "Appearance"),
            SettingsPage(SettingsPageKind.Window, "Window"),
            SettingsPage(SettingsPageKind.Providers, "Providers"),
            SettingsPage(SettingsPageKind.Notifications, "Notifications"),
            SettingsPage(SettingsPageKind.Refresh, "Refresh")
        ];

        foreach (DiagnosticSection section in diagnostics.Sections)
        {
            // Captured by title, not by instance - see SettingsPageViewModel.Content.
            string title = section.Title;
            pages.Add(new SettingsPageViewModel(
                title == DiagnosticsViewModel.ApplicationSectionTitle
                    ? SettingsPageKind.ApplicationDiagnostics
                    : SettingsPageKind.ProviderDiagnostics,
                title,
                DiagnosticsGroup,
                () => SectionFor(title)));
        }

        Pages = pages;
        _selectedPage = pages[0];

        PagesView = CollectionViewSource.GetDefaultView(Pages);
        PagesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SettingsPageViewModel.GroupTitle)));

        Diagnostics.PropertyChanged += OnDiagnosticsChanged;
    }

    public SettingsViewModel Settings { get; }

    public DiagnosticsViewModel Diagnostics { get; }

    public IReadOnlyList<SettingsPageViewModel> Pages { get; }

    /// <summary>
    /// The same pages, grouped for the sidebar. Grouped here rather than by a CollectionViewSource
    /// in the window's resources: a resource is outside the visual tree and has no DataContext, so
    /// binding its Source to Pages fails silently and the sidebar comes up empty.
    /// </summary>
    public ICollectionView PagesView { get; }

    public SettingsPageViewModel SelectedPage
    {
        get => _selectedPage;
        set
        {
            // A ListBox writes null here whenever its selection is cleared. Taking that would blank
            // the content pane with nothing on screen to click to get a page back.
            if (value is not null)
            {
                Set(ref _selectedPage, value);
            }
        }
    }

    public double? RememberedWidth => _store.Current.SettingsWindowWidth;

    public double? RememberedHeight => _store.Current.SettingsWindowHeight;

    /// <summary>Opens diagnostics, which is the tray menu's second way into this window.</summary>
    public void SelectFirstDiagnosticsPage()
    {
        SettingsPageViewModel? first = Pages.FirstOrDefault(page => page.IsDiagnostics);
        if (first is not null)
        {
            SelectedPage = first;
        }
    }

    public void RememberSize(double width, double height) =>
        _store.Update(settings => settings with
        {
            SettingsWindowWidth = width,
            SettingsWindowHeight = height
        });

    public void Dispose()
    {
        Diagnostics.PropertyChanged -= OnDiagnosticsChanged;
        Settings.Dispose();
    }

    private DiagnosticSection? SectionFor(string title) =>
        Diagnostics.Sections.FirstOrDefault(section => section.Title == title);

    private void OnDiagnosticsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DiagnosticsViewModel.Sections))
        {
            return;
        }

        foreach (SettingsPageViewModel page in Pages)
        {
            if (page.IsDiagnostics)
            {
                page.ContentChanged();
            }
        }
    }

    private SettingsPageViewModel SettingsPage(SettingsPageKind kind, string title) =>
        new(kind, title, SettingsGroup, () => Settings);
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/AiUsageMonitor.App.Tests --filter FullyQualifiedName~SettingsShellViewModelTests`

Expected: PASS, 10 tests.

- [ ] **Step 7: Full build and suite**

Run: `dotnet build`, then `dotnet test`

Expected: clean build, all tests pass.

- [ ] **Step 8: Commit**

```bash
git add src/AiUsageMonitor.App/ViewModels tests/AiUsageMonitor.App.Tests/SettingsShellViewModelTests.cs
git commit -m "feat: add the settings shell's navigation model"
```

---

### Task 3: The page controls

**Files:**
- Create: `src/AiUsageMonitor.App/Views/Settings/AppearancePage.xaml` (+ `.xaml.cs`)
- Create: `src/AiUsageMonitor.App/Views/Settings/WindowPage.xaml` (+ `.xaml.cs`)
- Create: `src/AiUsageMonitor.App/Views/Settings/ProvidersPage.xaml` (+ `.xaml.cs`)
- Create: `src/AiUsageMonitor.App/Views/Settings/NotificationsPage.xaml` (+ `.xaml.cs`)
- Create: `src/AiUsageMonitor.App/Views/Settings/RefreshPage.xaml` (+ `.xaml.cs`)
- Create: `src/AiUsageMonitor.App/Views/Settings/DiagnosticsPage.xaml` (+ `.xaml.cs`)
- Test: `tests/AiUsageMonitor.App.Tests/SettingsPageLoadingTests.cs`

**Interfaces:**
- Consumes: `SettingsViewModel` (unchanged), `DiagnosticSection` (unchanged).
- Produces: six `UserControl` types in namespace `AiUsageMonitor.App.Views.Settings`. The five settings pages expect a `SettingsViewModel` as `DataContext`; `DiagnosticsPage` expects a `DiagnosticSection`.

**This task is a lift, not a rewrite.** Every control below is cut from the current `src/AiUsageMonitor.App/Views/SettingsWindow.xaml` or `DiagnosticsWindow.xaml` with its bindings, styles, margins, automation names and comments intact. Do not restyle, reorder, rename, reword, or "tidy" anything. Three deliberate changes, and only these three:

1. The `TextBlock` carrying a section caption (`APPEARANCE`, `WINDOW`, `PROVIDERS`, `NOTIFICATIONS`, `REFRESH`) is **dropped** — the sidebar entry is now that label.
2. `Reset window position` moves onto `WindowPage`, `Re-check providers` onto `ProvidersPage`. Both keep `SettingsActionButtonStyle` and their existing `Command` bindings and `AutomationProperties.Name`.
3. The persistence-warning `TextBlock` is **not** on any page — Task 4 puts it in the shell.

`Open diagnostics` and `Open logs folder` are dropped from the settings pages entirely. They are handled in Task 4 — the footer replaces one, navigation replaces the other.

Every `.xaml.cs` is the same three lines, with the class name changed:

```csharp
using System.Windows.Controls;

namespace AiUsageMonitor.App.Views.Settings;

public partial class AppearancePage : UserControl
{
    public AppearancePage() => InitializeComponent();
}
```

Every `.xaml` opens the same way, with the class name changed:

```xml
<UserControl x:Class="AiUsageMonitor.App.Views.Settings.AppearancePage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <StackPanel>
    ...
  </StackPanel>
</UserControl>
```

Page contents, by source line in today's `SettingsWindow.xaml`:

| Page | Lift lines | Then add |
|---|---|---|
| `AppearancePage` | 30–58 (Theme through "Color bars by usage") | — |
| `WindowPage` | 64–85 (pinning note through hotkey warning) | the `Reset window position` button from line 219 |
| `ProvidersPage` | 88–138 (hint through the provider `ItemsControl`) | the `Re-check providers` button from line 218 |
| `NotificationsPage` | 141–186 (notify switch through quiet-hours summary) | — |
| `RefreshPage` | 189–214 (interval through the stale warning) | — |

`DiagnosticsPage` renders one section. Its body is lifted from `DiagnosticsWindow.xaml` lines 18–37 with the `ItemsControl`-over-`Sections` wrapper removed, since the page *is* one section:

```xml
<UserControl x:Class="AiUsageMonitor.App.Views.Settings.DiagnosticsPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <StackPanel>
    <TextBlock Text="{Binding Subtitle}" TextWrapping="Wrap" Margin="0,0,0,6" Foreground="{DynamicResource TextTertiaryBrush}">
      <TextBlock.Style>
        <Style TargetType="TextBlock" BasedOn="{StaticResource CaptionTextStyle}">
          <Setter Property="Visibility" Value="Visible" />
          <Style.Triggers><DataTrigger Binding="{Binding Subtitle}" Value="{x:Null}"><Setter Property="Visibility" Value="Collapsed" /></DataTrigger></Style.Triggers>
        </Style>
      </TextBlock.Style>
    </TextBlock>
    <ItemsControl ItemsSource="{Binding Fields}" Focusable="False">
      <ItemsControl.ItemTemplate>
        <DataTemplate>
          <Grid Margin="0,1">
            <Grid.ColumnDefinitions><ColumnDefinition Width="170" /><ColumnDefinition Width="*" /></Grid.ColumnDefinitions>
            <TextBlock Text="{Binding Label}" Foreground="{DynamicResource TextTertiaryBrush}" Style="{StaticResource CaptionTextStyle}" />
            <TextBlock Grid.Column="1" Text="{Binding Value}" TextWrapping="Wrap" IsHitTestVisible="True" Foreground="{DynamicResource TextPrimaryBrush}" Style="{StaticResource CaptionTextStyle}" />
          </Grid>
        </DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>
    <ItemsControl ItemsSource="{Binding Lines}" Margin="0,6,0,0" Focusable="False">
      <ItemsControl.ItemTemplate><DataTemplate><TextBlock Text="{Binding}" TextWrapping="Wrap" Style="{StaticResource CaptionTextStyle}" Foreground="{DynamicResource TextTertiaryBrush}" /></DataTemplate></ItemsControl.ItemTemplate>
    </ItemsControl>
  </StackPanel>
</UserControl>
```

The label column widens from 150 to 170 because the page is now in a pane roughly twice as wide as the old window.

- [ ] **Step 1: Write the failing test**

Create `tests/AiUsageMonitor.App.Tests/SettingsPageLoadingTests.cs`:

```csharp
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.App.Views.Settings;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Settings;

namespace AiUsageMonitor.App.Tests;

/// <summary>
/// The pages are a lift of markup that already worked, so what needs proving is that each one still
/// parses, binds and measures on its own - and that the two buttons that changed page came with it.
/// </summary>
[Collection("wpf")]
public class SettingsPageLoadingTests(WpfFixture wpf)
{
    private sealed class SilentProbe(string name) : IProviderProbe
    {
        public string Name => name;
        public string Mechanism => "fake";
        public MechanismTier Tier => MechanismTier.Official;

        public Task<ProviderSnapshot> ProbeAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private static SettingsViewModel Model()
    {
        string path = Path.Combine(Path.GetTempPath(), "aium-page-" + Guid.NewGuid().ToString("N"), "settings.json");

        return new SettingsViewModel(
            new SettingsService(new AppSettingsStore(path), AppSettings.Default),
            new StartupRegistration(@"Software\AiUsageMonitor\tests\Pages", "AiUsageMonitorTest", null),
            resetPosition: () => { },
            recheckProviders: () => { },
            openLogs: () => { },
            openDiagnostics: () => { },
            providers:
            [
                new ProviderDescriptor("claude-code", "Claude Code", "CC", new SilentProbe("Claude Code")),
                new ProviderDescriptor("codex", "Codex", "CX", new SilentProbe("Codex"))
            ]);
    }

    private static T Page<T>(object dataContext) where T : UserControl, new()
    {
        T page = new() { DataContext = dataContext, Width = 520 };
        Border host = new() { Child = page };
        host.Measure(new Size(520, 900));
        host.Arrange(new Rect(0, 0, 520, 900));
        host.UpdateLayout();
        return page;
    }

    [Fact]
    public void EverySettingsPageParsesBindsAndMeasures() => wpf.Invoke(() =>
    {
        SettingsViewModel model = Model();

        Assert.True(Page<AppearancePage>(model).ActualHeight > 0);
        Assert.True(Page<WindowPage>(model).ActualHeight > 0);
        Assert.True(Page<ProvidersPage>(model).ActualHeight > 0);
        Assert.True(Page<NotificationsPage>(model).ActualHeight > 0);
        Assert.True(Page<RefreshPage>(model).ActualHeight > 0);

        model.Dispose();
    });

    [Fact]
    public void TheTwoActionsThatChangedPageCameWithTheirPage() => wpf.Invoke(() =>
    {
        SettingsViewModel model = Model();

        Assert.Contains(
            Descendants(Page<WindowPage>(model)).OfType<Button>(),
            button => AutomationProperties.GetName(button) == "Reset window position");
        Assert.Contains(
            Descendants(Page<ProvidersPage>(model)).OfType<Button>(),
            button => AutomationProperties.GetName(button) == "Re-check providers");

        model.Dispose();
    });

    /// <summary>
    /// The sidebar entry is the section label now. A page that also printed its own caption would
    /// say the same word twice, six inches apart.
    /// </summary>
    [Fact]
    public void NoPageRepeatsItsOwnSidebarLabelAsACaption() => wpf.Invoke(() =>
    {
        SettingsViewModel model = Model();

        Assert.DoesNotContain("APPEARANCE", Texts(Page<AppearancePage>(model)));
        Assert.DoesNotContain("WINDOW", Texts(Page<WindowPage>(model)));
        Assert.DoesNotContain("PROVIDERS", Texts(Page<ProvidersPage>(model)));
        Assert.DoesNotContain("NOTIFICATIONS", Texts(Page<NotificationsPage>(model)));
        Assert.DoesNotContain("REFRESH", Texts(Page<RefreshPage>(model)));

        model.Dispose();
    });

    [Fact]
    public void TheDiagnosticsPageRendersOneSection() => wpf.Invoke(() =>
    {
        DiagnosticSection section = new(
            "Claude Code",
            null,
            [new DiagnosticField("Installed", "Yes"), new DiagnosticField("Version", "2.1.226")],
            ["five_hour · 5-hour window · 47%"]);

        DiagnosticsPage page = Page<DiagnosticsPage>(section);

        Assert.Contains("Installed", Texts(page));
        Assert.Contains("2.1.226", Texts(page));
        Assert.Contains("five_hour · 5-hour window · 47%", Texts(page));
    });

    private static IEnumerable<string> Texts(DependencyObject root)
    {
        if (root is TextBlock block)
        {
            yield return block.Text;
        }

        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            foreach (string text in Texts(System.Windows.Media.VisualTreeHelper.GetChild(root, i)))
            {
                yield return text;
            }
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            yield return child;

            foreach (DependencyObject descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/AiUsageMonitor.App.Tests --filter FullyQualifiedName~SettingsPageLoadingTests`

Expected: FAIL to compile — none of the page types exist.

- [ ] **Step 3: Create the six pages**

Follow the lift table and the templates above. Create both files for each page.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/AiUsageMonitor.App.Tests --filter FullyQualifiedName~SettingsPageLoadingTests`

Expected: PASS, 4 tests.

- [ ] **Step 5: Full build and suite**

Run: `dotnet build`, then `dotnet test`

Expected: clean build, all tests pass. `SettingsWindow.xaml` is untouched and its tests still pass — the pages are additive at this point.

- [ ] **Step 6: Commit**

```bash
git add src/AiUsageMonitor.App/Views/Settings tests/AiUsageMonitor.App.Tests/SettingsPageLoadingTests.cs
git commit -m "feat: split the settings body into one control per category"
```

---

### Task 4: The shell window, and the two ways into it

**Files:**
- Create: `src/AiUsageMonitor.App/Views/Settings/SettingsPageTemplateSelector.cs`
- Modify: `src/AiUsageMonitor.App/Themes/Controls.xaml`
- Rewrite: `src/AiUsageMonitor.App/Views/SettingsWindow.xaml`
- Rewrite: `src/AiUsageMonitor.App/Views/SettingsWindow.xaml.cs`
- Modify: `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs:370-428` and `:648-654`
- Delete: `src/AiUsageMonitor.App/Views/DiagnosticsWindow.xaml`, `src/AiUsageMonitor.App/Views/DiagnosticsWindow.xaml.cs`
- Modify: `tests/AiUsageMonitor.App.Tests/ViewLoadingTests.cs`
- Modify: `tests/AiUsageMonitor.App.Tests/WidgetWindowTests.cs:183-207`

**Interfaces:**
- Consumes: everything from Tasks 1–3.
- Produces: `SettingsWindow(SettingsShellViewModel model)` — the old `SettingsWindow(SettingsViewModel)` constructor is gone. `WidgetWindow.ShowSettings()` and `WidgetWindow.ShowDiagnostics()` both remain public and both open the one settings window; `ShowDiagnostics` selects the first diagnostics page.

**This task is large and cannot honestly be split.** Rewriting `SettingsWindow`'s constructor breaks `WidgetWindow`, which is the only caller; changing `WidgetWindow.ShowDiagnostics` breaks the tests that expect a `DiagnosticsWindow`. Any boundary drawn inside it leaves the build red, which no task in this plan is allowed to do. Work through it in order and commit once at the end.

**The six existing tests that break, and what each becomes.** Do not delete any of them except where stated:

| Test | Why it breaks | Do this |
|---|---|---|
| `TheSettingsWindowShowsEverySettingWithoutScrolling` | asserts `(ScrollViewer)window.Content`, which is the rule this increment removes | **Delete it.** Its replacement is the sidebar itself. |
| `TheSettingsWindowRendersInEveryPalette` | `SettingsContent()` measures at 380×640 | measure the shell at 740×560 |
| `TheSettingsWindowOffersNoPinning` | the Window page is not in the tree when Appearance is showing | select the Window page first |
| `TheSettingsWindowLoadsProviderPreferences` | asserts the `"PROVIDERS"` caption, which is now a sidebar entry | assert the sidebar entry instead, then select it |
| `TheSettingsWindowLoadsMiniModeWithItsDockFollowingIt` | Appearance is the first page, so this one only needs the new constructor | rewrite the helper |
| `TheSettingsWindowLoadsTheNotificationControls`, `TheQuietHoursTimesFollowTheirOwnCheckboxAndTheNotificationsSwitch` | Notifications page is not showing | select the Notifications page first |

`TheSettingsWindowIsNudgedBackInsideTheScreen` keeps working and must keep passing — it is the proof the clamp survived the rewrite.

- [ ] **Step 1: Write the failing tests**

In `tests/AiUsageMonitor.App.Tests/ViewLoadingTests.cs`, replace the private helpers `Shown`, `SettingsContent` and `SettingsModel` with:

```csharp
    private static SettingsWindow Shown(double left = -4000, double top = -4000)
    {
        SettingsWindow window = new(Shell())
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = left,
            Top = top,
            Opacity = 0,
            ShowActivated = false
        };

        window.Show();
        window.UpdateLayout();
        return window;
    }

    /// <summary>
    /// The shell's content, laid out at the window's default size, with <paramref name="page"/>
    /// showing. One page is in the visual tree at a time now, so a test that looks for a control has
    /// to say which page it expects to find it on.
    /// </summary>
    private static FrameworkElement SettingsContent(string page = "Appearance")
    {
        SettingsShellViewModel shell = Shell();
        SettingsWindow window = new(shell);
        shell.SelectedPage = shell.Pages.Single(entry => entry.Title == page);

        FrameworkElement content = (FrameworkElement)window.Content;
        content.Measure(new Size(740, 560));
        content.Arrange(new Rect(0, 0, 740, 560));
        content.UpdateLayout();
        return content;
    }

    private static SettingsShellViewModel Shell()
    {
        IReadOnlyList<ProviderDescriptor> providers =
        [
            new("claude-code", "Claude Code", "CC", new SilentProbe("Claude Code")),
            new("codex", "Codex", "CX", new SilentProbe("Codex"))
        ];

        string path = Path.Combine(Path.GetTempPath(), "aium-view-" + Guid.NewGuid().ToString("N"), "settings.json");
        SettingsService store = new(new AppSettingsStore(path), AppSettings.Default);

        SettingsViewModel settings = new(
            store,
            new StartupRegistration(@"Software\AiUsageMonitor\tests\ViewLoading", "AiUsageMonitorTest", null),
            resetPosition: () => { },
            recheckProviders: () => { },
            openLogs: () => { },
            openDiagnostics: () => { },
            providers: providers);

        DiagnosticsViewModel diagnostics = new(
            [],
            providers,
            new ProviderRefreshService(providers, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1)),
            new EnvironmentReport("1.0", ".NET", "Windows", "C:\\logs", true, false),
            new StartupReport(Now, null),
            "System",
            "100%",
            () => Now,
            _ => { },
            () => { });

        return new SettingsShellViewModel(store, settings, diagnostics);
    }
```

Then make these edits to the tests themselves:

1. **Delete** `TheSettingsWindowShowsEverySettingWithoutScrolling` entirely, including its `<summary>` block.
2. In `TheSettingsWindowOffersNoPinning`, change the first line to `FrameworkElement content = SettingsContent("Window");`.
3. In `TheSettingsWindowLoadsTheNotificationControls`, change the first line to `FrameworkElement content = SettingsContent("Notifications");`.
4. In `TheSettingsWindowLoadsProviderPreferences`, replace the body with:

```csharp
        FrameworkElement content = SettingsContent("Providers");

        Assert.Contains(
            Descendants(content).OfType<CheckBox>(),
            box => AutomationProperties.GetName(box) == "Show Claude Code");
        Assert.Contains(
            Descendants(content).OfType<Button>(),
            button => AutomationProperties.GetName(button) == "Move Codex down");
```

5. In `TheSettingsWindowLoadsMiniModeWithItsDockFollowingIt` and `TheQuietHoursTimesFollowTheirOwnCheckboxAndTheNotificationsSwitch`, replace the three opening lines that build a `SettingsViewModel` and a `SettingsWindow` with a shell. For mini mode:

```csharp
        SettingsShellViewModel shell = Shell();
        SettingsWindow window = new(shell);
        FrameworkElement content = (FrameworkElement)window.Content;
        content.Measure(new Size(740, 560));
        content.UpdateLayout();
        SettingsViewModel model = shell.Settings;
```

and change the final `model.Dispose();` to `shell.Dispose();`. For quiet hours, the same four lines, but select the Notifications page immediately after constructing the window:

```csharp
        shell.SelectedPage = shell.Pages.Single(entry => entry.Title == "Notifications");
```

6. Add these three new tests to the class:

```csharp
    [Fact]
    public void TheSidebarOffersEveryCategoryAndDiagnosticsAmongThem() => wpf.Invoke(() =>
    {
        FrameworkElement content = SettingsContent();
        ListBox navigation = Descendants(content).OfType<ListBox>().Single();

        Assert.Equal(
            new[] { "Appearance", "Window", "Providers", "Notifications", "Refresh", "Claude Code", "Codex", "Application" },
            navigation.Items.Cast<SettingsPageViewModel>().Select(page => page.Title));
    });

    /// <summary>
    /// The copy button is the shell's, not the page's, so that it can be offered on every
    /// diagnostics page while the logs folder stays on the one page it belongs to.
    /// </summary>
    [Fact]
    public void TheLogsFolderIsOfferedOnTheApplicationPageAndNoOther() => wpf.Invoke(() =>
    {
        FrameworkElement provider = SettingsContent("Claude Code");
        Assert.Contains(Descendants(provider).OfType<Button>(), button => AutomationProperties.GetName(button) == "Copy all diagnostics");
        Assert.DoesNotContain(Descendants(provider).OfType<Button>(), button => AutomationProperties.GetName(button) == "Open logs folder");

        FrameworkElement application = SettingsContent("Application");
        Assert.Contains(Descendants(application).OfType<Button>(), button => AutomationProperties.GetName(button) == "Copy all diagnostics");
        Assert.Contains(Descendants(application).OfType<Button>(), button => AutomationProperties.GetName(button) == "Open logs folder");
    });

    [Fact]
    public void ASettingsPageOffersNeitherOfTheDiagnosticsActions() => wpf.Invoke(() =>
    {
        FrameworkElement content = SettingsContent();

        Assert.DoesNotContain(Descendants(content).OfType<Button>(), button => AutomationProperties.GetName(button) == "Copy all diagnostics");
        Assert.DoesNotContain(Descendants(content).OfType<Button>(), button => AutomationProperties.GetName(button) == "Open logs folder");
    });

    [Fact]
    public void ARememberedSizeTooBigForTheScreenIsCutDownToIt() => wpf.Invoke(() =>
    {
        SettingsShellViewModel shell = Shell();
        shell.RememberSize(20000, 20000);

        SettingsWindow window = new(shell)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -4000,
            Top = -4000,
            Opacity = 0,
            ShowActivated = false
        };

        try
        {
            window.Show();
            window.UpdateLayout();

            Rect screen = SystemParameters.WorkArea;
            Assert.True(window.ActualWidth <= screen.Width + 0.5, $"{window.ActualWidth} wide against a screen of {screen.Width}");
            Assert.True(window.ActualHeight <= screen.Height + 0.5, $"{window.ActualHeight} tall against a screen of {screen.Height}");
        }
        finally
        {
            window.Close();
        }
    });
```

Add `using AiUsageMonitor.App.ViewModels;` if it is not already present, and `using System.Linq;` likewise.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/AiUsageMonitor.App.Tests --filter FullyQualifiedName~ViewLoadingTests`

Expected: FAIL to compile — `SettingsWindow` has no constructor taking a `SettingsShellViewModel`.

- [ ] **Step 3: Add the sidebar styles**

Append to `src/AiUsageMonitor.App/Themes/Controls.xaml`, before the closing `</ResourceDictionary>`:

```xml
  <Style x:Key="SettingsNavGroupTextStyle" TargetType="TextBlock" BasedOn="{StaticResource CaptionMicroTextStyle}">
    <Setter Property="Foreground" Value="{DynamicResource TextTertiaryBrush}" />
    <Setter Property="Margin" Value="12,14,12,4" />
  </Style>

  <!--
    The sidebar entry. Templated rather than themed through the default ListBoxItem because that one
    paints the system highlight colour, which belongs to no palette here and is invisible under high
    contrast against the layer it sits on. Selection is carried by the fill AND the weight, never by
    colour alone.
  -->
  <Style x:Key="SettingsNavItemStyle" TargetType="ListBoxItem">
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="HorizontalContentAlignment" Value="Stretch" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ListBoxItem">
          <Border x:Name="Fill" Margin="6,1" Padding="8,5" CornerRadius="{DynamicResource RadiusControl}" Background="Transparent">
            <TextBlock x:Name="Label" Text="{Binding Title}" TextTrimming="CharacterEllipsis" Style="{StaticResource BodySmallTextStyle}" Foreground="{DynamicResource TextSecondaryBrush}" />
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Label" Property="Foreground" Value="{DynamicResource TextPrimaryBrush}" />
            </Trigger>
            <Trigger Property="IsSelected" Value="True">
              <Setter TargetName="Fill" Property="Background" Value="{DynamicResource WidgetLayerAltBackgroundBrush}" />
              <Setter TargetName="Label" Property="Foreground" Value="{DynamicResource TextPrimaryBrush}" />
              <Setter TargetName="Label" Property="FontWeight" Value="SemiBold" />
            </Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True">
              <Setter TargetName="Fill" Property="BorderBrush" Value="{DynamicResource TextPrimaryBrush}" />
              <Setter TargetName="Fill" Property="BorderThickness" Value="1" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
```

- [ ] **Step 4: Write the template selector**

Create `src/AiUsageMonitor.App/Views/Settings/SettingsPageTemplateSelector.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using AiUsageMonitor.App.ViewModels;

namespace AiUsageMonitor.App.Views.Settings;

/// <summary>
/// Picks the body for the page on screen. A DataType-keyed template cannot do it: all five settings
/// pages carry the same <see cref="SettingsViewModel"/> instance as their content, so the type says
/// nothing about which page it is.
/// </summary>
public sealed class SettingsPageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Appearance { get; set; }
    public DataTemplate? Window { get; set; }
    public DataTemplate? Providers { get; set; }
    public DataTemplate? Notifications { get; set; }
    public DataTemplate? Refresh { get; set; }
    public DataTemplate? Diagnostics { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item switch
    {
        SettingsPageViewModel { Kind: SettingsPageKind.Appearance } => Appearance,
        SettingsPageViewModel { Kind: SettingsPageKind.Window } => Window,
        SettingsPageViewModel { Kind: SettingsPageKind.Providers } => Providers,
        SettingsPageViewModel { Kind: SettingsPageKind.Notifications } => Notifications,
        SettingsPageViewModel { Kind: SettingsPageKind.Refresh } => Refresh,
        SettingsPageViewModel { IsDiagnostics: true } => Diagnostics,
        _ => null
    };
}
```

- [ ] **Step 5: Write the shell markup**

Replace `src/AiUsageMonitor.App/Views/SettingsWindow.xaml` entirely:

```xml
<Window x:Class="AiUsageMonitor.App.Views.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:settings="clr-namespace:AiUsageMonitor.App.Views.Settings"
        Title="Quota Monitor settings"
        Width="740" Height="560" MinWidth="620" MinHeight="440"
        WindowStyle="ToolWindow" ResizeMode="CanResize" ShowInTaskbar="False"
        WindowStartupLocation="CenterOwner"
        Background="{DynamicResource WidgetWindowBackgroundBrush}"
        Foreground="{DynamicResource TextPrimaryBrush}"
        FontFamily="{StaticResource WidgetFontFamily}"
        UseLayoutRounding="True" SnapsToDevicePixels="True">
  <Window.Resources>
    <settings:SettingsPageTemplateSelector x:Key="PageBody">
      <settings:SettingsPageTemplateSelector.Appearance>
        <DataTemplate><settings:AppearancePage DataContext="{Binding Content}" /></DataTemplate>
      </settings:SettingsPageTemplateSelector.Appearance>
      <settings:SettingsPageTemplateSelector.Window>
        <DataTemplate><settings:WindowPage DataContext="{Binding Content}" /></DataTemplate>
      </settings:SettingsPageTemplateSelector.Window>
      <settings:SettingsPageTemplateSelector.Providers>
        <DataTemplate><settings:ProvidersPage DataContext="{Binding Content}" /></DataTemplate>
      </settings:SettingsPageTemplateSelector.Providers>
      <settings:SettingsPageTemplateSelector.Notifications>
        <DataTemplate><settings:NotificationsPage DataContext="{Binding Content}" /></DataTemplate>
      </settings:SettingsPageTemplateSelector.Notifications>
      <settings:SettingsPageTemplateSelector.Refresh>
        <DataTemplate><settings:RefreshPage DataContext="{Binding Content}" /></DataTemplate>
      </settings:SettingsPageTemplateSelector.Refresh>
      <settings:SettingsPageTemplateSelector.Diagnostics>
        <DataTemplate><settings:DiagnosticsPage DataContext="{Binding Content}" /></DataTemplate>
      </settings:SettingsPageTemplateSelector.Diagnostics>
    </settings:SettingsPageTemplateSelector>
  </Window.Resources>

  <Grid>
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width="176" />
      <ColumnDefinition Width="*" />
    </Grid.ColumnDefinitions>

    <Border Grid.Column="0"
            Background="{DynamicResource WidgetLayerAltBackgroundBrush}"
            BorderBrush="{DynamicResource WidgetCardStrokeBrush}" BorderThickness="0,0,1,0">
      <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
        <ListBox x:Name="Navigation"
                 ItemsSource="{Binding PagesView}"
                 SelectedItem="{Binding SelectedPage, Mode=TwoWay}"
                 ItemContainerStyle="{StaticResource SettingsNavItemStyle}"
                 Background="Transparent" BorderThickness="0"
                 ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                 HorizontalContentAlignment="Stretch"
                 AutomationProperties.Name="Settings categories">
          <ListBox.GroupStyle>
            <GroupStyle>
              <GroupStyle.HeaderTemplate>
                <DataTemplate>
                  <TextBlock Text="{Binding Name}" Style="{StaticResource SettingsNavGroupTextStyle}" />
                </DataTemplate>
              </GroupStyle.HeaderTemplate>
            </GroupStyle>
          </ListBox.GroupStyle>
        </ListBox>
      </ScrollViewer>
    </Border>

    <Grid Grid.Column="1">
      <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />
        <RowDefinition Height="*" />
        <RowDefinition Height="Auto" />
      </Grid.RowDefinitions>

      <!-- The banner is the shell's, not Appearance's: settings that cannot be saved is a fact
           about every page, and putting it on one of them means it is missed from the other seven. -->
      <TextBlock Grid.Row="0" Text="{Binding Settings.PersistenceWarningText}" TextWrapping="Wrap"
                 Margin="18,14,18,0" Foreground="{DynamicResource StateBadBrush}">
        <TextBlock.Style>
          <Style TargetType="TextBlock" BasedOn="{StaticResource CaptionTextStyle}">
            <Setter Property="Visibility" Value="Collapsed" />
            <Style.Triggers><DataTrigger Binding="{Binding Settings.HasPersistenceWarning}" Value="True"><Setter Property="Visibility" Value="Visible" /></DataTrigger></Style.Triggers>
          </Style>
        </TextBlock.Style>
      </TextBlock>

      <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
        <StackPanel Margin="18,14,18,18">
          <TextBlock Text="{Binding SelectedPage.Title}" Margin="0,0,0,10" Style="{StaticResource SubtitleTextStyle}" Foreground="{DynamicResource TextPrimaryBrush}" />
          <ContentControl Content="{Binding SelectedPage}" ContentTemplateSelector="{StaticResource PageBody}" Focusable="False" />
        </StackPanel>
      </ScrollViewer>

      <!-- Copy and the logs folder belong to the shell rather than to DiagnosticsPage, so that the
           copy action can sit on every diagnostics page while the logs folder stays on the one page
           it is about - and so DiagnosticsPage keeps a DiagnosticSection as its whole world. -->
      <StackPanel Grid.Row="2" x:Name="DiagnosticsActions" Margin="18,0,18,14">
        <StackPanel.Style>
          <Style TargetType="StackPanel">
            <Setter Property="Visibility" Value="Collapsed" />
            <Style.Triggers><DataTrigger Binding="{Binding SelectedPage.IsDiagnostics}" Value="True"><Setter Property="Visibility" Value="Visible" /></DataTrigger></Style.Triggers>
          </Style>
        </StackPanel.Style>
        <TextBlock Text="{Binding Diagnostics.CopyHintText}" TextWrapping="Wrap" Style="{StaticResource CaptionTextStyle}" Foreground="{DynamicResource TextTertiaryBrush}" />
        <StackPanel Orientation="Horizontal">
          <Button Style="{StaticResource SettingsActionButtonStyle}" Content="Copy all diagnostics" Command="{Binding Diagnostics.CopyCommand}" AutomationProperties.Name="Copy all diagnostics" />
          <Button Margin="16,0,0,0" Style="{StaticResource SettingsActionButtonStyle}" Content="Open logs folder" Command="{Binding Diagnostics.OpenLogsCommand}" AutomationProperties.Name="Open logs folder">
            <Button.Style>
              <Style TargetType="Button" BasedOn="{StaticResource SettingsActionButtonStyle}">
                <Setter Property="Visibility" Value="Collapsed" />
                <Style.Triggers><DataTrigger Binding="{Binding SelectedPage.Kind}" Value="ApplicationDiagnostics"><Setter Property="Visibility" Value="Visible" /></DataTrigger></Style.Triggers>
              </Style>
            </Button.Style>
          </Button>
        </StackPanel>
        <TextBlock x:Name="CopyConfirmation" Text="{Binding Diagnostics.CopyConfirmationText}" TextWrapping="Wrap" Foreground="{DynamicResource TextTertiaryBrush}">
          <TextBlock.Style>
            <Style TargetType="TextBlock" BasedOn="{StaticResource CaptionTextStyle}">
              <Setter Property="Visibility" Value="Visible" />
              <Style.Triggers><DataTrigger Binding="{Binding Diagnostics.CopyConfirmationText}" Value="{x:Null}"><Setter Property="Visibility" Value="Collapsed" /></DataTrigger></Style.Triggers>
            </Style>
          </TextBlock.Style>
        </TextBlock>
      </StackPanel>
    </Grid>
  </Grid>
</Window>
```

One trap while writing this: the logs button sets `Margin` as an attribute *and* declares a `Style` as a property element. That is legal — MC3024 fires only when the **same** property is set twice, so `Margin` attribute plus `Button.Style` element is fine, while a `Style="..."` attribute plus a `<Button.Style>` element is not. `SubtitleTextStyle`, `BodySmallTextStyle`, `CaptionTextStyle` and `CaptionMicroTextStyle` are all defined in `Tokens.xaml`; do not add new text styles.

- [ ] **Step 6: Write the shell code-behind**

Replace `src/AiUsageMonitor.App/Views/SettingsWindow.xaml.cs` entirely:

```csharp
using System.Windows;
using AiUsageMonitor.App.Interop;
using AiUsageMonitor.App.ViewModels;

namespace AiUsageMonitor.App.Views;

/// <summary>
/// Settings apply as they are changed. There is no OK, Cancel or Apply: the widget is visible
/// behind this window, so every change is already on screen by the time the user could press one,
/// and a commit step would only add a way to be wrong about whether a change had taken.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsShellViewModel _model;

    public SettingsWindow(SettingsShellViewModel model)
    {
        _model = model;
        InitializeComponent();
        DataContext = model;

        // Before the first layout pass, so the size below is the one the window is measured at
        // rather than a resize the user sees happen.
        if (model.RememberedWidth is double width)
        {
            Width = width;
        }

        if (model.RememberedHeight is double height)
        {
            Height = height;
        }
    }

    /// <summary>
    /// Caps the window at the screen it is opening on. The window no longer sizes itself to its
    /// content, but a size remembered from a larger monitor is the same problem wearing a different
    /// hat: set before the first layout pass, this is the bound that measurement obeys.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        Rect screen = ScreenBounds.WorkAreaFor(this);
        MaxHeight = screen.Height;
        MaxWidth = screen.Width;
    }

    /// <summary>
    /// Brings the window back inside the screen as soon as it knows how big it is.
    /// <para>
    /// <c>WindowStartupLocation="CenterOwner"</c> centres this window on the widget and stops
    /// there: it knows nothing of screen edges, so a window opened from a widget parked near the
    /// bottom of the screen would hang off it, with the sidebar's last entries unreachable.
    /// </para>
    /// </summary>
    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);

        Rect screen = ScreenBounds.WorkAreaFor(this);

        // ActualHeight, not info.NewSize.Height: a Window's ActualWidth and ActualHeight are its
        // outer size, title bar and borders included, and it is the outer rectangle that has to fit
        // on the screen.
        //
        // Math.Max, not Math.Clamp alone: on a screen too short for the window the low bound would
        // exceed the high one, and Math.Clamp throws rather than picking a side.
        Top = Math.Clamp(Top, screen.Top, Math.Max(screen.Top, screen.Bottom - ActualHeight));
        Left = Math.Clamp(Left, screen.Left, Math.Max(screen.Left, screen.Right - ActualWidth));
    }

    protected override void OnClosed(EventArgs e)
    {
        // RestoreBounds, not ActualWidth, when the window is not in its normal state: a window
        // closed while maximised reports the maximised size, and reopening at that size next time
        // would be a size the user never chose.
        Rect bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, ActualWidth, ActualHeight)
            : RestoreBounds;

        _model.RememberSize(bounds.Width, bounds.Height);
        _model.Dispose();
        base.OnClosed(e);
    }
}
```

- [ ] **Step 7: Rewrite the two view tests that build a `DiagnosticsWindow`**

In `tests/AiUsageMonitor.App.Tests/ViewLoadingTests.cs`, replace `DiagnosticsWindowLoadsWithAPopulatedViewModel` and `DiagnosticsCopyConfirmationAppearsOnlyAfterCopying` with:

```csharp
    [Fact]
    public void TheDiagnosticsPagesLoadWithAPopulatedViewModel() => wpf.Invoke(() =>
    {
        FrameworkElement content = SettingsContent("Claude Code");

        Assert.Contains("Installed", Texts(content));
        Assert.Contains("Mechanism tier", Texts(content));
    });

    /// <summary>
    /// The confirmation is the shell's, and it appears only once there is something to confirm.
    /// Copy is also the one action that replaces every DiagnosticSection while a page is on screen,
    /// so this exercises the path that would leave an orphaned section rendering.
    /// </summary>
    [Fact]
    public void TheCopyConfirmationAppearsOnlyAfterCopying() => wpf.Invoke(() =>
    {
        SettingsShellViewModel shell = Shell();
        SettingsWindow window = new(shell)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -4000,
            Top = -4000,
            Opacity = 0,
            ShowActivated = false
        };

        try
        {
            window.Show();
            shell.SelectFirstDiagnosticsPage();
            window.UpdateLayout();

            TextBlock confirmation = (TextBlock)window.FindName("CopyConfirmation");
            Assert.Equal(Visibility.Collapsed, confirmation.Visibility);

            shell.Diagnostics.CopyCommand.Execute(null);
            window.UpdateLayout();

            Assert.Equal(Visibility.Visible, confirmation.Visibility);
            Assert.Same(
                shell.Diagnostics.Sections.First(),
                shell.Pages.First(page => page.IsDiagnostics).Content);
        }
        finally
        {
            window.Close();
        }
    });
```

`CopyToClipboard` in the shell helper is already a no-op lambda, so no clipboard is touched.

- [ ] **Step 8: Rewrite the widget dismissal test**

In `tests/AiUsageMonitor.App.Tests/WidgetWindowTests.cs`, replace `DismissalClosesTheDiagnosticsWindowToo` with:

```csharp
    /// <summary>
    /// The dismissal has to take the owned window with it. A widget that hides while its settings
    /// window stays up leaves the largest window this application owns on screen with nothing
    /// behind it. Diagnostics is a page of that window now, so there is one window to close, not two.
    /// </summary>
    [Fact]
    public void DismissalClosesTheSettingsWindowOpenedOnDiagnostics() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default);

        WidgetWindow window = new(model, Settings(AppSettings.Default));
        window.Show();
        window.ShowDiagnostics();

        Assert.Contains(Application.Current.Windows.OfType<SettingsWindow>(), _ => true);

        window.DismissIfFocusLeftTheApplication(focusStayedInTheApplication: false);

        Assert.Empty(Application.Current.Windows.OfType<SettingsWindow>());
        Assert.Equal(Visibility.Hidden, window.Visibility);

        window.Close();
        model.Dispose();
    });
```

- [ ] **Step 9: Rewrite the widget's two entry points**

In `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs`, delete the `_diagnosticsWindow` field (line 34) and replace `ShowSettings` and `ShowDiagnostics` with:

```csharp
    /// <summary>Opens the settings window on its first page, or focuses the one already open.</summary>
    public void ShowSettings() => ShowShell(shell => shell.SelectedPage = shell.Pages[0]);

    /// <summary>
    /// Opens the settings window on diagnostics, or moves the one already open to it. The tray menu
    /// keeps both entries because they answer different questions; they are one window now.
    /// </summary>
    public void ShowDiagnostics() => ShowShell(shell => shell.SelectFirstDiagnosticsPage());

    private void ShowShell(Action<SettingsShellViewModel> select)
    {
        if (_settingsWindow is not null)
        {
            select((SettingsShellViewModel)_settingsWindow.DataContext);
            _settingsWindow.Activate();
            return;
        }

        SettingsViewModel settings = new(
            _settings,
            StartupRegistration.ForThisProcess(),
            resetPosition: ResetPlacement,
            recheckProviders: () => _ = _model.RefreshAsync(force: true),
            providers: _providers,
            globalHotkeyUnavailable: _globalHotkeyUnavailable);

        DiagnosticsViewModel diagnostics = new(
            _model.Providers,
            _providers,
            _refresh ?? new ProviderRefreshService(_providers, TimeSpan.Zero, TimeSpan.Zero),
            _environment,
            _startup,
            ThemeDescription(),
            DisplayScalingDescription(),
            clock: () => DateTimeOffset.Now,
            copyToClipboard: CopyToClipboard,
            openLogs: OpenLogsFolder);

        SettingsShellViewModel shell = new(_settings, settings, diagnostics);
        select(shell);

        _settingsWindow = new SettingsWindow(shell) { Owner = this };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;

        // The settings window feeds the same dismissal timer, so an outside click takes the pair
        // down whichever of them held the focus. Without this, focus leaving from the settings
        // window would never reach the widget's own OnDeactivated: the widget was deactivated when
        // the settings window opened, and a window that is already inactive is not deactivated again.
        _settingsWindow.Deactivated += (_, _) => _dismiss.Start();
        _settingsWindow.Activated += (_, _) => _dismiss.Stop();

        _settingsWindow.Show();
    }
```

Note the `SettingsViewModel` construction above already omits `openLogs` and `openDiagnostics`, which still exist at this point. **Add them back for now** — `openLogs: OpenLogsFolder, openDiagnostics: ShowDiagnostics,` — and Task 5 removes them.

- [ ] **Step 10: Collapse the dismissal**

At `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs:648-654`, replace the comment and the two `Close()` calls with:

```csharp
        // The owned window first: it is owned by the widget, and hiding an owner leaves an owned
        // window on screen as the only thing left of an application that has just gone away.
        _settingsWindow?.Close();
        HideToTray();
```

- [ ] **Step 11: Delete `DiagnosticsWindow`**

```bash
git rm src/AiUsageMonitor.App/Views/DiagnosticsWindow.xaml src/AiUsageMonitor.App/Views/DiagnosticsWindow.xaml.cs
```

- [ ] **Step 12: Run the full suite**

Run: `dotnet build`, then `dotnet test`

Expected: clean build, all tests pass. If the sidebar comes up empty, the `PagesView` binding is the suspect — check that `ItemsSource` is bound to `PagesView` and not to `Pages`.

- [ ] **Step 13: Commit**

```bash
git add -A
git commit -m "feat: give the settings window a category sidebar and fold diagnostics into it"
```

---

### Task 5: Retire the two dead constructor arguments

**Files:**
- Modify: `src/AiUsageMonitor.App/ViewModels/SettingsViewModel.cs`
- Modify: `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs`
- Modify: `tests/AiUsageMonitor.App.Tests/SettingsViewModelTests.cs`
- Modify: `tests/AiUsageMonitor.App.Tests/ProviderPreferenceViewModelTests.cs`
- Modify: `tests/AiUsageMonitor.App.Tests/ViewLoadingTests.cs`
- Modify: `tests/AiUsageMonitor.App.Tests/SettingsPageLoadingTests.cs`
- Modify: `tests/AiUsageMonitor.App.Tests/SettingsShellViewModelTests.cs`

**Interfaces:**
- Produces: `SettingsViewModel(SettingsService, StartupRegistration, Action resetPosition, Action recheckProviders, IReadOnlyList<ProviderDescriptor> providers, bool globalHotkeyUnavailable = false)`. `OpenLogsCommand` and `OpenDiagnosticsCommand` no longer exist on it.

`openDiagnostics` backed a button that is now navigation. `openLogs` backed a button that now lives on the Application diagnostics page against `DiagnosticsViewModel.OpenLogsCommand`, which already existed. Both are dead; leaving them wired to nothing is worse than removing them.

- [ ] **Step 1: Shrink the test that asserts the actions**

In `tests/AiUsageMonitor.App.Tests/SettingsViewModelTests.cs`, replace `TheActionsCallWhatTheyClaimTo` with:

```csharp
    [Fact]
    public void TheActionsCallWhatTheyClaimTo()
    {
        string path = Path.Combine(Path.GetTempPath(), "aium-vm-" + Guid.NewGuid().ToString("N"), "settings.json");
        SettingsService service = new(new AppSettingsStore(path), AppSettings.Default);
        int reset = 0, recheck = 0;
        SettingsViewModel model = new(
            service,
            new StartupRegistration(ScratchKey, "AiUsageMonitorTest", null),
            resetPosition: () => reset++,
            recheckProviders: () => recheck++,
            providers: Providers);

        model.ResetPositionCommand.Execute(null);
        model.RecheckProvidersCommand.Execute(null);

        Assert.Equal(1, reset);
        Assert.Equal(1, recheck);
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/AiUsageMonitor.App.Tests --filter FullyQualifiedName~TheActionsCallWhatTheyClaimTo`

Expected: FAIL to compile — the constructor still requires `openLogs` and `openDiagnostics`.

- [ ] **Step 3: Remove the parameters and their commands**

In `src/AiUsageMonitor.App/ViewModels/SettingsViewModel.cs`:
- delete the `Action openLogs` and `Action openDiagnostics` constructor parameters;
- delete the `OpenLogsCommand` and `OpenDiagnosticsCommand` property declarations (around line 221) and the two assignments in the constructor body (around line 94).

- [ ] **Step 4: Update every remaining call site**

Delete the `openLogs:` and `openDiagnostics:` argument lines from each of:
- `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs` (inside `ShowShell`)
- `tests/AiUsageMonitor.App.Tests/ProviderPreferenceViewModelTests.cs:130-131`
- `tests/AiUsageMonitor.App.Tests/SettingsViewModelTests.cs:41-42`
- `tests/AiUsageMonitor.App.Tests/ViewLoadingTests.cs` (in `Shell()`)
- `tests/AiUsageMonitor.App.Tests/SettingsPageLoadingTests.cs` (in `Model()`)
- `tests/AiUsageMonitor.App.Tests/SettingsShellViewModelTests.cs` (in `Shell()`)

Then search for stragglers:

```powershell
Select-String -Path src,tests -Include *.cs,*.xaml -Pattern "openDiagnostics|openLogs|OpenDiagnosticsCommand|OpenLogsCommand" -Recurse
```

The only surviving hits must be `DiagnosticsViewModel.OpenLogsCommand`, its `openLogs` constructor parameter, and the shell markup that binds it.

- [ ] **Step 5: Run the full suite**

Run: `dotnet build`, then `dotnet test`

Expected: clean build, all tests pass.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor: drop the settings actions that became navigation"
```

---

### Task 6: Amend the PRD and verify on screen

**Files:**
- Modify: `docs/PRD.md` §17 (line 524), §19 (lines 577, 582), §20

**Interfaces:** none.

- [ ] **Step 1: Replace the settings-window sizing requirement**

In `docs/PRD.md`, replace line 582 — the paragraph beginning *"The settings window must be tall enough to show every setting it offers at once"* — with:

```markdown
The settings window must present its settings as categories, each reachable in one click from a
sidebar that is always visible, with one category on screen at a time. It opens at a deliberate
default size rather than sizing itself to its content, is resizable, remembers the size the user
chose, and is capped by and kept inside the work area of the screen it opens on. Only the pane
showing the selected category scrolls.
```

- [ ] **Step 2: Reword the diagnostics bullet**

In §19, replace the bullet `- Open diagnostics.` with:

```markdown
- Reach diagnostics, as categories within the settings window rather than as a separate window.
```

- [ ] **Step 3: Tighten §17**

Replace the bullet at line 524 with:

```markdown
- **Settings window**, for configuration, and for diagnostics presented as categories within it.
```

- [ ] **Step 4: Add the diagnostics presentation rule to §20**

Append to §20, after the per-provider list:

```markdown
Diagnostics must be presented one provider per category, alongside a category for the application
itself, within the settings window. The copyable bundle remains whole-application and remains
redacted regardless of which category is on screen.
```

- [ ] **Step 5: Check nothing else in the PRD still promises the old shape**

```powershell
Select-String -Path docs/PRD.md -Pattern "diagnostics window|tall enough|sizes itself"
```

Expected: no hits. Fix any that appear.

- [ ] **Step 6: Full verification**

Run: `dotnet build`, then `dotnet test`

Expected: clean build, all tests pass.

Then launch it and look at it. A green suite proves the XAML parses and the pages measure; it proves nothing about whether the window looks right.

```powershell
dotnet run --project src/AiUsageMonitor.App
```

Open settings from the tray, click every sidebar entry, resize the window, close and reopen it to confirm the size came back, and press "Copy all diagnostics" while a provider page is showing to confirm the fields do not go stale. If a running instance holds the build output, build with `-p:BaseOutputPath=<scratch>/` rather than killing the user's widget.

- [ ] **Step 7: Commit**

```bash
git add docs/PRD.md
git commit -m "docs: require the settings window to present categories"
```

---

## Delegation note

Recommended for Codex: `--model gpt-5.6-terra --effort medium`. Tasks 1, 3, 5 and 6 are mechanical; the undetermined work is concentrated in Task 2 (the content indirection) and Task 4 (the shell markup plus eight migrated tests), and both are specified here down to the code. Raise to `high` only if Task 4's grouped `ListBox` fights back.

Task 4 is the one to watch on review: it is deliberately the largest, because every smaller boundary inside it leaves the build red.
