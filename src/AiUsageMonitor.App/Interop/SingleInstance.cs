using System.Runtime.InteropServices;
using System.Threading;

namespace AiUsageMonitor.App.Interop;

/// <summary>
/// One running widget per user session.
/// <para>
/// This matters more than it looks: with Start with Windows on and the window hidden in the tray,
/// launching the app again would otherwise start a second copy with a second tray icon, while the
/// click that started it appeared to do nothing at all.
/// </para>
/// </summary>
public sealed class SingleInstance : IDisposable
{
    /// <summary>
    /// Broadcast by a second instance to ask the first to show itself. Registered rather than
    /// invented, so the value cannot collide with another application's private message.
    /// </summary>
    public static readonly uint ShowMessage = RegisterWindowMessage("AiUsageMonitor.Show");

    /// <summary>
    /// Broadcast by a replacing instance to ask the running one to exit. Separate from
    /// <see cref="ShowMessage"/> because the two mean opposite things and a receiver must never
    /// guess which was meant.
    /// </summary>
    public static readonly uint QuitMessage = RegisterWindowMessage("AiUsageMonitor.Quit");

    private const int HWND_BROADCAST = 0xFFFF;

    private readonly Mutex _mutex;

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    /// <summary>
    /// True when this process is the first. The mutex is session-local, not global: two users
    /// logged into the same machine each get their own widget.
    /// </summary>
    public static bool TryAcquire(string name, out SingleInstance? instance)
    {
        Mutex mutex = new(initiallyOwned: true, name, out bool created);

        if (!created)
        {
            mutex.Dispose();
            instance = null;
            return false;
        }

        instance = new SingleInstance(mutex);
        return true;
    }

    /// <summary>
    /// Posts to every top-level window of one process, returning false when it has none.
    /// <para>
    /// This exists because <see cref="Broadcast"/> cannot reach this application's own widget.
    /// <c>HWND_BROADCAST</c> is documented to reach only <em>unowned</em> top-level windows, and the
    /// widget sets <c>ShowInTaskbar="False"</c>, which makes WPF create a hidden owner window for it
    /// — so the visible window is owned and every broadcast passes it by. Verified live: a broadcast
    /// left the widget untouched where a post to its window shut it down immediately.
    /// </para>
    /// <para>
    /// Posting to all of the process's top-level windows rather than hunting for the right one is
    /// deliberate. The widget is hidden while it sits in the tray, WPF's own bookkeeping windows sit
    /// beside it, and a registered message means nothing to any window that did not register it.
    /// </para>
    /// </summary>
    public static bool PostToProcess(int processId, uint message)
    {
        bool posted = false;

        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out uint owner);

            if (owner == (uint)processId)
            {
                PostMessage(handle, message, IntPtr.Zero, IntPtr.Zero);
                posted = true;
            }

            return true;
        }, IntPtr.Zero);

        return posted;
    }

    /// <summary>
    /// Best effort only — see <see cref="PostToProcess"/> for why this cannot reach a widget that
    /// is already running. Kept for the one case with no process to aim at: a release that predates
    /// the instance record and so cannot be identified at all.
    /// </summary>
    public static void Broadcast(uint message) =>
        PostMessage(new IntPtr(HWND_BROADCAST), message, IntPtr.Zero, IntPtr.Zero);

    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
