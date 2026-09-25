using System.Text.Json;

namespace AiUsageMonitor.Infrastructure.Providers.Claude;

public static class ClaudeTranscriptLine
{
    public static ClaudeUsageEntry? TryParse(string line)
    {
        if (!line.Contains("\"usage\"", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || String(root, "type") != "assistant"
                || !root.TryGetProperty("message", out JsonElement message)
                || message.ValueKind != JsonValueKind.Object
                || !message.TryGetProperty("usage", out JsonElement usage)
                || usage.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string? model = String(message, "model");
            string? messageId = String(message, "id");
            string? requestId = String(root, "requestId");
            string? timestampText = String(root, "timestamp");
            if (string.IsNullOrEmpty(model)
                || model == "<synthetic>"
                || messageId is null
                || requestId is null
                || timestampText is null
                || !DateTimeOffset.TryParse(timestampText, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out DateTimeOffset timestamp))
            {
                return null;
            }

            long cacheWrite5m = 0;
            long cacheWrite1h = 0;
            if (usage.TryGetProperty("cache_creation", out JsonElement cacheCreation))
            {
                if (cacheCreation.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                cacheWrite5m = Long(cacheCreation, "ephemeral_5m_input_tokens");
                cacheWrite1h = Long(cacheCreation, "ephemeral_1h_input_tokens");
            }
            else
            {
                cacheWrite5m = Long(usage, "cache_creation_input_tokens");
            }

            int webSearches = 0;
            if (usage.TryGetProperty("server_tool_use", out JsonElement tools))
            {
                if (tools.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                webSearches = Int(tools, "web_search_requests");
            }

            return new ClaudeUsageEntry(
                messageId + "|" + requestId,
                timestamp.ToUniversalTime(),
                model,
                Long(usage, "input_tokens"),
                Long(usage, "output_tokens"),
                Long(usage, "cache_read_input_tokens"),
                cacheWrite5m,
                cacheWrite1h,
                webSearches,
                String(usage, "speed"),
                String(usage, "inference_geo"));
        }
        catch (JsonException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static string? String(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long Long(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out JsonElement value) ? value.GetInt64() : 0;

    private static int Int(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out JsonElement value) ? value.GetInt32() : 0;
}
