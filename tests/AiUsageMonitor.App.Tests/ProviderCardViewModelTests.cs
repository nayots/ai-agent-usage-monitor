using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;

namespace AiUsageMonitor.App.Tests;

public class ProviderCardViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly FreshnessPolicy Policy = new(TimeSpan.FromMinutes(5));

    private sealed class SilentProbe : IProviderProbe
    {
        public string Name => "Claude Code";

        public Task<ProviderSnapshot> ProbeAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private static ProviderCardViewModel Card() =>
        new(new ProviderDescriptor("Claude Code", "CC", new SilentProbe()), colorBarsByUsage: true, _ => { });

    private static ProviderSnapshot Snapshot(
        ConnectionState state = ConnectionState.Connected,
        string? version = "2.1.227",
        IReadOnlyList<QuotaWindow>? windows = null,
        DateTimeOffset? retrievedAt = null,
        string? error = null) => new(
            ProviderName: "Claude Code",
            Installed: state != ConnectionState.NotInstalled,
            Version: version,
            ExecutablePath: null,
            State: state,
            Mechanism: "Anthropic OAuth usage endpoint (UNOFFICIAL/undocumented)",
            Tier: MechanismTier.Unofficial,
            UpdateModel: "pull (poll)",
            Windows: windows ?? [],
            RetrievedAt: retrievedAt,
            Error: error,
            Notes: []);

    private static QuotaWindow Window(string id, int order, double used) => new(
        Id: id, Label: id, UsedPercent: used, ResetsAt: null, WindowDuration: null,
        Order: order, IsPartial: true, Extra: new Dictionary<string, string>(), LabelIsProviderToken: true);

    [Fact]
    public void IdentityComesFromTheDescriptorNotTheSnapshot()
    {
        ProviderCardViewModel card = Card();

        Assert.Equal("Claude Code", card.DisplayName);
        Assert.Equal("CC", card.Monogram);
    }

    [Fact]
    public void VersionIsPrefixedOnlyWhenTheProviderReportedOne()
    {
        ProviderCardViewModel card = Card();

        card.Apply(Snapshot(retrievedAt: Now), Now, Policy);
        Assert.Equal("v2.1.227", card.VersionText);

        card.Apply(Snapshot(version: null, retrievedAt: Now), Now, Policy);
        Assert.Null(card.VersionText);
    }

    [Fact]
    public void TheTierIsAlwaysCarriedThroughFromTheSnapshot()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(retrievedAt: Now), Now, Policy);

        Assert.Equal(MechanismTier.Unofficial, card.Tier);
    }

    [Fact]
    public void WindowsKeepTheOrderTheProviderReportedThem()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(windows: [Window("c", 2, 10), Window("a", 0, 20), Window("b", 1, 30)], retrievedAt: Now), Now, Policy);

        Assert.Equal(["a", "b", "c"], card.Windows.Select(w => w.Label));
    }

    [Fact]
    public void AConnectedSnapshotOlderThanTheThresholdBecomesStale()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(windows: [Window("a", 0, 20)], retrievedAt: Now.AddMinutes(-6)), Now, Policy);

        Assert.Equal(ConnectionState.Stale, card.State);
        Assert.True(card.IsStale);
        Assert.Equal("6 minutes ago", card.StaleAgeText);
        Assert.True(card.Windows[0].IsStale);
    }

    [Fact]
    public void AFreshCardGoesStaleFromTheClockAloneWithoutANewSnapshot()
    {
        // The test above ages the snapshot before Apply ever sees it, so it passes even if
        // freshness is evaluated once and never again. This is the case that actually happens: a
        // card that was fresh when applied and is still holding that same snapshot when the
        // threshold passes. Nothing new arrives to re-trigger Apply while a provider is mid-backoff
        // (skipped for up to eight refresh intervals), after a resume from sleep, or whenever
        // RefreshIntervalSeconds is set longer than StaleAfterSeconds - both are user-editable and
        // clamped independently, so that combination is legal.
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(windows: [Window("a", 0, 20)], retrievedAt: Now), Now, Policy);

        Assert.Equal(ConnectionState.Connected, card.State);
        Assert.False(card.Windows[0].IsStale);

        card.Tick(Now.AddMinutes(6));

        Assert.Equal(ConnectionState.Stale, card.State);
        Assert.True(card.IsStale);
        Assert.True(card.Windows[0].IsStale);
    }

    [Fact]
    public void AStaleCardRecoversOnTheNextSuccessfulSnapshot()
    {
        // ApplyFreshness reads the snapshot's own state rather than the card's, so ageing must be
        // reversible: a card that went Stale purely from the clock has to come back to Connected
        // when a fresh snapshot lands, not stay latched.
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(windows: [Window("a", 0, 20)], retrievedAt: Now), Now, Policy);
        card.Tick(Now.AddMinutes(6));
        Assert.Equal(ConnectionState.Stale, card.State);

        DateTimeOffset later = Now.AddMinutes(6);
        card.Apply(Snapshot(windows: [Window("a", 0, 20)], retrievedAt: later), later, Policy);

        Assert.Equal(ConnectionState.Connected, card.State);
        Assert.False(card.Windows[0].IsStale);
    }

    [Fact]
    public void TickingAnErrorCardNeverAgesItIntoStale()
    {
        // Recomputing state every tick must not let age mask a failure any more than Apply does.
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(state: ConnectionState.Error, retrievedAt: Now, error: "boom"), Now, Policy);

        card.Tick(Now.AddHours(3));

        Assert.Equal(ConnectionState.Error, card.State);
        Assert.False(card.IsStale);
    }

    [Fact]
    public void AgeNeverMasksARealFailure()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(state: ConnectionState.Error, retrievedAt: Now.AddHours(-2), error: "boom"), Now, Policy);

        Assert.Equal(ConnectionState.Error, card.State);
        Assert.False(card.IsStale);
    }

    [Fact]
    public void UpdatedTextIsAbsentUntilSomethingHasSucceeded()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(state: ConnectionState.Waiting, retrievedAt: null), Now, Policy);

        Assert.Null(card.UpdatedText);
    }

    [Fact]
    public void UpdatedTextTracksTheLocalClock()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(windows: [Window("a", 0, 20)], retrievedAt: Now), Now, Policy);
        Assert.Equal("Updated 0s ago", card.UpdatedText);

        card.Tick(Now.AddSeconds(12));
        Assert.Equal("Updated 12s ago", card.UpdatedText);
    }

    [Fact]
    public void ConnectedWithWindowsShowsNoNotice()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(windows: [Window("a", 0, 20)], retrievedAt: Now), Now, Policy);

        Assert.Null(card.Notice);
    }

    [Fact]
    public void ConnectedWithNoWindowsIsNeitherAnErrorNorZeroUsage()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(retrievedAt: Now), Now, Policy);

        Assert.Equal("No quota windows reported", card.Notice!.Title);
        Assert.False(card.Notice.IsAlert);
    }

    [Fact]
    public void NotInstalledKeepsItsCardAndOffersARecheck()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(state: ConnectionState.NotInstalled, version: null), Now, Policy);

        Assert.Equal("Not installed on this machine", card.Notice!.Title);
        Assert.Equal("Check again", card.Notice.ActionText);
        Assert.False(card.Notice.IsAlert);
    }

    [Fact]
    public void UnavailableCommunicatesThatThereIsNoFallback()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(state: ConnectionState.Unavailable, retrievedAt: Now.AddHours(-2), error: "Claude Code is installed but has not stored a sign-in on this machine."), Now, Policy);

        Assert.Equal("Usage can no longer be read", card.Notice!.Title);
        Assert.True(card.Notice.IsAlert);
        Assert.Contains("no second source", card.Notice.Body);
        Assert.Contains("Claude Code is installed but has not stored a sign-in", card.Notice.Body);
        Assert.Contains("2 hours ago", card.Notice.Body);
        Assert.Equal("Retry now", card.Notice.ActionText);
    }

    [Fact]
    public void ANoticeNeverInventsAnAgeThatDoesNotExist()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(state: ConnectionState.Unavailable, retrievedAt: null), Now, Policy);

        Assert.DoesNotContain("Last successful update", card.Notice!.Body);
    }

    [Fact]
    public void TheStatusRowIsShownForEveryState()
    {
        // Compact mode hides the Connected chip; the default widget never does.
        foreach (ConnectionState state in Enum.GetValues<ConnectionState>())
        {
            ProviderCardViewModel card = Card();
            card.Apply(Snapshot(state: state, retrievedAt: Now), Now, Policy);
            Assert.False(string.IsNullOrWhiteSpace(card.StateLabel));
        }
    }

    [Fact]
    public void EveryConnectionStateHasItsOwnWord()
    {
        string[] labels = Enum.GetValues<ConnectionState>().Select(ConnectionStateText.Label).ToArray();

        Assert.Equal(labels.Length, labels.Distinct().Count());
        Assert.All(labels, label => Assert.False(string.IsNullOrWhiteSpace(label)));
    }

    [Fact]
    public void RetryingAsksTheServiceForThisProviderOnly()
    {
        List<string> retried = [];
        ProviderCardViewModel card = new(
            new ProviderDescriptor("Claude Code", "CC", new SilentProbe()),
            colorBarsByUsage: true,
            descriptor => retried.Add(descriptor.DisplayName));

        card.RetryCommand.Execute(null);

        Assert.Equal(["Claude Code"], retried);
    }
}
