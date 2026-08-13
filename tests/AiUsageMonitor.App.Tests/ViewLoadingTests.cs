using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AiUsageMonitor.App.Interop;
using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.App.Views;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;
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
            new ProviderDescriptor("Claude Code", "CC", new SilentProbe("Claude Code")),
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
            new ProviderDescriptor("Codex", "CX", new SilentProbe("Codex")),
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
            new ProviderDescriptor("Claude Code", "CC", new SilentProbe("Claude Code")),
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
            new ProviderDescriptor("Codex", "CX", new SilentProbe("Codex")),
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
        FrameworkElement content = SettingsContent();

        Assert.DoesNotContain(
            Descendants(content).OfType<CheckBox>(),
            box => box.Content is string label && label.Contains("top", StringComparison.OrdinalIgnoreCase));

        // Removing a control silently is its own failure: someone who goes looking for the setting
        // has to be told where it went.
        Assert.Contains(Texts(content), text => text.StartsWith("Pinning is on the widget's title bar", StringComparison.Ordinal));
    });

    /// <summary>
    /// Everything this window offers fits in it, on any screen with the room. A settings window is
    /// short enough to read at a glance or it is not worth having, and it was not: a cap written
    /// into the markup put a scrollbar over the last two sections on a screen with hundreds of
    /// spare pixels. The escape clause is the screen genuinely too short to hold the content, where
    /// the window is at its cap and the ScrollViewer is doing the only thing left to do.
    /// </summary>
    [Fact]
    public void TheSettingsWindowShowsEverySettingWithoutScrolling() => wpf.Invoke(() =>
    {
        SettingsWindow window = Shown();

        try
        {
            ScrollViewer viewer = (ScrollViewer)window.Content;

            Assert.True(
                viewer.ScrollableHeight == 0 || window.ActualHeight >= window.MaxHeight,
                $"{viewer.ExtentHeight} of content in a viewport of {viewer.ViewportHeight}: the "
                    + $"window is {window.ActualHeight} tall against a cap of {window.MaxHeight}");
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

    /// <summary>
    /// The settings window on screen, which is the only way to ask it how tall it ended up: a WPF
    /// window measures to nothing until it has a handle, and the scrollbar these tests are about
    /// exists only once the ScrollViewer has a real viewport to compare its content against.
    /// Transparent and unactivated, so a test run neither flashes a window at whoever is at the
    /// machine nor takes the focus off what they were doing.
    /// </summary>
    private static SettingsWindow Shown(double left = -4000, double top = -4000)
    {
        SettingsWindow window = new(SettingsModel())
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

    private static FrameworkElement SettingsContent()
    {
        SettingsWindow window = new(SettingsModel());
        FrameworkElement content = (FrameworkElement)window.Content;
        content.Measure(new Size(380, 640));
        content.Arrange(new Rect(0, 0, 380, 640));
        content.UpdateLayout();
        return content;
    }

    private static SettingsViewModel SettingsModel()
    {
        string path = Path.Combine(Path.GetTempPath(), "aium-view-" + Guid.NewGuid().ToString("N"), "settings.json");

        return new SettingsViewModel(
            new SettingsService(new AppSettingsStore(path), AppSettings.Default),
            new StartupRegistration(@"Software\AiUsageMonitor\tests\ViewLoading", "AiUsageMonitorTest", null),
            resetPosition: () => { },
            recheckProviders: () => { },
            openLogs: () => { });
    }

    private static ProviderCardViewModel Card(ConnectionState state, bool compact)
    {
        ProviderCardViewModel card = new(
            new ProviderDescriptor("Claude Code", "CC", new SilentProbe("Claude Code")),
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
