namespace AiUsageMonitor.Infrastructure.Refresh;

/// <summary>
/// Why an attempt was made. Recorded so request volume can be explained after the fact — the
/// investigation behind this work could calculate the configured rate from code but could not
/// reconstruct what had actually happened or why.
/// </summary>
public enum RefreshTrigger
{
    Scheduled,
    Startup,
    Resume,
    Unlock,
    ManualGlobal,
    ManualCard
}
