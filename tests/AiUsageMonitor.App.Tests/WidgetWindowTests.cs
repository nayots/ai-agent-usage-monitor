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
        public string Mechanism => "fake";
        public MechanismTier Tier => MechanismTier.Official;

        public Task<ProviderSnapshot> ProbeAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private static IReadOnlyList<ProviderDescriptor> Providers() =>
    [
        new("claude-code", "Claude Code", "CC", new SilentProbe("Claude Code")),
        new("codex", "Codex", "CX", new SilentProbe("Codex"))
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

        Assert.Empty(watcher.Observe(model.Providers, QuotaMilestones.Ladder));

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

    [Fact]
    public void EscapeDismissesOnlyAVisibleUnpinnedWidget()
    {
        Assert.True(WidgetWindow.ShouldDismissOnEscape(isPinned: false, isVisible: true));
        Assert.False(WidgetWindow.ShouldDismissOnEscape(isPinned: true, isVisible: true));
        Assert.False(WidgetWindow.ShouldDismissOnEscape(isPinned: false, isVisible: false));
    }

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

    /// <summary>
    /// The footer carries four things in 360px. This pins that all of them are laid out and none has
    /// been squeezed to nothing - the failure a XAML-only change makes silently.
    /// </summary>
    [Fact]
    public void TheFooterFitsTheProviderCountTheVersionAndBothLinks() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default);
        WidgetWindow window = new(model, Settings(AppSettings.Default));

        _ = Content(window);

        Button project = (Button)window.FindName("FooterProjectButton");
        Button refresh = (Button)window.FindName("FooterRefreshButton");
        FrameworkElement footer = (FrameworkElement)window.FindName("Footer");

        Assert.Equal("GitHub", project.Content);
        Assert.True(project.ActualWidth > 0, "The GitHub link rendered with no width.");
        Assert.True(refresh.ActualWidth > 0, "The Refresh link rendered with no width.");
        Assert.True(
            project.ActualWidth + refresh.ActualWidth < footer.ActualWidth,
            "The two footer links alone do not fit the footer.");

        model.Dispose();
    });

    /// <summary>
    /// PRD §28: a provider the user hid leaves the widget. The strip honoured this from the day it
    /// was added; the card list - the surface the setting is actually named for - did not, so every
    /// card rendered while the footer counted none of them.
    /// </summary>
    [Fact]
    public void AProviderTheUserHidHasNoCardOnTheWidget() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        AppSettings hiding = AppSettings.Default with { HiddenProviders = ["codex"] };
        MainViewModel model = Model(providers, hiding);
        WidgetWindow window = new(model, Settings(hiding));

        _ = Content(window);

        ItemsControl list = (ItemsControl)window.FindName("ProviderList");
        UIElement shown = Container(list, model, "Claude Code");
        UIElement hidden = Container(list, model, "Codex");

        Assert.Equal(Visibility.Visible, shown.Visibility);
        // Collapsed rather than Hidden: a hidden provider must cost no height either, or the widget
        // keeps a card-sized gap where the card used to be.
        Assert.Equal(Visibility.Collapsed, hidden.Visibility);
        Assert.Equal(0d, hidden.RenderSize.Height);

        model.Dispose();
    });

    /// <summary>
    /// A widget the filter has emptied keeps a body. Without one it shrinks to title bar plus
    /// footer - a bar that reads as a broken window rather than as an empty one - and the only
    /// account of what happened is the "0 providers" in the footer.
    /// </summary>
    [Fact]
    public void AnEmptiedWidgetExplainsItselfInsteadOfShrinkingToABar() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        AppSettings hiding = AppSettings.Default with { HiddenProviders = ["claude-code", "codex"] };
        MainViewModel model = Model(providers, hiding);
        WidgetWindow window = new(model, Settings(hiding));

        _ = Content(window);

        FrameworkElement empty = (FrameworkElement)window.FindName("EmptyState");
        TextBlock message = (TextBlock)window.FindName("EmptyStateMessage");
        TextBlock remedy = (TextBlock)window.FindName("EmptyStateRemedy");

        Assert.Equal(Visibility.Visible, empty.Visibility);
        Assert.True(empty.ActualHeight > 40, $"The empty state rendered {empty.ActualHeight} tall.");
        Assert.Equal("All providers are hidden.", message.Text);
        Assert.Equal("Show one again in settings, under Providers.", remedy.Text);

        model.Dispose();
    });

    /// <summary>
    /// And it is gone the moment there is a card to show - including a card that comes back after
    /// the empty state has already been on screen.
    /// </summary>
    [Fact]
    public void TheEmptyStateLeavesAsSoonAsAProviderIsShownAgain() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        AppSettings hiding = AppSettings.Default with { HiddenProviders = ["claude-code", "codex"] };
        MainViewModel model = Model(providers, hiding);
        SettingsService settings = Settings(hiding);
        WidgetWindow window = new(model, settings);

        _ = Content(window);
        FrameworkElement empty = (FrameworkElement)window.FindName("EmptyState");

        Assert.Equal(Visibility.Visible, empty.Visibility);

        settings.Update(current => current with { HiddenProviders = ["codex"] });

        Assert.Equal(Visibility.Collapsed, empty.Visibility);

        model.Dispose();
    });

    /// <summary>
    /// The flow as it is actually performed: the widget is already on screen when the box is
    /// unchecked. The card has to leave on the settings change and come back when it is ticked
    /// again, which is a different path from the one that builds the list at startup.
    /// </summary>
    [Fact]
    public void UncheckingAProviderWhileTheWidgetIsOpenTakesItsCardAway() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default);
        SettingsService settings = Settings(AppSettings.Default);
        WidgetWindow window = new(model, settings);

        _ = Content(window);
        ItemsControl list = (ItemsControl)window.FindName("ProviderList");
        UIElement codex = Container(list, model, "Codex");

        Assert.Equal(Visibility.Visible, codex.Visibility);

        settings.Update(current => current with { HiddenProviders = ["codex"] });
        Assert.Equal(Visibility.Collapsed, codex.Visibility);

        settings.Update(current => current with { HiddenProviders = [] });
        Assert.Equal(Visibility.Visible, codex.Visibility);

        model.Dispose();
    });

    /// <summary>
    /// The same trigger carries PRD §15's availability filter, which is a separate product rule
    /// reaching the widget through one binding - so it is pinned separately.
    /// </summary>
    [Fact]
    public void AnAbsentProviderHasNoCardOnceUnavailableProvidersAreHidden() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        AppSettings filtered = AppSettings.Default with { ShowUnavailableProviders = false };
        MainViewModel model = Model(providers, filtered);
        WidgetWindow window = new(model, Settings(filtered));
        model.Providers.Single(card => card.DisplayName == "Codex").Apply(
            NotInstalled("Codex"),
            DateTimeOffset.Now,
            FreshnessPolicy.Default);

        _ = Content(window);

        ItemsControl list = (ItemsControl)window.FindName("ProviderList");

        Assert.Equal(Visibility.Visible, Container(list, model, "Claude Code").Visibility);
        Assert.Equal(Visibility.Collapsed, Container(list, model, "Codex").Visibility);

        model.Dispose();
    });

    private static UIElement Container(ItemsControl list, MainViewModel model, string displayName) =>
        (UIElement)list.ItemContainerGenerator.ContainerFromItem(
            model.Providers.Single(card => card.DisplayName == displayName));

    private static ProviderSnapshot NotInstalled(string name) => new(
        ProviderName: name, Installed: false, Version: null, ExecutablePath: null,
        State: ConnectionState.NotInstalled, Mechanism: "fake", Tier: MechanismTier.Official,
        UpdateModel: "pull (poll)", Windows: [], RetrievedAt: DateTimeOffset.Now, Error: null, Notes: []);

    /// <summary>
    /// The overlay scrollbar shares its cell with the content instead of taking a column beside it.
    /// A stock template would narrow every card by its width the moment the list outgrew the 520px
    /// ceiling, which is exactly when the cards have most to say.
    /// </summary>
    [Fact]
    public void TheProviderListScrollBarDoesNotTakeLayoutWidthFromTheCards() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default);
        WidgetWindow window = new(model, Settings(AppSettings.Default));

        _ = Content(window);

        ItemsControl list = (ItemsControl)window.FindName("ProviderList");
        ScrollViewer viewer = Ancestor<ScrollViewer>(list);

        // A stock ScrollViewer puts the bar in its own column, so a visible bar leaves the viewport
        // narrower than the control. Overlaid, the two stay equal whether or not the bar is showing.
        Assert.Equal(viewer.ActualWidth, viewer.ViewportWidth);

        model.Dispose();
    });

    private static T Ancestor<T>(DependencyObject from) where T : DependencyObject
    {
        for (DependencyObject? at = VisualTreeHelper.GetParent(from); at is not null; at = VisualTreeHelper.GetParent(at))
        {
            if (at is T found)
            {
                return found;
            }
        }

        throw new InvalidOperationException($"No {typeof(T).Name} above {from.GetType().Name}.");
    }

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

    [Fact]
    public void ThePollTickIsFiveSeconds() => Assert.Equal(TimeSpan.FromSeconds(5), TickCadence.Poll);

    /// <summary>
    /// The poll timer stopped deciding when a provider is due, so it must stop tracking the refresh
    /// interval too. A timer still set to the shared interval would gate the service a second time
    /// and make a fifteen-second per-provider override unreachable under a five-minute shared one.
    /// </summary>
    [Fact]
    public void ThePollTimerHoldsTheFixedTickWhateverTheRefreshIntervalBecomes() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default);
        SettingsService settings = Settings(AppSettings.Default);
        ProviderRefreshService refresh = new(providers, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60));

        WidgetWindow window = new(model, settings, refresh, providers: providers);

        DispatcherTimer poll = (DispatcherTimer)typeof(WidgetWindow)
            .GetField("_poll", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;

        Assert.Equal(TickCadence.Poll, poll.Interval);

        settings.Update(s => s with { RefreshIntervalSeconds = 300 });

        Assert.Equal(TickCadence.Poll, poll.Interval);
        Assert.Equal(TimeSpan.FromSeconds(300), refresh.BaseInterval);

        model.Dispose();
    });

    /// <summary>
    /// A hidden provider must not be polled even once, so the whole cadence picture is pushed into
    /// the service before the first refresh rather than on the first settings change.
    /// </summary>
    [Fact]
    public void HiddenProvidersAndPerProviderIntervalsReachTheServiceAtStartup() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        AppSettings configured = AppSettings.Default with
        {
            RefreshIntervalSeconds = 300,
            HiddenProviders = ["codex"],
            ProviderRefreshSeconds = new Dictionary<string, int> { ["claude-code"] = 15 }
        };

        MainViewModel model = Model(providers, configured);
        ProviderRefreshService refresh = new(providers, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60));

        WidgetWindow window = new(model, Settings(configured), refresh, providers: providers);

        Assert.Contains("codex", refresh.HiddenProviderKeys);
        Assert.Equal(TimeSpan.FromSeconds(120), refresh.IntervalFor(providers[0]));
        Assert.Equal(TimeSpan.FromSeconds(300), refresh.IntervalFor(providers[1]));

        model.Dispose();
    });

    private static DispatcherTimer TickTimer(WidgetWindow window) =>
        (DispatcherTimer)typeof(WidgetWindow)
            .GetField("_tick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;

    private static object? MiniField(WidgetWindow window) =>
        typeof(WidgetWindow)
            .GetField("_mini", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window);

    [Fact]
    public void EnteringMiniModeReplacesTheWidgetRatherThanJoiningIt() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default);
        SettingsService settings = Settings(AppSettings.Default);
        WidgetWindow window = new(model, settings);

        window.EnterMiniMode();

        Assert.True(settings.Current.MiniMode);
        Assert.NotEqual(Visibility.Visible, window.Visibility);
        Assert.NotNull(MiniField(window));

        window.LeaveMiniMode();
        model.Dispose();
    });

    /// <summary>
    /// The strip is the always-visible window, so the tick that drives countdowns and the tray
    /// glyph has to run at the visible rate while it is up - even though the widget is hidden.
    /// </summary>
    [Fact]
    public void TheTickStaysAtTheVisibleRateWhileTheStripIsUp() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default);
        WidgetWindow window = new(model, Settings(AppSettings.Default));

        window.EnterMiniMode();

        Assert.Equal(TickCadence.Visible, TickTimer(window).Interval);

        window.LeaveMiniMode();
        model.Dispose();
    });

    /// <summary>
    /// LeaveMiniMode is called by ShowFromTray, which every entry point funnels through. Nulling
    /// the field before closing the strip is what stops that from recursing.
    /// </summary>
    [Fact]
    public void ShowingTheWidgetLeavesMiniModeWithoutRecursing() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default);
        SettingsService settings = Settings(AppSettings.Default);
        WidgetWindow window = new(model, settings);
        window.EnterMiniMode();

        settings.Update(s => s with { MiniMode = false });

        Assert.Null(MiniField(window));
        Assert.False(settings.Current.MiniMode);

        model.Dispose();
    });

    /// <summary>
    /// The balloon exists to explain a window that vanished. Nothing vanishes on the way into mini
    /// mode, and the hint is shown once ever - too scarce to spend on a visible window.
    /// </summary>
    [Fact]
    public void EnteringMiniModeDoesNotSpendTheTrayHint() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default);
        SettingsService settings = Settings(AppSettings.Default);
        WidgetWindow window = new(model, settings);

        window.EnterMiniMode();
        window.HideToTray();

        Assert.False(settings.Current.TrayHintShown);

        window.LeaveMiniMode();
        model.Dispose();
    });

    /// <summary>
    /// Toggling the setting alone must move the mode, because that is the only path the settings
    /// window's checkbox has.
    /// </summary>
    [Fact]
    public void TheSettingAloneEntersAndLeavesTheMode() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default);
        SettingsService settings = Settings(AppSettings.Default);
        WidgetWindow window = new(model, settings);

        settings.Update(s => s with { MiniMode = true });
        Assert.NotNull(MiniField(window));

        settings.Update(s => s with { MiniMode = false });
        Assert.Null(MiniField(window));

        model.Dispose();
    });

    [Fact]
    public void TheTrayMenuOffersMiniModeBetweenSettingsAndDiagnostics() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default);
        WidgetWindow window = new(model, Settings(AppSettings.Default));

        ContextMenu menu = Assert.IsType<ContextMenu>(window.Resources["TrayMenu"]);
        string[] headers = [.. menu.Items.OfType<MenuItem>().Select(item => (string)item.Header)];

        Assert.Equal(["Open", "Refresh all providers", "Settings", "Mini mode", "Diagnostics", "Exit"], headers);
        Assert.True(menu.Items.OfType<MenuItem>().Single(item => (string)item.Header == "Mini mode").IsCheckable);

        model.Dispose();
    });

    /// <summary>
    /// Counts the strips the fixture's shared <see cref="Application"/> is holding. Only ever read
    /// as a difference across one call: the collection is process-wide and every test in this
    /// collection contributes to it, so the absolute number means nothing.
    /// </summary>
    private static int OpenStrips() => Application.Current.Windows.OfType<MiniWindow>().Count();

    /// <summary>
    /// Entering the mode by the call rather than by the setting - which is what the tray menu item
    /// does - must build exactly one strip. It built two: the setting is announced synchronously,
    /// so the update inside EnterMiniMode re-entered it through OnSettingsChanged while the field
    /// was still null, and the outer call then overwrote that first strip with a second. Only the
    /// tracked one was ever closed, leaving a strip on screen that no setting, checkbox or tray
    /// tick admitted to.
    /// </summary>
    [Fact]
    public void EnteringMiniModeByCallBuildsOneStripNotTwo() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default);
        SettingsService settings = Settings(AppSettings.Default);
        WidgetWindow window = new(model, settings);

        int before = OpenStrips();
        window.EnterMiniMode();
        int entered = OpenStrips();

        window.LeaveMiniMode();
        int left = OpenStrips();

        Assert.Equal(before + 1, entered);

        // The other half of the same defect: leaving closes what this window tracks, so an
        // untracked strip would still be up here with MiniMode already back to false.
        Assert.Equal(before, left);
        Assert.False(settings.Current.MiniMode);

        model.Dispose();
    });

    private static void ClickTrayMiniMode(WidgetWindow window, MenuItem item) =>
        typeof(WidgetWindow)
            .GetMethod("TrayMini_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [item, new RoutedEventArgs()]);

    /// <summary>
    /// The tray gesture end to end, twice: opening the menu takes the tick from the setting, the
    /// click flips the tick, and the handler reads that new state back. Driven as a pair because
    /// the complaint was the pair disagreeing - a strip plainly on screen under a menu item with no
    /// tick against it - so the tick and the number of strips are asserted together at each step.
    /// </summary>
    [Fact]
    public void TheTrayTickAndTheStripOnScreenAgreeAcrossAFullToggle() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default);
        SettingsService settings = Settings(AppSettings.Default);
        WidgetWindow window = new(model, settings);
        MenuItem item = ((ContextMenu)window.Resources["TrayMenu"]).Items.OfType<MenuItem>().Single(entry => entry.IsCheckable);

        int before = OpenStrips();

        item.IsChecked = settings.Current.MiniMode;
        item.IsChecked = !item.IsChecked;
        ClickTrayMiniMode(window, item);

        Assert.Equal(before + 1, OpenStrips());
        Assert.True(settings.Current.MiniMode);

        item.IsChecked = settings.Current.MiniMode;
        item.IsChecked = !item.IsChecked;
        ClickTrayMiniMode(window, item);

        Assert.Equal(before, OpenStrips());
        Assert.False(settings.Current.MiniMode);

        window.HideToTray();
        model.Dispose();
    });
}
