using System.IO;
using AiUsageMonitor.App.Interop;
using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Settings;

namespace AiUsageMonitor.App.Tests;

public class SettingsViewModelTests
{
    private const string ScratchKey = @"Software\AiUsageMonitor\tests\SettingsVm";

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

    private static SettingsViewModel Model(
        out SettingsService service,
        AppSettings? initial = null,
        bool globalHotkeyUnavailable = false)
    {
        string path = Path.Combine(Path.GetTempPath(), "aium-vm-" + Guid.NewGuid().ToString("N"), "settings.json");
        service = new SettingsService(new AppSettingsStore(path), initial ?? AppSettings.Default);
        return new SettingsViewModel(
            service,
            new StartupRegistration(ScratchKey, "AiUsageMonitorTest", null),
            resetPosition: () => { },
            recheckProviders: () => { },
            openLogs: () => { },
            openDiagnostics: () => { },
            providers: Providers,
            globalHotkeyUnavailable: globalHotkeyUnavailable);
    }

    [Fact]
    public void ATogglePersistsThroughTheService()
    {
        SettingsViewModel model = Model(out SettingsService service);

        model.ColorBarsByUsage = false;

        Assert.False(service.Current.ColorBarsByUsage);
    }

    /// <summary>
    /// Pinning is offered by the title bar and nowhere else, because it lasts only as long as the
    /// session and a settings window promises the opposite. The window itself is checked in
    /// <c>ViewLoadingTests.TheSettingsWindowOffersNoPinning</c>; this is the half that would let a
    /// checkbox be added back without anyone noticing it had somewhere to bind to.
    /// </summary>
    [Fact]
    public void TheSettingsViewModelHasNoPinningToBindTo() =>
        Assert.Null(typeof(SettingsViewModel).GetProperty("AlwaysOnTop"));

    [Fact]
    public void QuotaNotificationsAreOnByDefault()
    {
        SettingsViewModel model = Model(out _);

        Assert.True(model.NotifyOnQuotaEvents);
    }

    [Fact]
    public void TheQuotaNotificationsTogglePersistsThroughTheService()
    {
        SettingsViewModel model = Model(out SettingsService service);

        model.NotifyOnQuotaEvents = false;

        Assert.False(service.Current.NotifyOnQuotaEvents);
    }

    [Fact]
    public void TheGlobalHotkeyTogglePersistsThroughTheService()
    {
        SettingsViewModel model = Model(out SettingsService service);

        model.GlobalHotkeyEnabled = false;

        Assert.False(service.Current.GlobalHotkeyEnabled);
    }

    [Fact]
    public void ARegisteredGlobalHotkeyHasNoWarning()
    {
        SettingsViewModel model = Model(out _, globalHotkeyUnavailable: false);

        Assert.Equal("Ctrl+Alt+Q", model.GlobalHotkeyLabel);
        Assert.False(model.HasGlobalHotkeyWarning);
        Assert.Null(model.GlobalHotkeyUnavailableReason);
    }

    [Fact]
    public void AnUnavailableGlobalHotkeyExplainsTheConflict()
    {
        SettingsViewModel model = Model(out _, globalHotkeyUnavailable: true);

        Assert.True(model.HasGlobalHotkeyWarning);
        Assert.Equal("Unavailable: another application already uses this shortcut.", model.GlobalHotkeyUnavailableReason);
    }

    [Fact]
    public void AQuotaNotificationsChangeMadeElsewhereIsReflectedBack()
    {
        SettingsViewModel model = Model(out SettingsService service);

        service.Update(s => s with { NotifyOnQuotaEvents = false });

        Assert.False(model.NotifyOnQuotaEvents);
    }

    [Fact]
    public void AChangeMadeElsewhereIsReflectedBack()
    {
        SettingsViewModel model = Model(out SettingsService service);

        service.Update(s => s with { ColorBarsByUsage = false });

        Assert.False(model.ColorBarsByUsage);
    }

    [Fact]
    public void SelectingARefreshIntervalWritesItsValue()
    {
        SettingsViewModel model = Model(out SettingsService service);

        ChoiceViewModel choice = model.RefreshIntervals.Single(c => c.Value == 300);
        choice.IsSelected = true;

        Assert.Equal(300, service.Current.RefreshIntervalSeconds);
        Assert.True(model.RefreshIntervals.Single(c => c.Value == 300).IsSelected);
    }

    [Theory]
    [InlineData(60, 300, true)]
    [InlineData(300, 60, false)]
    [InlineData(60, 60, true)]
    public void TheStaleThresholdWarningReflectsTheClampedSettings(int staleAfterSeconds, int refreshIntervalSeconds, bool expected)
    {
        SettingsViewModel model = Model(
            out _,
            AppSettings.Default with
            {
                StaleAfterSeconds = staleAfterSeconds,
                RefreshIntervalSeconds = refreshIntervalSeconds
            });

        Assert.Equal(expected, model.HasStaleThresholdWarning);
        Assert.Equal("Cards will always look stale — this is shorter than the refresh interval.", model.StaleThresholdWarningText);
    }

    [Fact]
    public void DeselectingAChoiceChangesNothing()
    {
        // Radio buttons report both sides of a move. Acting on the deselection would write the
        // outgoing value back over the incoming one, and which one won would depend on event order.
        SettingsViewModel model = Model(out SettingsService service);
        int before = service.Current.RefreshIntervalSeconds;

        model.RefreshIntervals.Single(c => c.Value == before).IsSelected = false;

        Assert.Equal(before, service.Current.RefreshIntervalSeconds);
    }

    [Fact]
    public void AHandEditedValueOutsideThePresetsSurvivesAsItsOwnChoice()
    {
        SettingsViewModel model = Model(out _, AppSettings.Default with { RefreshIntervalSeconds = 45 });

        ChoiceViewModel selected = model.RefreshIntervals.Single(c => c.IsSelected);

        Assert.Equal(45, selected.Value);
    }

    [Fact]
    public void EveryThemeIsOfferedAndTheCurrentOneIsSelected()
    {
        SettingsViewModel model = Model(out SettingsService service);

        Assert.Equal(3, model.Themes.Count);
        model.Themes.Single(c => c.Value == (int)ThemePreference.Dark).IsSelected = true;

        Assert.Equal(ThemePreference.Dark, service.Current.Theme);
    }

    [Fact]
    public void WithoutAKnownExecutableStartWithWindowsIsOfferedButDisabled()
    {
        SettingsViewModel model = Model(out _);

        Assert.False(model.CanStartWithWindows);
        Assert.False(model.StartWithWindows);
        Assert.False(string.IsNullOrWhiteSpace(model.StartWithWindowsUnavailableReason));
    }

    [Fact]
    public void TheActionsCallWhatTheyClaimTo()
    {
        string path = Path.Combine(Path.GetTempPath(), "aium-vm-" + Guid.NewGuid().ToString("N"), "settings.json");
        SettingsService service = new(new AppSettingsStore(path), AppSettings.Default);
        int reset = 0, recheck = 0, logs = 0, diagnostics = 0;
        SettingsViewModel model = new(
            service,
            new StartupRegistration(ScratchKey, "AiUsageMonitorTest", null),
            resetPosition: () => reset++,
            recheckProviders: () => recheck++,
            openLogs: () => logs++,
            openDiagnostics: () => diagnostics++,
            providers: Providers);

        model.ResetPositionCommand.Execute(null);
        model.RecheckProvidersCommand.Execute(null);
        model.OpenLogsCommand.Execute(null);
        model.OpenDiagnosticsCommand.Execute(null);

        Assert.Equal(1, reset);
        Assert.Equal(1, recheck);
        Assert.Equal(1, logs);
        Assert.Equal(1, diagnostics);
    }

    [Fact]
    public void TheDensityChoicesOfferStandardAndCompactAndWriteThrough()
    {
        SettingsViewModel model = Model(out SettingsService service);
        Assert.Equal(new[] { "Standard", "Compact" }, model.Densities.Select(choice => choice.Label));
        Assert.True(model.Densities[0].IsSelected);
        model.Densities[1].IsSelected = true;
        Assert.Equal(WidgetDensity.Compact, service.Current.Density);
    }

    [Fact]
    public void ADensityChangeMadeElsewhereMovesTheRadio()
    {
        SettingsViewModel model = Model(out SettingsService service);
        service.Update(s => s with { Density = WidgetDensity.Compact });
        Assert.False(model.Densities[0].IsSelected);
        Assert.True(model.Densities[1].IsSelected);
    }

    [Fact]
    public void PersistenceWarningFollowsTheSettingsServiceWithoutExposingFailureDetails()
    {
        string path = Path.Combine(Path.GetTempPath(), "aium-vm-" + Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(path);
        SettingsService service = new(new AppSettingsStore(path), AppSettings.Default);
        SettingsViewModel model = new(service, new StartupRegistration(ScratchKey, "AiUsageMonitorTest", null), () => { }, () => { }, () => { }, () => { }, Providers);

        model.ColorBarsByUsage = false;

        Assert.True(model.HasPersistenceWarning);
        Assert.Equal("Changes apply to this session only — the settings file could not be saved.", model.PersistenceWarningText);
        Assert.DoesNotContain(path, model.PersistenceWarningText);
        Assert.DoesNotContain("IOException", model.PersistenceWarningText);
        model.Dispose();
    }
}
