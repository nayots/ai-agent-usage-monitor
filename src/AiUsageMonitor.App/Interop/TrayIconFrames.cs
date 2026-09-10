using System.Linq;
using System.Runtime.InteropServices;
using AiUsageMonitor.App.ViewModels;

namespace AiUsageMonitor.App.Interop;

/// <summary>
/// Every frame of the current data, pre-rendered as an icon handle.
/// <para>
/// Rotation is a swap, not a redraw. Rebuilding the bitmap on every turn would be twenty-one
/// thousand icon handles a day at a four-second dwell; building them once per data change and
/// swapping among them is two or three. The set owns every handle it holds, which is why
/// <see cref="TrayIcon.SetIcon"/> has to be told not to take ownership of one.
/// </para>
/// <para>
/// One handle per frame. There used to be two - a turn opened on the provider's monogram and then
/// swapped to the number - but a frame carries its own name now, so there is nothing to alternate.
/// </para>
/// </summary>
public sealed class TrayIconFrames : IDisposable
{
    private readonly List<IntPtr> _icons;
    private bool _disposed;

    private TrayIconFrames(List<IntPtr> icons) => _icons = icons;

    public int Count => _icons.Count;

    public static TrayIconFrames Build(TrayGlyphState state, int size, TrayGlyphPalette palette) =>
        new([.. state.Frames.Select(frame => TrayGlyphRenderer.Render(frame, size, palette))]);

    /// <summary>
    /// The handle for one frame. Borrowed, never transferred: the caller must not destroy it.
    /// Returns <see cref="IntPtr.Zero"/> for an index outside the set, which the caller treats as
    /// "keep the icon you have".
    /// </summary>
    public IntPtr Icon(int index) =>
        _disposed || index < 0 || index >= _icons.Count ? IntPtr.Zero : _icons[index];

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (IntPtr icon in _icons.Where(icon => icon != IntPtr.Zero))
        {
            DestroyIcon(icon);
        }

        _icons.Clear();
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
