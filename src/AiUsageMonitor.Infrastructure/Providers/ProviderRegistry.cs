using AiUsageMonitor.Infrastructure.Providers.Claude;
using AiUsageMonitor.Infrastructure.Providers.Codex;

namespace AiUsageMonitor.Infrastructure.Providers;

/// <summary>Every provider this build knows how to probe, in the order their cards are laid out.</summary>
public static class ProviderRegistry
{
    public static IReadOnlyList<ProviderDescriptor> CreateDefault() =>
    [
        new("claude-code", "Claude Code", "CC", new ClaudeOAuthUsageProbe()),
        new("codex", "Codex", "CX", new CodexProbe())
    ];
}
