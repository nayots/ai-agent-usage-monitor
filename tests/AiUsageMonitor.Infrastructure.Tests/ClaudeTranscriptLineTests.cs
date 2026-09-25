using System.Reflection;
using AiUsageMonitor.Infrastructure.Providers.Claude;

namespace AiUsageMonitor.Infrastructure.Tests;

public sealed class ClaudeTranscriptLineTests
{
    private const string SecretPrompt = "SECRET PROMPT TEXT";

    [Fact]
    public void ARealAssistantReplyMapsOnlyUsageFields()
    {
        ClaudeUsageEntry entry = Assert.IsType<ClaudeUsageEntry>(ClaudeTranscriptLine.TryParse(Line()));

        Assert.Equal("message-id|request-id", entry.DedupKey);
        Assert.Equal(new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero), entry.Timestamp);
        Assert.Equal("claude-opus-5-5", entry.Model);
        Assert.Equal(10, entry.InputTokens);
        Assert.Equal(20, entry.OutputTokens);
        Assert.Equal(30, entry.CacheReadTokens);
        Assert.Equal(15, entry.CacheWrite5mTokens);
        Assert.Equal(25, entry.CacheWrite1hTokens);
        Assert.Equal(2, entry.WebSearchRequests);
        Assert.Equal("standard", entry.Speed);
        Assert.Equal("not_available", entry.InferenceGeo);
    }

    [Fact]
    public void AFlatCacheWriteIsConservativelyPricedAsFiveMinutes()
    {
        ClaudeUsageEntry entry = Assert.IsType<ClaudeUsageEntry>(ClaudeTranscriptLine.TryParse(Line(includeCacheCreation: false)));

        Assert.Equal(40, entry.CacheWrite5mTokens);
        Assert.Equal(0, entry.CacheWrite1hTokens);
    }

    [Theory]
    [InlineData("<synthetic>", "assistant", true, true, "2026-09-25T10:00:00.000Z")]
    [InlineData("claude-opus-5-5", "user", true, true, "2026-09-25T10:00:00.000Z")]
    [InlineData("claude-opus-5-5", "assistant", false, true, "2026-09-25T10:00:00.000Z")]
    [InlineData("claude-opus-5-5", "assistant", true, false, "2026-09-25T10:00:00.000Z")]
    [InlineData("claude-opus-5-5", "assistant", true, true, "not-a-timestamp")]
    public void InvalidRequiredFieldsAreRejected(string model, string type, bool requestId, bool messageId, string timestamp)
    {
        Assert.Null(ClaudeTranscriptLine.TryParse(Line(model, type, requestId, messageId, timestamp)));
    }

    [Theory]
    [InlineData("{not json with \"usage\"")]
    [InlineData("{\"type\":\"assistant\"}")]
    public void MalformedOrPrefilteredLinesAreRejected(string line)
    {
        Assert.Null(ClaudeTranscriptLine.TryParse(line));
    }

    [Fact]
    public void TheReturnedRecordNeverRetainsTranscriptContent()
    {
        ClaudeUsageEntry entry = Assert.IsType<ClaudeUsageEntry>(ClaudeTranscriptLine.TryParse(Line()));

        foreach (PropertyInfo property in typeof(ClaudeUsageEntry).GetProperties())
        {
            Assert.DoesNotContain(SecretPrompt, property.GetValue(entry)?.ToString() ?? string.Empty, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(SecretPrompt, entry.ToString(), StringComparison.Ordinal);
    }

    private static string Line(
        string model = "claude-opus-5-5",
        string type = "assistant",
        bool requestId = true,
        bool messageId = true,
        string timestamp = "2026-09-25T10:00:00.000Z",
        bool includeCacheCreation = true)
    {
        string request = requestId ? ",\"requestId\":\"request-id\"" : string.Empty;
        string id = messageId ? "\"id\":\"message-id\"," : string.Empty;
        string cacheCreation = includeCacheCreation ? ",\"cache_creation\":{\"ephemeral_5m_input_tokens\":15,\"ephemeral_1h_input_tokens\":25}" : string.Empty;
        return $"{{\"type\":\"{type}\"{request},\"timestamp\":\"{timestamp}\",\"message\":{{{id}\"model\":\"{model}\",\"role\":\"assistant\",\"content\":[{{\"type\":\"text\",\"text\":\"{SecretPrompt}\"}}],\"usage\":{{\"input_tokens\":10,\"output_tokens\":20,\"cache_read_input_tokens\":30,\"cache_creation_input_tokens\":40{cacheCreation},\"server_tool_use\":{{\"web_search_requests\":2,\"web_fetch_requests\":1}},\"speed\":\"standard\",\"inference_geo\":\"not_available\"}}}}}}";
    }
}
