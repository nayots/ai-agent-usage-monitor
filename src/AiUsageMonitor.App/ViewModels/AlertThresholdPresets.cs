using AiUsageMonitor.Domain;

namespace AiUsageMonitor.App.ViewModels;

/// <summary>
/// A named ladder the settings window can offer as one radio button. The id is what the choice
/// control carries; the thresholds are what gets written to settings.
/// </summary>
public sealed record AlertThresholdPreset(int Id, string Label, IReadOnlyList<int> Thresholds);

/// <summary>
/// The four ways of answering "how often do you want to hear from this", from every rung to only
/// the one that matters. Presentation, not domain: the domain has one default ladder and a
/// sanitizer, and knows nothing about which subsets a settings window chooses to offer.
/// </summary>
public static class AlertThresholdPresets
{
    /// <summary>
    /// The first entry is <see cref="QuotaMilestones.Ladder"/> itself rather than a second copy of
    /// the same numbers, so changing the default ladder changes this list with it.
    /// </summary>
    public static IReadOnlyList<AlertThresholdPreset> All { get; } =
    [
        new(0, "Every milestone", QuotaMilestones.Ladder),
        new(1, "80, 90 and 100%", [80, 90, 100]),
        new(2, "90 and 100%", [90, 100]),
        new(3, "100% only", [100])
    ];

    /// <summary>
    /// The preset matching <paramref name="thresholds"/>, or -1 when none does. Compared after
    /// sanitizing both sides, so a hand-edited list that only differs by order or by a duplicate
    /// still finds its preset.
    /// </summary>
    public static int IdFor(IReadOnlyList<int> thresholds)
    {
        IReadOnlyList<int> sanitized = QuotaMilestones.Sanitize(thresholds);

        foreach (AlertThresholdPreset preset in All)
        {
            if (QuotaMilestones.Sanitize(preset.Thresholds).SequenceEqual(sanitized))
            {
                return preset.Id;
            }
        }

        return -1;
    }

    /// <summary>
    /// How a ladder matching no preset is named in the list. It is shown rather than hidden so a
    /// list typed into the settings file by hand stays visible and stays selected.
    /// </summary>
    public static string CustomLabel(IReadOnlyList<int> thresholds) =>
        $"Custom ({string.Join(", ", QuotaMilestones.Sanitize(thresholds))}%)";
}
