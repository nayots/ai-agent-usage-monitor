using System.Collections.ObjectModel;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Refresh;

namespace AiUsageMonitor.App.ViewModels;

/// <summary>
/// One provider's card. Identity comes from the descriptor and everything else from the latest
/// snapshot; nothing here branches on which provider it is (PRD §21).
/// </summary>
public sealed class ProviderCardViewModel : ObservableObject
{
    private readonly ProviderDescriptor _descriptor;
    private bool _colorBarsByUsage;
    private bool _showPaceProjection = true;
    private bool _showWhenUnavailable = true;
    private bool _isHiddenByUser;
    private bool _isCompact;
    private ProviderSnapshot? _snapshot;
    private IReadOnlyList<QuotaWindow> _rows = [];
    private bool _rowsRetained;
    private DateTimeOffset? _lastSuccessAt;
    private FreshnessPolicy _freshness = FreshnessPolicy.Default;
    private ConnectionState _state = ConnectionState.Discovering;
    private MechanismTier _tier = MechanismTier.Unofficial;
    private string? _versionText;
    private string? _timestampText;
    private DateTimeOffset? _nextAttempt;
    private bool _isInFlight;
    private DateTimeOffset? _throttledUntil;
    private string? _nextCheckText;
    private ProviderNotice? _notice;

    public ProviderCardViewModel(ProviderDescriptor descriptor, bool colorBarsByUsage, Action<ProviderDescriptor> retry)
    {
        _descriptor = descriptor;
        _colorBarsByUsage = colorBarsByUsage;
        RetryCommand = new RelayCommand(() => retry(descriptor), () => CanRetry);
    }

    public string DisplayName => _descriptor.DisplayName;

    public string Monogram => _descriptor.Monogram;

    public RelayCommand RetryCommand { get; }

    /// <summary>
    /// Retry is offered for an ordinary failure and withheld while a request is already running or
    /// the provider is in a cooldown.
    /// </summary>
    public bool CanRetry => !_isInFlight && _throttledUntil is null;

    /// <summary>
    /// The snapshot behind everything else on this card, for the diagnostics screen alone. Nothing
    /// in the widget binds to it: it carries <see cref="ProviderSnapshot.Notes"/>, which may contain
    /// a local credentials path and must never appear on an always-visible card.
    /// </summary>
    public ProviderSnapshot? LatestSnapshot => _snapshot;

    public ObservableCollection<QuotaRowViewModel> Windows { get; } = [];

    public bool HasWindows => Windows.Count > 0;

    /// <summary>
    /// The first window this provider reported, or null when it has reported none. "First" is the
    /// provider's own answer - windows arrive in provider order and are never re-sorted - and it is
    /// the same window the tray glyph takes its digits from, so the strip and the glyph can never
    /// disagree about which limit they are showing.
    /// </summary>
    public QuotaRowViewModel? PrimaryWindow => Windows.Count > 0 ? Windows[0] : null;

    /// <summary>
    /// Setting this rebuilds the rows rather than mutating them. A <see cref="QuotaRowViewModel"/>
    /// is a pure projection of one <see cref="QuotaWindow"/>, and the rows are rebuilt on every
    /// snapshot anyway; making the row's own flag mutable would add observable state to the one
    /// class that has none. The caller ticks afterwards to refill the countdowns the rebuild cleared.
    /// </summary>
    public bool ColorBarsByUsage
    {
        get => _colorBarsByUsage;
        set
        {
            if (Set(ref _colorBarsByUsage, value) && _snapshot is not null)
            {
                RebuildWindows();
            }
        }
    }

    /// <summary>
    /// Rebuilds rows because their projection flags are immutable.
    /// </summary>
    public bool ShowPaceProjection
    {
        get => _showPaceProjection;
        set
        {
            if (Set(ref _showPaceProjection, value) && _snapshot is not null)
            {
                RebuildWindows();
            }
        }
    }

    /// <summary>PRD §15: an unavailable provider keeps its card unless the user hides it.</summary>
    public bool ShowWhenUnavailable
    {
        get => _showWhenUnavailable;
        set
        {
            if (Set(ref _showWhenUnavailable, value))
            {
                Raise(nameof(IsHiddenByFilter));
            }
        }
    }

    /// <summary>
    /// The user hid this provider outright (PRD §28). Distinct from ShowWhenUnavailable, which hides
    /// only providers that are absent from the machine; this hides one that is present and working.
    /// </summary>
    public bool IsHiddenByUser
    {
        get => _isHiddenByUser;
        set
        {
            if (Set(ref _isHiddenByUser, value))
            {
                Raise(nameof(IsHiddenByFilter));
            }
        }
    }

    /// <summary>
    /// Only a provider that is absent from the machine can be hidden by availability. An Error or
    /// Unavailable provider is installed and not working, which is exactly the card the user needs
    /// to see.
    /// </summary>
    public bool IsHiddenByFilter =>
        IsHiddenByUser || (!ShowWhenUnavailable && State is ConnectionState.NotInstalled or ConnectionState.Unsupported);

    /// <summary>
    /// Compact density (PRD §17). Set by <see cref="MainViewModel"/> from the one setting, never
    /// from the snapshot - density is a property of how the user wants to read the widget, not of
    /// anything a provider reported.
    /// </summary>
    public bool IsCompact
    {
        get => _isCompact;
        set
        {
            if (Set(ref _isCompact, value))
            {
                Raise(nameof(ShowStatusLine));
                Raise(nameof(ShowCompactSpacer));
            }
        }
    }

    /// <summary>
    /// The state chip and the timestamp beside it. Compact drops them, but only from a Connected
    /// card: silence means connected, and every other state comes straight back.
    /// <para>
    /// The design writes this condition as <c>!(dense &amp;&amp; connected &amp;&amp; !stale)</c>,
    /// because its mockup carries state and staleness as two independent props. Here they are not:
    /// <see cref="ConnectionState.Stale"/> is a value <see cref="State"/> takes, so Connected and
    /// Stale are already mutually exclusive and the third term would be dead.
    /// </para>
    /// </summary>
    public bool ShowStatusLine => !IsCompact || State != ConnectionState.Connected;

    /// <summary>
    /// Replaces the status line's height when it is gone, so the header does not sit directly on
    /// the column captions. Six pixels against the roughly twenty-five the status line occupied -
    /// compact keeps the separation and gives up the rest.
    /// </summary>
    public bool ShowCompactSpacer => !ShowStatusLine;

    public ConnectionState State { get => _state; private set { if (Set(ref _state, value)) { Raise(nameof(StateLabel)); Raise(nameof(IsStale)); Raise(nameof(IsHiddenByFilter)); Raise(nameof(ShowStatusLine)); Raise(nameof(ShowCompactSpacer)); } } }

    public string StateLabel => ConnectionStateText.Label(State);

    public bool IsStale => State == ConnectionState.Stale;

    private bool RowsAreStale => IsStale || _rowsRetained;

    public MechanismTier Tier { get => _tier; private set => Set(ref _tier, value); }

    public string? VersionText { get => _versionText; private set => Set(ref _versionText, value); }

    /// <summary>
    /// The card's one statement of time - either how old the rows are, or how long the provider has
    /// been failing. See <see cref="TimestampLine"/> for which, and why they share a line. A next
    /// check is a separate statement about the future, shown only while a failing provider is
    /// deliberately deferred because age alone otherwise reads as a bug.
    /// </summary>
    public string? TimestampText { get => _timestampText; private set { if (Set(ref _timestampText, value)) { Raise(nameof(HasTimestampText)); } } }

    public string? NextCheckText { get => _nextCheckText; private set { if (Set(ref _nextCheckText, value)) { Raise(nameof(HasNextCheckText)); } } }

    public bool HasNextCheckText => NextCheckText is not null;

    /// <summary>
    /// False when there is nothing truthful to say: nothing retrieved yet and no failure to date
    /// it from, which is every Discovering and Waiting card and any NotInstalled or Unsupported
    /// one. The view hides the whole line rather than rendering its separator against an absent
    /// value - missing data is absent, never a placeholder that reads like one.
    /// <para>
    /// This line is the only place a card states a time. The stale banner and the notice body both
    /// used to restate it from the same <c>RetrievedAt</c>, a few pixels below and under the same
    /// condition, so a stale card read "Stale . Updated 37 minutes ago" and then "Last successful
    /// update 37 minutes ago." Neither says it any more; do not reintroduce either.
    /// </para>
    /// </summary>
    public bool HasTimestampText => TimestampText is not null;

    public ProviderNotice? Notice { get => _notice; private set { if (Set(ref _notice, value)) { Raise(nameof(HasNotice)); } } }

    public bool HasNotice => Notice is not null;

    /// <summary>
    /// Replaces everything this card shows with the given snapshot. Never merges: a snapshot is
    /// whole.
    /// <para>
    /// The single exception is <see cref="_lastSuccessAt"/>, and it is not an exception to that
    /// rule so much as a different kind of fact. A snapshot is one observation and says nothing
    /// about any other; how long a provider has been broken is a property of the sequence, which
    /// only something outliving individual snapshots can hold. It is recorded here rather than
    /// stamped onto the failing snapshot because a snapshot's <c>RetrievedAt</c> means "when THIS
    /// data was retrieved", and a failure has no data - writing a borrowed timestamp there would
    /// also feed <see cref="FreshnessPolicy"/> an age for something that was never fetched.
    /// </para>
    /// </summary>
    public void Apply(ProviderSnapshot snapshot, DateTimeOffset now, FreshnessPolicy policy)
    {
        _snapshot = snapshot;
        _freshness = policy;

        if (snapshot.RetrievedAt is DateTimeOffset succeeded)
        {
            _lastSuccessAt = succeeded;
        }

        Tier = snapshot.Tier;
        VersionText = FormatVersion(snapshot.Version);

        if (snapshot.Windows.Count > 0)
        {
            _rows = snapshot.Windows;
            _rowsRetained = false;
        }
        else if (snapshot.State is ConnectionState.Error or ConnectionState.Unavailable)
        {
            _rowsRetained = _rows.Count > 0;
        }
        else
        {
            _rows = [];
            _rowsRetained = false;
        }

        RebuildWindows();

        Tick(now);
    }

    /// <summary>
    /// The scheduler's live view of this provider, pushed once per presentation tick. Retry
    /// availability is derived here rather than guessed from connection state.
    /// </summary>
    public void SetActivity(ProviderActivity activity, DateTimeOffset? throttledUntil)
    {
        _nextAttempt = activity.NextAttemptAt;
        _isInFlight = activity.IsInFlight;
        _throttledUntil = throttledUntil;
        Raise(nameof(CanRetry));
        RetryCommand.RaiseCanExecuteChanged();
    }

    private void RebuildWindows()
    {
        Windows.Clear();

        foreach (QuotaWindow window in QuotaOrdering.InProviderOrder(_rows))
        {
            Windows.Add(new QuotaRowViewModel(window, _colorBarsByUsage, _snapshot?.Mechanism, _showPaceProjection) { IsStale = RowsAreStale });
        }

        Raise(nameof(HasWindows));
        Raise(nameof(PrimaryWindow));
    }

    /// <summary>
    /// Providers do not agree on what their --version prints, and the card must not assume one
    /// provider's shape. Claude Code returns a bare "2.1.228", which wants a "v"; codex-cli returns
    /// "codex-cli 0.144.6", which already names itself and rendered as "vcodex-cli 0.144.6". Prefix
    /// only a version that starts with a digit, and otherwise pass the provider's own string
    /// through untouched rather than trying to parse a product name out of it.
    /// </summary>
    private static string? FormatVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        string trimmed = version.Trim();
        return char.IsDigit(trimmed[0]) ? "v" + trimmed : trimmed;
    }

    /// <summary>Recomputes everything derived from the local clock. Costs no provider call (PRD §14).</summary>
    public void Tick(DateTimeOffset now)
    {
        if (_snapshot is not ProviderSnapshot snapshot)
        {
            return;
        }

        // Freshness comes from the clock, not from the snapshot, so it has to be recomputed here
        // rather than once in Apply. A snapshot can cross the threshold with no new snapshot
        // arriving to trigger Apply: a provider mid-backoff is skipped entirely for up to eight
        // refresh intervals, a machine resuming from sleep has aged arbitrarily, and
        // RefreshIntervalSeconds and StaleAfterSeconds are independently user-editable - a slow
        // refresh with a tight threshold would otherwise never show Stale at all.
        //
        // ApplyFreshness reads snapshot.State rather than the current State, so recomputing every
        // tick is idempotent and cannot ratchet a card further along the state machine. State is
        // assigned first because both Notice and the rows' IsStale are derived from it.
        State = ConnectionStateRules.ApplyFreshness(
            snapshot.State,
            _freshness.Evaluate(snapshot.RetrievedAt, now));

        TimestampText = TimestampLine(snapshot, State, now);
        DateTimeOffset? showFrom = _throttledUntil ?? (_nextAttempt is DateTimeOffset nextAttempt
            && nextAttempt > now
            && State is ConnectionState.Error or ConnectionState.Unavailable
                ? nextAttempt
                : null);

        NextCheckText = showFrom is DateTimeOffset when && when > now
            ? "Next check in " + QuotaFormatting.FormatCountdown(when - now)
            : null;
        Notice = ProviderNoticeSelector.For(snapshot, State);

        foreach (QuotaRowViewModel window in Windows)
        {
            window.IsStale = RowsAreStale;
            window.Tick(now);
        }
    }

    /// <summary>
    /// The card's one statement of age or past time, and which of two facts it states depends on
    /// whether the card is showing anything. A deferred next check is separate future-facing copy:
    /// it appears only while a failure is being deliberately skipped, because age alone reads as
    /// a bug in that case.
    /// <para>
    /// A card with rows reports how old those rows are, including rows retained through an Error or
    /// Unavailable snapshot. A card whose rows are gone reports how long it has been failing
    /// instead: "Updated" would be claiming freshness for data that is not on screen, and saying
    /// nothing at all leaves the user unable to tell a provider that broke a moment ago from one
    /// that has been down all day - which is the whole question during an outage. Both are the same
    /// shape in the same place, so the eye reads one line, not two.
    /// </para>
    /// <para>
    /// Only the failure states earn the second form. NotInstalled, Unsupported and Waiting are
    /// settled facts about the machine whose notices already say everything there is to say, and a
    /// duration would imply something is still being attempted.
    /// </para>
    /// </summary>
    private string? TimestampLine(ProviderSnapshot snapshot, ConnectionState state, DateTimeOffset now)
    {
        if (snapshot.RetrievedAt is DateTimeOffset retrieved)
        {
            return "Updated " + RelativeTime.FormatAge(now - retrieved);
        }

        bool failing = state is ConnectionState.Error or ConnectionState.Unavailable;

        return failing && _lastSuccessAt is DateTimeOffset succeeded
            ? "Last succeeded " + RelativeTime.FormatAge(now - succeeded)
            : null;
    }
}
