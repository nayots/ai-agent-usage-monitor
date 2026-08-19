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
        var accumulator = new CursorEventAccumulator();

        Assert.False(accumulator.AddPage(Page(80)));
        Assert.True(accumulator.IsComplete);
        Assert.Null(accumulator.Refusal);
    }

    [Fact]
    public void TheCapIsFiftyPagesOfOneHundred()
    {
        Assert.Equal(100, CursorUsageEvents.PageSize);
        Assert.Equal(50, CursorUsageEvents.MaxPages);
    }
}
