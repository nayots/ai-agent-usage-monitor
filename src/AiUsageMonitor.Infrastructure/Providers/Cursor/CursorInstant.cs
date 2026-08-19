using System.Globalization;
using System.Text.Json;

namespace AiUsageMonitor.Infrastructure.Providers.Cursor;

/// <summary>
/// Reads an instant out of a Cursor payload, in either of the two dialects the field appears in.
/// <para>
/// The observed API returns unix milliseconds as a JSON STRING ("1788220800000"); the provider's
/// public documentation describes the same fields as RFC3339. Both are accepted, plus a bare
/// number, because betting on one would break the moment the other turned up. Anything else is
/// null - and null means the window simply has no reset time, never a fabricated one.
/// </para>
/// </summary>
public static class CursorInstant
{
    // 2000-01-01 and 2100-01-01 as unix milliseconds. Guards against a sentinel zero, a
    // seconds-based timestamp read as milliseconds, or a repurposed field.
    private const long MinPlausibleUnixMs = 946_684_800_000L;
    private const long MaxPlausibleUnixMs = 4_102_444_800_000L;

    public static DateTimeOffset? Parse(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetInt64(out long milliseconds) ? FromUnixMilliseconds(milliseconds) : null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? raw = element.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long fromString))
        {
            return FromUnixMilliseconds(fromString);
        }

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    /// <summary>The named property of <paramref name="parent"/>, or null when it is absent.</summary>
    public static DateTimeOffset? Property(JsonElement? parent, string propertyName) =>
        parent is JsonElement element
        && element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out JsonElement value)
            ? Parse(value)
            : null;

    private static DateTimeOffset? FromUnixMilliseconds(long milliseconds) =>
        milliseconds < MinPlausibleUnixMs || milliseconds > MaxPlausibleUnixMs
            ? null
            : DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
}
