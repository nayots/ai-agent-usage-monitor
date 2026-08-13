using AiUsageMonitor.App.Notifications;

namespace AiUsageMonitor.App.Tests;

public sealed class AlertBatchTests
{
    private static UsageAlert Alert(UsageAlertKind kind, string title) => new(kind, title, "detail");

    [Fact]
    public void EmptyAlertsStayEmpty() => Assert.Empty(AlertBatch.Coalesce([]));

    [Fact]
    public void OneMilestonePassesThroughAsTheSameInstance()
    {
        UsageAlert milestone = Alert(UsageAlertKind.Milestone, "one");

        Assert.Same(milestone, Assert.Single(AlertBatch.Coalesce([milestone])));
    }

    [Fact]
    public void ThreeNonLimitAlertsBecomeOneSilentSummary()
    {
        IReadOnlyList<UsageAlert> result = AlertBatch.Coalesce(
        [
            Alert(UsageAlertKind.Milestone, "one"),
            Alert(UsageAlertKind.Recovered, "two"),
            Alert(UsageAlertKind.ProviderRecovered, "three")
        ]);

        UsageAlert summary = Assert.Single(result);
        Assert.Equal(UsageAlertKind.Milestone, summary.Kind);
        Assert.Equal("3 quota updates", summary.Title);
        Assert.Equal("Open the widget for detail.", summary.Text);
        Assert.True(summary.IsSilent);
    }

    [Fact]
    public void LimitReachedComesFirstAndTheRemainderIsCoalesced()
    {
        UsageAlert first = Alert(UsageAlertKind.Milestone, "one");
        UsageAlert second = Alert(UsageAlertKind.ProviderFailed, "two");
        UsageAlert limit = Alert(UsageAlertKind.LimitReached, "limit");

        IReadOnlyList<UsageAlert> result = AlertBatch.Coalesce([first, second, limit]);

        Assert.Same(limit, result[0]);
        Assert.Equal("2 quota updates", result[1].Title);
        Assert.False(result[0].IsSilent);
        Assert.True(result[1].IsSilent);
    }

    [Fact]
    public void MultipleLimitReachedAlertsPassThroughIndividually()
    {
        UsageAlert first = Alert(UsageAlertKind.LimitReached, "one");
        UsageAlert second = Alert(UsageAlertKind.LimitReached, "two");

        IReadOnlyList<UsageAlert> result = AlertBatch.Coalesce([first, second]);

        Assert.Equal([first, second], result);
        Assert.All(result, alert => Assert.False(alert.IsSilent));
    }

    [Fact]
    public void OneRemainderAlongsideALimitPassesThroughUnchanged()
    {
        UsageAlert milestone = Alert(UsageAlertKind.Milestone, "one");
        UsageAlert limit = Alert(UsageAlertKind.LimitReached, "limit");

        IReadOnlyList<UsageAlert> result = AlertBatch.Coalesce([milestone, limit]);

        Assert.Same(limit, result[0]);
        Assert.Same(milestone, result[1]);
    }
}
