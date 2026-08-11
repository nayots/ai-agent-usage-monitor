namespace AiUsageMonitor.Infrastructure.Theming;

/// <summary>
/// Places the elapsed-time marker inside a bar track. The marker is a 4px box — a 2px core with
/// a 1px surface-coloured gap either side — and the box is inset linearly so that at 0% it sits
/// flush inside the left cap and at 100% flush inside the right, never overhanging the track and
/// never merging with an end cap. The core's centre therefore shifts by at most half the marker
/// width from a mathematically exact position, a deliberate trade at a 5px bar height.
/// See docs/design/tokens.md §2.
/// </summary>
public static class ElapsedMarkerLayout
{
    /// <summary>Overall marker width in device-independent pixels: 1px gap, 2px core, 1px gap.</summary>
    public const double MarkerWidth = 4.0;

    /// <summary>Left offset of the marker box within a track of <paramref name="trackWidth"/>.</summary>
    public static double OffsetFor(double elapsedFraction, double trackWidth, double markerWidth = MarkerWidth)
    {
        double travel = Math.Max(0.0, trackWidth - markerWidth);
        return Math.Clamp(elapsedFraction, 0.0, 1.0) * travel;
    }
}
