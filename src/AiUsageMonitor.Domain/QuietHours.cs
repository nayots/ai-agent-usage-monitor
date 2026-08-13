namespace AiUsageMonitor.Domain;

/// <summary>
/// A daily window during which non-critical notifications are held back. Minutes from local
/// midnight rather than a <see cref="TimeOnly"/> so the settings file stays a plain readable
/// number, and so a value from a hand-edited file can be normalised rather than refused.
/// </summary>
public sealed record QuietHours(bool Enabled, int StartMinutes, int EndMinutes)
{
    private const int MinutesPerDay = 24 * 60;

    /// <summary>The default schedule, switched off. 22:00 to 07:00 when someone turns it on.</summary>
    public static QuietHours Off { get; } = new(false, 1320, 420);

    /// <summary>
    /// Whether <paramref name="localTime"/> falls inside the window. Half-open at both ends, so a
    /// window ending at 07:00 is over at 07:00 rather than a minute later.
    /// <para>
    /// A window whose start equals its end answers false. A zero-length window reads as "not
    /// configured yet" far more often than "silence me forever", and the notifications switch
    /// already does the latter deliberately.
    /// </para>
    /// </summary>
    public bool Contains(TimeOnly localTime)
    {
        if (!Enabled)
        {
            return false;
        }

        int start = Normalize(StartMinutes);
        int end = Normalize(EndMinutes);

        if (start == end)
        {
            return false;
        }

        int minute = localTime.Hour * 60 + localTime.Minute;

        // A window that runs past midnight is two intervals, not one. Nearly every quiet-hours
        // schedule anyone actually wants is this shape, so it is the case worth getting right.
        return start < end
            ? minute >= start && minute < end
            : minute >= start || minute < end;
    }

    /// <summary>
    /// Any integer folded into 0-1439. A positive modulo rather than <c>%</c> alone, which keeps
    /// the sign of its left operand and would turn a hand-edited -60 into a window that never ends.
    /// </summary>
    private static int Normalize(int minutes) => ((minutes % MinutesPerDay) + MinutesPerDay) % MinutesPerDay;
}
