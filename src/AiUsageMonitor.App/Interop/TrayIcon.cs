using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Resources;

namespace AiUsageMonitor.App.Interop;

/// <summary>
/// A notification-area icon, hosted on an existing window's HWND.
/// <para>
/// Hand-rolled rather than taken from WinForms or a package, for one product reason: the context
/// menu stays an ordinary WPF <c>ContextMenu</c> and therefore honours all three palettes,
/// including high contrast. A <c>System.Windows.Forms.NotifyIcon</c> menu ignores them entirely.
/// </para>
/// <para>
/// The callback window is the owner's own HWND rather than a message-only window. A message-only
/// window cannot receive <c>HWND_BROADCAST</c>, which the single-instance handshake needs, and the
/// widget's window now lives for the whole process anyway.
/// </para>
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const int WM_TRAYICON = 0x0400 + 1024;   // WM_APP + 1024
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;

    private const int NIM_ADD = 0x0;
    private const int NIM_MODIFY = 0x1;
    private const int NIM_DELETE = 0x2;

    private const int NIF_MESSAGE = 0x1;
    private const int NIF_ICON = 0x2;
    private const int NIF_TIP = 0x4;
    private const int NIF_INFO = 0x10;

    private readonly Window _owner;
    private readonly string _tooltip;
    private readonly uint _taskbarCreated;
    private HwndSource? _source;
    private IntPtr _icon;
    private bool _added;
    private bool _disposed;

    public TrayIcon(Window owner, string tooltip)
    {
        _owner = owner;
        _tooltip = tooltip;
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    }

    /// <summary>Raised on left click: the user wants the widget.</summary>
    public event EventHandler? Activated;

    /// <summary>Raised on right click, on the UI thread, for the caller to open its own menu.</summary>
    public event EventHandler? ContextMenuRequested;

    /// <summary>
    /// Adds the icon. The owner window must already have an HWND, so call this no earlier than
    /// <c>OnSourceInitialized</c>.
    /// </summary>
    public void Show()
    {
        IntPtr handle = new WindowInteropHelper(_owner).Handle;

        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("The owner window has no handle yet.");
        }

        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);
        _icon = LoadIcon();

        Send(NIM_ADD);
        _added = true;
    }

    /// <summary>A one-off balloon. Used once, to say where the window went the first time it hides.</summary>
    public void ShowHint(string title, string text)
    {
        if (!_added)
        {
            return;
        }

        NOTIFYICONDATA data = Data(NIF_INFO);
        data.szInfoTitle = title;
        data.szInfo = text;
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_added)
        {
            Send(NIM_DELETE);
            _added = false;
        }

        _source?.RemoveHook(WndProc);
        _source = null;

        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Explorer restarts and every tray icon in the session is dropped. Without this the widget
        // is unreachable for the rest of the session, and the only way to end it is Task Manager.
        if (msg == _taskbarCreated && _added)
        {
            Send(NIM_ADD);
            return IntPtr.Zero;
        }

        if (msg != WM_TRAYICON)
        {
            return IntPtr.Zero;
        }

        switch ((int)lParam)
        {
            case WM_LBUTTONUP:
                Activated?.Invoke(this, EventArgs.Empty);
                handled = true;
                break;
            case WM_RBUTTONUP:
                ContextMenuRequested?.Invoke(this, EventArgs.Empty);
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    private void Send(int message)
    {
        NOTIFYICONDATA data = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        Shell_NotifyIcon(message, ref data);
    }

    /// <summary>
    /// Every field is assigned, including the ones this app never varies. An interop struct field
    /// that is never written raises CS0649, and warnings are errors here - so the padding fields
    /// are set explicitly rather than left to their defaults.
    /// </summary>
    private NOTIFYICONDATA Data(int flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = new WindowInteropHelper(_owner).Handle,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = WM_TRAYICON,
        hIcon = _icon,
        szTip = _tooltip,
        dwState = 0,
        dwStateMask = 0,
        szInfo = string.Empty,
        uVersion = 0,
        szInfoTitle = string.Empty,
        dwInfoFlags = 0
    };

    /// <summary>
    /// Loads the packed .ico and lets the shell pick the frame for the current DPI, rather than
    /// assuming 16x16 - which is wrong on every scaled display.
    /// </summary>
    private static IntPtr LoadIcon()
    {
        // Nullable: GetResourceStream is annotated to return null, and an unguarded dereference is
        // CS8602, which is an error here. A missing icon must not take the process down either -
        // a zero handle renders as the shell's default icon, which is worse than ours but visible.
        StreamResourceInfo? info = Application.GetResourceStream(
            new Uri("pack://application:,,,/AiUsageMonitor.App;component/Assets/app.ico", UriKind.Absolute));

        if (info?.Stream is null)
        {
            return IntPtr.Zero;
        }

        using MemoryStream buffer = new();
        info.Stream.CopyTo(buffer);

        string temporary = Path.Combine(Path.GetTempPath(), "aium-tray-" + Guid.NewGuid().ToString("N") + ".ico");
        File.WriteAllBytes(temporary, buffer.ToArray());

        try
        {
            int width = GetSystemMetrics(SM_CXSMICON);
            int height = GetSystemMetrics(SM_CYSMICON);
            return LoadImage(IntPtr.Zero, temporary, IMAGE_ICON, width, height, LR_LOADFROMFILE);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private const int SM_CXSMICON = 49;
    private const int SM_CYSMICON = 50;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x0010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint load);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
