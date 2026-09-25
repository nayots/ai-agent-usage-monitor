using AiUsageMonitor.Infrastructure.Providers.Claude;
using AiUsageMonitor.Infrastructure.Tests.Fakes;

namespace AiUsageMonitor.Infrastructure.Tests;

public sealed class ClaudeApiKeySignInTests
{
    private const string Secret = "sk-ant-api03-NEVER-PRINT-ME";

    [Fact]
    public void AStoredConsoleKeyIsFound()
    {
        using var directory = new TempDirectory();
        string config = directory.File("claude.json");
        File.WriteAllText(config, $$"""{"primaryApiKey":"{{Secret}}","numStartups":3}""");

        Assert.Equal("primaryApiKey", Detect(config, directory.File("settings.json")));
    }

    [Fact]
    public void AnEmptyStoredKeyIsNotASignIn()
    {
        using var directory = new TempDirectory();
        string config = directory.File("claude.json");
        File.WriteAllText(config, """{"primaryApiKey":""}""");

        Assert.Null(Detect(config, directory.File("settings.json")));
    }

    [Fact]
    public void AnApiKeyHelperIsFound()
    {
        using var directory = new TempDirectory();
        string settings = directory.File("settings.json");
        File.WriteAllText(settings, """{"apiKeyHelper":"C:/tools/key.cmd"}""");

        Assert.Equal("apiKeyHelper", Detect(directory.File("claude.json"), settings));
    }

    [Fact]
    public void TheEnvironmentVariableIsFound()
    {
        using var directory = new TempDirectory();

        string? found = ClaudeApiKeySignIn.Detect(
            directory.File("claude.json"),
            directory.File("settings.json"),
            name => name == "ANTHROPIC_API_KEY" ? Secret : null);

        Assert.Equal("ANTHROPIC_API_KEY", found);
    }

    [Fact]
    public void NothingFoundIsNull()
    {
        using var directory = new TempDirectory();
        string config = directory.File("claude.json");
        File.WriteAllText(config, """{"oauthAccount":{"billingType":"stripe_subscription"}}""");

        Assert.Null(Detect(config, directory.File("settings.json")));
    }

    [Fact]
    public void UnreadableFilesAreNotASignInAndDoNotThrow()
    {
        using var directory = new TempDirectory();
        string config = directory.File("claude.json");
        File.WriteAllText(config, "{ not json");

        Assert.Null(Detect(config, directory.File("settings.json")));
    }

    /// <summary>The result names where the key was found, never the key.</summary>
    [Fact]
    public void TheResultNeverCarriesTheKey()
    {
        using var directory = new TempDirectory();
        string config = directory.File("claude.json");
        File.WriteAllText(config, $$"""{"primaryApiKey":"{{Secret}}"}""");

        Assert.DoesNotContain("sk-ant", Detect(config, directory.File("settings.json")), StringComparison.Ordinal);
    }

    private static string? Detect(string config, string settings) =>
        ClaudeApiKeySignIn.Detect(config, settings, _ => null);
}
