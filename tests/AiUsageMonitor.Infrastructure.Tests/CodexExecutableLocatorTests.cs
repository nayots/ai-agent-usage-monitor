using AiUsageMonitor.Infrastructure.Providers.Codex;

namespace AiUsageMonitor.Infrastructure.Tests;

public class CodexExecutableLocatorTests
{
    [Fact]
    public void ExactVendoredLayoutResolvesToTheExecutable()
    {
        using TempDirectory dir = new();
        string executable = CreateVendoredExecutable(
            dir.Path,
            "codex-win32-x64",
            "x86_64-pc-windows-msvc");

        string? result = CodexExecutableLocator.VendoredExecutableUnder(dir.Path);

        Assert.Equal(executable, result);
        Assert.False(result!.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase));
        Assert.False(result.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Arm64VendoredLayoutResolvesThroughTheFallbackSearch()
    {
        using TempDirectory dir = new();
        string executable = CreateVendoredExecutable(
            dir.Path,
            "codex-win32-arm64",
            "aarch64-pc-windows-msvc");

        string? result = CodexExecutableLocator.VendoredExecutableUnder(dir.Path);

        Assert.Equal(executable, result);
        Assert.EndsWith("codex.exe", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AShimAloneIsNeverReturnedAsAnExecutable()
    {
        using TempDirectory dir = new();
        File.WriteAllText(Path.Combine(dir.Path, "codex.cmd"), "@echo off");

        string? result = CodexExecutableLocator.VendoredExecutableUnder(dir.Path);

        Assert.Null(result);
    }

    [Fact]
    public void ShimDirectoriesPrioritizesAppDataAndSkipsBlankPathEntries()
    {
        string appData = @"C:\Users\someone\AppData\Roaming";
        string npm = Path.Combine(appData, "npm");
        string shimDirectory = @"C:\tools";
        string powershellShimDirectory = @"C:\powershell-tools";
        IReadOnlyList<string> directories = CodexExecutableLocator.ShimDirectories(
            appData,
            $@";  ;{shimDirectory};{powershellShimDirectory};C:\other",
            candidate => candidate == Path.Combine(shimDirectory, "codex.cmd")
                || candidate == Path.Combine(powershellShimDirectory, "codex.ps1"));

        Assert.Equal(npm, directories[0]);
        Assert.Contains(shimDirectory, directories);
        Assert.Contains(powershellShimDirectory, directories);
        Assert.DoesNotContain(@"C:\other", directories);
        Assert.DoesNotContain(directories, d => string.IsNullOrWhiteSpace(d));
        Assert.DoesNotContain(directories, d => d.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            || d.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MissingVendoredLayoutReturnsNull()
    {
        using TempDirectory dir = new();

        Assert.Null(CodexExecutableLocator.VendoredExecutableUnder(dir.Path));
    }

    private static string CreateVendoredExecutable(string prefix, string architecture, string triple)
    {
        string executable = Path.Combine(
            prefix,
            "node_modules",
            "@openai",
            "codex",
            "node_modules",
            "@openai",
            architecture,
            "vendor",
            triple,
            "bin",
            "codex.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllText(executable, string.Empty);
        return executable;
    }
}
