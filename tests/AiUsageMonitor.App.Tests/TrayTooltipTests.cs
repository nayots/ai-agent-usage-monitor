using AiUsageMonitor.App.ViewModels;

namespace AiUsageMonitor.App.Tests;

public class TrayTooltipTests
{
    [Fact]
    public void ItNamesTheProviderThenOneLinePerWindow()
    {
        string tip = TrayTooltip.Compose("Claude Code",
            [("5 hour", "92%", "18:05"), ("7 day", "44%", "Sun")]);

        Assert.Equal("Claude Code\r\n5 hour 92% · 18:05\r\n7 day 44% · Sun", tip);
    }

    /// <summary>
    /// szTip is a fixed 128-character buffer and the marshaller throws on anything longer, so the
    /// budget is not advisory. A provider with a dozen windows must still produce a valid tip.
    /// </summary>
    [Fact]
    public void ItNeverExceedsTheShellsBuffer()
    {
        string tip = TrayTooltip.Compose(
            new string('N', 200),
            [.. Enumerable.Range(0, 12).Select(i => ($"window number {i}", "100%", "in 3 hours 22 minutes"))]);

        Assert.True(tip.Length <= TrayTooltip.MaxLength, $"the tip was {tip.Length} characters");
        Assert.Equal(127, TrayTooltip.MaxLength);
    }

    [Fact]
    public void ItDropsWholeWindowsFromTheBottomRatherThanTruncatingALine()
    {
        string tip = TrayTooltip.Compose("Claude Code",
            [.. Enumerable.Range(0, 12).Select(i => ($"window {i}", "50%", "later"))]);

        Assert.StartsWith("Claude Code\r\nwindow 0 50% · later", tip);
        Assert.DoesNotContain("window 11", tip);
        Assert.DoesNotContain("windo\r\n", tip);
    }

    [Fact]
    public void AWindowWithNoValueSaysSoRatherThanShowingNothing()
    {
        Assert.Equal("Codex\r\nweekly —", TrayTooltip.Compose("Codex", [("weekly", null, null)]));
    }

    [Fact]
    public void AProviderWithNoWindowsIsJustItsName()
    {
        Assert.Equal("Cursor", TrayTooltip.Compose("Cursor", []));
    }
}
