using System.Text.Json;

namespace AiUsageMonitor.Infrastructure.Providers.Claude;

/// <summary>
/// Recognises Claude Code authenticated by something other than a Claude subscription - a Console
/// API key, a keyless Console sign-in (an Anthropic profile), an environment credential, an
/// <c>apiKeyHelper</c> script, or a cloud provider. None of these has a subscription quota, so
/// there is nothing for this application to show; recognising them is only so the card can say
/// so, instead of claiming that no sign-in exists.
/// <para>
/// The sources and their precedence are Claude Code's own (its authentication documentation,
/// "Authentication precedence"). The keyless Console sign-in, the default since 2.1.242, stores
/// no key at all: it writes a profile under the Anthropic configuration directory and signs out
/// of any claude.ai login - which is exactly the machine that used to read "has not stored a
/// sign-in".
/// </para>
/// <para>
/// Only ever tests that a credential is PRESENT. No key value is read into a string: a present,
/// non-empty JSON string is established with <see cref="JsonElement.ValueEquals(string)"/>, a
/// profile by a file existing, and an environment credential by the variable being non-empty. The
/// result names where the credential was found, never the credential, and nothing is ever sent.
/// </para>
/// </summary>
public static class ClaudeApiKeySignIn
{
    public const string AnthropicProfile = "Anthropic profile";

    // Claude Code's precedence order. Cloud providers first: they override every other source.
    private static readonly string[] CloudProviderVariables =
        ["CLAUDE_CODE_USE_BEDROCK", "CLAUDE_CODE_USE_VERTEX", "CLAUDE_CODE_USE_FOUNDRY"];

    private static readonly string[] CredentialVariables =
        ["ANTHROPIC_AUTH_TOKEN", "ANTHROPIC_API_KEY", "ANTHROPIC_PROFILE"];

    /// <summary>Where Claude Code keeps what this class inspects.</summary>
    public sealed record Locations(string GlobalConfig, string Settings, string Credentials, string AnthropicConfigDirectory)
    {
        /// <summary>
        /// <c>CLAUDE_CONFIG_DIR</c> moves Claude Code's own files; the Anthropic configuration
        /// directory is shared with the <c>ant</c> CLI and has its own override.
        /// </summary>
        public static Locations For(string userProfile, string appData, Func<string, string?> environment)
        {
            string? configDir = environment("CLAUDE_CONFIG_DIR");
            string claudeDir = string.IsNullOrWhiteSpace(configDir) ? Path.Combine(userProfile, ".claude") : configDir;
            string globalConfig = string.IsNullOrWhiteSpace(configDir)
                ? Path.Combine(userProfile, ".claude.json")
                : Path.Combine(configDir, ".claude.json");

            string? anthropicDir = environment("ANTHROPIC_CONFIG_DIR");

            return new Locations(
                GlobalConfig: globalConfig,
                Settings: Path.Combine(claudeDir, "settings.json"),
                Credentials: Path.Combine(claudeDir, ".credentials.json"),
                AnthropicConfigDirectory: string.IsNullOrWhiteSpace(anthropicDir) ? Path.Combine(appData, "Anthropic") : anthropicDir);
        }

        public static Locations ForThisMachine() => For(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetEnvironmentVariable);
    }

    /// <summary>True when <paramref name="source"/> names a cloud provider rather than an Anthropic credential.</summary>
    public static bool IsCloudProvider(string source) => CloudProviderVariables.Contains(source, StringComparer.Ordinal);

    /// <summary>
    /// Where a non-subscription credential was found - an environment variable's name,
    /// <c>apiKeyHelper</c>, <c>primaryApiKey</c> or <see cref="AnthropicProfile"/> - or null when
    /// there is none. Never throws: an unreadable file is "not found", which leaves the caller's
    /// ordinary no-sign-in message in place.
    /// </summary>
    public static string? Detect(Locations locations, Func<string, string?> environment)
    {
        foreach (string variable in CloudProviderVariables.Concat(CredentialVariables))
        {
            if (!string.IsNullOrWhiteSpace(environment(variable)))
            {
                return variable;
            }
        }

        if (HasNonEmptyString(locations.Settings, "apiKeyHelper"))
        {
            return "apiKeyHelper";
        }

        if (HasNonEmptyString(locations.Credentials, "primaryApiKey") || HasNonEmptyString(locations.GlobalConfig, "primaryApiKey"))
        {
            return "primaryApiKey";
        }

        return HasProfile(locations.AnthropicConfigDirectory) ? AnthropicProfile : null;
    }

    public static string? DetectOnThisMachine() => Detect(Locations.ForThisMachine(), Environment.GetEnvironmentVariable);

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

    /// <summary>A profile is a file under <c>configs</c>; its contents are never opened.</summary>
    private static bool HasProfile(string anthropicConfigDirectory)
    {
        try
        {
            string configs = Path.Combine(anthropicConfigDirectory, "configs");
            return Directory.Exists(configs) && Directory.EnumerateFiles(configs, "*.json").Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
