using System.Text;

namespace AiUsageMonitor.Infrastructure.Logging;

/// <summary>
/// Appends whole lines to <c>{baseName}.log</c>, rotating to <c>{baseName}.1.log</c> and so on
/// once the current file would pass <paramref name="maxBytes"/>, and keeping at most
/// <paramref name="maxFiles"/> files. Writes are serialised: a log line is either fully present
/// or absent, never half-written.
/// </summary>
public sealed class RollingFileWriter : IDisposable
{
    private readonly Lock _gate = new();
    private readonly string _directory;
    private readonly string _baseName;
    private readonly long _maxBytes;
    private readonly int _maxFiles;
    private bool _disposed;

    public RollingFileWriter(string directory, string baseName, long maxBytes = 1_048_576, int maxFiles = 5)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFiles, 1);

        _directory = directory;
        _baseName = baseName;
        _maxBytes = maxBytes;
        _maxFiles = maxFiles;

        Directory.CreateDirectory(_directory);
    }

    private string CurrentPath => Path.Combine(_directory, _baseName + ".log");

    private string PathForGeneration(int generation) =>
        Path.Combine(_directory, _baseName + "." + generation + ".log");

    public void Write(string line)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                byte[] payload = Encoding.UTF8.GetBytes(line + Environment.NewLine);
                FileInfo current = new(CurrentPath);

                if (current.Exists && current.Length + payload.Length > _maxBytes)
                {
                    Rotate();
                }

                using FileStream stream = new(CurrentPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                stream.Write(payload);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never take the application down. A lost line is acceptable;
                // an unhandled exception on the UI thread because a log file was locked is not.
            }
        }
    }

    private void Rotate()
    {
        // Drop the oldest generation, shift the rest down, then move the current file to .1.
        string oldest = PathForGeneration(_maxFiles - 1);
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (int generation = _maxFiles - 2; generation >= 1; generation--)
        {
            string from = PathForGeneration(generation);
            if (File.Exists(from))
            {
                File.Move(from, PathForGeneration(generation + 1), overwrite: true);
            }
        }

        if (_maxFiles > 1)
        {
            File.Move(CurrentPath, PathForGeneration(1), overwrite: true);
        }
        else
        {
            File.Delete(CurrentPath);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }
    }
}
