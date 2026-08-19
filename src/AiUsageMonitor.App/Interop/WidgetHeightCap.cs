namespace AiUsageMonitor.App.Interop;

/// <summary>
/// How tall the widget may grow before its provider list starts scrolling.
/// <para>
/// PRD line 240 fixed this at 520 device-independent pixels, measured against healthy cards. A
/// provider in an error state adds a notice and a retry button to its card, and two failing
/// providers together need roughly seventy pixels more than that — so the one state in which
/// reading a whole card matters most was the one state that scrolled. The cap therefore follows
/// the screen the widget is actually on.
/// </para>
/// <para>
/// Pure, so the rule is testable without a monitor — the same reason <see cref="PlacementClamp"/>
/// is separate from the window that uses it.
/// </para>
/// </summary>
public static class WidgetHeightCap
{
    /// <summary>The measured cap from PRD line 240, and the floor this rule never goes below.</summary>
    public const double Measured = 520;

    /// <summary>
    /// Kept clear of the work area's edge, so a widget grown to the cap still reads as a window
    /// sitting on the desktop rather than one wedged against it.
    /// </summary>
    public const double Margin = 48;

    /// <summary>
    /// The cap for a screen whose work area is <paramref name="workAreaHeight"/> DIPs tall. Never
    /// below <see cref="Measured"/>: a short screen keeps exactly the behaviour that shipped, and
    /// a work area the shell declined to report — zero, negative, or NaN — falls back to it rather
    /// than producing a window with no usable height.
    /// </summary>
    public static double For(double workAreaHeight) =>
        double.IsFinite(workAreaHeight)
            ? Math.Max(Measured, workAreaHeight - Margin)
            : Measured;
}
