using AiUsageMonitor.Infrastructure.Settings;

namespace AiUsageMonitor.Infrastructure.Tests;

public class SettingsServiceTests
{
    private static SettingsService Service(TempDirectory dir, AppSettings? initial = null) =>
        new(new AppSettingsStore(dir.File("settings.json")), initial ?? AppSettings.Default);

    [Fact]
    public void AnUpdateChangesCurrentPersistsAndRaises()
    {
        using TempDirectory dir = new();
        SettingsService service = Service(dir);
        AppSettings? raised = null;
        service.Changed += (_, settings) => raised = settings;

        // Deliberately not AlwaysOnTop: that one is session state and never reaches the file, so it
        // cannot stand for persistence here.
        service.Update(s => s with { ColorBarsByUsage = false });

        Assert.False(service.Current.ColorBarsByUsage);
        Assert.NotNull(raised);
        Assert.False(raised!.ColorBarsByUsage);
        Assert.False(new AppSettingsStore(dir.File("settings.json")).Load().Settings.ColorBarsByUsage);
    }

    [Fact]
    public void EachUpdateComposesAgainstCurrentNotAgainstACapturedCopy()
    {
        // This is the whole reason Update takes a function. A caller holding an AppSettings from
        // startup - which WidgetWindow.SavePlacement does - would otherwise write back a state that
        // silently reverts every change made since.
        using TempDirectory dir = new();
        SettingsService service = Service(dir);

        service.Update(s => s with { AlwaysOnTop = true });
        service.Update(s => s with { WindowLeft = 42 });

        Assert.True(service.Current.AlwaysOnTop);
        Assert.Equal(42, service.Current.WindowLeft);
    }

    [Fact]
    public void AnUpdateThatChangesNothingIsNotAnnounced()
    {
        using TempDirectory dir = new();
        SettingsService service = Service(dir);
        int raises = 0;
        service.Changed += (_, _) => raises++;

        service.Update(s => s with { AlwaysOnTop = false });

        Assert.Equal(0, raises);
    }

    [Fact]
    public void AFailedSaveKeepsTheChangeInMemory()
    {
        // A directory where the settings file should be makes every write fail. Losing a setting
        // because a disk write failed is worse than losing it at restart: the user watched the
        // toggle move.
        using TempDirectory dir = new();
        Directory.CreateDirectory(dir.File("settings.json"));
        SettingsService service = new(new AppSettingsStore(dir.File("settings.json")), AppSettings.Default);

        service.Update(s => s with { AlwaysOnTop = true });

        Assert.True(service.Current.AlwaysOnTop);
    }
}
