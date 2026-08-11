using System.Text.Json;
using Xunit;

namespace AiUsageMonitor.Domain.Tests;

public class QuotaLabelTests
{
    private static QuotaWindow ExtractSingle(string id)
    {
        using JsonDocument doc = JsonDocument.Parse(
            $$"""{ "{{id}}": { "used_percent": 10, "resets_at": 1786800000 } }""");

        return Assert.Single(DuckTypedQuotaExtractor.Extract(doc.RootElement));
    }

    [Fact]
    public void UnparseableName_KeepsTheProviderToken_Verbatim()
    {
        // Verified live on 2026-08-10. "Nimbus quill" would read as a feature name this
        // application recognises. It does not, and must not pretend to. PRD SS7.2 item 10.
        QuotaWindow window = ExtractSingle("nimbus_quill");

        Assert.Equal("nimbus_quill", window.Label);
        Assert.True(window.LabelIsProviderToken);
    }

    [Fact]
    public void SingleTokenName_KeepsTheProviderToken_Verbatim()
    {
        QuotaWindow window = ExtractSingle("codex");

        Assert.Equal("codex", window.Label);
        Assert.True(window.LabelIsProviderToken);
    }

    [Fact]
    public void ParseableName_IsHumanized()
    {
        QuotaWindow window = ExtractSingle("five_hour");

        Assert.Equal("5 hour", window.Label);
        Assert.False(window.LabelIsProviderToken);
    }

    [Fact]
    public void ParseableName_PreservesTrailingProviderTokens()
    {
        // The "opus" token must survive - never dropped, never reinterpreted.
        QuotaWindow window = ExtractSingle("seven_day_opus");

        Assert.Equal("7 day (opus)", window.Label);
        Assert.False(window.LabelIsProviderToken);
    }

    [Fact]
    public void TheRawIdentifierIsAlwaysPreserved_RegardlessOfLabelling()
    {
        // PRD SS7.2 item 10: the provider-supplied identifier stays available for every
        // window, so a tooltip and diagnostics can always show it.
        Assert.Equal("nimbus_quill", ExtractSingle("nimbus_quill").Id);
        Assert.Equal("five_hour", ExtractSingle("five_hour").Id);
    }
}
