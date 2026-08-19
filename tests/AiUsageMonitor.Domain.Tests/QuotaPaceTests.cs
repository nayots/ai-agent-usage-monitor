using Xunit;

namespace AiUsageMonitor.Domain.Tests;

public class QuotaPaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private static QuotaWindow Window(double durationHours, double elapsedHours, double? used) =>
        QuotaWindowTests.Window(
            usedPercent: used,
            resetsAt: Now.AddHours(durationHours - elapsedHours),
            windowDuration: TimeSpan.FromHours(durationHours));

    [Fact]
    public void For_ProjectsExhaustionAndShortfall_FromTheAveragePaceSoFar()
    {
        PaceProjection? projection = QuotaPace.For(Window(durationHours: 100, elapsedHours: 50, used: 80), Now);

        Assert.NotNull(projection);
        Assert.Equal(Now.AddHours(12.5), projection.ExhaustsAt);
        Assert.Equal(TimeSpan.FromHours(37.5), projection.Shortfall);
    }

    [Fact]
    public void For_ProjectsTheLiveSevenDayWindowRecordedInTheSpec()
    {
        PaceProjection? projection = QuotaPace.For(Window(durationHours: 168, elapsedHours: 42, used: 44), Now);

        Assert.NotNull(projection);
        Assert.Equal(72.545, projection.Shortfall.TotalHours, 3);
    }

    [Fact]
    public void For_IsNull_WhenUsageTracksBehindTheEvenPace() =>
        Assert.Null(QuotaPace.For(Window(durationHours: 5, elapsedHours: 3.1, used: 37), Now));

    [Fact]
    public void For_IsNull_WhenUsageTracksExactlyTheEvenPace() =>
        Assert.Null(QuotaPace.For(Window(durationHours: 168, elapsedHours: 146.16, used: 87), Now));

    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(100d)]
    [InlineData(150d)]
    public void For_IsNull_WhenTheReportedPercentageIsOutsideTheOpenRange(double? used) =>
        Assert.Null(QuotaPace.For(Window(durationHours: 100, elapsedHours: 50, used: used), Now));

    [Fact]
    public void For_IsNull_WhenTheReportedPercentageIsNotFinite() =>
        Assert.Null(QuotaPace.For(Window(durationHours: 100, elapsedHours: 50, used: double.NaN), Now));

    [Fact]
    public void For_ProjectsForAWindowJustShortOfExhausted() =>
        Assert.NotNull(QuotaPace.For(Window(durationHours: 100, elapsedHours: 50, used: 99.9), Now));

    [Fact]
    public void For_IsNull_WhenTheProviderSuppliedNoResetTime() =>
        Assert.Null(QuotaPace.For(QuotaWindowTests.Window(usedPercent: 50, resetsAt: null, windowDuration: TimeSpan.FromHours(100)), Now));

    [Fact]
    public void For_IsNull_WhenTheWindowDurationCouldNotBeDerived() =>
        Assert.Null(QuotaPace.For(QuotaWindowTests.Window(usedPercent: 50, resetsAt: Now.AddHours(4), windowDuration: null), Now));

    [Fact]
    public void For_IsNull_WhenTheWindowDurationIsZero() =>
        Assert.Null(QuotaPace.For(QuotaWindowTests.Window(usedPercent: 50, resetsAt: Now.AddHours(4), windowDuration: TimeSpan.Zero), Now));

    [Fact]
    public void For_ProjectsAtExactlyTheMinimumElapsedFraction() =>
        Assert.NotNull(QuotaPace.For(Window(durationHours: 100, elapsedHours: 10, used: 50), Now));

    [Fact]
    public void For_IsNull_JustBelowTheMinimumElapsedFraction() =>
        Assert.Null(QuotaPace.For(Window(durationHours: 100, elapsedHours: 9, used: 50), Now));

    [Fact]
    public void For_IsNull_WhenTheWindowHasFullyElapsed() =>
        Assert.Null(QuotaPace.For(Window(durationHours: 100, elapsedHours: 100, used: 50), Now));

    [Theory]
    [InlineData(49.9, false)]
    [InlineData(50.0, false)]
    [InlineData(50.1, true)]
    public void For_RequiresTheShortfallToClearTwoPercentOfTheWindow(double used, bool projects) =>
        Assert.Equal(projects, QuotaPace.For(Window(durationHours: 100, elapsedHours: 49, used: used), Now) is not null);

    [Fact]
    public void MinimumsAreTheValuesTheSpecFixed()
    {
        Assert.Equal(0.10, QuotaPace.MinimumElapsedFraction);
        Assert.Equal(0.02, QuotaPace.MinimumShortfallFraction);
    }
}
