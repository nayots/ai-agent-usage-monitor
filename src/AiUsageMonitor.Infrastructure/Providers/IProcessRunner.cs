namespace AiUsageMonitor.Infrastructure.Providers;

/// <summary>One live child process, as the probes use it: write requests, read framed replies.</summary>
public interface IProcessSession : IDisposable
{
    TextWriter StandardInput { get; }
    TextReader StandardOutput { get; }
    Task WaitForExitAsync(CancellationToken ct);

    /// <summary>
    /// Everything the process has written to standard error. A CLI that rejects its own command
    /// line exits before writing a single stdout frame and explains itself here and nowhere else,
    /// so a probe that reads only stdout can report that the stream closed but never why.
    ///
    /// This is diagnostic only and must never displace the caller's own error: implementations
    /// return an empty string rather than throwing when the text cannot be read in time.
    /// </summary>
    Task<string> ReadStandardErrorAsync(CancellationToken ct);
}

/// <summary>
/// How a probe launches a provider executable. Exists so the two adapters can be tested without a
/// real install: production wiring uses <see cref="DefaultProcessRunner"/> and behaves exactly as
/// the static helper always did.
/// </summary>
public interface IProcessRunner
{
    Task<(int ExitCode, string StdOut, string StdErr)> RunCapturedAsync(
        string exePath, string arguments, TimeSpan timeout, CancellationToken ct);

    /// <summary>
    /// Starts a duplex session. The caller owns disposal, which must kill the process tree if it
    /// has not already exited.
    /// </summary>
    IProcessSession Start(string exePath, string arguments);
}
