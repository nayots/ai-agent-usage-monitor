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

    /// <summary>
    /// Whether reading usage through this mechanism contacts the provider's own first-party host
    /// (PRD §20). A property of the mechanism, never of the last call. Defaults to false so a probe
    /// that touches nothing but the local machine needs no ceremony; a probe that makes a network
    /// call MUST override this, and both shipped probes state it explicitly either way.
    /// </summary>
    bool MakesFirstPartyNetworkCall => false;

    Task<ProviderSnapshot> ProbeAsync(CancellationToken ct);
}
