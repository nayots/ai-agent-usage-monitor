using System.Text.Json;
using AiUsageMonitor.Infrastructure.Providers.Cursor;

namespace AiUsageMonitor.Infrastructure.Tests;

public sealed class CursorBillingCycleTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void ParsesAUnixMillisecondStringBecauseThatIsWhatTheApiActuallyReturns()
    {
        // Measured on a live enterprise seat: "1788220800000" -> 2026-09-01T00:00:00Z.
        JsonElement element = Json("""{"v":"1788220800000"}""").GetProperty("v");

        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), CursorInstant.Parse(element));
    }

    [Fact]
    public void ParsesAUnixMillisecondNumber()
    {
        Assert.Equal(
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            CursorInstant.Parse(Json("""{"v":1788220800000}""").GetProperty("v")));
    }

    [Fact]
    public void ParsesRfc3339BecauseThatIsWhatTheProvidersDocumentationDescribes()
    {
        // openusage.sh documents these fields as RFC3339 while the observed seat returned unix
        // milliseconds. The two sources disagree, so both are accepted rather than betting.
        Assert.Equal(
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            CursorInstant.Parse(Json("""{"v":"2026-09-01T00:00:00Z"}""").GetProperty("v")));
    }

    [Theory]
    [InlineData("""{"v":null}""")]
    [InlineData("""{"v":""}""")]
    [InlineData("""{"v":"nonsense"}""")]
    [InlineData("""{"v":0}""")]
    [InlineData("""{"v":true}""")]
    public void RefusesAnythingItCannotReadRatherThanGuessing(string json)
    {
        Assert.Null(CursorInstant.Parse(Json(json).GetProperty("v")));
    }

    [Fact]
    public void PrefersPlanInfoForTheResetInstant()
    {
        CursorBillingCycle cycle = CursorBillingCycle.Read(
            currentPeriodUsage: Json("""{"billingCycleEnd":"1785542400000"}"""),
            planInfo: Json("""{"planInfo":{"billingCycleEnd":"1788220800000"}}"""));

        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), cycle.End);
    }

    [Fact]
    public void FallsBackToCurrentPeriodUsageWhenPlanInfoCarriesNoEnd()
    {
        CursorBillingCycle cycle = CursorBillingCycle.Read(
            currentPeriodUsage: Json("""{"billingCycleStart":"1785542400000","billingCycleEnd":"1788220800000"}"""),
            planInfo: Json("""{"planInfo":{"planName":"Pro"}}"""));

        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), cycle.End);
    }

    [Fact]
    public void ARealStartWinsOutrightAndNothingIsMarkedAsDerived()
    {
        // 2026-08-16 -> 2026-09-01: a genuine, reported cycle. Nothing to derive.
        CursorBillingCycle cycle = CursorBillingCycle.Read(
            currentPeriodUsage: Json("""{"billingCycleStart":"1786838400000","billingCycleEnd":"1788220800000"}"""),
            planInfo: null);

        Assert.Equal(new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero), cycle.Start);
        Assert.Equal(TimeSpan.FromDays(16), cycle.Duration);
        Assert.False(cycle.DurationWasDerived);
    }

    [Fact]
    public void ADegenerateStartIsRejectedAndTheDurationIsDerivedFromTheMonthBoundary()
    {
        // The measured enterprise seat returned start == end, both 2026-08-19T15:32Z.
        //
        // THIS IS THE TEST THAT PINS THE RULE. Note that the degenerate start (19 Aug) IS
        // strictly earlier than the end actually used (1 Sep, from planInfo). So a rule that
        // only compared the start against the end being USED would accept this placeholder and
        // silently report a 13-day "month". The start must be judged against the end it was
        // reported BESIDE, which it equals - and therefore fails.
        CursorBillingCycle cycle = CursorBillingCycle.Read(
            currentPeriodUsage: Json("""{"billingCycleStart":"1787153574780","billingCycleEnd":"1787153574780"}"""),
            planInfo: Json("""{"planInfo":{"billingCycleEnd":"1788220800000"}}"""));

        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), cycle.Start);
        Assert.Equal(TimeSpan.FromDays(31), cycle.Duration);
        Assert.True(cycle.DurationWasDerived);
    }

    [Fact]
    public void AStartLaterThanTheEndBeingUsedIsAlsoRejected()
    {
        // 2026-09-15 start against a 2026-09-01 end: backwards, so not a cycle either.
        CursorBillingCycle cycle = CursorBillingCycle.Read(
            currentPeriodUsage: Json("""{"billingCycleStart":"1789435800000"}"""),
            planInfo: Json("""{"planInfo":{"billingCycleEnd":"1788220800000"}}"""));

        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), cycle.Start);
        Assert.True(cycle.DurationWasDerived);
    }

    [Fact]
    public void AnEndThatIsNotAMonthBoundaryYieldsNoDurationAtAll()
    {
        // 2026-09-15T01:30:00Z. Deriving "a month" from this would be a guess, so the window
        // simply goes without an elapsed marker.
        CursorBillingCycle cycle = CursorBillingCycle.Read(
            currentPeriodUsage: null,
            planInfo: Json("""{"planInfo":{"billingCycleEnd":"1789435800000"}}"""));

        Assert.NotNull(cycle.End);
        Assert.Null(cycle.Start);
        Assert.Null(cycle.Duration);
        Assert.False(cycle.DurationWasDerived);
    }

    [Fact]
    public void NothingReadableYieldsAnUnknownCycleRatherThanAnException()
    {
        CursorBillingCycle cycle = CursorBillingCycle.Read(Json("{}"), Json("{}"));

        Assert.Null(cycle.End);
        Assert.Null(cycle.Duration);
    }
}
