using System.Windows;
using System.Windows.Input;
using AiUsageMonitor.App.Interop;
using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Infrastructure.Settings;
using Microsoft.Win32;

namespace AiUsageMonitor.App.Views;

public partial class MiniWindow : Window
{
    private readonly SettingsService _settings;
    private bool _systemEventsSubscribed;

    public MiniWindow(MainViewModel model, SettingsService settings)
    {
        _settings = settings;
        InitializeComponent();
        DataContext = model;
        DpiChanged += (_, _) => ApplyPlacement();
        ContentRendered += (_, _) => ApplyPlacement();
        try { SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged; _systemEventsSubscribed = true; }
        catch { SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged; }
    }

    public event EventHandler? ExpandRequested;
    public event EventHandler? ContextMenuRequested;

    public void ApplyPlacement()
    {
        Point point = MiniPlacement.Fit(new Size(ActualWidth, ActualHeight), ScreenBounds.WorkAreaFor(this), _settings.Current.MiniDock, _settings.Current.MiniLeft);
        Left = point.X;
        Top = point.Y;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_systemEventsSubscribed) SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        base.OnClosed(e);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(ApplyPlacement);
    private void Expand_Click(object sender, RoutedEventArgs e) => ExpandRequested?.Invoke(this, EventArgs.Empty);
    private void Background_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 1) DragMove(); }
    private void Background_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { ApplyPlacement(); _settings.Update(s => s with { MiniLeft = Left }); }
    private void Background_MouseRightButtonUp(object sender, MouseButtonEventArgs e) => ContextMenuRequested?.Invoke(this, EventArgs.Empty);
    private void Background_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ExpandRequested?.Invoke(this, EventArgs.Empty);
}
