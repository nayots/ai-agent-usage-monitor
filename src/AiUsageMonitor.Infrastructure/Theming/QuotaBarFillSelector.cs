namespace AiUsageMonitor.Infrastructure.Theming;

/// <summary>Which brush role a quota bar's fill takes. Maps to one token per role.</summary>
public enum QuotaBarFill
{
    Accent,
    High,
    Exhausted,
    Stale
}

/// <summary>
/// Bar tone by usage band, per PRD §16.1. Exactly three bands, no interpolation: a
/// continuously varying colour would communicate a rate, which the PRD forbids.
/// </summary>
public static class QuotaBarFillSelector
{
    /// <summary>Start of the high band, in percent used.</summary>
    public const double HighBandStartPercent = 75.0;

    /// <summary>Start of the exhausted band, in percent used.</summary>
    public const double ExhaustedBandStartPercent = 100.0;

    public static QuotaBarFill Select(double? usedPercent, bool limitReached, bool colorBarsByUsage, bool isStale)
    {
        // Stale outranks everything: a de-emphasised value must not also carry a band claim.
        if (isStale)
        {
            return QuotaBarFill.Stale;
        }

        if (limitReached || usedPercent >= ExhaustedBandStartPercent)
        {
            return QuotaBarFill.Exhausted;
        }

        // No value means no band. Absence is never treated as 0% or as any usage level.
        if (usedPercent is not double used || !colorBarsByUsage)
        {
            return QuotaBarFill.Accent;
        }

        return used >= HighBandStartPercent ? QuotaBarFill.High : QuotaBarFill.Accent;
    }
}
