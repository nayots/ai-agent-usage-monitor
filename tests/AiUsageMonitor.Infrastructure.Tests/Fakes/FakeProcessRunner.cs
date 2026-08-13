using AiUsageMonitor.Infrastructure.Providers;

namespace AiUsageMonitor.Infrastructure.Tests.Fakes;

public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Dictionary<(string ExePath, string Arguments), Queue<Func<CancellationToken, Task<(int ExitCode, string StdOut, string StdErr)>>>> _captured = [];
    private readonly Dictionary<(string ExePath, string Arguments), Queue<FakeProcessSession>> _sessions = [];
    private readonly Dictionary<string, int> _runCounts = new(StringComparer.OrdinalIgnoreCase);

    public int RunCapturedCallCount(string exePath) => _runCounts.TryGetValue(exePath, out int count) ? count : 0;

    public void EnqueueCaptured(string exePath, string arguments, int exitCode, string stdOut, string stdErr = "")
    {
        GetCapturedQueue(exePath, arguments).Enqueue(_ => Task.FromResult((exitCode, stdOut, stdErr)));
    }

    public void EnqueueCapturedFailure(string exePath, string arguments, Exception exception)
    {
        GetCapturedQueue(exePath, arguments).Enqueue(_ => Task.FromException<(int ExitCode, string StdOut, string StdErr)>(exception));
    }

    public void EnqueueSession(string exePath, string arguments, params string[] outputLines)
    {
        GetSessionQueue(exePath, arguments).Enqueue(new FakeProcessSession(outputLines));
    }

    public Task<(int ExitCode, string StdOut, string StdErr)> RunCapturedAsync(
        string exePath, string arguments, TimeSpan timeout, CancellationToken ct)
    {
        _runCounts[exePath] = RunCapturedCallCount(exePath) + 1;
        return GetCapturedQueue(exePath, arguments).Dequeue()(ct);
    }

    public IProcessSession Start(string exePath, string arguments) => GetSessionQueue(exePath, arguments).Dequeue();

    private Queue<Func<CancellationToken, Task<(int ExitCode, string StdOut, string StdErr)>>> GetCapturedQueue(string exePath, string arguments)
    {
        if (!_captured.TryGetValue((exePath, arguments), out Queue<Func<CancellationToken, Task<(int ExitCode, string StdOut, string StdErr)>>>? queue))
        {
            queue = [];
            _captured.Add((exePath, arguments), queue);
        }

        return queue;
    }

    private Queue<FakeProcessSession> GetSessionQueue(string exePath, string arguments)
    {
        if (!_sessions.TryGetValue((exePath, arguments), out Queue<FakeProcessSession>? queue))
        {
            queue = [];
            _sessions.Add((exePath, arguments), queue);
        }

        return queue;
    }

    public sealed class FakeProcessSession : IProcessSession
    {
        public FakeProcessSession(IEnumerable<string> outputLines)
        {
            StandardInput = new StringWriter();
            StandardOutput = new StringReader(string.Join(Environment.NewLine, outputLines));
        }

        public TextWriter StandardInput { get; }
        public TextReader StandardOutput { get; }
        public Task WaitForExitAsync(CancellationToken ct) => Task.CompletedTask;
        public void Dispose() { }
    }
}
