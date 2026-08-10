namespace AiUsageMonitor.Domain;

/// <summary>
/// A provider-specific probe capable of discovering installation state and, when possible,
/// retrieving quota data through a verified official local mechanism.
/// </summary>
public interface IProviderProbe
{
    string Name { get; }

    Task<ProviderSnapshot> ProbeAsync(CancellationToken ct);
}
