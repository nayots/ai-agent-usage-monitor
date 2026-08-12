using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using AiUsageMonitor.App.Interop;
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

        _tick.Tick += (_, _) => _model.Tick();
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
        _model.Dispose();
        base.OnClosed(e);
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

    private void Minimise_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

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
}
