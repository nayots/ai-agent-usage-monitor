namespace AiUsageMonitor.Domain;

/// <summary>
/// A provider-specific probe capable of discovering installation state and, when possible,
/// retrieving quota data through a verified official local mechanism.
/// </summary>
public interface IProviderProbe
{
    string Name { get; }

    /// <summary>
    /// How this probe reads usage, in the same words its own snapshots carry. A stable fact about
    /// the mechanism, so a failure the probe never got to author can still be labelled honestly.
    /// </summary>
    string Mechanism { get; }

    /// <summary>Official or Unofficial. A property of the mechanism, never of the last call.</summary>
    MechanismTier Tier { get; }

    Task<ProviderSnapshot> ProbeAsync(CancellationToken ct);
}
