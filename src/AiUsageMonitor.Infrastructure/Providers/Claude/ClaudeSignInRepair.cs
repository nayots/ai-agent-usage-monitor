namespace AiUsageMonitor.Infrastructure.Providers.Claude;

/// <summary>
/// Asks Claude Code to renew its own sign-in when the stored access token has lapsed.
/// This type never sees a credential: it runs the provider command, then observes whether the
/// non-secret stored expiry moved.
/// </summary>
public sealed class ClaudeSignInRepair
{
    /// <summary>The one place the repair subcommand is named.</summary>
    public const string RepairArguments = "doctor";

    private static readonly TimeSpan RepairTimeout = TimeSpan.FromSeconds(30);

    private readonly IProcessRunner _processes;
    private bool _hasFailed;
    private DateTimeOffset? _failedExpiry;

    public ClaudeSignInRepair(IProcessRunner processes) => _processes = processes;

    /// <summary>
    /// Returns true when the stored sign-in is now different from <paramref name="staleExpiry"/>.
    /// </summary>
    public async Task<bool> TryRepairAsync(
        string exePath,
        DateTimeOffset? staleExpiry,
        Func<ClaudeAccountMetadata> reloadMetadata,
        ICollection<string> notes,
        CancellationToken ct)
    {
        if (Rotated(staleExpiry, reloadMetadata()))
        {
            notes.Add("The stored sign-in had already been renewed by Claude Code itself, so nothing was run.");
            return true;
        }

        if (_hasFailed && _failedExpiry == staleExpiry)
        {
            notes.Add("An automatic repair was already attempted for this sign-in and did not help, so none was attempted again.");
            return false;
        }

        notes.Add($"Asked Claude Code to renew its own sign-in (\"{RepairArguments}\").");

        try
        {
            await _processes.RunCapturedAsync(exePath, RepairArguments, RepairTimeout, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            notes.Add($"The renewal command could not be run ({ex.GetType().Name}).");
            RecordFailure(staleExpiry);
            return false;
        }

        if (Rotated(staleExpiry, reloadMetadata()))
        {
            notes.Add("Claude Code renewed its sign-in; the reading below used the renewed one.");
            _hasFailed = false;
            _failedExpiry = null;
            return true;
        }

        notes.Add("The renewal command ran but the stored sign-in did not change.");
        RecordFailure(staleExpiry);
        return false;
    }

    private static bool Rotated(DateTimeOffset? staleExpiry, ClaudeAccountMetadata reloaded) =>
        reloaded.AccessTokenExpiresAt is DateTimeOffset current && current != staleExpiry;

    private void RecordFailure(DateTimeOffset? staleExpiry)
    {
        _hasFailed = true;
        _failedExpiry = staleExpiry;
    }
}
