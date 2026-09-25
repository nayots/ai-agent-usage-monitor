using System.Globalization;

namespace AiUsageMonitor.Infrastructure.Providers.Claude;

public static class ClaudeUsageEstimateFormat
{
    public static string AmountText(ClaudeUsagePeriod period) =>
        $"{(period.HasUnpricedTokens ? "≥" : "≈")} ${period.CostUsd.ToString("0.00", CultureInfo.InvariantCulture)} · {CompactTokens(period.TotalTokens)} tokens";

    public static string CompactTokens(long tokens)
    {
        if (tokens < 1_000)
        {
            return tokens.ToString(CultureInfo.InvariantCulture);
        }

        if (tokens >= 999_950 && tokens < 1_000_000)
        {
            return "1M";
        }

        (decimal divisor, string suffix) = tokens switch
        {
            >= 1_000_000_000 => (1_000_000_000m, "B"),
            >= 1_000_000 => (1_000_000m, "M"),
            _ => (1_000m, "K"),
        };
        return (tokens / divisor).ToString("0.#", CultureInfo.InvariantCulture) + suffix;
    }
}
