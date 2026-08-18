namespace AiUsageMonitor.App.Interop;

/// <summary>The real messenger: registered broadcast window messages.</summary>
internal sealed class WindowMessenger : IInstanceMessenger
{
    public void RequestShow() => SingleInstance.BroadcastShow();

    public void RequestQuit() => SingleInstance.BroadcastQuit();
}
