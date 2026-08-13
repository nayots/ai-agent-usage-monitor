using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Reflection;
using AiUsageMonitor.App.Interop;
using AiUsageMonitor.App.Notifications;
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
    public void TheWidgetBuildsAUsageAlertWatcherThatIsQuietOnFirstObservation() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default);
        WidgetWindow window = new(model, Settings(AppSettings.Default));

        FieldInfo watcherField = typeof(WidgetWindow).GetField("_alerts", BindingFlags.Instance | BindingFlags.NonPublic)!;
        UsageAlertWatcher watcher = Assert.IsType<UsageAlertWatcher>(watcherField.GetValue(window));

        Assert.Empty(watcher.Observe(model.Providers));

        model.Dispose();
    });

    [Fact]
    public void NotificationFormattingFitsTheShellInformationBuffers()
    {
        (string title, string text) = TrayIcon.FormatNotification(new string('t', 64), new string('x', 256));

        Assert.Equal(new string('t', 63), title);
        Assert.Equal(new string('x', 255), text);
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

    /// <summary>
    /// The widget lives in the notification area and nowhere else. A taskbar button would be a
    /// second place to find it and a second thing to close, and the title bar drops its minimise
    /// button for the same reason - with nothing to minimise towards it could only do what the
    /// button beside it does.
    /// </summary>
    [Fact]
    public void TheWidgetKeepsNoTaskbarButton() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default);

        WidgetWindow window = new(model, Settings(AppSettings.Default));

        Assert.False(window.ShowInTaskbar);

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

    /// <summary>
    /// The title bar's pin writes the setting and reads the window's Topmost back, so this covers
    /// both halves at once: the settings window and the tray can move it too, and the pin has to
    /// follow whichever of them did.
    /// </summary>
    [Fact]
    public void ChangingAlwaysOnTopMovesTheWindowWhicheverSurfaceChangedIt() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default);
        SettingsService settings = Settings(AppSettings.Default);

        WidgetWindow window = new(model, settings);

        Assert.False(window.Topmost);

        settings.Update(s => s with { AlwaysOnTop = true });
        Assert.True(window.Topmost);

        settings.Update(s => s with { AlwaysOnTop = false });
        Assert.False(window.Topmost);

        model.Dispose();
    });

    /// <summary>
    /// The pin's two states differ by shape, not only by colour: an accent-tinted outline and an
    /// accent-tinted solid are the same mark to anyone under high contrast.
    /// </summary>
    [Fact]
    public void ThePinShowsAnOutlineWhenLooseAndASolidWhenKeptOnTop() => wpf.Invoke(() =>
    {
        Style style = (Style)Application.Current.Resources["TitleBarPinButtonStyle"];

        Assert.Equal("", Content(style.Setters));

        // XAML leaves a DataTrigger's Value a string - it cannot know the bound property's type at
        // parse time - so the comparison is against its text, not against a boolean.
        DataTrigger pinned = style.Triggers.OfType<DataTrigger>().Single();

        Assert.Equal("True", pinned.Value.ToString());
        Assert.Equal("", Content(pinned.Setters));
    });

    /// <summary>
    /// The widget is dismissed by the focus leaving the application, not by it leaving the widget:
    /// its own settings window, its tray menu and its tooltips are all places the focus is allowed
    /// to go.
    /// </summary>
    [Fact]
    public void FocusLandingOutsideTheApplicationDismissesTheWidget() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default);

        WidgetWindow window = new(model, Settings(AppSettings.Default));
        window.DismissIfFocusLeftTheApplication(focusStayedInTheApplication: false);

        Assert.Equal(Visibility.Hidden, window.Visibility);

        model.Dispose();
    });

    [Fact]
    public void FocusMovingBetweenTheApplicationsOwnWindowsLeavesTheWidgetAlone() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default);

        WidgetWindow window = new(model, Settings(AppSettings.Default));
        window.DismissIfFocusLeftTheApplication(focusStayedInTheApplication: true);

        Assert.NotEqual(Visibility.Hidden, window.Visibility);

        model.Dispose();
    });

    /// <summary>
    /// The pin's second job, and the one the dismissal would otherwise make impossible: a widget
    /// kept above other windows is being watched while another window is worked in.
    /// </summary>
    [Fact]
    public void APinnedWidgetStaysWhereItIsWhenTheFocusLeaves() => wpf.Invoke(() =>
    {
        AppSettings pinned = AppSettings.Default with { AlwaysOnTop = true };
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, pinned);
        SettingsService settings = Settings(pinned);

        WidgetWindow window = new(model, settings);
        window.DismissIfFocusLeftTheApplication(focusStayedInTheApplication: false);

        Assert.NotEqual(Visibility.Hidden, window.Visibility);

        // Unpinning is enough to make it dismissable again - the rule reads the setting each time
        // rather than caching what it was when the window was built.
        settings.Update(s => s with { AlwaysOnTop = false });
        window.DismissIfFocusLeftTheApplication(focusStayedInTheApplication: false);

        Assert.Equal(Visibility.Hidden, window.Visibility);

        model.Dispose();
    });

    /// <summary>
    /// Hiding the widget deactivates it, which brings the question back a moment later. Answering
    /// it twice would, on a first run, say where the widget went in a second balloon - so the
    /// evidence here is the flag that records the balloon, not the visibility that both share.
    /// </summary>
    [Fact]
    public void AWidgetAlreadyHiddenIsNotDismissedTwice() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default);
        SettingsService settings = Settings(AppSettings.Default);

        WidgetWindow window = new(model, settings) { Visibility = Visibility.Hidden };
        window.DismissIfFocusLeftTheApplication(focusStayedInTheApplication: false);

        Assert.False(settings.Current.TrayHintShown);

        model.Dispose();
    });

    /// <summary>
    /// The wiring between losing the focus and answering for it. The delay is the point: the focus
    /// can come back to this application a moment after it leaves - a click on the notification
    /// area icon reaches the shell before it reaches the widget - so an activation that arrives
    /// while the timer is running has to call the whole thing off.
    /// </summary>
    [Fact]
    public void LosingTheFocusArmsTheDismissalAndRegainingItCallsItOff() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default);

        WidgetWindow window = new(model, Settings(AppSettings.Default));
        DispatcherTimer dismiss = (DispatcherTimer)typeof(WidgetWindow)
            .GetField("_dismiss", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;

        Assert.False(dismiss.IsEnabled);

        Raise(window, "OnDeactivated");
        Assert.True(dismiss.IsEnabled);

        Raise(window, "OnActivated");
        Assert.False(dismiss.IsEnabled);

        model.Dispose();
    });

    /// <summary>
    /// Window raises activation through protected virtual methods rather than through anything a
    /// caller can invoke, so this is how a test reaches the override under test.
    /// </summary>
    private static void Raise(Window window, string method) => typeof(Window)
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic, [typeof(EventArgs)])!
        .Invoke(window, [EventArgs.Empty]);

    [Fact]
    public void TheTitleBarIconsUseAFontThatFallsBackOnOlderWindows() => wpf.Invoke(() =>
    {
        FontFamily font = (FontFamily)Application.Current.Resources["WidgetIconFontFamily"];

        Assert.Equal("Segoe Fluent Icons, Segoe MDL2 Assets", font.Source);
    });

    private static string? Content(IEnumerable<SetterBase> setters) => setters
        .OfType<Setter>()
        .Where(setter => setter.Property == ContentControl.ContentProperty)
        .Select(setter => setter.Value as string)
        .SingleOrDefault();

    [Fact]
    public void TheFooterCountsTheProvidersItWasGiven() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default);

        Assert.Equal("2 providers", model.FooterText);

        model.Dispose();
    });

    private static FrameworkElement Content(WidgetWindow window)
    {
        FrameworkElement content = (FrameworkElement)window.Content;
        content.Measure(new Size(360, 520));
        content.Arrange(new Rect(0, 0, 360, 520));
        content.UpdateLayout();
        return content;
    }

    [Fact]
    public void TheChromeShrinksAtCompactDensity() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default with { Density = WidgetDensity.Compact });
        WidgetWindow window = new(model, Settings(AppSettings.Default with { Density = WidgetDensity.Compact }));
        FrameworkElement content = Content(window);
        Assert.Equal(28d, ((FrameworkElement)window.FindName("TitleBar")).Height);
        Assert.Equal(24d, ((FrameworkElement)window.FindName("Footer")).Height);
        Assert.Equal(new Thickness(8, 0, 8, 2), ((FrameworkElement)window.FindName("ProviderList")).Margin);
        Assert.True(content.DesiredSize.Height > 0);
        model.Dispose();
    });

    [Fact]
    public void TheChromeKeepsItsFullSizeAtStandardDensity() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default);

        WidgetWindow window = new(model, Settings(AppSettings.Default));
        Content(window);

        Assert.Equal(32d, ((FrameworkElement)window.FindName("TitleBar")).Height);
        Assert.Equal(26d, ((FrameworkElement)window.FindName("Footer")).Height);
        Assert.Equal(new Thickness(10, 0, 10, 2), ((FrameworkElement)window.FindName("ProviderList")).Margin);

        model.Dispose();
    });

    [Fact]
    public void TheWholeWidgetIsShorterAtCompactDensity() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> standardProviders = Providers();
        MainViewModel standardModel = Model(standardProviders, AppSettings.Default);
        WidgetWindow standardWindow = new(standardModel, Settings(AppSettings.Default));
        double standard = Content(standardWindow).DesiredSize.Height;
        AppSettings compactSettings = AppSettings.Default with { Density = WidgetDensity.Compact };
        IReadOnlyList<ProviderDescriptor> compactProviders = Providers();
        MainViewModel compactModel = Model(compactProviders, compactSettings);
        WidgetWindow compactWindow = new(compactModel, Settings(compactSettings));
        double compact = Content(compactWindow).DesiredSize.Height;
        Assert.True(compact < standard, $"compact measured {compact} against standard {standard}");
        standardModel.Dispose();
        compactModel.Dispose();
    });

    [Fact]
    public void ChangingDensityMovesTheChromeWithoutRebuildingTheWindow() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default);
        SettingsService settings = Settings(AppSettings.Default);
        WidgetWindow window = new(model, settings);
        Content(window);
        Assert.Equal(32d, ((FrameworkElement)window.FindName("TitleBar")).Height);
        settings.Update(s => s with { Density = WidgetDensity.Compact });
        Content(window);
        Assert.Equal(28d, ((FrameworkElement)window.FindName("TitleBar")).Height);
        model.Dispose();
    });
}
