using System.IO;
using System.Text.Json;

namespace AiUsageMonitor.App.Interop;

/// <summary>Which executable, and which version of it, currently owns the single-instance mutex.</summary>
public sealed record RunningInstance(string ExecutablePath, string Version);

/// <summary>
/// The running instance's own description of itself, on disk.
/// <para>
/// Written when a process acquires the single-instance mutex and deleted when it exits cleanly. It
/// is only ever read by a process that <em>failed</em> to acquire, which is why a record left behind
/// by a crash needs no separate invalidation: whoever next acquires the mutex overwrites it.
/// </para>
/// <para>
/// Nothing here may throw. A widget that cannot write a housekeeping file must still start.
/// </para>
/// </summary>
public sealed class RunningInstanceFile
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _path;

    public RunningInstanceFile(string path) => _path = path;

    /// <summary>
    /// %APPDATA%\AiUsageMonitor\running-instance.json, resolved for whichever user is running, and
    /// beside the settings file for the same reason: never a literal path.
    /// </summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AiUsageMonitor",
        "running-instance.json");

    /// <summary>Null when there is no record, or when the one on disk cannot be trusted.</summary>
    public RunningInstance? Read()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            RunningInstance? record = JsonSerializer.Deserialize<RunningInstance>(
                File.ReadAllText(_path),
                SerializerOptions);

            return string.IsNullOrWhiteSpace(record?.ExecutablePath) ? null : record;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Write(RunningInstance instance)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_path, JsonSerializer.Serialize(instance, SerializerOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort. The cost of failing is one missed takeover prompt, not a failed startup.
        }
    }

    public void Delete()
    {
        try
        {
            File.Delete(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // As above: a record that outlives its process is overwritten by the next one.
        }
    }
}
