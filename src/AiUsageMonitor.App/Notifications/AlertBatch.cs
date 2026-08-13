namespace AiUsageMonitor.App.Notifications;

/// <summary>
/// Turns one tick's alerts into what should actually be shown. Pure, so the rule is testable
/// without a notification area.
/// </summary>
public static class AlertBatch
{
    public static IReadOnlyList<UsageAlert> Coalesce(IReadOnlyList<UsageAlert> alerts)
    {
        List<UsageAlert> limits = [];
        List<UsageAlert> remainder = [];

        foreach (UsageAlert alert in alerts)
        {
            (alert.Kind == UsageAlertKind.LimitReached ? limits : remainder).Add(alert);
        }

        if (remainder.Count == 1)
        {
            limits.Add(remainder[0]);
        }
        else if (remainder.Count > 1)
        {
            limits.Add(new UsageAlert(
                UsageAlertKind.Milestone,
                $"{remainder.Count} quota updates",
                "Open the widget for detail."));
        }

        return limits;
    }
}
