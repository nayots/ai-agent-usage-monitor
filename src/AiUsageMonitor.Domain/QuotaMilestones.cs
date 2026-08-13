namespace AiUsageMonitor.Domain;

/// <summary>
/// The usage readings worth telling someone about, as a ladder of percentages.
/// <para>
/// Provider-neutral, like everything else here: the ladder is applied to whatever windows a
/// provider happens to report, and knows nothing about how long any of them lasts. A rung is a
/// property of how much of a quota is gone, not of which quota it is.
/// </para>
/// </summary>
public static class QuotaMilestones
{
    /// <summary>
    /// Every ten points to eighty, then every five. The spacing tightens where the consequences do:
    /// the difference between 30% and 40% changes nothing about what you can do next, and the
    /// difference between 90% and 95% is most of what is left.
    /// </summary>
    public static IReadOnlyList<int> Ladder { get; } = [10, 20, 30, 40, 50, 60, 70, 80, 85, 90, 95, 100];

    /// <summary>
    /// The highest rung at or below <paramref name="usedPercent"/>, or 0 when nothing has been
    /// reached — which is also the answer for a window that reported no percentage at all. Zero is
    /// "no rung", not "0% used": the two are indistinguishable here and deliberately so, because
    /// neither is worth a notification.
    /// <para>
    /// Readings above 100 answer 100 rather than nothing. A provider is free to over-report, and
    /// <see cref="QuotaWindow.UsedPercent"/> is passed through unclamped precisely so that stays
    /// visible; a ladder that fell off its top rung would go quiet exactly when a limit was most
    /// thoroughly spent.
    /// </para>
    /// </summary>
    public static int Crossed(double? usedPercent) => Crossed(usedPercent, Ladder);

    /// <inheritdoc cref="Crossed(double?)"/>
    /// <param name="ladder">
    /// The rungs to measure against, ascending. Supplied rather than assumed because the user
    /// chooses how often to be told; <see cref="Ladder"/> is only the default.
    /// </param>
    public static int Crossed(double? usedPercent, IReadOnlyList<int> ladder)
    {
        if (usedPercent is not double used)
        {
            return 0;
        }

        for (int index = ladder.Count - 1; index >= 0; index--)
        {
            if (used >= ladder[index])
            {
                return ladder[index];
            }
        }

        return 0;
    }

    /// <summary>
    /// A user-supplied ladder made safe to use. Values outside 1-100 are dropped, duplicates
    /// collapse, 100 is always present, and the result is ascending. A list with no usable value at
    /// all falls back to <see cref="Ladder"/>: a hand-edited settings file must not be able to
    /// silence alerts by accident, and the notifications switch already exists for silencing them
    /// on purpose.
    /// </summary>
    public static IReadOnlyList<int> Sanitize(IReadOnlyList<int>? thresholds)
    {
        if (thresholds is null)
        {
            return Ladder;
        }

        SortedSet<int> kept = [];

        foreach (int threshold in thresholds)
        {
            if (threshold is >= 1 and <= 100)
            {
                kept.Add(threshold);
            }
        }

        if (kept.Count == 0)
        {
            return Ladder;
        }

        kept.Add(100);
        return [.. kept];
    }
}
