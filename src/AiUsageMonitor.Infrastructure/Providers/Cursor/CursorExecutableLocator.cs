using System.Diagnostics;

namespace AiUsageMonitor.Infrastructure.Providers.Cursor;

/// <summary>
/// Finds the local Cursor installation. The candidate list is pure and separately testable
/// because the ordering is the part that can silently regress; only <see cref="Locate"/> touches
/// the filesystem. Every location is resolved per-user at runtime.
/// </summary>
public static class CursorExecutableLocator
{
    /// <summary>
    /// Candidates in priority order: the per-user install location first, then a machine-wide
    /// install, then PATH. Both executable forms are tried for PATH entries because Cursor's
    /// shell shim is a <c>.cmd</c>, not an <c>.exe</c>.
    /// </summary>
    public static IReadOnlyList<string> CandidatePaths(string localAppData, string programFiles, string? pathEnvironment)
    {
        List<string> candidates =
        [
            Path.Combine(localAppData, "Programs", "cursor", "Cursor.exe"),
            Path.Combine(programFiles, "cursor", "Cursor.exe"),
        ];

        foreach (string directory in (pathEnvironment ?? string.Empty).Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            candidates.Add(Path.Combine(directory.Trim(), "Cursor.exe"));
            candidates.Add(Path.Combine(directory.Trim(), "cursor.cmd"));
        }

        return candidates;
    }

    /// <summary>The first candidate that exists, or null when Cursor is not installed here.</summary>
    public static string? Locate()
    {
        IReadOnlyList<string> candidates = CandidatePaths(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetEnvironmentVariable("PATH"));

        foreach (string candidate in candidates)
        {
            try
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An unreadable PATH entry is not a reason to stop looking at the rest.
            }
        }

        return null;
    }

    /// <summary>The product version from executable metadata, or null when it cannot be read.</summary>
    public static string? TryReadVersion(string executablePath)
    {
        try
        {
            string? version = FileVersionInfo.GetVersionInfo(executablePath).ProductVersion;
            return string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            return null;
        }
    }
}
