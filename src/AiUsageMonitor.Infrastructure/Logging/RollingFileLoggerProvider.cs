using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace AiUsageMonitor.Infrastructure.Logging;

/// <summary>
/// Writes log entries to a rotating local file, one line per entry. Local only: nothing is ever
/// transmitted, and no provider credential ever reaches a log line because none is passed to one.
/// </summary>
public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private readonly RollingFileWriter _writer;
    private readonly LogLevel _minimumLevel;

    public RollingFileLoggerProvider(RollingFileWriter writer, LogLevel minimumLevel = LogLevel.Information)
    {
        _writer = writer;
        _minimumLevel = minimumLevel;
    }

    /// <summary>%LOCALAPPDATA%\AiUsageMonitor\logs, resolved for whichever user is running.</summary>
    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AiUsageMonitor",
        "logs");

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _writer, _minimumLevel));

    public void Dispose() => _writer.Dispose();

    private sealed class FileLogger(string category, RollingFileWriter writer, LogLevel minimumLevel) : ILogger
    {
        private static readonly char[] NewLineCharacters = ['\r', '\n'];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= minimumLevel && logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string message = formatter(state, exception);
            if (exception is not null)
            {
                message += $" | {exception.GetType().Name}: {exception.Message}";
            }

            string timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            writer.Write($"{timestamp} [{Abbreviate(logLevel)}] {category}: {Flatten(message)}");
        }

        private static string Flatten(string message) =>
            message.IndexOfAny(NewLineCharacters) < 0
                ? message
                : string.Join(" ", message.Split(NewLineCharacters, StringSplitOptions.RemoveEmptyEntries));

        private static string Abbreviate(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???"
        };
    }
}
