using AiUsageMonitor.Infrastructure.Updates;

namespace AiUsageMonitor.App.ViewModels;

/// <summary>
/// Whether a verdict is worth interrupting for. Pure, so the once-per-version rule is testable
/// without a tray icon.
/// <para>
/// This is the one update surface that appears unbidden, so it fires on new information only: a
/// release announces itself once, and a user who declines it is not asked again. The next release
/// is new information and does announce.
/// </para>
/// </summary>
public static class UpdateAnnouncement
{
    public const string Title = "AI Usage Monitor";

    public static bool ShouldAnnounce(UpdateStatus status, string? lastNotifiedVersion) =>
        status.Availability == UpdateAvailability.UpdateAvailable
        && status.Latest is not null
        && status.Latest.ToString() != lastNotifiedVersion;

    /// <summary>Rendered from parsed numbers, never from the tag - spec D6.</summary>
    public static string Text(UpdateStatus status) =>
        $"Version {status.Latest} is available. Open the widget's footer or the tray menu to get it.";
}
