using System.IO;
using System.Linq;
using AiUsageMonitor.App.Interop;
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
            openLogs: () => { },
            openDiagnostics: () => { },
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

        Assert.All(shell.Pages.Where(page => !page.IsDiagnostics), page => Assert.Same(shell.Settings, page.Content));

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
