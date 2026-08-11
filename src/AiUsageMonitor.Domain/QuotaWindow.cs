namespace AiUsageMonitor.Domain;

/// <summary>
/// A single provider-reported quota window (e.g. a rolling usage bucket with a reset time).
/// Provider-neutral: nothing here assumes a specific window name, count, or duration.
/// </summary>
/// <param name="LabelIsProviderToken">
/// True when <paramref name="Label"/> is the provider's raw identifier because the name could not
/// be resolved to a duration. The UI must render these distinctly so a provider term is never
/// mistaken for a label this application understands (PRD §7.2 item 10).
/// </param>
public sealed record QuotaWindow(
    string Id,
    string Label,
    double? UsedPercent,
    DateTimeOffset? ResetsAt,
    TimeSpan? WindowDuration,
    int Order,
    bool IsPartial,
    IReadOnlyDictionary<string, string> Extra,
    bool LabelIsProviderToken)
{
    /// <summary>
    /// Percent remaining, derived from <see cref="UsedPercent"/>. Null when usage is unknown.
    /// Deliberately clamped to [0, 100]: this is a <em>derived</em> value, and a negative or
    /// over-100 "remaining" is meaningless regardless of what the provider reported for used.
    /// Contrast <see cref="QuotaFormatting.FormatUsedPercent"/>, which renders the provider's
    /// <see cref="UsedPercent"/> unclamped and verbatim - the two are a deliberate pair, not an
    /// inconsistency: the raw value stays visible so provider over-reporting is never silently
    /// hidden, while this derived complement stays meaningful by construction.
    /// </summary>
    public double? RemainingPercent => UsedPercent is double used ? Math.Clamp(100.0 - used, 0.0, 100.0) : null;

    /// <summary>Time remaining until this window resets, relative to <paramref name="now"/>. Null when the reset time is unknown.
    /// Clamped to <see cref="TimeSpan.Zero"/> if the reset time has already passed (stale data).</summary>
    public TimeSpan? TimeUntilReset(DateTimeOffset now)
    {
        if (ResetsAt is not DateTimeOffset resetsAt)
        {
            return null;
        }

        TimeSpan remaining = resetsAt - now;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    /// <summary>
    /// Fraction of the current window that has elapsed, in [0, 1]. Null unless BOTH <see cref="ResetsAt"/>
    /// and <see cref="WindowDuration"/> are known. Window start is computed as ResetsAt - WindowDuration.
    /// </summary>
    public double? ElapsedFraction(DateTimeOffset now)
    {
        if (ResetsAt is not DateTimeOffset resetsAt || WindowDuration is not TimeSpan duration || duration <= TimeSpan.Zero)
        {
            return null;
        }

        DateTimeOffset windowStart = resetsAt - duration;
        double elapsedTicks = (now - windowStart).Ticks;
        double fraction = elapsedTicks / duration.Ticks;
        return Math.Clamp(fraction, 0.0, 1.0);
    }
}
