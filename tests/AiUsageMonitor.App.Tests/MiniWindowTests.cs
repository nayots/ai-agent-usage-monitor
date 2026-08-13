using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.App.Views;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Refresh;
using AiUsageMonitor.Infrastructure.Settings;

namespace AiUsageMonitor.App.Tests;

[Collection("wpf")]
public class MiniWindowTests(WpfFixture wpf)
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

    private static MainViewModel Model(IReadOnlyList<ProviderDescriptor> providers)
    {
        ProviderRefreshService service = new(providers, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60));
        return new MainViewModel(service, providers, AppSettings.Default, () => Now);
    }

    private static SettingsService Settings()
    {
        string path = Path.Combine(Path.GetTempPath(), "aium-mini-" + Guid.NewGuid().ToString("N"), "settings.json");
        return new SettingsService(new AppSettingsStore(path), AppSettings.Default);
    }

    private static ProviderSnapshot Snapshot(IReadOnlyList<QuotaWindow> windows) => new(
        ProviderName: "Claude Code", Installed: true, Version: null, ExecutablePath: null,
        State: ConnectionState.Connected, Mechanism: "stub", Tier: MechanismTier.Unofficial,
        UpdateModel: "pull (poll)", Windows: windows, RetrievedAt: Now, Error: null, Notes: []);

    private static QuotaWindow Window(double? used) => new(
        Id: "five_hour", Label: "5-hour", UsedPercent: used, ResetsAt: Now.AddHours(4),
        WindowDuration: TimeSpan.FromHours(5), Order: 0, IsPartial: false,
        Extra: new Dictionary<string, string>(), LabelIsProviderToken: false);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (DependencyObject nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    private static FrameworkElement Rendered(MainViewModel model, SettingsService settings)
    {
        MiniWindow window = new(model, settings);
        FrameworkElement content = (FrameworkElement)window.Content;
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        content.Arrange(new Rect(content.DesiredSize));
        content.UpdateLayout();
        return content;
    }

    [Fact]
    public void TheStripLoadsWithAPopulatedModel() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers);
        model.Providers[0].Apply(Snapshot([Window(62)]), Now, FreshnessPolicy.Default);

        Assert.True(Rendered(model, Settings()).ActualWidth > 0);

        model.Dispose();
    });

    /// <summary>
    /// A provider with no window at all must read as "nothing to report", never as a limit that is
    /// completely unused. The bar carries a null percentage for the same reason - an empty track,
    /// not a zero-width fill that a glance cannot tell apart from 0%.
    /// </summary>
    [Fact]
    public void AProviderWithNoPrimaryWindowShowsADashRatherThanZero() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers);
        model.Providers[0].Apply(Snapshot([]), Now, FreshnessPolicy.Default);

        Assert.Null(model.Providers[0].PrimaryWindow);

        string[] texts = [.. Descendants(Rendered(model, Settings()))
            .OfType<TextBlock>()
            .Select(block => block.Text)];

        Assert.Contains("—", texts);
        Assert.DoesNotContain("0%", texts);

        model.Dispose();
    });

    /// <summary>
    /// PRD §28 calls this a one-line strip. Between 24 and 32 DIP is what "one line" means here:
    /// tall enough to read, short enough that docking it costs a line of screen and not a band.
    /// </summary>
    [Fact]
    public void TheStripIsOneLineTall() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers);
        model.Providers[0].Apply(Snapshot([Window(62)]), Now, FreshnessPolicy.Default);

        double height = Rendered(model, Settings()).DesiredSize.Height;

        Assert.InRange(height, 24, 32);

        model.Dispose();
    });

    /// <summary>
    /// The strip is a projection of the cards, so a provider the user hid is not on it either.
    /// </summary>
    [Fact]
    public void AHiddenProviderIsNotOnTheStrip() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers);
        model.Providers[0].Apply(Snapshot([Window(62)]), Now, FreshnessPolicy.Default);
        model.Providers[0].IsHiddenByUser = true;

        FrameworkElement content = Rendered(model, Settings());

        Assert.Contains(
            Descendants(content).OfType<ContentPresenter>(),
            presenter => presenter.DataContext == model.Providers[0] && presenter.Visibility != Visibility.Visible);

        model.Dispose();
    });
}
