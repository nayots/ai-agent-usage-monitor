using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AiUsageMonitor.App.Interop;
using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.App.Views;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Diagnostics;
using AiUsageMonitor.Infrastructure.Refresh;
using AiUsageMonitor.Infrastructure.Settings;

namespace AiUsageMonitor.App.Tests;

[Collection("wpf")]
public class ViewLoadingTests(WpfFixture wpf)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed class SilentProbe(string name) : IProviderProbe
    {
        public string Name => name;
        public string Mechanism => "fake";
        public MechanismTier Tier => MechanismTier.Official;

        public Task<ProviderSnapshot> ProbeAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private static QuotaWindow Window(double? used, bool token, bool withReset) => new(
        Id: token ? "nimbus_quill" : "five_hour",
        Label: token ? "nimbus_quill" : "5-hour window",
        UsedPercent: used,
        ResetsAt: withReset ? Now.AddHours(4) : null,
        WindowDuration: withReset ? TimeSpan.FromHours(5) : null,
        Order: 0,
        IsPartial: !withReset,
        Extra: new Dictionary<string, string>(),
        LabelIsProviderToken: token);

    private static ProviderSnapshot Snapshot(ConnectionState state, IReadOnlyList<QuotaWindow> windows) => new(
        ProviderName: "Claude Code", Installed: true, Version: "2.1.227", ExecutablePath: null,
        State: state, Mechanism: "stub", Tier: MechanismTier.Unofficial, UpdateModel: "pull (poll)",
        Windows: windows, RetrievedAt: state == ConnectionState.NotInstalled ? null : Now,
        Error: null, Notes: []);

    [Theory]
    // The d suffixes are load-bearing. xUnit boxes an InlineData literal at its written type and
    // hands it to the reflection binder, which does no numeric widening: a bare 47 arrives as an
    // Int32 and fails to bind to double? at run time, not compile time. Only the null case would
    // survive.
    [InlineData(47d, false, true)]
    [InlineData(100d, false, true)]
    [InlineData(34d, true, false)]
    [InlineData(null, false, false)]
    public void EveryRowFormRendersWithoutThrowing(double? used, bool token, bool withReset) => wpf.Invoke(() =>
    {
        QuotaRowViewModel row = new(Window(used, token, withReset), colorBarsByUsage: true);
        row.Tick(Now);

        ControlLoadingTests.Measured(new QuotaRowView { DataContext = row, Width = 320 });
    });

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

    [Fact]
    public void TrayMenuContainsDiagnostics() => wpf.Invoke(() =>
    {
        ProviderDescriptor provider = new("claude-code", "Claude Code", "CC", new SilentProbe("Claude Code"));
        ProviderRefreshService refresh = new([provider], TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1));
        string path = Path.Combine(Path.GetTempPath(), "aium-tray-" + Guid.NewGuid().ToString("N"), "settings.json");
        SettingsService settings = new(new AppSettingsStore(path), AppSettings.Default);
        WidgetWindow window = new(new MainViewModel(refresh, [provider], settings.Current, () => Now), settings, refresh);

        ContextMenu menu = Assert.IsType<ContextMenu>(window.Resources["TrayMenu"]);
        Assert.Contains(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, "Diagnostics"));
    });

    [Theory]
    [InlineData(ConnectionState.Connected)]
    [InlineData(ConnectionState.Stale)]
    [InlineData(ConnectionState.NotInstalled)]
    [InlineData(ConnectionState.Unavailable)]
    [InlineData(ConnectionState.Error)]
    [InlineData(ConnectionState.Waiting)]
    [InlineData(ConnectionState.Unsupported)]
    [InlineData(ConnectionState.Discovering)]
    public void EveryCardStateRendersWithoutThrowing(ConnectionState state) => wpf.Invoke(() =>
    {
        ProviderCardViewModel card = new(
            new ProviderDescriptor("claude-code", "Claude Code", "CC", new SilentProbe("Claude Code")),
            colorBarsByUsage: true,
            _ => { });
        card.Apply(Snapshot(state, [Window(47, false, true), Window(34, true, false)]), Now, FreshnessPolicy.Default);

        ControlLoadingTests.Measured(new ProviderCardView { DataContext = card, Width = 340 });
    });

    [Fact]
    public void TheTimestampLineDropsItsSeparatorWhenThereIsNothingToTimestamp() => wpf.Invoke(() =>
    {
        // NotInstalled never has a RetrievedAt, so there is no timestamp. Rendering the separator
        // regardless would leave a dangling "Not installed ·" - and on a machine where one of the
        // two providers simply is not present, that is the first thing the user ever sees.
        ProviderCardViewModel card = new(
            new ProviderDescriptor("codex", "Codex", "CX", new SilentProbe("Codex")),
            colorBarsByUsage: true,
            _ => { });
        card.Apply(Snapshot(ConnectionState.NotInstalled, []), Now, FreshnessPolicy.Default);

        ProviderCardView view = ControlLoadingTests.Measured(new ProviderCardView { DataContext = card, Width = 340 });

        Assert.Equal(Visibility.Collapsed, ((TextBlock)view.FindName("TimestampLine")).Visibility);
    });

    [Fact]
    public void TheTimestampLineKeepsItsSeparatorWhenThereIsATimestamp() => wpf.Invoke(() =>
    {
        ProviderCardViewModel card = new(
            new ProviderDescriptor("claude-code", "Claude Code", "CC", new SilentProbe("Claude Code")),
            colorBarsByUsage: true,
            _ => { });
        card.Apply(Snapshot(ConnectionState.Connected, [Window(47d, false, true)]), Now, FreshnessPolicy.Default);

        ProviderCardView view = ControlLoadingTests.Measured(new ProviderCardView { DataContext = card, Width = 340 });

        TextBlock updated = (TextBlock)view.FindName("TimestampLine");
        Assert.Equal(Visibility.Visible, updated.Visibility);
        Assert.Equal("· Updated 0s ago", updated.Text);
    });

    [Fact]
    public void AnExhaustedRowStatesItsResetTimeOnceNotTwice() => wpf.Invoke(() =>
    {
        // The alert line under the bar used to repeat the countdown that the RESETS IN column
        // already carries, three columns to the right of it, so a limit-reached row put the same
        // value on screen twice.
        QuotaRowViewModel row = new(Window(100d, false, true), colorBarsByUsage: true);
        row.Tick(Now);

        QuotaRowView view = ControlLoadingTests.Measured(new QuotaRowView { DataContext = row, Width = 320 });

        Assert.False(string.IsNullOrWhiteSpace(row.CountdownText));
        Assert.Equal(1, Texts(view).Count(text => text.Contains(row.CountdownText!, StringComparison.Ordinal)));
    });

    [Fact]
    public void AFreshExhaustedRowStatesItsLimitAtFullStrength() => wpf.Invoke(() =>
    {
        QuotaRowViewModel row = new(Window(100d, false, true), colorBarsByUsage: true);
        row.Tick(Now);

        QuotaRowView view = ControlLoadingTests.Measured(new QuotaRowView { DataContext = row, Width = 320 });

        Assert.Same(view.FindResource("TextPrimaryBrush"), Named(view, "CountdownCell").Foreground);
        Assert.Same(view.FindResource("StateBadBrush"), Named(view, "LimitReachedText").Foreground);
        Assert.Same(view.FindResource("StateBadBrush"), Named(view, "LimitReachedMark").Foreground);
    });

    [Fact]
    public void AStaleExhaustedRowGreysItsAlertAlongWithEverythingElse() => wpf.Invoke(() =>
    {
        // A row can be stale and exhausted at once. It used to grey its label and percentage while
        // leaving the countdown at full strength and "Limit reached" at full-saturation red - the
        // loudest claim on the card shouting from data the card itself says may be out of date.
        QuotaRowViewModel row = new(Window(100d, false, true), colorBarsByUsage: true) { IsStale = true };
        row.Tick(Now);

        QuotaRowView view = ControlLoadingTests.Measured(new QuotaRowView { DataContext = row, Width = 320 });

        object greyed = view.FindResource("TextTertiaryBrush");
        Assert.Same(greyed, Named(view, "CountdownCell").Foreground);
        Assert.Same(greyed, Named(view, "LimitReachedText").Foreground);
        Assert.Same(greyed, Named(view, "LimitReachedMark").Foreground);
    });

    private static TextBlock Named(FrameworkElement view, string name) => (TextBlock)view.FindName(name);

    /// <summary>Every string this element actually puts on screen, in visual-tree order.</summary>
    private static IEnumerable<string> Texts(DependencyObject root)
    {
        if (root is TextBlock block)
        {
            yield return block.Text;
        }

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            foreach (string text in Texts(VisualTreeHelper.GetChild(root, i)))
            {
                yield return text;
            }
        }
    }

    [Fact]
    public void ACardWithNoWindowsStillRenders() => wpf.Invoke(() =>
    {
        ProviderCardViewModel card = new(
            new ProviderDescriptor("codex", "Codex", "CX", new SilentProbe("Codex")),
            colorBarsByUsage: false,
            _ => { });
        card.Apply(Snapshot(ConnectionState.Connected, []), Now, FreshnessPolicy.Default);

        FrameworkElement view = ControlLoadingTests.Measured(new ProviderCardView { DataContext = card, Width = 340 });
        Assert.True(view.ActualHeight > 0);
    });

    [Theory]
    [InlineData("Themes/Light.xaml")]
    [InlineData("Themes/Dark.xaml")]
    [InlineData("Themes/HighContrast.xaml")]
    public void TheSettingsWindowRendersInEveryPalette(string palette) => wpf.Invoke(() =>
    {
        ResourceDictionary dictionary = new()
        {
            Source = new Uri($"pack://application:,,,/AiUsageMonitor.App;component/{palette}", UriKind.Absolute)
        };
        Application.Current.Resources.MergedDictionaries.Add(dictionary);

        try
        {
            // Measured on the window's content, not the window. A WPF Window's own DesiredSize
            // stays zero until it has an HWND, so measuring the Window asserts nothing. Measuring
            // its content still forces every template to expand and every DynamicResource in this
            // palette to resolve, which is the whole point of the test.
            Assert.True(SettingsContent().DesiredSize.Height > 0);
        }
        finally
        {
            Application.Current.Resources.MergedDictionaries.Remove(dictionary);
        }
    });

    /// <summary>
    /// Pinning is offered by the title bar and nowhere else: it lasts only as long as the session,
    /// and a settings window promises the opposite. Asserted against the rendered window rather
    /// than against the markup, because a binding left behind for a property that no longer exists
    /// fails silently - no exception and no warning, just a checkbox that does nothing.
    /// </summary>
    [Fact]
    public void TheSettingsWindowOffersNoPinning() => wpf.Invoke(() =>
    {
        FrameworkElement content = SettingsContent("Window");

        Assert.DoesNotContain(
            Descendants(content).OfType<CheckBox>(),
            box => box.Content is string label && label.Contains("top", StringComparison.OrdinalIgnoreCase));

        // Removing a control silently is its own failure: someone who goes looking for the setting
        // has to be told where it went.
        Assert.Contains(Texts(content), text => text.StartsWith("Pinning is on the widget's title bar", StringComparison.Ordinal));
    });

    /// <summary>
    /// The dock choices are only meaningful once mini mode is on, and the enabling chain has to
    /// actually resolve at run time for that to be true on screen.
    /// </summary>
    [Fact]
    public void TheSettingsWindowLoadsMiniModeWithItsDockFollowingIt() => wpf.Invoke(() =>
    {
        SettingsShellViewModel shell = Shell();
        SettingsWindow window = new(shell);
        FrameworkElement content = (FrameworkElement)window.Content;
        content.Measure(new Size(740, 560));
        content.UpdateLayout();
        SettingsViewModel model = shell.Settings;

        Assert.Contains(
            Descendants(content).OfType<CheckBox>(),
            box => AutomationProperties.GetName(box) == "Mini mode");
        Assert.Contains("A one-line strip pinned to a screen edge, above other windows. Click it to bring the full widget back.", Texts(content));

        RadioButton bottom = Descendants(content).OfType<RadioButton>()
            .First(button => button.GroupName == "mini-dock" && AutomationProperties.GetName(button) == "Bottom");

        Assert.False(bottom.IsEnabled);

        model.MiniMode = true;
        content.UpdateLayout();
        Assert.True(bottom.IsEnabled);

        shell.Dispose();
    });

    [Fact]
    public void TheSettingsWindowLoadsTheNotificationControls() => wpf.Invoke(() =>
    {
        FrameworkElement content = SettingsContent("Notifications");

        Assert.Contains("Tell me when a window passes", Texts(content));
        Assert.Contains(
            Descendants(content).OfType<RadioButton>(),
            button => AutomationProperties.GetName(button) == "100% only");
        Assert.Contains(
            Descendants(content).OfType<CheckBox>(),
            box => AutomationProperties.GetName(box) == "Quiet hours");
        Assert.Contains("100% always notifies, and is the only alert that makes a sound.", Texts(content));
    });

    /// <summary>
    /// A schedule for notifications that are switched off entirely is a control that does nothing,
    /// and the quiet-hours times are a schedule for a schedule. Both follow the switch above them,
    /// which is only true if the enabling chain actually resolves at run time.
    /// </summary>
    [Fact]
    public void TheQuietHoursTimesFollowTheirOwnCheckboxAndTheNotificationsSwitch() => wpf.Invoke(() =>
    {
        SettingsShellViewModel shell = Shell();
        SettingsWindow window = new(shell);
        shell.SelectedPage = shell.Pages.Single(entry => entry.Title == "Notifications");
        FrameworkElement content = (FrameworkElement)window.Content;
        content.Measure(new Size(740, 560));
        content.UpdateLayout();
        SettingsViewModel model = shell.Settings;

        RadioButton start = Descendants(content).OfType<RadioButton>()
            .First(button => button.GroupName == "quiet-start");

        Assert.False(start.IsEnabled);

        model.QuietHoursEnabled = true;
        content.UpdateLayout();
        Assert.True(start.IsEnabled);

        model.NotifyOnQuotaEvents = false;
        content.UpdateLayout();
        Assert.False(start.IsEnabled);

        shell.Dispose();
    });

    [Fact]
    public void TheSettingsWindowLoadsProviderPreferences() => wpf.Invoke(() =>
    {
        FrameworkElement content = SettingsContent("Providers");
        Assert.Contains(
            Descendants(content).OfType<CheckBox>(),
            box => AutomationProperties.GetName(box) == "Show Claude Code");
        Assert.Contains(
            Descendants(content).OfType<Button>(),
            button => AutomationProperties.GetName(button) == "Move Codex down");
    });

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
    /// <para>
    /// Asserted on <c>Visibility</c>, not on presence in the visual tree: the footer is always in
    /// the tree and collapses, so a <c>DoesNotContain</c> over descendants can never fail however
    /// wrong the markup is. Not on <c>IsVisible</c> either - that is false for every element in a
    /// tree which has only been measured and arranged and never shown, so it would pass for the
    /// wrong reason. The <c>Visibility</c> property is what the DataTriggers actually set.
    /// </para>
    /// </summary>
    [Fact]
    public void TheLogsFolderIsOfferedOnTheApplicationPageAndNoOther() => wpf.Invoke(() =>
    {
        FrameworkElement provider = SettingsContent("Claude Code");
        Assert.Equal(Visibility.Visible, DiagnosticsActions(provider).Visibility);
        Assert.Equal(Visibility.Visible, ActionButton(provider, "Copy all diagnostics").Visibility);
        Assert.Equal(Visibility.Collapsed, ActionButton(provider, "Open logs folder").Visibility);

        FrameworkElement application = SettingsContent("Application");
        Assert.Equal(Visibility.Visible, DiagnosticsActions(application).Visibility);
        Assert.Equal(Visibility.Visible, ActionButton(application, "Copy all diagnostics").Visibility);
        Assert.Equal(Visibility.Visible, ActionButton(application, "Open logs folder").Visibility);
    });

    [Fact]
    public void ASettingsPageOffersNeitherOfTheDiagnosticsActions() => wpf.Invoke(() =>
    {
        Assert.Equal(Visibility.Collapsed, DiagnosticsActions(SettingsContent()).Visibility);
    });

    /// <summary>The shell's diagnostics footer, which is present on every page and collapses.</summary>
    private static FrameworkElement DiagnosticsActions(FrameworkElement content) =>
        Descendants(content).OfType<FrameworkElement>().First(element => element.Name == "DiagnosticsActions");

    private static Button ActionButton(FrameworkElement content, string automationName) =>
        Descendants(content).OfType<Button>().First(button => AutomationProperties.GetName(button) == automationName);

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

    /// <summary>
    /// A window tall enough to show everything is also tall enough to hang off the bottom of the
    /// screen, and CenterOwner will happily put it there when the widget it centres on is parked
    /// near an edge. Settings the user cannot reach are no better than settings behind a scrollbar.
    /// </summary>
    [Fact]
    public void TheSettingsWindowIsNudgedBackInsideTheScreen() => wpf.Invoke(() =>
    {
        Rect screen = SystemParameters.WorkArea;
        SettingsWindow window = Shown(left: screen.Right - 40, top: screen.Bottom - 40);

        try
        {
            Assert.True(
                window.Top + window.ActualHeight <= screen.Bottom + 0.5,
                $"the window runs from {window.Top} to {window.Top + window.ActualHeight}, past {screen.Bottom}");
            Assert.True(
                window.Left + window.ActualWidth <= screen.Right + 0.5,
                $"the window runs from {window.Left} to {window.Left + window.ActualWidth}, past {screen.Right}");
        }
        finally
        {
            window.Close();
        }
    });

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

    private static ProviderCardViewModel Card(ConnectionState state, bool compact)
    {
        ProviderCardViewModel card = new(
            new ProviderDescriptor("claude-code", "Claude Code", "CC", new SilentProbe("Claude Code")),
            colorBarsByUsage: true,
            _ => { })
        {
            IsCompact = compact
        };
        card.Apply(Snapshot(state, [Window(47d, false, true), Window(62d, false, true)]), Now, FreshnessPolicy.Default);
        return card;
    }

    private static ProviderCardView Rendered(ProviderCardViewModel card) =>
        ControlLoadingTests.Measured(new ProviderCardView { DataContext = card, Width = 340 });

    [Fact]
    public void ACompactCardIsShorterThanTheSameCardAtStandardDensity() => wpf.Invoke(() =>
    {
        double standard = Rendered(Card(ConnectionState.Connected, compact: false)).DesiredSize.Height;
        double compact = Rendered(Card(ConnectionState.Connected, compact: true)).DesiredSize.Height;

        Assert.True(compact < standard, $"compact measured {compact} against standard {standard}");
    });

    [Fact]
    public void ACompactCardDropsItsMonogramAndVersion() => wpf.Invoke(() =>
    {
        ProviderCardView standard = Rendered(Card(ConnectionState.Connected, compact: false));
        Assert.Equal(Visibility.Visible, ((FrameworkElement)standard.FindName("Monogram")).Visibility);
        Assert.Equal(Visibility.Visible, ((FrameworkElement)standard.FindName("VersionLine")).Visibility);

        ProviderCardView compact = Rendered(Card(ConnectionState.Connected, compact: true));
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)compact.FindName("Monogram")).Visibility);
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)compact.FindName("VersionLine")).Visibility);
    });

    [Fact]
    public void ACompactConnectedCardDropsItsStatusLineForASpacer() => wpf.Invoke(() =>
    {
        ProviderCardView view = Rendered(Card(ConnectionState.Connected, compact: true));

        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("StatusLine")).Visibility);
        Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("CompactHeaderSpacer")).Visibility);
    });

    [Theory]
    [InlineData(ConnectionState.Error)]
    [InlineData(ConnectionState.Stale)]
    [InlineData(ConnectionState.Unavailable)]
    public void ACompactCardWithAProblemKeepsItsStatusLine(ConnectionState state) => wpf.Invoke(() =>
    {
        ProviderCardView view = Rendered(Card(state, compact: true));

        Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("StatusLine")).Visibility);
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("CompactHeaderSpacer")).Visibility);
    });

    [Fact]
    public void TheColumnCaptionsSurviveCompactDensity() => wpf.Invoke(() =>
    {
        ProviderCardView view = Rendered(Card(ConnectionState.Connected, compact: true));

        Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("ColumnCaptions")).Visibility);
        Assert.Contains("USED", Texts(view));
    });

    [Fact]
    public void RowsInsideACompactCardTakeTheTighterPadding() => wpf.Invoke(() =>
    {
        Thickness standard = FirstRowPadding(Rendered(Card(ConnectionState.Connected, compact: false)));
        Thickness compact = FirstRowPadding(Rendered(Card(ConnectionState.Connected, compact: true)));

        Assert.Equal(new Thickness(0, 6, 0, 5), standard);
        Assert.Equal(new Thickness(0, 4, 0, 4), compact);
    });

    [Fact]
    public void ARowWithNoCardAboveItFallsBackToStandardPadding() => wpf.Invoke(() =>
    {
        QuotaRowViewModel row = new(Window(47d, false, true), colorBarsByUsage: true);
        row.Tick(Now);

        QuotaRowView view = ControlLoadingTests.Measured(new QuotaRowView { DataContext = row, Width = 320 });

        Assert.Equal(new Thickness(0, 6, 0, 5), ((Border)view.FindName("RowFrame")).Padding);
    });

    private static Thickness FirstRowPadding(ProviderCardView card) =>
        Descendants(card).OfType<QuotaRowView>()
            .Select(row => ((Border)row.FindName("RowFrame")).Padding)
            .First();

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            yield return child;

            foreach (DependencyObject descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
