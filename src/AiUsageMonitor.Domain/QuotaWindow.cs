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
/// <param name="AmountText">
/// What this window's consumption is in the provider's OWN unit, already composed for display —
/// for example <c>"$11.71 of $100"</c> — or null when the provider measures only in percent.
/// <para>
/// This exists because a percentage is a lossy rendering of a window whose limit is not a
/// percentage. A monthly spend ceiling is money; "12%" tells a reader nothing about whether they
/// can afford this afternoon's work, and the bar beside it already conveys the fraction. Windows
/// that are natively percentages — every rolling quota window — leave this null and lose nothing.
/// </para>
/// <para>
/// It is a <em>string the adapter composed</em>, deliberately, and never a number plus a unit this
/// layer would have to format. Only the adapter knows whether its payload stated a currency at
/// all: Codex reports its spend limit as currency strings it has already formatted, and Cursor's
/// enterprise ceiling arrives in a field that names dollars outright, while the same provider's
/// individual figures carry no currency marker and therefore get no symbol invented for them.
/// Deciding that here would mean guessing on behalf of a provider that did not say.
/// </para>
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
    bool LabelIsProviderToken,
    string? AmountText = null)
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
