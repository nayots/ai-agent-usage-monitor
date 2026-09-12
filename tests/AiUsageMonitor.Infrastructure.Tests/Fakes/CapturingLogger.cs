using Microsoft.Extensions.Logging;

namespace AiUsageMonitor.Infrastructure.Tests.Fakes;

/// <summary>
/// Captures what was logged, with its level, so a test can assert both the record and its severity.
/// Formats each entry exactly as a real provider would, which is what lets a test assert that a
/// credential never reaches a log line.
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IReadOnlyList<string> Messages => [.. Entries.Select(e => e.Message)];

    public IEnumerable<string> MessagesAt(LogLevel level) =>
        Entries.Where(e => e.Level == level).Select(e => e.Message);

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoopDisposable.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception)));

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
