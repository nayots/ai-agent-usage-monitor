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
    /// Asks whichever instance already exists to show itself. Posted rather than sent, so a first
    /// instance that is busy cannot block this one from exiting.
    /// </summary>
    public static void BroadcastShow() => PostMessage(new IntPtr(HWND_BROADCAST), ShowMessage, IntPtr.Zero, IntPtr.Zero);

    /// <summary>
    /// Asks whichever instance already exists to exit, so this one can take the mutex. Cooperative
    /// on purpose: the running copy shuts down through its own exit path, releasing its tray icon
    /// and flushing its settings, neither of which survives a terminated process.
    /// </summary>
    public static void BroadcastQuit() => PostMessage(new IntPtr(HWND_BROADCAST), QuitMessage, IntPtr.Zero, IntPtr.Zero);

    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
}
