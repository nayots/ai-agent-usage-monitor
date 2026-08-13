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
    private readonly SettingsViewModel _model;

    public SettingsWindow(SettingsViewModel model)
    {
        _model = model;
        InitializeComponent();
        DataContext = model;
    }

    /// <summary>
    /// Caps the window at the screen it is opening on, so that <c>SizeToContent</c> can take it as
    /// tall as its content needs and no taller. Set here rather than in markup because the screen
    /// is not known until the window has a handle, and set before the first layout pass, which is
    /// what makes it the cap the size-to-content measurement obeys.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        MaxHeight = ScreenBounds.WorkAreaFor(this).Height;
    }

    /// <summary>
    /// Brings the window back inside the screen as soon as it knows how big it is - which is here,
    /// in the same layout pass that grew it to its content, and so before it is ever painted at a
    /// position half off the screen.
    /// <para>
    /// <c>WindowStartupLocation="CenterOwner"</c> centres this window on the widget and stops
    /// there: it knows nothing of screen edges, so a tall settings window opened from a widget
    /// parked near the bottom of the screen would hang off it - the last section unreachable, which
    /// is worse than the scrollbar this window no longer has.
    /// </para>
    /// </summary>
    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);

        Rect screen = ScreenBounds.WorkAreaFor(this);

        // ActualHeight, not info.NewSize.Height: a Window's ActualWidth and ActualHeight are its
        // outer size, title bar and borders included, and it is the outer rectangle that has to fit
        // on the screen. NewSize is the content's, some 39 device-independent pixels shorter.
        //
        // Math.Max, not Math.Clamp alone: on a screen too short for the window the low bound would
        // exceed the high one, and Math.Clamp throws rather than picking a side.
        Top = Math.Clamp(Top, screen.Top, Math.Max(screen.Top, screen.Bottom - ActualHeight));
        Left = Math.Clamp(Left, screen.Left, Math.Max(screen.Left, screen.Right - ActualWidth));
    }

    protected override void OnClosed(EventArgs e)
    {
        _model.Dispose();
        base.OnClosed(e);
    }
}
