using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace AiUsageMonitor.App.Interop;

/// <summary>
/// The usable part of the monitor a window is on — the monitor less the taskbar and anything else
/// docked to an edge — in the device-independent pixels WPF measures and positions in.
/// <para>
/// <see cref="SystemParameters.WorkArea"/> is not this: it only ever answers for the primary
/// monitor, so a window sized or nudged against it is measured against the wrong screen the moment
/// it is on a second one.
/// </para>
/// </summary>
internal static class ScreenBounds
{
    private const uint NearestMonitor = 0x00000002;

    /// <summary>
    /// The work area of the monitor <paramref name="window"/> is on. Falls back to the primary
    /// monitor's when the window has no handle yet, or when the shell declines to answer: both
    /// leave the caller with a usable rectangle rather than nothing, and on the single-monitor
    /// machine that most of these are, the fallback is the same rectangle.
    /// </summary>
    public static Rect WorkAreaFor(Window window)
    {
        if (PresentationSource.FromVisual(window) is not HwndSource source || source.CompositionTarget is null)
        {
            return SystemParameters.WorkArea;
        }

        IntPtr monitor = MonitorFromWindow(source.Handle, NearestMonitor);
        MonitorInfo info = new() { Size = Marshal.SizeOf<MonitorInfo>() };

        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            return SystemParameters.WorkArea;
        }

        // Win32 answers in physical pixels. WPF's Left, Top, Width and MaxHeight are all in DIPs,
        // and the two are the same number only at 100% scaling - at 150% an unconverted work area
        // is half as tall again as the screen really is.
        Matrix toDips = source.CompositionTarget.TransformFromDevice;

        return new Rect(
            toDips.Transform(new Point(info.WorkLeft, info.WorkTop)),
            toDips.Transform(new Point(info.WorkRight, info.WorkBottom)));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public int MonitorLeft;
        public int MonitorTop;
        public int MonitorRight;
        public int MonitorBottom;
        public int WorkLeft;
        public int WorkTop;
        public int WorkRight;
        public int WorkBottom;
        public uint Flags;
    }

    // DllImport rather than LibraryImport, for the reason given in DwmWindowChrome: the generator
    // buys nothing for blittable signatures and would pull AllowUnsafeBlocks into the project.
#pragma warning disable SYSLIB1054
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
#pragma warning restore SYSLIB1054
}
