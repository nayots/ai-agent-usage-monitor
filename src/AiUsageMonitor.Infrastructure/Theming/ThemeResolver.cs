using AiUsageMonitor.Infrastructure.Settings;

namespace AiUsageMonitor.Infrastructure.Theming;

/// <summary>The theme dictionary actually in force, after the user's preference meets the OS.</summary>
public enum ThemeVariant
{
    Light,
    Dark,
    HighContrast
}

public static class ThemeResolver
{
    /// <summary>
    /// High contrast always wins: it is an accessibility setting the user has switched on at the
    /// OS level, and an application-level theme preference does not get to override it.
    /// </summary>
    public static ThemeVariant Resolve(ThemePreference preference, bool systemUsesLightTheme, bool highContrast)
    {
        if (highContrast)
        {
            return ThemeVariant.HighContrast;
        }

        return preference switch
        {
            ThemePreference.Light => ThemeVariant.Light,
            ThemePreference.Dark => ThemeVariant.Dark,
            _ => systemUsesLightTheme ? ThemeVariant.Light : ThemeVariant.Dark
        };
    }
}
