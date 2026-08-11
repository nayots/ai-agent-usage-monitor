namespace AiUsageMonitor.Domain;

/// <summary>How current a provider snapshot is, relative to a configured threshold.</summary>
public enum FreshnessState
{
    /// <summary>No successful retrieval has happened yet. Distinct from stale: there is no data to age.</summary>
    Unknown,

    /// <summary>Retrieved within the threshold.</summary>
    Fresh,

    /// <summary>Older than the threshold. Values may still be shown, but must be marked (PRD §18).</summary>
    Stale
}

/// <summary>
/// Decides whether a snapshot has aged past its threshold. Thresholds are configurable per
/// provider integration and conservative by default (PRD §14).
/// </summary>
public sealed record FreshnessPolicy(TimeSpan StaleAfter)
{
    public static FreshnessPolicy Default { get; } = new(TimeSpan.FromMinutes(5));

    public FreshnessState Evaluate(DateTimeOffset? retrievedAt, DateTimeOffset now)
    {
        if (retrievedAt is not DateTimeOffset at)
        {
            return FreshnessState.Unknown;
        }

        TimeSpan age = now - at;

        // Clock skew, DST transitions and resume-from-sleep all produce future timestamps.
        // Treat them as fresh rather than reporting a negative age as stale.
        if (age < TimeSpan.Zero)
        {
            return FreshnessState.Fresh;
        }

        return age > StaleAfter ? FreshnessState.Stale : FreshnessState.Fresh;
    }
}

/// <summary>Provider-neutral rules for deriving the state a card should present.</summary>
public static class ConnectionStateRules
{
    /// <summary>
    /// Ages a <see cref="ConnectionState.Connected"/> provider into <see cref="ConnectionState.Stale"/>.
    /// Every other state is returned untouched: age must never mask a real failure, because
    /// presenting an aged Error as Stale implies recoverable data exists when it does not.
    /// </summary>
    public static ConnectionState ApplyFreshness(ConnectionState state, FreshnessState freshness) =>
        state == ConnectionState.Connected && freshness == FreshnessState.Stale
            ? ConnectionState.Stale
            : state;
}
