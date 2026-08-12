using AiUsageMonitor.Domain;

namespace AiUsageMonitor.App.ViewModels;

/// <summary>
/// What a card says when it has no quota rows to show, or has rows that cannot be trusted.
/// <paramref name="ActionText"/> is null unless there is something this build can actually do —
/// a button that does nothing is worse than no button.
/// </summary>
public sealed record ProviderNotice(string Title, string Body, bool IsAlert, string? ActionText);

public static class ProviderNoticeSelector
{
    /// <summary>
    /// Takes no clock. A notice says what is wrong, never how old anything is — the header line
    /// owns age — so there is deliberately nothing here for a timestamp to feed.
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

            ConnectionState.Unavailable => new ProviderNotice(
                "Usage can no longer be read",
                Compose(
                    "The only source this provider has stopped returning usable data. There is no second source to fall back to.",
                    snapshot.Error),
                IsAlert: true,
                ActionText: "Retry now"),

            ConnectionState.Error => new ProviderNotice(
                "The last read failed",
                Compose("The most recent attempt did not return usable data.", snapshot.Error),
                IsAlert: true,
                ActionText: "Retry now"),

            ConnectionState.Connected or ConnectionState.Stale when snapshot.Windows.Count == 0 => new ProviderNotice(
                "No quota windows reported",
                "The provider is installed, authenticated and reachable, and returned no windows. This is neither an error nor zero usage.",
                IsAlert: false,
                ActionText: null),

            _ => null
        };
    }

    /// <summary>
    /// Appends only what exists. A missing reason is simply left out — never replaced by "unknown",
    /// which reads as a value.
    /// <para>
    /// This deliberately does NOT restate how old the data is. The card header already carries
    /// "Updated {age}" from the same <c>RetrievedAt</c>, immediately above, and under exactly the
    /// same condition — so every sentence added here was the second copy of a line already on
    /// screen. Age has one home on a card, and it is the header.
    /// </para>
    /// </summary>
    private static string Compose(string lead, string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? lead : lead + " " + reason;
}
