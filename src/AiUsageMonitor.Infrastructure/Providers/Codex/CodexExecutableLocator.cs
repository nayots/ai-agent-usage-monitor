namespace AiUsageMonitor.Infrastructure.Providers.Codex;

/// <summary>
/// Finds the real Codex executable rather than an npm shim. The app launches with shell execution
/// disabled, so a <c>.cmd</c> or <c>.ps1</c> file is not a usable result.
/// </summary>
public static class CodexExecutableLocator
{
    /// <summary>
    /// npm prefixes to search, in priority order: %APPDATA%\npm first, then every PATH directory
    /// holding a codex shim. Pure - takes the filesystem test as a delegate so ordering, which is
    /// the part that silently regresses, is testable without touching disk.
    /// </summary>
    public static IReadOnlyList<string> ShimDirectories(
        string appData,
        string? pathEnvironment,
        Func<string, bool> fileExists)
    {
        string npmDirectory = Path.Combine(appData, "npm");
        var directories = new List<string> { npmDirectory };

        foreach (string entry in (pathEnvironment ?? string.Empty).Split(Path.PathSeparator))
        {
            string directory = entry.Trim();
            if (directory.Length == 0 || directories.Contains(directory, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (fileExists(Path.Combine(directory, "codex.cmd"))
                || fileExists(Path.Combine(directory, "codex.ps1")))
            {
                directories.Add(directory);
            }
        }

        return directories;
    }

    /// <summary>The vendored codex.exe under one npm prefix, or null. Touches the filesystem.</summary>
    public static string? VendoredExecutableUnder(string npmPrefix)
    {
        string packageRoot = Path.Combine(npmPrefix, "node_modules", "@openai", "codex", "node_modules", "@openai");
        string verified = Path.Combine(
            packageRoot,
            "codex-win32-x64",
            "vendor",
            "x86_64-pc-windows-msvc",
            "bin",
            "codex.exe");
        if (File.Exists(verified))
        {
            return verified;
        }

        foreach (string architectureDirectory in SafeEnumerateDirectories(packageRoot, "codex-win32-*"))
        {
            string vendorDirectory = Path.Combine(architectureDirectory, "vendor");
            foreach (string tripleDirectory in SafeEnumerateDirectories(vendorDirectory, "*"))
            {
                string candidate = Path.Combine(tripleDirectory, "bin", "codex.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The first real codex executable found, or null. Never returns a .cmd or .ps1 shim: the
    /// launch paths set UseShellExecute = false and cannot run one.
    /// </summary>
    public static string? Locate()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string npmDirectory = Path.Combine(appData, "npm");
        string? pathEnvironment = Environment.GetEnvironmentVariable("PATH");

        if (VendoredExecutableUnder(npmDirectory) is string verified)
        {
            return verified;
        }

        if (FindOnPath(pathEnvironment, "codex.exe") is string direct)
        {
            return direct;
        }

        foreach (string npmPrefix in ShimDirectories(appData, pathEnvironment, File.Exists).Skip(1))
        {
            if (VendoredExecutableUnder(npmPrefix) is string candidate)
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? FindOnPath(string? pathEnvironment, string fileName)
    {
        foreach (string entry in (pathEnvironment ?? string.Empty).Split(Path.PathSeparator))
        {
            string directory = entry.Trim();
            if (directory.Length == 0)
            {
                continue;
            }

            string candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> SafeEnumerateDirectories(string path, string pattern)
    {
        try
        {
            return Directory.GetDirectories(path, pattern);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}
