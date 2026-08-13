namespace AiUsageMonitor.Infrastructure.Providers;

/// <summary>
/// A failure whose Message this application authored, and which is therefore safe to render on a
/// card verbatim. Nothing derived from provider output may be interpolated into it.
/// </summary>
public sealed class ProviderMechanismException(string message) : Exception(message);
