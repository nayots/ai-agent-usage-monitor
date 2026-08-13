namespace AiUsageMonitor.Infrastructure.Diagnostics;

public static class DiagnosticRedaction
{
    /// <summary>
    /// Replaces local user identifiers with placeholders before diagnostics leave the application.
    /// </summary>
    public static string? Redact(string? text)
    {
        if (text is null || text.Length == 0)
        {
            return text;
        }

        try
        {
            string result = text;
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(profile))
            {
                result = result.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
                result = result.Replace(SwapSeparators(profile), "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
            }

            string userName = Environment.UserName;
            if (userName.Length >= 3)
            {
                result = result.Replace(userName, "%USERNAME%", StringComparison.OrdinalIgnoreCase);
            }

            return result;
        }
        catch
        {
            return text;
        }
    }

    private static string SwapSeparators(string path) => path.Contains('\\')
        ? path.Replace('\\', '/')
        : path.Replace('/', '\\');
}
