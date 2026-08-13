using System.Windows;

namespace AiUsageMonitor.App.Interop;

public static class PlacementClamp
{
    /// <summary>
    /// The window rectangle moved - never resized - so it sits wholly inside <paramref name="workArea"/>.
    /// A window larger than the work area is aligned to its top-left, so the title bar stays reachable.
    /// Pure, so the rule is testable without a monitor.
    /// </summary>
    public static Rect Fit(Rect window, Rect workArea)
    {
        if (window.Width > workArea.Width || window.Height > workArea.Height)
        {
            return new Rect(workArea.TopLeft, window.Size);
        }

        return new Rect(
            Math.Clamp(window.Left, workArea.Left, workArea.Right - window.Width),
            Math.Clamp(window.Top, workArea.Top, workArea.Bottom - window.Height),
            window.Width,
            window.Height);
    }
}
