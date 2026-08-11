namespace AiUsageMonitor.Domain;

/// <summary>
/// Confidence tier of the mechanism a snapshot was obtained through (PRD §4.1.1).
/// Every provider card and every diagnostics entry must render this.
/// </summary>
public enum MechanismTier
{
    /// <summary>
    /// Read from an undocumented source that may change or stop without notice.
    /// Deliberately the zero value: a tier that was never explicitly set must fail
    /// safe to the claim that promises less, never to <see cref="Official"/>.
    /// </summary>
    Unofficial = 0,

    /// <summary>Read from a documented, provider-supported interface.</summary>
    Official = 1
}
