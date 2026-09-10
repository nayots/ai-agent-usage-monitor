using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Infrastructure.Theming;

namespace AiUsageMonitor.App.Tests;

public class TrayRotationTests
{
    private static readonly TimeSpan Dwell = TimeSpan.FromSeconds(4);

    private static TrayGlyphFrame Reading(string mark, double used) =>
        new(mark, ((int)used).ToString(), used, QuotaBarFill.Accent, TrayFrameKind.Reading);

    private static TrayGlyphFrame Troubled(string mark, TrayFrameKind kind) =>
        new(mark, null, null, QuotaBarFill.Accent, kind);

    private static readonly TrayGlyphFrame[] Three =
        [Reading("CC", 92d), Reading("CX", 44d), Reading("CR", 22d)];

    /// <summary>
    /// A turn is one frame now. It used to open on a second of the provider's initials before the
    /// number arrived; the initials are in the frame itself, so the whole turn carries a reading.
    /// </summary>
    [Fact]
    public void AWholeTurnShowsOneFrameFromItsFirstTickToItsLast()
    {
        Assert.Equal(0, TrayRotation.At(Three, TimeSpan.Zero, Dwell));
        Assert.Equal(0, TrayRotation.At(Three, TimeSpan.FromMilliseconds(999), Dwell));
        Assert.Equal(0, TrayRotation.At(Three, TimeSpan.FromSeconds(1), Dwell));
        Assert.Equal(0, TrayRotation.At(Three, TimeSpan.FromSeconds(3.9), Dwell));
    }

    [Fact]
    public void TheTurnAdvancesOnEveryDwellAndWrapsAtTheEnd()
    {
        Assert.Equal(1, TrayRotation.At(Three, TimeSpan.FromSeconds(4), Dwell));
        Assert.Equal(2, TrayRotation.At(Three, TimeSpan.FromSeconds(8), Dwell));
        Assert.Equal(0, TrayRotation.At(Three, TimeSpan.FromSeconds(12), Dwell));
    }

    /// <summary>
    /// The answer to the obvious objection - that you might be looking away when the one that
    /// matters goes past. When it matters, it stops going past.
    /// </summary>
    [Fact]
    public void ItParksOnTheOnlyTroubledProvider()
    {
        TrayGlyphFrame[] frames = [Reading("CC", 61d), Troubled("CX", TrayFrameKind.AtLimit), Reading("CR", 22d)];

        foreach (double second in (double[])[0d, 1.5d, 4d, 9d, 400d])
        {
            Assert.Equal(1, TrayRotation.At(frames, TimeSpan.FromSeconds(second), Dwell));
        }
    }

    [Fact]
    public void TwoInTroubleRotateBetweenThemselvesAndIgnoreTheHealthyOne()
    {
        TrayGlyphFrame[] frames =
            [Troubled("CC", TrayFrameKind.AtLimit), Reading("CX", 12d), Troubled("CR", TrayFrameKind.Failed)];

        Assert.Equal(0, TrayRotation.At(frames, TimeSpan.Zero, Dwell));
        Assert.Equal(2, TrayRotation.At(frames, TimeSpan.FromSeconds(4), Dwell));
        Assert.Equal(0, TrayRotation.At(frames, TimeSpan.FromSeconds(8), Dwell));
    }

    /// <summary>Waiting is not trouble. A provider that has not answered yet does not seize the icon.</summary>
    [Fact]
    public void AWaitingProviderDoesNotCountAsTrouble()
    {
        TrayGlyphFrame[] frames = [Reading("CC", 61d), Troubled("CX", TrayFrameKind.Waiting)];

        Assert.Equal(0, TrayRotation.At(frames, TimeSpan.Zero, Dwell));
        Assert.Equal(1, TrayRotation.At(frames, TimeSpan.FromSeconds(4), Dwell));
    }

    [Fact]
    public void OneProviderNeverTurns()
    {
        TrayGlyphFrame[] one = [Reading("CC", 61d)];

        Assert.Equal(0, TrayRotation.At(one, TimeSpan.Zero, Dwell));
        Assert.Equal(0, TrayRotation.At(one, TimeSpan.FromSeconds(30), Dwell));
    }

    /// <summary>With rotation off there is one frame forever, so it had better be the worst one.</summary>
    [Fact]
    public void RotationOffHoldsTheWorstProviderRatherThanTheFirst()
    {
        Assert.Equal(0, TrayRotation.At(Three, TimeSpan.FromSeconds(9), null));
        Assert.Equal(1, TrayRotation.At([Reading("CC", 12d), Reading("CX", 88d)], TimeSpan.FromSeconds(9), null));
    }

    [Fact]
    public void NoFramesYieldsNoSlide()
    {
        Assert.Equal(-1, TrayRotation.At([], TimeSpan.Zero, Dwell));
    }

    [Fact]
    public void ItTurnsOnlyWhenThereIsSomethingToTurnAndNobodyIsWatchingTheWindow()
    {
        Assert.True(TrayRotation.ShouldTurn(Three, windowVisible: false, sessionLocked: false, Dwell));
        Assert.False(TrayRotation.ShouldTurn(Three, windowVisible: true, sessionLocked: false, Dwell));
        Assert.False(TrayRotation.ShouldTurn(Three, windowVisible: false, sessionLocked: true, Dwell));
        Assert.False(TrayRotation.ShouldTurn(Three, windowVisible: false, sessionLocked: false, null));
        Assert.False(TrayRotation.ShouldTurn([Reading("CC", 5d)], windowVisible: false, sessionLocked: false, Dwell));
        Assert.False(TrayRotation.ShouldTurn(
            [Reading("CC", 5d), Troubled("CX", TrayFrameKind.Failed)], windowVisible: false, sessionLocked: false, Dwell));
    }

    [Fact]
    public void ThePresetsStayInsideTheirOwnBounds()
    {
        Assert.All(TrayRotation.DwellPresets, seconds =>
            Assert.InRange(seconds, TrayRotation.MinimumDwellSeconds, TrayRotation.MaximumDwellSeconds));
    }
}
