using AiUsageMonitor.App.Notifications;

namespace AiUsageMonitor.App.Tests;

public sealed class WidgetTickCadenceTests
{
    [Fact]
    public void VisiblePresentationUpdatesEverySecond()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), TickCadence.Visible);
        Assert.Equal(TickCadence.Visible, TickCadence.For(isVisible: true));
    }

    [Fact]
    public void HiddenPresentationUpdatesEveryFiveSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), TickCadence.Hidden);
        Assert.Equal(TickCadence.Hidden, TickCadence.For(isVisible: false));
    }
}
