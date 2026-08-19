using System.Text.Json;
using AiUsageMonitor.Infrastructure.Providers.Cursor;

namespace AiUsageMonitor.Infrastructure.Tests;

public sealed class CursorUsageEventsTests
{
    private static JsonElement Page(int total, params string[] events) =>
        JsonDocument.Parse(
            $$"""{"totalUsageEventsCount":{{total}},"usageEventsDisplay":[{{string.Join(",", events)}}]}""")
            .RootElement.Clone();

    private static string Event(double chargedCents, string owner = "1") =>
        $$"""{"chargedCents":{{chargedCents.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"owningUser":"{{owner}}","model":"composer-2.5"}""";

    [Fact]
    public void SumsChargedCentsAcrossPages()
    {
        var accumulator = new CursorEventAccumulator();

        Assert.True(accumulator.AddPage(Page(4, Event(7.36), Event(2.64))));
        Assert.True(accumulator.AddPage(Page(4, Event(10), Event(5))));

        Assert.Equal(25.0, accumulator.SpentCents, 3);
        Assert.Equal(4, accumulator.EventCount);
        Assert.Equal(4, accumulator.TotalReported);
        Assert.True(accumulator.IsComplete);
        Assert.Null(accumulator.Refusal);
    }

    [Fact]
    public void FallsBackToTokenUsageTotalWhenAnEventCarriesNoChargedCents()
    {
        var accumulator = new CursorEventAccumulator();

        accumulator.AddPage(Page(1, """{"owningUser":"1","tokenUsage":{"totalCents":3.5}}"""));

        Assert.Equal(3.5, accumulator.SpentCents, 3);
    }

    [Fact]
    public void AnEventWithNoCostAtAllCountsAsZeroRatherThanFailing()
    {
        var accumulator = new CursorEventAccumulator();

        Assert.True(accumulator.AddPage(Page(1, """{"owningUser":"1"}""")));
        Assert.Equal(0.0, accumulator.SpentCents, 3);
        Assert.Equal(1, accumulator.EventCount);
    }

    [Fact]
    public void RefusesToTotalWhenAPageHoldsMoreThanOneOwner()
    {
        var accumulator = new CursorEventAccumulator();

        Assert.False(accumulator.AddPage(Page(2, Event(5, owner: "1"), Event(5, owner: "2"))));

        Assert.NotNull(accumulator.Refusal);
        Assert.Contains("more than one", accumulator.Refusal!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefusesWhenASecondPageIntroducesADifferentOwner()
    {
        var accumulator = new CursorEventAccumulator();

        Assert.True(accumulator.AddPage(Page(2, Event(5, owner: "1"))));
        Assert.False(accumulator.AddPage(Page(2, Event(5, owner: "2"))));

        Assert.NotNull(accumulator.Refusal);
    }

    [Fact]
    public void OnceRefusedItStaysRefused()
    {
        var accumulator = new CursorEventAccumulator();
        accumulator.AddPage(Page(2, Event(5, owner: "1"), Event(5, owner: "2")));

        Assert.False(accumulator.AddPage(Page(2, Event(5, owner: "1"))));
        Assert.NotNull(accumulator.Refusal);
    }

    [Fact]
    public void IsIncompleteWhileFewerEventsHaveArrivedThanTheProviderReports()
    {
        var accumulator = new CursorEventAccumulator();

        accumulator.AddPage(Page(80, Event(1)));

        Assert.False(accumulator.IsComplete);
    }

    [Fact]
    public void AnEmptyPageEndsTheWalkRatherThanLoopingForever()
    {
        // A genuinely empty billing cycle: the provider says there are no events and delivers
        // none. Zero spend here is a real reading and must be shown as one.
        var accumulator = new CursorEventAccumulator();

        Assert.False(accumulator.AddPage(Page(0)));
        Assert.True(accumulator.IsComplete);
        Assert.Null(accumulator.Refusal);
        Assert.Equal(0.0, accumulator.SpentCents, 3);
    }

    [Fact]
    public void EventsPromisedButNotDeliveredNeverCountAsACompletedWalk()
    {
        // The provider claims 80 events and hands back an empty page. Treating that as complete
        // would publish a confident $0.00 for someone who has actually spent money - the exact
        // fabricated reading the missing-data rule exists to prevent.
        var accumulator = new CursorEventAccumulator();

        Assert.False(accumulator.AddPage(Page(80)));
        Assert.False(accumulator.IsComplete);
    }

    [Fact]
    public void AResponseWithNoEventsArrayIsRefusedRatherThanReadAsZero()
    {
        var accumulator = new CursorEventAccumulator();

        Assert.False(accumulator.AddPage(
            JsonDocument.Parse("""{"totalUsageEventsCount":80}""").RootElement.Clone()));

        Assert.NotNull(accumulator.Refusal);
        Assert.False(accumulator.IsComplete);
    }

    [Fact]
    public void AnEmptyObjectIsRefusedToo()
    {
        var accumulator = new CursorEventAccumulator();

        Assert.False(accumulator.AddPage(JsonDocument.Parse("{}").RootElement.Clone()));

        Assert.NotNull(accumulator.Refusal);
    }

    [Fact]
    public void AnUnstatedCountFallsBackToTheShortPageConvention()
    {
        // No totalUsageEventsCount at all. A short page is the only honest end-of-data signal
        // left; a full one means there is probably more, so the walk must not stop.
        var accumulator = new CursorEventAccumulator();

        Assert.True(accumulator.AddPage(
            JsonDocument.Parse($$"""{"usageEventsDisplay":[{{Event(5)}}]}""").RootElement.Clone()));

        Assert.Null(accumulator.TotalReported);
        Assert.True(accumulator.IsComplete);
    }

    [Fact]
    public void AFullPageWithNoStatedCountIsNotTreatedAsTheEnd()
    {
        string full = string.Join(",", Enumerable.Repeat(Event(1), CursorUsageEvents.PageSize));
        var accumulator = new CursorEventAccumulator();

        accumulator.AddPage(JsonDocument.Parse($$"""{"usageEventsDisplay":[{{full}}]}""").RootElement.Clone());

        Assert.False(accumulator.IsComplete);
    }

    [Fact]
    public void TheCapIsFiftyPagesOfOneHundred()
    {
        Assert.Equal(100, CursorUsageEvents.PageSize);
        Assert.Equal(50, CursorUsageEvents.MaxPages);
    }
}
