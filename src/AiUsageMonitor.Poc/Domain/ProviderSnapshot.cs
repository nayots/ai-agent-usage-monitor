namespace AiUsageMonitor.Poc.Domain;

/// <summary>
/// Point-in-time result of probing a single provider for installation, connection, and quota state.
/// </summary>
public sealed record ProviderSnapshot(
    string ProviderName,
    bool Installed,
    string? Version,
    string? ExecutablePath,
    ConnectionState State,
    string Mechanism,
    string? UpdateModel,
    IReadOnlyList<QuotaWindow> Windows,
    DateTimeOffset? RetrievedAt,
    string? Error,
    IReadOnlyList<string> Notes);
