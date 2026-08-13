using Xunit;

namespace AiUsageMonitor.Domain.Tests;

public class QuietHoursTests
{
    private static TimeOnly At(int hour, int minute = 0) => new(hour, minute);

    /// <summary>
    /// Disabled short-circuits before the times are looked at, so a half-configured schedule left
    /// in the settings file cannot silence anything.
    /// </summary>
    [Fact]
    public void ADisabledScheduleContainsNoTimeAtAll()
    {
        QuietHours quiet = new(false, 1320, 420);

        Assert.False(quiet.Contains(At(23, 30)));
        Assert.False(quiet.Contains(At(3)));
        Assert.False(quiet.Contains(At(12)));
    }

    [Theory]
    [InlineData(13, 0, true)]
    [InlineData(13, 59, true)]
    [InlineData(14, 0, false)]
    [InlineData(12, 59, false)]
    public void ADaytimeWindowIsHalfOpenAtBothEnds(int hour, int minute, bool expected) =>
        Assert.Equal(expected, new QuietHours(true, 780, 840).Contains(At(hour, minute)));

    [Theory]
    [InlineData(22, 0, true)]
    [InlineData(23, 30, true)]
    [InlineData(0, 0, true)]
    [InlineData(6, 59, true)]
    [InlineData(7, 0, false)]
    [InlineData(12, 0, false)]
    [InlineData(21, 59, false)]
    public void AWindowRunningPastMidnightCoversBothSidesOfIt(int hour, int minute, bool expected) =>
        Assert.Equal(expected, new QuietHours(true, 1320, 420).Contains(At(hour, minute)));

    /// <summary>
    /// Not configured yet, rather than silence me forever - and the notifications switch already
    /// does the latter deliberately.
    /// </summary>
    [Fact]
    public void AZeroLengthWindowContainsNothingIncludingItsOwnEndpoint() =>
        Assert.False(new QuietHours(true, 600, 600).Contains(At(10)));

    /// <summary>
    /// A negative or over-large number in a hand-edited file has to fold rather than throw, and
    /// must not fold into a window that never ends.
    /// </summary>
    [Theory]
    [InlineData(-60, 23, 30, true)]
    [InlineData(-60, 22, 30, false)]
    [InlineData(1500, 1, 30, true)]
    [InlineData(1500, 0, 30, false)]
    public void OutOfRangeMinutesFoldIntoTheDay(int startMinutes, int hour, int minute, bool expected) =>
        Assert.Equal(expected, new QuietHours(true, startMinutes, 420).Contains(At(hour, minute)));

    [Fact]
    public void OffIsTheDefaultOvernightScheduleSwitchedOff() =>
        Assert.Equal(new QuietHours(false, 1320, 420), QuietHours.Off);
}
