namespace AiUsageMonitor.Domain;

/// <summary>Provider-neutral presentation rules for a set of quota windows.</summary>
public static class QuotaOrdering
{
    /// <summary>
    /// Returns windows in the order the provider reported them. They are never re-sorted by
    /// duration or by countdown: verification observed a seven-day window resetting sooner than a
    /// five-hour window on the same account, so any duration-derived ordering is simply wrong
    /// (PRD SS7.3). OrderBy is a stable sort, so equal Order values keep their input sequence.
    /// </summary>
    public static IReadOnlyList<QuotaWindow> InProviderOrder(IEnumerable<QuotaWindow> windows) =>
        windows.OrderBy(w => w.Order).ToList();

    /// <summary>
    /// The label to render. Falls back to the provider's raw identifier when no label was
    /// derived — never an empty string, and never an invented name (PRD SS13).
    /// </summary>
    public static string DisplayLabel(QuotaWindow window) =>
        string.IsNullOrWhiteSpace(window.Label) ? window.Id : window.Label;
}
