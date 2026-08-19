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
                "The only source this provider has stopped returning usable data.",
                snapshot.Error,
                keep: "There is no second source to fall back to."),

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

    /// <summary>
    /// <paramref name="lead"/> is the sentence that restates <paramref name="title"/> and may be
    /// traded away for the provider's own reason. <paramref name="keep"/> is the opposite: a clause
    /// the title does not carry, which stays on the card whatever else is shown. Unavailable needs
    /// the distinction - "there is no second source" is the whole point of that state, and it must
    /// never end up in a tooltip.
    /// </summary>
    private static ProviderNotice CreateNotice(string title, string lead, string? reason, string? keep = null)
    {
        (string body, string? detailText) = Compose(lead, reason, keep);
        return new ProviderNotice(title, body, IsAlert: true, ActionText: "Retry now", DetailText: detailText);
    }

    /// <summary>
    /// Shows only what the card does not already say. The title states that the read failed; the
    /// lead restates that at greater length, so when the provider gave a reason, the reason is the
    /// body and the full statement moves to the tooltip. A missing reason is simply left out -
    /// never replaced by "unknown", which reads as a value - and then the lead is the body, because
    /// a notice has to say something.
    /// <para>
    /// This is worth two wrapped lines per failing card at 360px, which is why it is a rule and not
    /// a preference: two providers failing at once used to overflow the widget's height cap on the
    /// one screen where reading a whole card matters most.
    /// </para>
    /// <para>
    /// This deliberately does NOT restate how old the data is. The card header already carries
    /// "Updated {age}" from the same <c>RetrievedAt</c>, immediately above, and under exactly the
    /// same condition - so every sentence added here was the second copy of a line already on
    /// screen. Age has one home on a card, and it is the header. The lead now follows the same
    /// rule against the title.
    /// </para>
    /// </summary>
    private static (string Body, string? DetailText) Compose(string lead, string? reason, string? keep)
    {
        string WithKeep(string text) => keep is null ? text : $"{text} {keep}";

        if (string.IsNullOrWhiteSpace(reason))
        {
            return (WithKeep(lead), null);
        }

        string full = WithKeep($"{lead} {reason}");

        const int maxReasonLength = 200;
        if (reason.Length <= maxReasonLength)
        {
            return (WithKeep(reason), full);
        }

        int boundedLength = char.IsHighSurrogate(reason[maxReasonLength - 1])
            ? maxReasonLength - 1
            : maxReasonLength;
        return (WithKeep($"{reason[..boundedLength]}…"), full);
    }
}
