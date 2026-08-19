using AiUsageMonitor.Domain;
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
        Assert.True(defaults.ShowPaceProjection);
        Assert.False(defaults.AlwaysOnTop);
        Assert.False(defaults.StartWithWindows);
        Assert.True(defaults.GlobalHotkeyEnabled);
        Assert.Equal(WidgetDensity.Normal, defaults.Density);
        Assert.False(defaults.MiniMode);
        Assert.Equal(MiniDock.Top, defaults.MiniDock);
        Assert.Null(defaults.MiniLeft);
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
            ShowPaceProjection = false,
            StartWithWindows = true,
            GlobalHotkeyEnabled = false,
            Density = WidgetDensity.Compact,
            MiniMode = true,
            MiniDock = MiniDock.Bottom,
            MiniLeft = 1234.5,
            ShowUnavailableProviders = false,
            StaleAfterSeconds = 90,
            TrayHintShown = true
        };

        store.Save(written);

        AppSettings loaded = store.Load().Settings;
        Assert.Equal(written.Theme, loaded.Theme);
        Assert.Equal(written.ColorBarsByUsage, loaded.ColorBarsByUsage);
        Assert.Equal(written.ShowPaceProjection, loaded.ShowPaceProjection);
        Assert.Equal(written.StartWithWindows, loaded.StartWithWindows);
        Assert.Equal(written.GlobalHotkeyEnabled, loaded.GlobalHotkeyEnabled);
        Assert.Equal(written.Density, loaded.Density);
        Assert.Equal(written.MiniMode, loaded.MiniMode);
        Assert.Equal(written.MiniDock, loaded.MiniDock);
        Assert.Equal(written.MiniLeft, loaded.MiniLeft);
        Assert.Equal(written.ShowUnavailableProviders, loaded.ShowUnavailableProviders);
        Assert.Equal(written.StaleAfterSeconds, loaded.StaleAfterSeconds);
        Assert.True(loaded.TrayHintShown);
    }

    [Fact]
    public void EnumsPersistAsNamesNotNumbers()
    {
        using TempDirectory dir = new();
        AppSettingsStore store = new(dir.File("settings.json"));

        store.Save(AppSettings.Default with { Theme = ThemePreference.Dark, MiniDock = MiniDock.Bottom });

        string json = File.ReadAllText(dir.File("settings.json"));
        Assert.Contains("\"Dark\"", json);
        Assert.Contains("\"Bottom\"", json);
        Assert.DoesNotContain("\"Theme\": 2", json);
    }

    [Fact]
    public void AnUnknownMiniDockFallsBackToTopWithoutDiscardingTheSettingsFile()
    {
        using TempDirectory dir = new();
        string path = dir.File("settings.json");
        File.WriteAllText(path, """{ "MiniDock": "Sideways" }""");

        SettingsLoadResult result = new AppSettingsStore(path).Load();

        Assert.Equal(MiniDock.Top, result.Settings.MiniDock);
        Assert.Null(result.CorruptBackupPath);
        Assert.True(File.Exists(path));
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
    public void RefreshIntervalDefaultsToTwoMinutes() =>
        Assert.Equal(TimeSpan.FromMinutes(2), AppSettings.Default.RefreshInterval);

    [Theory]
    [InlineData(0, 120)]
    [InlineData(-5, 120)]
    [InlineData(14, 120)]
    [InlineData(15, 120)]
    [InlineData(30, 120)]
    [InlineData(60, 120)]
    [InlineData(120, 120)]
    [InlineData(180, 180)]
    [InlineData(3600, 3600)]
    [InlineData(99999, 3600)]
    public void RefreshIntervalIsClampedRatherThanRejected(int configured, int expectedSeconds)
    {
        // A hand-edited settings file must never stop the application starting, and a zero-second
        // interval would poll a provider in a tight loop.
        AppSettings settings = AppSettings.Default with { RefreshIntervalSeconds = configured };

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), settings.RefreshInterval);
    }

    [Theory]
    [InlineData("unknown", null, 120)]
    [InlineData("codex", 0, 120)]
    [InlineData("codex", 5, 120)]
    [InlineData("codex", 15, 120)]
    [InlineData("codex", 60, 120)]
    [InlineData("codex", 99999, 3600)]
    public void ProviderRefreshIntervalsUseTheOverrideOnlyWhenItIsNonZero(string key, int? overrideSeconds, int expectedSeconds)
    {
        IReadOnlyDictionary<string, int> overrides = overrideSeconds is null
            ? new Dictionary<string, int>()
            : new Dictionary<string, int> { ["Codex"] = overrideSeconds.Value };
        AppSettings settings = AppSettings.Default with { ProviderRefreshSeconds = overrides };

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), settings.RefreshIntervalFor(key));
    }

    [Fact]
    public void AnIntervalBelowTheFloorIsNotRewrittenToDisk()
    {
        using TempDirectory dir = new();
        string path = dir.File("settings.json");
        File.WriteAllText(path, """{ "RefreshIntervalSeconds": 15 }""");
        AppSettingsStore store = new(path);

        AppSettings loaded = store.Load().Settings;
        store.Save(loaded with { Theme = ThemePreference.Dark });

        Assert.Equal(TimeSpan.FromSeconds(120), loaded.RefreshInterval);
        Assert.Contains("\"RefreshIntervalSeconds\": 15", File.ReadAllText(path));
    }

    [Fact]
    public void HiddenProvidersMatchKeysCaseInsensitively()
    {
        AppSettings settings = AppSettings.Default with { HiddenProviders = ["Codex"] };

        Assert.True(settings.IsProviderHidden("codex"));
    }

    [Fact]
    public void ProviderPreferencesRoundTripThroughTheStore()
    {
        using TempDirectory dir = new();
        AppSettingsStore store = new(dir.File("settings.json"));
        AppSettings written = AppSettings.Default with
        {
            ProviderOrder = ["codex", "claude-code"],
            HiddenProviders = ["claude-code"],
            ProviderRefreshSeconds = new Dictionary<string, int> { ["codex"] = 120 }
        };

        store.Save(written);

        AppSettings loaded = store.Load().Settings;
        Assert.Equal(written.ProviderOrder, loaded.ProviderOrder);
        Assert.Equal(written.HiddenProviders, loaded.HiddenProviders);
        Assert.Equal(written.ProviderRefreshSeconds, loaded.ProviderRefreshSeconds);
    }

    [Fact]
    public void SavingDefaultsDoesNotWriteAnEmptyProviderOrder()
    {
        using TempDirectory dir = new();
        AppSettingsStore store = new(dir.File("settings.json"));

        store.Save(AppSettings.Default);

        Assert.DoesNotContain("ProviderOrder", File.ReadAllText(dir.File("settings.json")));
    }

    [Fact]
    public void NullProviderPreferencesInAHandEditedFileLoadAsEmptyCollections()
    {
        using TempDirectory dir = new();
        string path = dir.File("settings.json");
        File.WriteAllText(path, """{ "ProviderOrder": null, "HiddenProviders": null, "ProviderRefreshSeconds": null }""");

        AppSettings settings = new AppSettingsStore(path).Load().Settings;

        Assert.Empty(settings.ProviderOrder);
        Assert.Empty(settings.HiddenProviders);
        Assert.Empty(settings.ProviderRefreshSeconds);
        Assert.False(settings.IsProviderHidden("codex"));
        Assert.Null(settings.RefreshSecondsOverrideFor("codex"));
        Assert.Equal(settings.RefreshInterval, settings.RefreshIntervalFor("codex"));
    }

    [Fact]
    public void AlertThresholdsDefaultToTheFullLadder() =>
        Assert.Equal(QuotaMilestones.Ladder, AppSettings.Default.EffectiveAlertThresholds);

    [Fact]
    public void AlertThresholdsRoundTripThroughTheStore()
    {
        using TempDirectory dir = new();
        AppSettingsStore store = new(dir.File("settings.json"));

        store.Save(AppSettings.Default with { AlertThresholds = [90, 100] });

        Assert.Equal([90, 100], store.Load().Settings.AlertThresholds);
    }

    /// <summary>
    /// The sanitized view is derived. Writing it would quietly rewrite what the user typed into the
    /// file, and would then be read back as if they had typed that.
    /// </summary>
    [Fact]
    public void TheSanitizedLadderIsNeverWrittenToTheFile()
    {
        using TempDirectory dir = new();
        AppSettingsStore store = new(dir.File("settings.json"));

        store.Save(AppSettings.Default with { AlertThresholds = [90, 100] });

        Assert.DoesNotContain("EffectiveAlertThresholds", File.ReadAllText(dir.File("settings.json")));
    }

    /// <summary>
    /// A hand-edited file can hold a null here just as it can for the provider collections, and the
    /// derived view has to survive it - the widget must still start, and still alert.
    /// </summary>
    [Fact]
    public void ANullLadderInAHandEditedFileFallsBackToTheDefaultOne()
    {
        using TempDirectory dir = new();
        string path = dir.File("settings.json");
        File.WriteAllText(path, """{ "AlertThresholds": null }""");

        AppSettings settings = new AppSettingsStore(path).Load().Settings;

        Assert.Equal(QuotaMilestones.Ladder, settings.EffectiveAlertThresholds);
    }

    [Fact]
    public void QuietHoursAreOffByDefaultAndRunOvernightWhenSwitchedOn()
    {
        Assert.False(AppSettings.Default.QuietHoursEnabled);
        Assert.Equal(QuietHours.Off, AppSettings.Default.QuietHours);
    }

    [Fact]
    public void QuietHoursRoundTripThroughTheStore()
    {
        using TempDirectory dir = new();
        AppSettingsStore store = new(dir.File("settings.json"));

        store.Save(AppSettings.Default with
        {
            QuietHoursEnabled = true,
            QuietHoursStartMinutes = 1260,
            QuietHoursEndMinutes = 480
        });

        AppSettings loaded = store.Load().Settings;
        Assert.Equal(new QuietHours(true, 1260, 480), loaded.QuietHours);
        Assert.DoesNotContain("\"QuietHours\":", File.ReadAllText(dir.File("settings.json")));
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

    [Fact]
    public void TheSettingsWindowSizeRoundTrips()
    {
        using TempDirectory directory = new();
        AppSettingsStore store = new(directory.File("settings.json"));

        store.Save(AppSettings.Default with { SettingsWindowWidth = 900, SettingsWindowHeight = 700 });

        AppSettings loaded = store.Load().Settings;
        Assert.Equal(900, loaded.SettingsWindowWidth);
        Assert.Equal(700, loaded.SettingsWindowHeight);
    }

    [Fact]
    public void AWindowSizeIsNullUntilTheWindowHasBeenResized()
    {
        Assert.Null(AppSettings.Default.SettingsWindowWidth);
        Assert.Null(AppSettings.Default.SettingsWindowHeight);
    }
}
