using System.Text.Json;
using Xunit;

namespace AiUsageMonitor.Domain.Tests;

public class DuckTypedQuotaExtractorTests
{
    /// <summary>
    /// Four windows across four different key dialects, one of them ("three_hour_nimbus")
    /// a name no provider documents. Ported from the Program.cs self-test.
    /// </summary>
    private const string MultiDialectSample =
        """
        {
          "rateLimits": {
            "five_hour": { "used_percent": 42.5, "resets_at": 1786800000 },
            "seven_day": { "utilization": 73.1, "reset": "2026-08-17T12:00:00Z" },
            "seven_day_opus": { "usedPercent": 12.0, "resetsAt": 1786900000, "windowDurationMins": 10080 },
            "three_hour_nimbus": { "percent_used": 5.5, "reset_at": 1786700000 }
          },
          "meta": { "note": "self-test sample, deliberately not a real provider shape" }
        }
        """;

    internal static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    [Fact]
    public void Extract_FindsEveryWindow_AcrossAllKeyDialects()
    {
        using JsonDocument doc = JsonDocument.Parse(MultiDialectSample);

        IReadOnlyList<QuotaWindow> windows = DuckTypedQuotaExtractor.Extract(doc.RootElement);

        Assert.Equal(4, windows.Count);
    }

    [Fact]
    public void Extract_KeepsUndocumentedWindowName_WithoutCodeChange()
    {
        using JsonDocument doc = JsonDocument.Parse(MultiDialectSample);

        IReadOnlyList<QuotaWindow> windows = DuckTypedQuotaExtractor.Extract(doc.RootElement);

        Assert.Contains(windows, w => w.Id == "three_hour_nimbus");
    }

    [Fact]
    public void Extract_ExcludesContextWindowFill_BecauseItCarriesNoResetKey()
    {
        // context_window has used_percentage but no reset-ish key. It is conversation fill,
        // not subscription quota, and must never surface as a quota window. PRD SS7.3.
        string json = File.ReadAllText(FixturePath("claude-statusline-sample.json"));
        using JsonDocument doc = JsonDocument.Parse(json);

        IReadOnlyList<QuotaWindow> windows = DuckTypedQuotaExtractor.Extract(doc.RootElement);

        Assert.DoesNotContain(windows, w => w.Id == "context_window");
    }

    [Fact]
    public void Extract_ReadsBothStatusLineWindows_FromTheRecordedSample()
    {
        string json = File.ReadAllText(FixturePath("claude-statusline-sample.json"));
        using JsonDocument doc = JsonDocument.Parse(json);

        IReadOnlyList<QuotaWindow> windows = DuckTypedQuotaExtractor.Extract(doc.RootElement);

        Assert.Equal(2, windows.Count);
        Assert.Contains(windows, w => w.Id == "five_hour");
        Assert.Contains(windows, w => w.Id == "seven_day");
    }

    [Fact]
    public void Extract_InvertsRemainingPercentages_IntoUsedPercent()
    {
        // "remaining_percentage" reports the opposite quantity and must be inverted, not copied.
        using JsonDocument doc = JsonDocument.Parse(
            """{ "some_window": { "remaining_percentage": 20, "resets_at": 1786800000 } }""");

        IReadOnlyList<QuotaWindow> windows = DuckTypedQuotaExtractor.Extract(doc.RootElement);

        QuotaWindow window = Assert.Single(windows);
        Assert.Equal(80.0, window.UsedPercent);
        Assert.Equal("remaining", window.Extra["source"]);
    }
}
