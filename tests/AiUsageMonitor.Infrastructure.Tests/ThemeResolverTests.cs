using AiUsageMonitor.Infrastructure.Settings;
using AiUsageMonitor.Infrastructure.Theming;

namespace AiUsageMonitor.Infrastructure.Tests;

public class ThemeResolverTests
{
    [Theory]
    [InlineData(ThemePreference.System)]
    [InlineData(ThemePreference.Light)]
    [InlineData(ThemePreference.Dark)]
    public void HighContrastOverridesEveryPreference(ThemePreference preference)
    {
        Assert.Equal(
            ThemeVariant.HighContrast,
            ThemeResolver.Resolve(preference, systemUsesLightTheme: true, highContrast: true));
    }

    [Fact]
    public void ExplicitLightIgnoresADarkSystem() =>
        Assert.Equal(
            ThemeVariant.Light,
            ThemeResolver.Resolve(ThemePreference.Light, systemUsesLightTheme: false, highContrast: false));

    [Fact]
    public void ExplicitDarkIgnoresALightSystem() =>
        Assert.Equal(
            ThemeVariant.Dark,
            ThemeResolver.Resolve(ThemePreference.Dark, systemUsesLightTheme: true, highContrast: false));

    [Theory]
    [InlineData(true, ThemeVariant.Light)]
    [InlineData(false, ThemeVariant.Dark)]
    public void SystemFollowsTheOperatingSystem(bool systemUsesLightTheme, ThemeVariant expected) =>
        Assert.Equal(expected, ThemeResolver.Resolve(ThemePreference.System, systemUsesLightTheme, highContrast: false));
}
