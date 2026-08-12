using System.Collections.ObjectModel;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;

namespace AiUsageMonitor.App.ViewModels;

/// <summary>
/// One provider's card. Identity comes from the descriptor and everything else from the latest
/// snapshot; nothing here branches on which provider it is (PRD §21).
/// </summary>
public sealed class ProviderCardViewModel : ObservableObject
{
    private readonly ProviderDescriptor _descriptor;
    private readonly bool _colorBarsByUsage;
    private ProviderSnapshot? _snapshot;
    private FreshnessPolicy _freshness = FreshnessPolicy.Default;
    private ConnectionState _state = ConnectionState.Discovering;
    private MechanismTier _tier = MechanismTier.Unofficial;
    private string? _versionText;
    private string? _updatedText;
    private string? _staleAgeText;
    private ProviderNotice? _notice;

    public ProviderCardViewModel(ProviderDescriptor descriptor, bool colorBarsByUsage, Action<ProviderDescriptor> retry)
    {
        _descriptor = descriptor;
        _colorBarsByUsage = colorBarsByUsage;
        RetryCommand = new RelayCommand(() => retry(descriptor));
    }

    public string DisplayName => _descriptor.DisplayName;

    public string Monogram => _descriptor.Monogram;

    public RelayCommand RetryCommand { get; }

    public ObservableCollection<QuotaRowViewModel> Windows { get; } = [];

    public ConnectionState State { get => _state; private set { if (Set(ref _state, value)) { Raise(nameof(StateLabel)); Raise(nameof(IsStale)); } } }

    public string StateLabel => ConnectionStateText.Label(State);

    public bool IsStale => State == ConnectionState.Stale;

    public MechanismTier Tier { get => _tier; private set => Set(ref _tier, value); }

    public string? VersionText { get => _versionText; private set => Set(ref _versionText, value); }

    public string? UpdatedText { get => _updatedText; private set => Set(ref _updatedText, value); }

    public string? StaleAgeText { get => _staleAgeText; private set => Set(ref _staleAgeText, value); }

    public ProviderNotice? Notice { get => _notice; private set { if (Set(ref _notice, value)) { Raise(nameof(HasNotice)); } } }

    public bool HasNotice => Notice is not null;

    /// <summary>Replaces everything this card shows with the given snapshot. Never merges: a snapshot is whole.</summary>
    public void Apply(ProviderSnapshot snapshot, DateTimeOffset now, FreshnessPolicy policy)
    {
        _snapshot = snapshot;
        _freshness = policy;

        Tier = snapshot.Tier;
        VersionText = snapshot.Version is null ? null : "v" + snapshot.Version;

        Windows.Clear();
        foreach (QuotaWindow window in QuotaOrdering.InProviderOrder(snapshot.Windows))
        {
            Windows.Add(new QuotaRowViewModel(window, _colorBarsByUsage));
        }

        Tick(now);
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

        TimeSpan? age = snapshot.RetrievedAt is DateTimeOffset at ? now - at : null;
        UpdatedText = RelativeTime.FormatAge(age) is string formatted ? "Updated " + formatted : null;
        StaleAgeText = RelativeTime.FormatAge(age);
        Notice = ProviderNoticeSelector.For(snapshot, State, now);

        foreach (QuotaRowViewModel window in Windows)
        {
            window.IsStale = IsStale;
            window.Tick(now);
        }
    }
}
