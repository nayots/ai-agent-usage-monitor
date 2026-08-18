using System.ComponentModel;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers.Claude;
using AiUsageMonitor.Infrastructure.Providers.Codex;
using AiUsageMonitor.Infrastructure.Tests.Fakes;

namespace AiUsageMonitor.Infrastructure.Tests;

/// <summary>
/// Both probes look for their provider on a cadence of their own rather than on every read. These
/// tests use the not-installed path deliberately: it returns before any HTTP request or process
/// launch, so what they count is the detection itself and nothing else.
/// </summary>
public sealed class ProbeInstallationCachingTests
{
    private const string ExePath = "C:\\tools\\claude.exe";

    private static readonly DateTimeOffset Start = new(2026, 8, 18, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ClaudeLooksForItsExecutableOnceWithinTheLifetime()
    {
        int lookups = 0;
        DateTimeOffset now = Start;
        ClaudeOAuthUsageProbe probe = ClaudeProbe(() => { lookups++; return null; }, () => now);

        await probe.ProbeAsync(CancellationToken.None);
        now = now.AddMinutes(5);
        await probe.ProbeAsync(CancellationToken.None);
        now = now.AddMinutes(20);
        await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(1, lookups);
    }

    [Fact]
    public async Task ClaudeLooksAgainOnceTheLifetimeHasLapsed()
    {
        int lookups = 0;
        DateTimeOffset now = Start;
        ClaudeOAuthUsageProbe probe = ClaudeProbe(() => { lookups++; return null; }, () => now);

        await probe.ProbeAsync(CancellationToken.None);
        now = now.AddMinutes(31);
        await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(2, lookups);
    }

    [Fact]
    public async Task ClaudeLooksAgainImmediatelyAfterAnInvalidation()
    {
        int lookups = 0;
        DateTimeOffset now = Start;
        ClaudeOAuthUsageProbe probe = ClaudeProbe(() => { lookups++; return null; }, () => now);

        await probe.ProbeAsync(CancellationToken.None);
        probe.InvalidateInstallation();
        now = now.AddSeconds(1);
        await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(2, lookups);
    }

    [Fact]
    public async Task ClaudeSaysInItsNotesWhenADetectionWasReused()
    {
        DateTimeOffset now = Start;
        ClaudeOAuthUsageProbe probe = ClaudeProbe(() => null, () => now);

        await probe.ProbeAsync(CancellationToken.None);
        now = now.AddMinutes(5);
        ProviderSnapshot second = await probe.ProbeAsync(CancellationToken.None);

        Assert.Contains(second.Notes, note => note.Contains("re-used from a check 5 minutes ago", StringComparison.Ordinal));
        Assert.Contains(second.Notes, note => note.Contains("Re-check providers", StringComparison.Ordinal));
    }

    /// <summary>
    /// A --version that fails is part of the detection, so it is held for the lifetime like any other
    /// result rather than retried on the next quota read. Stated as a test because it is the one cost
    /// of the outer cache that is not obvious: before it existed, a transient failure was re-attempted
    /// a minute later. An invalidation is the way back, and the second half pins that it works.
    /// </summary>
    [Fact]
    public async Task AFailedVersionReadIsHeldForTheLifetimeAndRetriedAfterAnInvalidation()
    {
        FakeProcessRunner processes = new();
        processes.EnqueueCapturedFailure(ExePath, "--version", new Win32Exception());
        processes.EnqueueCaptured(ExePath, "--version", 0, "2.1.233 (Claude Code)");

        DateTimeOffset now = Start;
        ClaudeOAuthUsageProbe probe = new(
            processes, handler: null, () => ExePath, () => "missing-credentials.json",
            lastWriteUtc: _ => new DateTime(2026, 8, 18, 9, 0, 0, DateTimeKind.Utc),
            clock: () => now);

        ProviderSnapshot first = await probe.ProbeAsync(CancellationToken.None);
        now = now.AddMinutes(5);
        ProviderSnapshot second = await probe.ProbeAsync(CancellationToken.None);

        Assert.Null(first.Version);
        Assert.Null(second.Version);
        Assert.Equal(1, processes.RunCapturedCallCount(ExePath));

        probe.InvalidateInstallation();
        ProviderSnapshot third = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal("2.1.233", third.Version);
        Assert.Equal(2, processes.RunCapturedCallCount(ExePath));
    }

    [Fact]
    public async Task CodexLooksForItsExecutableOnceWithinTheLifetime()
    {
        int lookups = 0;
        DateTimeOffset now = Start;
        CodexProbe probe = new(new FakeProcessRunner(), () => { lookups++; return null; }, clock: () => now);

        await probe.ProbeAsync(CancellationToken.None);
        now = now.AddMinutes(5);
        await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(1, lookups);
    }

    [Fact]
    public async Task CodexLooksAgainImmediatelyAfterAnInvalidation()
    {
        int lookups = 0;
        DateTimeOffset now = Start;
        CodexProbe probe = new(new FakeProcessRunner(), () => { lookups++; return null; }, clock: () => now);

        await probe.ProbeAsync(CancellationToken.None);
        probe.InvalidateInstallation();
        await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(2, lookups);
    }

    /// <summary>
    /// The path list is what tells a user where the application looked, so it has to survive on a
    /// snapshot built from a re-used detection rather than only on the one that did the looking.
    /// </summary>
    [Fact]
    public async Task CodexStillReportsWhereItLookedOnAReusedDetection()
    {
        DateTimeOffset now = Start;
        CodexProbe probe = new(new FakeProcessRunner(), () => null, clock: () => now);

        await probe.ProbeAsync(CancellationToken.None);
        now = now.AddMinutes(5);
        ProviderSnapshot second = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal(ConnectionState.NotInstalled, second.State);
        Assert.Contains(second.Notes, note => note.Contains("Checked (in order)", StringComparison.Ordinal));
    }

    // No HTTP handler: every case here stops at "not installed", which returns before any request
    // is built. A stub would only assert that the untaken path stayed untaken.
    private static ClaudeOAuthUsageProbe ClaudeProbe(Func<string?> locate, Func<DateTimeOffset> clock) =>
        new(new FakeProcessRunner(), handler: null, locate, () => "unused", clock: clock);
}
