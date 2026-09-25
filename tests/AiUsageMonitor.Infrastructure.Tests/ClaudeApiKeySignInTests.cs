using AiUsageMonitor.Infrastructure.Providers.Claude;

namespace AiUsageMonitor.Infrastructure.Tests;

public sealed class ClaudeApiKeySignInTests
{
    private const string Secret = "sk-ant-api03-NEVER-PRINT-ME";

    [Fact]
    public void AStoredConsoleKeyInTheGlobalConfigIsFound()
    {
        using var directory = new TempDirectory();
        ClaudeApiKeySignIn.Locations locations = Locations(directory);
        File.WriteAllText(locations.GlobalConfig, $$"""{"primaryApiKey":"{{Secret}}","numStartups":3}""");

        Assert.Equal("primaryApiKey", Detect(locations));
    }

    /// <summary>The legacy Console route stores its key "with your other credentials".</summary>
    [Fact]
    public void AStoredConsoleKeyInTheCredentialsFileIsFound()
    {
        using var directory = new TempDirectory();
        ClaudeApiKeySignIn.Locations locations = Locations(directory);
        File.WriteAllText(locations.Credentials, $$"""{"primaryApiKey":"{{Secret}}"}""");

        Assert.Equal("primaryApiKey", Detect(locations));
    }

    [Fact]
    public void AnEmptyStoredKeyIsNotASignIn()
    {
        using var directory = new TempDirectory();
        ClaudeApiKeySignIn.Locations locations = Locations(directory);
        File.WriteAllText(locations.GlobalConfig, """{"primaryApiKey":""}""");

        Assert.Null(Detect(locations));
    }

    [Fact]
    public void AnApiKeyHelperIsFound()
    {
        using var directory = new TempDirectory();
        ClaudeApiKeySignIn.Locations locations = Locations(directory);
        File.WriteAllText(locations.Settings, """{"apiKeyHelper":"C:/tools/key.cmd"}""");

        Assert.Equal("apiKeyHelper", Detect(locations));
    }

    /// <summary>
    /// The keyless Console sign-in (Claude Code 2.1.242+) stores no key at all: it writes an
    /// Anthropic profile and signs out of any claude.ai login.
    /// </summary>
    [Fact]
    public void AnAnthropicProfileIsFound()
    {
        using var directory = new TempDirectory();
        ClaudeApiKeySignIn.Locations locations = Locations(directory);
        Directory.CreateDirectory(Path.Combine(locations.AnthropicConfigDirectory, "configs"));
        File.WriteAllText(Path.Combine(locations.AnthropicConfigDirectory, "configs", "default.json"), "{}");

        Assert.Equal("Anthropic profile", Detect(locations));
    }

    [Theory]
    [InlineData("ANTHROPIC_API_KEY")]
    [InlineData("ANTHROPIC_AUTH_TOKEN")]
    [InlineData("ANTHROPIC_PROFILE")]
    public void AnEnvironmentCredentialIsFound(string variable)
    {
        using var directory = new TempDirectory();

        string? found = ClaudeApiKeySignIn.Detect(Locations(directory), name => name == variable ? Secret : null);

        Assert.Equal(variable, found);
    }

    [Theory]
    [InlineData("CLAUDE_CODE_USE_BEDROCK")]
    [InlineData("CLAUDE_CODE_USE_VERTEX")]
    [InlineData("CLAUDE_CODE_USE_FOUNDRY")]
    public void ACloudProviderIsFoundAndNamedAsOne(string variable)
    {
        using var directory = new TempDirectory();

        string? found = ClaudeApiKeySignIn.Detect(Locations(directory), name => name == variable ? "1" : null);

        Assert.Equal(variable, found);
        Assert.True(ClaudeApiKeySignIn.IsCloudProvider(found!));
    }

    [Fact]
    public void NothingFoundIsNull()
    {
        using var directory = new TempDirectory();
        ClaudeApiKeySignIn.Locations locations = Locations(directory);
        File.WriteAllText(locations.GlobalConfig, """{"oauthAccount":{"billingType":"stripe_subscription"}}""");
        File.WriteAllText(locations.Credentials, """{"mcpOAuth":{}}""");

        Assert.Null(Detect(locations));
    }

    [Fact]
    public void UnreadableFilesAreNotASignInAndDoNotThrow()
    {
        using var directory = new TempDirectory();
        ClaudeApiKeySignIn.Locations locations = Locations(directory);
        File.WriteAllText(locations.GlobalConfig, "{ not json");
        File.WriteAllText(locations.Credentials, "[1,2");

        Assert.Null(Detect(locations));
    }

    /// <summary>The result names where the key was found, never the key.</summary>
    [Fact]
    public void TheResultNeverCarriesTheKey()
    {
        using var directory = new TempDirectory();
        ClaudeApiKeySignIn.Locations locations = Locations(directory);
        File.WriteAllText(locations.GlobalConfig, $$"""{"primaryApiKey":"{{Secret}}"}""");

        Assert.DoesNotContain("sk-ant", Detect(locations), StringComparison.Ordinal);
    }

    [Fact]
    public void ClaudeConfigDirMovesEveryClaudeFileButNotTheAnthropicProfiles()
    {
        ClaudeApiKeySignIn.Locations locations = ClaudeApiKeySignIn.Locations.For(
            userProfile: "C:/home", appData: "C:/roaming", environment: name => name == "CLAUDE_CONFIG_DIR" ? "D:/claude" : null);

        Assert.Equal(Path.Combine("D:/claude", ".credentials.json"), locations.Credentials);
        Assert.Equal(Path.Combine("D:/claude", "settings.json"), locations.Settings);
        Assert.Equal(Path.Combine("D:/claude", ".claude.json"), locations.GlobalConfig);
        Assert.Equal(Path.Combine("C:/roaming", "Anthropic"), locations.AnthropicConfigDirectory);
    }

    [Fact]
    public void WithoutClaudeConfigDirTheFilesAreInTheUserProfile()
    {
        ClaudeApiKeySignIn.Locations locations = ClaudeApiKeySignIn.Locations.For(
            userProfile: "C:/home", appData: "C:/roaming", environment: _ => null);

        Assert.Equal(Path.Combine("C:/home", ".claude", ".credentials.json"), locations.Credentials);
        Assert.Equal(Path.Combine("C:/home", ".claude", "settings.json"), locations.Settings);
        Assert.Equal(Path.Combine("C:/home", ".claude.json"), locations.GlobalConfig);
    }

    private static ClaudeApiKeySignIn.Locations Locations(TempDirectory directory) => new(
        GlobalConfig: directory.File("claude.json"),
        Settings: directory.File("settings.json"),
        Credentials: directory.File("credentials.json"),
        AnthropicConfigDirectory: directory.File("anthropic"));

    private static string? Detect(ClaudeApiKeySignIn.Locations locations) =>
        ClaudeApiKeySignIn.Detect(locations, _ => null);
}
