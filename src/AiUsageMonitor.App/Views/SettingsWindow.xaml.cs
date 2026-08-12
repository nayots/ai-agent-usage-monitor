using System.Windows;
using AiUsageMonitor.App.ViewModels;

namespace AiUsageMonitor.App.Views;

/// <summary>
/// Settings apply as they are changed. There is no OK, Cancel or Apply: the widget is visible
/// behind this window, so every change is already on screen by the time the user could press one,
/// and a commit step would only add a way to be wrong about whether a change had taken.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _model;

    public SettingsWindow(SettingsViewModel model)
    {
        _model = model;
        InitializeComponent();
        DataContext = model;
    }

    protected override void OnClosed(EventArgs e)
    {
        _model.Dispose();
        base.OnClosed(e);
    }
}
