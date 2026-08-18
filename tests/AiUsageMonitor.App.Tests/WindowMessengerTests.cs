using AiUsageMonitor.App.Interop;

namespace AiUsageMonitor.App.Tests;

/// <summary>
/// Covers the choice between aiming a message and broadcasting it. The interop itself is faked:
/// a real post would reach whatever widget the developer has running.
/// </summary>
public sealed class WindowMessengerTests
{
    private static RunningInstance Running(int processId) =>
        new(@"C:\downloads\widget-v1.exe", "0.1.5", processId);

    private sealed class Recorder
    {
        public List<(int ProcessId, uint Message)> Posts { get; } = [];
        public List<uint> Broadcasts { get; } = [];

        public bool PostToProcess(int processId, uint message)
        {
            Posts.Add((processId, message));
            return FoundAWindow;
        }

        public void Broadcast(uint message) => Broadcasts.Add(message);

        public bool FoundAWindow { get; set; } = true;
    }

    [Fact]
    public void AQuitIsAimedAtTheRunningProcess()
    {
        // The whole point of the record's process id. A broadcast cannot reach the widget: it sets
        // ShowInTaskbar="False", so WPF gives it a hidden owner window, and HWND_BROADCAST reaches
        // only unowned top-level windows.
        Recorder recorder = new();
        WindowMessenger messenger = new(recorder.PostToProcess, recorder.Broadcast);

        messenger.RequestQuit(Running(4321));

        Assert.Equal((4321, SingleInstance.QuitMessage), Assert.Single(recorder.Posts));
        Assert.Empty(recorder.Broadcasts);
    }

    [Fact]
    public void AShowIsAimedTheSameWay()
    {
        Recorder recorder = new();
        WindowMessenger messenger = new(recorder.PostToProcess, recorder.Broadcast);

        messenger.RequestShow(Running(4321));

        Assert.Equal((4321, SingleInstance.ShowMessage), Assert.Single(recorder.Posts));
        Assert.Empty(recorder.Broadcasts);
    }

    [Fact]
    public void WithNothingToAimAtItFallsBackToABroadcast()
    {
        // A release that predates the record file. The broadcast will not reach it either, but it
        // costs nothing and is the only thing left to try.
        Recorder recorder = new();
        WindowMessenger messenger = new(recorder.PostToProcess, recorder.Broadcast);

        messenger.RequestShow(null);

        Assert.Empty(recorder.Posts);
        Assert.Equal(SingleInstance.ShowMessage, Assert.Single(recorder.Broadcasts));
    }

    [Fact]
    public void ARecordWrittenBeforeProcessIdsExistedFallsBackToABroadcast()
    {
        Recorder recorder = new();
        WindowMessenger messenger = new(recorder.PostToProcess, recorder.Broadcast);

        messenger.RequestShow(Running(0));

        Assert.Empty(recorder.Posts);
        Assert.Equal(SingleInstance.ShowMessage, Assert.Single(recorder.Broadcasts));
    }

    [Fact]
    public void AProcessWithNoWindowsLeftFallsBackToABroadcast()
    {
        // The recorded process died between the record being written and this message being sent.
        Recorder recorder = new() { FoundAWindow = false };
        WindowMessenger messenger = new(recorder.PostToProcess, recorder.Broadcast);

        messenger.RequestQuit(Running(4321));

        Assert.Single(recorder.Posts);
        Assert.Equal(SingleInstance.QuitMessage, Assert.Single(recorder.Broadcasts));
    }
}
