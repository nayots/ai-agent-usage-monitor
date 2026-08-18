namespace AiUsageMonitor.Infrastructure.Updates;

/// <summary>Where this build stands against the release feed.</summary>
public enum UpdateAvailability
{
    /// <summary>
    /// Not established. No check has succeeded yet, the last one failed, or one of the two versions
    /// could not be read. Never rendered as "up to date" - that would be a claim, not a silence.
    /// </summary>
    Unknown,

    /// <summary>This build is the latest release, or is ahead of it.</summary>
    Current,

    /// <summary>The feed names a release newer than this build.</summary>
    UpdateAvailable
}

/// <summary>
/// The published verdict. <see cref="Current"/> and <see cref="Latest"/> are parsed values, so every
/// surface renders normalized numbers rather than the tag that arrived over the network.
/// </summary>
public sealed record UpdateStatus(
    UpdateAvailability Availability,
    ReleaseVersion? Current,
    ReleaseVersion? Latest,
    DateTimeOffset? LastCheckedUtc,
    string? FailureReason);

/// <summary>
/// Decides when to ask, asks once at a time, and holds the answer (spec D4, D7, D8).
/// <para>
/// The caller owns the clock: <see cref="IsDue"/> and <see cref="CheckAsync"/> both take
/// <c>now</c>, and there is no timer inside. This is the shape <c>ProviderRefreshService</c> already
/// uses, for the same reason - every cadence rule below is then testable without waiting a day.
/// </para>
/// <para>
/// This is not a provider and must never become one. It produces a version string, not a quota
/// snapshot; none of the polling floor, throttle advice, reset alignment or per-provider interval
/// machinery applies to it.
/// </para>
/// </summary>
public sealed class UpdateCheckService
{
    /// <summary>The ordinary cadence. Once a day is as often as a release can plausibly matter.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    /// <summary>
    /// After a failure, not a full day. The common failure is transient - the machine was offline,
    /// or waking - and waiting 24 hours to retry a request that failed because a lid was shut makes
    /// the feature useless on exactly the machines it runs on.
    /// </summary>
    public static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// How long the manual button stays refused after a check. Exists so the button cannot be
    /// hammered, not because GitHub's 60-per-hour ceiling is reachable at a daily cadence.
    /// </summary>
    public static readonly TimeSpan ManualCooldown = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long after startup the first check may run (spec D7). It exists so the check never
    /// competes with the first quota read, which is the one the user is actually waiting to see.
    /// <para>
    /// It is a floor on every scheduled attempt, not only the first: a machine that has been off
    /// for days comes back with a stored <c>lastCheckedUtc</c> whose next attempt is already in the
    /// past, and without this that check would fire on the first tick - the very race the delay
    /// exists to prevent.
    /// </para>
    /// </summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);

    private readonly GitHubReleaseClient _client;
    private readonly ReleaseVersion? _current;
    private readonly Lock _gate = new();

    private Task<UpdateStatus>? _inFlight;
    private DateTimeOffset _nextAttempt;
    private DateTimeOffset? _lastAttempt;

    public UpdateCheckService(
        string currentVersion,
        GitHubReleaseClient? client = null,
        string? initialETag = null,
        DateTimeOffset? lastCheckedUtc = null,
        DateTimeOffset? startedAt = null)
    {
        _client = client ?? new GitHubReleaseClient();

        // "unknown" is what EnvironmentReport.CaptureApplicationVersion returns when it cannot read
        // the assembly, and it parses to null here - which is the whole point.
        _current = ReleaseVersion.Parse(currentVersion);

        ETag = initialETag;
        Status = new UpdateStatus(UpdateAvailability.Unknown, _current, null, lastCheckedUtc, null);

        // Never earlier than the startup delay, whichever of the two is later. A first run has no
        // stored check and waits out the delay; a machine that has been off for a week has one that
        // came due days ago and waits out the delay too.
        DateTimeOffset floor = (startedAt ?? DateTimeOffset.Now).Add(StartupDelay);
        DateTimeOffset scheduled = lastCheckedUtc?.Add(CheckInterval) ?? floor;
        _nextAttempt = scheduled > floor ? scheduled : floor;
    }

    /// <summary>
    /// Whether the background cadence runs. A manual check ignores this entirely - turning the
    /// feature off must stop unattended requests, not take the button away.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public UpdateStatus Status { get; private set; }

    /// <summary>The last ETag worth sending, so an unchanged check costs a 304.</summary>
    public string? ETag { get; private set; }

    public event EventHandler<UpdateStatus>? StatusChanged;

    public bool IsDue(DateTimeOffset now) => Enabled && now >= _nextAttempt;

    public bool CanCheckManually(DateTimeOffset now) =>
        _lastAttempt is null || now - _lastAttempt >= ManualCooldown;

    /// <summary>
    /// Runs a check, or joins the one already running. Never throws: every failure below becomes an
    /// <see cref="UpdateAvailability.Unknown"/> status with a reason.
    /// </summary>
    public Task<UpdateStatus> CheckAsync(bool manual, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            // Shared rather than skipped, so a manual check started while a scheduled one is in
            // flight gets that one's answer instead of a second request or a silent no-op.
            if (_inFlight is not null)
            {
                return _inFlight;
            }

            _lastAttempt = now;

            Task<UpdateStatus> started = RunAsync(now, cancellationToken);

            // Only park it if it is still running. A run that never suspends - every await already
            // complete by the time it is reached - finishes inline, which means its finally has
            // *already* cleared _inFlight by the time this assignment happens. Assigning
            // unconditionally would leave a completed task parked there for the life of the
            // process, and every later check, scheduled or manual, would return that stale answer
            // without ever asking again.
            _inFlight = started.IsCompleted ? null : started;
            return started;
        }
    }

    private async Task<UpdateStatus> RunAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            ReleaseLookup lookup = await _client
                .FetchLatestAsync(ETag, cancellationToken)
                .ConfigureAwait(false);

            Apply(lookup, now);
            return Status;
        }
        finally
        {
            lock (_gate)
            {
                _inFlight = null;
            }
        }
    }

    private void Apply(ReleaseLookup lookup, DateTimeOffset now)
    {
        UpdateStatus updated;

        switch (lookup.Outcome)
        {
            case ReleaseLookupOutcome.Succeeded:
                ETag = lookup.ETag;
                updated = Verdict(lookup.Version, now);
                _nextAttempt = now.Add(CheckInterval);
                break;

            case ReleaseLookupOutcome.NotModified:
                // Nothing changed, so the verdict stands and only its timestamp moves.
                updated = Status with { LastCheckedUtc = now, FailureReason = null };
                _nextAttempt = now.Add(CheckInterval);
                break;

            default:
                // The ETag is deliberately kept: a failed check is not evidence the feed changed.
                updated = new UpdateStatus(
                    UpdateAvailability.Unknown,
                    _current,
                    Status.Latest,
                    Status.LastCheckedUtc,
                    lookup.FailureReason);
                _nextAttempt = now.Add(RetryInterval);
                break;
        }

        Publish(updated);
    }

    private UpdateStatus Verdict(ReleaseVersion? latest, DateTimeOffset now)
    {
        // Both sides must be readable. One that is not means no verdict - never "up to date".
        UpdateAvailability availability = _current is null || latest is null
            ? UpdateAvailability.Unknown
            : latest.CompareTo(_current) > 0
                ? UpdateAvailability.UpdateAvailable
                : UpdateAvailability.Current;

        return new UpdateStatus(availability, _current, latest, now, null);
    }

    private void Publish(UpdateStatus updated)
    {
        // A record, so an update that produces an equal value is not a change and is not announced -
        // the same rule SettingsService.Update follows.
        if (updated == Status)
        {
            return;
        }

        Status = updated;
        StatusChanged?.Invoke(this, updated);
    }
}
