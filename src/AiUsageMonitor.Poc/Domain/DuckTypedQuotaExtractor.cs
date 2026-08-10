using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiUsageMonitor.Poc.Domain;

/// <summary>
/// Walks an arbitrary <see cref="JsonElement"/> tree and duck-types <see cref="QuotaWindow"/> instances
/// out of it: any object carrying BOTH a "used/utilization percent"-ish numeric property AND a
/// "reset"-ish property is treated as a quota window, regardless of which provider produced it or
/// what the surrounding schema looks like. This lets a provider invent a brand-new window name or
/// nest it anywhere in its response without requiring a code change here.
/// </summary>
public static class DuckTypedQuotaExtractor
{
    // Listed in priority order; the first matching key present on a candidate object wins.
    // "remaining"-flavoured spellings carry a true IsRemaining flag: their value is inverted
    // (UsedPercent = 100 - value) because they report the OPPOSITE quantity. This was found via
    // real Claude Code statusLine payloads, which use "used_percentage" / "remaining_percentage"
    // rather than any of the originally-assumed spellings - proof the duck-typing key list itself
    // must stay open to extension.
    private static readonly (string Key, bool IsRemaining)[] PercentKeys =
    [
        ("utilization", false),
        ("usedPercent", false),
        ("used_percent", false),
        ("percentUsed", false),
        ("percent_used", false),
        ("used", false),
        ("used_percentage", false),
        ("usedPercentage", false),
        ("remaining_percentage", true),
        ("remainingPercentage", true),
    ];

    private static readonly string[] ResetKeys =
    [
        "resetsAt", "resets_at", "reset_at", "resetTime", "reset"
    ];

    // Not part of the mandated key list, but harmless to pick up when a provider happens to use the
    // one concrete spelling we've verified live (Codex's "windowDurationMins"). Absence never blocks
    // window detection - it only leaves WindowDuration null and IsPartial true.
    private static readonly string[] DurationKeys =
    [
        "windowDurationMins"
    ];

    private static readonly Dictionary<string, string> NumberWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["one"] = "1",
        ["two"] = "2",
        ["three"] = "3",
        ["four"] = "4",
        ["five"] = "5",
        ["six"] = "6",
        ["seven"] = "7",
        ["eight"] = "8",
        ["nine"] = "9",
        ["ten"] = "10",
        ["eleven"] = "11",
        ["twelve"] = "12",
    };

    // Used only to INFER a window's duration from its own id when no explicit duration field was
    // found (see TryInferDurationFromName below). Never used to invent a window - only to fill in
    // WindowDuration on a window that duck typing already qualified via percent + reset keys.
    private static readonly Dictionary<string, Func<double, TimeSpan>> DurationUnitFactories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["minute"] = TimeSpan.FromMinutes,
        ["minutes"] = TimeSpan.FromMinutes,
        ["hour"] = TimeSpan.FromHours,
        ["hours"] = TimeSpan.FromHours,
        ["day"] = TimeSpan.FromDays,
        ["days"] = TimeSpan.FromDays,
        ["week"] = n => TimeSpan.FromDays(n * 7),
        ["weeks"] = n => TimeSpan.FromDays(n * 7),
        ["month"] = n => TimeSpan.FromDays(n * 30),
        ["months"] = n => TimeSpan.FromDays(n * 30),
    };

    private static readonly Regex CamelBoundary = new("(?<=[a-z0-9])(?=[A-Z])", RegexOptions.Compiled);

    public static IReadOnlyList<QuotaWindow> Extract(JsonElement root)
    {
        var results = new List<QuotaWindow>();
        int order = 0;
        Walk(root, propertyName: null, results, ref order);
        return results;
    }

    private static void Walk(JsonElement element, string? propertyName, List<QuotaWindow> results, ref int order)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                string candidateId = propertyName ?? "root";
                if (TryExtractWindow(element, candidateId, order, out QuotaWindow? window))
                {
                    results.Add(window);
                    order++;
                }

                foreach (JsonProperty prop in element.EnumerateObject())
                {
                    Walk(prop.Value, prop.Name, results, ref order);
                }

                break;

            case JsonValueKind.Array:
                int index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    string itemName = propertyName is null ? $"item[{index}]" : $"{propertyName}[{index}]";
                    Walk(item, itemName, results, ref order);
                    index++;
                }

                break;
        }
    }

    private static bool TryExtractWindow(JsonElement obj, string id, int order, out QuotaWindow window)
    {
        var props = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty p in obj.EnumerateObject())
        {
            props[p.Name] = p.Value;
        }

        if (!TryFindPercent(props, out double usedPercent, out string? percentKey, out bool wasRemaining))
        {
            window = null!;
            return false;
        }

        if (!TryFindResetProperty(props, ResetKeys, out DateTimeOffset? resetsAt, out string? resetKey))
        {
            window = null!;
            return false;
        }

        TimeSpan? windowDuration = TryFindDuration(props, out string? durationKey);

        // An explicit duration field always wins. Only when the payload didn't carry one do we fall
        // back to inferring it from the window's own id (e.g. "five_hour" -> 5h, "seven_day_opus" ->
        // 7d). If the id doesn't parse as "<number-word|digits>_<unit>[...]", WindowDuration stays
        // null - never a guess.
        bool inferredFromName = false;
        if (windowDuration is null)
        {
            TimeSpan? inferred = TryInferDurationFromName(id);
            if (inferred is not null)
            {
                windowDuration = inferred;
                inferredFromName = true;
            }
        }

        bool isPartial = resetsAt is null || windowDuration is null;

        var extra = new Dictionary<string, string>
        {
            ["duckTyped.percentKey"] = percentKey ?? string.Empty,
            ["duckTyped.resetKey"] = resetKey ?? string.Empty,
        };
        if (durationKey is not null)
        {
            extra["duckTyped.durationKey"] = durationKey;
        }
        if (inferredFromName)
        {
            extra["duration_source"] = "inferred_from_name";
        }
        if (wasRemaining)
        {
            // Auditable marker: UsedPercent above was derived as 100 - rawValue, not read directly.
            extra["source"] = "remaining";
        }

        window = new QuotaWindow(
            Id: id,
            Label: Humanize(id),
            UsedPercent: usedPercent,
            ResetsAt: resetsAt,
            WindowDuration: windowDuration,
            Order: order,
            IsPartial: isPartial,
            Extra: extra);
        return true;
    }

    private static bool TryFindPercent(
        Dictionary<string, JsonElement> props,
        out double usedPercent,
        out string? matchedKey,
        out bool wasRemaining)
    {
        foreach ((string key, bool isRemaining) in PercentKeys)
        {
            if (props.TryGetValue(key, out JsonElement el)
                && el.ValueKind == JsonValueKind.Number
                && el.TryGetDouble(out double v))
            {
                usedPercent = isRemaining ? 100.0 - v : v;
                matchedKey = key;
                wasRemaining = isRemaining;
                return true;
            }
        }

        usedPercent = 0;
        matchedKey = null;
        wasRemaining = false;
        return false;
    }

    private static bool TryFindResetProperty(
        Dictionary<string, JsonElement> props,
        string[] keysInPriority,
        out DateTimeOffset? resetsAt,
        out string? matchedKey)
    {
        foreach (string key in keysInPriority)
        {
            if (props.TryGetValue(key, out JsonElement el))
            {
                matchedKey = key;
                resetsAt = ParseReset(el);
                return true; // property presence is enough to qualify as a window, even if the value fails to parse
            }
        }

        resetsAt = null;
        matchedKey = null;
        return false;
    }

    private static TimeSpan? TryFindDuration(Dictionary<string, JsonElement> props, out string? matchedKey)
    {
        foreach (string key in DurationKeys)
        {
            if (props.TryGetValue(key, out JsonElement el)
                && el.ValueKind == JsonValueKind.Number
                && el.TryGetDouble(out double mins))
            {
                matchedKey = key;
                return TimeSpan.FromMinutes(mins);
            }
        }

        matchedKey = null;
        return null;
    }

    /// <summary>
    /// Infers a window's duration purely from its own id (e.g. "five_hour" -&gt; 5 hours,
    /// "seven_day_opus" -&gt; 7 days) when - and only when - the id parses cleanly as a recognised
    /// number-word or digit run followed by a recognised time unit. Reuses the exact same tokenising
    /// / number-word rules as <see cref="Humanize"/>, so trailing provider-invented tokens (e.g. the
    /// "opus" in "seven_day_opus") never block the match. Returns null - never a guess - for any id
    /// that doesn't parse this way (e.g. "nimbus_quill", "codex:primary").
    /// </summary>
    private static TimeSpan? TryInferDurationFromName(string id)
    {
        string label = Humanize(id);
        string[] parts = label.Split(' ', 2);
        if (parts.Length < 2)
        {
            return null;
        }

        if (!double.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out double n))
        {
            return null; // first token wasn't a recognised number-word (e.g. "codex primary")
        }

        string unitToken = parts[1];
        int parenIndex = unitToken.IndexOf('(');
        if (parenIndex >= 0)
        {
            unitToken = unitToken[..parenIndex].Trim();
        }

        return DurationUnitFactories.TryGetValue(unitToken, out Func<double, TimeSpan>? factory) ? factory(n) : null;
    }

    private static DateTimeOffset? ParseReset(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Number when el.TryGetInt64(out long unixSeconds):
                return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            case JsonValueKind.Number when el.TryGetDouble(out double d):
                return DateTimeOffset.FromUnixTimeSeconds((long)d);
            case JsonValueKind.String
                when DateTimeOffset.TryParse(
                    el.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset dto):
                return dto;
            default:
                return null;
        }
    }

    /// <summary>
    /// Humanises a raw provider identifier into a display label without inventing meaning:
    /// splits on '_', '-', and camelCase boundaries, maps recognised English number-words
    /// (one..twelve) to digits, and keeps every other token verbatim. Two leading tokens are
    /// joined as "N unit" (e.g. "5 hour"); any further tokens are appended parenthetically
    /// (e.g. "7 day (opus)") so unrecognised, provider-invented tokens are preserved rather
    /// than dropped.
    /// </summary>
    public static string Humanize(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return id;
        }

        List<string> rawTokens = SplitTokens(id);
        if (rawTokens.Count == 0)
        {
            return id;
        }

        List<string> tokens = rawTokens
            .Select(t => NumberWords.TryGetValue(t, out string? digit) ? digit : t)
            .ToList();

        if (tokens.Count == 1)
        {
            return tokens[0];
        }

        string main = $"{tokens[0]} {tokens[1]}";
        if (tokens.Count > 2)
        {
            string extra = string.Join(' ', tokens.Skip(2));
            return $"{main} ({extra})";
        }

        return main;
    }

    private static List<string> SplitTokens(string id)
    {
        string[] parts = id.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries);
        var tokens = new List<string>();
        foreach (string part in parts)
        {
            string[] camelParts = CamelBoundary.Split(part);
            foreach (string t in camelParts)
            {
                if (t.Length > 0)
                {
                    tokens.Add(t);
                }
            }
        }

        return tokens;
    }
}
