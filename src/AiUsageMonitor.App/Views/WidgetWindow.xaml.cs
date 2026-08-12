using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using AiUsageMonitor.App.Interop;
using AiUsageMonitor.App.Notifications;
using AiUsageMonitor.App.Theming;
using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Infrastructure.Logging;
using AiUsageMonitor.Infrastructure.Refresh;
using AiUsageMonitor.Infrastructure.Settings;
using AiUsageMonitor.Infrastructure.Theming;

namespace AiUsageMonitor.App.Views;

public partial class WidgetWindow : Window
{
    private readonly MainViewModel _model;
    private readonly SettingsService _settings;
    private readonly ProviderRefreshService? _refresh;
    private readonly ThemeManager? _theme;
    private SettingsWindow? _settingsWindow;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _poll = new();
    private readonly UsageAlertWatcher _alerts = new();
    private TrayIcon? _tray;
    private TrayGlyphState _glyph = TrayGlyphState.Empty;
    private ThemeVariant? _glyphVariant;
    private bool _shuttingDown;

    public WidgetWindow(MainViewModel model, SettingsService settings, ProviderRefreshService? refresh = null, ThemeManager? theme = null)
    {
        _model = model;
        _settings = settings;
        _refresh = refresh;
        _theme = theme;

        InitializeComponent();
        DataContext = model;

        Topmost = settings.Current.AlwaysOnTop;
        RestorePlacement(settings.Current);

        _tick.Tick += OnTick;
        _poll.Interval = settings.Current.RefreshInterval;
        _poll.Tick += (_, _) => _ = _model.RefreshAsync(force: false);

        _settings.Changed += OnSettingsChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        IntPtr handle = new WindowInteropHelper(this).Handle;
        DwmWindowChrome.UseRoundedCorners(handle);
        ApplyTitleBarTheme(handle);

        _tray = new TrayIcon(this, "Quota Monitor");
        _tray.Activated += (_, _) => ShowFromTray();
        _tray.ContextMenuRequested += OnTrayContextMenuRequested;
        _tray.Show();

        HwndSource.FromHwnd(handle)?.AddHook(OnWindowMessage);

        if (_theme is not null)
        {
            _theme.Changed += OnThemeChanged;
        }
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        _tick.Start();
        _poll.Start();
        _ = _model.RefreshAsync(force: true);
    }

    protected override void OnClosed(EventArgs e)
    {
        _tick.Stop();
        _poll.Stop();

        // Named rather than a lambda so it can actually be detached. ThemeManager outlives this
        // window - it is a singleton owned by the application - so a subscription left behind
        // would keep calling back into a closed window for the rest of the process's life.
        if (_theme is not null)
        {
            _theme.Changed -= OnThemeChanged;
        }

        _settings.Changed -= OnSettingsChanged;

        SavePlacement();
        _tray?.Dispose();
        _model.Dispose();
        base.OnClosed(e);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _model.Tick();
        UpdateTrayGlyph();

        foreach (UsageAlert alert in _alerts.Observe(_model.Providers))
        {
            if (_settings.Current.NotifyOnQuotaEvents)
            {
                _tray?.Notify(alert.Title, alert.Text, alert.IsSilent);
            }
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

    /// <summary>
    /// Everything a settings change reaches from here. The poll timer's interval is reassigned
    /// rather than the timer restarted: DispatcherTimer applies a new interval on its next tick,
    /// which is the behaviour wanted - a user shortening the interval should not also force an
    /// immediate provider call.
    /// </summary>
    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        Topmost = settings.AlwaysOnTop;
        _poll.Interval = settings.RefreshInterval;

        if (_refresh is not null)
        {
            _refresh.BaseInterval = settings.RefreshInterval;
        }

        _theme?.Apply(settings.Theme);
        _model.ApplySettings(settings);
        UpdateTrayGlyph();
    }

    /// <summary>Opens the settings window, or focuses the one already open.</summary>
    public void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        SettingsViewModel model = new(
            _settings,
            StartupRegistration.ForThisProcess(),
            resetPosition: ResetPlacement,
            recheckProviders: () => _ = _model.RefreshAsync(force: true),
            openLogs: OpenLogsFolder);

        _settingsWindow = new SettingsWindow(model) { Owner = this };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void ResetPlacement()
    {
        _settings.Update(s => s with { WindowLeft = null, WindowTop = null });
        Left = (SystemParameters.WorkArea.Width - Width) / 2;
        Top = (SystemParameters.WorkArea.Height - ActualHeight) / 2;
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
    /// Written through the settings service rather than by assigning <see cref="Window.Topmost"/>
    /// directly, so the choice persists and the settings window's own checkbox follows it. The
    /// window's Topmost is then set by <see cref="OnSettingsChanged"/>, on the one path that
    /// already owns it.
    /// </summary>
    private void Pin_Click(object sender, RoutedEventArgs e) =>
        _settings.Update(s => s with { AlwaysOnTop = !s.AlwaysOnTop });

    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == SingleInstance.ShowMessage)
        {
            ShowFromTray();
            handled = true;
        }

        return IntPtr.Zero;
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

        menu.PlacementTarget = this;
        menu.Placement = PlacementMode.MousePoint;
        menu.StaysOpen = false;
        SetForegroundWindow(new WindowInteropHelper(this).Handle);
        menu.IsOpen = true;
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>
    /// Hiding, not closing. The process keeps polling, so the tray icon can go on saying something
    /// true. The first time only, a balloon says where the window went: a widget that vanishes with
    /// no explanation reads as a crash.
    /// </summary>
    public void HideToTray()
    {
        Hide();

        if (!_settings.Current.TrayHintShown)
        {
            _tray?.Notify("Quota Monitor", "Still running in the notification area. Click the icon to bring it back.", silent: true);
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

    private void TrayRefresh_Click(object sender, RoutedEventArgs e) => _ = _model.RefreshAsync(force: true);

    private void TraySettings_Click(object sender, RoutedEventArgs e)
    {
        ShowFromTray();
        ShowSettings();
    }

    private void TrayExit_Click(object sender, RoutedEventArgs e) => ExitApplication();

    private void Close_Click(object sender, RoutedEventArgs e) => HideToTray();

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

        // A saved position on a monitor that has since been unplugged would put the window
        // somewhere the user cannot reach it (PRD §17). Fall back to centring rather than trusting it.
        Rect desktop = new(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        Rect proposed = new(left, top, Width, 100);

        if (!desktop.IntersectsWith(proposed))
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left;
        Top = top;
    }

    private void SavePlacement() =>
        _settings.Update(s => s with { WindowLeft = Left, WindowTop = Top });

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
