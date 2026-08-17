using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Refresh;
using AiUsageMonitor.Infrastructure.Settings;

namespace AiUsageMonitor.App.Tests;

public class MainViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed class StubProbe(string name, ConnectionState state, IReadOnlyList<QuotaWindow> windows) : IProviderProbe
    {
        public int Calls { get; private set; }
        public string Name => name;
        public string Mechanism => "stub";
        public MechanismTier Tier => MechanismTier.Official;

        public Task<ProviderSnapshot> ProbeAsync(CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new ProviderSnapshot(
                ProviderName: name,
                Installed: true,
                Version: "1.0.0",
                ExecutablePath: null,
                State: state,
                Mechanism: "stub",
                Tier: MechanismTier.Official,
                UpdateModel: "pull (poll)",
                Windows: windows,
                RetrievedAt: state == ConnectionState.Connected ? Now : null,
                Error: null,
                Notes: []));
        }
    }

    private static QuotaWindow Window() => new(
        Id: "five_hour", Label: "5-hour window", UsedPercent: 47,
        ResetsAt: Now.AddMinutes(295), WindowDuration: TimeSpan.FromHours(5),
        Order: 0, IsPartial: false, Extra: new Dictionary<string, string>(), LabelIsProviderToken: false);

    private static (MainViewModel Model, IReadOnlyList<ProviderDescriptor> Providers) Build(params ProviderDescriptor[] providers) =>
        Build(AppSettings.Default, providers);

    private static (MainViewModel Model, IReadOnlyList<ProviderDescriptor> Providers) Build(
        AppSettings settings,
        params ProviderDescriptor[] providers)
    {
        ProviderRefreshService service = new(providers, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60));
        MainViewModel model = new(service, providers, settings, () => Now);
        return (model, providers);
    }

    [Fact]
    public void ACardExistsForEveryProviderBeforeAnythingHasBeenProbed()
    {
        (MainViewModel model, _) = Build(
            new ProviderDescriptor("claude-code", "Claude Code", "CC", new StubProbe("Claude Code", ConnectionState.Connected, [])),
            new ProviderDescriptor("codex", "Codex", "CX", new StubProbe("Codex", ConnectionState.Connected, [])));

        Assert.Equal(["Claude Code", "Codex"], model.Providers.Select(p => p.DisplayName));
    }

    [Fact]
    public void TheFooterCountsProvidersAndAgreesWithItself()
    {
        (MainViewModel one, _) = Build(new ProviderDescriptor("codex", "Codex", "CX", new StubProbe("Codex", ConnectionState.Connected, [])));
        Assert.Equal("1 provider", one.FooterText);

        (MainViewModel two, _) = Build(
            new ProviderDescriptor("claude-code", "Claude Code", "CC", new StubProbe("Claude Code", ConnectionState.Connected, [])),
            new ProviderDescriptor("codex", "Codex", "CX", new StubProbe("Codex", ConnectionState.Connected, [])));
        Assert.Equal("2 providers", two.FooterText);
    }

    [Fact]
    public async Task ARefreshRoutesEachSnapshotToItsOwnCard()
    {
        (MainViewModel model, _) = Build(
            new ProviderDescriptor("claude-code", "Claude Code", "CC", new StubProbe("Claude Code", ConnectionState.Connected, [Window()])),
            new ProviderDescriptor("codex", "Codex", "CX", new StubProbe("Codex", ConnectionState.NotInstalled, [])));

        await model.RefreshAsync(force: true);

        Assert.Single(model.Providers[0].Windows);
        Assert.Equal(ConnectionState.Connected, model.Providers[0].State);
        Assert.Empty(model.Providers[1].Windows);
        Assert.Equal(ConnectionState.NotInstalled, model.Providers[1].State);
    }

    [Fact]
    public async Task LifecycleRefreshIsDeferredWhileLockedAndRunsAfterUnlock()
    {
        ProviderDescriptor provider = new(
            "codex",
            "Codex",
            "CX",
            new StubProbe("Codex", ConnectionState.Connected, []));
        ProviderRefreshService service = new([provider], TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60));
        MainViewModel model = new(service, [provider], AppSettings.Default, () => Now);

        model.SetWorkstationLocked(true);
        await model.RefreshAfterLifecycleEventAsync(RefreshTrigger.Resume);
        model.SetWorkstationLocked(false);
        await model.RefreshAfterLifecycleEventAsync(RefreshTrigger.Unlock);

        Assert.False(service.IsWorkstationLocked);
        Assert.Equal(RefreshTrigger.Unlock, service.ActivityFor(provider, Now).LastTrigger);
        model.Dispose();
    }

    /// <summary>
    /// The trigger-less overload derives its trigger from <c>force</c>. Fixing it to a manual
    /// trigger would make an unforced refresh exempt from the workstation-lock pause, because the
    /// two manual triggers are the ones the scheduler deliberately lets through while locked - so a
    /// scheduled poll routed through this overload would keep hitting the provider behind a lock
    /// screen.
    /// </summary>
    [Fact]
    public async Task AnUnforcedRefreshDoesNotPollWhileTheWorkstationIsLocked()
    {
        StubProbe probe = new("Codex", ConnectionState.Connected, []);
        ProviderDescriptor provider = new("codex", "Codex", "CX", probe);
        ProviderRefreshService service = new([provider], TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60));
        MainViewModel model = new(service, [provider], AppSettings.Default, () => Now);

        model.SetWorkstationLocked(true);
        await model.RefreshAsync(force: false);

        Assert.Equal(0, probe.Calls);

        model.SetWorkstationLocked(false);
        await model.RefreshAsync(force: false);

        Assert.Equal(1, probe.Calls);
        model.Dispose();
    }

    [Fact]
    public void RetryingOneProviderDoesNotRefreshTheOther()
    {
        StubProbe claudeProbe = new("Claude Code", ConnectionState.Error, []);
        StubProbe codexProbe = new("Codex", ConnectionState.Connected, []);
        ProviderDescriptor claude = new("claude-code", "Claude Code", "CC", claudeProbe);
        ProviderDescriptor codex = new("codex", "Codex", "CX", codexProbe);
        (MainViewModel model, _) = Build(claude, codex);

        model.Providers.Single(card => card.DisplayName == "Claude Code").RetryCommand.Execute(null);

        Assert.Equal(1, claudeProbe.Calls);
        Assert.Equal(0, codexProbe.Calls);
        model.Dispose();
    }

    [Fact]
    public async Task RefreshIsNotReentrant()
    {
        (MainViewModel model, _) = Build(
            new ProviderDescriptor("codex", "Codex", "CX", new StubProbe("Codex", ConnectionState.Connected, [])));

        Assert.True(model.RefreshCommand.CanExecute(null));

        Task refresh = model.RefreshAsync(force: true);
        await refresh;

        Assert.True(model.RefreshCommand.CanExecute(null));
        Assert.False(model.IsRefreshing);
    }

    [Fact]
    public async Task TickAdvancesEveryCardWithoutProbingAgain()
    {
        StubProbe probe = new("Codex", ConnectionState.Connected, [Window()]);
        (MainViewModel model, _) = Build(new ProviderDescriptor("codex", "Codex", "CX", probe));

        await model.RefreshAsync(force: true);
        string? before = model.Providers[0].Windows[0].CountdownText;

        model.Tick();

        Assert.Equal(before, model.Providers[0].Windows[0].CountdownText);
        Assert.Equal("Updated 0s ago", model.Providers[0].TimestampText);
    }

    [Fact]
    public async Task DisposingCancelsInFlightWorkAndStopsRoutingSnapshots()
    {
        (MainViewModel model, _) = Build(
            new ProviderDescriptor("codex", "Codex", "CX", new StubProbe("Codex", ConnectionState.Connected, [Window()])));

        model.Dispose();

        await model.RefreshAsync(force: true);

        Assert.Empty(model.Providers[0].Windows);
    }

    [Fact]
    public async Task ASettingsChangeReachesTheRowsThatRenderIt()
    {
        (MainViewModel model, _) = Build(
            new ProviderDescriptor("codex", "Codex", "CX", new StubProbe("Codex", ConnectionState.Connected, [Window()])));
        await model.RefreshAsync(force: true);

        Assert.True(model.Providers[0].Windows[0].ColorBarsByUsage);

        model.ApplySettings(AppSettings.Default with { ColorBarsByUsage = false });

        Assert.False(model.Providers[0].Windows[0].ColorBarsByUsage);
        Assert.Equal(47, model.Providers[0].Windows[0].UsedPercent);
    }

    [Fact]
    public async Task HidingUnavailableProvidersDropsThemFromTheFooterCount()
    {
        (MainViewModel model, _) = Build(
            new ProviderDescriptor("claude-code", "Claude Code", "CC", new StubProbe("Claude Code", ConnectionState.Connected, [Window()])),
            new ProviderDescriptor("codex", "Codex", "CX", new StubProbe("Codex", ConnectionState.NotInstalled, [])));
        await model.RefreshAsync(force: true);

        Assert.Equal("2 providers", model.FooterText);

        model.ApplySettings(AppSettings.Default with { ShowUnavailableProviders = false });

        Assert.Equal("1 provider", model.FooterText);
        Assert.True(model.Providers[1].IsHiddenByFilter);
    }

    [Fact]
    public void DensityReachesEveryCardFromTheSettingsItWasBuiltWith()
    {
        ProviderDescriptor[] providers =
        [
            new("claude-code", "Claude Code", "CC", new StubProbe("Claude Code", ConnectionState.Connected, [])),
            new("codex", "Codex", "CX", new StubProbe("Codex", ConnectionState.Connected, []))
        ];
        ProviderRefreshService service = new(providers, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60));
        MainViewModel model = new(
            service,
            providers,
            AppSettings.Default with { Density = WidgetDensity.Compact },
            () => Now);

        Assert.True(model.IsCompact);
        Assert.All(model.Providers, card => Assert.True(card.IsCompact));

        model.Dispose();
    }

    [Fact]
    public void ADensityChangeMadeLaterReachesEveryCardToo()
    {
        (MainViewModel model, _) = Build(
            new ProviderDescriptor("claude-code", "Claude Code", "CC", new StubProbe("Claude Code", ConnectionState.Connected, [])),
            new ProviderDescriptor("codex", "Codex", "CX", new StubProbe("Codex", ConnectionState.Connected, [])));

        Assert.False(model.IsCompact);

        model.ApplySettings(AppSettings.Default with { Density = WidgetDensity.Compact });

        Assert.True(model.IsCompact);
        Assert.All(model.Providers, card => Assert.True(card.IsCompact));

        model.ApplySettings(AppSettings.Default with { Density = WidgetDensity.Normal });

        Assert.False(model.IsCompact);
        Assert.All(model.Providers, card => Assert.False(card.IsCompact));

        model.Dispose();
    }

    [Fact]
    public void EmptySavedOrderRestoresRegistryOrderInPlaceWithoutRecreatingCards()
    {
        ProviderDescriptor claude = new("claude-code", "Claude Code", "CC", new StubProbe("Claude Code", ConnectionState.Connected, []));
        ProviderDescriptor codex = new("codex", "Codex", "CX", new StubProbe("Codex", ConnectionState.Connected, []));
        (MainViewModel model, _) = Build(AppSettings.Default with { ProviderOrder = ["codex", "claude-code"] }, claude, codex);
        ProviderCardViewModel codexCard = model.Providers[0];
        ProviderCardViewModel claudeCard = model.Providers[1];

        model.ApplySettings(AppSettings.Default);

        Assert.Equal(["Claude Code", "Codex"], model.Providers.Select(card => card.DisplayName));
        Assert.Same(claudeCard, model.Providers[0]);
        Assert.Same(codexCard, model.Providers[1]);
    }

    [Fact]
    public void HidingAndUnhidingAConnectedProviderChangesTheFooterWithoutRecreatingIt()
    {
        ProviderDescriptor claude = new("claude-code", "Claude Code", "CC", new StubProbe("Claude Code", ConnectionState.Connected, []));
        ProviderDescriptor codex = new("codex", "Codex", "CX", new StubProbe("Codex", ConnectionState.Connected, []));
        (MainViewModel model, _) = Build(claude, codex);
        ProviderCardViewModel codexCard = model.Providers[1];

        model.ApplySettings(AppSettings.Default with { HiddenProviders = ["codex"] });

        Assert.Equal("1 provider", model.FooterText);
        Assert.True(codexCard.IsHiddenByFilter);

        model.ApplySettings(AppSettings.Default);

        Assert.Equal("2 providers", model.FooterText);
        Assert.Same(codexCard, model.Providers[1]);
        Assert.False(codexCard.IsHiddenByFilter);
    }
}
