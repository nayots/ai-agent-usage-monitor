using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Providers.Claude;

namespace AiUsageMonitor.Infrastructure.Tests.Fakes;

/// <summary>Models the CLI changing its credential file without spawning a real process.</summary>
public sealed class RewritingProcessRunner(IProcessRunner inner, Action onRun) : IProcessRunner
{
    public Task<(int ExitCode, string StdOut, string StdErr)> RunCapturedAsync(
        string exePath, string arguments, TimeSpan timeout, CancellationToken ct)
    {
        // Reads the production constant rather than repeating its value. The subcommand is expected
        // to change - "update" is the documented fallback if the live check on this branch shows
        // "doctor" does not renew reliably - and a second copy here would quietly stop matching,
        // turning that one-line swap into a pile of confusing test failures.
        if (arguments == ClaudeSignInRepair.RepairArguments)
        {
            onRun();
        }

        return inner.RunCapturedAsync(exePath, arguments, timeout, ct);
    }

    public IProcessSession Start(string exePath, string arguments) => inner.Start(exePath, arguments);
}
