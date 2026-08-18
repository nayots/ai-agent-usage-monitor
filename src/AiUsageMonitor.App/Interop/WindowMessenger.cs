namespace AiUsageMonitor.App.Interop;

/// <summary>
/// The real messenger: a registered window message aimed at the running instance's own process.
/// <para>
/// Aimed rather than broadcast because a broadcast cannot reach the widget at all — it sets
/// <c>ShowInTaskbar="False"</c>, so WPF gives it a hidden owner window, and <c>HWND_BROADCAST</c>
/// reaches only unowned top-level windows. The broadcast survives here purely as a fallback for a
/// running copy that published no process id to aim at, where it is the only thing left to try.
/// </para>
/// </summary>
public sealed class WindowMessenger : IInstanceMessenger
{
    private readonly Func<int, uint, bool> _postToProcess;
    private readonly Action<uint> _broadcast;

    public WindowMessenger()
        : this(SingleInstance.PostToProcess, SingleInstance.Broadcast)
    {
    }

    public WindowMessenger(Func<int, uint, bool> postToProcess, Action<uint> broadcast)
    {
        _postToProcess = postToProcess;
        _broadcast = broadcast;
    }

    public void RequestShow(RunningInstance? running) => Send(SingleInstance.ShowMessage, running);

    public void RequestQuit(RunningInstance? running) => Send(SingleInstance.QuitMessage, running);

    private void Send(uint message, RunningInstance? running)
    {
        if (running is not null && running.ProcessId > 0 && _postToProcess(running.ProcessId, message))
        {
            return;
        }

        _broadcast(message);
    }
}
