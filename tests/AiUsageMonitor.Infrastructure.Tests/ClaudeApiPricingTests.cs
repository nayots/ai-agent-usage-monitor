using AiUsageMonitor.Infrastructure.Providers.Claude;

namespace AiUsageMonitor.Infrastructure.Tests;

public sealed class ClaudeApiPricingTests
{
    [Theory]
    [InlineData("claude-fable-5-1", 10)]
    [InlineData("claude-mythos-5-1", 10)]
    [InlineData("claude-fable-5", 10)]
    [InlineData("claude-mythos-5", 10)]
    [InlineData("claude-opus-5-5", 4)]
    [InlineData("claude-opus-5", 5)]
    [InlineData("claude-opus-4-8", 5)]
    [InlineData("claude-opus-4-7", 5)]
    [InlineData("claude-opus-4-6", 5)]
    [InlineData("claude-opus-4-5", 5)]
    [InlineData("claude-opus-4-1", 15)]
    [InlineData("claude-opus-4", 15)]
    [InlineData("claude-sonnet-5", 2)]
    [InlineData("claude-sonnet-4-6", 3)]
    [InlineData("claude-sonnet-4-5", 3)]
    [InlineData("claude-sonnet-4", 3)]
    [InlineData("claude-haiku-4-5", 1)]
    [InlineData("claude-3-5-haiku", 0.8)]
    public void EveryPublishedModelRowPricesOneMillionInputTokens(string model, decimal expected)
    {
        Assert.Equal(expected, ClaudeApiPricing.CostUsd(Entry(model, input: 1_000_000)));
    }

    [Theory]
    [InlineData("claude-opus-5", 50_000, 0, 0, 0, 15_000, 0.625)]
    [InlineData("claude-opus-5", 10_000, 40_000, 0, 0, 15_000, 0.445)]
    [InlineData("claude-opus-5-5", 0, 1_000_000, 0, 0, 0, 0.20)]
    [InlineData("claude-fable-5-1", 0, 1_000_000, 0, 0, 0, 0.25)]
    [InlineData("claude-sonnet-4-6", 0, 1_000_000, 0, 0, 0, 0.30)]
    [InlineData("claude-sonnet-4-6", 0, 0, 1_000_000, 0, 0, 3.75)]
    [InlineData("claude-sonnet-4-6", 0, 0, 0, 1_000_000, 0, 6)]
    public void PricesInputOutputAndCacheCategories(string model, long input, long cacheRead, long write5m, long write1h, long output, decimal expected)
    {
        Assert.Equal(expected, ClaudeApiPricing.CostUsd(Entry(model, input, output, cacheRead, write5m, write1h)));
    }

    [Theory]
    [InlineData("claude-opus-5-5", 48)]
    [InlineData("claude-opus-5", 60)]
    [InlineData("claude-opus-4-6", 30)]
    public void FastModeUsesOnlyPublishedFastRates(string model, decimal expected)
    {
        Assert.Equal(expected, ClaudeApiPricing.CostUsd(Entry(model, input: 1_000_000, output: 1_000_000, speed: "fast")));
    }

    [Theory]
    [InlineData("claude-sonnet-4-6", "us", 3.3)]
    [InlineData("claude-opus-4-5", "us", 5)]
    [InlineData("claude-sonnet-4-6", "not_available", 3)]
    public void GeographicSurchargeAppliesOnlyToEligibleModels(string model, string geo, decimal expected)
    {
        Assert.Equal(expected, ClaudeApiPricing.CostUsd(Entry(model, input: 1_000_000, inferenceGeo: geo)));
    }

    [Fact]
    public void WebSearchIsPricedOutsideTheTokenGeoMultiplier()
    {
        Assert.Equal(0.03m, ClaudeApiPricing.CostUsd(Entry("claude-haiku-4-5", webSearches: 3)));
    }

    [Theory]
    [InlineData("claude-opus-5-5", true, 4)]
    [InlineData("claude-opus-4-8", true, 5)]
    [InlineData("claude-sonnet-4-5-20250929", true, 3)]
    [InlineData("claude-opus-5-5[1m]", true, 4)]
    [InlineData("claude-opus-5-5x", false, 0)]
    [InlineData("claude-sonnet-4-5-2025", false, 0)]
    [InlineData("gpt-5", false, 0)]
    [InlineData("<synthetic>", false, 0)]
    public void ModelPrefixesOnlyMatchAtApprovedBoundaries(string model, bool priced, decimal expected)
    {
        Assert.Equal(priced, ClaudeApiPricing.IsPriced(model));
        Assert.Equal(priced ? expected : (decimal?)null, ClaudeApiPricing.CostUsd(Entry(model, input: 1_000_000)));
    }

    private static ClaudeUsageEntry Entry(string model, long input = 0, long output = 0, long cacheRead = 0, long write5m = 0, long write1h = 0, int webSearches = 0, string? speed = null, string? inferenceGeo = null) =>
        new("id|request", DateTimeOffset.UnixEpoch, model, input, output, cacheRead, write5m, write1h, webSearches, speed, inferenceGeo);
}
