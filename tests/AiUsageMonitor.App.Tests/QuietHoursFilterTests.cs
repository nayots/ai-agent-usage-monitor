using AiUsageMonitor.App.Notifications;
using AiUsageMonitor.Domain;

namespace AiUsageMonitor.App.Tests;

public class QuietHoursFilterTests
{
    private static readonly QuietHours Overnight = new(true, 1320, 420);

    private static readonly UsageAlert[] OneOfEach =
    [
        new(UsageAlertKind.Milestone, "past 80%", "80% used."),
        new(UsageAlertKind.LimitReached, "limit reached", "100% used."),
        new(UsageAlertKind.Recovered, "back under 80%", "62% used."),
        new(UsageAlertKind.ProviderFailed, "stopped reporting usage", "Open the widget."),
        new(UsageAlertKind.ProviderRecovered, "reporting usage again", "The numbers are current.")
    ];

    [Fact]
    public void InsideTheWindowOnlyAReachedLimitSurvives()
    {
        IReadOnlyList<UsageAlert> kept = QuietHoursFilter.Apply(OneOfEach, Overnight, new TimeOnly(3, 0));

        Assert.Equal(UsageAlertKind.LimitReached, Assert.Single(kept).Kind);
    }

    [Fact]
    public void OutsideTheWindowNothingIsTouched()
    {
        IReadOnlyList<UsageAlert> kept = QuietHoursFilter.Apply(OneOfEach, Overnight, new TimeOnly(12, 0));

        Assert.Equal(OneOfEach, kept);
    }

    [Fact]
    public void ADisabledScheduleNeverSuppressesAnything()
    {
        IReadOnlyList<UsageAlert> kept = QuietHoursFilter.Apply(OneOfEach, QuietHours.Off, new TimeOnly(3, 0));

        Assert.Equal(OneOfEach, kept);
    }

    /// <summary>
    /// The filter runs before <see cref="AlertBatch.Coalesce"/>. Applied the other way round, three
    /// suppressed milestones would arrive as one balloon reading "3 quota updates" - delivered,
    /// counted, and impossible to trace back to the alerts that were supposed to be held.
    /// </summary>
    [Fact]
    public void SuppressedMilestonesCannotRideAlongInsideACoalescedBalloon()
    {
        UsageAlert[] alerts =
        [
            new(UsageAlertKind.Milestone, "past 80%", "80% used."),
            new(UsageAlertKind.Milestone, "past 90%", "90% used."),
            new(UsageAlertKind.LimitReached, "limit reached", "100% used.")
        ];

        IReadOnlyList<UsageAlert> delivered = AlertBatch.Coalesce(
            QuietHoursFilter.Apply(alerts, Overnight, new TimeOnly(23, 30)));

        Assert.Equal("limit reached", Assert.Single(delivered).Title);
    }
}
