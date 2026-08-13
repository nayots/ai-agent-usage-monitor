namespace AiUsageMonitor.App.Notifications;

public static class TickCadence
{
    public static readonly TimeSpan Visible = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan Hidden = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How often the window asks the refresh service whether any provider is due. Not the refresh
    /// interval: the service owns that, per provider, so this only has to be short enough that a
    /// 15-second interval is not measurably late. A tick with nothing due costs one dictionary
    /// lookup per provider and starts no work.
    /// </summary>
    public static readonly TimeSpan Poll = TimeSpan.FromSeconds(5);

    public static TimeSpan For(bool isVisible) => isVisible ? Visible : Hidden;
}
