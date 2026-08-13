using AiUsageMonitor.Domain;

namespace AiUsageMonitor.App.ViewModels;

/// <summary>
/// What a card says when it has no quota rows to show, or has rows that cannot be trusted.
/// <paramref name="ActionText"/> is null unless there is something this build can actually do -
/// a button that does nothing is worse than no button.
/// </summary>
public sealed record ProviderNotice(string Title, string Body, bool IsAlert, string? ActionText, string? DetailText = null);

public static class ProviderNoticeSelector
{
    /// <summary>
    /// Takes no clock. A notice says what is wrong, never how old anything is - the header line
    /// owns age - so there is deliberately nothing here for a timestamp to feed.
    /// </summary>
    public static ProviderNotice? For(ProviderSnapshot snapshot, ConnectionState state)
    {
        return state switch
        {
            ConnectionState.NotInstalled => new ProviderNotice(
                "Not installed on this machine",
                "The card stays in place. Nothing is shown in place of usage.",
                IsAlert: false,
                ActionText: "Check again"),

            ConnectionState.Unsupported => new ProviderNotice(
                "Usage is not available from this version",
                "The installed version does not expose usage through a mechanism this application can verify.",
                IsAlert: false,
                ActionText: null),

            ConnectionState.Waiting => new ProviderNotice(
                "Waiting for the first usage report",
                "The provider is installed. Nothing has been reported yet.",
                IsAlert: false,
                ActionText: null),

            ConnectionState.Unavailable => CreateNotice(
                "Usage can no longer be read",
                "The only source this provider has stopped returning usable data. There is no second source to fall back to.",
                snapshot.Error),

            ConnectionState.Error => CreateNotice(
                "The last read failed",
                "The most recent attempt did not return usable data.",
                snapshot.Error),

            ConnectionState.Connected or ConnectionState.Stale when snapshot.Windows.Count == 0 => new ProviderNotice(
                "No quota windows reported",
                "The provider is installed, authenticated and reachable, and returned no windows. This is neither an error nor zero usage.",
                IsAlert: false,
                ActionText: null),

            _ => null
        };
    }

    private static ProviderNotice CreateNotice(string title, string lead, string? reason)
    {
        (string body, string? detailText) = Compose(lead, reason);
        return new ProviderNotice(title, body, IsAlert: true, ActionText: "Retry now", DetailText: detailText);
    }

    /// <summary>
    /// Appends only what exists. A missing reason is simply left out - never replaced by "unknown",
    /// which reads as a value.
    /// <para>
    /// This deliberately does NOT restate how old the data is. The card header already carries
    /// "Updated {age}" from the same <c>RetrievedAt</c>, immediately above, and under exactly the
    /// same condition - so every sentence added here was the second copy of a line already on
    /// screen. Age has one home on a card, and it is the header.
    /// </para>
    /// </summary>
    private static (string Body, string? DetailText) Compose(string lead, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return (lead, null);
        }

        const int maxReasonLength = 200;
        if (reason.Length <= maxReasonLength)
        {
            return ($"{lead} {reason}", null);
        }

        int boundedLength = char.IsHighSurrogate(reason[maxReasonLength - 1])
            ? maxReasonLength - 1
            : maxReasonLength;
        return ($"{lead} {reason[..boundedLength]}…", $"{lead} {reason}");
    }
}
