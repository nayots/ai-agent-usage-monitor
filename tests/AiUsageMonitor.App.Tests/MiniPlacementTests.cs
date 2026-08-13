using System.Windows;
using AiUsageMonitor.App.Interop;
using AiUsageMonitor.Infrastructure.Settings;

namespace AiUsageMonitor.App.Tests;

public class MiniPlacementTests
{
    [Fact]
    public void FitsTopAndBottomToTheirDockedEdges()
    {
        Rect work = new(10, 20, 300, 200);
        Assert.Equal(20, MiniPlacement.Fit(new Size(40, 30), work, MiniDock.Top, 50).Y);
        Assert.Equal(190, MiniPlacement.Fit(new Size(40, 30), work, MiniDock.Bottom, 50).Y);
    }

    [Fact]
    public void DefaultsAndClampsAlongTheWorkArea()
    {
        Rect work = new(10, 20, 300, 200);
        Assert.Equal(270, MiniPlacement.Fit(new Size(40, 30), work, MiniDock.Top, null).X);
        Assert.Equal(10, MiniPlacement.Fit(new Size(40, 30), work, MiniDock.Top, -1).X);
        Assert.Equal(270, MiniPlacement.Fit(new Size(40, 30), work, MiniDock.Top, 999).X);
        Assert.Equal(10, MiniPlacement.Fit(new Size(400, 30), work, MiniDock.Top, 999).X);
    }
}
