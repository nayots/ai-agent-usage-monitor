namespace AiUsageMonitor.App.ViewModels;

/// <summary>
/// Which provider the one tray icon is showing at a given moment. Pure and clock-free: the caller
/// supplies elapsed time, so the whole behaviour is testable without waiting for it.
/// </summary>
public static class TrayRotation
{
    public const int MinimumDwellSeconds = 3;
    public const int MaximumDwellSeconds = 8;

    /// <summary>Offered in Settings. Zero is a fourth option there and means "do not rotate".</summary>
    public static readonly IReadOnlyList<int> DwellPresets = [3, 4, 6, 8];

    /// <summary>
    /// Which frame is up, or -1 when there is nothing to show. One frame per turn: every frame
    /// carries its own name, so there is nothing for a turn to alternate between.
    /// <para>
    /// Every provider takes its turn regardless of state. v0.5.0 gave trouble exclusive turns -
    /// including parking for good on a single troubled provider - but that made an otherwise
    /// healthy set of providers disappear from the tray for as long as the trouble lasted, which
    /// read as rotation having stopped rather than as a feature.
    /// </para>
    /// </summary>
    public static int At(IReadOnlyList<TrayGlyphFrame> frames, TimeSpan elapsed, TimeSpan? dwell)
    {
        if (frames.Count == 0)
        {
            return -1;
        }

        if (frames.Count == 1 || dwell is not TimeSpan turn || turn <= TimeSpan.Zero)
        {
            return Worst(frames);
        }

        // Floor division with a non-negative remainder, so a clock that hands back a negative
        // elapsed - a corrected system time, a resumed stopwatch - lands on a frame rather than
        // throwing or indexing backwards.
        long slot = elapsed.Ticks / turn.Ticks;

        return (int)(((slot % frames.Count) + frames.Count) % frames.Count);
    }

    /// <summary>
    /// Whether the icon should be turning at all. Everything that stops it is here rather than
    /// spread through the window: nothing to turn between, the user already looking at the whole
    /// widget, the session locked - the same condition that already pauses polling - or rotation
    /// switched off.
    /// </summary>
    public static bool ShouldTurn(
        IReadOnlyList<TrayGlyphFrame> frames,
        bool windowVisible,
        bool sessionLocked,
        TimeSpan? dwell) =>
        dwell is TimeSpan turn
        && turn > TimeSpan.Zero
        && !windowVisible
        && !sessionLocked
        && frames.Count > 1;

    private static int Worst(IReadOnlyList<TrayGlyphFrame> frames)
    {
        int worst = 0;

        for (int index = 1; index < frames.Count; index++)
        {
            if (frames[index].UsedPercent > frames[worst].UsedPercent)
            {
                worst = index;
            }
        }

        return worst;
    }
}
