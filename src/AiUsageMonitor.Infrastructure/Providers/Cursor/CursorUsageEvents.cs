using System.Text.Json;

namespace AiUsageMonitor.Infrastructure.Providers.Cursor;

/// <summary>Sizes for the usage-event walk. See <see cref="CursorEventAccumulator"/>.</summary>
public static class CursorUsageEvents
{
    public const int PageSize = 100;

    public const int MaxPages = 50;
}

/// <summary>
/// Totals a user's spend across pages of <c>GetFilteredUsageEvents</c>, refusing rather than
/// guessing whenever the data stops looking like one person's.
/// </summary>
public sealed class CursorEventAccumulator
{
    private string? _owner;

    public double SpentCents { get; private set; }

    public int EventCount { get; private set; }

    /// <summary>
    /// The provider's own count of matching events, or null when it did not state one. Null is
    /// deliberately not zero: "the provider says there are none" and "the provider did not say"
    /// lead to opposite conclusions about whether a total can be trusted.
    /// </summary>
    public int? TotalReported { get; private set; }

    public string? Refusal { get; private set; }

    public bool IsComplete { get; private set; }

    /// <summary>
    /// Adds one response page. Returns false when the walk must stop - either because a guard
    /// tripped (<see cref="Refusal"/> is then set) or because the page was empty.
    /// </summary>
    public bool AddPage(JsonElement page)
    {
        if (Refusal is not null)
        {
            return false;
        }

        if (page.ValueKind != JsonValueKind.Object)
        {
            Refusal = "Cursor returned a usage response this application could not read.";
            return false;
        }

        if (page.TryGetProperty("totalUsageEventsCount", out JsonElement total)
            && total.ValueKind == JsonValueKind.Number
            && total.TryGetInt32(out int reported))
        {
            TotalReported = reported;
        }

        // Every observed response carries this array, empty when there is nothing to report. Its
        // absence is a response this application cannot read, and the difference matters: an
        // absent array once meant "walk complete" with whatever had been summed so far, so a
        // response claiming 80 events while delivering none produced a confident $0.00 - a
        // fabricated reading of exactly the kind the missing-data rule exists to prevent.
        if (!page.TryGetProperty("usageEventsDisplay", out JsonElement events)
            || events.ValueKind != JsonValueKind.Array)
        {
            Refusal = "Cursor returned a usage response this application could not read.";
            return false;
        }

        int added = 0;
        foreach (JsonElement usageEvent in events.EnumerateArray())
        {
            if (usageEvent.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!AcceptOwner(usageEvent))
            {
                return false;
            }

            SpentCents += CostOf(usageEvent);
            EventCount++;
            added++;
        }

        // "Complete" means every event the provider says exists has been counted - never merely
        // "the pages stopped arriving". When the provider stated a count, that is the target. When
        // it did not, the only honest end-of-data signal left is a short page, the universal
        // pagination convention: a full page means there is probably more.
        IsComplete = TotalReported is int expected
            ? EventCount >= expected
            : added < CursorUsageEvents.PageSize;

        return added > 0;
    }

    private bool AcceptOwner(JsonElement usageEvent)
    {
        if (!usageEvent.TryGetProperty("owningUser", out JsonElement owner))
        {
            return true;
        }

        string? value = owner.ValueKind switch
        {
            JsonValueKind.String => owner.GetString(),
            JsonValueKind.Number => owner.GetRawText(),
            _ => null,
        };

        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        if (_owner is null)
        {
            _owner = value;
            return true;
        }

        if (string.Equals(_owner, value, StringComparison.Ordinal))
        {
            return true;
        }

        Refusal = "Cursor returned usage for more than one account, so this figure would not be yours.";
        return false;
    }

    private static double CostOf(JsonElement usageEvent)
    {
        if (usageEvent.TryGetProperty("chargedCents", out JsonElement charged)
            && charged.ValueKind == JsonValueKind.Number
            && charged.TryGetDouble(out double cents))
        {
            return cents;
        }

        return usageEvent.TryGetProperty("tokenUsage", out JsonElement tokens)
            && tokens.ValueKind == JsonValueKind.Object
            && tokens.TryGetProperty("totalCents", out JsonElement totalCents)
            && totalCents.ValueKind == JsonValueKind.Number
            && totalCents.TryGetDouble(out double fallback)
                ? fallback
                : 0.0;
    }
}
