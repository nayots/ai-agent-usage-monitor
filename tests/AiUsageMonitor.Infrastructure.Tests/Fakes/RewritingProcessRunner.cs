using AiUsageMonitor.Infrastructure.Providers;

namespace AiUsageMonitor.Infrastructure.Tests.Fakes;

/// <summary>Models the CLI changing its credential file without spawning a real process.</summary>
public sealed class RewritingProcessRunner(IProcessRunner inner, Action onRun) : IProcessRunner
{
    private const string ClaudeSignInRepairArguments = "doctor";

    public Task<(int ExitCode, string StdOut, string StdErr)> RunCapturedAsync(
        string exePath, string arguments, TimeSpan timeout, CancellationToken ct)
    {
        if (arguments == ClaudeSignInRepairArguments)
        {
            onRun();
        }

        return inner.RunCapturedAsync(exePath, arguments, timeout, ct);
    }

    public IProcessSession Start(string exePath, string arguments) => inner.Start(exePath, arguments);
}
