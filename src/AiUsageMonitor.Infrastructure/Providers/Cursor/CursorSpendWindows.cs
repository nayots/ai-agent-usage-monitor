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

    public const string SpendLabel = "Monthly spend";
    public const string PooledSpendLabel = "Team pooled spend";

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
        Build(CycleSpendId, SpendLabel, spentCents, limitCents, cycle, membershipType, "usage_events", order: 0);

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
        int order)
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
            LabelIsProviderToken: false);
    }

    private static double? Number(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out double number)
            ? number
            : null;

    private static string Usd(double cents) => (cents / 100.0).ToString("0.00", CultureInfo.InvariantCulture);
}
