namespace AiUsageMonitor.Poc.Domain;

/// <summary>
/// Provider-neutral connection lifecycle state for a quota probe.
/// </summary>
public enum ConnectionState
{
    /// <summary>The provider's executable could not be found through any supported discovery mechanism.</summary>
    NotInstalled,

    /// <summary>Discovery of the provider's executable / mechanism is in progress.</summary>
    Discovering,

    /// <summary>The provider is installed but a request is in flight / awaiting a response.</summary>
    Waiting,

    /// <summary>A quota response was retrieved successfully through a verified local mechanism.</summary>
    Connected,

    /// <summary>The last successful retrieval is older than expected; data shown may be out of date.</summary>
    Stale,

    /// <summary>The provider is installed but the local mechanism is temporarily unavailable.</summary>
    Unavailable,

    /// <summary>The provider is installed but has no verified local quota mechanism.</summary>
    Unsupported,

    /// <summary>Retrieval was attempted but failed unexpectedly (timeout, parse failure, process error, etc.).</summary>
    Error
}
