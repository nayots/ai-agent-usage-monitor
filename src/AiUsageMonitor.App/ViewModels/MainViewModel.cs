using System.Collections.ObjectModel;
using System.Linq;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Refresh;
using AiUsageMonitor.Infrastructure.Settings;

namespace AiUsageMonitor.App.ViewModels;

/// <summary>
/// The widget's root. Owns one card per registered provider and routes each snapshot to its own
/// card as it arrives, so a slow provider never delays a fast one.
/// </summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly ProviderRefreshService _refresh;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<Action> _dispatch;
    private readonly IReadOnlyList<ProviderDescriptor> _providers;
    private FreshnessPolicy _freshness;
    private readonly Dictionary<ProviderDescriptor, ProviderCardViewModel> _cards = [];
    private readonly CancellationTokenSource _lifetime = new();
    private bool _isRefreshing;
    private bool _isCompact;

    /// <param name="dispatch">
    /// Marshals a snapshot onto the UI thread. Defaults to running inline, which is what tests
    /// want; the window passes its dispatcher. The refresh service raises its event on whichever
    /// thread the probe finished on and deliberately knows nothing about a dispatcher.
    /// </param>
    public MainViewModel(
        ProviderRefreshService refresh,
        IReadOnlyList<ProviderDescriptor> providers,
        AppSettings settings,
        Func<DateTimeOffset> clock,
        Action<Action>? dispatch = null)
    {
        _refresh = refresh;
        _providers = providers;
        _clock = clock;
        _dispatch = dispatch ?? (action => action());
        _freshness = new FreshnessPolicy(settings.StaleAfter);
        _isCompact = settings.Density == WidgetDensity.Compact;

        foreach (ProviderDescriptor provider in ProviderOrdering.Apply(_providers, settings.ProviderOrder))
        {
            ProviderCardViewModel card = new(provider, settings.ColorBarsByUsage, RetryOne)
            {
                ShowWhenUnavailable = settings.ShowUnavailableProviders,
                IsHiddenByUser = settings.IsProviderHidden(provider.Key),
                IsCompact = _isCompact
            };
            _cards[provider] = card;
            Providers.Add(card);
        }

        RefreshCommand = new RelayCommand(() => _ = RefreshAsync(force: true, RefreshTrigger.ManualGlobal), () => !IsRefreshing);
        _refresh.Refreshed += OnRefreshed;
    }

    public ObservableCollection<ProviderCardViewModel> Providers { get; } = [];

    public RelayCommand RefreshCommand { get; }

    public string FooterText
    {
        get
        {
            int visible = Providers.Count(card => !card.IsHiddenByFilter);
            return visible == 1 ? "1 provider" : $"{visible} providers";
        }
    }

    /// <summary>
    /// Compact density (PRD §17), for the window chrome. The cards carry their own copy rather than
    /// binding up to this one, because a card is also rendered outside this window - in tests and in
    /// the render harness - and a binding that resolves to nothing there would silently read as
    /// standard.
    /// </summary>
    public bool IsCompact { get => _isCompact; private set => Set(ref _isCompact, value); }

    /// <summary>
    /// Re-applies everything a settings change can reach. Costs no provider call: freshness, bar
    /// colour and the visibility filter are all derived from data already held.
    /// </summary>
    public void ApplySettings(AppSettings settings)
    {
        _freshness = new FreshnessPolicy(settings.StaleAfter);
        IsCompact = settings.Density == WidgetDensity.Compact;

        foreach (ProviderCardViewModel card in Providers)
        {
            card.ColorBarsByUsage = settings.ColorBarsByUsage;
            card.ShowWhenUnavailable = settings.ShowUnavailableProviders;
            card.IsCompact = IsCompact;
        }

        foreach (ProviderDescriptor provider in _cards.Keys)
        {
            _cards[provider].IsHiddenByUser = settings.IsProviderHidden(provider.Key);
        }

        IReadOnlyList<ProviderDescriptor> orderedProviders = ProviderOrdering.Apply(_providers, settings.ProviderOrder);
        for (int targetIndex = 0; targetIndex < orderedProviders.Count; targetIndex++)
        {
            ProviderCardViewModel card = _cards[orderedProviders[targetIndex]];
            int currentIndex = Providers.IndexOf(card);
            if (currentIndex != targetIndex)
            {
                Providers.Move(currentIndex, targetIndex);
            }
        }

        Tick();
        Raise(nameof(FooterText));
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (Set(ref _isRefreshing, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public async Task RefreshAsync(bool force, RefreshTrigger trigger)
    {
        if (_lifetime.IsCancellationRequested)
        {
            return;
        }

        IsRefreshing = true;

        try
        {
            await _refresh.RefreshAllAsync(force, trigger, _clock(), _lifetime.Token).ConfigureAwait(true);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    // Retained for existing internal callers. New application call sites name their trigger.
    public Task RefreshAsync(bool force) => RefreshAsync(force, RefreshTrigger.ManualGlobal);

    public void SetWorkstationLocked(bool locked) => _refresh.IsWorkstationLocked = locked;

    public Task RefreshAfterLifecycleEventAsync(RefreshTrigger trigger) =>
        _lifetime.IsCancellationRequested
            ? Task.CompletedTask
            : _refresh.RefreshAfterLifecycleEventAsync(trigger, _clock(), _lifetime.Token);

    /// <summary>Advances every locally derived value: countdowns, ages, elapsed markers.</summary>
    public void Tick()
    {
        DateTimeOffset now = _clock();

        foreach ((ProviderDescriptor provider, ProviderCardViewModel card) in _cards)
        {
            card.SetActivity(_refresh.ActivityFor(provider, now), _refresh.ThrottledUntil(provider, now));
            card.Tick(now);
        }
    }

    public void Dispose()
    {
        _refresh.Refreshed -= OnRefreshed;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private void RetryOne(ProviderDescriptor provider)
    {
        if (!_lifetime.IsCancellationRequested)
        {
            _ = _refresh.RefreshAsync(provider, RefreshTrigger.ManualCard, _clock(), _lifetime.Token);
        }
    }

    private void OnRefreshed(object? sender, ProviderRefreshed e)
    {
        if (_lifetime.IsCancellationRequested || !_cards.TryGetValue(e.Provider, out ProviderCardViewModel? card))
        {
            return;
        }

        _dispatch(() =>
        {
            card.Apply(e.Snapshot, _clock(), _freshness);
            Raise(nameof(FooterText));
        });
    }
}
