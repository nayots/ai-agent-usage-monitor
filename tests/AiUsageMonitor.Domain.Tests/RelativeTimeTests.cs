using AiUsageMonitor.Domain;

namespace AiUsageMonitor.Domain.Tests;

public class RelativeTimeTests
{
    [Fact]
    public void NullAgeFormatsAsNullSoTheCallerOmitsTheElement() =>
        Assert.Null(RelativeTime.FormatAge(null));

    [Theory]
    [InlineData(0, "0s ago")]
    [InlineData(12, "12s ago")]
    [InlineData(59, "59s ago")]
    public void SecondsUnderAMinute(int seconds, string expected) =>
        Assert.Equal(expected, RelativeTime.FormatAge(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(60, "1 minute ago")]
    [InlineData(119, "1 minute ago")]
    [InlineData(360, "6 minutes ago")]
    [InlineData(3599, "59 minutes ago")]
    public void MinutesUnderAnHour(int seconds, string expected) =>
        Assert.Equal(expected, RelativeTime.FormatAge(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(1, "1 hour ago")]
    [InlineData(2, "2 hours ago")]
    [InlineData(23, "23 hours ago")]
    public void HoursUnderADay(int hours, string expected) =>
        Assert.Equal(expected, RelativeTime.FormatAge(TimeSpan.FromHours(hours)));

    [Theory]
    [InlineData(24, "1 day ago")]
    [InlineData(72, "3 days ago")]
    public void DaysAndAbove(int hours, string expected) =>
        Assert.Equal(expected, RelativeTime.FormatAge(TimeSpan.FromHours(hours)));

    [Fact]
    public void FutureAgesClampToZeroRatherThanRenderingNegative()
    {
        // Clock skew, DST transitions and resume-from-sleep all produce a future timestamp.
        Assert.Equal("0s ago", RelativeTime.FormatAge(TimeSpan.FromSeconds(-30)));
    }
}
