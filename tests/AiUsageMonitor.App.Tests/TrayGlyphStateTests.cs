using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Refresh;
using AiUsageMonitor.Infrastructure.Theming;

namespace AiUsageMonitor.App.Tests;

public class TrayGlyphStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly FreshnessPolicy Policy = new(TimeSpan.FromMinutes(5));

    [Fact]
    public void OneFramePerVisibleProviderInCardOrder()
    {
        TrayGlyphState state = TrayGlyphState.From(
            [Card("CC", 92d, traySlot: 0), Card("CX", 44d, traySlot: 1), Card("CR", 22d, traySlot: 2)]);

        Assert.Equal([0, 1, 2], state.Frames.Select(frame => frame.Slot));
    }

    /// <summary>
    /// The first window, not the fullest. Reporting the fullest made the icon read "95" for a
    /// weekly window while the five-hour window the user actually works against sat at 5% - the
    /// figure a glance at the tray is for. The tooltip still lists every window.
    /// </summary>
    [Fact]
    public void AFrameReportsItsProvidersFirstWindowRatherThanItsFullest()
    {
        TrayGlyphState state = TrayGlyphState.From([Card("CC", 5d, 95d, 40d)]);

        Assert.Equal("5", state.Frames[0].Digits);
        Assert.Equal(5d, state.Frames[0].UsedPercent);
    }

    [Fact]
    public void AFirstWindowWithNoPercentageFallsThroughToTheNextThatHasOne()
    {
        TrayGlyphState state = TrayGlyphState.From([Card("CC", null, 30d, 60d)]);

        Assert.Equal("30", state.Frames[0].Digits);
    }

    /// <summary>
    /// The one exception to "first window": a later window at its limit blocks work however empty
    /// the first one is, so printing the first window's small figure would say "carry on" to a
    /// user who cannot.
    /// </summary>
    [Fact]
    public void ALaterWindowAtItsLimitFloodsTheFrame()
    {
        TrayGlyphFrame frame = TrayGlyphState.From([Card("CC", 5d, 100d)]).Frames[0];

        Assert.Equal(TrayFrameKind.AtLimit, frame.Kind);
        Assert.Null(frame.Digits);
        Assert.Equal(100d, frame.UsedPercent);
    }

    /// <summary>
    /// At the limit the icon floods and prints nothing: "100" is three characters, hits the condense
    /// floor, gives back height and lands smaller than any other reading. The flood is the statement.
    /// </summary>
    [Fact]
    public void AtLimitCarriesNoFigure()
    {
        TrayGlyphFrame frame = TrayGlyphState.From([Card("CC", 100d)]).Frames[0];

        Assert.Equal(TrayFrameKind.AtLimit, frame.Kind);
        Assert.Null(frame.Digits);
        Assert.Equal(100d, frame.UsedPercent);
    }

    [Fact]
    public void FrameCarriesTheProvidersSlot()
    {
        TrayGlyphState state = TrayGlyphState.From([Card("CR", 42d, traySlot: 2)]);

        Assert.Equal(2, Assert.Single(state.Frames).Slot);
    }

    [Fact]
    public void AReadingBelowTheLimitThatRoundsToAHundredSaysNinetyNine()
    {
        Assert.Equal("99", TrayGlyphState.From([Card("CC", 99.6d)]).Frames[0].Digits);
    }

    [Fact]
    public void AFailingProviderIsAFailedFrameWithNoValue()
    {
        TrayGlyphFrame frame = TrayGlyphState.From([Card("CX", 44d, state: ConnectionState.Error)]).Frames[0];

        Assert.Equal(TrayFrameKind.Failed, frame.Kind);
        Assert.Null(frame.Digits);
        Assert.Null(frame.UsedPercent);
    }

    [Fact]
    public void AProviderReportingNoPercentageWaitsRatherThanReadingZero()
    {
        TrayGlyphFrame frame = TrayGlyphState.From([Card("CR")]).Frames[0];

        Assert.Equal(TrayFrameKind.Waiting, frame.Kind);
        Assert.Null(frame.UsedPercent);
        Assert.Null(frame.Digits);
    }

    /// <summary>
    /// The widget can afford a card that says "Not installed"; sixteen pixels cannot afford a
    /// rotation slot for a tool with no quota to report. This is the one place the glyph
    /// deliberately shows less than the window.
    /// </summary>
    [Fact]
    public void AbsentProvidersGetNoFrameEvenWhenTheirCardIsVisible()
    {
        TrayGlyphState state = TrayGlyphState.From(
            [Card("CC", 61d), Card("CX", state: ConnectionState.NotInstalled), Card("CR", state: ConnectionState.Unsupported)]);

        Assert.Equal([0], state.Frames.Select(frame => frame.Slot));
    }

    [Fact]
    public void AHiddenCardContributesNoFrame()
    {
        ProviderCardViewModel hidden = Card("CX", 44d);
        hidden.IsHiddenByUser = true;

        Assert.Equal([0], TrayGlyphState.From([Card("CC", 61d), hidden]).Frames.Select(f => f.Slot));
    }

    [Fact]
    public void MatchesComparesFramesByValueSoAnIdenticalRebuildIsNotAChange()
    {
        Assert.True(TrayGlyphState.From([Card("CC", 61d)]).Matches(TrayGlyphState.From([Card("CC", 61d)])));
        Assert.False(TrayGlyphState.From([Card("CC", 61d)]).Matches(TrayGlyphState.From([Card("CC", 62d)])));
    }

    [Fact]
    public void AnEmptyStateHasNoContentSoTheStaticIconStays()
    {
        Assert.False(TrayGlyphState.Empty.HasContent);
        Assert.False(TrayGlyphState.From([]).HasContent);
    }

    private static ProviderCardViewModel Card(
        string monogram,
        double? first = null,
        double? second = null,
        double? third = null,
        ConnectionState state = ConnectionState.Connected,
        int traySlot = 0)
    {
        ProviderCardViewModel card = new(
            new ProviderDescriptor(monogram.ToLowerInvariant(), monogram, monogram, new SilentProbe(monogram), traySlot),
            colorBarsByUsage: true,
            _ => { });
        double?[] percentages = [first, second, third];
        IReadOnlyList<QuotaWindow> windows = percentages.Any(used => used is not null)
            ? [.. percentages.Select((used, index) => Window($"w{index}", index, used))]
            : [];
        card.Apply(Snapshot(state, windows), Now, Policy);
        return card;
    }

    private static ProviderSnapshot Snapshot(ConnectionState state, IReadOnlyList<QuotaWindow> windows) => new(
        ProviderName: "Provider",
        Installed: state != ConnectionState.NotInstalled,
        Version: "1.0",
        ExecutablePath: null,
        State: state,
        Mechanism: "fake",
        Tier: MechanismTier.Official,
        UpdateModel: "pull",
        Windows: windows,
        RetrievedAt: Now,
        Error: null,
        Notes: []);

    private static QuotaWindow Window(string id, int order, double? used) => new(
        Id: id, Label: id, UsedPercent: used, ResetsAt: null, WindowDuration: null,
        Order: order, IsPartial: true, Extra: new Dictionary<string, string>(), LabelIsProviderToken: true);

    private sealed class SilentProbe(string name) : IProviderProbe
    {
        public string Name => name;
        public string Mechanism => "fake";
        public MechanismTier Tier => MechanismTier.Official;

        public Task<ProviderSnapshot> ProbeAsync(CancellationToken ct) => throw new NotSupportedException();
    }
}
