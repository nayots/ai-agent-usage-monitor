namespace AiUsageMonitor.Infrastructure.Providers.Claude;

/// <summary>
/// Finds the local Claude Code executable. The candidate list is pure and separately testable
/// because the ordering is the part that can silently regress; only <see cref="Locate"/> touches
/// the filesystem. Every location is resolved per-user at runtime - the release artifact has to
/// run on a machine that is not the author's.
/// </summary>
public static class ClaudeExecutableLocator
{
    /// <summary>
    /// Candidates in priority order: the native installer's location first (verified on Windows 11
    /// for Claude Code 2.1.227), then the npm global shim, then PATH. Both executable forms are
    /// tried for PATH entries because an npm install puts a <c>.cmd</c> shim there, not an
    /// <c>.exe</c>.
    /// </summary>
    public static IReadOnlyList<string> CandidatePaths(string userProfile, string appData, string? pathEnvironment)
    {
        List<string> candidates =
        [
            Path.Combine(userProfile, ".local", "bin", "claude.exe"),
            Path.Combine(appData, "npm", "claude.cmd")
        ];

        foreach (string directory in (pathEnvironment ?? string.Empty).Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            candidates.Add(Path.Combine(directory.Trim(), "claude.exe"));
            candidates.Add(Path.Combine(directory.Trim(), "claude.cmd"));
        }

        return candidates;
    }

    /// <summary>The first candidate that exists, or null when Claude Code is not installed here.</summary>
    public static string? Locate()
    {
        IReadOnlyList<string> candidates = CandidatePaths(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
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

    /// <summary>
    /// "2.1.227 (Claude Code)" -> "2.1.227". The first whitespace-delimited token of the first
    /// line, or null: an unparseable banner leaves the version absent rather than displaying
    /// whatever the executable happened to print.
    /// </summary>
    public static string? ParseVersion(string? standardOutput)
    {
        if (string.IsNullOrWhiteSpace(standardOutput))
        {
            return null;
        }

        string firstLine = standardOutput.Split('\n')[0].Trim();
        string[] tokens = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length == 0 ? null : tokens[0];
    }
}
