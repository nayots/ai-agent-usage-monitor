using System.Windows;
using AiUsageMonitor.App.Interop;

namespace AiUsageMonitor.App.Tests;

public class PlacementClampTests
{
    private static readonly Rect WorkArea = new(0, 0, 1000, 700);

    [Fact]
    public void AWindowInsideTheWorkAreaIsUnchanged() =>
        Assert.Equal(new Rect(100, 100, 360, 200), PlacementClamp.Fit(new Rect(100, 100, 360, 200), WorkArea));

    [Fact]
    public void AWindowOverhangingTheRightEdgeMovesLeftWithoutResizing() =>
        Assert.Equal(new Rect(640, 100, 360, 200), PlacementClamp.Fit(new Rect(800, 100, 360, 200), WorkArea));

    [Theory]
    [InlineData(-20, 100, 0, 100)]
    [InlineData(100, -20, 100, 0)]
    [InlineData(100, 600, 100, 500)]
    public void AWindowOverhangingAnyOtherEdgeIsMovedWhollyInside(double left, double top, double expectedLeft, double expectedTop) =>
        Assert.Equal(new Rect(expectedLeft, expectedTop, 360, 200), PlacementClamp.Fit(new Rect(left, top, 360, 200), WorkArea));

    [Theory]
    [InlineData(1200, 900, 640, 500)]
    [InlineData(-30000, -30000, 0, 0)]
    [InlineData(999, 100, 640, 100)]
    public void AWindowOutsideOrBarelyInsideTheWorkAreaIsMovedFullyInside(
        double left,
        double top,
        double expectedLeft,
        double expectedTop) =>
        Assert.Equal(
            new Rect(expectedLeft, expectedTop, 360, 200),
            PlacementClamp.Fit(new Rect(left, top, 360, 200), WorkArea));

    [Fact]
    public void AWindowTallerThanTheWorkAreaKeepsItsSizeAtTheWorkAreasTopLeft() =>
        Assert.Equal(new Rect(0, 0, 360, 800), PlacementClamp.Fit(new Rect(100, 100, 360, 800), WorkArea));

    [Fact]
    public void AWorkAreaWithANonZeroOriginIsHonoured() =>
        Assert.Equal(
            new Rect(2560, 500, 360, 200),
            PlacementClamp.Fit(new Rect(2800, 600, 360, 200), new Rect(1920, 40, 1000, 660)));
}
