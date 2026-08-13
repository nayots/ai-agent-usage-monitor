using System.Windows;
using AiUsageMonitor.Infrastructure.Settings;

namespace AiUsageMonitor.App.Interop;

public static class MiniPlacement
{
    /// <summary>Where a strip of <paramref name="size"/> sits on <paramref name="workArea"/>.</summary>
    public static Point Fit(Size size, Rect workArea, MiniDock dock, double? desiredLeft)
    {
        double right = workArea.Right - size.Width;
        double left = size.Width >= workArea.Width
            ? workArea.Left
            : Math.Clamp(desiredLeft ?? right, workArea.Left, right);
        return new Point(left, dock == MiniDock.Bottom ? workArea.Bottom - size.Height : workArea.Top);
    }
}
