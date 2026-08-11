using AiUsageMonitor.Infrastructure.Logging;

namespace AiUsageMonitor.Infrastructure.Tests;

public class RollingFileWriterTests
{
    [Fact]
    public void WritesALineToTheCurrentFile()
    {
        using TempDirectory dir = new();
        using RollingFileWriter writer = new(dir.Path, "app", maxBytes: 1024, maxFiles: 3);

        writer.Write("hello");

        Assert.Equal(new[] { "hello" }, File.ReadAllLines(dir.File("app.log")));
    }

    [Fact]
    public void CreatesTheDirectoryWhenMissing()
    {
        using TempDirectory dir = new();
        string nested = Path.Combine(dir.Path, "logs");
        using RollingFileWriter writer = new(nested, "app", maxBytes: 1024, maxFiles: 3);

        writer.Write("hello");

        Assert.True(File.Exists(Path.Combine(nested, "app.log")));
    }

    [Fact]
    public void RotatesWhenTheCurrentFileWouldExceedTheLimit()
    {
        using TempDirectory dir = new();
        using RollingFileWriter writer = new(dir.Path, "app", maxBytes: 32, maxFiles: 3);

        writer.Write(new string('a', 30));
        writer.Write("after-rotation");

        Assert.Equal("after-rotation", File.ReadAllText(dir.File("app.log")).Trim());
        Assert.Contains("aaa", File.ReadAllText(dir.File("app.1.log")));
    }

    [Fact]
    public void NeverKeepsMoreThanMaxFiles()
    {
        using TempDirectory dir = new();
        using RollingFileWriter writer = new(dir.Path, "app", maxBytes: 16, maxFiles: 3);

        for (int i = 0; i < 20; i++)
        {
            writer.Write(new string((char)('a' + i % 26), 20));
        }

        Assert.Equal(3, Directory.GetFiles(dir.Path, "app*.log").Length);
    }

    [Fact]
    public void ConcurrentWritesNeverInterleaveWithinALine()
    {
        using TempDirectory dir = new();
        using RollingFileWriter writer = new(dir.Path, "app", maxBytes: 1_000_000, maxFiles: 2);

        Parallel.For(0, 200, i => writer.Write($"line-{i:D3}-END"));

        string[] lines = File.ReadAllLines(dir.File("app.log"));
        Assert.Equal(200, lines.Length);
        Assert.All(lines, line => Assert.EndsWith("-END", line));
    }
}
