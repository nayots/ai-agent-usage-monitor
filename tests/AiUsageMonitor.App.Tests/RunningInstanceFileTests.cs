using System.IO;
using AiUsageMonitor.App.Interop;

namespace AiUsageMonitor.App.Tests;

/// <summary>
/// Exercises the real file system under a scratch path rather than a mock of the API under test.
/// The file is deleted in Dispose whatever the test did.
/// </summary>
public sealed class RunningInstanceFileTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        $"aium-running-{Guid.NewGuid():N}.json");

    [Fact]
    public void NothingWrittenReadsAsNoInstance() => Assert.Null(new RunningInstanceFile(_path).Read());

    [Fact]
    public void WhatIsWrittenIsWhatIsRead()
    {
        RunningInstanceFile file = new(_path);

        file.Write(new RunningInstance(@"C:\downloads\widget-v2.exe", "0.1.6"));

        RunningInstance? read = file.Read();
        Assert.NotNull(read);
        Assert.Equal(@"C:\downloads\widget-v2.exe", read!.ExecutablePath);
        Assert.Equal("0.1.6", read.Version);
    }

    [Fact]
    public void WritingTwiceKeepsTheSecondRecord()
    {
        RunningInstanceFile file = new(_path);

        file.Write(new RunningInstance(@"C:\a\widget.exe", "0.1.5"));
        file.Write(new RunningInstance(@"C:\b\widget.exe", "0.1.6"));

        Assert.Equal(@"C:\b\widget.exe", file.Read()!.ExecutablePath);
    }

    [Fact]
    public void DeletingRemovesTheRecordAndIsSafeWhenAbsent()
    {
        RunningInstanceFile file = new(_path);
        file.Write(new RunningInstance(@"C:\a\widget.exe", "0.1.5"));

        file.Delete();
        file.Delete();

        Assert.Null(file.Read());
    }

    [Fact]
    public void ACorruptRecordReadsAsNoInstance()
    {
        // A half-written file left by a hard power-off must not stop the application starting.
        File.WriteAllText(_path, "{ this is not json");

        Assert.Null(new RunningInstanceFile(_path).Read());
    }

    [Fact]
    public void ARecordWithNoExecutablePathReadsAsNoInstance()
    {
        File.WriteAllText(_path, """{ "ExecutablePath": "", "Version": "0.1.6" }""");

        Assert.Null(new RunningInstanceFile(_path).Read());
    }

    [Fact]
    public void TheDefaultPathSitsBesideTheSettingsFile()
    {
        Assert.Equal(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AiUsageMonitor",
                "running-instance.json"),
            RunningInstanceFile.DefaultPath);
    }

    public void Dispose()
    {
        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
            // A locked file must not fail an otherwise passing test.
        }
    }
}
