using AiUsageMonitor.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace AiUsageMonitor.Infrastructure.Tests;

public class RollingFileLoggerProviderTests
{
    [Fact]
    public void WritesOneLinePerEntryWithLevelAndCategory()
    {
        using TempDirectory dir = new();
        using RollingFileWriter writer = new(dir.Path, "app");
        using RollingFileLoggerProvider provider = new(writer);

        provider.CreateLogger("Probe.Codex").LogWarning("rate limit read failed");

        string[] lines = File.ReadAllLines(dir.File("app.log"));
        Assert.Single(lines);
        Assert.Contains("[WRN]", lines[0]);
        Assert.Contains("Probe.Codex", lines[0]);
        Assert.Contains("rate limit read failed", lines[0]);
    }

    [Fact]
    public void CollapsesMultiLineMessagesOntoOneLine()
    {
        // One entry must stay one line: a log file where an entry can span lines cannot be
        // read back reliably by anything, including the diagnostics screen.
        using TempDirectory dir = new();
        using RollingFileWriter writer = new(dir.Path, "app");
        using RollingFileLoggerProvider provider = new(writer);

        provider.CreateLogger("X").LogError("first\r\nsecond\nthird");

        Assert.Single(File.ReadAllLines(dir.File("app.log")));
    }

    [Fact]
    public void IncludesExceptionTypeAndMessage()
    {
        using TempDirectory dir = new();
        using RollingFileWriter writer = new(dir.Path, "app");
        using RollingFileLoggerProvider provider = new(writer);

        provider.CreateLogger("X").LogError(new InvalidOperationException("boom"), "probe failed");

        string text = File.ReadAllText(dir.File("app.log"));
        Assert.Contains("InvalidOperationException", text);
        Assert.Contains("boom", text);
    }

    [Fact]
    public void SuppressedLevelsWriteNothing()
    {
        using TempDirectory dir = new();
        using RollingFileWriter writer = new(dir.Path, "app");
        using RollingFileLoggerProvider provider = new(writer, LogLevel.Warning);

        provider.CreateLogger("X").LogInformation("chatter");

        Assert.False(File.Exists(dir.File("app.log")));
    }

    [Fact]
    public void DefaultDirectoryIsUnderTheCurrentUsersLocalProfile()
    {
        string expectedRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(expectedRoot, RollingFileLoggerProvider.DefaultDirectory);
    }
}
