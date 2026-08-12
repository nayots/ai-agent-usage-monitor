using AiUsageMonitor.App.Notifications;
using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;

namespace AiUsageMonitor.App.Tests;

public class UsageAlertWatcherTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly FreshnessPolicy Policy = new(TimeSpan.FromMinutes(5));

    private sealed class SilentProbe(string name) : IProviderProbe
    {
        public string Name => name;

        public Task<ProviderSnapshot> ProbeAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    [Fact]
    public void FirstReadingOfAWindowSeedsSilently()
    {
        UsageAlertWatcher watcher = new();
        ProviderCardViewModel card = Card("Claude Code", Connected("five-hour", "5-hour", 81));

        Assert.Empty(watcher.Observe([card]));
    }

    [Fact]
    public void RisingToALadderRungBelowOneHundredRaisesAMilestoneWithTheActualReading()
    {
        UsageAlertWatcher watcher = new();
        ProviderCardViewModel card = Card("Claude Code", Connected("five-hour", "5-hour", 79));
        watcher.Observe([card]);

        card.Apply(Connected("five-hour", "5-hour", 81, Now.AddHours(1).AddMinutes(12)), Now, Policy);

        UsageAlert alert = Assert.Single(watcher.Observe([card]));
        Assert.Equal(new UsageAlert(UsageAlertKind.Milestone, "Claude Code · 5-hour past 80%", "81% used. Resets in 1h 12m."), alert);
    }

    [Fact]
    public void RisingToOneHundredRaisesALimitReachedAlert()
    {
        UsageAlertWatcher watcher = new();
        ProviderCardViewModel card = Card("Claude Code", Connected("five-hour", "5-hour", 95));
        watcher.Observe([card]);

        card.Apply(Connected("five-hour", "5-hour", 100, Now.AddMinutes(47)), Now, Policy);

        UsageAlert alert = Assert.Single(watcher.Observe([card]));
        Assert.Equal(new UsageAlert(UsageAlertKind.LimitReached, "Claude Code · 5-hour limit reached", "100% used. Resets in 47m 00s."), alert);
    }

    [Fact]
    public void AWindowWithoutAResetOmitsTheSecondBodySentence()
    {
        UsageAlertWatcher watcher = new();
        ProviderCardViewModel card = Card("Claude Code", Connected("five-hour", "5-hour", 95));
        watcher.Observe([card]);

        card.Apply(Connected("five-hour", "5-hour", 100), Now, Policy);

        Assert.Equal("100% used.", Assert.Single(watcher.Observe([card])).Text);
    }

    [Fact]
    public void FallingFromOneHundredToBelowEightyRaisesALimitResetAlert()
    {
        UsageAlertWatcher watcher = new();
        ProviderCardViewModel card = Card("Claude Code", Connected("five-hour", "5-hour", 100));
        watcher.Observe([card]);

        card.Apply(Connected("five-hour", "5-hour", 12, Now.AddHours(4).AddMinutes(58)), Now, Policy);

        UsageAlert alert = Assert.Single(watcher.Observe([card]));
        Assert.Equal(new UsageAlert(UsageAlertKind.Recovered, "Claude Code · 5-hour limit reset", "12% used. Resets in 4h 58m."), alert);
    }

    [Fact]
    public void FallingBelowEightyFromTheTightZoneRaisesABackUnderEightyAlert()
    {
        UsageAlertWatcher watcher = new();
        ProviderCardViewModel card = Card("Claude Code", Connected("five-hour", "5-hour", 86));
        watcher.Observe([card]);

        card.Apply(Connected("five-hour", "5-hour", 62, Now.AddHours(2).AddMinutes(3)), Now, Policy);

        UsageAlert alert = Assert.Single(watcher.Observe([card]));
        Assert.Equal(new UsageAlert(UsageAlertKind.Recovered, "Claude Code · 5-hour back under 80%", "62% used. Resets in 2h 03m."), alert);
    }

    [Fact]
    public void FallingWithinTheTightZoneDoesNotAnnounceRecovery()
    {
        UsageAlertWatcher watcher = new();
        ProviderCardViewModel card = Card("Claude Code", Connected("five-hour", "5-hour", 86));
        watcher.Observe([card]);

        card.Apply(Connected("five-hour", "5-hour", 82), Now, Policy);

        Assert.Empty(watcher.Observe([card]));
    }

    [Fact]
    public void ProviderWorkingToFailingRaisesAFailureWithoutTheReason()
    {
        UsageAlertWatcher watcher = new();
        ProviderCardViewModel card = Card("Claude Code", Connected("five-hour", "5-hour", 20));
        watcher.Observe([card]);

        card.Apply(Snapshot(ConnectionState.Error, error: "bearer secret"), Now, Policy);

        Assert.Equal(new UsageAlert(UsageAlertKind.ProviderFailed, "Claude Code stopped reporting usage", "Open the widget for the reason."), Assert.Single(watcher.Observe([card])));
    }

    [Fact]
    public void ProviderFailingToWorkingRaisesARecoveryAlert()
    {
        UsageAlertWatcher watcher = new();
        ProviderCardViewModel card = Card("Claude Code", Snapshot(ConnectionState.Error));
        Assert.Empty(watcher.Observe([card]));

        card.Apply(Connected("five-hour", "5-hour", 20), Now, Policy);

        Assert.Equal(new UsageAlert(UsageAlertKind.ProviderRecovered, "Claude Code is reporting usage again", "The numbers on the card are current."), Assert.Single(watcher.Observe([card])));
    }

    [Theory]
    [InlineData(ConnectionState.Discovering)]
    [InlineData(ConnectionState.Waiting)]
    public void UnknownProviderStatesDoNotSeedHealth(ConnectionState state)
    {
        UsageAlertWatcher watcher = new();
        ProviderCardViewModel card = Card("Claude Code", Snapshot(state));
        watcher.Observe([card]);

        card.Apply(Snapshot(ConnectionState.Error), Now, Policy);

        Assert.Empty(watcher.Observe([card]));
    }

    [Fact]
    public void AbsentProviderTransitionsAreNeverAnnounced()
    {
        UsageAlertWatcher watcher = new();
        ProviderCardViewModel card = Card("Claude Code", Snapshot(ConnectionState.NotInstalled));
        watcher.Observe([card]);

        card.Apply(Snapshot(ConnectionState.Connected), Now, Policy);

        Assert.Empty(watcher.Observe([card]));
    }

    [Fact]
    public void AJumpRaisesOnlyTheHighestCrossedRung()
    {
        UsageAlertWatcher watcher = new();
        ProviderCardViewModel card = Card("Claude Code", Connected("five-hour", "5-hour", 12));
        watcher.Observe([card]);

        card.Apply(Connected("five-hour", "5-hour", 92), Now, Policy);

        UsageAlert alert = Assert.Single(watcher.Observe([card]));
        Assert.Equal(UsageAlertKind.Milestone, alert.Kind);
        Assert.Equal("Claude Code · 5-hour past 90%", alert.Title);
    }

    [Fact]
    public void NullPercentageLeavesTheWindowStateAlone()
    {
        UsageAlertWatcher watcher = new();
        ProviderCardViewModel card = Card("Claude Code", Connected("five-hour", "5-hour", 79));
        watcher.Observe([card]);

        card.Apply(Connected("five-hour", "5-hour", null), Now, Policy);
        Assert.Empty(watcher.Observe([card]));

        card.Apply(Connected("five-hour", "5-hour", 81), Now, Policy);
        Assert.Equal(UsageAlertKind.Milestone, Assert.Single(watcher.Observe([card])).Kind);
    }

    [Fact]
    public void ProvidersKeepWindowStateSeparate()
    {
        UsageAlertWatcher watcher = new();
        ProviderCardViewModel claude = Card("Claude Code", Connected("five-hour", "5-hour", 79));
        ProviderCardViewModel codex = Card("Codex", Connected("five-hour", "5-hour", 12));
        watcher.Observe([claude, codex]);

        claude.Apply(Connected("five-hour", "5-hour", 81), Now, Policy);
        codex.Apply(Connected("five-hour", "5-hour", 12), Now, Policy);

        UsageAlert alert = Assert.Single(watcher.Observe([claude, codex]));
        Assert.Equal("Claude Code · 5-hour past 80%", alert.Title);
    }

    [Fact]
    public void AWindowKeepsItsStateAcrossAProviderFailure()
    {
        UsageAlertWatcher watcher = new();
        ProviderCardViewModel card = Card("Claude Code", Connected("five-hour", "5-hour", 79));
        watcher.Observe([card]);

        card.Apply(Snapshot(ConnectionState.Error), Now, Policy);
        watcher.Observe([card]);
        card.Apply(Connected("five-hour", "5-hour", 81), Now, Policy);

        UsageAlert[] alerts = [.. watcher.Observe([card])];
        Assert.Contains(alerts, alert => alert.Kind == UsageAlertKind.ProviderRecovered);
        Assert.Contains(alerts, alert => alert.Title == "Claude Code · 5-hour past 80%");
    }

    private static ProviderCardViewModel Card(string name, ProviderSnapshot snapshot)
    {
        ProviderCardViewModel card = new(new ProviderDescriptor(name, name[..2], new SilentProbe(name)), colorBarsByUsage: true, _ => { });
        card.Apply(snapshot, Now, Policy);
        return card;
    }

    private static ProviderSnapshot Connected(string id, string label, double? used, DateTimeOffset? resetsAt = null) =>
        Snapshot(ConnectionState.Connected, [Window(id, label, used, resetsAt)]);

    private static ProviderSnapshot Snapshot(ConnectionState state, IReadOnlyList<QuotaWindow>? windows = null, string? error = null) => new(
        ProviderName: "provider",
        Installed: state != ConnectionState.NotInstalled,
        Version: null,
        ExecutablePath: null,
        State: state,
        Mechanism: "test mechanism",
        Tier: MechanismTier.Unofficial,
        UpdateModel: "pull (poll)",
        Windows: windows ?? [],
        RetrievedAt: Now,
        Error: error,
        Notes: []);

    private static QuotaWindow Window(string id, string label, double? used, DateTimeOffset? resetsAt) => new(
        Id: id, Label: label, UsedPercent: used, ResetsAt: resetsAt, WindowDuration: null,
        Order: 0, IsPartial: true, Extra: new Dictionary<string, string>(), LabelIsProviderToken: true);
}
