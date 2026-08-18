using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AiUsageMonitor.App.Interop;
using AiUsageMonitor.App.Notifications;
using AiUsageMonitor.App.Theming;
using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Infrastructure.Logging;
using AiUsageMonitor.Infrastructure.Diagnostics;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Refresh;
using AiUsageMonitor.Infrastructure.Settings;
using AiUsageMonitor.Infrastructure.Theming;
using AiUsageMonitor.Infrastructure.Updates;
using Microsoft.Win32;

namespace AiUsageMonitor.App.Views;

public partial class WidgetWindow : Window
{
    private readonly MainViewModel _model;
    private readonly SettingsService _settings;
    private readonly ProviderRefreshService? _refresh;
    private readonly ThemeManager? _theme;
    /// <summary>
    /// The project's home. The only address this application ever hands to a browser, and it goes
    /// there because the user clicked a link asking for it. Kept here beside the footer button that
    /// uses it rather than in a settings file, so it cannot be repointed by editing one.
    /// </summary>
    private const string ProjectUrl = "https://github.com/nayots/ai-agent-usage-monitor";

    private readonly IReadOnlyList<ProviderDescriptor> _providers;
    private readonly EnvironmentReport _environment;
    private readonly StartupReport _startup;
    private readonly UpdateCheckService? _updates;
    private SettingsWindow? _settingsWindow;
    private MiniWindow? _mini;
    private readonly DispatcherTimer _tick = new() { Interval = TickCadence.Visible };
    private readonly DispatcherTimer _poll = new();

    /// <summary>
    /// Answers a lost focus on a short delay rather than at once, because the focus can land back
    /// inside this application a moment after it leaves. Clicking the notification-area icon gives
    /// the foreground to the shell first and only then reaches the widget as a click; moving from
    /// the widget to its settings window deactivates one before it activates the other. Both would
    /// read as an outside click if answered immediately.
    /// </summary>
    private readonly DispatcherTimer _dismiss = new() { Interval = TimeSpan.FromMilliseconds(150) };

    private readonly UsageAlertWatcher _alerts = new();
    private TrayIcon? _tray;
    private WinEventProc? _foregroundHook;
    private IntPtr _foregroundHookHandle;
    private TrayGlyphState _glyph = TrayGlyphState.Empty;
    private ThemeVariant? _glyphVariant;
    private bool _systemEventsSubscribed;
    private bool _sourceInitialized;
    private bool _globalHotkeyRegistered;
    private bool _globalHotkeyUnavailable;
    private bool _shuttingDown;

    public WidgetWindow(
        MainViewModel model,
        SettingsService settings,
        ProviderRefreshService? refresh = null,
        ThemeManager? theme = null,
        IReadOnlyList<ProviderDescriptor>? providers = null,
        EnvironmentReport? environment = null,
        StartupReport? startup = null,
        UpdateCheckService? updates = null)
    {
        _model = model;
        _settings = settings;
        _refresh = refresh;
        _theme = theme;
        _providers = providers ?? [];
        _environment = environment ?? EnvironmentReport.Capture();
        _startup = startup ?? new StartupReport(DateTimeOffset.Now, null);
        _updates = updates;

        InitializeComponent();
        DataContext = model;
        DpiChanged += (_, _) => ClampPlacement();

        Topmost = settings.Current.AlwaysOnTop;
        RestorePlacement(settings.Current);

        _tick.Tick += OnTick;

        // A fixed short tick, not the refresh interval. The service decides, per provider, whether
        // anything is due - which is the only place that can be decided now that a provider may
        // have its own interval or be hidden entirely. Pushed before the first refresh so a hidden
        // provider is never polled even once.
        _poll.Interval = TickCadence.Poll;
        _poll.Tick += (_, _) =>
        {
            _ = _model.RefreshAsync(force: false, RefreshTrigger.Scheduled);
            _ = CheckForUpdatesIfDueAsync();
        };
        ApplyCadence(settings.Current);
        _dismiss.Tick += OnDismissTick;

        _settings.Changed += OnSettingsChanged;

        if (_updates is not null)
        {
            _updates.StatusChanged += OnUpdateStatusChanged;
            _model.ApplyUpdateStatus(_updates.Status);
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        IntPtr handle = new WindowInteropHelper(this).Handle;
        DwmWindowChrome.UseRoundedCorners(handle);
        ApplyTitleBarTheme(handle);

        _tray = new TrayIcon(this, "AI Usage Monitor");
        _tray.Activated += (_, _) => ShowFromTray();
        _tray.ContextMenuRequested += OnTrayContextMenuRequested;
        _tray.Show();

        HwndSource.FromHwnd(handle)?.AddHook(OnWindowMessage);
        WatchTheForeground();
        _sourceInitialized = true;
        UpdateGlobalHotkeyRegistration(_settings.Current);

        if (_theme is not null)
        {
            _theme.Changed += OnThemeChanged;
        }

        try
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            _systemEventsSubscribed = true;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Unable to subscribe to system events ({0}); continuing without lifecycle refreshes.", ex.GetType().Name);
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        }
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        ClampPlacement();

        _tick.Start();
        _poll.Start();
        _ = _model.RefreshAsync(force: true, RefreshTrigger.Startup);

        // Last, so the widget is fully constructed before it is hidden again. Restoring mini mode
        // must not leave the widget on screen beside the strip.
        if (_settings.Current.MiniMode)
        {
            EnterMiniMode();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _tick.Stop();
        _poll.Stop();
        _dismiss.Stop();

        // Before the tray icon goes, and before anything else can fail: the strip is a separate
        // top-level window, so one left behind would outlive the application that owns it.
        MiniWindow? strip = _mini;
        _mini = null;
        strip?.Close();

        if (_foregroundHookHandle != IntPtr.Zero)
        {
            UnhookWinEvent(_foregroundHookHandle);
            _foregroundHookHandle = IntPtr.Zero;
        }

        // Named rather than a lambda so it can actually be detached. ThemeManager outlives this
        // window - it is a singleton owned by the application - so a subscription left behind
        // would keep calling back into a closed window for the rest of the process's life.
        if (_theme is not null)
        {
            _theme.Changed -= OnThemeChanged;
        }

        if (_systemEventsSubscribed)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _systemEventsSubscribed = false;
        }

        _settings.Changed -= OnSettingsChanged;

        if (_updates is not null)
        {
            _updates.StatusChanged -= OnUpdateStatusChanged;
        }

        UnregisterGlobalHotkey();
        SavePlacement();
        _tray?.Dispose();
        _model.Dispose();
        base.OnClosed(e);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _model.Tick();
        UpdateTrayGlyph();

        DeliverAlerts();
    }

    /// <summary>
    /// Observed unconditionally, delivered conditionally - and the order matters. The watcher has
    /// to keep advancing its state while notifications are switched off, because its alerts are
    /// edge-triggered: skipping the observation would leave every rung where it stood, and turning
    /// notifications back on would then release a burst of crossings the user already lived through.
    /// <para>
    /// Quiet hours are the same argument a second time, which is why they are applied here and not
    /// inside the watcher: suppressing the observation overnight would bank every crossing the user
    /// slept through and release them all at 07:00. They are applied before coalescing, so a
    /// suppressed milestone cannot ride along inside a merged balloon.
    /// </para>
    /// <para>
    /// The settings are read here rather than cached, so switching either off silences the next
    /// alert rather than the next restart.
    /// </para>
    /// </summary>
    private void DeliverAlerts()
    {
        AppSettings settings = _settings.Current;
        IReadOnlyList<UsageAlert> alerts = _alerts.Observe(_model.Providers, settings.EffectiveAlertThresholds);

        if (!settings.NotifyOnQuotaEvents)
        {
            return;
        }

        alerts = QuietHoursFilter.Apply(alerts, settings.QuietHours, TimeOnly.FromDateTime(DateTime.Now));

        foreach (UsageAlert alert in AlertBatch.Coalesce(alerts))
        {
            _tray?.Notify(alert.Title, alert.Text, alert.IsSilent);
        }
    }

    /// <summary>
    /// Redraws the notification-area glyph, and only when it would actually differ.
    /// <para>
    /// This is driven from the one-second tick rather than from a refresh because not every change
    /// the glyph shows comes from a refresh: a card goes stale, and its bars grey, on the clock
    /// alone. Rebuilding the state costs a walk over a handful of rows; what must not happen sixty
    /// times a minute is the redraw, because each one is a new icon handle and a message to another
    /// process. So the state is compared, not the trigger.
    /// </para>
    /// </summary>
    private void UpdateTrayGlyph()
    {
        if (_tray is null)
        {
            return;
        }

        TrayGlyphState state = TrayGlyphState.From(_model.Providers);
        ThemeVariant variant = TrayGlyphPalette.TaskbarVariant;

        if (!state.HasContent || (variant == _glyphVariant && state.Matches(_glyph)))
        {
            return;
        }

        IntPtr icon = TrayGlyphRenderer.Render(
            state.Bars,
            state.Digits,
            state.DigitsAreStale,
            state.Overlay,
            TrayIcon.SmallIconSize,
            TrayGlyphPalette.For(variant));

        if (icon == IntPtr.Zero)
        {
            // GDI refused the bitmap. Keeping the icon already in the tray says something slightly
            // out of date; replacing it with nothing would say the widget had gone.
            return;
        }

        _tray.SetIcon(icon);
        _glyph = state;
        _glyphVariant = variant;
    }

    private void OnThemeChanged(object? sender, EventArgs e) =>
        ApplyTitleBarTheme(new WindowInteropHelper(this).Handle);

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            Dispatcher.BeginInvoke(() => AfterSystemEvent(RefreshTrigger.Resume));
        }
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
                Dispatcher.BeginInvoke(() => _model.SetWorkstationLocked(true));
                break;

            case SessionSwitchReason.SessionUnlock:
                Dispatcher.BeginInvoke(() =>
                {
                    // Clear the pause before requesting the unlock refresh, or it refuses itself.
                    _model.SetWorkstationLocked(false);
                    AfterSystemEvent(RefreshTrigger.Unlock);
                });
                break;
        }
    }

    private void AfterSystemEvent(RefreshTrigger trigger)
    {
        _ = _model.RefreshAfterLifecycleEventAsync(trigger);
        OnTick(this, EventArgs.Empty);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(ClampPlacement);

    /// <summary>
    /// Everything a settings change reaches from here. The poll timer's own interval is not among
    /// them any more: it is a fixed tick that only asks whether anything is due, and the answer -
    /// shared interval, per-provider override, hidden entirely - lives in the refresh service.
    /// </summary>
    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        Topmost = settings.AlwaysOnTop;
        ApplyCadence(settings);

        // Reconciled from the setting rather than driven from the control that changed it, so the
        // settings window's checkbox and the tray menu item need no code path of their own.
        if (settings.MiniMode && _mini is null)
        {
            EnterMiniMode();
        }
        else if (!settings.MiniMode && _mini is not null)
        {
            ShowFromTray();
        }

        _theme?.Apply(settings.Theme);
        _model.ApplySettings(settings);
        if (_sourceInitialized)
        {
            UpdateGlobalHotkeyRegistration(settings);
        }
        UpdateTrayGlyph();
    }

    /// <summary>
    /// Hands the refresh service the whole cadence picture in one go: the shared interval, the
    /// per-provider overrides, and which providers the user has hidden. Replaced wholesale rather
    /// than patched, so a key removed from the settings file stops applying rather than lingering.
    /// </summary>
    private void ApplyCadence(AppSettings settings)
    {
        if (_refresh is null)
        {
            return;
        }

        Dictionary<string, TimeSpan> overrides = [];

        foreach (ProviderDescriptor provider in _providers)
        {
            if (settings.RefreshSecondsOverrideFor(provider.Key) is not null)
            {
                overrides[provider.Key] = settings.RefreshIntervalFor(provider.Key);
            }
        }

        _refresh.BaseInterval = settings.RefreshInterval;
        _refresh.IntervalOverrides = overrides;
        _refresh.HiddenProviderKeys = settings.HiddenProviders;
    }

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
            recheckProviders: RecheckProviders,
            providers: _providers,
            globalHotkeyUnavailable: _globalHotkeyUnavailable,
            updates: _updates);

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

    private string ThemeDescription() => _theme is null
        ? _settings.Current.Theme.ToString()
        : $"{_settings.Current.Theme} · resolved {_theme.Current}";

    private string DisplayScalingDescription()
    {
        if (PresentationSource.FromVisual(this) is null)
        {
            return DiagnosticsViewModel.EmptyValue;
        }

        double percentage = VisualTreeHelper.GetDpi(this).DpiScaleX * 100;
        return percentage.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";
    }

    private static void CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // The clipboard belongs to another process for the moment. Diagnostics remains open.
        }
    }

    private void ResetPlacement()
    {
        _settings.Update(s => s with { WindowLeft = null, WindowTop = null });
        Left = (SystemParameters.WorkArea.Width - Width) / 2;
        Top = (SystemParameters.WorkArea.Height - ActualHeight) / 2;
    }

    /// <summary>
    /// The one action that means "look at this machine again" rather than "get me current numbers".
    /// Each probe caches where its provider is installed and what version it reports, so without the
    /// invalidation below a provider installed a moment ago would stay invisible until that cache
    /// lapsed - which is the whole reason this button exists.
    /// </summary>
    private void RecheckProviders()
    {
        foreach (ProviderDescriptor provider in _providers)
        {
            provider.Probe.InvalidateInstallation();
        }

        _ = _model.RefreshAsync(force: true, RefreshTrigger.ManualGlobal);
    }

    /// <summary>
    /// UseShellExecute is required: without it the explorer.exe launch is treated as a raw process
    /// start and the folder argument is ignored.
    /// </summary>
    private void OpenLogsFolder()
    {
        try
        {
            Directory.CreateDirectory(RollingFileLoggerProvider.DefaultDirectory);
            Process.Start(new ProcessStartInfo(RollingFileLoggerProvider.DefaultDirectory) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // Failing to open a folder is not a reason to take the widget down.
        }
    }

    private void ApplyTitleBarTheme(IntPtr handle) =>
        DwmWindowChrome.UseDarkTitleBar(handle, _theme?.Current == ThemeVariant.Dark);

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => ShowSettings();

    /// <summary>
    /// Hands the project's address to whatever the user has set as their browser. This is an
    /// ordinary hyperlink the user clicked, not the browser automation PRD §23 forbids: nothing is
    /// read back, no page is driven, and the application never learns whether the browser opened.
    /// </summary>
    private void Project_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(ProjectUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            // A machine with no registered browser is not a reason to take the widget down.
        }
    }

    /// <summary>
    /// Spec D1: the terminal action is the browser, not a download. The destination is the same
    /// compile-time constant the settings page uses.
    /// </summary>
    private void Update_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(GitHubReleaseClient.ReleasePageUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Driven from the same 5-second tick as the provider poll rather than from a timer of its own.
    /// The service owns the cadence and answers whether anything is due - so a second timer would
    /// only be a second place for that answer to live.
    /// </summary>
    private async Task CheckForUpdatesIfDueAsync()
    {
        DateTimeOffset now = DateTimeOffset.Now;

        if (_updates is null || !_updates.IsDue(now))
        {
            return;
        }

        UpdateStatus status = await _updates.CheckAsync(manual: false, now, CancellationToken.None);

        _settings.Update(s => s with
        {
            LastUpdateCheckUtc = status.LastCheckedUtc,
            LastUpdateCheckETag = _updates.ETag
        });
    }

    private void OnUpdateStatusChanged(object? sender, UpdateStatus status) =>
        Dispatcher.Invoke(() => _model.ApplyUpdateStatus(status));

    /// <summary>
    /// Written through the settings service rather than by assigning <see cref="Window.Topmost"/>
    /// directly, so the choice persists and the settings window's own checkbox follows it. The
    /// window's Topmost is then set by <see cref="OnSettingsChanged"/>, on the one path that
    /// already owns it.
    /// </summary>
    private void Pin_Click(object sender, RoutedEventArgs e) =>
        _settings.Update(s => s with { AlwaysOnTop = !s.AlwaysOnTop });

    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == WindowMessageHotkey && wParam.ToInt32() == GlobalHotkeyId)
        {
            ToggleGlobalHotkey();
            handled = true;
        }

        if ((uint)msg == SingleInstance.ShowMessage)
        {
            ShowFromTray();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void ToggleGlobalHotkey()
    {
        if (Visibility == Visibility.Hidden || !IsActive)
        {
            ShowFromTray();
            return;
        }

        HideToTray();
    }

    /// <summary>
    /// A tray menu has no window to be placed relative to, so it is positioned by the mouse and
    /// given a placement target explicitly. StaysOpen false plus focusing the window first is what
    /// makes it dismiss on an outside click - a menu opened from a tray icon otherwise stays up.
    /// </summary>
    private void OnTrayContextMenuRequested(object? sender, EventArgs e)
    {
        if (Resources["TrayMenu"] is not ContextMenu menu)
        {
            return;
        }

        // Set as the menu opens rather than kept in step from elsewhere: the mode can change from
        // the settings window or from a click on the strip, and a tick that lied about which mode
        // you were in would be worse than no tick at all.
        if (MiniModeMenuItem(menu) is MenuItem item)
        {
            item.IsChecked = _settings.Current.MiniMode;
        }

        // Set as the menu opens, for the same reason the mini-mode tick is: the verdict can change
        // between two openings, and a stale item offering a version that is already installed is
        // worse than no item at all.
        UpdateStatus? status = _updates?.Status;
        bool hasUpdate = status?.Availability == UpdateAvailability.UpdateAvailable;

        foreach (FrameworkElement element in menu.Items.OfType<FrameworkElement>()
                     .Where(item => Equals(item.Tag, "update")))
        {
            element.Visibility = hasUpdate ? Visibility.Visible : Visibility.Collapsed;
        }

        if (hasUpdate && menu.Items.OfType<MenuItem>().FirstOrDefault(item => Equals(item.Tag, "update")) is MenuItem update)
        {
            update.Header = UpdateCopy.TrayText(status!);
        }

        menu.PlacementTarget = this;
        menu.Placement = PlacementMode.MousePoint;
        menu.StaysOpen = false;
        SetForegroundWindow(new WindowInteropHelper(this).Handle);
        menu.IsOpen = true;
    }

    private static MenuItem? MiniModeMenuItem(ContextMenu menu) =>
        menu.Items.OfType<MenuItem>().FirstOrDefault(item => item.IsCheckable);

    /// <summary>
    /// The widget is a glance, not a workspace: once the focus leaves this application entirely,
    /// its windows go the same way the close button sends them - the settings window closed, the
    /// widget hidden to the notification area, the process still polling behind the icon.
    /// <para>
    /// Watched at the session's foreground rather than only at this window's own activation,
    /// because a window that was never activated is never deactivated either - and that state is
    /// ordinary, not exotic. A widget shown while another application holds the focus, which is
    /// what happens when it is launched from a terminal or shown from the tray without being
    /// granted the foreground, appears without ever becoming active. Waiting for its deactivation
    /// would leave that widget on screen through every click elsewhere: the exact complaint this
    /// feature exists to answer.
    /// </para>
    /// <para>
    /// Out of context, so nothing is injected into another process and no other application's
    /// content is read. Only the fact that the foreground moved is taken from the event; which
    /// window it moved to is read from the shell at the tick, on the one path that already
    /// answers that question.
    /// </para>
    /// </summary>
    private void WatchTheForeground()
    {
        // Held in a field. The callback is handed to unmanaged code, which the garbage collector
        // cannot see: a delegate referenced only by the argument would be collected while the hook
        // was still live, and the next foreground change in the session would call into freed
        // memory. A zero handle just means the fallback below is all there is.
        _foregroundHook = (_, _, _, _, _, _, _) => _dismiss.Start();
        _foregroundHookHandle = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND,
            EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _foregroundHook,
            idProcess: 0,
            idThread: 0,
            WINEVENT_OUTOFCONTEXT);
    }

    /// <summary>
    /// The fallback, and the only path when the hook above could not be installed. Kept because it
    /// costs two lines and covers the case the hook cannot: nothing else has to be true for a
    /// window to know it has just lost the focus.
    /// <para>
    /// The pin is the exemption from all of it, and the reason it is the right one: a widget kept
    /// above other windows is being watched while something else is worked in, which is exactly
    /// the situation a dismissal would make impossible.
    /// </para>
    /// </summary>
    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        _dismiss.Start();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        _dismiss.Stop();
    }

    private void OnDismissTick(object? sender, EventArgs e) =>
        DismissIfFocusLeftTheApplication(ForegroundBelongsToThisApplication());

    /// <summary>
    /// The dismissal itself, given the one fact it cannot establish without a shell to ask: whether
    /// the focus is still somewhere in this application. Split there so the rule can be exercised
    /// without arranging a real foreground window.
    /// <para>
    /// The visibility test is against Hidden rather than for Visible, because what it rules out is
    /// dismissing what is already dismissed: hiding the widget deactivates it, which brings the
    /// question back a moment later, and answering yes twice would hide what is hidden and, on a
    /// first run, say where it went in a second balloon. Hidden is the state that hiding produces.
    /// A window that has never been shown is Collapsed instead, which passes - reachable in a test,
    /// never in use, since a window that was never activated is never deactivated either.
    /// </para>
    /// </summary>
    public void DismissIfFocusLeftTheApplication(bool focusStayedInTheApplication)
    {
        _dismiss.Stop();

        if (_shuttingDown
            || focusStayedInTheApplication
            || Visibility == Visibility.Hidden
            || _settings.Current.AlwaysOnTop)
        {
            return;
        }

        // The owned window first: it is owned by the widget, and hiding an owner leaves an owned
        // window on screen as the only thing left of an application that has just gone away.
        _settingsWindow?.Close();
        HideToTray();
    }

    /// <summary>
    /// Whether the window now holding the foreground belongs to this application. Asked by process
    /// rather than by comparing against the windows this class knows about, because not every
    /// window that can take the focus is one of them: a context menu and a tooltip are each their
    /// own top-level window, and the tray menu is opened by this widget on purpose.
    /// </summary>
    private static bool ForegroundBelongsToThisApplication()
    {
        IntPtr foreground = GetForegroundWindow();

        if (foreground == IntPtr.Zero)
        {
            // Nothing holds the foreground at all - true mid-switch, and while the session is
            // locked. Read as ours, so the widget is never dismissed by an absence of focus rather
            // than by a click that landed somewhere else.
            return true;
        }

        _ = GetWindowThreadProcessId(foreground, out uint process);
        return process == (uint)Environment.ProcessId;
    }

    private void ShowFromTray()
    {
        // The strip and the widget are never on screen together, and this is the one line that
        // makes that true everywhere: the tray's Open and Settings, the global hotkey and the
        // single-instance broadcast all arrive here.
        LeaveMiniMode();

        // The click that reached the icon gave the foreground to the shell, which deactivated the
        // widget - so there may be a dismissal pending against the very action asking for it.
        _dismiss.Stop();

        Show();
        WindowState = WindowState.Normal;
        Activate();
        Dispatcher.BeginInvoke(new Action(() => FooterRefreshButton.Focus()), DispatcherPriority.Input);

        // Hidden-to-tray is the primary operating mode: provider polling continues at full rate so
        // the glyph and quota notifications remain current. Only unseen presentation work slows.
        UpdateTickCadence();
        OnTick(this, EventArgs.Empty);
    }

    /// <summary>
    /// Replaces the widget with the edge-docked strip. The pair is mutually exclusive, so nothing
    /// here has to decide which window owns the placement, the dismissal timer or the cadence.
    /// <para>
    /// The strip is built and the field assigned <em>before</em> the setting is announced, which is
    /// the same order <see cref="LeaveMiniMode"/> uses and for the same reason. Settings are
    /// announced synchronously, and <see cref="OnSettingsChanged"/> answers a raised MiniMode by
    /// calling straight back into here; announcing first re-entered this method with the field
    /// still null, so the inner call built and showed a strip that the outer call then overwrote
    /// with a second one. Only the tracked strip is ever closed, so the other stayed on screen for
    /// the life of the process - mini mode plainly visible while the setting, the settings
    /// checkbox and the tray menu's tick all said it was off.
    /// </para>
    /// </summary>
    public void EnterMiniMode()
    {
        if (_mini is not null)
        {
            return;
        }

        // Hide(), not HideToTray(): the balloon exists to explain a window that vanished, and this
        // one has not vanished - it is on the screen edge, which is where the user just sent it.
        Hide();
        _dismiss.Stop();

        _mini = new MiniWindow(_model, _settings);
        _mini.ExpandRequested += (_, _) => ShowFromTray();
        _mini.ContextMenuRequested += OnTrayContextMenuRequested;
        _mini.Show();

        _settings.Update(s => s with { MiniMode = true });
        UpdateTickCadence();
    }

    /// <summary>
    /// Closes the strip. Deliberately does not show the widget: <see cref="ShowFromTray"/> calls
    /// this, so doing so here would recurse. The field is nulled before the close so a Closed
    /// handler cannot find a strip that is on its way out.
    /// </summary>
    public void LeaveMiniMode()
    {
        if (_mini is null)
        {
            return;
        }

        MiniWindow strip = _mini;
        _mini = null;
        strip.Close();

        _settings.Update(s => s with { MiniMode = false });
        UpdateTickCadence();
    }

    /// <summary>
    /// The tick drives countdown strings and the tray glyph, so it runs at the visible rate when
    /// <em>either</em> window is on screen. A strip re-reading its countdowns once every five
    /// seconds is visibly wrong, and the strip is the window that is always visible.
    /// </summary>
    private void UpdateTickCadence() =>
        _tick.Interval = TickCadence.For(Visibility == Visibility.Visible || _mini is not null);

    /// <summary>
    /// Hiding, not closing. The process keeps polling, so the tray icon can go on saying something
    /// true. The first time only, a balloon says where the window went: a widget that vanishes with
    /// no explanation reads as a crash.
    /// </summary>
    public void HideToTray()
    {
        Hide();

        // The tray glyph and quota notifications are fed by polling, so their cadence is untouched;
        // a five-second lag on countdown strings nobody can see costs nothing.
        UpdateTickCadence();

        // Nothing has gone missing while the strip is up, so nothing needs explaining - and the
        // hint is shown once ever, which is too scarce to spend on a window that is still visible.
        if (_mini is null && !_settings.Current.TrayHintShown)
        {
            _tray?.Notify("AI Usage Monitor", "Still running in the notification area. Click the icon to bring it back.", silent: true);
            _settings.Update(s => s with { TrayHintShown = true });
        }
    }

    /// <summary>
    /// The one path that actually ends the process. Everything OnClosed used to own on a close now
    /// happens here, because a close no longer means an exit.
    /// </summary>
    public void ExitApplication()
    {
        _shuttingDown = true;
        Close();
        Application.Current.Shutdown();
    }

    private void TrayOpen_Click(object sender, RoutedEventArgs e) => ShowFromTray();

    private void TrayRefresh_Click(object sender, RoutedEventArgs e) => _ = _model.RefreshAsync(force: true, RefreshTrigger.ManualGlobal);

    private void TrayUpdate_Click(object sender, RoutedEventArgs e) => Update_Click(sender, e);

    private void TraySettings_Click(object sender, RoutedEventArgs e)
    {
        ShowFromTray();
        ShowSettings();
    }

    private void TrayDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        ShowFromTray();
        ShowDiagnostics();
    }

    /// <summary>
    /// Reads the item's own new state rather than negating the setting, so the tick the user just
    /// saw and the mode they end up in are the same answer.
    /// </summary>
    private void TrayMini_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item)
        {
            return;
        }

        if (item.IsChecked)
        {
            EnterMiniMode();
            return;
        }

        ShowFromTray();
    }

    private void TrayExit_Click(object sender, RoutedEventArgs e) => ExitApplication();

    private void Close_Click(object sender, RoutedEventArgs e) => HideToTray();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ShouldDismissOnEscape(_settings.Current.AlwaysOnTop, Visibility == Visibility.Visible))
        {
            HideToTray();
            e.Handled = true;
        }
    }

    public static bool ShouldDismissOnEscape(bool isPinned, bool isVisible) => !isPinned && isVisible;

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Alt+F4 and the system menu both reach here. Neither should end a widget that lives in the
        // tray; only the tray's own Exit does.
        if (!_shuttingDown)
        {
            e.Cancel = true;
            HideToTray();
        }

        base.OnClosing(e);
    }

    private void RestorePlacement(AppSettings settings)
    {
        if (settings.WindowLeft is not double left || settings.WindowTop is not double top)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left;
        Top = top;
    }

    private void ClampPlacement()
    {
        Rect fitted = PlacementClamp.Fit(
            new Rect(Left, Top, ActualWidth, ActualHeight),
            ScreenBounds.WorkAreaFor(this));

        if (fitted.Left != Left)
        {
            Left = fitted.Left;
        }

        if (fitted.Top != Top)
        {
            Top = fitted.Top;
        }
    }

    private void SavePlacement() =>
        _settings.Update(s => s with { WindowLeft = Left, WindowTop = Top });

    private void UpdateGlobalHotkeyRegistration(AppSettings settings)
    {
        UnregisterGlobalHotkey();
        _globalHotkeyUnavailable = false;

        if (!settings.GlobalHotkeyEnabled)
        {
            return;
        }

        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (RegisterHotKey(handle, GlobalHotkeyId, HotkeyControl | HotkeyAlt | HotkeyNoRepeat, VirtualKeyQ))
        {
            _globalHotkeyRegistered = true;
            return;
        }

        _globalHotkeyUnavailable = true;
        Trace.TraceInformation("Global hotkey Ctrl+Alt+Q is unavailable because another application already uses it.");
    }

    private void UnregisterGlobalHotkey()
    {
        if (!_globalHotkeyRegistered)
        {
            return;
        }

        _ = UnregisterHotKey(new WindowInteropHelper(this).Handle, GlobalHotkeyId);
        _globalHotkeyRegistered = false;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int GlobalHotkeyId = 1;
    private const uint HotkeyControl = 0x0002;
    private const uint HotkeyAlt = 0x0001;
    private const uint HotkeyNoRepeat = 0x4000;
    private const uint VirtualKeyQ = 0x51;
    private const uint WindowMessageHotkey = 0x0312;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    private delegate void WinEventProc(
        IntPtr hook, uint id, IntPtr window, int objectId, int childId, uint thread, uint time);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr module, WinEventProc callback, uint idProcess, uint idThread, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);
}
