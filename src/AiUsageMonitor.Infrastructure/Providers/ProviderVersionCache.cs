namespace AiUsageMonitor.Infrastructure.Providers;

/// <summary>
/// Remembers a provider executable's reported version so a hidden widget does not relaunch it once
/// a minute for a string that changes on upgrade. Keyed on the path AND its last-write time, so an
/// upgrade in place invalidates without any version comparison.
/// </summary>
public sealed class ProviderVersionCache
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string exePath, DateTime lastWriteUtc, out string version)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(exePath, out Entry? entry) && entry.LastWriteUtc == lastWriteUtc)
            {
                version = entry.Version;
                return true;
            }
        }

        version = string.Empty;
        return false;
    }

    public void Store(string exePath, DateTime lastWriteUtc, string version)
    {
        lock (_gate)
        {
            _entries[exePath] = new Entry(lastWriteUtc, version);
        }
    }

    private sealed record Entry(DateTime LastWriteUtc, string Version);
}
