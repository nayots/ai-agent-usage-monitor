using AiUsageMonitor.Domain;

namespace AiUsageMonitor.Infrastructure.Providers;

/// <summary>
/// A provider's display identity paired with the probe that speaks to it. The monogram is
/// registered explicitly rather than derived: initials would give "Codex" a single "C", and the
/// approved design uses "CX". Adding a provider is one entry in <see cref="ProviderRegistry"/>
/// plus its probe - no change to any view or view model (PRD §21).
/// </summary>
public sealed record ProviderDescriptor(string Key, string DisplayName, string Monogram, IProviderProbe Probe);
