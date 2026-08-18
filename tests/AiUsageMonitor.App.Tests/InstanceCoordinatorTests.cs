using System.IO;
using AiUsageMonitor.App.Interop;

namespace AiUsageMonitor.App.Tests;

/// <summary>
/// Exercises real named mutexes and a real record file. The mutex name is unique per test so the
/// suite never collides with itself or with a widget the developer has running.
/// <para>
/// The messenger is a fake for a second reason beyond assertion: the production one posts a real
/// quit message to a real process, so a test that used it could shut down the developer's own
/// running widget.
/// </para>
/// </summary>
public sealed class InstanceCoordinatorTests : IDisposable
{
    private readonly string _mutexName = $"AiUsageMonitor.Tests.{Guid.NewGuid():N}";
    private readonly string _recordPath = Path.Combine(
        Path.GetTempPath(),
        $"aium-coordinator-{Guid.NewGuid():N}.json");

    private const string First = @"C:\downloads\widget-v1.exe";
    private const string Second = @"C:\downloads\widget-v2.exe";

    private sealed class FakeMessenger : IInstanceMessenger
    {
        public int ShowRequests { get; private set; }
        public int QuitRequests { get; private set; }

        public RunningInstance? ShowTarget { get; private set; }
        public RunningInstance? QuitTarget { get; private set; }

        public void RequestShow(RunningInstance? running)
        {
            ShowRequests++;
            ShowTarget = running;
        }

        public void RequestQuit(RunningInstance running)
        {
            QuitRequests++;
            QuitTarget = running;
        }
    }

    private sealed class FakePrompts : IInstancePrompts
    {
        private readonly Func<RunningInstance, bool> _answer;

        public FakePrompts(Func<RunningInstance, bool> answer) => _answer = answer;

        public RunningInstance? Asked { get; private set; }
        public int FailuresReported { get; private set; }

        public bool ConfirmReplace(RunningInstance running)
        {
            Asked = running;
            return _answer(running);
        }

        public void ReportReplaceFailed() => FailuresReported++;
    }

    private InstanceCoordinator Coordinator(
        string? executablePath,
        string version,
        IInstanceMessenger messenger,
        IInstancePrompts prompts) =>
        new(_mutexName,
            executablePath,
            version,
            new RunningInstanceFile(_recordPath),
            messenger,
            prompts,
            replaceTimeout: TimeSpan.FromMilliseconds(300),
            pollInterval: TimeSpan.FromMilliseconds(20));

    private static FakePrompts NeverAsked() =>
        new(_ => throw new InvalidOperationException("The user must not be prompted on this path."));

    [Fact]
    public void TheFirstInstanceStartsAndRecordsItself()
    {
        FakeMessenger messenger = new();
        InstanceCoordinator coordinator = Coordinator(First, "0.1.5", messenger, NeverAsked());

        InstanceOutcome outcome = coordinator.Acquire(out SingleInstance? instance);

        using (instance)
        {
            Assert.Equal(InstanceOutcome.Start, outcome);
            Assert.NotNull(instance);
            Assert.Equal(0, messenger.ShowRequests);

            RunningInstance? record = new RunningInstanceFile(_recordPath).Read();
            Assert.Equal(First, record!.ExecutablePath);
            Assert.Equal("0.1.5", record.Version);
        }
    }

    [Fact]
    public void ASecondCopyOfTheSameFileDefersWithoutAsking()
    {
        // The common case by a wide margin: clicking the shortcut of an app that is already running.
        // It must stay exactly as it was and never grow a dialog.
        InstanceCoordinator running = Coordinator(First, "0.1.5", new FakeMessenger(), NeverAsked());
        running.Acquire(out SingleInstance? held);

        using (held)
        {
            FakeMessenger messenger = new();
            InstanceCoordinator second = Coordinator(First, "0.1.5", messenger, NeverAsked());

            InstanceOutcome outcome = second.Acquire(out SingleInstance? instance);

            Assert.Equal(InstanceOutcome.Defer, outcome);
            Assert.Null(instance);
            Assert.Equal(1, messenger.ShowRequests);
            Assert.Equal(0, messenger.QuitRequests);
        }
    }

    [Fact]
    public void AMissingRecordDefersWithoutAsking()
    {
        // What an upgrade from a release that predates the record file looks like: the mutex is held
        // but nothing on disk says by what. Falling back to today's behaviour is the honest answer.
        SingleInstance.TryAcquire(_mutexName, out SingleInstance? held);

        using (held)
        {
            FakeMessenger messenger = new();
            InstanceCoordinator second = Coordinator(Second, "0.1.6", messenger, NeverAsked());

            InstanceOutcome outcome = second.Acquire(out SingleInstance? instance);

            Assert.Equal(InstanceOutcome.Defer, outcome);
            Assert.Null(instance);
            Assert.Equal(1, messenger.ShowRequests);
        }
    }

    [Fact]
    public void ADifferentExecutableAsksAndDefersWhenDeclined()
    {
        InstanceCoordinator running = Coordinator(First, "0.1.5", new FakeMessenger(), NeverAsked());
        running.Acquire(out SingleInstance? held);

        using (held)
        {
            FakeMessenger messenger = new();
            FakePrompts prompts = new(_ => false);
            InstanceCoordinator upgraded = Coordinator(Second, "0.1.6", messenger, prompts);

            InstanceOutcome outcome = upgraded.Acquire(out SingleInstance? instance);

            Assert.Equal(InstanceOutcome.Defer, outcome);
            Assert.Null(instance);
            Assert.Equal(First, prompts.Asked!.ExecutablePath);
            Assert.Equal("0.1.5", prompts.Asked.Version);
            Assert.Equal(1, messenger.ShowRequests);
            Assert.Equal(0, messenger.QuitRequests);
        }
    }

    [Fact]
    public void ADifferentExecutableTakesOverWhenAccepted()
    {
        InstanceCoordinator running = Coordinator(First, "0.1.5", new FakeMessenger(), NeverAsked());
        running.Acquire(out SingleInstance? held);

        FakeMessenger messenger = new();

        // Standing in for the running copy honouring the quit message. It has to happen on this
        // thread: Mutex.ReleaseMutex throws if it is called from a thread that does not own the
        // mutex, and the prompt is invoked synchronously from Acquire.
        FakePrompts prompts = new(_ =>
        {
            held!.Dispose();
            return true;
        });

        InstanceCoordinator upgraded = Coordinator(Second, "0.1.6", messenger, prompts);

        InstanceOutcome outcome = upgraded.Acquire(out SingleInstance? instance);

        using (instance)
        {
            Assert.Equal(InstanceOutcome.Start, outcome);
            Assert.NotNull(instance);
            Assert.Equal(1, messenger.QuitRequests);
            Assert.Equal(0, messenger.ShowRequests);
            Assert.Equal(0, prompts.FailuresReported);

            RunningInstance? record = new RunningInstanceFile(_recordPath).Read();
            Assert.Equal(Second, record!.ExecutablePath);
            Assert.Equal("0.1.6", record.Version);
        }
    }

    [Fact]
    public void ATakeoverThatIsIgnoredReportsFailureAndDoesNotStart()
    {
        InstanceCoordinator running = Coordinator(First, "0.1.5", new FakeMessenger(), NeverAsked());
        running.Acquire(out SingleInstance? held);

        using (held)
        {
            FakeMessenger messenger = new();
            FakePrompts prompts = new(_ => true);
            InstanceCoordinator upgraded = Coordinator(Second, "0.1.6", messenger, prompts);

            InstanceOutcome outcome = upgraded.Acquire(out SingleInstance? instance);

            Assert.Equal(InstanceOutcome.Blocked, outcome);
            Assert.Null(instance);
            Assert.Equal(1, messenger.QuitRequests);
            Assert.Equal(1, prompts.FailuresReported);

            // The copy that would not die still owns the record.
            Assert.Equal(First, new RunningInstanceFile(_recordPath).Read()!.ExecutablePath);
        }
    }

    [Fact]
    public void ReleaseRemovesTheRecord()
    {
        InstanceCoordinator coordinator = Coordinator(First, "0.1.5", new FakeMessenger(), NeverAsked());
        coordinator.Acquire(out SingleInstance? instance);

        using (instance)
        {
            coordinator.Release();

            Assert.Null(new RunningInstanceFile(_recordPath).Read());
        }
    }

    [Fact]
    public void WithoutAKnownExecutableNothingIsRecorded()
    {
        InstanceCoordinator coordinator = Coordinator(null, "0.1.6", new FakeMessenger(), NeverAsked());

        InstanceOutcome outcome = coordinator.Acquire(out SingleInstance? instance);

        using (instance)
        {
            Assert.Equal(InstanceOutcome.Start, outcome);
            Assert.Null(new RunningInstanceFile(_recordPath).Read());
        }
    }

    [Fact]
    public void TheQuitIsAimedAtTheProcessTheRecordNames()
    {
        // A broadcast cannot reach the widget at all - it is an owned window - so the message has to
        // be aimed, and the only thing that says where is the record.
        InstanceCoordinator running = Coordinator(First, "0.1.5", new FakeMessenger(), NeverAsked());
        running.Acquire(out SingleInstance? held);

        using (held)
        {
            FakeMessenger messenger = new();
            InstanceCoordinator upgraded = Coordinator(Second, "0.1.6", messenger, new FakePrompts(_ => true));

            upgraded.Acquire(out _);

            Assert.Equal(Environment.ProcessId, messenger.QuitTarget!.ProcessId);
            Assert.Equal(First, messenger.QuitTarget.ExecutablePath);
        }
    }

    [Fact]
    public void DeferringDoesNotDeleteTheRunningInstancesRecord()
    {
        // App.OnExit runs on every exit path, this one included, and it calls Release. A coordinator
        // that never acquired anything must therefore release nothing: deleting here would throw
        // away a record that still belongs to the copy that is still running, and the next launch of
        // a different executable would read null and silently skip the takeover offer.
        InstanceCoordinator running = Coordinator(First, "0.1.5", new FakeMessenger(), NeverAsked());
        running.Acquire(out SingleInstance? held);

        using (held)
        {
            InstanceCoordinator second = Coordinator(First, "0.1.5", new FakeMessenger(), NeverAsked());
            second.Acquire(out _);

            second.Release();

            Assert.Equal(First, new RunningInstanceFile(_recordPath).Read()!.ExecutablePath);
        }
    }

    [Fact]
    public void ABlockedTakeoverDoesNotDeleteTheRunningInstancesRecord()
    {
        InstanceCoordinator running = Coordinator(First, "0.1.5", new FakeMessenger(), NeverAsked());
        running.Acquire(out SingleInstance? held);

        using (held)
        {
            InstanceCoordinator upgraded = Coordinator(Second, "0.1.6", new FakeMessenger(), new FakePrompts(_ => true));
            Assert.Equal(InstanceOutcome.Blocked, upgraded.Acquire(out _));

            upgraded.Release();

            Assert.Equal(First, new RunningInstanceFile(_recordPath).Read()!.ExecutablePath);
        }
    }

    public void Dispose()
    {
        try
        {
            File.Delete(_recordPath);
        }
        catch (IOException)
        {
            // A locked file must not fail an otherwise passing test.
        }
    }
}
