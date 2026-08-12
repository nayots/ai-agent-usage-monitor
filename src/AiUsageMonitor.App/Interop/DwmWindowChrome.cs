using System.Runtime.InteropServices;

namespace AiUsageMonitor.App.Interop;

/// <summary>
/// Windows 11 window presentation that WPF does not expose: rounded corners and a title-bar
/// border that follows the app theme. Every call is best-effort — on a Windows build that does
/// not know an attribute, DWM returns a failure code and the window is square-cornered, which is
/// cosmetic. Nothing here requires elevation.
/// </summary>
internal static class DwmWindowChrome
{
    private const int UseImmersiveDarkMode = 20;
    private const int WindowCornerPreference = 33;
    private const int RoundedCorners = 2;

    // DllImport rather than LibraryImport: the signature is a single blittable int by reference,
    // the source generator buys nothing here, and LibraryImport would pull AllowUnsafeBlocks into
    // a project that has no other need for it.
#pragma warning disable SYSLIB1054
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
#pragma warning restore SYSLIB1054

    public static void UseRoundedCorners(IntPtr handle) => Set(handle, WindowCornerPreference, RoundedCorners);

    public static void UseDarkTitleBar(IntPtr handle, bool dark) => Set(handle, UseImmersiveDarkMode, dark ? 1 : 0);

    private static void Set(IntPtr handle, int attribute, int value)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            _ = DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Older Windows. The window still works; it is square-cornered.
        }
    }
}
