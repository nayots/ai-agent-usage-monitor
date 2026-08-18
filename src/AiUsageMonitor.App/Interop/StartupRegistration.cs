using Microsoft.Win32;

namespace AiUsageMonitor.App.Interop;

/// <summary>
/// The Start with Windows setting, as a per-user Run entry.
/// <para>
/// HKEY_CURRENT_USER only, so no administrator rights are involved and nothing machine-wide or
/// policy-owned is touched. This is application-owned configuration; it is not, and must never
/// become, a way to modify a provider's own configuration.
/// </para>
/// </summary>
public sealed class StartupRegistration
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string DefaultValueName = "AiUsageMonitor";

    private readonly string _keyPath;
    private readonly string _valueName;
    private readonly string? _executablePath;

    public StartupRegistration(string keyPath, string valueName, string? executablePath)
    {
        _keyPath = keyPath;
        _valueName = valueName;
        _executablePath = executablePath;
    }

    /// <summary>
    /// Resolved at run time, never hardcoded: the release artifact has to work on a machine that is
    /// not the author's, from whatever folder its owner unpacked it into.
    /// </summary>
    public static StartupRegistration ForThisProcess() =>
        new(RunKeyPath, DefaultValueName, Environment.ProcessPath);

    /// <summary>
    /// False when the process cannot name its own executable, in which case there is nothing
    /// truthful to register. The UI disables the control and says why rather than offering a
    /// toggle that silently does nothing.
    /// </summary>
    public bool IsSupported => _executablePath is not null;

    /// <summary>
    /// True when a value exists under this application's own name. Deliberately not a path
    /// comparison: the value name is ours, nothing else writes it, so its presence means "registered
    /// to start with Windows" whichever copy's path it holds. An upgraded release carries a new file
    /// name, and reading that as off would show an unchecked box while Windows went on launching the
    /// previous version at every login.
    /// </summary>
    public bool IsEnabled => _executablePath is not null && StoredCommand() is not null;

    /// <summary>
    /// Registered, but under some other executable — an entry left behind by a previous release or
    /// by a copy that has since moved. This is a stale entry to repair, not evidence of a second
    /// application; see <see cref="SyncPath"/>.
    /// </summary>
    public bool IsRegisteredElsewhere =>
        _executablePath is not null
        && StoredCommand() is string stored
        && !string.Equals(stored, Command(_executablePath), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Repoints an existing registration at the running executable, and does nothing at all when
    /// there is no registration to repair. Without this, upgrading to a release with a new file name
    /// leaves Windows starting the old one forever, and deleting the old file leaves the entry
    /// dangling with nothing in the UI to say so.
    /// </summary>
    public void SyncPath()
    {
        if (IsRegisteredElsewhere)
        {
            Enable();
        }
    }

    private string? StoredCommand()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(_keyPath);
        return key?.GetValue(_valueName) as string;
    }

    public void Enable()
    {
        if (_executablePath is null)
        {
            return;
        }

        using RegistryKey key = Registry.CurrentUser.CreateSubKey(_keyPath, writable: true);
        key.SetValue(_valueName, Command(_executablePath), RegistryValueKind.String);
    }

    /// <summary>
    /// Deletes this application's own value name, whatever it currently contains, and never the
    /// key: Run is shared with every other application the user has.
    /// </summary>
    public void Disable()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: true);
        key?.DeleteValue(_valueName, throwOnMissingValue: false);
    }

    /// <summary>Quoted, or a path containing a space is read as a program plus arguments.</summary>
    private static string Command(string executablePath) => "\"" + executablePath + "\"";
}
