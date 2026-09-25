using AiUsageMonitor.Infrastructure.Providers.Claude;

namespace AiUsageMonitor.Infrastructure.Tests;

public sealed class ClaudeUsageEstimateFormatTests
{
    [Theory]
    [InlineData(950, "950")]
    [InlineData(12_400, "12.4K")]
    [InlineData(1_200_000, "1.2M")]
    [InlineData(2_000_000, "2M")]
    [InlineData(3_400_000_000, "3.4B")]
    [InlineData(999_950, "1M")]
    public void CompactTokensUsesInvariantRoundedUnits(long tokens, string expected)
    {
        Assert.Equal(expected, ClaudeUsageEstimateFormat.CompactTokens(tokens));
    }

    [Fact]
    public void AmountTextShowsEstimatedCostAndTokens()
    {
        Assert.Equal("≈ $38.20 · 12.4M tokens", ClaudeUsageEstimateFormat.AmountText(new ClaudeUsagePeriod(12_400_000, 0, 0, 0, 38.2m, 0)));
    }

    [Fact]
    public void UnpricedTokensTurnTheEstimateIntoALowerBound()
    {
        Assert.StartsWith("≥ $", ClaudeUsageEstimateFormat.AmountText(new ClaudeUsagePeriod(0, 0, 0, 0, 38.2m, 1)));
    }

    [Fact]
    public void AReadZeroIsRenderedAsZero()
    {
        Assert.Equal("≈ $0.00 · 0 tokens", ClaudeUsageEstimateFormat.AmountText(new ClaudeUsagePeriod(0, 0, 0, 0, 0m, 0)));
    }
}
