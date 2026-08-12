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

    [Theory]
    // Claude Code's --version is a bare number and wants the v. codex-cli prints its own name and
    // rendered live as "vcodex-cli 0.144.6" until this was fixed - the card must not assume one
    // provider's shape.
    [InlineData("2.1.228", "v2.1.228")]
    [InlineData("0.144.6", "v0.144.6")]
    [InlineData("codex-cli 0.144.6", "codex-cli 0.144.6")]
    [InlineData("  2.1.228  ", "v2.1.228")]
    [InlineData("   ", null)]
    public void AVersionIsPrefixedOnlyWhenItIsBareEnoughToNeedIt(string version, string? expected)
    {
        ProviderCardViewModel card = Card();

        card.Apply(Snapshot(version: version, retrievedAt: Now), Now, Policy);

        Assert.Equal(expected, card.VersionText);
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
        Assert.Equal("Updated 6 minutes ago", card.TimestampText);
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
    public void TheTimestampLineIsAbsentUntilSomethingHasSucceeded()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(state: ConnectionState.Waiting, retrievedAt: null), Now, Policy);

        Assert.Null(card.TimestampText);
    }

    [Fact]
    public void TheTimestampLineTracksTheLocalClock()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(windows: [Window("a", 0, 20)], retrievedAt: Now), Now, Policy);
        Assert.Equal("Updated 0s ago", card.TimestampText);

        card.Tick(Now.AddSeconds(12));
        Assert.Equal("Updated 12s ago", card.TimestampText);
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
        Assert.Equal("Retry now", card.Notice.ActionText);
    }

    [Fact]
    public void ACardThatBreaksReportsHowLongItHasBeenSinceItLastWorked()
    {
        // Every production failure path reports RetrievedAt null, so the failing snapshot cannot
        // say this about itself. Without it the card cannot tell a provider that broke a moment ago
        // from one that has been down all day - the question a user actually has during an outage.
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(windows: [Window("a", 0, 20)], retrievedAt: Now), Now, Policy);

        DateTimeOffset broke = Now.AddHours(9);
        card.Apply(Snapshot(state: ConnectionState.Error, retrievedAt: null, error: "boom"), broke, Policy);

        Assert.Equal("Last succeeded 9 hours ago", card.TimestampText);

        card.Tick(broke.AddHours(1));
        Assert.Equal("Last succeeded 10 hours ago", card.TimestampText);
    }

    [Fact]
    public void AFailingCardNeverClaimsToHaveBeenUpdated()
    {
        // The two forms share one line, so the noun is what distinguishes them. A card with no rows
        // on it saying "Updated" would be claiming freshness for data that is not on screen.
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(windows: [Window("a", 0, 20)], retrievedAt: Now), Now, Policy);
        card.Apply(Snapshot(state: ConnectionState.Unavailable, retrievedAt: null, error: "boom"), Now.AddMinutes(3), Policy);

        Assert.DoesNotContain("Updated", card.TimestampText);
    }

    [Fact]
    public void ACardThatHasNeverSucceededDatesNothing()
    {
        // Failing since launch. There is no last success to count from, and inventing one - or
        // dating the failure from process start - would be fabricating a fact nobody reported.
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(state: ConnectionState.Error, retrievedAt: null, error: "boom"), Now, Policy);

        Assert.Null(card.TimestampText);
    }

    [Fact]
    public void OnlyAFailureIsWorthDating()
    {
        // A provider uninstalled mid-session has a last success on record, but "last succeeded 3
        // minutes ago" next to "Not installed on this machine" implies something is still being
        // attempted. Settled facts about the machine carry no duration.
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(windows: [Window("a", 0, 20)], retrievedAt: Now), Now, Policy);
        card.Apply(Snapshot(state: ConnectionState.NotInstalled, version: null, retrievedAt: null), Now.AddMinutes(3), Policy);

        Assert.Null(card.TimestampText);
    }

    [Fact]
    public void ARecoveredCardGoesBackToDatingItsData()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(windows: [Window("a", 0, 20)], retrievedAt: Now), Now, Policy);
        card.Apply(Snapshot(state: ConnectionState.Error, retrievedAt: null, error: "boom"), Now.AddHours(9), Policy);

        DateTimeOffset recovered = Now.AddHours(10);
        card.Apply(Snapshot(windows: [Window("a", 0, 20)], retrievedAt: recovered), recovered, Policy);

        Assert.Equal("Updated 0s ago", card.TimestampText);
    }

    [Fact]
    public void ACardStatesItsAgeOnceAndOnlyInTheHeader()
    {
        // Both the notice body and the stale banner used to restate the header's age from the same
        // RetrievedAt, under the same condition, a few pixels below it. A card that has an age says
        // so exactly once, whichever of the two forms that line is currently in.
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(windows: [Window("a", 0, 20)], retrievedAt: Now), Now, Policy);
        card.Apply(Snapshot(state: ConnectionState.Unavailable, retrievedAt: null, error: "boom"), Now.AddHours(2), Policy);

        Assert.Equal("Last succeeded 2 hours ago", card.TimestampText);
        Assert.DoesNotContain("2 hours ago", card.Notice!.Body);
        Assert.DoesNotContain("Last successful update", card.Notice.Body);
    }

    [Fact]
    public void ANoticeNeverInventsAnAgeThatDoesNotExist()
    {
        // The production failure paths all report RetrievedAt: null, so this is the shape a real
        // Unavailable card has - and neither line may fabricate an age to fill the gap.
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(state: ConnectionState.Unavailable, retrievedAt: null), Now, Policy);

        Assert.Null(card.TimestampText);
        Assert.DoesNotContain("ago", card.Notice!.Body);
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

    [Fact]
    public void TurningColourBandsOffRebuildsTheRowsThatRenderThem()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(windows: [Window("five_hour", 0, 82)], retrievedAt: Now), Now, Policy);

        card.ColorBarsByUsage = false;

        Assert.Single(card.Windows);
        Assert.False(card.Windows[0].ColorBarsByUsage);
        Assert.Equal(82, card.Windows[0].UsedPercent);
    }

    [Fact]
    public void AHiddenProviderIsOnlyHiddenWhenItIsActuallyAbsent()
    {
        // NotInstalled and Unsupported are facts about the machine. An Error is a provider that is
        // present and broken, and hiding it would hide the one card the user needs to see.
        ProviderCardViewModel card = Card();
        card.ShowWhenUnavailable = false;

        card.Apply(Snapshot(ConnectionState.Error, error: "boom"), Now, Policy);
        Assert.False(card.IsHiddenByFilter);

        card.Apply(Snapshot(ConnectionState.NotInstalled), Now, Policy);
        Assert.True(card.IsHiddenByFilter);

        card.ShowWhenUnavailable = true;
        Assert.False(card.IsHiddenByFilter);
    }

    [Fact]
    public void AConnectedCardDropsItsStatusLineOnlyWhenCompact()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(ConnectionState.Connected, retrievedAt: Now), Now, Policy);

        Assert.True(card.ShowStatusLine);
        Assert.False(card.ShowCompactSpacer);

        card.IsCompact = true;

        Assert.False(card.ShowStatusLine);
        Assert.True(card.ShowCompactSpacer);
    }

    [Theory]
    [InlineData(ConnectionState.Error)]
    [InlineData(ConnectionState.Stale)]
    [InlineData(ConnectionState.Waiting)]
    [InlineData(ConnectionState.Unavailable)]
    [InlineData(ConnectionState.Unsupported)]
    [InlineData(ConnectionState.NotInstalled)]
    [InlineData(ConnectionState.Discovering)]
    public void ACardThatIsNotConnectedKeepsItsStatusLineEvenWhenCompact(ConnectionState state)
    {
        ProviderCardViewModel card = Card();
        card.IsCompact = true;
        card.Apply(Snapshot(state, retrievedAt: null), Now, Policy);

        Assert.True(card.ShowStatusLine);
        Assert.False(card.ShowCompactSpacer);
    }

    [Fact]
    public void AStateChangeRepublishesTheStatusLineWithoutADensityChange()
    {
        ProviderCardViewModel card = Card();
        card.IsCompact = true;
        card.Apply(Snapshot(ConnectionState.Connected, retrievedAt: Now), Now, Policy);

        List<string?> raised = [];
        card.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        card.Tick(Now.AddHours(1));

        Assert.Equal(ConnectionState.Stale, card.State);
        Assert.Contains(nameof(ProviderCardViewModel.ShowStatusLine), raised);
        Assert.True(card.ShowStatusLine);
    }
}
