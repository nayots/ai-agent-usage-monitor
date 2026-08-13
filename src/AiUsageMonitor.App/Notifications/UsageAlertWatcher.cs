using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Domain;

namespace AiUsageMonitor.App.Notifications;

/// <summary>
/// Remembers the last meaningful provider and quota observations, translating changes into the
/// small set of alerts the shell can show. Snapshots rebuild row view models, so window state is
/// held here rather than on a row instance.
/// </summary>
public sealed class UsageAlertWatcher
{
    private readonly Dictionary<ProviderCardViewModel, ProviderHealth> _providerHealth = [];
    private readonly Dictionary<(ProviderCardViewModel Provider, string WindowId), int> _windowRungs = [];

    /// <summary>
    /// Observes the cards' current state and returns only newly crossed alert edges, measured
    /// against <paramref name="ladder"/> - the rungs the user chose to hear about.
    /// </summary>
    public IReadOnlyList<UsageAlert> Observe(IEnumerable<ProviderCardViewModel> providers, IReadOnlyList<int> ladder)
    {
        List<UsageAlert> alerts = [];

        foreach (ProviderCardViewModel provider in providers)
        {
            if (provider.IsHiddenByFilter)
            {
                continue;
            }

            ObserveProviderHealth(provider, alerts);

            // Stale data can still establish that a provider is working, but must never move a
            // quota rung: the next Connected reading is the next trustworthy reading.
            if (provider.State != ConnectionState.Connected)
            {
                continue;
            }

            foreach (QuotaRowViewModel window in provider.Windows)
            {
                ObserveWindow(provider, window, alerts, ladder);
            }
        }

        return alerts;
    }

    private void ObserveProviderHealth(ProviderCardViewModel provider, List<UsageAlert> alerts)
    {
        ProviderHealth? current = HealthOf(provider.State);
        if (current is null)
        {
            return;
        }

        if (_providerHealth.TryGetValue(provider, out ProviderHealth previous)
            && previous != current
            && previous != ProviderHealth.Absent
            && current != ProviderHealth.Absent)
        {
            alerts.Add(current == ProviderHealth.Failing
                ? new UsageAlert(UsageAlertKind.ProviderFailed, $"{provider.DisplayName} stopped reporting usage", "Open the widget for the reason.")
                : new UsageAlert(UsageAlertKind.ProviderRecovered, $"{provider.DisplayName} is reporting usage again", "The numbers on the card are current."));
        }

        _providerHealth[provider] = current.Value;
    }

    private void ObserveWindow(
        ProviderCardViewModel provider,
        QuotaRowViewModel window,
        List<UsageAlert> alerts,
        IReadOnlyList<int> ladder)
    {
        if (window.UsedPercent is null)
        {
            return;
        }

        int current = QuotaMilestones.Crossed(window.UsedPercent, ladder);
        (ProviderCardViewModel Provider, string WindowId) key = (provider, window.Id);

        if (!_windowRungs.TryGetValue(key, out int previous))
        {
            _windowRungs[key] = current;
            return;
        }

        _windowRungs[key] = current;

        if (current > previous)
        {
            UsageAlertKind kind = current == 100 ? UsageAlertKind.LimitReached : UsageAlertKind.Milestone;
            string title = current == 100
                ? $"{provider.DisplayName} · {window.Label} limit reached"
                : $"{provider.DisplayName} · {window.Label} past {current}%";
            alerts.Add(new UsageAlert(kind, title, UsageText(window)));
        }
        else if (ladder.Count > 0)
        {
            // Recovery is worth saying only once a window had got seriously full, and 80% is where
            // that starts. A ladder that does not offer 80 has nothing to say below its own bottom
            // rung, so falling under that rung is the same event by the only definition left.
            int recoveryRung = ladder.Contains(80) ? 80 : ladder.Min();

            if (previous >= recoveryRung && current < recoveryRung)
            {
                string title = previous == 100
                    ? $"{provider.DisplayName} · {window.Label} limit reset"
                    : $"{provider.DisplayName} · {window.Label} back under {recoveryRung}%";
                alerts.Add(new UsageAlert(UsageAlertKind.Recovered, title, UsageText(window)));
            }
        }
    }

    private static ProviderHealth? HealthOf(ConnectionState state) => state switch
    {
        ConnectionState.Connected or ConnectionState.Stale => ProviderHealth.Working,
        ConnectionState.Error or ConnectionState.Unavailable => ProviderHealth.Failing,
        ConnectionState.NotInstalled or ConnectionState.Unsupported => ProviderHealth.Absent,
        ConnectionState.Discovering or ConnectionState.Waiting => null,
        _ => null
    };

    private static string UsageText(QuotaRowViewModel window) => window.CountdownText is null
        ? $"{window.UsedText} used."
        : $"{window.UsedText} used. Resets in {window.CountdownText}.";

    private enum ProviderHealth
    {
        Working,
        Failing,
        Absent
    }
}
