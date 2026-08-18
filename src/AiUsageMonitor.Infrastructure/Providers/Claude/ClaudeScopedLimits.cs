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
    /// The two vocabularies this endpoint uses for the same window. The <c>limits</c> array says
    /// <c>session</c> and <c>weekly</c> where the top-level keys say <c>five_hour</c> and
    /// <c>seven_day</c>, which is why <see cref="DuplicatesAnExistingWindow"/> matches on values
    /// first and only consults this table when the values cannot decide.
    /// <para>
    /// Deliberately tiny, and deliberately not a general "normalize the name" step. It holds only
    /// the pairs this endpoint has actually been observed to use. A kind Anthropic invents tomorrow
    /// is absent from it, is therefore never suppressed, and renders under its own label - which is
    /// the behaviour the domain requires of an unrecognised provider token.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> EquivalentTopLevelIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["session"] = "five_hour",
        ["weekly"] = "seven_day",
    };

    /// <summary>
    /// Whether this candidate is the same quota the shared extractor already found under a top-level
    /// key.
    /// <para>
    /// The primary rule is reset instant plus percentage, and it is deliberately not a name
    /// comparison: the array uses a different vocabulary from the top-level keys, so comparing names
    /// alone would miss every real duplicate.
    /// </para>
    /// <para>
    /// That rule needs a reset instant on both sides, and there is a common case where neither has
    /// one. When a window rolls over, the endpoint reports it at 0% with no <c>resets_at</c> - there
    /// is no active window left to reset - and from that moment the value comparison can no longer
    /// fire at all. Observed 2026-08-17: a five-hour window that had just reset rendered twice, once
    /// as "5 hour" and once as "session", both 0% and both with an empty resets-in column. So when
    /// the values cannot decide, fall back to the small table of vocabularies this endpoint is known
    /// to use for one window, and require the percentages to agree where both are known - a synonym
    /// whose usage genuinely differs is not the same window and must not be hidden.
    /// </para>
    /// </summary>
    private static bool DuplicatesAnExistingWindow(QuotaWindow candidate, IReadOnlyList<QuotaWindow> alreadyFound)
    {
        foreach (QuotaWindow existing in alreadyFound)
        {
            if (MatchesByValue(candidate, existing) || MatchesByKnownEquivalentId(candidate, existing))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesByValue(QuotaWindow candidate, QuotaWindow existing) =>
        candidate.ResetsAt is DateTimeOffset candidateReset
        && candidate.UsedPercent is double candidatePercent
        && existing.ResetsAt is DateTimeOffset existingReset
        && existing.UsedPercent is double existingPercent
        && existingReset.ToUnixTimeSeconds() == candidateReset.ToUnixTimeSeconds()
        && SamePercent(existingPercent, candidatePercent);

    private static bool MatchesByKnownEquivalentId(QuotaWindow candidate, QuotaWindow existing)
    {
        if (!EquivalentTopLevelIds.TryGetValue(candidate.Id, out string? equivalent)
            || !StringComparer.OrdinalIgnoreCase.Equals(equivalent, existing.Id))
        {
            return false;
        }

        // Two known names for one window still have to be reporting the same thing. If both state a
        // usage and the two disagree, they are not interchangeable and the candidate survives to be
        // seen rather than being silently folded into a number that contradicts it.
        return candidate.UsedPercent is not double candidatePercent
            || existing.UsedPercent is not double existingPercent
            || SamePercent(existingPercent, candidatePercent);
    }

    private static bool SamePercent(double left, double right) => Math.Abs(left - right) < 0.0001;
}
