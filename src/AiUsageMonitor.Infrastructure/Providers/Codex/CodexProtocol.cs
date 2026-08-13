using System.Text.Json;
using AiUsageMonitor.Infrastructure.Providers;

namespace AiUsageMonitor.Infrastructure.Providers.Codex;

/// <summary>
/// One app-server frame at a time. Responses omit <c>jsonrpc</c> and unsolicited notifications may
/// interleave before the answer, so callers must keep reading until this returns true.
/// </summary>
public static class CodexProtocol
{
    /// <summary>
    /// True when this line is the id:2 result. False for notifications, other ids and unparseable
    /// lines. Throws <see cref="ProviderMechanismException"/> on an id:2 error frame. Appends only
    /// safe observations to <paramref name="notes"/>.
    /// </summary>
    public static bool TryReadResult(string line, List<string> notes, out JsonElement result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!root.TryGetProperty("id", out JsonElement id) || id.ValueKind != JsonValueKind.Number)
            {
                if (root.TryGetProperty("method", out JsonElement method) && method.ValueKind == JsonValueKind.String)
                {
                    notes.Add($"Observed and skipped unsolicited notification: {method.GetString()}");
                }

                return false;
            }

            if (!id.TryGetInt32(out int numericId) || numericId != 2)
            {
                return false;
            }

            if (root.TryGetProperty("error", out JsonElement error))
            {
                List<string> keys = error.ValueKind == JsonValueKind.Object
                    ? error.EnumerateObject().Select(property => property.Name).ToList()
                    : [];
                if (keys.Count > 0)
                {
                    notes.Add($"Codex error keys: {string.Join(", ", keys)}.");
                }

                string message = error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("code", out JsonElement code)
                    && code.ValueKind == JsonValueKind.Number
                    && code.TryGetInt32(out int numericCode)
                    ? $"The Codex app-server rejected the rate-limit request (error {numericCode})."
                    : "The Codex app-server rejected the rate-limit request.";
                throw new ProviderMechanismException(message);
            }

            if (!root.TryGetProperty("result", out JsonElement response))
            {
                return false;
            }

            result = response.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
