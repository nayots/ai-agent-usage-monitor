namespace AiUsageMonitor.Domain;

/// <summary>
/// When a quota window is projected to run dry, and how far before its reset that falls.
/// </summary>
public sealed record PaceProjection(DateTimeOffset ExhaustsAt, TimeSpan Shortfall);

/// <summary>
/// Projects quota exhaustion from a single snapshot, bounded by PRD section 16.3.
/// </summary>
public static class QuotaPace
{
    public const double MinimumElapsedFraction = 0.10;
    public const double MinimumShortfallFraction = 0.02;

    /// <summary>
    /// The projection for <paramref name="window"/> at <paramref name="now"/>, or null when a
    /// guard rejects it.
    /// </summary>
    public static PaceProjection? For(QuotaWindow window, DateTimeOffset now)
    {
        if (window.UsedPercent is not double used || !double.IsFinite(used) || used <= 0.0 || used >= 100.0)
        {
            return null;
        }

        if (window.ResetsAt is not DateTimeOffset resetsAt
            || window.WindowDuration is not TimeSpan duration
            || duration <= TimeSpan.Zero)
        {
            return null;
        }

        if (window.ElapsedFraction(now) is not double elapsedFraction
            || elapsedFraction < MinimumElapsedFraction
            || elapsedFraction >= 1.0)
        {
            return null;
        }

        double durationTicks = duration.Ticks;
        double elapsedTicks = elapsedFraction * durationTicks;
        double ticksToFull = elapsedTicks * (100.0 / used);
        double shortfallTicks = durationTicks - ticksToFull;

        if (shortfallTicks <= MinimumShortfallFraction * durationTicks)
        {
            return null;
        }

        DateTimeOffset windowStart = resetsAt - duration;
        return new PaceProjection(
            ExhaustsAt: windowStart + TimeSpan.FromTicks((long)ticksToFull),
            Shortfall: TimeSpan.FromTicks((long)shortfallTicks));
    }
}
