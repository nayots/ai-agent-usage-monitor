namespace AiUsageMonitor.Infrastructure.Providers;

/// <summary>Production process launcher that preserves the existing process-helper behaviour.</summary>
public sealed class DefaultProcessRunner : IProcessRunner
{
    public static DefaultProcessRunner Instance { get; } = new();

    private DefaultProcessRunner()
    {
    }

    public Task<(int ExitCode, string StdOut, string StdErr)> RunCapturedAsync(
        string exePath, string arguments, TimeSpan timeout, CancellationToken ct) =>
        ProcessRunner.RunCapturedAsync(exePath, arguments, timeout, ct);

    public IProcessSession Start(string exePath, string arguments) => ProcessRunner.StartDuplex(exePath, arguments);
}
