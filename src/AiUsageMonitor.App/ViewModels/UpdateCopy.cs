using AiUsageMonitor.Infrastructure.Updates;

namespace AiUsageMonitor.App.ViewModels;

/// <summary>
/// Every sentence the update check puts on screen, in one place and pure so it can be asserted
/// without a window. This is user-facing copy and is held to that standard.
/// <para>
/// Versions are rendered from <see cref="ReleaseVersion"/>, never from the tag the feed returned -
/// see spec D6. Nothing here may carry a URL, a status code or an exception message.
/// </para>
/// </summary>
public static class UpdateCopy
{
    public static string StatusText(UpdateStatus status) => status.Availability switch
    {
        UpdateAvailability.UpdateAvailable => $"Version {status.Latest} is available.",
        UpdateAvailability.Current => "You are up to date.",

        // A failure explains itself; an unknown that has no reason yet simply says so. Neither may
        // claim the application is current - it does not know that.
        _ => status.FailureReason ?? "Not checked yet."
    };

    public static string LastCheckedText(DateTimeOffset? lastChecked, DateTimeOffset now)
    {
        if (lastChecked is null)
        {
            return "Not checked yet";
        }

        TimeSpan age = now - lastChecked.Value;

        if (age < TimeSpan.FromMinutes(1))
        {
            return "Checked just now";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return $"Checked {Plural((int)age.TotalMinutes, "minute")} ago";
        }

        return age < TimeSpan.FromDays(1)
            ? $"Checked {Plural((int)age.TotalHours, "hour")} ago"
            : $"Checked {Plural((int)age.TotalDays, "day")} ago";
    }

    /// <summary>
    /// The footer's version, which becomes a link when there is somewhere to go. Returns null when
    /// there is no update, so the footer keeps the plain tertiary text it already had.
    /// </summary>
    public static string? FooterText(UpdateStatus status) =>
        status.Availability == UpdateAvailability.UpdateAvailable && status.Current is not null
            ? $"v{status.Current} → {status.Latest}"
            : null;

    /// <summary>The tray item's header. Same rule: parsed numbers, never the tag.</summary>
    public static string TrayText(UpdateStatus status) => $"Update available ({status.Latest})";

    private static string Plural(int count, string unit) =>
        count == 1 ? $"1 {unit}" : $"{count} {unit}s";
}
