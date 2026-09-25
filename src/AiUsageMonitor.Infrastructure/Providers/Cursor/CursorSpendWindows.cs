using System.Globalization;
using System.Text.Json;
using AiUsageMonitor.Domain;

namespace AiUsageMonitor.Infrastructure.Providers.Cursor;

/// <summary>Turns Cursor's money into the shape every other quota bar already has.</summary>
public static class CursorSpendWindows
{
    public const string PlanSpendId = "cursor:plan_spend";
    public const string PooledSpendId = "cursor:pooled_spend";
    public const string CycleSpendId = "cursor:cycle_spend";
    public const string OverallSpendId = "cursor:overall_spend";
    public const string IncludedUsageId = "cursor:included_usage";
    public const string OnDemandSpendId = "cursor:on_demand_spend";

    public const string SpendLabel = "Monthly spend";
    public const string PooledSpendLabel = "Team pooled spend";

    /// <summary>
    /// The usage summary's per-user buckets this application recognises, in the order they are
    /// shown. Any other bucket under <c>individualUsage</c> still becomes a window, after these,
    /// under its own provider token.
    /// </summary>
    private static readonly (string Key, string Id, string Label, bool PrintsCurrency)[] SummaryBuckets =
    [
        // Only "overall" prints a "$". Its limit was confirmed live as the user's spend limit in
        // cents - 20000 against an admin-set $200, where GetHardLimit's dollar-named field said 100
        // for the team default - so the unit is established by evidence, not assumed. The other
        // buckets have not been observed and keep bare percentages.
        ("overall", OverallSpendId, SpendLabel, true),
        ("plan", IncludedUsageId, "Included usage", false),
        ("onDemand", OnDemandSpendId, "On-demand spend", false),
    ];

    /// <summary>
    /// Windows from <c>GET /auth/usage-summary</c>, the one source that reports the limit actually
    /// enforced for this user - including an admin's per-user override, which GetHardLimit's
    /// team-default <c>perUserMonthlyLimitDollars</c> does not reflect.
    /// <para>
    /// Reads <c>individualUsage</c> only. <c>teamUsage</c> is the whole team's aggregate, and
    /// showing it here would present a team's spend as one person's.
    /// </para>
    /// </summary>
    public static IReadOnlyList<QuotaWindow> FromUsageSummary(
        JsonElement summary, CursorBillingCycle cycle, string? membershipType)
    {
        if (summary.ValueKind != JsonValueKind.Object
            || !summary.TryGetProperty("individualUsage", out JsonElement individual)
            || individual.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        string? limitType = summary.TryGetProperty("limitType", out JsonElement type) && type.ValueKind == JsonValueKind.String
            ? type.GetString()
            : null;

        List<(string Key, string Id, string Label, bool PrintsCurrency, bool IsProviderToken)> ordered =
            [.. SummaryBuckets.Select(b => (b.Key, b.Id, b.Label, b.PrintsCurrency, false))];
        foreach (JsonProperty bucket in individual.EnumerateObject())
        {
            if (!SummaryBuckets.Any(known => known.Key == bucket.Name))
            {
                ordered.Add((bucket.Name, "cursor:summary_" + bucket.Name, bucket.Name, false, true));
            }
        }

        List<QuotaWindow> windows = [];
        foreach ((string key, string id, string label, bool printsCurrency, bool isProviderToken) in ordered)
        {
            if (!individual.TryGetProperty(key, out JsonElement bucket)
                || bucket.ValueKind != JsonValueKind.Object
                || (bucket.TryGetProperty("enabled", out JsonElement enabled) && enabled.ValueKind == JsonValueKind.False)
                || Number(bucket, "used") is not double used)
            {
                continue;
            }

            // A null or zero limit is "no ceiling", never a zero one: no percentage is invented.
            double limit = Number(bucket, "limit") is double reported and > 0 ? reported : 0.0;

            windows.Add(Build(
                id,
                label,
                used,
                limit,
                cycle,
                membershipType,
                "usage_summary",
                windows.Count,
                amountText: printsCurrency ? UsdPair(used, limit) : null,
                labelIsProviderToken: isProviderToken,
                limitType: limitType));
        }

        return windows;
    }

    public static IReadOnlyList<QuotaWindow> FromPlanUsage(
        JsonElement currentPeriodUsage, CursorBillingCycle cycle, string? membershipType)
    {
        if (currentPeriodUsage.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        List<QuotaWindow> windows = [];

        if (Spend(currentPeriodUsage, "planUsage", "totalSpend", "limit") is (double spent, double limit))
        {
            windows.Add(Build(PlanSpendId, SpendLabel, spent, limit, cycle, membershipType, "plan_usage", windows.Count));
        }

        if (Spend(currentPeriodUsage, "spendLimitUsage", "pooledUsed", "pooledLimit") is (double pooledUsed, double pooledLimit))
        {
            windows.Add(Build(
                PooledSpendId, PooledSpendLabel, pooledUsed, pooledLimit, cycle, membershipType, "spend_limit_usage", windows.Count));
        }

        return windows;
    }

    public static QuotaWindow FromEventTotal(
        double spentCents, double limitCents, CursorBillingCycle cycle, string? membershipType) =>
        Build(
            CycleSpendId,
            SpendLabel,
            spentCents,
            limitCents,
            cycle,
            membershipType,
            "usage_events",
            order: 0,
            // The only path entitled to print a currency symbol. This ceiling comes from
            // GetHardLimit's perUserMonthlyLimitDollars, a field that names its own unit, so "$" is
            // the provider's statement rather than this application's assumption. The individual
            // path's planUsage figures name no currency and therefore get no symbol invented.
            amountText: UsdPair(spentCents, limitCents));

    private static (double Spent, double Limit)? Spend(
        JsonElement root, string objectName, string spentName, string limitName)
    {
        if (!root.TryGetProperty(objectName, out JsonElement usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        double? limit = Number(usage, limitName);
        double? spent = Number(usage, spentName);
        return limit is > 0 && spent is not null ? (spent.Value, limit.Value) : null;
    }

    private static QuotaWindow Build(
        string id,
        string label,
        double spentCents,
        double limitCents,
        CursorBillingCycle cycle,
        string? membershipType,
        string source,
        int order,
        string? amountText = null,
        bool labelIsProviderToken = false,
        string? limitType = null)
    {
        double? usedPercent = limitCents > 0 ? spentCents / limitCents * 100.0 : null;

        var extra = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cursor.source"] = source,
            ["cursor.spentUsd"] = Usd(spentCents),
        };

        if (limitCents > 0)
        {
            extra["cursor.limitUsd"] = Usd(limitCents);
        }

        if (!string.IsNullOrWhiteSpace(membershipType))
        {
            extra["cursor.membershipType"] = membershipType;
        }

        if (!string.IsNullOrWhiteSpace(limitType))
        {
            extra["cursor.limitType"] = limitType;
        }

        if (cycle.DurationWasDerived)
        {
            extra["duration_source"] = "derived_from_cycle_end";
        }

        return new QuotaWindow(
            Id: id,
            Label: label,
            UsedPercent: usedPercent,
            ResetsAt: cycle.End,
            WindowDuration: cycle.Duration,
            Order: order,
            IsPartial: cycle.End is null || cycle.Duration is null || usedPercent is null,
            Extra: extra,
            LabelIsProviderToken: labelIsProviderToken,
            AmountText: amountText);
    }

    private static double? Number(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out double number)
            ? number
            : null;

    private static string Usd(double cents) => (cents / 100.0).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// "$11.71 of $100" - what the row shows beneath the bar instead of leaving the reader with a
    /// bare percentage. Null when the ceiling is unknown, because "$11.71 of nothing" would state a
    /// limit nobody reported.
    /// <para>
    /// The spend keeps its cents and a whole ceiling drops its "<c>.00</c>", which is how Cursor's
    /// own dashboard writes the same pair. This line exists to be read at a glance, and "$100"
    /// reads faster than "$100.00" while losing nothing.
    /// </para>
    /// </summary>
    private static string? UsdPair(double spentCents, double limitCents) =>
        limitCents > 0 ? $"${Usd(spentCents)} of ${Whole(limitCents)}" : null;

    private static string Whole(double cents) =>
        Math.Abs(cents % 100) < 0.001
            ? (cents / 100.0).ToString("0", CultureInfo.InvariantCulture)
            : Usd(cents);
}
