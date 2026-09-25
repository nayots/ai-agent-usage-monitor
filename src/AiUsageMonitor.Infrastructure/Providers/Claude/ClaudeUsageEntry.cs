namespace AiUsageMonitor.Infrastructure.Providers.Claude;

public sealed record ClaudeUsageEntry(
    string DedupKey,
    DateTimeOffset Timestamp,
    string Model,
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWrite5mTokens,
    long CacheWrite1hTokens,
    int WebSearchRequests,
    string? Speed,
    string? InferenceGeo)
{
    public long TotalTokens => InputTokens + OutputTokens + CacheReadTokens + CacheWrite5mTokens + CacheWrite1hTokens;
}
