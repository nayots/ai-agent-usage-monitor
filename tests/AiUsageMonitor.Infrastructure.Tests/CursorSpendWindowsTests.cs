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
