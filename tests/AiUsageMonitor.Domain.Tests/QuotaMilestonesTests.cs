using Xunit;

namespace AiUsageMonitor.Domain.Tests;

public class QuotaMilestonesTests
{
    // Every reading is written as a double literal. xUnit hands InlineData arguments to the method
    // by reflection with no numeric conversion, so a bare 0 arrives as an int and fails to bind to
    // double? at invocation time - a failure that looks nothing like the assertion it replaces.
    [Theory]
    [InlineData(null, 0)]
    [InlineData(0d, 0)]
    [InlineData(9.9d, 0)]
    [InlineData(10d, 10)]
    [InlineData(62d, 60)]
    [InlineData(79.9d, 70)]
    [InlineData(80d, 80)]
    [InlineData(84.9d, 80)]
    [InlineData(85d, 85)]
    [InlineData(99.9d, 95)]
    [InlineData(100d, 100)]
    [InlineData(140d, 100)]
    public void TheCrossedRungIsTheHighestAtOrBelowTheReading(double? used, int expected) =>
        Assert.Equal(expected, QuotaMilestones.Crossed(used));

    [Fact]
    public void TheLadderTightensAboveEighty() =>
        Assert.Equal([10, 20, 30, 40, 50, 60, 70, 80, 85, 90, 95, 100], QuotaMilestones.Ladder);

    /// <summary>
    /// A window that reports nothing must be indistinguishable from one that has reached no rung,
    /// because the alternative is an alert derived from data that does not exist.
    /// </summary>
    [Fact]
    public void AnAbsentReadingReachesNoRungRatherThanTheBottomOne() =>
        Assert.Equal(QuotaMilestones.Crossed(0), QuotaMilestones.Crossed(null));
}
