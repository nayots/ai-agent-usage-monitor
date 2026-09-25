namespace AiUsageMonitor.Infrastructure.Providers.Claude;

public static class ClaudeApiPricing
{
    public static readonly DateOnly PricesAsOf = new(2026, 9, 25);

    private sealed record Price(decimal Input, decimal Output, decimal CacheReadMultiplier, bool UsGeoEligible, decimal? FastInput = null, decimal? FastOutput = null);

    private static readonly IReadOnlyDictionary<string, Price> Prices = new Dictionary<string, Price>
    {
        ["claude-fable-5-1"] = new(10m, 50m, 0.025m, true),
        ["claude-mythos-5-1"] = new(10m, 50m, 0.025m, true),
        ["claude-fable-5"] = new(10m, 50m, 0.1m, true),
        ["claude-mythos-5"] = new(10m, 50m, 0.1m, true),
        ["claude-opus-5-5"] = new(4m, 20m, 0.05m, true, 8m, 40m),
        ["claude-opus-5"] = new(5m, 25m, 0.1m, true, 10m, 50m),
        ["claude-opus-4-8"] = new(5m, 25m, 0.1m, true, 10m, 50m),
        ["claude-opus-4-7"] = new(5m, 25m, 0.1m, true),
        ["claude-opus-4-6"] = new(5m, 25m, 0.1m, true),
        ["claude-opus-4-5"] = new(5m, 25m, 0.1m, false),
        ["claude-opus-4-1"] = new(15m, 75m, 0.1m, false),
        ["claude-opus-4"] = new(15m, 75m, 0.1m, false),
        ["claude-sonnet-5"] = new(2m, 10m, 0.1m, true),
        ["claude-sonnet-4-6"] = new(3m, 15m, 0.1m, true),
        ["claude-sonnet-4-5"] = new(3m, 15m, 0.1m, false),
        ["claude-sonnet-4"] = new(3m, 15m, 0.1m, false),
        ["claude-haiku-4-5"] = new(1m, 5m, 0.1m, false),
        ["claude-3-5-haiku"] = new(0.80m, 4m, 0.1m, false),
    };

    public static bool IsPriced(string modelId) => FindPrice(modelId) is not null;

    public static decimal? CostUsd(ClaudeUsageEntry entry)
    {
        Price? price = FindPrice(entry.Model);
        if (price is null)
        {
            return null;
        }

        decimal inputRate = entry.Speed == "fast" && price.FastInput is decimal fastInput ? fastInput : price.Input;
        decimal outputRate = entry.Speed == "fast" && price.FastOutput is decimal fastOutput ? fastOutput : price.Output;
        decimal geoMultiplier = entry.InferenceGeo == "us" && price.UsGeoEligible ? 1.1m : 1m;
        decimal tokenCost =
            (entry.InputTokens * inputRate)
            + (entry.OutputTokens * outputRate)
            + (entry.CacheReadTokens * inputRate * price.CacheReadMultiplier)
            + (entry.CacheWrite5mTokens * inputRate * 1.25m)
            + (entry.CacheWrite1hTokens * inputRate * 2m);

        return (tokenCost / 1_000_000m * geoMultiplier) + (entry.WebSearchRequests * 10m / 1_000m);
    }

    private static Price? FindPrice(string modelId)
    {
        return Prices
            .Where(pair => Matches(pair.Key, modelId))
            .OrderByDescending(pair => pair.Key.Length)
            .Select(pair => pair.Value)
            .FirstOrDefault();
    }

    private static bool Matches(string prefix, string modelId)
    {
        if (!modelId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (modelId.Length == prefix.Length)
        {
            return true;
        }

        if (modelId[prefix.Length] == '[')
        {
            return true;
        }

        return modelId.Length == prefix.Length + 9
            && modelId[prefix.Length] == '-'
            && modelId.AsSpan(prefix.Length + 1).ToString().All(char.IsAsciiDigit);
    }
}
