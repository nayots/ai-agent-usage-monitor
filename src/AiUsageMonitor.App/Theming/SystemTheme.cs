using Microsoft.Win32;
using System.IO;

namespace AiUsageMonitor.App.Theming;

/// <summary>
/// Reads the current Windows appearance settings. Read-only and per-user: this application
/// never writes a system setting and never needs elevation to read one.
/// </summary>
public static class SystemTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>
    /// True when Windows is set to a light app theme. Defaults to true when the value is absent
    /// or unreadable, matching the Windows default rather than guessing dark.
    /// </summary>
    public static bool UsesLightTheme
    {
        get
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return true;
            }
        }
    }

    public static bool IsHighContrast => System.Windows.SystemParameters.HighContrast;
}
