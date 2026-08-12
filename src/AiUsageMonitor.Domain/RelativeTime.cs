using System.Globalization;

namespace AiUsageMonitor.Domain;

/// <summary>
/// Renders how long ago something happened. Returns null for a null age so a caller omits the
/// element entirely rather than rendering a placeholder that reads like data (PRD §4.3).
/// </summary>
public static class RelativeTime
{
    public static string? FormatAge(TimeSpan? age)
    {
        if (age is not TimeSpan span)
        {
            return null;
        }

        // Clock skew, DST transitions and resume-from-sleep all produce future timestamps.
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        if (span.TotalMinutes < 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalSeconds}s ago");
        }

        if (span.TotalHours < 1)
        {
            return Plural((int)span.TotalMinutes, "minute");
        }

        return span.TotalDays < 1
            ? Plural((int)span.TotalHours, "hour")
            : Plural((int)span.TotalDays, "day");
    }

    private static string Plural(int count, string unit) => count == 1
        ? string.Create(CultureInfo.InvariantCulture, $"1 {unit} ago")
        : string.Create(CultureInfo.InvariantCulture, $"{count} {unit}s ago");
}
