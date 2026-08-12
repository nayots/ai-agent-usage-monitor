using AiUsageMonitor.Infrastructure.Providers.Claude;
using AiUsageMonitor.Infrastructure.Providers.Codex;

namespace AiUsageMonitor.Infrastructure.Providers;

/// <summary>Every provider this build knows how to probe, in the order their cards are laid out.</summary>
public static class ProviderRegistry
{
    public static IReadOnlyList<ProviderDescriptor> CreateDefault() =>
    [
        new("Claude Code", "CC", new ClaudeOAuthUsageProbe()),
        new("Codex", "CX", new CodexProbe())
    ];
}
