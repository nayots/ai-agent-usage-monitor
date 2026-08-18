using System.ComponentModel;
using System.Windows.Data;
using AiUsageMonitor.Infrastructure.Diagnostics;
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
            SettingsPage(SettingsPageKind.Refresh, "Refresh"),
            SettingsPage(SettingsPageKind.Updates, "Updates")
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
