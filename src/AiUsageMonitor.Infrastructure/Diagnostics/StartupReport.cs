namespace AiUsageMonitor.Infrastructure.Diagnostics;

/// <summary>What startup did, recorded once by App and never mutated afterwards.</summary>
public sealed record StartupReport(DateTimeOffset StartedAt, string? SettingsBackupPath)
{
    public bool SettingsWereUnreadable => SettingsBackupPath is not null;
}
