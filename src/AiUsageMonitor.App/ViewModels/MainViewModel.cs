using System.Collections.ObjectModel;
using System.Linq;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Diagnostics;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Refresh;
using AiUsageMonitor.Infrastructure.Settings;
using AiUsageMonitor.Infrastructure.Updates;

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
    private string? _updateFooterText;

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
        Action<Action>? dispatch = null,
        string? applicationVersion = null)
    {
        VersionText = FormatVersion(applicationVersion ?? EnvironmentReport.CaptureApplicationVersion());
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
                ShowPaceProjection = settings.ShowPaceProjection,
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
    /// False when the filter has emptied the widget. Not an error: it is the ordinary shape of a
    /// widget whose providers the user has all hidden, which is why the body answers it with an
    /// explanation rather than with the blank strip between title bar and footer that it would
    /// otherwise be.
    /// </summary>
    public bool HasVisibleProviders => Providers.Any(card => !card.IsHiddenByFilter);

    /// <summary>
    /// What emptied the widget, in the user's terms. The two causes need different remedies - one
    /// is undone in settings, the other by installing a CLI - so they are never collapsed into one
    /// sentence, and a widget emptied by both says so rather than naming whichever cause the code
    /// happened to test first.
    /// </summary>
    public string EmptyStateText => EmptyState().Text;

    /// <summary>Where to undo it. Null when there is nothing honest to point the user at.</summary>
    public string? EmptyStateHint => EmptyState().Hint;

    public bool HasEmptyStateHint => EmptyStateHint is not null;

    private (string Text, string? Hint) EmptyState()
    {
        if (Providers.Count == 0)
        {
            return ("No providers to show.", null);
        }

        bool hiddenByUser = Providers.Any(card => card.IsHiddenByUser);
        bool hiddenAsUnavailable = Providers.Any(card => !card.IsHiddenByUser && card.IsHiddenByFilter);

        return (hiddenByUser, hiddenAsUnavailable) switch
        {
            (true, false) => ("All providers are hidden.", "Show one again in settings, under Providers."),
            (false, true) => ("No providers are available on this machine.", "Providers that are not installed are hidden in settings."),
            _ => ("No providers to show.", "They are hidden, or not available on this machine.")
        };
    }

    /// <summary>
    /// The running build, stated in the footer because it is the one thing a bug report needs and
    /// the one thing nobody can look up from the outside - the executable is not code-signed and
    /// carries no visible identity beyond its file name.
    /// <para>
    /// Deliberately separate from <see cref="FooterText"/> rather than appended to it: "how many
    /// providers" and "what build is this" are two facts, and a single string would make either
    /// impossible to assert or re-word without disturbing the other.
    /// </para>
    /// </summary>
    public string? VersionText { get; }

    /// <summary>
    /// False when the version could not be read. The footer then omits the version and its separator
    /// entirely rather than rendering "unknown", which reads like a diagnosis of the providers
    /// sitting above it rather than a fact about the application.
    /// </summary>
    public bool HasVersionText => VersionText is not null;

    /// <summary>
    /// The footer's version rendered as a link target when a newer release exists, and null
    /// otherwise. A separate property rather than a mutation of <see cref="VersionText"/>: the
    /// footer shows one or the other, and a single string would make either impossible to assert
    /// without disturbing the other - the same reason the version is separate from the count.
    /// </summary>
    public string? UpdateFooterText => _updateFooterText;

    public bool HasUpdate => _updateFooterText is not null;

    /// <summary>
    /// Takes a verdict from <c>UpdateCheckService</c>. Pushed in rather than subscribed to, so the
    /// view model needs no reference to the service and stays constructible in a test with nothing
    /// behind it.
    /// </summary>
    public void ApplyUpdateStatus(UpdateStatus status)
    {
        string? text = UpdateCopy.FooterText(status);

        if (_updateFooterText == text)
        {
            return;
        }

        _updateFooterText = text;
        Raise(nameof(UpdateFooterText));
        Raise(nameof(HasUpdate));
    }

    /// <summary>
    /// Prefixes a "v" only when the version starts with a digit - the same rule
    /// <see cref="ProviderCardViewModel"/> applies to a provider's version, and for the same reason:
    /// "unknown" must never render as "vunknown".
    /// </summary>
    private static string? FormatVersion(string version)
    {
        string trimmed = version.Trim();

        return trimmed.Length == 0 || !char.IsDigit(trimmed[0]) ? null : "v" + trimmed;
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
            card.ShowPaceProjection = settings.ShowPaceProjection;
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
        RaiseFilterDerived();
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

    /// <summary>
    /// Retained for callers that have no trigger to name. The trigger is derived from
    /// <paramref name="force"/> rather than fixed, because the two differ in a way that matters:
    /// the manual triggers are exempt from the workstation-lock pause, so labelling an UNforced
    /// refresh as a manual one would let a scheduled poll run while the machine is locked - exactly
    /// the traffic this increment exists to stop. An unforced refresh is a scheduled one.
    /// </summary>
    public Task RefreshAsync(bool force) =>
        RefreshAsync(force, force ? RefreshTrigger.ManualGlobal : RefreshTrigger.Scheduled);

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

    /// <summary>
    /// Everything derived from which cards the filter admits. One call rather than four scattered
    /// ones: the footer count and the empty state are two readings of the same fact, and a site
    /// that raised only the count would leave the body claiming the widget is empty while cards
    /// sit in it.
    /// </summary>
    private void RaiseFilterDerived()
    {
        Raise(nameof(FooterText));
        Raise(nameof(HasVisibleProviders));
        Raise(nameof(EmptyStateText));
        Raise(nameof(EmptyStateHint));
        Raise(nameof(HasEmptyStateHint));
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
            RaiseFilterDerived();
        });
    }
}
