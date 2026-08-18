using System.IO;
using AiUsageMonitor.App.Interop;
using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Settings;
using Microsoft.Win32;

namespace AiUsageMonitor.App.Tests;

/// <summary>
/// The settings window's side of resetting application settings (PRD §19). The reset itself is
/// covered in the infrastructure suite; what is guarded here is that nothing happens without a
/// second press, that the one setting living outside the settings file goes with it, and that the
/// user is told where their previous settings went.
/// </summary>
public sealed class SettingsResetViewModelTests : IDisposable
{
    private const string ScratchKey = @"Software\AiUsageMonitor\tests\SettingsReset";
    private const string ValueName = "AiUsageMonitorTest";

    private sealed class SilentProbe(string name) : IProviderProbe
    {
        public string Name => name;
        public string Mechanism => "fake";
        public MechanismTier Tier => MechanismTier.Official;
        public Task<ProviderSnapshot> ProbeAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private static IReadOnlyList<ProviderDescriptor> Providers =>
    [
        new ProviderDescriptor("claude-code", "Claude Code", "CC", new SilentProbe("Claude Code")),
        new ProviderDescriptor("codex", "Codex", "CX", new SilentProbe("Codex"))
    ];

    private static AppSettings Customized => AppSettings.Default with
    {
        Theme = ThemePreference.Dark,
        Density = WidgetDensity.Compact,
        ColorBarsByUsage = false,
        StaleAfterSeconds = 90,
        TrayHintShown = true
    };

    private static SettingsViewModel Model(
        out SettingsService service,
        out string path,
        AppSettings? initial = null,
        string? executablePath = null,
        Action? resetPosition = null)
    {
        path = Path.Combine(Path.GetTempPath(), "aium-reset-" + Guid.NewGuid().ToString("N"), "settings.json");
        service = new SettingsService(new AppSettingsStore(path), initial ?? Customized);
        return new SettingsViewModel(
            service,
            new StartupRegistration(ScratchKey, ValueName, executablePath),
            resetPosition: resetPosition ?? (() => { }),
            recheckProviders: () => { },
            providers: Providers);
    }

    [Fact]
    public void AskingToResetChangesNothingUntilItIsConfirmed()
    {
        SettingsViewModel model = Model(out SettingsService service, out _);

        model.ResetSettingsCommand.Execute(null);

        Assert.True(model.IsConfirmingReset);
        Assert.Equal(ThemePreference.Dark, service.Current.Theme);
        Assert.Equal(WidgetDensity.Compact, service.Current.Density);
    }

    [Fact]
    public void CancellingLeavesEverySettingUntouched()
    {
        SettingsViewModel model = Model(out SettingsService service, out _);

        model.ResetSettingsCommand.Execute(null);
        model.CancelResetCommand.Execute(null);

        Assert.False(model.IsConfirmingReset);
        Assert.Null(model.ResetResultText);
        Assert.Equal(ThemePreference.Dark, service.Current.Theme);
    }

    [Fact]
    public void ConfirmingPutsEverySettingBackToItsDefault()
    {
        SettingsViewModel model = Model(out SettingsService service, out _);

        model.ResetSettingsCommand.Execute(null);
        model.ConfirmResetCommand.Execute(null);

        Assert.Equal(AppSettings.Default, service.Current);
        Assert.False(model.IsConfirmingReset);
    }

    /// <summary>
    /// The one application setting that does not live in the settings file. Left behind, it would be
    /// read back from the registry on the next load and silently undo its own reset.
    /// </summary>
    [Fact]
    public void ConfirmingClearsTheStartWithWindowsRegistryEntry()
    {
        SettingsViewModel model = Model(out _, out _, executablePath: @"C:\app\widget.exe");
        StartupRegistration startup = new(ScratchKey, ValueName, @"C:\app\widget.exe");
        startup.Enable();
        Assert.True(startup.IsEnabled);

        model.ResetSettingsCommand.Execute(null);
        model.ConfirmResetCommand.Execute(null);

        Assert.False(startup.IsEnabled);
    }

    [Fact]
    public void ConfirmingRecentresTheWidgetRatherThanLeavingItWhereItWas()
    {
        int recentred = 0;
        SettingsViewModel model = Model(out _, out _, resetPosition: () => recentred++);

        model.ResetSettingsCommand.Execute(null);
        model.ConfirmResetCommand.Execute(null);

        Assert.Equal(1, recentred);
    }

    [Fact]
    public void TheResultNamesWhereThePreviousSettingsWereSaved()
    {
        SettingsViewModel model = Model(out SettingsService service, out string path);
        service.Update(settings => settings with { Theme = ThemePreference.Light });

        model.ResetSettingsCommand.Execute(null);
        model.ConfirmResetCommand.Execute(null);

        string? backup = Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.backup").SingleOrDefault();
        Assert.NotNull(backup);
        Assert.Contains(Path.GetFileName(backup), model.ResetResultText);
    }

    /// <summary>
    /// A first run has nothing to preserve. That is not a failure and must not be reported as one,
    /// but the result must not claim a backup exists either.
    /// </summary>
    [Fact]
    public void TheResultSaysSoWhenThereWereNoPreviousSettingsToSave()
    {
        SettingsViewModel model = Model(out _, out _, initial: AppSettings.Default);

        model.ResetSettingsCommand.Execute(null);
        model.ConfirmResetCommand.Execute(null);

        Assert.NotNull(model.ResetResultText);
        Assert.DoesNotContain(".backup", model.ResetResultText);
    }

    [Fact]
    public void AskingAgainAfterAResetClearsTheOldResult()
    {
        SettingsViewModel model = Model(out _, out _);

        model.ResetSettingsCommand.Execute(null);
        model.ConfirmResetCommand.Execute(null);
        model.ResetSettingsCommand.Execute(null);

        Assert.Null(model.ResetResultText);
    }

    public void Dispose()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(ScratchKey, throwOnMissingSubKey: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A scratch key that cannot be removed must not fail an otherwise passing test.
        }
    }
}
