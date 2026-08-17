using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Refresh;
using Microsoft.Extensions.Logging;

namespace AiUsageMonitor.Infrastructure.Tests;

public class ProviderRefreshServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeProbe(
        string name,
        Func<CancellationToken, Task<ProviderSnapshot>> behaviour,
        string mechanism = "fake",
        MechanismTier tier = MechanismTier.Official) : IProviderProbe
    {
        public int Calls { get; private set; }
        public string Name => name;
        public string Mechanism => mechanism;
        public MechanismTier Tier => tier;

        public Task<ProviderSnapshot> ProbeAsync(CancellationToken ct)
        {
            Calls++;
            return behaviour(ct);
        }
    }

    private static ProviderSnapshot Snapshot(
        string name,
        ConnectionState state,
        ThrottleAdvice? throttle = null,
        string? error = null,
        IReadOnlyList<string>? notes = null) => new(
        ProviderName: name,
        Installed: true,
        Version: null,
        ExecutablePath: null,
        State: state,
        Mechanism: "fake",
        Tier: MechanismTier.Official,
        UpdateModel: "pull (poll)",
        Windows: [],
        RetrievedAt: state == ConnectionState.Connected ? Now : null,
        Error: error,
        Notes: notes ?? [],
        Throttle: throttle);

    private static ProviderDescriptor Descriptor(
        string name,
        Func<CancellationToken, Task<ProviderSnapshot>> behaviour,
        string mechanism = "fake",
        MechanismTier tier = MechanismTier.Official) =>
        new(name.ToLowerInvariant(), name, name[..1], new FakeProbe(name, behaviour, mechanism, tier));

    private static ProviderRefreshService Service(params ProviderDescriptor[] providers) =>
        new(providers, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(60));

    private static ProviderRefreshService ServiceWithInterval(TimeSpan baseInterval, params ProviderDescriptor[] providers) =>
        new(providers, TimeSpan.FromMilliseconds(250), baseInterval);

    private sealed class CapturingLogger : ILogger<ProviderRefreshService>
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoopDisposable.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));

        private sealed class NoopDisposable : IDisposable
        {
            public static readonly NoopDisposable Instance = new();
            public void Dispose() { }
        }
    }

    [Fact]
    public void ActivityForAnUnprobedProviderIsEmpty()
    {
        ProviderDescriptor provider = Descriptor("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Connected)));

        ProviderActivity activity = Service(provider).ActivityFor(provider, Now);

        Assert.Null(activity.LastAttemptStartedAt);
        Assert.Null(activity.LastCompletedAt);
        Assert.Null(activity.LastSuccessAt);
        Assert.Null(activity.NextAttemptAt);
        Assert.Equal(0, activity.ConsecutiveFailures);
        Assert.False(activity.IsInFlight);
    }

    [Fact]
    public async Task ActivityForASuccessfulRefreshRecordsAttemptCompletionAndSuccess()
    {
        ProviderDescriptor provider = Descriptor("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Connected)));
        ProviderRefreshService service = Service(provider);

        await service.RefreshAllAsync(force: true, Now, CancellationToken.None);

        ProviderActivity activity = service.ActivityFor(provider, Now);
        Assert.Equal(Now, activity.LastAttemptStartedAt);
        Assert.Equal(Now, activity.LastCompletedAt);
        Assert.Equal(Now, activity.LastSuccessAt);
        Assert.Equal(0, activity.ConsecutiveFailures);
    }

    [Fact]
    public async Task ActivityForAnErrorRefreshRecordsCompletionWithoutSuccess()
    {
        ProviderDescriptor provider = Descriptor("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Error)));
        ProviderRefreshService service = Service(provider);

        await service.RefreshAllAsync(force: true, Now, CancellationToken.None);

        ProviderActivity activity = service.ActivityFor(provider, Now);
        Assert.Equal(Now, activity.LastCompletedAt);
        Assert.Null(activity.LastSuccessAt);
        Assert.Equal(1, activity.ConsecutiveFailures);
    }

    [Fact]
    public async Task ActivityForASuccessAfterTwoFailuresClearsFailuresAndMovesSuccess()
    {
        ConnectionState state = ConnectionState.Error;
        ProviderDescriptor provider = Descriptor("Alpha", _ => Task.FromResult(Snapshot("Alpha", state)));
        ProviderRefreshService service = Service(provider);

        await service.RefreshAllAsync(force: true, Now, CancellationToken.None);
        await service.RefreshAllAsync(force: true, Now.AddMinutes(1), CancellationToken.None);
        state = ConnectionState.Connected;
        DateTimeOffset successAt = Now.AddMinutes(2);
        await service.RefreshAllAsync(force: true, successAt, CancellationToken.None);

        ProviderActivity activity = service.ActivityFor(provider, successAt);
        Assert.Equal(successAt, activity.LastSuccessAt);
        Assert.Equal(0, activity.ConsecutiveFailures);
    }

    [Fact]
    public async Task AnUnforcedBackoffSkipDoesNotMoveActivityAttemptStart()
    {
        ProviderDescriptor provider = Descriptor("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Error)));
        ProviderRefreshService service = Service(provider);

        await service.RefreshAllAsync(force: false, Now, CancellationToken.None);
        await service.RefreshAllAsync(force: false, Now.AddSeconds(1), CancellationToken.None);

        ProviderActivity activity = service.ActivityFor(provider, Now.AddSeconds(1));
        Assert.Equal(Now, activity.LastAttemptStartedAt);
    }

    [Fact]
    public async Task RaisesOneEventPerProvider()
    {
        ProviderDescriptor a = Descriptor("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Connected)));
        ProviderDescriptor b = Descriptor("Beta", _ => Task.FromResult(Snapshot("Beta", ConnectionState.Connected)));
        ProviderRefreshService service = Service(a, b);

        List<string> seen = [];
        service.Refreshed += (_, e) => { lock (seen) { seen.Add(e.Provider.DisplayName); } };

        await service.RefreshAllAsync(force: true, Now, CancellationToken.None);

        Assert.Equal(["Alpha", "Beta"], seen.Order());
    }

    [Fact]
    public async Task OneProviderThrowingDoesNotStopTheOther()
    {
        ProviderDescriptor bad = Descriptor("Alpha", _ => throw new InvalidOperationException("boom"));
        ProviderDescriptor good = Descriptor("Beta", _ => Task.FromResult(Snapshot("Beta", ConnectionState.Connected)));
        ProviderRefreshService service = Service(bad, good);

        Dictionary<string, ProviderSnapshot> results = [];
        service.Refreshed += (_, e) => { lock (results) { results[e.Provider.DisplayName] = e.Snapshot; } };

        await service.RefreshAllAsync(force: true, Now, CancellationToken.None);

        Assert.Equal(ConnectionState.Error, results["Alpha"].State);
        Assert.Equal(ConnectionState.Connected, results["Beta"].State);
    }

    [Fact]
    public async Task AThrownExceptionNeverEscapesAsAFailedTask()
    {
        ProviderDescriptor bad = Descriptor("Alpha", _ => throw new InvalidOperationException("boom"));

        await Service(bad).RefreshAllAsync(force: true, Now, CancellationToken.None);
    }

    [Fact]
    public async Task ServiceAuthoredFailureKeepsTheProbeMechanismAndTier()
    {
        ProviderDescriptor provider = Descriptor(
            "Alpha",
            _ => throw new InvalidOperationException("boom"),
            mechanism: "m",
            tier: MechanismTier.Official);
        ProviderRefreshService service = Service(provider);
        ProviderSnapshot? result = null;
        service.Refreshed += (_, e) => result = e.Snapshot;

        await service.RefreshAsync(provider, Now, CancellationToken.None);

        Assert.Equal("m", result!.Mechanism);
        Assert.Equal(MechanismTier.Official, result.Tier);
    }

    [Fact]
    public async Task AHangingProbeIsCutOffAndReportedAsAnError()
    {
        ProviderDescriptor hanging = Descriptor("Alpha", async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Snapshot("Alpha", ConnectionState.Connected);
        });
        ProviderRefreshService service = Service(hanging);

        ProviderSnapshot? result = null;
        service.Refreshed += (_, e) => result = e.Snapshot;

        await service.RefreshAllAsync(force: true, Now, CancellationToken.None);

        Assert.Equal(ConnectionState.Error, result!.State);
        Assert.Contains("Timed out", result.Error);
        Assert.Equal("fake", result.Mechanism);
        Assert.Equal(MechanismTier.Official, result.Tier);
    }

    [Fact]
    public async Task AProbeThatIgnoresCancellationIsStillCutOff()
    {
        // The hanging test above is cooperative: its Task.Delay observes the token, so it would
        // pass even if the service only signalled cancellation and awaited. This probe never looks
        // at its token, which is the case a merely-cooperative timeout cannot bound at all.
        using ManualResetEventSlim release = new();
        ProviderDescriptor stubborn = Descriptor("Alpha", _ =>
            Task.Run(() =>
            {
                release.Wait(TimeSpan.FromSeconds(30));
                return Snapshot("Alpha", ConnectionState.Connected);
            }));
        ProviderRefreshService service = Service(stubborn);

        ProviderSnapshot? result = null;
        service.Refreshed += (_, e) => result = e.Snapshot;

        try
        {
            await service.RefreshAllAsync(force: true, Now, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(ConnectionState.Error, result!.State);
            Assert.Contains("Timed out", result.Error);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task ASubscriberThrowingDoesNotAbortTheCycleForOtherProviders()
    {
        // Subscribers run synchronously on the completing thread, so an exception in one would
        // otherwise escape RefreshAllAsync - which documents itself as never throwing - and cancel
        // every other provider's refresh along with it.
        ProviderDescriptor first = Descriptor("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Connected)));
        ProviderDescriptor second = Descriptor("Beta", _ => Task.FromResult(Snapshot("Beta", ConnectionState.Connected)));
        ProviderRefreshService service = Service(first, second);

        List<string> seen = [];
        service.Refreshed += (_, e) =>
        {
            seen.Add(e.Provider.DisplayName);
            throw new InvalidOperationException("A subscriber bug.");
        };

        await service.RefreshAllAsync(force: true, Now, CancellationToken.None);

        Assert.Equal(["Alpha", "Beta"], seen.Order());
    }

    [Fact]
    public async Task CallerCancellationIsNotReportedAsAProviderError()
    {
        using CancellationTokenSource cts = new();
        ProviderDescriptor slow = Descriptor("Alpha", async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Snapshot("Alpha", ConnectionState.Connected);
        });
        ProviderRefreshService service = Service(slow);

        bool raised = false;
        service.Refreshed += (_, _) => raised = true;

        Task refresh = service.RefreshAllAsync(force: true, Now, cts.Token);
        await cts.CancelAsync();
        await refresh;

        Assert.False(raised);
    }

    [Fact]
    public async Task AFailingProviderIsSkippedUntilItsBackoffExpires()
    {
        FakeProbe probe = new("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Error)));
        ProviderDescriptor descriptor = new("alpha", "Alpha", "A", probe);
        ProviderRefreshService service = Service(descriptor);

        await service.RefreshAllAsync(force: false, Now, CancellationToken.None);
        Assert.Equal(1, probe.Calls);

        await service.RefreshAllAsync(force: false, Now.AddSeconds(1), CancellationToken.None);
        Assert.Equal(1, probe.Calls);

        await service.RefreshAllAsync(force: false, Now.AddSeconds(61), CancellationToken.None);
        Assert.Equal(2, probe.Calls);
    }

    [Fact]
    public async Task AManualRefreshIgnoresBackoff()
    {
        FakeProbe probe = new("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Error)));
        ProviderDescriptor descriptor = new("alpha", "Alpha", "A", probe);
        ProviderRefreshService service = Service(descriptor);

        await service.RefreshAllAsync(force: false, Now, CancellationToken.None);
        await service.RefreshAllAsync(force: true, Now.AddSeconds(1), CancellationToken.None);

        Assert.Equal(2, probe.Calls);
    }

    [Fact]
    public async Task ASuccessfulRefreshClearsTheBackoff()
    {
        ConnectionState next = ConnectionState.Error;
        FakeProbe probe = new("Alpha", _ => Task.FromResult(Snapshot("Alpha", next)));
        ProviderDescriptor descriptor = new("alpha", "Alpha", "A", probe);
        ProviderRefreshService service = Service(descriptor);

        await service.RefreshAllAsync(force: false, Now, CancellationToken.None);
        next = ConnectionState.Connected;
        await service.RefreshAllAsync(force: true, Now.AddSeconds(1), CancellationToken.None);

        await service.RefreshAllAsync(force: false, Now.AddSeconds(2), CancellationToken.None);
        Assert.Equal(2, probe.Calls);

        await service.RefreshAllAsync(force: false, Now.AddSeconds(62), CancellationToken.None);
        Assert.Equal(3, probe.Calls);
    }

    [Fact]
    public async Task NextAttemptForReportsOnlyAnActiveFailureBackoff()
    {
        ConnectionState next = ConnectionState.Error;
        ProviderDescriptor provider = Descriptor("Alpha", _ => Task.FromResult(Snapshot("Alpha", next)));
        ProviderRefreshService service = Service(provider);

        Assert.Null(service.NextAttemptFor(provider, Now));

        await service.RefreshAllAsync(force: false, Now, CancellationToken.None);
        Assert.Equal(Now.AddMinutes(1), service.NextAttemptFor(provider, Now));
        Assert.Null(service.NextAttemptFor(provider, Now.AddMinutes(1)));

        next = ConnectionState.Connected;
        await service.RefreshAllAsync(force: true, Now.AddSeconds(1), CancellationToken.None);
        Assert.Equal(Now.AddMinutes(1).AddSeconds(1), service.NextAttemptFor(provider, Now.AddSeconds(2)));
    }

    [Fact]
    public async Task AProviderReleasedFromSharingCanStartAFreshAttempt()
    {
        var first = new TaskCompletionSource<ProviderSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempt = 0;
        FakeProbe probe = new("Alpha", _ => ++attempt switch
        {
            1 => first.Task,
            _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Connected)),
        });
        ProviderDescriptor provider = new("alpha", "Alpha", "A", probe);
        ProviderRefreshService service = Service(provider);

        Task initial = service.RefreshAsync(provider, RefreshTrigger.ManualCard, Now, CancellationToken.None);
        Task shared = service.RefreshAsync(provider, RefreshTrigger.ManualCard, Now.AddSeconds(1), CancellationToken.None);
        Assert.Equal(1, probe.Calls);

        first.SetResult(Snapshot("Alpha", ConnectionState.Error));
        await Task.WhenAll(initial, shared);

        await service.RefreshAsync(provider, RefreshTrigger.ManualCard, Now.AddSeconds(2), CancellationToken.None);
        Assert.Equal(2, probe.Calls);
    }

    [Fact]
    public async Task ANonForcedCycleSkipsAProviderWithAnAttemptInFlight()
    {
        var pending = new TaskCompletionSource<ProviderSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeProbe probe = new("Alpha", _ => pending.Task);
        ProviderDescriptor provider = new("alpha", "Alpha", "A", probe);
        ProviderRefreshService service = Service(provider);

        Task inFlight = service.RefreshAllAsync(force: true, Now, CancellationToken.None);
        await service.RefreshAllAsync(force: false, Now.AddSeconds(1), CancellationToken.None);

        Assert.Equal(1, probe.Calls);
        pending.SetResult(Snapshot("Alpha", ConnectionState.Connected));
        await inFlight;
    }

    [Fact]
    public async Task AManualRetryJoinsTheAttemptAlreadyInFlight()
    {
        var pending = new TaskCompletionSource<ProviderSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeProbe probe = new("Alpha", _ => pending.Task);
        ProviderDescriptor provider = new("alpha", "Alpha", "A", probe);
        ProviderRefreshService service = Service(provider);

        Task initial = service.RefreshAllAsync(force: true, RefreshTrigger.Startup, Now, CancellationToken.None);
        Task retry = service.RefreshAsync(provider, RefreshTrigger.ManualCard, Now.AddSeconds(1), CancellationToken.None);

        Assert.Equal(1, probe.Calls);
        Assert.False(retry.IsCompleted);
        pending.SetResult(Snapshot("Alpha", ConnectionState.Connected));
        await Task.WhenAll(initial, retry);
    }

    [Fact]
    public async Task SuppressedRequestsCountsRefreshesThatJoinedAnAttempt()
    {
        var pending = new TaskCompletionSource<ProviderSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeProbe probe = new("Alpha", _ => pending.Task);
        ProviderDescriptor provider = new("alpha", "Alpha", "A", probe);
        ProviderRefreshService service = Service(provider);

        Task first = service.RefreshAsync(provider, RefreshTrigger.ManualCard, Now, CancellationToken.None);
        Task second = service.RefreshAsync(provider, RefreshTrigger.ManualCard, Now.AddSeconds(1), CancellationToken.None);
        Task third = service.RefreshAsync(provider, RefreshTrigger.ManualCard, Now.AddSeconds(2), CancellationToken.None);

        Assert.Equal(1, probe.Calls);
        Assert.Equal(2, service.ActivityFor(provider, Now).SuppressedRequests);
        pending.SetResult(Snapshot("Alpha", ConnectionState.Connected));
        await Task.WhenAll(first, second, third);
    }

    [Fact]
    public async Task TheTriggerOfTheLastAttemptIsRecorded()
    {
        ProviderDescriptor provider = Descriptor("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Connected)));
        ProviderRefreshService service = Service(provider);

        await service.RefreshAsync(provider, RefreshTrigger.ManualCard, Now, CancellationToken.None);

        Assert.Equal(RefreshTrigger.ManualCard, service.ActivityFor(provider, Now).LastTrigger);
    }

    [Fact]
    public async Task CancellationClearsTheInFlightMarkerWithoutPublishingAFailure()
    {
        var pending = new TaskCompletionSource<ProviderSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempt = 0;
        FakeProbe probe = new("Alpha", _ => ++attempt == 1
            ? pending.Task
            : Task.FromResult(Snapshot("Alpha", ConnectionState.Connected)));
        ProviderDescriptor provider = new("alpha", "Alpha", "A", probe);
        ProviderRefreshService service = Service(provider);
        using CancellationTokenSource cts = new();

        Task cancelled = service.RefreshAllAsync(force: true, Now, cts.Token);
        await cts.CancelAsync();
        await cancelled;

        await service.RefreshAllAsync(force: false, Now.AddSeconds(1), CancellationToken.None);
        Assert.Equal(2, probe.Calls);
    }

    [Fact]
    public async Task DifferentProvidersBeginProbingBeforeEitherCompletes()
    {
        var alphaPending = new TaskCompletionSource<ProviderSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var betaPending = new TaskCompletionSource<ProviderSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeProbe alphaProbe = new("Alpha", _ => alphaPending.Task);
        FakeProbe betaProbe = new("Beta", _ => betaPending.Task);
        ProviderDescriptor alpha = new("alpha", "Alpha", "A", alphaProbe);
        ProviderDescriptor beta = new("beta", "Beta", "B", betaProbe);
        ProviderRefreshService service = Service(alpha, beta);

        Task cycle = service.RefreshAllAsync(force: true, Now, CancellationToken.None);

        Assert.Equal(1, alphaProbe.Calls);
        Assert.Equal(1, betaProbe.Calls);
        alphaPending.SetResult(Snapshot("Alpha", ConnectionState.Connected));
        betaPending.SetResult(Snapshot("Beta", ConnectionState.Connected));
        await cycle;
    }

    [Fact]
    public void NotInstalledIsAFactNotAFailureSoItIsNeverBackedOff()
    {
        Assert.Equal(TimeSpan.Zero, ProviderRefreshService.BackoffFor(0, TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public async Task ASuccessfulRefreshWaitsForTheConfiguredIntervalBeforeAnotherUnforcedCycle()
    {
        FakeProbe probe = new("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Connected)));
        ProviderDescriptor provider = new("alpha", "Alpha", "A", probe);
        ProviderRefreshService service = Service([provider]);

        await service.RefreshAllAsync(force: false, Now, CancellationToken.None);
        await service.RefreshAllAsync(force: false, Now.AddSeconds(30), CancellationToken.None);
        await service.RefreshAllAsync(force: false, Now.AddSeconds(61), CancellationToken.None);

        Assert.Equal(2, probe.Calls);
    }

    [Fact]
    public async Task AnOverridePollsOnlyItsProviderWhenTheSharedIntervalHasNotElapsed()
    {
        FakeProbe alphaProbe = new("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Connected)));
        FakeProbe betaProbe = new("Beta", _ => Task.FromResult(Snapshot("Beta", ConnectionState.Connected)));
        ProviderDescriptor alpha = new("alpha", "Alpha", "A", alphaProbe);
        ProviderDescriptor beta = new("beta", "Beta", "B", betaProbe);
        ProviderRefreshService service = ServiceWithInterval(TimeSpan.FromSeconds(300), alpha, beta);
        service.IntervalOverrides = new Dictionary<string, TimeSpan> { ["ALPHA"] = TimeSpan.FromSeconds(15) };

        await service.RefreshAllAsync(force: false, Now, CancellationToken.None);
        await service.RefreshAllAsync(force: false, Now.AddSeconds(20), CancellationToken.None);

        Assert.Equal(2, alphaProbe.Calls);
        Assert.Equal(1, betaProbe.Calls);
    }

    [Fact]
    public async Task AHiddenProviderIsSkippedByForcedRefreshAll()
    {
        FakeProbe probe = new("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Connected)));
        ProviderDescriptor provider = new("alpha", "Alpha", "A", probe);
        ProviderRefreshService service = Service([provider]);
        service.HiddenProviderKeys = ["ALPHA"];

        await service.RefreshAllAsync(force: true, Now, CancellationToken.None);

        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public async Task AHiddenProviderIsStillProbedByExplicitRefresh()
    {
        FakeProbe probe = new("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Connected)));
        ProviderDescriptor provider = new("alpha", "Alpha", "A", probe);
        ProviderRefreshService service = Service([provider]);
        service.HiddenProviderKeys = ["ALPHA"];

        await service.RefreshAsync(provider, Now, CancellationToken.None);

        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public async Task ANotInstalledProviderWaitsForTheNormalIntervalBeforeAnotherUnforcedCycle()
    {
        FakeProbe probe = new("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.NotInstalled)));
        ProviderDescriptor provider = new("alpha", "Alpha", "A", probe);
        ProviderRefreshService service = Service([provider]);

        await service.RefreshAllAsync(force: false, Now, CancellationToken.None);
        await service.RefreshAllAsync(force: false, Now.AddSeconds(30), CancellationToken.None);
        await service.RefreshAllAsync(force: false, Now.AddSeconds(61), CancellationToken.None);

        Assert.Equal(2, probe.Calls);
    }

    [Fact]
    public void IntervalAndHiddenProviderSettingsAreCaseInsensitiveAndNeverNull()
    {
        ProviderDescriptor provider = Descriptor("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Connected)));
        ProviderRefreshService service = Service([provider]);

        service.IntervalOverrides = new Dictionary<string, TimeSpan> { ["ALPHA"] = TimeSpan.FromSeconds(15) };
        service.HiddenProviderKeys = ["ALPHA"];

        Assert.Equal(TimeSpan.FromSeconds(15), service.IntervalFor(provider));
        Assert.Contains("alpha", service.HiddenProviderKeys, StringComparer.OrdinalIgnoreCase);

        service.IntervalOverrides = null!;
        service.HiddenProviderKeys = null!;

        Assert.Empty(service.IntervalOverrides);
        Assert.Empty(service.HiddenProviderKeys);
    }

    [Theory]
    [InlineData(1, 60)]
    [InlineData(2, 120)]
    [InlineData(3, 240)]
    [InlineData(4, 480)]
    [InlineData(5, 480)]
    [InlineData(9, 480)]
    public void BackoffDoublesAndThenStopsGrowing(int failures, int expectedSeconds) =>
        Assert.Equal(
            TimeSpan.FromSeconds(expectedSeconds),
            ProviderRefreshService.BackoffFor(failures, TimeSpan.FromSeconds(60)));

    [Fact]
    public async Task AThrottleWithAProviderInstantSchedulesExactlyThatInstant()
    {
        DateTimeOffset instructed = Now + TimeSpan.FromMinutes(15);
        ProviderDescriptor provider = Descriptor("Alpha", _ => Task.FromResult(
            Snapshot("Alpha", ConnectionState.Error, new ThrottleAdvice(instructed))));
        ProviderRefreshService service = Service(provider);

        await service.RefreshAsync(provider, RefreshTrigger.ManualCard, Now, CancellationToken.None);

        ProviderActivity activity = service.ActivityFor(provider, Now);
        Assert.Equal(instructed, service.NextAttemptFor(provider, Now));
        Assert.Equal(NextAttemptSource.ProviderThrottle, activity.NextAttemptSource);
        Assert.Equal(0, activity.ConsecutiveFailures);
    }

    [Fact]
    public void ConsecutiveThrottlesWithoutAnInstantUseTheTwoFourEightMinuteLadder()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), ProviderRefreshService.ThrottleBackoffFor(1));
        Assert.Equal(TimeSpan.FromMinutes(4), ProviderRefreshService.ThrottleBackoffFor(2));
        Assert.Equal(TimeSpan.FromMinutes(8), ProviderRefreshService.ThrottleBackoffFor(3));
        Assert.Equal(TimeSpan.FromMinutes(8), ProviderRefreshService.ThrottleBackoffFor(5));
    }

    [Fact]
    public async Task AForcedRefreshIsRefusedDuringAThrottleCooldown()
    {
        int calls = 0;
        ProviderDescriptor provider = Descriptor("Alpha", _ =>
        {
            calls++;
            return Task.FromResult(Snapshot("Alpha", ConnectionState.Error, new ThrottleAdvice(Now + TimeSpan.FromMinutes(5))));
        });
        ProviderRefreshService service = Service(provider);

        await service.RefreshAsync(provider, RefreshTrigger.ManualCard, Now, CancellationToken.None);
        await service.RefreshAsync(provider, RefreshTrigger.ManualCard, Now.AddMinutes(1), CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.Equal(Now + TimeSpan.FromMinutes(5), service.ThrottledUntil(provider, Now.AddMinutes(1)));
    }

    [Fact]
    public async Task AnAttemptRecordsItsOutcomeCategoryAndDuration()
    {
        ProviderDescriptor provider = Descriptor("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Connected)));
        ProviderRefreshService service = Service(provider);

        await service.RefreshAsync(provider, RefreshTrigger.ManualCard, Now, CancellationToken.None);

        ProviderActivity activity = service.ActivityFor(provider, Now);
        Assert.Equal("Success", activity.LastOutcome);
        Assert.NotNull(activity.LastDuration);
    }

    [Fact]
    public async Task AnAttemptDurationMeasuresTheProbeWork()
    {
        ProviderDescriptor provider = Descriptor("Alpha", async ct =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(30), ct);
            return Snapshot("Alpha", ConnectionState.Connected);
        });
        ProviderRefreshService service = Service(provider);

        await service.RefreshAsync(provider, RefreshTrigger.ManualCard, Now, CancellationToken.None);

        Assert.True(service.ActivityFor(provider, Now).LastDuration >= TimeSpan.FromMilliseconds(10));
    }

    [Fact]
    public async Task AThrottledAttemptRecordsTheThrottledOutcome()
    {
        ProviderDescriptor provider = Descriptor("Alpha", _ => Task.FromResult(
            Snapshot("Alpha", ConnectionState.Error, new ThrottleAdvice(Now.AddMinutes(2)))));
        ProviderRefreshService service = Service(provider);

        await service.RefreshAsync(provider, RefreshTrigger.ManualCard, Now, CancellationToken.None);

        Assert.Equal("Throttled", service.ActivityFor(provider, Now).LastOutcome);
    }

    [Fact]
    public async Task TheAttemptLogLineNeverCarriesProviderText()
    {
        const string error = "SECRET-abc";
        const string note = "SECRET-note";
        CapturingLogger logger = new();
        ProviderDescriptor provider = Descriptor("Alpha", _ => Task.FromResult(
            Snapshot("Alpha", ConnectionState.Error, error: error, notes: [note])));
        ProviderRefreshService service = new([provider], TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(60), logger);

        await service.RefreshAsync(provider, RefreshTrigger.ManualCard, Now, CancellationToken.None);

        Assert.DoesNotContain(logger.Messages, message => message.Contains(error, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains(note, StringComparison.Ordinal));
    }

    [Fact]
    public async Task NoScheduledAttemptStartsWhileTheWorkstationIsLocked()
    {
        FakeProbe probe = new("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Connected)));
        ProviderDescriptor provider = new("alpha", "Alpha", "A", probe);
        ProviderRefreshService service = Service(provider);
        service.IsWorkstationLocked = true;

        await service.RefreshAllAsync(force: false, RefreshTrigger.Scheduled, Now, CancellationToken.None);

        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public async Task AnAttemptAlreadyInFlightIsNotCancelledByALock()
    {
        var pending = new TaskCompletionSource<ProviderSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeProbe probe = new("Alpha", _ => pending.Task);
        ProviderDescriptor provider = new("alpha", "Alpha", "A", probe);
        ProviderRefreshService service = Service(provider);

        Task attempt = service.RefreshAllAsync(force: true, RefreshTrigger.Startup, Now, CancellationToken.None);
        service.IsWorkstationLocked = true;
        pending.SetResult(Snapshot("Alpha", ConnectionState.Connected));
        await attempt;

        Assert.Equal(Now, service.ActivityFor(provider, Now).LastSuccessAt);
    }

    [Fact]
    public async Task AManualRefreshStillWorksWhileLocked()
    {
        FakeProbe probe = new("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Connected)));
        ProviderDescriptor provider = new("alpha", "Alpha", "A", probe);
        ProviderRefreshService service = Service(provider);
        service.IsWorkstationLocked = true;

        await service.RefreshAllAsync(force: true, RefreshTrigger.ManualGlobal, Now, CancellationToken.None);
        await service.RefreshAsync(provider, RefreshTrigger.ManualCard, Now.AddSeconds(1), CancellationToken.None);

        Assert.Equal(2, probe.Calls);
    }

    [Fact]
    public async Task UnlockRequestsExactlyOneRefreshCycle()
    {
        FakeProbe probe = new("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Connected)));
        ProviderDescriptor provider = new("alpha", "Alpha", "A", probe);
        ProviderRefreshService service = Service(provider);
        service.IsWorkstationLocked = true;

        for (int tick = 0; tick < 10; tick++)
        {
            await service.RefreshAllAsync(force: false, RefreshTrigger.Scheduled, Now.AddSeconds(tick), CancellationToken.None);
        }

        service.IsWorkstationLocked = false;
        await service.RefreshAfterLifecycleEventAsync(RefreshTrigger.Unlock, Now.AddMinutes(1), CancellationToken.None);

        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public async Task ResumeAndUnlockWithinTheWindowProduceOneRefresh()
    {
        FakeProbe probe = new("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Connected)));
        ProviderDescriptor provider = new("alpha", "Alpha", "A", probe);
        ProviderRefreshService service = Service(provider);

        await service.RefreshAfterLifecycleEventAsync(RefreshTrigger.Resume, Now, CancellationToken.None);
        await service.RefreshAfterLifecycleEventAsync(RefreshTrigger.Unlock, Now.AddSeconds(3), CancellationToken.None);

        Assert.Equal(1, probe.Calls);
        Assert.Equal(1, service.ActivityFor(provider, Now).CoalescedLifecycleRefreshes);
    }

    [Fact]
    public async Task ResumeAndUnlockOutsideTheWindowProduceTwoRefreshes()
    {
        FakeProbe probe = new("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Connected)));
        ProviderDescriptor provider = new("alpha", "Alpha", "A", probe);
        ProviderRefreshService service = Service(provider);

        await service.RefreshAfterLifecycleEventAsync(RefreshTrigger.Resume, Now, CancellationToken.None);
        await service.RefreshAfterLifecycleEventAsync(RefreshTrigger.Unlock, Now.AddSeconds(30), CancellationToken.None);

        Assert.Equal(2, probe.Calls);
    }

    [Fact]
    public async Task AResumeWhileLockedIsDeferredAndDoesNotSwallowTheUnlock()
    {
        FakeProbe probe = new("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Connected)));
        ProviderDescriptor provider = new("alpha", "Alpha", "A", probe);
        ProviderRefreshService service = Service(provider);
        service.IsWorkstationLocked = true;

        await service.RefreshAfterLifecycleEventAsync(RefreshTrigger.Resume, Now, CancellationToken.None);
        service.IsWorkstationLocked = false;
        await service.RefreshAfterLifecycleEventAsync(RefreshTrigger.Unlock, Now.AddSeconds(2), CancellationToken.None);

        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public async Task ADeliberateManualRefreshIsNeverCoalesced()
    {
        FakeProbe probe = new("Alpha", _ => Task.FromResult(Snapshot("Alpha", ConnectionState.Connected)));
        ProviderDescriptor provider = new("alpha", "Alpha", "A", probe);
        ProviderRefreshService service = Service(provider);

        await service.RefreshAfterLifecycleEventAsync(RefreshTrigger.Resume, Now, CancellationToken.None);
        await service.RefreshAllAsync(force: true, RefreshTrigger.ManualGlobal, Now.AddSeconds(1), CancellationToken.None);

        Assert.Equal(2, probe.Calls);
    }

    [Fact]
    public async Task AnUnlockRefreshStillObeysAThrottleCooldown()
    {
        int calls = 0;
        ProviderDescriptor provider = Descriptor("Alpha", _ =>
        {
            calls++;
            return Task.FromResult(Snapshot("Alpha", ConnectionState.Error, new ThrottleAdvice(Now + TimeSpan.FromMinutes(5))));
        });
        ProviderRefreshService service = Service(provider);

        await service.RefreshAsync(provider, RefreshTrigger.ManualCard, Now, CancellationToken.None);
        DateTimeOffset? next = service.NextAttemptFor(provider, Now.AddMinutes(1));
        await service.RefreshAfterLifecycleEventAsync(RefreshTrigger.Unlock, Now.AddMinutes(1), CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.Equal(next, service.NextAttemptFor(provider, Now.AddMinutes(1)));
    }
}
