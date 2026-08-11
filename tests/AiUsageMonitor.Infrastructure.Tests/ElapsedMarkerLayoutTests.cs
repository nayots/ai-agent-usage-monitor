using AiUsageMonitor.Infrastructure.Theming;

namespace AiUsageMonitor.Infrastructure.Tests;

public class ElapsedMarkerLayoutTests
{
    [Fact]
    public void AtTheStartTheBoxSitsFlushInsideTheLeftEdge() =>
        Assert.Equal(0.0, ElapsedMarkerLayout.OffsetFor(0.0, trackWidth: 100.0));

    [Fact]
    public void AtTheEndTheBoxSitsFlushInsideTheRightEdge()
    {
        Assert.Equal(96.0, ElapsedMarkerLayout.OffsetFor(1.0, trackWidth: 100.0));
    }

    [Fact]
    public void HalfwayIsHalfOfTheAvailableTravel() =>
        Assert.Equal(48.0, ElapsedMarkerLayout.OffsetFor(0.5, trackWidth: 100.0));

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(2.0, 96.0)]
    public void FractionsOutsideTheUnitRangeAreClamped(double fraction, double expected) =>
        Assert.Equal(expected, ElapsedMarkerLayout.OffsetFor(fraction, trackWidth: 100.0));

    [Fact]
    public void ATrackNarrowerThanTheMarkerNeverProducesANegativeOffset() =>
        Assert.Equal(0.0, ElapsedMarkerLayout.OffsetFor(1.0, trackWidth: 2.0));
}
