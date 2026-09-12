using AiUsageMonitor.Infrastructure.Providers.Claude;
using AiUsageMonitor.Infrastructure.Providers.Codex;
using AiUsageMonitor.Infrastructure.Providers.Cursor;
using Microsoft.Extensions.Logging;

namespace AiUsageMonitor.Infrastructure.Providers;

/// <summary>Every provider this build knows how to probe, in the order their cards are laid out.</summary>
public static class ProviderRegistry
{
    /// <summary>
    /// Builds the providers, reading the optional Claude auto-repair setting at probe time.
    /// </summary>
    /// <param name="loggerFactory">
    /// Optional, and only the Claude probe uses it today - for the sign-in renewal, which is the one
    /// thing a probe does that changes state outside this process and so has to leave a record that
    /// outlives it. Null keeps every probe silent, which is what the POC wants: it prints its own
    /// report.
    /// </param>
    public static IReadOnlyList<ProviderDescriptor> CreateDefault(
        Func<bool>? claudeSignInAutoRepairEnabled = null,
        ILoggerFactory? loggerFactory = null) =>
    [
        new("claude-code", "Claude Code", "CC",
            new ClaudeOAuthUsageProbe(
                signInAutoRepairEnabled: claudeSignInAutoRepairEnabled,
                logger: loggerFactory?.CreateLogger<ClaudeOAuthUsageProbe>()),
            TraySlot: 0),
        new("codex", "Codex", "CX", new CodexProbe(), TraySlot: 1),
        new("cursor", "Cursor", "CR", new CursorUsageProbe(), TraySlot: 2)
    ];
}
