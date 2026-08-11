using System.Globalization;

namespace AiUsageMonitor.Domain;

/// <summary>
/// Renders quota values as display strings. Every method returns null for null input:
/// a caller omits the element entirely rather than substituting a placeholder, because a
/// rendered "0%" or "--" is indistinguishable from real data at a glance (PRD §4.3).
/// </summary>
public static class QuotaFormatting
{
    /// <summary>
    /// Renders the provider's <paramref name="usedPercent"/> verbatim, deliberately unclamped:
    /// a provider reporting e.g. 150 renders as "150% used" rather than being silently masked to
    /// 100. Fabricating or hiding an out-of-range provider value is exactly what this project
    /// forbids - over-reporting should stay visible, not disappear. Contrast
    /// <see cref="QuotaWindow.RemainingPercent"/>, which clamps to [0, 100] because it is a
    /// derived value where a negative or over-100 "remaining" would be meaningless. The two are
    /// a deliberate pair, not an inconsistency.
    /// </summary>
    public static string? FormatUsedPercent(double? usedPercent) =>
        usedPercent is double v ? $"{Round(v)}% used" : null;

    public static string? FormatRemainingPercent(double? remainingPercent) =>
        remainingPercent is double v ? $"{Round(v)}% remaining" : null;

    /// <summary>
    /// Two units at the largest meaningful scale: "5d 07h", "4h 12m", "9m 30s".
    /// Negative spans clamp to zero - a stale snapshot's reset time is routinely in the past.
    /// </summary>
    public static string? FormatCountdown(TimeSpan? remaining)
    {
        if (remaining is not TimeSpan span)
        {
            return null;
        }

        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        if (span.TotalDays >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalDays}d {span.Hours:D2}h");
        }

        if (span.TotalHours >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours}h {span.Minutes:D2}m");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{span.Minutes}m {span.Seconds:D2}s");
    }

    private static string Round(double value) =>
        Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
}
