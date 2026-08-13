using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiUsageMonitor.Infrastructure.Refresh;

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

    /// <summary>
    /// Delay before a provider that has failed <paramref name="consecutiveFailures"/> times in a
    /// row is asked again: doubling, capped at 8x the base interval. PRD §24 requires repeated
    /// failures to stop aggressive retries while leaving manual refresh available.
    /// </summary>
    public static TimeSpan BackoffFor(int consecutiveFailures, TimeSpan baseInterval) =>
        consecutiveFailures <= 0
            ? TimeSpan.Zero
            : baseInterval * Math.Min(Math.Pow(2, consecutiveFailures - 1), 8);

    /// <summary>
    /// Probes every provider concurrently. Never throws: a provider that fails produces an Error
    /// snapshot, so one provider can never take down the other or the process (PRD §4.5).
    /// </summary>
    public async Task RefreshAllAsync(bool force, DateTimeOffset now, CancellationToken ct)
    {
        List<Task> running = [];

        foreach (ProviderDescriptor provider in _providers)
        {
            running.Add(StartRefreshAsync(provider, force, now, ct));
        }

        await Task.WhenAll(running).ConfigureAwait(false);
    }

    /// <summary>Probes one provider, ignoring its backoff. This is what a manual retry calls.</summary>
    public Task RefreshAsync(ProviderDescriptor provider, DateTimeOffset now, CancellationToken ct) =>
        StartRefreshAsync(provider, force: true, now, ct);

    private Task StartRefreshAsync(ProviderDescriptor provider, bool force, DateTimeOffset now, CancellationToken ct)
    {
        long sequence;

        lock (_gate)
        {
            AttemptState attempts = GetAttempts(provider);
            if (!force && (IsBackedOff(provider, now) || attempts.InFlight.Count > 0))
            {
                return Task.CompletedTask;
            }

            sequence = ++attempts.LastStarted;
            attempts.InFlight.Add(sequence);
        }

        return RefreshAttemptAsync(provider, sequence, now, ct);
    }

    private async Task RefreshAttemptAsync(
        ProviderDescriptor provider,
        long sequence,
        DateTimeOffset now,
        CancellationToken ct)
    {
        ProviderSnapshot snapshot;

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

            if (TryPublish(provider, sequence, snapshot, now))
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

    private bool IsBackedOff(ProviderDescriptor provider, DateTimeOffset now) =>
        _backoff.TryGetValue(provider, out Backoff? state) && state.NextAttempt > now;

    private AttemptState GetAttempts(ProviderDescriptor provider)
    {
        if (!_attempts.TryGetValue(provider, out AttemptState? attempts))
        {
            attempts = new AttemptState();
            _attempts[provider] = attempts;
        }

        return attempts;
    }

    private bool TryPublish(ProviderDescriptor provider, long sequence, ProviderSnapshot snapshot, DateTimeOffset now)
    {
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
            Record(provider, snapshot, now);
            return true;
        }
    }

    private void ClearInFlight(ProviderDescriptor provider, long sequence)
    {
        lock (_gate)
        {
            GetAttempts(provider).InFlight.Remove(sequence);
        }
    }

    private void Record(ProviderDescriptor provider, ProviderSnapshot snapshot, DateTimeOffset now)
    {
        // NotInstalled and Unsupported are stable facts about the machine, not failures to retry
        // more slowly - and re-checking them costs a file-existence test.
        bool failed = snapshot.State is ConnectionState.Error or ConnectionState.Unavailable;

        if (!_backoff.TryGetValue(provider, out Backoff? state))
        {
            state = new Backoff();
            _backoff[provider] = state;
        }

        state.ConsecutiveFailures = failed ? state.ConsecutiveFailures + 1 : 0;
        state.NextAttempt = now + BackoffFor(state.ConsecutiveFailures, BaseInterval);
    }

    private sealed class Backoff
    {
        public int ConsecutiveFailures { get; set; }

        public DateTimeOffset NextAttempt { get; set; }
    }

    private sealed class AttemptState
    {
        public long LastStarted { get; set; }

        public long HighestPublished { get; set; }

        public HashSet<long> InFlight { get; } = [];
    }
}
