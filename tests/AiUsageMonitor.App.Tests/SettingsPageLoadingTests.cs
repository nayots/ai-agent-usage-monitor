using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AiUsageMonitor.App.Interop;
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
