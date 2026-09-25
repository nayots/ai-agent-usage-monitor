using System.Globalization;

namespace AiUsageMonitor.Infrastructure.Providers.Claude;

public static class ClaudeUsageEstimateFormat
{
    public static string AmountText(ClaudeUsagePeriod period) =>
        $"{(period.HasUnpricedTokens ? "≥" : "≈")} ${period.CostUsd.ToString("0.00", CultureInfo.InvariantCulture)} · {CompactTokens(period.TotalTokens)} tokens";

    private static readonly (decimal Divisor, string Suffix)[] Units =
    [
        (1_000m, "K"),
        (1_000_000m, "M"),
        (1_000_000_000m, "B"),
    ];

    /// <summary>
    /// One decimal, trailing ".0" dropped. A value that rounds up to 1000 of one unit moves to the
    /// next - 999,950 is "1M", never "1000K" - which is checked after rounding, for every unit.
    /// </summary>
    public static string CompactTokens(long tokens)
    {
        if (tokens < 1_000)
        {
            return tokens.ToString(CultureInfo.InvariantCulture);
        }

        for (int i = 0; i < Units.Length; i++)
        {
            (decimal divisor, string suffix) = Units[i];
            decimal rounded = Math.Round(tokens / divisor, 1, MidpointRounding.AwayFromZero);
            if (rounded < 1_000m || i == Units.Length - 1)
            {
                return rounded.ToString("0.#", CultureInfo.InvariantCulture) + suffix;
            }
        }

        throw new InvalidOperationException("unreachable");
    }
}
