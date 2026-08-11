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

    [Fact]
    public void RoundTripsEveryProperty()
    {
        using TempDirectory dir = new();
        AppSettingsStore store = new(dir.File("settings.json"));
        AppSettings written = new()
        {
            Theme = ThemePreference.Dark,
            ColorBarsByUsage = false,
            AlwaysOnTop = true,
            StartWithWindows = true,
            Density = WidgetDensity.Compact,
            ShowUnavailableProviders = false,
            StaleAfterSeconds = 90
        };

        store.Save(written);

        Assert.Equal(written, store.Load().Settings);
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
}
