using AiUsageMonitor.Infrastructure.Providers.Claude;
using AiUsageMonitor.Infrastructure.Tests.Fakes;
using Microsoft.Extensions.Logging;

namespace AiUsageMonitor.Infrastructure.Tests;

public sealed class ClaudeSignInRepairTests
{
    private const string ExePath = "C:\\tools\\claude.exe";

    // Pinned, not ambient. Liveness is half of what decides a repair worked, so a real clock would
    // make every "renewed" fixture below expire on 2026-09-11 and start failing the next day.
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-11T12:00:00Z");

    private static readonly DateTimeOffset Stale = DateTimeOffset.Parse("2026-09-11T10:00:00Z");
    private static readonly DateTimeOffset Renewed = DateTimeOffset.Parse("2026-09-11T22:00:00Z");

    private static ClaudeSignInRepair CreateRepair(FakeProcessRunner processes) => new(processes, () => Now);

    private static ClaudeSignInRepair CreateRepair(
        FakeProcessRunner processes, CapturingLogger<ClaudeOAuthUsageProbe> logger) =>
        new(processes, () => Now, logger);

    [Fact]
    public async Task ASuccessfulRenewalIsRecordedInTheLogAsWellAsTheNotes()
    {
        var processes = new FakeProcessRunner();
        processes.EnqueueCaptured(ExePath, ClaudeSignInRepair.RepairArguments, 0, string.Empty);
        var logger = new CapturingLogger<ClaudeOAuthUsageProbe>();
        ClaudeSignInRepair repair = CreateRepair(processes, logger);
        var reloads = new Queue<ClaudeAccountMetadata>([Metadata(Stale), Metadata(Renewed)]);

        bool repaired = await repair.TryRepairAsync(
            ExePath, Stale, reloads.Dequeue, [], CancellationToken.None);

        Assert.True(repaired);
        Assert.Contains(logger.MessagesAt(LogLevel.Information), m => m.Contains("asking Claude Code to renew", StringComparison.Ordinal));
        Assert.Contains(logger.MessagesAt(LogLevel.Information), m => m.Contains("renewed its sign-in", StringComparison.Ordinal));
        Assert.Empty(logger.MessagesAt(LogLevel.Warning));
    }

    [Fact]
    public async Task ARenewalThatChangedNothingIsLoggedAsAWarning()
    {
        var processes = new FakeProcessRunner();
        processes.EnqueueCaptured(ExePath, ClaudeSignInRepair.RepairArguments, 0, string.Empty);
        var logger = new CapturingLogger<ClaudeOAuthUsageProbe>();
        ClaudeSignInRepair repair = CreateRepair(processes, logger);

        await repair.TryRepairAsync(ExePath, Stale, () => Metadata(Stale), [], CancellationToken.None);

        Assert.Contains(logger.MessagesAt(LogLevel.Warning), m => m.Contains("did not change", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFailedLaunchLogsTheExceptionTypeAndNeverItsMessage()
    {
        var processes = new FakeProcessRunner();
        processes.EnqueueCapturedFailure(
            ExePath, ClaudeSignInRepair.RepairArguments, new IOException("C:\\secret\\path failed"));
        var logger = new CapturingLogger<ClaudeOAuthUsageProbe>();
        ClaudeSignInRepair repair = CreateRepair(processes, logger);

        await repair.TryRepairAsync(ExePath, Stale, () => Metadata(Stale), [], CancellationToken.None);

        Assert.Contains(logger.MessagesAt(LogLevel.Warning), m => m.Contains("IOException", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, m => m.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ABlockedRetryIsSilentSoAStuckSignInDoesNotLogEveryPoll()
    {
        var processes = new FakeProcessRunner();
        processes.EnqueueCaptured(ExePath, ClaudeSignInRepair.RepairArguments, 0, string.Empty);
        var logger = new CapturingLogger<ClaudeOAuthUsageProbe>();
        ClaudeSignInRepair repair = CreateRepair(processes, logger);

        await repair.TryRepairAsync(ExePath, Stale, () => Metadata(Stale), [], CancellationToken.None);
        int afterFirst = logger.Entries.Count;

        // Three more polls against the same unrepaired sign-in.
        for (int i = 0; i < 3; i++)
        {
            await repair.TryRepairAsync(ExePath, Stale, () => Metadata(Stale), [], CancellationToken.None);
        }

        Assert.Equal(afterFirst, logger.Entries.Count);
    }

    private static ClaudeAccountMetadata Metadata(DateTimeOffset? expiresAt) =>
        new(expiresAt, RefreshTokenExpiresAt: null, SubscriptionType: null, RateLimitTier: null);

    [Fact]
    public async Task ARotationToAnotherAlreadySpentTokenIsNotARepair()
    {
        var processes = new FakeProcessRunner();
        processes.EnqueueCaptured(ExePath, ClaudeSignInRepair.RepairArguments, 0, string.Empty);
        ClaudeSignInRepair repair = CreateRepair(processes);
        var notes = new List<string>();

        // The expiry moved, so it is a different sign-in - but it is spent too. Accepting it would
        // send a request that can only be rejected, which is the whole point of not sending one.
        DateTimeOffset alsoSpent = Stale.AddMinutes(30);

        bool repaired = await repair.TryRepairAsync(
            ExePath, Stale, () => Metadata(alsoSpent), notes, CancellationToken.None);

        Assert.False(repaired);
    }

    [Fact]
    public async Task AStillFutureExpiryThatDidNotMoveIsNotARepair()
    {
        var processes = new FakeProcessRunner();
        processes.EnqueueCaptured(ExePath, ClaudeSignInRepair.RepairArguments, 0, string.Empty);
        ClaudeSignInRepair repair = CreateRepair(processes);

        // The rejected-token path: the endpoint refused a token the file still calls live. Nothing
        // changed, so there is nothing new to retry with.
        bool repaired = await repair.TryRepairAsync(
            ExePath, Renewed, () => Metadata(Renewed), [], CancellationToken.None);

        Assert.False(repaired);
    }

    [Fact]
    public async Task RenewalThatHappenedElsewhereIsAcceptedWithoutSpawningAnything()
    {
        var processes = new FakeProcessRunner();
        var repair = CreateRepair(processes);
        var notes = new List<string>();

        bool repaired = await repair.TryRepairAsync(
            ExePath, Stale, () => Metadata(Renewed), notes, CancellationToken.None);

        Assert.True(repaired);
        Assert.Equal(0, processes.RunCapturedCallCount(ExePath));
    }

    [Fact]
    public async Task ARotatedExpiryAfterTheCommandCountsAsRepaired()
    {
        var processes = new FakeProcessRunner();
        processes.EnqueueCaptured(ExePath, ClaudeSignInRepair.RepairArguments, 0, "Claude Code doctor");
        var repair = CreateRepair(processes);
        var notes = new List<string>();
        var reloads = new Queue<ClaudeAccountMetadata>([Metadata(Stale), Metadata(Renewed)]);

        bool repaired = await repair.TryRepairAsync(ExePath, Stale, reloads.Dequeue, notes, CancellationToken.None);

        Assert.True(repaired);
        Assert.Equal(1, processes.RunCapturedCallCount(ExePath));
    }

    [Fact]
    public async Task AnUnchangedExpiryAfterTheCommandIsNotRepaired()
    {
        var processes = new FakeProcessRunner();
        processes.EnqueueCaptured(ExePath, ClaudeSignInRepair.RepairArguments, 0, string.Empty);
        var repair = CreateRepair(processes);

        bool repaired = await repair.TryRepairAsync(ExePath, Stale, () => Metadata(Stale), [], CancellationToken.None);

        Assert.False(repaired);
        Assert.Equal(1, processes.RunCapturedCallCount(ExePath));
    }

    [Fact]
    public async Task AnUnreadableCredentialFileIsNeverMistakenForARotation()
    {
        var processes = new FakeProcessRunner();
        processes.EnqueueCaptured(ExePath, ClaudeSignInRepair.RepairArguments, 0, string.Empty);
        var repair = CreateRepair(processes);

        bool repaired = await repair.TryRepairAsync(
            ExePath, Stale, () => ClaudeAccountMetadata.Empty, [], CancellationToken.None);

        Assert.False(repaired);
    }

    [Fact]
    public async Task TheSameStaleExpiryIsOnlyEverAttemptedOnce()
    {
        var processes = new FakeProcessRunner();
        processes.EnqueueCaptured(ExePath, ClaudeSignInRepair.RepairArguments, 0, string.Empty);
        var repair = CreateRepair(processes);

        bool first = await repair.TryRepairAsync(ExePath, Stale, () => Metadata(Stale), [], CancellationToken.None);
        bool second = await repair.TryRepairAsync(ExePath, Stale, () => Metadata(Stale), [], CancellationToken.None);

        Assert.False(first);
        Assert.False(second);
        Assert.Equal(1, processes.RunCapturedCallCount(ExePath));
    }

    [Fact]
    public async Task ANewStaleExpiryIsAttemptedAgain()
    {
        var processes = new FakeProcessRunner();
        processes.EnqueueCaptured(ExePath, ClaudeSignInRepair.RepairArguments, 0, string.Empty);
        processes.EnqueueCaptured(ExePath, ClaudeSignInRepair.RepairArguments, 0, string.Empty);
        var repair = CreateRepair(processes);
        DateTimeOffset later = Stale.AddHours(8);

        await repair.TryRepairAsync(ExePath, Stale, () => Metadata(Stale), [], CancellationToken.None);
        await repair.TryRepairAsync(ExePath, later, () => Metadata(later), [], CancellationToken.None);

        Assert.Equal(2, processes.RunCapturedCallCount(ExePath));
    }

    [Fact]
    public async Task AFailedLaunchIsReportedAsNotRepairedAndBlocksTheNextAttempt()
    {
        var processes = new FakeProcessRunner();
        processes.EnqueueCapturedFailure(ExePath, ClaudeSignInRepair.RepairArguments, new IOException("boom"));
        var repair = CreateRepair(processes);
        var notes = new List<string>();

        bool repaired = await repair.TryRepairAsync(ExePath, Stale, () => Metadata(Stale), notes, CancellationToken.None);

        Assert.False(repaired);
        Assert.Contains(notes, n => n.Contains("IOException", StringComparison.Ordinal));
        Assert.DoesNotContain(notes, n => n.Contains("boom", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASuccessfulRepairClearsAnEarlierBlock()
    {
        var processes = new FakeProcessRunner();
        processes.EnqueueCaptured(ExePath, ClaudeSignInRepair.RepairArguments, 0, string.Empty);
        processes.EnqueueCaptured(ExePath, ClaudeSignInRepair.RepairArguments, 0, string.Empty);
        var repair = CreateRepair(processes);

        await repair.TryRepairAsync(ExePath, Stale, () => Metadata(Stale), [], CancellationToken.None);
        DateTimeOffset other = Stale.AddHours(8);
        var reloads = new Queue<ClaudeAccountMetadata>([Metadata(other), Metadata(Renewed)]);
        await repair.TryRepairAsync(ExePath, other, reloads.Dequeue, [], CancellationToken.None);
        processes.EnqueueCaptured(ExePath, ClaudeSignInRepair.RepairArguments, 0, string.Empty);
        await repair.TryRepairAsync(ExePath, Stale, () => Metadata(Stale), [], CancellationToken.None);

        Assert.Equal(3, processes.RunCapturedCallCount(ExePath));
    }
}
