namespace AiUsageMonitor.Infrastructure.Providers.Claude;

/// <summary>
/// The non-secret fields that sit beside the OAuth token in Claude Code's own credential file.
///
/// This type exists to be safe to hold, log, and format. It carries NO credential material and no
/// property may ever be added that does - which is the whole point of it being a separate type
/// rather than extra properties on a "credentials" record. A record's compiler-generated
/// <c>ToString</c> prints every property it has, so a record that held the access token could dump
/// it into a note or a diagnostic through nothing more than string interpolation. This one cannot,
/// by construction: the token stays a bare local string in
/// <see cref="ClaudeOAuthUsageProbe"/> and never enters a container of any kind.
/// </summary>
/// <param name="AccessTokenExpiresAt">
/// When the stored access token stops being accepted, or null when the file did not state it in a
/// form that could be trusted. Null means "unknown", never "expired" - see
/// <see cref="ClaudeOAuthUsageProbe"/>, which may only skip a request it is confident would fail.
/// </param>
/// <param name="RefreshTokenExpiresAt">
/// When the refresh token stops being usable, or null when unknown. This is what separates a
/// sign-in that Claude Code will silently repair on its next run from one the user has to redo by
/// hand.
/// </param>
/// <param name="SubscriptionType">The account's plan, as the provider names it. Rendered verbatim.</param>
/// <param name="RateLimitTier">The account's rate-limit tier, as the provider names it.</param>
public sealed record ClaudeAccountMetadata(
    DateTimeOffset? AccessTokenExpiresAt,
    DateTimeOffset? RefreshTokenExpiresAt,
    string? SubscriptionType,
    string? RateLimitTier)
{
    /// <summary>Nothing known - the shape returned whenever the credential file could not be read.</summary>
    public static ClaudeAccountMetadata Empty { get; } = new(null, null, null, null);
}
