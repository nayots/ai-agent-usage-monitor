using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Domain;

namespace AiUsageMonitor.App.Tests;

public class ProviderNoticeTests
{
    /// <summary>
    /// The body is the reason alone. The lead only restates the title at greater length, and on a
    /// 360px card it cost two wrapped lines that said nothing the line above had not.
    /// </summary>
    [Fact]
    public void AnErrorReasonIsTheWholeBody()
    {
        ProviderNotice notice = ProviderNoticeSelector.For(Snapshot("Connection interrupted."), ConnectionState.Error)!;

        Assert.Equal("Connection interrupted.", notice.Body);
    }

    [Fact]
    public void TheLeadSurvivesInTheTooltipRatherThanBeingLost()
    {
        ProviderNotice notice = ProviderNoticeSelector.For(Snapshot("Connection interrupted."), ConnectionState.Error)!;

        Assert.Equal("The most recent attempt did not return usable data. Connection interrupted.", notice.DetailText);
    }

    [Fact]
    public void ALongErrorReasonKeepsTheCompleteComposedDetail()
    {
        string reason = new('x', 250);
        ProviderNotice notice = ProviderNoticeSelector.For(Snapshot(reason), ConnectionState.Error)!;

        Assert.Equal(new string('x', 200) + "…", notice.Body);
        Assert.Equal("The most recent attempt did not return usable data. " + reason, notice.DetailText);
        Assert.DoesNotContain("…", notice.DetailText);
    }

    /// <summary>
    /// With no reason to show, the lead is all there is — so it stays in the body rather than
    /// leaving a notice that says only that something failed. This is the one case the trim leaves
    /// exactly as it was.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void ABlankErrorReasonLeavesOnlyTheLead(string? reason)
    {
        ProviderNotice notice = ProviderNoticeSelector.For(Snapshot(reason), ConnectionState.Error)!;

        Assert.Equal("The most recent attempt did not return usable data.", notice.Body);
        Assert.Null(notice.DetailText);
    }

    [Fact]
    public void TruncationDoesNotLeaveAHalfSurrogate()
    {
        string reason = new string('x', 199) + "😀" + new string('y', 50);
        ProviderNotice notice = ProviderNoticeSelector.For(Snapshot(reason), ConnectionState.Error)!;

        Assert.Equal(new string('x', 199) + "…", notice.Body);
        Assert.False(char.IsHighSurrogate(notice.Body[^2]));
    }

    /// <summary>
    /// Unavailable composes through the same path, but only its first sentence restates the title.
    /// "There is no second source to fall back to" is the entire point of that state — it is not a
    /// restatement of anything, so the trim must not reach it and it must never end up tooltip-only.
    /// </summary>
    [Fact]
    public void AnUnavailableProviderDropsTheLeadButKeepsTheNoFallbackClause()
    {
        ProviderNotice notice = ProviderNoticeSelector.For(
            Snapshot("The usage endpoint could not be resolved."), ConnectionState.Unavailable)!;

        Assert.Equal(
            "The usage endpoint could not be resolved. There is no second source to fall back to.",
            notice.Body);
        Assert.DoesNotContain("The only source this provider has stopped", notice.Body);
        Assert.StartsWith("The only source this provider has stopped returning usable data.", notice.DetailText);
    }

    /// <summary>
    /// With no reason to trade for, the clause still has to sit after the lead rather than replace
    /// it — the no-fallback statement is an addition to the notice, never the whole of it.
    /// </summary>
    [Fact]
    public void TheNoFallbackClauseFollowsTheLeadWhenThereIsNoReason()
    {
        ProviderNotice notice = ProviderNoticeSelector.For(Snapshot(null), ConnectionState.Unavailable)!;

        Assert.Equal(
            "The only source this provider has stopped returning usable data. There is no second source to fall back to.",
            notice.Body);
        Assert.Null(notice.DetailText);
    }

    private static ProviderSnapshot Snapshot(string? error) => new(
        "Provider", true, null, null, ConnectionState.Error, "test", MechanismTier.Official,
        "pull", [], null, error, []);
}
