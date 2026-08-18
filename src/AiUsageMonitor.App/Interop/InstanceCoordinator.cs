using System.Threading;

namespace AiUsageMonitor.App.Interop;

/// <summary>What a starting process should do, once it knows whether anything else is running.</summary>
public enum InstanceOutcome
{
    /// <summary>This process owns the widget and should start normally.</summary>
    Start,

    /// <summary>Another copy is running and has been asked to show itself; exit quietly.</summary>
    Defer,

    /// <summary>Another copy would not release the mutex; the user has been told. Exit.</summary>
    Blocked,
}

/// <summary>How a starting instance talks to one that is already running.</summary>
public interface IInstanceMessenger
{
    void RequestShow();

    void RequestQuit();
}

/// <summary>The two questions a takeover can put to the user.</summary>
public interface IInstancePrompts
{
    bool ConfirmReplace(RunningInstance running);

    void ReportReplaceFailed();
}

/// <summary>
/// Decides what a second instance does, which is not always "show the first one and exit".
/// <para>
/// Releases carry the version in the file name, so an upgraded copy is a different file from the one
/// already running. Without this, launching the copy you just downloaded silently surfaces the old
/// version's window and exits — leaving the user on the previous release while believing they
/// upgraded. A second copy of the <em>same</em> file still behaves exactly as it always has.
/// </para>
/// </summary>
public sealed class InstanceCoordinator
{
    private readonly string _mutexName;
    private readonly string? _executablePath;
    private readonly string _version;
    private readonly RunningInstanceFile _record;
    private readonly IInstanceMessenger _messenger;
    private readonly IInstancePrompts _prompts;
    private readonly TimeSpan _replaceTimeout;
    private readonly TimeSpan _pollInterval;

    public InstanceCoordinator(
        string mutexName,
        string? executablePath,
        string version,
        RunningInstanceFile record,
        IInstanceMessenger messenger,
        IInstancePrompts prompts,
        TimeSpan? replaceTimeout = null,
        TimeSpan? pollInterval = null)
    {
        _mutexName = mutexName;
        _executablePath = executablePath;
        _version = version;
        _record = record;
        _messenger = messenger;
        _prompts = prompts;
        _replaceTimeout = replaceTimeout ?? TimeSpan.FromSeconds(5);
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(100);
    }

    public InstanceOutcome Acquire(out SingleInstance? instance)
    {
        if (SingleInstance.TryAcquire(_mutexName, out instance))
        {
            Claim();
            return InstanceOutcome.Start;
        }

        RunningInstance? running = _record.Read();

        // No record means a release that predates this mechanism, or a copy that could not write
        // one. Either way there is nothing to identify, so behave as this application always has.
        if (running is null || IsThisExecutable(running) || !_prompts.ConfirmReplace(running))
        {
            _messenger.RequestShow();
            return InstanceOutcome.Defer;
        }

        _messenger.RequestQuit();

        if (!WaitForRelease(out instance))
        {
            _prompts.ReportReplaceFailed();
            return InstanceOutcome.Blocked;
        }

        Claim();
        return InstanceOutcome.Start;
    }

    /// <summary>
    /// Call before releasing the mutex, never after. A replacing instance is polling for the mutex
    /// and writes its own record the moment it acquires one; releasing first would let it write a
    /// record that this call then deletes.
    /// </summary>
    public void Release() => _record.Delete();

    private bool IsThisExecutable(RunningInstance running) =>
        _executablePath is not null
        && string.Equals(running.ExecutablePath, _executablePath, StringComparison.OrdinalIgnoreCase);

    private void Claim()
    {
        // A process that cannot name its own executable cannot be told apart from another copy, and
        // a record claiming an empty path would make every later launch look like an upgrade.
        if (_executablePath is null)
        {
            _record.Delete();
            return;
        }

        _record.Write(new RunningInstance(_executablePath, _version));
    }

    /// <summary>
    /// Polls until the named mutex can be created. Creating it is the ground truth that the previous
    /// holder is gone: an open handle keeps the named object alive, so nothing short of a successful
    /// create proves the process behind it has actually exited.
    /// </summary>
    private bool WaitForRelease(out SingleInstance? instance)
    {
        DateTime deadline = DateTime.UtcNow + _replaceTimeout;

        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(_pollInterval);

            if (SingleInstance.TryAcquire(_mutexName, out instance))
            {
                return true;
            }
        }

        instance = null;
        return false;
    }
}
