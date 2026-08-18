using AiUsageMonitor.Infrastructure.Settings;

namespace AiUsageMonitor.Infrastructure.Tests;

/// <summary>
/// Resetting application settings (PRD §19). Provider configuration is never in this file and is
/// never touched by any of it; what these tests guard is that the reset is complete, that it is
/// reversible, and that the one value which is session state rather than a setting survives it.
/// </summary>
public class SettingsResetTests
{
    private static AppSettings HeavilyCustomized => AppSettings.Default with
    {
        Theme = ThemePreference.Dark,
        Density = WidgetDensity.Compact,
        ColorBarsByUsage = false,
        NotifyOnQuotaEvents = false,
        GlobalHotkeyEnabled = false,
        MiniMode = true,
        MiniDock = MiniDock.Bottom,
        MiniLeft = 240,
        ShowUnavailableProviders = false,
        StaleAfterSeconds = 90,
        RefreshIntervalSeconds = 900,
        AlertThresholds = [42],
        QuietHoursEnabled = true,
        QuietHoursStartMinutes = 60,
        QuietHoursEndMinutes = 120,
        ProviderOrder = ["codex", "claude-code"],
        HiddenProviders = ["codex"],
        ProviderRefreshSeconds = new Dictionary<string, int> { ["codex"] = 600 },
        WindowLeft = 12,
        WindowTop = 34,
        SettingsWindowWidth = 900,
        SettingsWindowHeight = 700,
        TrayHintShown = true,
        StartWithWindows = true
    };

    [Fact]
    public void BackUpCopiesTheSettingsFileAndLeavesTheOriginalInPlace()
    {
        using TempDirectory dir = new();
        string path = dir.File("settings.json");
        AppSettingsStore store = new(path);
        store.Save(HeavilyCustomized);
        string original = File.ReadAllText(path);

        string? backup = store.BackUp();

        Assert.NotNull(backup);
        Assert.True(File.Exists(backup));
        Assert.Equal(original, File.ReadAllText(backup));
        Assert.Equal(original, File.ReadAllText(path));
        Assert.EndsWith(".backup", backup);
    }

    /// <summary>
    /// Nothing to preserve is not a failure. A first run has no settings file, and a reset there is
    /// a no-op that must still report success rather than an error the user cannot act on.
    /// </summary>
    [Fact]
    public void BackUpReturnsNullWhenThereIsNoSettingsFile()
    {
        using TempDirectory dir = new();
        AppSettingsStore store = new(dir.File("settings.json"));

        Assert.Null(store.BackUp());
    }

    /// <summary>
    /// A file that cannot be copied must not stop the reset. The user asked for their settings back;
    /// refusing because the safety net could not be built leaves them stuck with the state they are
    /// trying to escape. The caller reports the missing backup instead.
    /// </summary>
    [Fact]
    public void BackUpReturnsNullWhenTheFileCannotBeCopied()
    {
        using TempDirectory dir = new();
        string path = dir.File("settings.json");
        AppSettingsStore store = new(path);
        store.Save(AppSettings.Default with { Theme = ThemePreference.Dark });

        using FileStream _ = new(path, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Null(store.BackUp());
    }

    [Fact]
    public void ResetRestoresEveryPersistedSettingToItsDefault()
    {
        using TempDirectory dir = new();
        SettingsService service = new(new AppSettingsStore(dir.File("settings.json")), HeavilyCustomized);

        service.Reset();

        Assert.Equal(AppSettings.Default, service.Current);
    }

    /// <summary>
    /// Pinning answers "what am I doing right now", not "how do I want this to work" - it is the one
    /// value on this record that is never persisted. Unpinning a widget someone is actively watching
    /// a quota drain on is not part of putting their settings back.
    /// </summary>
    [Fact]
    public void ResetKeepsPinningBecauseItIsSessionStateRatherThanASetting()
    {
        using TempDirectory dir = new();
        SettingsService service = new(
            new AppSettingsStore(dir.File("settings.json")),
            HeavilyCustomized with { AlwaysOnTop = true });

        service.Reset();

        Assert.True(service.Current.AlwaysOnTop);
        Assert.Equal(AppSettings.Default with { AlwaysOnTop = true }, service.Current);
    }

    [Fact]
    public void ResetAnnouncesTheChangeSoEveryOpenSurfaceRepaints()
    {
        using TempDirectory dir = new();
        SettingsService service = new(new AppSettingsStore(dir.File("settings.json")), HeavilyCustomized);
        List<AppSettings> announced = [];
        service.Changed += (_, settings) => announced.Add(settings);

        service.Reset();

        Assert.Equal([AppSettings.Default], announced);
    }

    /// <summary>
    /// Compared as written text rather than as records. <see cref="AppSettings"/> is a record whose
    /// collection properties compare by reference, so a settings file that round-trips through JSON
    /// never equals <see cref="AppSettings.Default"/> however identical its contents - the arrays
    /// are different instances. The file's bytes are what "reset to defaults" actually promises.
    /// </summary>
    private static string DefaultsAsWritten(TempDirectory dir)
    {
        string path = dir.File("reference-defaults.json");
        new AppSettingsStore(path).Save(AppSettings.Default);
        return File.ReadAllText(path);
    }

    [Fact]
    public void ResetWritesTheDefaultsToDisk()
    {
        using TempDirectory dir = new();
        string path = dir.File("settings.json");
        SettingsService service = new(new AppSettingsStore(path), HeavilyCustomized);

        service.Reset();

        Assert.Equal(DefaultsAsWritten(dir), File.ReadAllText(path));
    }

    [Fact]
    public void ResetReturnsWhereTheOldSettingsWerePreserved()
    {
        using TempDirectory dir = new();
        string path = dir.File("settings.json");
        AppSettingsStore store = new(path);
        store.Save(HeavilyCustomized);
        string before = File.ReadAllText(path);
        SettingsService service = new(store, HeavilyCustomized);

        string? backup = service.Reset();

        Assert.NotNull(backup);
        Assert.Equal(before, File.ReadAllText(backup));
        Assert.NotEqual(before, File.ReadAllText(path));
    }

    /// <summary>
    /// Reset means "make the file defaults", not "apply a change". A file holding state the loaded
    /// record never carried - hand-edited keys, a value normalized on read - must still be rewritten,
    /// so this deliberately does not go through the equality short-circuit that guards an ordinary
    /// update.
    /// </summary>
    [Fact]
    public void ResetRewritesTheFileEvenWhenTheLoadedSettingsAlreadyMatchTheDefaults()
    {
        using TempDirectory dir = new();
        string path = dir.File("settings.json");
        AppSettingsStore store = new(path);
        store.Save(HeavilyCustomized);
        SettingsService service = new(store, AppSettings.Default);

        service.Reset();

        Assert.Equal(DefaultsAsWritten(dir), File.ReadAllText(path));
    }

    [Fact]
    public void ResetOnAFirstRunWithNoSettingsFileReportsNoBackup()
    {
        using TempDirectory dir = new();
        SettingsService service = new(new AppSettingsStore(dir.File("settings.json")), AppSettings.Default);

        Assert.Null(service.Reset());
    }

    [Fact]
    public void BackUpsTakenInTheSameSecondDoNotOverwriteOneAnother()
    {
        using TempDirectory dir = new();
        string path = dir.File("settings.json");
        AppSettingsStore store = new(path);
        store.Save(HeavilyCustomized);

        string? first = store.BackUp();
        string? second = store.BackUp();

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
    }
}
