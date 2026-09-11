using AiUsageMonitor.Domain;

namespace AiUsageMonitor.Infrastructure.Providers;

/// <summary>
/// A provider's display identity paired with the probe that speaks to it. The monogram is
/// registered explicitly rather than derived: initials would give "Codex" a single "C", and the
/// approved design uses "CX". Adding a provider is one entry in <see cref="ProviderRegistry"/>
/// plus its probe - no change to any view or view model (PRD §21).
/// </summary>
/// <param name="TraySlot">
/// Which of the notification icon's three flag positions this provider owns, and with it which
/// colour token the flag takes. Keyed to the provider, never to its index in the visible list:
/// hiding one card must not renumber the others.
/// </param>
public sealed record ProviderDescriptor(
    string Key, string DisplayName, string Monogram, IProviderProbe Probe, int TraySlot = 0);
