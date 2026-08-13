namespace AiUsageMonitor.App.Notifications;

public static class TickCadence
{
    public static readonly TimeSpan Visible = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan Hidden = TimeSpan.FromSeconds(5);

    public static TimeSpan For(bool isVisible) => isVisible ? Visible : Hidden;
}
