using Xunit;

namespace AiUsageMonitor.Domain.Tests;

public class QuotaWindowTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    internal static QuotaWindow Window(
        string id = "w",
        double? usedPercent = 50,
        DateTimeOffset? resetsAt = null,
        TimeSpan? windowDuration = null,
        int order = 0,
        bool labelIsProviderToken = false) =>
        new(
            Id: id,
            Label: id,
            UsedPercent: usedPercent,
            ResetsAt: resetsAt,
            WindowDuration: windowDuration,
            Order: order,
            IsPartial: resetsAt is null || windowDuration is null,
            Extra: new Dictionary<string, string>(),
            LabelIsProviderToken: labelIsProviderToken);

    [Fact]
    public void TimeUntilReset_IsNull_WhenTheProviderSuppliedNoResetTime()
    {
        // Absence must stay absence. A null countdown is omitted, never rendered as zero.
        Assert.Null(Window(resetsAt: null).TimeUntilReset(Now));
    }

    [Fact]
    public void TimeUntilReset_ReturnsTheRemainingSpan()
    {
        QuotaWindow window = Window(resetsAt: Now.AddHours(4).AddMinutes(12));

        Assert.Equal(TimeSpan.FromMinutes(252), window.TimeUntilReset(Now));
    }

    [Fact]
    public void TimeUntilReset_ClampsToZero_WhenTheResetTimeHasPassed()
    {
        // Stale snapshots routinely carry a reset time in the past. Never show a negative countdown.
        QuotaWindow window = Window(resetsAt: Now.AddHours(-3));

        Assert.Equal(TimeSpan.Zero, window.TimeUntilReset(Now));
    }

    [Fact]
    public void ElapsedFraction_IsNull_WhenTheWindowDurationIsUnknown()
    {
        // This is the nimbus_quill case: a real live window with a percentage and nothing else.
        // PRD SS16 requires the elapsed marker to be omitted, never guessed.
        QuotaWindow window = Window(resetsAt: Now.AddHours(2), windowDuration: null);

        Assert.Null(window.ElapsedFraction(Now));
    }

    [Fact]
    public void ElapsedFraction_IsNull_WhenTheResetTimeIsUnknown()
    {
        QuotaWindow window = Window(resetsAt: null, windowDuration: TimeSpan.FromHours(5));

        Assert.Null(window.ElapsedFraction(Now));
    }

    [Fact]
    public void ElapsedFraction_IsNull_ForAZeroLengthWindow()
    {
        QuotaWindow window = Window(resetsAt: Now, windowDuration: TimeSpan.Zero);

        Assert.Null(window.ElapsedFraction(Now));
    }

    [Theory]
    [InlineData(0, 1.0)]      // reset is now: the window is fully elapsed
    [InlineData(5, 0.0)]      // reset is a full duration away: nothing elapsed
    [InlineData(1, 0.8)]
    [InlineData(4, 0.2)]
    public void ElapsedFraction_MapsResetDistanceOntoZeroToOne(int hoursUntilReset, double expected)
    {
        QuotaWindow window = Window(
            resetsAt: Now.AddHours(hoursUntilReset),
            windowDuration: TimeSpan.FromHours(5));

        Assert.Equal(expected, window.ElapsedFraction(Now)!.Value, precision: 6);
    }

    [Fact]
    public void ElapsedFraction_ClampsAboveOne_WhenTheResetTimeIsAlreadyPast()
    {
        QuotaWindow window = Window(
            resetsAt: Now.AddHours(-10),
            windowDuration: TimeSpan.FromHours(5));

        Assert.Equal(1.0, window.ElapsedFraction(Now)!.Value, precision: 6);
    }

    [Fact]
    public void ElapsedFraction_ReproducesTheVerifiedCodexState()
    {
        // Verified 2026-08-10: 100% used with only ~24% of a 7-day window elapsed.
        // The gap between fill and marker is the whole reason the marker exists (PRD SS16).
        var duration = TimeSpan.FromDays(7);
        QuotaWindow window = Window(
            usedPercent: 100,
            resetsAt: Now.Add(duration * 0.76),
            windowDuration: duration);

        Assert.Equal(0.24, window.ElapsedFraction(Now)!.Value, precision: 2);
        Assert.Equal(100.0, window.UsedPercent);
    }

    [Fact]
    public void RemainingPercent_IsNull_WhenUsageIsUnknown()
    {
        // Must never collapse to 100. Unknown usage is unknown remaining.
        Assert.Null(Window(usedPercent: null).RemainingPercent);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(28, 72)]
    [InlineData(100, 0)]
    [InlineData(120, 0)]   // provider over-reporting is clamped, not propagated
    [InlineData(-5, 100)]  // a negative "used" clamps the derived remaining to 100, not 105
    public void RemainingPercent_IsTheClampedComplementOfUsage(double used, double expected)
    {
        Assert.Equal(expected, Window(usedPercent: used).RemainingPercent);
    }
}
