using AiUsageMonitor.App.Interop;

namespace AiUsageMonitor.App.Tests;

public class WidgetHeightCapTests
{
    [Fact]
    public void ATallScreenLetsTheWidgetGrowPastTheMeasuredCap()
    {
        Assert.Equal(1032, WidgetHeightCap.For(1080));
    }

    [Fact]
    public void AShortScreenKeepsExactlyTheMeasuredCap()
    {
        Assert.Equal(WidgetHeightCap.Measured, WidgetHeightCap.For(500));
    }

    /// <summary>
    /// The margin is what stops the cap creeping past the measured value on a screen barely taller
    /// than it: the widget must clear the work area's edge, not touch it.
    /// </summary>
    [Fact]
    public void AScreenExactlyOneMarginTallerThanTheCapStillYieldsTheCap()
    {
        Assert.Equal(WidgetHeightCap.Measured, WidgetHeightCap.For(WidgetHeightCap.Measured + WidgetHeightCap.Margin));
    }

    [Fact]
    public void OneMoreThanThatGrowsByOne()
    {
        Assert.Equal(
            WidgetHeightCap.Measured + 1,
            WidgetHeightCap.For(WidgetHeightCap.Measured + WidgetHeightCap.Margin + 1));
    }

    /// <summary>
    /// A work area of zero means the shell declined to answer, not that the screen has no height.
    /// The measured cap is the honest answer to that, and it is what shipped before this rule
    /// existed - never a window of negative height.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1080)]
    [InlineData(double.NaN)]
    public void AnUnusableWorkAreaFallsBackToTheMeasuredCap(double workAreaHeight)
    {
        Assert.Equal(WidgetHeightCap.Measured, WidgetHeightCap.For(workAreaHeight));
    }
}
