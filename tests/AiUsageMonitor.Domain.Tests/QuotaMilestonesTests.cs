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

    [Theory]
    [InlineData(84d, 80)]
    [InlineData(90d, 90)]
    [InlineData(140d, 100)]
    [InlineData(null, 0)]
    public void ASuppliedLadderReplacesTheDefaultOne(double? used, int expected) =>
        Assert.Equal(expected, QuotaMilestones.Crossed(used, [80, 90, 100]));

    /// <summary>
    /// A sparse ladder must go quiet between its rungs rather than fall back to the default one.
    /// Choosing to hear less is the whole point of choosing a ladder.
    /// </summary>
    [Fact]
    public void AReadingBelowTheLowestRungOfASparseLadderReachesNothing() =>
        Assert.Equal(0, QuotaMilestones.Crossed(9, [10, 100]));

    [Fact]
    public void SanitizeDropsOutOfRangeValuesCollapsesDuplicatesAndSorts() =>
        Assert.Equal([20, 95, 100], QuotaMilestones.Sanitize([95, 20, 20, 200, -3]));

    [Fact]
    public void SanitizeKeepsALadderThatIsAlreadyJustTheTopRung() =>
        Assert.Equal([100], QuotaMilestones.Sanitize([100]));

    [Fact]
    public void SanitizeAddsAHundredToALadderThatOmittedIt() =>
        Assert.Equal([50, 100], QuotaMilestones.Sanitize([50]));

    /// <summary>
    /// The three ways a hand-edited settings file can hold nothing usable. Each falls back to the
    /// default ladder rather than to silence: the notifications switch is how someone asks for
    /// silence on purpose, and a typo must not be able to imitate it.
    /// </summary>
    [Fact]
    public void SanitizeFallsBackToTheDefaultLadderRatherThanToSilence()
    {
        Assert.Equal(QuotaMilestones.Ladder, QuotaMilestones.Sanitize(null));
        Assert.Equal(QuotaMilestones.Ladder, QuotaMilestones.Sanitize([]));
        Assert.Equal(QuotaMilestones.Ladder, QuotaMilestones.Sanitize([0, -5, 101, 900]));
    }

    [Fact]
    public void SanitizeAlwaysProducesAnAscendingLadderContainingAHundred()
    {
        IReadOnlyList<int> sanitized = QuotaMilestones.Sanitize([100, 3, 77, 3, 40]);

        Assert.Contains(100, sanitized);
        Assert.Equal(sanitized.OrderBy(rung => rung), sanitized);
    }
}
