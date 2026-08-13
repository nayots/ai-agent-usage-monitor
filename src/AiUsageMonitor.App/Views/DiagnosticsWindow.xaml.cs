using System.Windows;
using AiUsageMonitor.App.Interop;
using AiUsageMonitor.App.ViewModels;

namespace AiUsageMonitor.App.Views;

public partial class DiagnosticsWindow : Window
{
    public DiagnosticsWindow(DiagnosticsViewModel model)
    {
        InitializeComponent();
        DataContext = model;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        MaxHeight = ScreenBounds.WorkAreaFor(this).Height;
    }
}
