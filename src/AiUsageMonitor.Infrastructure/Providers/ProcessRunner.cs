using System.Diagnostics;
using System.Text;

namespace AiUsageMonitor.Infrastructure.Providers;

/// <summary>
/// Small helper for launching provider executables directly - never through a shell, never through
/// PowerShell - with UTF-8 (no BOM) standard streams, and a hard timeout backed by a Kill() backstop.
/// </summary>
internal static class ProcessRunner
{
    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunCapturedAsync(
        string exePath,
        string arguments,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdOut.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stdErr.AppendLine(e.Data); };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return (process.ExitCode, stdOut.ToString(), stdErr.ToString());
        }
        finally
        {
            TryKill(process);
        }
    }

    public static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort backstop only - the process may already be gone.
        }
    }

    public static IProcessSession StartDuplex(string exePath, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            StandardInputEncoding = new UTF8Encoding(false),
            CreateNoWindow = true,
        };

        var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
            return new ProcessSession(process);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private sealed class ProcessSession : IProcessSession
    {
        private Process? _process;
        private readonly Task<string> _standardError;

        public ProcessSession(Process process)
        {
            _process = process;

            // Drained from the moment the process starts, for two independent reasons. A redirected
            // pipe that nobody reads can fill and block the child, which would turn a chatty CLI
            // into a hang; and a CLI that rejects its command line writes its reason here before
            // exiting, which is the only account of the failure that ever exists.
            _standardError = process.StandardError.ReadToEndAsync();
        }

        public TextWriter StandardInput => GetProcess().StandardInput;
        public TextReader StandardOutput => GetProcess().StandardOutput;

        public Task WaitForExitAsync(CancellationToken ct) => GetProcess().WaitForExitAsync(ct);

        public async Task<string> ReadStandardErrorAsync(CancellationToken ct)
        {
            try
            {
                return await _standardError.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
                // Diagnostics must never make the outcome worse than the failure being diagnosed.
                return string.Empty;
            }
        }

        public void Dispose()
        {
            Process? process = Interlocked.Exchange(ref _process, null);
            if (process is null)
            {
                return;
            }

            TryKill(process);
            process.Dispose();
        }

        private Process GetProcess() => _process ?? throw new ObjectDisposedException(nameof(ProcessSession));
    }
}
