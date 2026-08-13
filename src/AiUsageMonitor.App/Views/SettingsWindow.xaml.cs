using System.Windows;
using AiUsageMonitor.App.Interop;
using AiUsageMonitor.App.ViewModels;

namespace AiUsageMonitor.App.Views;

/// <summary>
/// Settings apply as they are changed. There is no OK, Cancel or Apply: the widget is visible
/// behind this window, so every change is already on screen by the time the user could press one,
/// and a commit step would only add a way to be wrong about whether a change had taken.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsShellViewModel _model;

    public SettingsWindow(SettingsShellViewModel model)
    {
        _model = model;
        InitializeComponent();
        DataContext = model;

        // Before the first layout pass, so the size below is the one the window is measured at
        // rather than a resize the user sees happen.
        if (model.RememberedWidth is double width)
        {
            Width = width;
        }

        if (model.RememberedHeight is double height)
        {
            Height = height;
        }
    }

    /// <summary>
    /// Caps the window at the screen it is opening on. The window no longer sizes itself to its
    /// content, but a size remembered from a larger monitor is the same problem wearing a different
    /// hat: set before the first layout pass, this is the bound that measurement obeys.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        Rect screen = ScreenBounds.WorkAreaFor(this);
        MaxHeight = screen.Height;
        MaxWidth = screen.Width;
    }

    /// <summary>
    /// Brings the window back inside the screen as soon as it knows how big it is.
    /// <para>
    /// <c>WindowStartupLocation="CenterOwner"</c> centres this window on the widget and stops
    /// there: it knows nothing of screen edges, so a window opened from a widget parked near the
    /// bottom of the screen would hang off it, with the sidebar's last entries unreachable.
    /// </para>
    /// </summary>
    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);

        Rect screen = ScreenBounds.WorkAreaFor(this);

        // ActualHeight, not info.NewSize.Height: a Window's ActualWidth and ActualHeight are its
        // outer size, title bar and borders included, and it is the outer rectangle that has to fit
        // on the screen.
        //
        // Math.Max, not Math.Clamp alone: on a screen too short for the window the low bound would
        // exceed the high one, and Math.Clamp throws rather than picking a side.
        Top = Math.Clamp(Top, screen.Top, Math.Max(screen.Top, screen.Bottom - ActualHeight));
        Left = Math.Clamp(Left, screen.Left, Math.Max(screen.Left, screen.Right - ActualWidth));
    }

    protected override void OnClosed(EventArgs e)
    {
        // RestoreBounds, not ActualWidth, when the window is not in its normal state: a window
        // closed while maximised reports the maximised size, and reopening at that size next time
        // would be a size the user never chose.
        Rect bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, ActualWidth, ActualHeight)
            : RestoreBounds;

        _model.RememberSize(bounds.Width, bounds.Height);
        _model.Dispose();
        base.OnClosed(e);
    }
}
