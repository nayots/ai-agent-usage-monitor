using Xunit;

namespace AiUsageMonitor.Domain.Tests;

public class QuotaFormattingTests
{
    [Fact]
    public void FormatUsedPercent_IsNull_WhenUsageIsUnknown()
    {
        // The single most important assertion in this file. Missing data is null and
        // surfaces as Waiting/Unavailable - never as "0% used". PRD SS4.3 / SS13.
        Assert.Null(QuotaFormatting.FormatUsedPercent(null));
    }

    [Theory]
    [InlineData(0, "0% used")]
    [InlineData(28, "28% used")]
    [InlineData(100, "100% used")]
    [InlineData(42.5, "43% used")]   // away-from-zero, not banker's rounding
    [InlineData(42.4, "42% used")]
    public void FormatUsedPercent_StatesTheDirectionExplicitly(double used, string expected)
    {
        // PRD SS16: the visible percentage text must make the direction explicit.
        Assert.Equal(expected, QuotaFormatting.FormatUsedPercent(used));
    }

    [Theory]
    [InlineData(150, "150% used")]
    [InlineData(-5, "-5% used")]
    public void FormatUsedPercent_RendersOutOfRangeValuesVerbatim(double used, string expected)
    {
        // Deliberate pair with QuotaWindow.RemainingPercent, which clamps: the raw used value
        // stays visible so provider over-reporting is never silently hidden. See the XML doc on
        // both members for the full rationale.
        Assert.Equal(expected, QuotaFormatting.FormatUsedPercent(used));
    }

    [Fact]
    public void FormatRemainingPercent_IsNull_WhenUsageIsUnknown()
    {
        Assert.Null(QuotaFormatting.FormatRemainingPercent(null));
    }

    [Fact]
    public void FormatRemainingPercent_StatesTheDirectionExplicitly()
    {
        Assert.Equal("72% remaining", QuotaFormatting.FormatRemainingPercent(72));
    }

    [Fact]
    public void FormatCountdown_IsNull_WhenNoResetTimeIsKnown()
    {
        // nimbus_quill has no reset time. The countdown is omitted, not zeroed.
        Assert.Null(QuotaFormatting.FormatCountdown(null));
    }

    [Theory]
    [InlineData(0, 0, 9, 30, "9m 30s")]
    [InlineData(0, 4, 12, 0, "4h 12m")]
    [InlineData(0, 1, 0, 0, "1h 00m")]
    [InlineData(3, 4, 30, 0, "3d 04h")]
    [InlineData(5, 7, 39, 0, "5d 07h")]
    public void FormatCountdown_UsesTwoUnitsAtTheAppropriateScale(
        int days, int hours, int minutes, int seconds, string expected)
    {
        var span = new TimeSpan(days, hours, minutes, seconds);

        Assert.Equal(expected, QuotaFormatting.FormatCountdown(span));
    }

    [Fact]
    public void FormatCountdown_ClampsNegativeSpansToZero()
    {
        Assert.Equal("0m 00s", QuotaFormatting.FormatCountdown(TimeSpan.FromMinutes(-30)));
    }
}
