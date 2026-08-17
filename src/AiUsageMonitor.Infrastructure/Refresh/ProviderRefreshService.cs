using System.Diagnostics;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiUsageMonitor.Infrastructure.Refresh;

/// <summary>Why the next attempt is scheduled when it is. Diagnostics needs to tell an
/// application-authored wait apart from one the provider asked for (spec §4.7).</summary>
public enum NextAttemptSource
{
    Interval,
    FailureBackoff,
    ProviderThrottle,
    ApplicationThrottle
}

/// <summary>
/// What the service knows about one provider's polling history. PRD §20 requires the last
/// discovery time, the last successful refresh, and enough about deferral to explain a card
/// that has not moved. All of it already existed inside this service; none of it was readable.
/// </summary>
public sealed record ProviderActivity(
    DateTimeOffset? LastAttemptStartedAt,
    DateTimeOffset? LastCompletedAt,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? NextAttemptAt,
    int ConsecutiveFailures,
    bool IsInFlight,
    RefreshTrigger? LastTrigger = null,
    int SuppressedRequests = 0,
    int ConsecutiveThrottles = 0,
    NextAttemptSource NextAttemptSource = NextAttemptSource.Interval,
    int CoalescedLifecycleRefreshes = 0,
    TimeSpan? LastDuration = null,
    string? LastOutcome = null);

/// <summary>One provider's answer, raised as soon as that provider answers rather than at the end of a cycle.</summary>
public sealed record ProviderRefreshed(ProviderDescriptor Provider, ProviderSnapshot Snapshot);

/// <summary>
/// Polls every registered provider. Both providers are pull-based: Codex's rate-limit
/// notifications only fire during an active model turn, which an observer never starts, and the
/// Claude Code endpoint is a request/response call. Nothing here interprets a provider's quota
/// semantics - it decides only when to ask and what to do when asking keeps failing.
/// </summary>
public sealed class ProviderRefreshService
{
    private readonly IReadOnlyList<ProviderDescriptor> _providers;
    private readonly TimeSpan _timeout;
    private readonly ILogger _logger;
    private readonly Dictionary<ProviderDescriptor, Backoff> _backoff = [];
    private readonly Dictionary<ProviderDescriptor, AttemptState> _attempts = [];
    private readonly Lock _gate = new();
    private IReadOnlyDictionary<string, TimeSpan> _intervalOverrides = new Dictionary<string, TimeSpan>();
    private IReadOnlyCollection<string> _hiddenProviderKeys = [];
    private bool _isWorkstationLocked;
    private DateTimeOffset? _lastLifecycleRefreshAt;
    private int _coalescedLifecycleRefreshes;

    public ProviderRefreshService(
        IReadOnlyList<ProviderDescriptor> providers,
        TimeSpan timeout,
        TimeSpan baseInterval,
        ILogger<ProviderRefreshService>? logger = null)
    {
        _providers = providers;
        _timeout = timeout;
        BaseInterval = baseInterval;
        _logger = logger ?? NullLogger<ProviderRefreshService>.Instance;
    }

    /// <summary>
    /// Raised on whichever thread the probe completed on. Subscribers that touch UI state must
    /// marshal - this service knows nothing about a dispatcher.
    /// </summary>
    public event EventHandler<ProviderRefreshed>? Refreshed;

    /// <summary>
    /// The polling cadence backoff is measured against. Settable because the user can change the
    /// refresh interval while the process runs; a provider already in backoff simply measures its
    /// next attempt against the new value.
    /// </summary>
    public TimeSpan BaseInterval { get; set; }

    /// <summary>
    /// Whether Windows has explicitly reported that the workstation is locked. This is lifecycle
    /// state, never an inference from input idleness, visibility, or a timer.
    /// </summary>
    public bool IsWorkstationLocked
    {
        get
        {
            lock (_gate)
            {
                return _isWorkstationLocked;
            }
        }
        set
        {
            lock (_gate)
            {
                _isWorkstationLocked = value;
            }
        }
    }

    /// <summary>
    /// How close together two system lifecycle events must be to count as one user action.
    /// </summary>
    public static readonly TimeSpan LifecycleCoalescingWindow = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Per-provider cadence overrides, keyed by <see cref="ProviderDescriptor.Key"/>. A provider not
    /// named here polls at <see cref="BaseInterval"/>. Replaced wholesale when settings change.
    /// </summary>
    public IReadOnlyDictionary<string, TimeSpan> IntervalOverrides
    {
        get
        {
            lock (_gate)
            {
                return _intervalOverrides;
            }
        }
        set
        {
            lock (_gate)
            {
                _intervalOverrides = value ?? new Dictionary<string, TimeSpan>();
            }
        }
    }

    /// <summary>
    /// Providers the user has hidden. They are not polled at all: a hidden card shows nothing, feeds
    /// no glyph bar and raises no alert, so polling it would be work with no consumer.
    /// </summary>
    public IReadOnlyCollection<string> HiddenProviderKeys
    {
        get
        {
            lock (_gate)
            {
                return _hiddenProviderKeys;
            }
        }
        set
        {
            lock (_gate)
            {
                _hiddenProviderKeys = value ?? [];
            }
        }
    }

    public TimeSpan IntervalFor(ProviderDescriptor provider)
    {
        lock (_gate)
        {
            return IntervalForUnsafe(provider);
        }
    }

    /// <summary>
    /// When this provider is next eligible for an unforced attempt, or null when it is not being
    /// deferred. A manual retry ignores this entirely.
    /// </summary>
    public DateTimeOffset? NextAttemptFor(ProviderDescriptor provider, DateTimeOffset now)
    {
        lock (_gate)
        {
            return _backoff.TryGetValue(provider, out Backoff? state) && state.NextAttempt > now
                ? state.NextAttempt
                : null;
        }
    }

    public ProviderActivity ActivityFor(ProviderDescriptor provider, DateTimeOffset now)
    {
        lock (_gate)
        {
            _attempts.TryGetValue(provider, out AttemptState? attempts);
            _backoff.TryGetValue(provider, out Backoff? backoff);

            return new ProviderActivity(
                attempts?.LastAttemptStartedAt,
                attempts?.LastCompletedAt,
                attempts?.LastSuccessAt,
                NextAttemptFor(provider, now),
                backoff?.ConsecutiveFailures ?? 0,
                attempts?.InFlight.Count > 0,
                attempts?.LastTrigger,
                attempts?.SuppressedRequests ?? 0,
                backoff?.ConsecutiveThrottles ?? 0,
                backoff?.NextAttemptSource ?? NextAttemptSource.Interval,
                _coalescedLifecycleRefreshes,
                attempts?.LastDuration,
                attempts?.LastOutcome);
        }
    }

    /// <summary>
    /// Delay before a provider that has failed <paramref name="consecutiveFailures"/> times in a
    /// row is asked again: doubling, capped at 8x the base interval. PRD §24 requires repeated
    /// failures to stop aggressive retries while leaving manual refresh available.
    /// </summary>
    public static TimeSpan BackoffFor(int consecutiveFailures, TimeSpan baseInterval) =>
        consecutiveFailures <= 0
            ? TimeSpan.Zero
            : baseInterval * Math.Min(Math.Pow(2, consecutiveFailures - 1), 8);

    private static readonly TimeSpan[] ThrottleLadder =
    [
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(4),
        TimeSpan.FromMinutes(8)
    ];

    /// <summary>
    /// How long to wait after a provider refused the request without saying for how long. Fixed
    /// minutes rather than a multiple of the configured interval: the wait a provider needs is a fact
    /// about the provider, not about how often the user wants the widget updated.
    /// </summary>
    public static TimeSpan ThrottleBackoffFor(int consecutiveThrottles) =>
        ThrottleLadder[Math.Clamp(consecutiveThrottles, 1, ThrottleLadder.Length) - 1];

    /// <summary>
    /// Probes every provider concurrently. Never throws: a provider that fails produces an Error
    /// snapshot, so one provider can never take down the other or the process (PRD §4.5).
    /// </summary>
    public async Task RefreshAllAsync(bool force, RefreshTrigger trigger, DateTimeOffset now, CancellationToken ct)
    {
        List<Task> running = [];

        foreach (ProviderDescriptor provider in _providers)
        {
            lock (_gate)
            {
                if (IsHiddenUnsafe(provider))
                {
                    continue;
                }
            }

            running.Add(StartRefreshAsync(provider, force, trigger, now, ct));
        }

        await Task.WhenAll(running).ConfigureAwait(false);
    }

    /// <summary>Probes one provider, ignoring its backoff. This is what a manual retry calls.</summary>
    public Task RefreshAsync(ProviderDescriptor provider, RefreshTrigger trigger, DateTimeOffset now, CancellationToken ct) =>
        StartRefreshAsync(provider, force: true, trigger, now, ct);

    /// <summary>
    /// Starts one refresh for a system lifecycle event, coalescing a closely following event from
    /// the same wake-and-unlock action. A lock defers the event entirely so its subsequent unlock
    /// remains eligible to refresh.
    /// </summary>
    public Task RefreshAfterLifecycleEventAsync(RefreshTrigger trigger, DateTimeOffset now, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_isWorkstationLocked)
            {
                return Task.CompletedTask;
            }

            if (_lastLifecycleRefreshAt is DateTimeOffset last && now - last < LifecycleCoalescingWindow)
            {
                _coalescedLifecycleRefreshes++;
                return Task.CompletedTask;
            }

            _lastLifecycleRefreshAt = now;
        }

        return RefreshAllAsync(force: true, trigger, now, ct);
    }

    // Retained for existing internal callers. New application call sites name their trigger.
    public Task RefreshAllAsync(bool force, DateTimeOffset now, CancellationToken ct) =>
        RefreshAllAsync(force, RefreshTrigger.Scheduled, now, ct);

    // Retained for existing internal callers. New application call sites name their trigger.
    public Task RefreshAsync(ProviderDescriptor provider, DateTimeOffset now, CancellationToken ct) =>
        RefreshAsync(provider, RefreshTrigger.ManualCard, now, ct);

    private Task StartRefreshAsync(
        ProviderDescriptor provider,
        bool force,
        RefreshTrigger trigger,
        DateTimeOffset now,
        CancellationToken ct)
    {
        long sequence;
        TaskCompletionSource completion;

        lock (_gate)
        {
            AttemptState attempts = GetAttempts(provider);
            if (attempts.Current is Task running)
            {
                attempts.SuppressedRequests++;
                return running;
            }

            // The only gate a forced refresh may not bypass. An ordinary failure backoff still yields
            // to a manual retry - that is how someone recovers a provider by hand - but asking harder
            // is exactly the wrong response to being told to ask less (spec §4.4).
            if (IsThrottledUnsafe(provider, now))
            {
                attempts.SuppressedRequests++;
                return Task.CompletedTask;
            }

            bool startedByTheApplication =
                trigger is not (RefreshTrigger.ManualGlobal or RefreshTrigger.ManualCard);

            if (startedByTheApplication && _isWorkstationLocked)
            {
                return Task.CompletedTask;
            }

            if (!force && IsBackedOffUnsafe(provider, now))
            {
                return Task.CompletedTask;
            }

            sequence = ++attempts.LastStarted;
            attempts.LastAttemptStartedAt = now;
            attempts.LastTrigger = trigger;
            attempts.InFlight.Add(sequence);

            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            attempts.Current = completion.Task;
        }

        return RunAttemptAsync(provider, sequence, now, completion, ct);
    }

    private async Task RunAttemptAsync(
        ProviderDescriptor provider,
        long sequence,
        DateTimeOffset now,
        TaskCompletionSource completion,
        CancellationToken ct)
    {
        try
        {
            await RefreshAttemptAsync(provider, sequence, now, ct).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                GetAttempts(provider).Current = null;
            }

            completion.TrySetResult();
        }
    }

    private async Task RefreshAttemptAsync(
        ProviderDescriptor provider,
        long sequence,
        DateTimeOffset now,
        CancellationToken ct)
    {
        ProviderSnapshot snapshot;
        long durationStart = Stopwatch.GetTimestamp();

        try
        {
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(_timeout);

            try
            {
                // Raced against the token rather than simply awaited. CancelAfter only *signals*
                // cancellation; a probe that never observes its token would leave a bare await pending
                // forever, making the timeout cooperative rather than real. PRD ss24 asks for a bound
                // that holds regardless of how well-behaved the probe is, and this is the isolation
                // boundary for probes that do not exist yet.
                Task<ProviderSnapshot> probing = provider.Probe.ProbeAsync(linked.Token);
                Task settled = await Task
                    .WhenAny(probing, Task.Delay(Timeout.InfiniteTimeSpan, linked.Token))
                    .ConfigureAwait(false);

                if (!ReferenceEquals(settled, probing))
                {
                    Observe(probing, provider);

                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }

                    snapshot = Failed(provider, $"Timed out after {_timeout.TotalSeconds:0}s.");
                }
                else
                {
                    snapshot = await probing.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The application is shutting down or the user cancelled. Not a provider failure, and
                // not something to report as one - raise nothing and leave the last snapshot standing.
                return;
            }
            catch (OperationCanceledException)
            {
                snapshot = Failed(provider, $"Timed out after {_timeout.TotalSeconds:0}s.");
            }
            catch (Exception ex)
            {
                // A probe is expected to return a state rather than throw. If one throws anyway, the
                // failure stays inside its own card.
                //
                // The split here is deliberate. The local log gets the whole exception, because that is
                // what makes a failing provider diagnosable. The card gets the type name ALONE - never
                // ex.Message - because this catch is the generic backstop for any IProviderProbe,
                // including ones not written yet, and an arbitrary message is exactly the sort of
                // string that can carry something it should not. The same rule already governs the
                // generic catch in ClaudeOAuthUsageProbe.ReadAccessToken; follow it here.
                _logger.LogWarning(ex, "The probe for {Provider} threw instead of returning a state.", provider.DisplayName);
                snapshot = Failed(provider, $"The provider probe failed unexpectedly ({ex.GetType().Name}).");
            }

            if (TryPublish(provider, sequence, snapshot, now, Stopwatch.GetElapsedTime(durationStart)))
            {
                RaiseRefreshed(provider, snapshot);
            }
        }
        finally
        {
            ClearInFlight(provider, sequence);
        }
    }

    /// <summary>
    /// Subscribers run synchronously on the thread the probe finished on, so a subscriber that
    /// throws would propagate out through <see cref="RefreshAllAsync"/> - which documents itself as
    /// never throwing - and abort the whole cycle, taking every other provider's refresh with it.
    /// That is exactly the coupling this service exists to prevent, so a bad subscriber is
    /// contained the same way a bad probe is.
    /// </summary>
    private void RaiseRefreshed(ProviderDescriptor provider, ProviderSnapshot snapshot)
    {
        try
        {
            Refreshed?.Invoke(this, new ProviderRefreshed(provider, snapshot));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A subscriber threw while handling the refresh of {Provider}.", provider.DisplayName);
        }
    }

    /// <summary>
    /// Keeps an abandoned probe's eventual failure from surfacing as an unobserved task exception.
    /// The task is deliberately not awaited: it is abandoned precisely because it outran its bound.
    /// </summary>
    private void Observe(Task<ProviderSnapshot> abandoned, ProviderDescriptor provider) =>
        _ = abandoned.ContinueWith(
            faulted => _logger.LogWarning(
                faulted.Exception,
                "The probe for {Provider} failed after it had already been abandoned for exceeding its timeout.",
                provider.DisplayName),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private ProviderSnapshot Failed(ProviderDescriptor provider, string error) => new(
        ProviderName: provider.DisplayName,
        Installed: true,
        Version: null,
        ExecutablePath: null,
        State: ConnectionState.Error,
        Mechanism: provider.Probe.Mechanism,
        Tier: provider.Probe.Tier,
        UpdateModel: "pull (poll)",
        Windows: [],
        RetrievedAt: null,
        Error: error,
        Notes: []);

    private bool IsBackedOffUnsafe(ProviderDescriptor provider, DateTimeOffset now) =>
        _backoff.TryGetValue(provider, out Backoff? state) && state.NextAttempt > now;

    private bool IsThrottledUnsafe(ProviderDescriptor provider, DateTimeOffset now) =>
        _backoff.TryGetValue(provider, out Backoff? state)
        && state.ThrottleUntil is DateTimeOffset until
        && until > now;

    /// <summary>
    /// When this provider may next be contacted at all, including by a manual retry, or null when no
    /// throttle cooldown is active.
    /// </summary>
    public DateTimeOffset? ThrottledUntil(ProviderDescriptor provider, DateTimeOffset now)
    {
        lock (_gate)
        {
            return _backoff.TryGetValue(provider, out Backoff? state)
                && state.ThrottleUntil is DateTimeOffset until
                && until > now
                    ? until
                    : null;
        }
    }

    private AttemptState GetAttempts(ProviderDescriptor provider)
    {
        if (!_attempts.TryGetValue(provider, out AttemptState? attempts))
        {
            attempts = new AttemptState();
            _attempts[provider] = attempts;
        }

        return attempts;
    }

    private bool TryPublish(
        ProviderDescriptor provider,
        long sequence,
        ProviderSnapshot snapshot,
        DateTimeOffset now,
        TimeSpan attemptDuration)
    {
        RefreshTrigger? trigger;
        string outcome;
        TimeSpan? duration;
        int failures;
        int throttles;
        DateTimeOffset nextAttempt;
        NextAttemptSource nextAttemptSource;
        int suppressed;

        lock (_gate)
        {
            AttemptState attempts = GetAttempts(provider);
            if (sequence < attempts.HighestPublished)
            {
                _logger.LogDebug(
                    "Discarding superseded refresh attempt {Sequence} for {Provider}; attempt {Published} was already published.",
                    sequence,
                    provider.DisplayName,
                    attempts.HighestPublished);
                return false;
            }

            attempts.HighestPublished = sequence;
            attempts.LastCompletedAt = now;
            if (snapshot.RetrievedAt is not null)
            {
                attempts.LastSuccessAt = now;
            }

            Record(provider, snapshot, now);
            attempts.LastDuration = attempts.LastAttemptStartedAt is not null ? attemptDuration : null;
            attempts.LastOutcome = OutcomeOf(snapshot);

            Backoff state = _backoff[provider];
            trigger = attempts.LastTrigger;
            outcome = attempts.LastOutcome;
            duration = attempts.LastDuration;
            failures = state.ConsecutiveFailures;
            throttles = state.ConsecutiveThrottles;
            nextAttempt = state.NextAttempt;
            nextAttemptSource = state.NextAttemptSource;
            suppressed = attempts.SuppressedRequests;
        }

        _logger.LogInformation(
            "Provider {Provider} attempt ({Trigger}) ended {Outcome} in {DurationMs}ms; " +
            "failures={Failures} throttles={Throttles} next={NextAttempt} because={NextAttemptSource} suppressed={Suppressed}",
            provider.DisplayName,
            trigger,
            outcome,
            duration?.TotalMilliseconds ?? 0,
            failures,
            throttles,
            nextAttempt,
            nextAttemptSource,
            suppressed);

        return true;
    }

    /// <summary>
    /// A safe, closed vocabulary for how an attempt ended. It is application-authored so no
    /// provider-controlled text reaches diagnostics or the rolling log.
    /// </summary>
    private static string OutcomeOf(ProviderSnapshot snapshot) => snapshot.Throttle is not null
        ? "Throttled"
        : snapshot.State switch
        {
            ConnectionState.Connected => "Success",
            ConnectionState.Stale => "Success",
            ConnectionState.Unavailable => "Unavailable",
            ConnectionState.NotInstalled => "NotInstalled",
            ConnectionState.Unsupported => "Unsupported",
            ConnectionState.Discovering => "Discovering",
            ConnectionState.Waiting => "Waiting",
            ConnectionState.Error => "Error",
            _ => "Error"
        };

    private void ClearInFlight(ProviderDescriptor provider, long sequence)
    {
        lock (_gate)
        {
            GetAttempts(provider).InFlight.Remove(sequence);
        }
    }

    private void Record(ProviderDescriptor provider, ProviderSnapshot snapshot, DateTimeOffset now)
    {
        if (!_backoff.TryGetValue(provider, out Backoff? state))
        {
            state = new Backoff();
            _backoff[provider] = state;
        }

        TimeSpan interval = IntervalForUnsafe(provider);

        if (snapshot.Throttle is ThrottleAdvice advice)
        {
            state.ConsecutiveThrottles++;

            if (advice.NotBefore is DateTimeOffset instructed)
            {
                state.ThrottleUntil = instructed;
                state.NextAttemptSource = NextAttemptSource.ProviderThrottle;
            }
            else
            {
                state.ThrottleUntil = now + ThrottleBackoffFor(state.ConsecutiveThrottles);
                state.NextAttemptSource = NextAttemptSource.ApplicationThrottle;
            }

            DateTimeOffset healthy = now + interval;
            state.NextAttempt = state.ThrottleUntil.Value > healthy ? state.ThrottleUntil.Value : healthy;
            return;
        }

        state.ConsecutiveThrottles = 0;
        state.ThrottleUntil = null;

        // NotInstalled and Unsupported are stable facts about the machine, not failures to retry
        // more slowly - and re-checking them costs a file-existence test.
        bool failed = snapshot.State is ConnectionState.Error or ConnectionState.Unavailable;
        state.ConsecutiveFailures = failed ? state.ConsecutiveFailures + 1 : 0;
        state.NextAttemptSource = failed ? NextAttemptSource.FailureBackoff : NextAttemptSource.Interval;
        state.NextAttempt = now + (failed ? BackoffFor(state.ConsecutiveFailures, interval) : interval);
    }

    private TimeSpan IntervalForUnsafe(ProviderDescriptor provider)
    {
        foreach ((string key, TimeSpan interval) in _intervalOverrides)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(key, provider.Key))
            {
                return interval;
            }
        }

        return BaseInterval;
    }

    private bool IsHiddenUnsafe(ProviderDescriptor provider) =>
        _hiddenProviderKeys.Contains(provider.Key, StringComparer.OrdinalIgnoreCase);

    private sealed class Backoff
    {
        public int ConsecutiveFailures { get; set; }

        public int ConsecutiveThrottles { get; set; }

        public DateTimeOffset? ThrottleUntil { get; set; }

        public NextAttemptSource NextAttemptSource { get; set; }

        public DateTimeOffset NextAttempt { get; set; }
    }

    private sealed class AttemptState
    {
        public long LastStarted { get; set; }

        public DateTimeOffset? LastAttemptStartedAt { get; set; }

        public DateTimeOffset? LastCompletedAt { get; set; }

        public DateTimeOffset? LastSuccessAt { get; set; }

        public long HighestPublished { get; set; }

        public HashSet<long> InFlight { get; } = [];

        public Task? Current { get; set; }

        public int SuppressedRequests { get; set; }

        public RefreshTrigger? LastTrigger { get; set; }

        public TimeSpan? LastDuration { get; set; }

        public string? LastOutcome { get; set; }
    }
}
