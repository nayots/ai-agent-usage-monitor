using System.Globalization;
using System.Text.Json;
using AiUsageMonitor.Domain;

namespace AiUsageMonitor.Infrastructure.Providers.Claude;

/// <summary>Normalizes Claude's provider-specific <c>limits</c> array before shared quota handling.</summary>
public static class ClaudeScopedLimits
{
    public static IReadOnlyList<QuotaWindow> Normalize(JsonElement root, IReadOnlyList<QuotaWindow> alreadyFound)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("limits", out JsonElement limits)
            || limits.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var candidates = new List<QuotaWindow>();
        int order = alreadyFound.Count;

        foreach (JsonElement entry in limits.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string? kind = StringProperty(entry, "kind");
            string? scope = StringProperty(entry, "scope");
            string? id = IdFor(kind, scope);
            if (id is null
                || !entry.TryGetProperty("percent", out JsonElement percent)
                || percent.ValueKind != JsonValueKind.Number
                || !percent.TryGetDouble(out double usedPercent))
            {
                continue;
            }

            bool labelIsProviderToken = !DuckTypedQuotaExtractor.TryHumanize(id, out string label);
            DateTimeOffset? resetsAt = ParseReset(entry);
            TimeSpan? duration = null;
            var extra = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["claude.kind"] = kind!,
                ["claude.source"] = "limits",
            };

            string? group = StringProperty(entry, "group");
            if (group is not null)
            {
                extra["claude.group"] = group;
            }

            if (scope is not null)
            {
                extra["claude.scope"] = scope;
            }

            if (entry.TryGetProperty("is_active", out JsonElement isActive)
                && (isActive.ValueKind == JsonValueKind.True || isActive.ValueKind == JsonValueKind.False))
            {
                extra["claude.is_active"] = isActive.GetBoolean() ? "true" : "false";
            }

            candidates.Add(new QuotaWindow(
                Id: id,
                Label: label,
                UsedPercent: usedPercent,
                ResetsAt: resetsAt,
                WindowDuration: duration,
                Order: order++,
                IsPartial: resetsAt is null || duration is null,
                Extra: extra,
                LabelIsProviderToken: labelIsProviderToken));
        }

        var survivors = new List<QuotaWindow>();
        int survivorOrder = alreadyFound.Count;
        foreach (QuotaWindow candidate in candidates)
        {
            if (!DuplicatesAnExistingWindow(candidate, alreadyFound))
            {
                survivors.Add(candidate with { Order = survivorOrder++ });
            }
        }

        return survivors;
    }

    /// <summary>
    /// A stable window id built from the entry's own declared identity: its kind, plus the model scope
    /// when the entry is scoped to one. Array position is deliberately not part of it - "limits[2]"
    /// changes the moment the provider reorders the array, which would silently rename a window
    /// between two refreshes of the same account.
    /// </summary>
    private static string? IdFor(string? kind, string? scope)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(scope) ? kind.Trim() : $"{kind.Trim()}_{scope.Trim()}";
    }

    private static string? StringProperty(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static DateTimeOffset? ParseReset(JsonElement entry)
    {
        if (!entry.TryGetProperty("resets_at", out JsonElement reset)
            || reset.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            reset.GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTimeOffset resetsAt)
            ? resetsAt
            : null;
    }

    /// <summary>
    /// Whether this candidate is the same quota the shared extractor already found under a top-level
    /// key. Matching is on reset instant plus percentage, NOT on name: the array uses a different
    /// vocabulary from the top-level keys - "session" for "five_hour", "weekly" for "seven_day" - so a
    /// name comparison would miss every real duplicate. A candidate with an unknown reset or unknown
    /// usage can never be proven to be a duplicate and is therefore kept.
    /// </summary>
    private static bool DuplicatesAnExistingWindow(QuotaWindow candidate, IReadOnlyList<QuotaWindow> alreadyFound)
    {
        if (candidate.ResetsAt is not DateTimeOffset candidateReset || candidate.UsedPercent is not double candidatePercent)
        {
            return false;
        }

        foreach (QuotaWindow existing in alreadyFound)
        {
            if (existing.ResetsAt is DateTimeOffset existingReset
                && existing.UsedPercent is double existingPercent
                && existingReset.ToUnixTimeSeconds() == candidateReset.ToUnixTimeSeconds()
                && Math.Abs(existingPercent - candidatePercent) < 0.0001)
            {
                return true;
            }
        }

        return false;
    }
}
