using AiUsageMonitor.Infrastructure.Providers.Claude;
using AiUsageMonitor.Infrastructure.Providers.Codex;
using AiUsageMonitor.Infrastructure.Providers.Cursor;

namespace AiUsageMonitor.Infrastructure.Providers;

/// <summary>Every provider this build knows how to probe, in the order their cards are laid out.</summary>
public static class ProviderRegistry
{
    public static IReadOnlyList<ProviderDescriptor> CreateDefault() =>
    [
        new("claude-code", "Claude Code", "CC", new ClaudeOAuthUsageProbe(), TraySlot: 0),
        new("codex", "Codex", "CX", new CodexProbe(), TraySlot: 1),
        new("cursor", "Cursor", "CR", new CursorUsageProbe(), TraySlot: 2)
    ];
}
