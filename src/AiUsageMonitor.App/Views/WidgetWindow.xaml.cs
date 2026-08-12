using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using AiUsageMonitor.App.Interop;
using AiUsageMonitor.App.Theming;
using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Infrastructure.Settings;
using AiUsageMonitor.Infrastructure.Theming;

namespace AiUsageMonitor.App.Views;

public partial class WidgetWindow : Window
{
    private readonly MainViewModel _model;
    private readonly AppSettings _settings;
    private readonly AppSettingsStore? _store;
    private readonly ThemeManager? _theme;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _poll = new();

    public WidgetWindow(MainViewModel model, AppSettings settings, AppSettingsStore? store = null, ThemeManager? theme = null)
    {
        _model = model;
        _settings = settings;
        _store = store;
        _theme = theme;

        InitializeComponent();
        DataContext = model;

        Topmost = settings.AlwaysOnTop;
        RestorePlacement(settings);

        _tick.Tick += (_, _) => _model.Tick();
        _poll.Interval = settings.RefreshInterval;
        _poll.Tick += (_, _) => _ = _model.RefreshAsync(force: false);
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

        SavePlacement();
        _model.Dispose();
        base.OnClosed(e);
    }

    private void OnThemeChanged(object? sender, EventArgs e) =>
        ApplyTitleBarTheme(new WindowInteropHelper(this).Handle);

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

    private void SavePlacement()
    {
        if (_store is null)
        {
            return;
        }

        try
        {
            _store.Save(_settings with { WindowLeft = Left, WindowTop = Top });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing a window position is not a reason to fail a shutdown.
        }
    }
}
