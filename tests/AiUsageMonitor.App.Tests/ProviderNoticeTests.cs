using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Domain;

namespace AiUsageMonitor.App.Tests;

public class ProviderNoticeTests
{
    [Fact]
    public void ErrorReasonsAreBoundedAndBlankReasonsAreOmitted()
    {
        string reason = new('x', 500);
        ProviderSnapshot longError = Snapshot(reason);

        ProviderNotice notice = ProviderNoticeSelector.For(longError, ConnectionState.Error)!;

        Assert.True(notice.Body.Length <= "The most recent attempt did not return usable data.".Length + 1 + 201);
        Assert.EndsWith("…", notice.Body);
        Assert.Equal("The most recent attempt did not return usable data.", ProviderNoticeSelector.For(Snapshot(" "), ConnectionState.Error)!.Body);
    }

    private static ProviderSnapshot Snapshot(string? error) => new(
        "Provider", true, null, null, ConnectionState.Error, "test", MechanismTier.Official,
        "pull", [], null, error, []);
}
