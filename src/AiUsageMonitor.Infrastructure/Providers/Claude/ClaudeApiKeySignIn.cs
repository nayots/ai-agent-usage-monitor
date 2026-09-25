using System.Text.Json;

namespace AiUsageMonitor.Infrastructure.Providers.Claude;

/// <summary>
/// Recognises Claude Code signed in with an API key - the Anthropic Console login, an
/// <c>ANTHROPIC_API_KEY</c> environment variable, or an <c>apiKeyHelper</c> script - rather than a
/// Claude subscription. Such an account is billed per token and has no subscription quota, so
/// there is nothing for this application to show; recognising it is only so the card can say so,
/// instead of claiming no sign-in exists.
/// <para>
/// Only ever tests that a key is PRESENT. The key's value is never read into a string: a present,
/// non-empty JSON string is established with <see cref="JsonElement.ValueEquals(string)"/>, and the
/// result names where the key was found, never the key. It is never sent anywhere either - there is
/// no endpoint this application would send it to.
/// </para>
/// </summary>
public static class ClaudeApiKeySignIn
{
    public const string EnvironmentVariable = "ANTHROPIC_API_KEY";

    /// <summary>
    /// Where an API-key sign-in was found (<c>primaryApiKey</c>, <c>apiKeyHelper</c> or
    /// <see cref="EnvironmentVariable"/>), or null when there is none. Never throws: an unreadable
    /// file is "not found", which leaves the caller's ordinary no-sign-in message in place.
    /// </summary>
    public static string? Detect(string claudeConfigPath, string settingsPath, Func<string, string?> environment)
    {
        if (!string.IsNullOrWhiteSpace(environment(EnvironmentVariable)))
        {
            return EnvironmentVariable;
        }

        if (HasNonEmptyString(claudeConfigPath, "primaryApiKey"))
        {
            return "primaryApiKey";
        }

        if (HasNonEmptyString(settingsPath, "apiKeyHelper"))
        {
            return "apiKeyHelper";
        }

        return null;
    }

    /// <summary>The real machine: <c>~/.claude.json</c>, <c>~/.claude/settings.json</c> and the process environment.</summary>
    public static string? DetectOnThisMachine()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Detect(
            Path.Combine(userProfile, ".claude.json"),
            Path.Combine(userProfile, ".claude", "settings.json"),
            Environment.GetEnvironmentVariable);
    }

    private static bool HasNonEmptyString(string path, string propertyName)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            using FileStream stream = File.OpenRead(path);
            using JsonDocument document = JsonDocument.Parse(stream);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(propertyName, out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                && !value.ValueEquals(string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }
}
