using System.Text.Json;

namespace AiUsageMonitor.Infrastructure.Providers.Cursor;

/// <summary>
/// Reads the expiry claim out of a JWT's payload. This is a local read of a public, unencrypted
/// claim in a string already on disk - it is not a use of the credential's authority, and no
/// request of any kind is made. Nothing else about the token is ever read or reported.
/// </summary>
public static class CursorJwt
{
    // 2000-01-01 and 2100-01-01 as unix seconds. A value outside this range is treated as unknown
    // rather than as an expiry, for the same reason the Claude adapter does it: the only consumer
    // decides whether to SKIP a request, so a misread value could silently disable a working
    // widget. Unknown always falls through to sending the request.
    private const long MinPlausibleUnixSeconds = 946_684_800L;
    private const long MaxPlausibleUnixSeconds = 4_102_444_800L;

    /// <summary>
    /// The token's <c>exp</c> as an instant, or null when it is absent, unreadable or implausible.
    /// Never throws: every malformed input is simply "unknown".
    /// </summary>
    public static DateTimeOffset? TryReadExpiry(string? jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt))
        {
            return null;
        }

        string[] parts = jwt.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            string payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload += (payload.Length % 4) switch
            {
                2 => "==",
                3 => "=",
                _ => string.Empty,
            };

            using JsonDocument document = JsonDocument.Parse(Convert.FromBase64String(payload));
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("exp", out JsonElement exp)
                || exp.ValueKind != JsonValueKind.Number
                || !exp.TryGetInt64(out long seconds)
                || seconds < MinPlausibleUnixSeconds
                || seconds > MaxPlausibleUnixSeconds)
            {
                return null;
            }

            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }
}
