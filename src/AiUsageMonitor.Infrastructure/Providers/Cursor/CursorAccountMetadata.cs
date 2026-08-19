namespace AiUsageMonitor.Infrastructure.Providers.Cursor;

/// <summary>
/// The non-secret facts stored beside Cursor's access token, returned separately FROM that token
/// and never together with it.
/// <para>
/// The separation is the point, and it is the same rule the Claude adapter follows. A record's
/// generated <c>ToString</c> prints every property it has, so a "credentials" record holding the
/// token could leak it into a log or a note through nothing more than string interpolation. This
/// type structurally cannot hold a credential, so printing it is always safe.
/// </para>
/// <para>
/// <see cref="TeamId"/> is a request parameter only. It is never rendered, logged, or placed in a
/// note or a window's <c>Extra</c>.
/// </para>
/// </summary>
public sealed record CursorAccountMetadata(
    string? MembershipType,
    long? TeamId,
    DateTimeOffset? AccessTokenExpiresAt)
{
    public static readonly CursorAccountMetadata Empty = new(null, null, null);
}
