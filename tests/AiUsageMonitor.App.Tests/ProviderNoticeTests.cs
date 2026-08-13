using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Domain;

namespace AiUsageMonitor.App.Tests;

public class ProviderNoticeTests
{
    [Fact]
    public void AShortErrorReasonNeedsNoDetailTooltip()
    {
        ProviderNotice notice = ProviderNoticeSelector.For(Snapshot("Connection interrupted."), ConnectionState.Error)!;

        Assert.Equal("The most recent attempt did not return usable data. Connection interrupted.", notice.Body);
        Assert.Null(notice.DetailText);
    }

    [Fact]
    public void ALongErrorReasonKeepsTheCompleteComposedDetail()
    {
        string reason = new('x', 250);
        ProviderNotice notice = ProviderNoticeSelector.For(Snapshot(reason), ConnectionState.Error)!;

        Assert.Equal("The most recent attempt did not return usable data. " + new string('x', 200) + "…", notice.Body);
        Assert.Equal("The most recent attempt did not return usable data. " + reason, notice.DetailText);
        Assert.DoesNotContain("…", notice.DetailText);
    }

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

        Assert.Equal("The most recent attempt did not return usable data. " + new string('x', 199) + "…", notice.Body);
        Assert.False(char.IsHighSurrogate(notice.Body[^2]));
    }

    private static ProviderSnapshot Snapshot(string? error) => new(
        "Provider", true, null, null, ConnectionState.Error, "test", MechanismTier.Official,
        "pull", [], null, error, []);
}
