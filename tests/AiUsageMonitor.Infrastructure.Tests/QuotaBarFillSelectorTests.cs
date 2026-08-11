using AiUsageMonitor.Infrastructure.Theming;

namespace AiUsageMonitor.Infrastructure.Tests;

public class QuotaBarFillSelectorTests
{
    private static QuotaBarFill Select(double? used, bool limitReached = false, bool colorBarsByUsage = true, bool isStale = false) =>
        QuotaBarFillSelector.Select(used, limitReached, colorBarsByUsage, isStale);

    [Theory]
    [InlineData(0.0)]
    [InlineData(50.0)]
    [InlineData(74.9)]
    public void BelowSeventyFiveIsAccent(double used) => Assert.Equal(QuotaBarFill.Accent, Select(used));

    [Theory]
    [InlineData(75.0)]
    [InlineData(99.9)]
    public void SeventyFiveThroughNinetyNineIsHigh(double used) => Assert.Equal(QuotaBarFill.High, Select(used));

    [Fact]
    public void OneHundredIsExhausted() => Assert.Equal(QuotaBarFill.Exhausted, Select(100.0));

    [Fact]
    public void OverOneHundredIsExhaustedRatherThanUnhandled()
    {
        Assert.Equal(QuotaBarFill.Exhausted, Select(150.0));
    }

    [Fact]
    public void ExhaustedIgnoresTheSetting()
    {
        Assert.Equal(QuotaBarFill.Exhausted, Select(100.0, colorBarsByUsage: false));
    }

    [Fact]
    public void HighBandCollapsesToAccentWhenTheSettingIsOff() =>
        Assert.Equal(QuotaBarFill.Accent, Select(80.0, colorBarsByUsage: false));

    [Fact]
    public void ProviderReportedLimitReachedIsExhaustedAtAnyPercentage() =>
        Assert.Equal(QuotaBarFill.Exhausted, Select(40.0, limitReached: true));

    [Fact]
    public void StaleWinsOverEveryOtherRole() =>
        Assert.Equal(QuotaBarFill.Stale, Select(100.0, limitReached: true, isStale: true));

    [Fact]
    public void UnknownUsageClaimsNoBand()
    {
        Assert.Equal(QuotaBarFill.Accent, Select(null));
    }
}
