using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Media;
using AiUsageMonitor.App.Interop;
using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Infrastructure.Theming;

namespace AiUsageMonitor.App.Tests;

[Collection("wpf")]
public class TrayIconFramesTests(WpfFixture wpf)
{
    private static readonly TrayGlyphPalette Palette = new(
        Colors.Black, Colors.Blue, Colors.Orange, Colors.Red, Colors.Gray, Colors.DarkRed, Colors.White);

    private static TrayGlyphState State(params TrayGlyphFrame[] frames) => new(frames);

    private static TrayGlyphFrame Reading(string mark) =>
        new(mark, "44", 44d, QuotaBarFill.Accent, TrayFrameKind.Reading);

    /// <summary>
    /// One handle per frame, now that a frame names itself. The pair existed only so a turn could
    /// open on the monogram and then swap to the number; there is nothing left to swap to.
    /// </summary>
    [Fact]
    public void EveryFrameGetsExactlyOneIconAndNoTwoFramesShareIt() => wpf.Invoke(() =>
    {
        using TrayIconFrames frames = TrayIconFrames.Build(
            State(Reading("CC"), new("CX", null, null, QuotaBarFill.Accent, TrayFrameKind.Failed)),
            16,
            Palette);

        Assert.Equal(2, frames.Count);
        Assert.NotEqual(IntPtr.Zero, frames.Icon(0));
        Assert.NotEqual(IntPtr.Zero, frames.Icon(1));
        Assert.NotEqual(frames.Icon(0), frames.Icon(1));

        // Borrowed, not minted: asking twice must hand back the same handle.
        Assert.Equal(frames.Icon(0), frames.Icon(0));
    });

    [Fact]
    public void AnIndexOutsideTheSetYieldsNoHandleRatherThanThrowing() => wpf.Invoke(() =>
    {
        using TrayIconFrames frames = TrayIconFrames.Build(State(Reading("CC")), 16, Palette);

        Assert.Equal(IntPtr.Zero, frames.Icon(-1));
        Assert.Equal(IntPtr.Zero, frames.Icon(9));
    });

    /// <summary>
    /// Rotation swaps among these handles for as long as the widget runs, so a set that leaked or
    /// double-freed would not be a one-off - it would accumulate, or crash the shell's draw.
    /// </summary>
    [Fact]
    public void DisposingDestroysEveryHandleExactlyOnce() => wpf.Invoke(() =>
    {
        TrayIconFrames frames = TrayIconFrames.Build(
            State(Reading("CC"), Reading("CX"), new("CR", null, null, QuotaBarFill.Accent, TrayFrameKind.Waiting)),
            16,
            Palette);

        List<IntPtr> handles = [.. Enumerable.Range(0, frames.Count).Select(frames.Icon)];

        frames.Dispose();

        foreach (IntPtr handle in handles.Distinct())
        {
            // Already destroyed by Dispose, so a second destroy must fail rather than succeed.
            Assert.False(DestroyIcon(handle), "a handle survived Dispose and was destroyed twice");
        }

        frames.Dispose();
    });

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
