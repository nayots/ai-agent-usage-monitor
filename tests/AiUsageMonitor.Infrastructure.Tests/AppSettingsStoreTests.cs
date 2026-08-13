using AiUsageMonitor.Infrastructure.Settings;

namespace AiUsageMonitor.Infrastructure.Tests;

public class AppSettingsStoreTests
{
    [Fact]
    public void DefaultsMatchTheProductRequirements()
    {
        AppSettings defaults = AppSettings.Default;

        Assert.Equal(ThemePreference.System, defaults.Theme);
        Assert.True(defaults.ColorBarsByUsage);
        Assert.False(defaults.AlwaysOnTop);
        Assert.False(defaults.StartWithWindows);
        Assert.Equal(WidgetDensity.Normal, defaults.Density);
        Assert.True(defaults.ShowUnavailableProviders);
        Assert.Equal(300, defaults.StaleAfterSeconds);
    }

    [Fact]
    public void MissingFileLoadsDefaultsAndWritesNothing()
    {
        using TempDirectory dir = new();
        AppSettingsStore store = new(dir.File("settings.json"));

        SettingsLoadResult result = store.Load();

        Assert.Equal(AppSettings.Default, result.Settings);
        Assert.Null(result.CorruptBackupPath);
        Assert.False(File.Exists(dir.File("settings.json")));
    }

    /// <summary>
    /// Pinning is what someone does while watching a quota drain, not how they want the widget to
    /// behave forever. It is the one property here that must not come back - which also means the
    /// round-trip test above cannot cover it, since equality would fail on it either way.
    /// </summary>
    [Fact]
    public void PinningIsSessionStateAndDoesNotSurviveASave()
    {
        using TempDirectory dir = new();
        AppSettingsStore store = new(dir.File("settings.json"));

        store.Save(AppSettings.Default with { AlwaysOnTop = true });

        Assert.False(store.Load().Settings.AlwaysOnTop);
        Assert.DoesNotContain("AlwaysOnTop", File.ReadAllText(dir.File("settings.json")));
    }

    /// <summary>
    /// A settings file written before pinning stopped being persisted, or edited by hand. The key
    /// is not an error and must not trip the corrupt-file path - it is simply ignored.
    /// </summary>
    [Fact]
    public void APinLeftInAnOlderSettingsFileIsIgnoredRatherThanHonoured()
    {
        using TempDirectory dir = new();
        string path = dir.File("settings.json");
        File.WriteAllText(path, """{ "AlwaysOnTop": true, "StaleAfterSeconds": 90 }""");

        SettingsLoadResult result = new AppSettingsStore(path).Load();

        Assert.False(result.Settings.AlwaysOnTop);
        Assert.Equal(90, result.Settings.StaleAfterSeconds);
        Assert.Null(result.CorruptBackupPath);
    }

    [Fact]
    public void RoundTripsEveryProperty()
    {
        using TempDirectory dir = new();
        AppSettingsStore store = new(dir.File("settings.json"));
        AppSettings written = new()
        {
            Theme = ThemePreference.Dark,
            ColorBarsByUsage = false,
            StartWithWindows = true,
            Density = WidgetDensity.Compact,
            ShowUnavailableProviders = false,
            StaleAfterSeconds = 90,
            TrayHintShown = true
        };

        store.Save(written);

        Assert.Equal(written, store.Load().Settings);
        Assert.True(store.Load().Settings.TrayHintShown);
    }

    [Fact]
    public void EnumsPersistAsNamesNotNumbers()
    {
        using TempDirectory dir = new();
        AppSettingsStore store = new(dir.File("settings.json"));

        store.Save(AppSettings.Default with { Theme = ThemePreference.Dark });

        string json = File.ReadAllText(dir.File("settings.json"));
        Assert.Contains("\"Dark\"", json);
        Assert.DoesNotContain("\"Theme\": 2", json);
    }

    [Fact]
    public void CorruptFileIsMovedAsideAndDefaultsAreReturned()
    {
        using TempDirectory dir = new();
        string path = dir.File("settings.json");
        File.WriteAllText(path, "{ this is not json");
        AppSettingsStore store = new(path);

        SettingsLoadResult result = store.Load();

        Assert.Equal(AppSettings.Default, result.Settings);
        Assert.NotNull(result.CorruptBackupPath);
        Assert.True(File.Exists(result.CorruptBackupPath));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void UnknownPropertiesAreIgnored()
    {
        using TempDirectory dir = new();
        string path = dir.File("settings.json");
        File.WriteAllText(path, """{ "Theme": "Dark", "SomeFutureSetting": 42 }""");
        AppSettingsStore store = new(path);

        SettingsLoadResult result = store.Load();

        Assert.Equal(ThemePreference.Dark, result.Settings.Theme);
        Assert.Null(result.CorruptBackupPath);
    }

    [Fact]
    public void SaveCreatesMissingDirectoriesAndLeavesNoTempFile()
    {
        using TempDirectory dir = new();
        string path = Path.Combine(dir.Path, "nested", "deeper", "settings.json");
        AppSettingsStore store = new(path);

        store.Save(AppSettings.Default);

        Assert.True(File.Exists(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(-5, 30)]
    [InlineData(300, 300)]
    [InlineData(99999, 3600)]
    public void StaleAfterClampsToASaneRange(int configured, int expectedSeconds)
    {
        AppSettings settings = AppSettings.Default with { StaleAfterSeconds = configured };

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), settings.StaleAfter);
    }

    [Fact]
    public void DefaultPathIsUnderTheCurrentUsersRoamingProfile()
    {
        string expectedRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        Assert.StartsWith(expectedRoot, AppSettingsStore.DefaultPath);
        Assert.EndsWith("settings.json", AppSettingsStore.DefaultPath);
    }

    [Fact]
    public void RefreshIntervalDefaultsToOneMinute() =>
        Assert.Equal(TimeSpan.FromMinutes(1), AppSettings.Default.RefreshInterval);

    [Theory]
    [InlineData(0, 15)]
    [InlineData(-5, 15)]
    [InlineData(14, 15)]
    [InlineData(15, 15)]
    [InlineData(90, 90)]
    [InlineData(3600, 3600)]
    [InlineData(99999, 3600)]
    public void RefreshIntervalIsClampedRatherThanRejected(int configured, int expectedSeconds)
    {
        // A hand-edited settings file must never stop the application starting, and a zero-second
        // interval would poll a provider in a tight loop.
        AppSettings settings = AppSettings.Default with { RefreshIntervalSeconds = configured };

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), settings.RefreshInterval);
    }

    [Fact]
    public void WindowPlacementDefaultsToAbsentSoTheFirstRunIsCentred()
    {
        Assert.Null(AppSettings.Default.WindowLeft);
        Assert.Null(AppSettings.Default.WindowTop);
    }

    [Fact]
    public void WindowPlacementRoundTripsThroughTheStore()
    {
        using TempDirectory directory = new();
        AppSettingsStore store = new(Path.Combine(directory.Path, "settings.json"));

        store.Save(AppSettings.Default with { WindowLeft = 1234.5, WindowTop = -20 });

        AppSettings loaded = store.Load().Settings;
        Assert.Equal(1234.5, loaded.WindowLeft);
        Assert.Equal(-20, loaded.WindowTop);
    }
}
