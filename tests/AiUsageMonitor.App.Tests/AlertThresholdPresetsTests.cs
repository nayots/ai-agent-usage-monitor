using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Domain;

namespace AiUsageMonitor.App.Tests;

public class AlertThresholdPresetsTests
{
    [Fact]
    public void TheFirstPresetIsTheDefaultLadderItselfRatherThanACopy() =>
        Assert.Same(QuotaMilestones.Ladder, AlertThresholdPresets.All[0].Thresholds);

    [Fact]
    public void ThePresetsRunFromEveryRungToOnlyTheLast()
    {
        Assert.Equal([0, 1, 2, 3], AlertThresholdPresets.All.Select(preset => preset.Id));
        Assert.Equal(
            ["Every milestone", "80, 90 and 100%", "90 and 100%", "100% only"],
            AlertThresholdPresets.All.Select(preset => preset.Label));
    }

    [Theory]
    [InlineData(new[] { 80, 90, 100 }, 1)]
    [InlineData(new[] { 90, 100 }, 2)]
    [InlineData(new[] { 100 }, 3)]
    public void EveryPresetIsFoundByItsOwnThresholds(int[] thresholds, int expectedId) =>
        Assert.Equal(expectedId, AlertThresholdPresets.IdFor(thresholds));

    [Fact]
    public void TheDefaultLadderIsFoundAsTheFirstPreset() =>
        Assert.Equal(0, AlertThresholdPresets.IdFor(QuotaMilestones.Ladder));

    /// <summary>
    /// -1 rather than a nearest match: a ladder someone typed into the settings file is theirs, and
    /// snapping it to a preset would silently change how often they are told.
    /// </summary>
    [Fact]
    public void ALadderMatchingNoPresetIsNotPretendedToBeOne() =>
        Assert.Equal(-1, AlertThresholdPresets.IdFor([75, 100]));

    /// <summary>
    /// Comparison happens after sanitizing, so a file holding the right rungs in the wrong order
    /// still shows its preset selected rather than an identical-looking custom entry.
    /// </summary>
    [Fact]
    public void AnUnsortedOrDuplicatedLadderStillFindsItsPreset() =>
        Assert.Equal(2, AlertThresholdPresets.IdFor([100, 90, 90]));

    [Fact]
    public void ACustomLadderIsNamedByItsRungs() =>
        Assert.Equal("Custom (75, 90, 100%)", AlertThresholdPresets.CustomLabel([75, 90, 100]));
}
