using System.Text.Json;
using Xunit;

namespace AiUsageMonitor.Domain.Tests;

public class DuckTypedQuotaExtractorTests
{
    /// <summary>
    /// Five windows across four different key dialects, one of them ("three_hour_nimbus")
    /// a name no provider documents. Ported from the Program.cs self-test. "three_hour_extra"
    /// is the one window whose explicit windowDurationMins (600 = 10h) deliberately disagrees
    /// with what its name would infer (3h) - "seven_day_opus" below carries an explicit
    /// windowDurationMins too, but 10080 minutes happens to equal its name-inferred 7 days, so
    /// it cannot prove explicit-beats-inferred precedence on its own.
    /// </summary>
    private const string MultiDialectSample =
        """
        {
          "rateLimits": {
            "five_hour": { "used_percent": 42.5, "resets_at": 1786800000 },
            "seven_day": { "utilization": 73.1, "reset": "2026-08-17T12:00:00Z" },
            "seven_day_opus": { "usedPercent": 12.0, "resetsAt": 1786900000, "windowDurationMins": 10080 },
            "three_hour_nimbus": { "percent_used": 5.5, "reset_at": 1786700000 },
            "three_hour_extra": { "used_percent": 8.0, "resets_at": 1786750000, "windowDurationMins": 600 }
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

        Assert.Equal(5, windows.Count);
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

    // The tests above only ever assert window counts and ids - never a parsed value. Under
    // mutation testing all 62 pre-existing tests survived each of: FromHours -> FromMinutes,
    // FromUnixTimeSeconds -> FromUnixTimeMilliseconds, TryFindDuration neutered to return null,
    // and the ISO-8601 branch of ParseReset short-circuited to null. The extractor qualifies a
    // window on key PRESENCE; these assertions pin the parsed VALUES instead.

    [Fact]
    public void Extract_ParsesUnixSecondsReset_ToTheExactInstant()
    {
        // Catches FromUnixTimeSeconds -> FromUnixTimeMilliseconds (resets off by ~55,000 years).
        using JsonDocument doc = JsonDocument.Parse(MultiDialectSample);

        IReadOnlyList<QuotaWindow> windows = DuckTypedQuotaExtractor.Extract(doc.RootElement);

        QuotaWindow fiveHour = windows.Single(w => w.Id == "five_hour");
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1786800000), fiveHour.ResetsAt);
    }

    [Fact]
    public void Extract_ParsesIso8601Reset_ToTheExactInstant()
    {
        // Catches the ISO-8601 branch of ParseReset being short-circuited to null, which would
        // stop the live Claude usage endpoint's resets from parsing at all.
        using JsonDocument doc = JsonDocument.Parse(MultiDialectSample);

        IReadOnlyList<QuotaWindow> windows = DuckTypedQuotaExtractor.Extract(doc.RootElement);

        QuotaWindow sevenDay = windows.Single(w => w.Id == "seven_day");
        Assert.Equal(new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero), sevenDay.ResetsAt);
    }

    [Fact]
    public void Extract_InfersFiveHourWindowDuration_FromTheName()
    {
        // Catches ["hour"] = TimeSpan.FromHours -> FromMinutes (every elapsed-time marker
        // wrong by 60x).
        using JsonDocument doc = JsonDocument.Parse(MultiDialectSample);

        IReadOnlyList<QuotaWindow> windows = DuckTypedQuotaExtractor.Extract(doc.RootElement);

        QuotaWindow fiveHour = windows.Single(w => w.Id == "five_hour");
        Assert.Equal(TimeSpan.FromHours(5), fiveHour.WindowDuration);
    }

    [Fact]
    public void Extract_InfersSevenDayWindowDuration_FromTheName()
    {
        using JsonDocument doc = JsonDocument.Parse(MultiDialectSample);

        IReadOnlyList<QuotaWindow> windows = DuckTypedQuotaExtractor.Extract(doc.RootElement);

        QuotaWindow sevenDay = windows.Single(w => w.Id == "seven_day");
        Assert.Equal(TimeSpan.FromDays(7), sevenDay.WindowDuration);
    }

    [Fact]
    public void Extract_PrefersExplicitWindowDuration_OverNameInference()
    {
        // "three_hour_extra" would infer 3 hours (180 min) from its name, but its explicit
        // windowDurationMins (600 = 10h) must win. Catches TryFindDuration being neutered to
        // return null, which would silently drop Codex's verified windowDurationMins field.
        using JsonDocument doc = JsonDocument.Parse(MultiDialectSample);

        IReadOnlyList<QuotaWindow> windows = DuckTypedQuotaExtractor.Extract(doc.RootElement);

        QuotaWindow window = windows.Single(w => w.Id == "three_hour_extra");
        Assert.Equal(TimeSpan.FromMinutes(600), window.WindowDuration);
    }
}
