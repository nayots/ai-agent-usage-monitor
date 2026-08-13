using System.IO;
using AiUsageMonitor.App.Interop;
using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Settings;

namespace AiUsageMonitor.App.Tests;

public class ProviderPreferenceViewModelTests
{
    private const string ScratchKey = @"Software\AiUsageMonitor\tests\ProviderPreferenceVm";

    private sealed class SilentProbe(string name) : IProviderProbe
    {
        public string Name => name;
        public string Mechanism => "fake";
        public MechanismTier Tier => MechanismTier.Official;
        public Task<ProviderSnapshot> ProbeAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    [Fact]
    public void HidingAndShowingAProviderPreservesTheOtherHiddenProvider()
    {
        SettingsViewModel model = Model(out SettingsService service, AppSettings.Default with { HiddenProviders = ["codex"] });
        ProviderPreferenceViewModel claude = model.ProviderPreferences.Single(provider => provider.Key == "claude-code");

        claude.IsVisible = false;
        Assert.Equal(["codex", "claude-code"], service.Current.HiddenProviders);

        claude.IsVisible = true;
        Assert.Equal(["codex"], service.Current.HiddenProviders);
    }

    [Fact]
    public void MovingTheSecondProviderUpWritesTheFullSwappedOrder()
    {
        SettingsViewModel model = Model(out SettingsService service);

        model.ProviderPreferences[1].MoveUpCommand.Execute(null);

        Assert.Equal(["codex", "claude-code"], service.Current.ProviderOrder);
    }

    [Fact]
    public void ProviderPreferencesFollowTheCurrentEffectiveOrder()
    {
        SettingsViewModel model = Model(out _, AppSettings.Default with { ProviderOrder = ["codex"] });

        Assert.Equal(["codex", "claude-code"], model.ProviderPreferences.Select(provider => provider.Key));
    }

    [Fact]
    public void MoveCommandsAreDisabledAtTheListEnds()
    {
        SettingsViewModel model = Model(out _);

        Assert.False(model.ProviderPreferences[0].CanMoveUp);
        Assert.False(model.ProviderPreferences[0].MoveUpCommand.CanExecute(null));
        Assert.False(model.ProviderPreferences[1].CanMoveDown);
        Assert.False(model.ProviderPreferences[1].MoveDownCommand.CanExecute(null));
    }

    [Fact]
    public void SelectingSharedIntervalRemovesOnlyThisProvidersOverride()
    {
        SettingsViewModel model = Model(out SettingsService service, AppSettings.Default with
        {
            ProviderRefreshSeconds = new Dictionary<string, int> { ["claude-code"] = 120, ["codex"] = 300 }
        });
        ProviderPreferenceViewModel claude = model.ProviderPreferences.Single(provider => provider.Key == "claude-code");

        claude.Intervals.Single(choice => choice.Value == 0).IsSelected = true;

        Assert.Equal(300, service.Current.ProviderRefreshSeconds["codex"]);
        Assert.False(service.Current.ProviderRefreshSeconds.ContainsKey("claude-code"));
    }

    [Fact]
    public void SelectingProviderIntervalWritesOnlyThatProvidersOverride()
    {
        SettingsViewModel model = Model(out SettingsService service, AppSettings.Default with
        {
            ProviderRefreshSeconds = new Dictionary<string, int> { ["codex"] = 300 }
        });
        ProviderPreferenceViewModel claude = model.ProviderPreferences.Single(provider => provider.Key == "claude-code");

        claude.Intervals.Single(choice => choice.Value == 120).IsSelected = true;

        Assert.Equal(120, service.Current.ProviderRefreshSeconds["claude-code"]);
        Assert.Equal(300, service.Current.ProviderRefreshSeconds["codex"]);
    }

    [Fact]
    public void AHandEditedProviderIntervalIsAppendedAndSorted()
    {
        SettingsViewModel model = Model(out _, AppSettings.Default with
        {
            ProviderRefreshSeconds = new Dictionary<string, int> { ["claude-code"] = 45 }
        });
        ProviderPreferenceViewModel claude = model.ProviderPreferences.Single(provider => provider.Key == "claude-code");

        Assert.Equal([0, 15, 30, 45, 60, 120, 300, 600], claude.Intervals.Select(choice => choice.Value));
        Assert.Equal("45s", claude.Intervals.Single(choice => choice.Value == 45).Label);
    }

    [Fact]
    public void ProviderIntervalsHaveDistinctRadioGroups()
    {
        SettingsViewModel model = Model(out _);

        Assert.NotEqual(model.ProviderPreferences[0].Intervals[0].GroupName, model.ProviderPreferences[1].Intervals[0].GroupName);
    }

    private static SettingsViewModel Model(out SettingsService service, AppSettings? initial = null)
    {
        string path = Path.Combine(Path.GetTempPath(), "aium-provider-vm-" + Guid.NewGuid().ToString("N"), "settings.json");
        service = new SettingsService(new AppSettingsStore(path), initial ?? AppSettings.Default);
        IReadOnlyList<ProviderDescriptor> providers =
        [
            new ProviderDescriptor("claude-code", "Claude Code", "CC", new SilentProbe("Claude Code")),
            new ProviderDescriptor("codex", "Codex", "CX", new SilentProbe("Codex"))
        ];

        return new SettingsViewModel(
            service,
            new StartupRegistration(ScratchKey, "AiUsageMonitorTest", null),
            resetPosition: () => { },
            recheckProviders: () => { },
            providers: providers);
    }
}
