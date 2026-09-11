using AiUsageMonitor.Infrastructure.Providers.Claude;
using AiUsageMonitor.Infrastructure.Tests.Fakes;

namespace AiUsageMonitor.Infrastructure.Tests;

public sealed class ClaudeSignInRepairTests
{
    private const string ExePath = "C:\\tools\\claude.exe";

    private static readonly DateTimeOffset Stale = DateTimeOffset.Parse("2026-09-11T10:00:00Z");
    private static readonly DateTimeOffset Renewed = DateTimeOffset.Parse("2026-09-11T22:00:00Z");

    private static ClaudeAccountMetadata Metadata(DateTimeOffset? expiresAt) =>
        new(expiresAt, RefreshTokenExpiresAt: null, SubscriptionType: null, RateLimitTier: null);

    [Fact]
    public async Task RenewalThatHappenedElsewhereIsAcceptedWithoutSpawningAnything()
    {
        var processes = new FakeProcessRunner();
        var repair = new ClaudeSignInRepair(processes);
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
        var repair = new ClaudeSignInRepair(processes);
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
        var repair = new ClaudeSignInRepair(processes);

        bool repaired = await repair.TryRepairAsync(ExePath, Stale, () => Metadata(Stale), [], CancellationToken.None);

        Assert.False(repaired);
        Assert.Equal(1, processes.RunCapturedCallCount(ExePath));
    }

    [Fact]
    public async Task AnUnreadableCredentialFileIsNeverMistakenForARotation()
    {
        var processes = new FakeProcessRunner();
        processes.EnqueueCaptured(ExePath, ClaudeSignInRepair.RepairArguments, 0, string.Empty);
        var repair = new ClaudeSignInRepair(processes);

        bool repaired = await repair.TryRepairAsync(
            ExePath, Stale, () => ClaudeAccountMetadata.Empty, [], CancellationToken.None);

        Assert.False(repaired);
    }

    [Fact]
    public async Task TheSameStaleExpiryIsOnlyEverAttemptedOnce()
    {
        var processes = new FakeProcessRunner();
        processes.EnqueueCaptured(ExePath, ClaudeSignInRepair.RepairArguments, 0, string.Empty);
        var repair = new ClaudeSignInRepair(processes);

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
        var repair = new ClaudeSignInRepair(processes);
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
        var repair = new ClaudeSignInRepair(processes);
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
        var repair = new ClaudeSignInRepair(processes);

        await repair.TryRepairAsync(ExePath, Stale, () => Metadata(Stale), [], CancellationToken.None);
        DateTimeOffset other = Stale.AddHours(8);
        var reloads = new Queue<ClaudeAccountMetadata>([Metadata(other), Metadata(Renewed)]);
        await repair.TryRepairAsync(ExePath, other, reloads.Dequeue, [], CancellationToken.None);
        processes.EnqueueCaptured(ExePath, ClaudeSignInRepair.RepairArguments, 0, string.Empty);
        await repair.TryRepairAsync(ExePath, Stale, () => Metadata(Stale), [], CancellationToken.None);

        Assert.Equal(3, processes.RunCapturedCallCount(ExePath));
    }
}
