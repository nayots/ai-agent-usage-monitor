using System.Text.Json;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers.Cursor;

namespace AiUsageMonitor.Infrastructure.Tests;

public sealed class CursorSpendWindowsTests
{
    private static readonly DateTimeOffset CycleEnd = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly CursorBillingCycle DerivedCycle =
        new(CycleEnd.AddMonths(-1), CycleEnd, TimeSpan.FromDays(31), DurationWasDerived: true);

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void PlanUsageBecomesAMonthlySpendWindow()
    {
        IReadOnlyList<QuotaWindow> windows = CursorSpendWindows.FromPlanUsage(
            Json("""{"planUsage":{"totalSpend":2500,"limit":10000,"includedSpend":2000,"bonusSpend":0}}"""),
            DerivedCycle,
            "pro");

        QuotaWindow window = Assert.Single(windows);
        Assert.Equal("cursor:plan_spend", window.Id);
        Assert.Equal("Monthly spend", window.Label);
        Assert.Equal(25.0, window.UsedPercent!.Value, 3);
        Assert.Equal(CycleEnd, window.ResetsAt);
        Assert.Equal(TimeSpan.FromDays(31), window.WindowDuration);
        Assert.False(window.IsPartial);
        Assert.False(window.LabelIsProviderToken);
    }

    [Fact]
    public void TheDollarFiguresAndThePlanTravelInExtra()
    {
        QuotaWindow window = Assert.Single(CursorSpendWindows.FromPlanUsage(
            Json("""{"planUsage":{"totalSpend":1171,"limit":10000}}"""), DerivedCycle, "pro"));

        Assert.Equal("11.71", window.Extra["cursor.spentUsd"]);
        Assert.Equal("100.00", window.Extra["cursor.limitUsd"]);
        Assert.Equal("pro", window.Extra["cursor.membershipType"]);
        Assert.Equal("plan_usage", window.Extra["cursor.source"]);
        Assert.Equal("derived_from_cycle_end", window.Extra["duration_source"]);
    }

    [Fact]
    public void AReportedCycleLeavesNoDerivationMarker()
    {
        var reported = new CursorBillingCycle(
            CycleEnd.AddDays(-17), CycleEnd, TimeSpan.FromDays(17), DurationWasDerived: false);

        QuotaWindow window = Assert.Single(CursorSpendWindows.FromPlanUsage(
            Json("""{"planUsage":{"totalSpend":100,"limit":10000}}"""), reported, "pro"));

        Assert.False(window.Extra.ContainsKey("duration_source"));
    }

    [Fact]
    public void PooledTeamSpendBecomesASecondWindow()
    {
        IReadOnlyList<QuotaWindow> windows = CursorSpendWindows.FromPlanUsage(
            Json("""{"planUsage":{"totalSpend":2500,"limit":10000},"spendLimitUsage":{"pooledUsed":45000,"pooledLimit":90000}}"""),
            DerivedCycle,
            "team");

        Assert.Equal(2, windows.Count);
        QuotaWindow pooled = windows[1];
        Assert.Equal("cursor:pooled_spend", pooled.Id);
        Assert.Equal("Team pooled spend", pooled.Label);
        Assert.Equal(50.0, pooled.UsedPercent!.Value, 3);
        Assert.Equal("spend_limit_usage", pooled.Extra["cursor.source"]);
        Assert.Equal(1, pooled.Order);
    }

    [Theory]
    [InlineData("""{"planUsage":{"totalSpend":2500,"limit":0}}""")]
    [InlineData("""{"planUsage":{"totalSpend":2500}}""")]
    [InlineData("""{"planUsage":{"limit":10000}}""")]
    [InlineData("""{"planUsage":{}}""")]
    [InlineData("{}")]
    [InlineData("""{"billingCycleStart":"1","billingCycleEnd":"1","displayThreshold":100}""")]
    public void NoUsableFiguresProduceNoWindowAtAllRatherThanAZeroBar(string json)
    {
        Assert.Empty(CursorSpendWindows.FromPlanUsage(Json(json), DerivedCycle, "enterprise"));
    }

    [Fact]
    public void AnEventTotalBecomesTheSameShapeOfWindow()
    {
        QuotaWindow window = CursorSpendWindows.FromEventTotal(
            spentCents: 1170.61, limitCents: 10000, DerivedCycle, "enterprise");

        Assert.Equal("cursor:cycle_spend", window.Id);
        Assert.Equal("Monthly spend", window.Label);
        Assert.Equal(11.7061, window.UsedPercent!.Value, 3);
        Assert.Equal(CycleEnd, window.ResetsAt);
        Assert.Equal("usage_events", window.Extra["cursor.source"]);
        Assert.Equal("11.71", window.Extra["cursor.spentUsd"]);
        Assert.False(window.LabelIsProviderToken);
    }

    [Fact]
    public void TheEventTotalCarriesTheProvidersOwnAmountForTheRowToShow()
    {
        QuotaWindow window = CursorSpendWindows.FromEventTotal(1170.61, 10000, DerivedCycle, "enterprise");

        // A whole ceiling drops its ".00", exactly as Cursor's own dashboard writes the pair.
        Assert.Equal("$11.71 of $100", window.AmountText);
    }

    [Fact]
    public void AnUnevenCeilingKeepsItsCents()
    {
        QuotaWindow window = CursorSpendWindows.FromEventTotal(1170.61, 12550, DerivedCycle, "enterprise");

        Assert.Equal("$11.71 of $125.50", window.AmountText);
    }

    [Fact]
    public void NoCeilingMeansNoAmountRatherThanAnOpenEndedOne()
    {
        QuotaWindow window = CursorSpendWindows.FromEventTotal(1170.61, limitCents: 0, DerivedCycle, "enterprise");

        Assert.Null(window.AmountText);
    }

    [Fact]
    public void ThePlanUsagePathInventsNoCurrencyBecauseThePayloadNamesNone()
    {
        // planUsage reports bare numbers. Only the enterprise ceiling arrives in a field that says
        // "Dollars", so only that path may print a "$". This row keeps its percentage.
        QuotaWindow window = Assert.Single(CursorSpendWindows.FromPlanUsage(
            Json("""{"planUsage":{"totalSpend":2500,"limit":10000}}"""), DerivedCycle, "pro"));

        Assert.Null(window.AmountText);
        Assert.Equal(25.0, window.UsedPercent!.Value, 3);
    }

    [Fact]
    public void AnEventTotalWithNoLimitIsUnknownNeverZero()
    {
        QuotaWindow window = CursorSpendWindows.FromEventTotal(1170.61, limitCents: 0, DerivedCycle, "enterprise");

        Assert.Null(window.UsedPercent);
        Assert.Equal("11.71", window.Extra["cursor.spentUsd"]);
        Assert.False(window.Extra.ContainsKey("cursor.limitUsd"));
    }

    private static readonly CursorBillingCycle ReportedCycle = new(
        new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
        TimeSpan.FromDays(30),
        DurationWasDerived: false);

    [Fact]
    public void TheSummarysOverallBucketIsTheMonthlySpendAgainstThisUsersOwnLimit()
    {
        QuotaWindow window = Assert.Single(CursorSpendWindows.FromUsageSummary(
            Json("""{"limitType":"team","individualUsage":{"overall":{"enabled":true,"used":10948,"limit":20000,"remaining":9052}}}"""),
            ReportedCycle,
            "enterprise"));

        Assert.Equal("cursor:overall_spend", window.Id);
        Assert.Equal("Monthly spend", window.Label);
        Assert.Equal(54.74, window.UsedPercent!.Value, 2);
        Assert.Equal("$109.48 of $200", window.AmountText);
        Assert.Equal("usage_summary", window.Extra["cursor.source"]);
        Assert.Equal("team", window.Extra["cursor.limitType"]);
        Assert.Equal(ReportedCycle.End, window.ResetsAt);
        Assert.False(window.IsPartial);
    }

    [Fact]
    public void EveryIndividualBucketBecomesAWindowInAFixedOrderAndOnlyOverallPrintsACurrency()
    {
        IReadOnlyList<QuotaWindow> windows = CursorSpendWindows.FromUsageSummary(
            Json("""
                {"individualUsage":{
                  "onDemand":{"enabled":true,"used":500,"limit":1000},
                  "plan":{"enabled":true,"used":2000,"limit":2000},
                  "overall":{"enabled":true,"used":2500,"limit":3000}}}
                """),
            ReportedCycle,
            "pro");

        Assert.Equal(["cursor:overall_spend", "cursor:included_usage", "cursor:on_demand_spend"], windows.Select(w => w.Id));
        Assert.Equal(["Monthly spend", "Included usage", "On-demand spend"], windows.Select(w => w.Label));
        Assert.Equal([0, 1, 2], windows.Select(w => w.Order));
        Assert.NotNull(windows[0].AmountText);
        Assert.Null(windows[1].AmountText);
        Assert.Null(windows[2].AmountText);
    }

    [Fact]
    public void ADisabledBucketIsSkipped()
    {
        IReadOnlyList<QuotaWindow> windows = CursorSpendWindows.FromUsageSummary(
            Json("""{"individualUsage":{"overall":{"enabled":true,"used":1,"limit":100},"onDemand":{"enabled":false,"used":0,"limit":0}}}"""),
            ReportedCycle,
            "pro");

        Assert.Equal("cursor:overall_spend", Assert.Single(windows).Id);
    }

    /// <summary>A bucket the provider invents renders under its own name, never dropped.</summary>
    [Fact]
    public void AnUnknownBucketKeepsItsProviderToken()
    {
        QuotaWindow window = Assert.Single(CursorSpendWindows.FromUsageSummary(
            Json("""{"individualUsage":{"burstCredits":{"enabled":true,"used":5,"limit":50}}}"""),
            ReportedCycle,
            "pro"));

        Assert.Equal("cursor:summary_burstCredits", window.Id);
        Assert.Equal("burstCredits", window.Label);
        Assert.True(window.LabelIsProviderToken);
        Assert.Equal(10.0, window.UsedPercent!.Value, 3);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"individualUsage":{}}""")]
    [InlineData("""{"individualUsage":{"overall":{"enabled":true,"limit":100}}}""")]
    [InlineData("""{"individualUsage":null}""")]
    public void ASummaryWithNoUsableFiguresProducesNoWindow(string json)
    {
        Assert.Empty(CursorSpendWindows.FromUsageSummary(Json(json), ReportedCycle, "enterprise"));
    }

    [Fact]
    public void TheTeamsAggregateIsNeverReportedAsThisUsersSpend()
    {
        IReadOnlyList<QuotaWindow> windows = CursorSpendWindows.FromUsageSummary(
            Json("""{"teamUsage":{"onDemand":{"enabled":true,"used":795661,"limit":100000}}}"""),
            ReportedCycle,
            "enterprise");

        Assert.Empty(windows);
    }

    [Fact]
    public void TheSummaryStatesItsOwnCycle()
    {
        CursorBillingCycle cycle = CursorBillingCycle.FromSummary(
            Json("""{"billingCycleStart":"2026-09-01T00:00:00.000Z","billingCycleEnd":"2026-10-01T00:00:00.000Z"}"""))!;

        Assert.Equal(ReportedCycle, cycle);
    }

    [Theory]
    [InlineData("""{"billingCycleStart":"2026-10-01T00:00:00.000Z","billingCycleEnd":"2026-10-01T00:00:00.000Z"}""")]
    [InlineData("""{"billingCycleEnd":"2026-10-01T00:00:00.000Z"}""")]
    [InlineData("{}")]
    public void ASummaryCycleThatIsNotARealPeriodIsIgnored(string json)
    {
        Assert.Null(CursorBillingCycle.FromSummary(Json(json)));
    }

    [Fact]
    public void AnUnknownCycleLeavesTheWindowPartial()
    {
        QuotaWindow window = CursorSpendWindows.FromEventTotal(100, 10000, CursorBillingCycle.Unknown, "enterprise");

        Assert.Null(window.ResetsAt);
        Assert.Null(window.WindowDuration);
        Assert.True(window.IsPartial);
    }

    [Fact]
    public void NoIdentifyingValueEverReachesExtra()
    {
        QuotaWindow window = CursorSpendWindows.FromEventTotal(1170.61, 10000, DerivedCycle, "enterprise");

        Assert.All(
            window.Extra.Keys,
            key => Assert.True(
                key.StartsWith("cursor.", StringComparison.Ordinal) || key == "duration_source",
                $"unexpected Extra key: {key}"));
        Assert.DoesNotContain(window.Extra.Keys, key =>
            key.Contains("email", StringComparison.OrdinalIgnoreCase)
            || key.Contains("team", StringComparison.OrdinalIgnoreCase)
            || key.Contains("user", StringComparison.OrdinalIgnoreCase));
    }
}
