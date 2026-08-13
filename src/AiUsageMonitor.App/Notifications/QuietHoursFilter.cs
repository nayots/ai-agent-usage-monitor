using AiUsageMonitor.Domain;

namespace AiUsageMonitor.App.Notifications;

/// <summary>
/// Which of the observed alerts may be delivered right now. Pure, like <see cref="AlertBatch"/>,
/// so the rule is testable without waiting until 3am.
/// <para>
/// Applied after observation and before coalescing. After observation because the watcher's rungs
/// must advance whether or not anyone is told - see <c>WidgetWindow.DeliverAlerts</c>. Before
/// coalescing because a suppressed milestone merged into a delivered balloon would be delivered
/// anyway, counted in a "3 quota updates" that the user was supposed to sleep through.
/// </para>
/// </summary>
public static class QuietHoursFilter
{
    public static IReadOnlyList<UsageAlert> Apply(
        IReadOnlyList<UsageAlert> alerts,
        QuietHours quietHours,
        TimeOnly localTime)
    {
        if (!quietHours.Contains(localTime))
        {
            return alerts;
        }

        // Reaching a limit survives quiet hours. It is the one alert that changes what the user can
        // do next, and the one they would be angriest to have been spared.
        return [.. alerts.Where(alert => alert.Kind == UsageAlertKind.LimitReached)];
    }
}
