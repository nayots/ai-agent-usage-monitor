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
/// </summary>
public sealed class TrayIconFrames : IDisposable
{
    /// <summary><see cref="Number"/> is <see cref="IntPtr.Zero"/> when the frame names itself always.</summary>
    private readonly record struct Pair(IntPtr Number, IntPtr Name);

    private readonly List<Pair> _pairs;
    private bool _disposed;

    private TrayIconFrames(List<Pair> pairs) => _pairs = pairs;

    public int Count => _pairs.Count;

    public static TrayIconFrames Build(TrayGlyphState state, int size, TrayGlyphPalette palette)
    {
        List<Pair> pairs = [];

        foreach (TrayGlyphFrame frame in state.Frames)
        {
            IntPtr name = TrayGlyphRenderer.Render(frame, showsName: true, size, palette);

            // A frame whose band always carries its name has nothing to alternate with, so it gets
            // one handle rather than two identical ones - and Dispose then cannot free it twice.
            IntPtr number = frame.NamesItselfAlways
                ? IntPtr.Zero
                : TrayGlyphRenderer.Render(frame, showsName: false, size, palette);

            pairs.Add(new Pair(number, name));
        }

        return new TrayIconFrames(pairs);
    }

    /// <summary>
    /// The handle for one frame. Borrowed, never transferred: the caller must not destroy it.
    /// Returns <see cref="IntPtr.Zero"/> for an index outside the set, which the caller treats as
    /// "keep the icon you have".
    /// </summary>
    public IntPtr Icon(int index, bool showsName)
    {
        if (_disposed || index < 0 || index >= _pairs.Count)
        {
            return IntPtr.Zero;
        }

        Pair pair = _pairs[index];
        return showsName || pair.Number == IntPtr.Zero ? pair.Name : pair.Number;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (Pair pair in _pairs)
        {
            if (pair.Name != IntPtr.Zero)
            {
                DestroyIcon(pair.Name);
            }

            if (pair.Number != IntPtr.Zero)
            {
                DestroyIcon(pair.Number);
            }
        }

        _pairs.Clear();
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
