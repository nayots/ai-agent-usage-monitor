using System.Linq;

namespace AiUsageMonitor.App.ViewModels;

/// <param name="Index">Which frame is up, or -1 when there is nothing to show.</param>
/// <param name="ShowsName">
/// True while the turn is opening on the provider's monogram. Only ever true for a
/// <see cref="TrayFrameKind.Reading"/> frame; the other kinds carry their name regardless, so the
/// renderer takes this as "also show the name", never as "hide it".
/// </param>
public readonly record struct TrayGlyphSlide(int Index, bool ShowsName);

/// <summary>
/// Which provider the one tray icon is showing at a given moment. Pure and clock-free: the caller
/// supplies elapsed time, so the whole behaviour is testable without waiting for it.
/// </summary>
public static class TrayRotation
{
    /// <summary>How long each turn opens on the provider's monogram before the number.</summary>
    public static readonly TimeSpan NameDwell = TimeSpan.FromSeconds(1);

    public const int MinimumDwellSeconds = 3;
    public const int MaximumDwellSeconds = 8;

    /// <summary>Offered in Settings. Zero is a fourth option there and means "do not rotate".</summary>
    public static readonly IReadOnlyList<int> DwellPresets = [3, 4, 6, 8];

    /// <summary>
    /// The frames eligible for a turn: the troubled ones if there are any, otherwise all of them.
    /// Waiting is deliberately not trouble - a provider that has not answered yet has nothing to
    /// report and must not seize the icon from one that has.
    /// </summary>
    private static IReadOnlyList<int> Eligible(IReadOnlyList<TrayGlyphFrame> frames)
    {
        List<int> troubled = [.. Enumerable.Range(0, frames.Count)
            .Where(index => frames[index].Kind is TrayFrameKind.AtLimit or TrayFrameKind.Failed)];

        return troubled.Count > 0 ? troubled : [.. Enumerable.Range(0, frames.Count)];
    }

    public static TrayGlyphSlide At(IReadOnlyList<TrayGlyphFrame> frames, TimeSpan elapsed, TimeSpan? dwell)
    {
        IReadOnlyList<int> eligible = Eligible(frames);

        if (eligible.Count == 0)
        {
            return new TrayGlyphSlide(-1, false);
        }

        if (eligible.Count == 1 || dwell is not TimeSpan turn || turn <= TimeSpan.Zero)
        {
            return new TrayGlyphSlide(Worst(frames, eligible), false);
        }

        // Floor division with a non-negative remainder, so a clock that hands back a negative
        // elapsed - a corrected system time, a resumed stopwatch - lands on a frame rather than
        // throwing or indexing backwards.
        long slot = elapsed.Ticks / turn.Ticks;
        int position = (int)(((slot % eligible.Count) + eligible.Count) % eligible.Count);
        TimeSpan within = TimeSpan.FromTicks(elapsed.Ticks - (slot * turn.Ticks));

        return new TrayGlyphSlide(eligible[position], within < NameDwell);
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
        && Eligible(frames).Count > 1;

    private static int Worst(IReadOnlyList<TrayGlyphFrame> frames, IReadOnlyList<int> eligible)
    {
        int worst = eligible[0];

        foreach (int index in eligible)
        {
            if (frames[index].UsedPercent > frames[worst].UsedPercent)
            {
                worst = index;
            }
        }

        return worst;
    }
}
