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
    /// True only when the stored command line is this executable. An entry left by a copy of the
    /// app that has since moved is not this app starting with Windows, so it reads as off and
    /// <see cref="Enable"/> overwrites it.
    /// </summary>
    public bool IsEnabled
    {
        get
        {
            if (_executablePath is null)
            {
                return false;
            }

            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(_keyPath);
            return key?.GetValue(_valueName) is string stored
                && string.Equals(stored, Command(_executablePath), StringComparison.OrdinalIgnoreCase);
        }
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
