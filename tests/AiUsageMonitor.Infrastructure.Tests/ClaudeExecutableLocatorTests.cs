using AiUsageMonitor.Infrastructure.Providers.Claude;

namespace AiUsageMonitor.Infrastructure.Tests;

public class ClaudeExecutableLocatorTests
{
    [Fact]
    public void NativeInstallLocationIsCheckedFirst()
    {
        IReadOnlyList<string> candidates = ClaudeExecutableLocator.CandidatePaths(
            userProfile: @"C:\Users\someone",
            appData: @"C:\Users\someone\AppData\Roaming",
            pathEnvironment: null);

        Assert.Equal(@"C:\Users\someone\.local\bin\claude.exe", candidates[0]);
    }

    [Fact]
    public void NpmGlobalShimIsCheckedAfterTheNativeInstall()
    {
        IReadOnlyList<string> candidates = ClaudeExecutableLocator.CandidatePaths(
            userProfile: @"C:\Users\someone",
            appData: @"C:\Users\someone\AppData\Roaming",
            pathEnvironment: null);

        Assert.Equal(@"C:\Users\someone\AppData\Roaming\npm\claude.cmd", candidates[1]);
    }

    [Fact]
    public void EveryPathDirectoryContributesBothExecutableForms()
    {
        IReadOnlyList<string> candidates = ClaudeExecutableLocator.CandidatePaths(
            userProfile: @"C:\Users\someone",
            appData: @"C:\Users\someone\AppData\Roaming",
            pathEnvironment: @"C:\tools;;  ;C:\other");

        Assert.Contains(@"C:\tools\claude.exe", candidates);
        Assert.Contains(@"C:\tools\claude.cmd", candidates);
        Assert.Contains(@"C:\other\claude.exe", candidates);
        Assert.Contains(@"C:\other\claude.cmd", candidates);
    }

    [Fact]
    public void BlankPathEntriesAreSkipped()
    {
        IReadOnlyList<string> candidates = ClaudeExecutableLocator.CandidatePaths(
            userProfile: @"C:\Users\someone",
            appData: @"C:\Users\someone\AppData\Roaming",
            pathEnvironment: @";  ;");

        Assert.Equal(2, candidates.Count);
    }

    [Theory]
    [InlineData("2.1.227 (Claude Code)\r\n", "2.1.227")]
    [InlineData("2.1.227\n", "2.1.227")]
    [InlineData("  2.1.227 (Claude Code)  ", "2.1.227")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void VersionIsTheFirstTokenOfTheFirstLine(string? stdout, string? expected) =>
        Assert.Equal(expected, ClaudeExecutableLocator.ParseVersion(stdout));
}
