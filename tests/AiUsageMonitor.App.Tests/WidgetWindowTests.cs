using System.IO;
using System.Windows;
using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.App.Views;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Refresh;
using AiUsageMonitor.Infrastructure.Settings;

namespace AiUsageMonitor.App.Tests;

[Collection("wpf")]
public class WidgetWindowTests(WpfFixture wpf)
{
    private sealed class SilentProbe(string name) : IProviderProbe
    {
        public string Name => name;

        public Task<ProviderSnapshot> ProbeAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private static IReadOnlyList<ProviderDescriptor> Providers() =>
    [
        new("Claude Code", "CC", new SilentProbe("Claude Code")),
        new("Codex", "CX", new SilentProbe("Codex"))
    ];

    private static MainViewModel Model(IReadOnlyList<ProviderDescriptor> providers, AppSettings settings)
    {
        ProviderRefreshService service = new(providers, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60));
        return new MainViewModel(service, providers, settings, () => DateTimeOffset.Now);
    }

    private static SettingsService Settings(AppSettings settings)
    {
        string path = Path.Combine(Path.GetTempPath(), "aium-widget-" + Guid.NewGuid().ToString("N"), "settings.json");
        return new SettingsService(new AppSettingsStore(path), settings);
    }

    [Fact]
    public void TheWindowConstructsWithoutXamlErrors() => wpf.Invoke(() =>
    {
        // Constructing the window runs InitializeComponent, which is where a bad resource key or a
        // control whose static initializer throws surfaces - the failure that once shipped green.
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default);

        WidgetWindow window = new(model, Settings(AppSettings.Default));

        Assert.Equal(360, window.Width);
        Assert.Equal(520, window.MaxHeight);

        model.Dispose();
    });

    [Fact]
    public void AFirstRunWithNoSavedPlacementIsCentred() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default);

        WidgetWindow window = new(model, Settings(AppSettings.Default));

        Assert.Equal(WindowStartupLocation.CenterScreen, window.WindowStartupLocation);

        model.Dispose();
    });

    [Fact]
    public void APlacementOnAMonitorThatIsNoLongerThereFallsBackToCentring() => wpf.Invoke(() =>
    {
        // A position saved against a monitor that has since been unplugged would otherwise put the
        // window somewhere with no way to drag it back (PRD §17).
        AppSettings offscreen = AppSettings.Default with { WindowLeft = -30000, WindowTop = -30000 };
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, offscreen);

        WidgetWindow window = new(model, Settings(offscreen));

        Assert.Equal(WindowStartupLocation.CenterScreen, window.WindowStartupLocation);

        model.Dispose();
    });

    [Fact]
    public void TheFooterCountsTheProvidersItWasGiven() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default);

        Assert.Equal("2 providers", model.FooterText);

        model.Dispose();
    });
}
