using AiUsageMonitor.App.Interop;
using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Domain;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Theming;

namespace AiUsageMonitor.App.Tests;

public class TrayGlyphStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly FreshnessPolicy Policy = new(TimeSpan.FromMinutes(5));

    private sealed class SilentProbe(string name) : IProviderProbe
    {
        public string Name => name;
        public string Mechanism => "fake";
        public MechanismTier Tier => MechanismTier.Official;

        public Task<ProviderSnapshot> ProbeAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private static ProviderCardViewModel Card(string name = "Claude Code", bool colorBarsByUsage = true) =>
        new(new ProviderDescriptor(name, name[..2], new SilentProbe(name)), colorBarsByUsage, _ => { });

    private static ProviderSnapshot Snapshot(
        ConnectionState state = ConnectionState.Connected,
        IReadOnlyList<QuotaWindow>? windows = null,
        DateTimeOffset? retrievedAt = null) => new(
            ProviderName: "Claude Code",
            Installed: state != ConnectionState.NotInstalled,
            Version: "2.1.227",
            ExecutablePath: null,
            State: state,
            Mechanism: "Anthropic OAuth usage endpoint (UNOFFICIAL/undocumented)",
            Tier: MechanismTier.Unofficial,
            UpdateModel: "pull (poll)",
            Windows: windows ?? [],
            RetrievedAt: retrievedAt,
            Error: null,
            Notes: []);

    private static QuotaWindow Window(string id, int order, double? used) => new(
        Id: id, Label: id, UsedPercent: used, ResetsAt: null, WindowDuration: null,
        Order: order, IsPartial: true, Extra: new Dictionary<string, string>(), LabelIsProviderToken: true);

    /// <summary>A card carrying the given windows, current as of <see cref="Now"/>.</summary>
    private static ProviderCardViewModel Connected(string name, params double?[] percentages)
    {
        ProviderCardViewModel card = Card(name);
        card.Apply(
            Snapshot(windows: [.. percentages.Select((used, index) => Window($"w{index}", index, used))], retrievedAt: Now),
            Now,
            Policy);

        return card;
    }

    [Fact]
    public void TheDigitsAreTheWorstPrimaryWindowAcrossEveryProvider()
    {
        TrayGlyphState state = TrayGlyphState.From([Connected("Claude Code", 31), Connected("Codex", 82, 4)]);

        Assert.Equal("82", state.Digits);
        Assert.False(state.DigitsAreStale);
    }

    /// <summary>
    /// Claude Code reports its five-hour window first and its weekly one second, and the weekly is
    /// usually further along. One number on a sixteen-pixel icon should be the limit that governs
    /// what you can do next, so it comes from the window the provider put first and not from the
    /// largest figure on the card.
    /// </summary>
    [Fact]
    public void TheDigitsComeFromThePrimaryWindowEvenWhenALaterOneIsWorse()
    {
        TrayGlyphState state = TrayGlyphState.From([Connected("Claude Code", 51, 64)]);

        Assert.Equal("51", state.Digits);
        Assert.Equal(2, state.Bars.Count);
    }

    /// <summary>
    /// Deferring to the next window down is precisely how the weekly reading reached the icon, so a
    /// primary window that reports nothing yields no digits at all.
    /// </summary>
    [Fact]
    public void APrimaryWindowWithNoReadingDoesNotDeferToTheNextOne()
    {
        TrayGlyphState state = TrayGlyphState.From([Connected("Claude Code", null, 64)]);

        Assert.Null(state.Digits);
        Assert.Equal(2, state.Bars.Count);
    }

    [Fact]
    public void TheDigitsRoundTheSameWayTheCardDoes()
    {
        // The number on the glyph and the number on the row are read side by side; disagreeing by
        // a point looks like a bug in whichever one the user trusts less.
        Assert.Equal("43", TrayGlyphState.From([Connected("Claude Code", 42.5)]).Digits);
    }

    [Fact]
    public void NoPercentageAnywhereMeansNoDigitsRatherThanAZero()
    {
        TrayGlyphState state = TrayGlyphState.From([Connected("Claude Code", null, null)]);

        Assert.Null(state.Digits);
        Assert.Equal(2, state.Bars.Count);
        Assert.All(state.Bars, bar => Assert.Null(bar.UsedPercent));
    }

    [Fact]
    public void AReadingAtTheLimitShowsOneHundredAndKeepsTheAlertOverlay()
    {
        TrayGlyphState state = TrayGlyphState.From([Connected("Claude Code", 100, 12)]);

        Assert.Equal("100", state.Digits);
        Assert.Equal(TrayOverlay.Alert, state.Overlay);
        Assert.Equal(QuotaBarFill.Exhausted, state.Bars[0].Fill);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(7.4, "7")]
    [InlineData(83.5, "84")]
    [InlineData(100.0001, "100")]
    [InlineData(120, "100")]
    [InlineData(99.6, "99")]
    [InlineData(99.4, "99")]
    public void GlyphDigitsAreHonestAtTheLimit(double used, string expected) =>
        Assert.Equal(expected, TrayGlyphState.From([Connected("Claude Code", used)]).Digits);

    [Fact]
    public void AFailingProviderOutranksAnExhaustedWindow()
    {
        ProviderCardViewModel failing = Card("Codex");
        failing.Apply(Snapshot(ConnectionState.Error), Now, Policy);

        TrayGlyphState state = TrayGlyphState.From([Connected("Claude Code", 100), failing]);

        Assert.Equal(TrayOverlay.Error, state.Overlay);
    }

    [Fact]
    public void AProviderHiddenByTheFilterContributesNothingToTheGlyph()
    {
        ProviderCardViewModel absent = Card("Codex");
        absent.ShowWhenUnavailable = false;
        absent.Apply(Snapshot(ConnectionState.NotInstalled), Now, Policy);

        TrayGlyphState state = TrayGlyphState.From([Connected("Claude Code", 55), absent]);

        Assert.Single(state.Bars);
        Assert.Equal(TrayOverlay.None, state.Overlay);
    }

    [Fact]
    public void TheFirstBarOfEachLaterProviderStartsAGroupAndTheVeryFirstDoesNot()
    {
        TrayGlyphState state = TrayGlyphState.From([Connected("Claude Code", 10, 20), Connected("Codex", 30, 40)]);

        Assert.Equal([false, false, true, false], state.Bars.Select(bar => bar.StartsGroup));
    }

    [Fact]
    public void AStaleCardGreysItsBarsAndTheNumberTakenFromThem()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(windows: [Window("w0", 0, 63)], retrievedAt: Now.AddMinutes(-30)), Now, Policy);

        TrayGlyphState state = TrayGlyphState.From([card]);

        Assert.Equal(ConnectionState.Stale, card.State);
        Assert.Equal(QuotaBarFill.Stale, state.Bars[0].Fill);
        Assert.True(state.DigitsAreStale);
        Assert.Equal("63", state.Digits);
    }

    [Fact]
    public void BarToneFollowsTheSameSettingTheWidgetsOwnBarsFollow()
    {
        ProviderCardViewModel plain = Card(colorBarsByUsage: false);
        plain.Apply(Snapshot(windows: [Window("w0", 0, 92)], retrievedAt: Now), Now, Policy);

        Assert.Equal(QuotaBarFill.High, TrayGlyphState.From([Connected("Claude Code", 92)]).Bars[0].Fill);
        Assert.Equal(QuotaBarFill.Accent, TrayGlyphState.From([plain]).Bars[0].Fill);
    }

    [Fact]
    public void AGlyphWithNothingToSayIsNotDrawnButOneWithOnlyAnErrorIs()
    {
        ProviderCardViewModel waiting = Card();
        waiting.Apply(Snapshot(ConnectionState.Waiting), Now, Policy);

        ProviderCardViewModel failing = Card("Codex");
        failing.Apply(Snapshot(ConnectionState.Unavailable), Now, Policy);

        Assert.False(TrayGlyphState.Empty.HasContent);
        Assert.False(TrayGlyphState.From([waiting]).HasContent);
        Assert.True(TrayGlyphState.From([failing]).HasContent);
    }

    [Fact]
    public void MatchesSeesAChangedPercentageSoTheIconIsOnlyRedrawnWhenItWouldDiffer()
    {
        TrayGlyphState before = TrayGlyphState.From([Connected("Claude Code", 31)]);

        Assert.True(before.Matches(TrayGlyphState.From([Connected("Claude Code", 31)])));
        Assert.False(before.Matches(TrayGlyphState.From([Connected("Claude Code", 32)])));
        Assert.False(before.Matches(TrayGlyphState.From([Connected("Claude Code", 31), Connected("Codex", 5)])));
    }
}
