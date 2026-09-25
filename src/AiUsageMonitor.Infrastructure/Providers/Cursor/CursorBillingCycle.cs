using System.Text.Json;

namespace AiUsageMonitor.Infrastructure.Providers.Cursor;

/// <summary>
/// The period a spend figure is measured over. <see cref="End"/> becomes the window's reset
/// instant and <see cref="Duration"/> its window duration, which is what earns the bar its
/// elapsed marker (PRD ss16) and its pace projection.
/// </summary>
/// <param name="DurationWasDerived">
/// True when <see cref="Start"/> was not reported and had to be derived from a month-boundary
/// end. The window records this in its <c>Extra</c> so the marker on the bar can always be traced
/// back to whether the provider stated the period or this application worked it out.
/// </param>
public sealed record CursorBillingCycle(
    DateTimeOffset? Start,
    DateTimeOffset? End,
    TimeSpan? Duration,
    bool DurationWasDerived)
{
    public static readonly CursorBillingCycle Unknown = new(null, null, null, false);

    /// <summary>
    /// Reads the cycle from the two payloads that describe it, preferring <c>planInfo</c>'s end -
    /// on the measured seat that was the only one of the two that was correct.
    /// </summary>
    public static CursorBillingCycle Read(JsonElement? currentPeriodUsage, JsonElement? planInfo)
    {
        JsonElement? planDetails = planInfo is JsonElement info
            && info.ValueKind == JsonValueKind.Object
            && info.TryGetProperty("planInfo", out JsonElement details)
            && details.ValueKind == JsonValueKind.Object
                ? details
                : null;

        DateTimeOffset? end = CursorInstant.Property(planDetails, "billingCycleEnd")
            ?? CursorInstant.Property(currentPeriodUsage, "billingCycleEnd");

        if (end is not DateTimeOffset cycleEnd)
        {
            return Unknown;
        }

        // A reported start is trustworthy only when it is strictly earlier than BOTH the end it
        // was reported beside AND the end actually being used.
        //
        // Judging it against the end being used alone is not enough, and the measured enterprise
        // seat is the counter-example: it returned billingCycleStart EQUAL to its own
        // billingCycleEnd - a placeholder, not a cycle - while the end actually used comes from
        // planInfo and is two weeks later. Comparing only against that later end would accept the
        // placeholder and silently report a 13-day "month" whose elapsed marker was nonsense.
        DateTimeOffset? reportedStart = CursorInstant.Property(currentPeriodUsage, "billingCycleStart");
        DateTimeOffset? ownEnd = CursorInstant.Property(currentPeriodUsage, "billingCycleEnd");

        if (reportedStart is DateTimeOffset start
            && start < cycleEnd
            && (ownEnd is not DateTimeOffset reportedEnd || start < reportedEnd))
        {
            return new CursorBillingCycle(start, cycleEnd, cycleEnd - start, DurationWasDerived: false);
        }

        // Only one derivation is permitted, and only when the end is an exact UTC month boundary:
        // then the period this figure accrued over is unambiguously the preceding month. Any
        // other end instant leaves the duration unknown rather than guessed.
        if (!IsUtcMonthBoundary(cycleEnd))
        {
            return new CursorBillingCycle(null, cycleEnd, null, DurationWasDerived: false);
        }

        DateTimeOffset derivedStart = cycleEnd.AddMonths(-1);
        return new CursorBillingCycle(derivedStart, cycleEnd, cycleEnd - derivedStart, DurationWasDerived: true);
    }

    /// <summary>
    /// The cycle the usage summary states for itself, or null when it does not state a real period.
    /// Unlike GetCurrentPeriodUsage on the measured enterprise seat - whose start equalled its end -
    /// the summary reported a proper month (2026-09-01 to 2026-10-01), so no derivation is needed;
    /// a start not strictly before its end is rejected by the same rule as <see cref="Read"/>.
    /// </summary>
    public static CursorBillingCycle? FromSummary(JsonElement summary)
    {
        DateTimeOffset? start = CursorInstant.Property(summary, "billingCycleStart");
        DateTimeOffset? end = CursorInstant.Property(summary, "billingCycleEnd");

        return start is DateTimeOffset from && end is DateTimeOffset to && from < to
            ? new CursorBillingCycle(from, to, to - from, DurationWasDerived: false)
            : null;
    }

    private static bool IsUtcMonthBoundary(DateTimeOffset instant)
    {
        DateTimeOffset utc = instant.ToUniversalTime();
        return utc.Day == 1 && utc.TimeOfDay == TimeSpan.Zero;
    }
}
