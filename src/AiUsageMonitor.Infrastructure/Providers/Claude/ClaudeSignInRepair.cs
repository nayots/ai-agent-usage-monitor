using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiUsageMonitor.Infrastructure.Providers.Claude;

/// <summary>
/// Asks Claude Code to renew its own sign-in when the stored access token has lapsed.
/// This type never sees a credential: it runs the provider command, then observes whether the
/// non-secret stored expiry moved.
///
/// Every outcome is written twice, to two sinks that outlive different things. The notes reach the
/// Diagnostics pane, where they are readable now and die with the process; the log reaches disk,
/// where a repair that misbehaved on someone else's machine can still be read tomorrow. Both are
/// safe to write because this type has no credential to leak into either.
/// </summary>
public sealed class ClaudeSignInRepair
{
    /// <summary>The one place the repair subcommand is named.</summary>
    public const string RepairArguments = "doctor";

    private static readonly TimeSpan RepairTimeout = TimeSpan.FromSeconds(30);

    private readonly IProcessRunner _processes;
    private readonly Func<DateTimeOffset> _now;
    private readonly ILogger _logger;
    private bool _hasFailed;
    private DateTimeOffset? _failedExpiry;

    public ClaudeSignInRepair(
        IProcessRunner processes,
        Func<DateTimeOffset>? now = null,
        ILogger? logger = null)
    {
        _processes = processes;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Returns true when the stored sign-in is now a different one from <paramref name="staleExpiry"/>
    /// <em>and</em> that new one is still live.
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
            _logger.LogInformation(
                "Claude Code had already renewed its own sign-in, so no renewal command was run.");
            return true;
        }

        if (_hasFailed && _failedExpiry == staleExpiry)
        {
            notes.Add("An automatic repair was already attempted for this sign-in and did not help, so none was attempted again.");

            // Deliberately not logged. This branch is reached on every poll for as long as the
            // sign-in stays broken - a line here would be one every two minutes, all of them
            // restating the single warning already written when the attempt actually failed.
            return false;
        }

        notes.Add($"Asked Claude Code to renew its own sign-in (\"{RepairArguments}\").");
        _logger.LogInformation(
            "Claude Code's stored sign-in has lapsed; asking Claude Code to renew it ({Arguments}).",
            RepairArguments);

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

            // The exception type, never the exception - its message can carry a path or a command
            // line, and a log file is exactly where that must not end up.
            _logger.LogWarning(
                "The Claude Code renewal command could not be run ({Failure}). The sign-in stays lapsed.",
                ex.GetType().Name);
            RecordFailure(staleExpiry);
            return false;
        }

        if (Rotated(staleExpiry, reloadMetadata()))
        {
            notes.Add("Claude Code renewed its sign-in; the reading below used the renewed one.");
            _logger.LogInformation("Claude Code renewed its sign-in; the reading used the renewed one.");
            _hasFailed = false;
            _failedExpiry = null;
            return true;
        }

        notes.Add("The renewal command ran but the stored sign-in did not change.");
        _logger.LogWarning(
            "The Claude Code renewal command ran but the stored sign-in did not change, so it was not renewed. "
            + "No further attempt is made until the stored sign-in changes.");
        RecordFailure(staleExpiry);
        return false;
    }

    /// <summary>
    /// Whether the stored expiry is now a different, readable, still-future instant from the one we
    /// started with. All three conditions are load-bearing, and each guards a different call site:
    ///
    /// <list type="bullet">
    /// <item><description>
    /// <b>Readable.</b> The token-free reader returns <see cref="ClaudeAccountMetadata.Empty"/> for a
    /// file it cannot read or parse. Without this, an unreadable file would compare unequal to a real
    /// instant and be reported as a successful renewal.
    /// </description></item>
    /// <item><description>
    /// <b>Changed.</b> Required by the rejected-token path, where the stored expiry was already in
    /// the future when the endpoint rejected it. Liveness alone would call that a renewal and retry
    /// the identical token, buying a guaranteed second rejection.
    /// </description></item>
    /// <item><description>
    /// <b>Still future.</b> Required by the lapsed-sign-in path, where change alone would accept a
    /// rotation to another already-spent token and send a request that can only be rejected - the
    /// exact doomed call that path exists to avoid.
    /// </description></item>
    /// </list>
    /// </summary>
    private bool Rotated(DateTimeOffset? staleExpiry, ClaudeAccountMetadata reloaded) =>
        reloaded.AccessTokenExpiresAt is DateTimeOffset current
        && current != staleExpiry
        && current > _now();

    private void RecordFailure(DateTimeOffset? staleExpiry)
    {
        _hasFailed = true;
        _failedExpiry = staleExpiry;
    }
}
