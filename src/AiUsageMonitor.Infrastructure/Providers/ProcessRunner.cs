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
}
