using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AiUsageMonitor.App.Interop;
using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Infrastructure.Theming;

namespace AiUsageMonitor.App.Tests;

/// <summary>
/// A sixteen-pixel drawing is only really verified by looking at the pixels, so these assert on the
/// bitmap rather than on the icon handle. A handle proves GDI accepted the bytes; it says nothing
/// about whether the number was silently squeezed out of the square.
/// <para>
/// The icon is a flag, a figure and a gauge. Every zone is re-derived from the requested size so
/// the rectangles remain on whole device pixels at every shell scaling factor.
/// </para>
/// </summary>
[Collection("wpf")]
public class TrayGlyphRendererTests(WpfFixture wpf)
{
    /// <summary>Deliberately garish: every role has to be identifiable in a pixel by itself.</summary>
    private static readonly TrayGlyphPalette Palette = new(
        Ink: Color.FromRgb(0x00, 0xFF, 0xFF),
        Accent: Color.FromRgb(0x00, 0x00, 0xFF),
        High: Color.FromRgb(0x00, 0xFF, 0x00),
        Exhausted: Color.FromRgb(0xFF, 0x00, 0xFF),
        Stale: Color.FromRgb(0x80, 0x80, 0x80),
        Bad: Color.FromRgb(0xFF, 0x00, 0x00),
        Layer: Color.FromRgb(0xFF, 0xFF, 0xFF),
        Flag0: Color.FromRgb(0x11, 0x22, 0x33),
        Flag1: Color.FromRgb(0x44, 0x55, 0x66),
        Flag2: Color.FromRgb(0x77, 0x88, 0x99));

    private const int Flag = 3;
    private const int GaugeTop = 14;
    private const int Base = 15;

    private static TrayGlyphFrame Reading(string digits, double used, QuotaBarFill fill = QuotaBarFill.Accent) =>
        new(0, digits, used, fill, TrayFrameKind.Reading);

    private static IEnumerable<TrayGlyphFrame> Frames() =>
    [
        Reading("8", 8d),
        Reading("92", 92d, QuotaBarFill.High),
        Reading("61", 61d, QuotaBarFill.Stale),
        new(0, null, 100d, QuotaBarFill.Exhausted, TrayFrameKind.AtLimit),
        new(1, null, null, QuotaBarFill.Accent, TrayFrameKind.Failed),
        new(2, null, null, QuotaBarFill.Accent, TrayFrameKind.Waiting)
    ];

    [Fact]
    public void EveryFrameYieldsAnIconHandleThatCanBeDestroyed() => wpf.Invoke(() =>
    {
        foreach (int size in (int[])[16, 20, 24, 32])
        {
            foreach (TrayGlyphFrame frame in Frames())
            {
                IntPtr icon = TrayGlyphRenderer.Render(frame, size, Palette);

                Assert.NotEqual(IntPtr.Zero, icon);
                Assert.True(DestroyIcon(icon), $"{size}px, {frame.Kind}");
            }
        }
    });

    [Fact]
    public void NothingIsDrawnForAnIconWithNoSize() => wpf.Invoke(() =>
        Assert.Equal(IntPtr.Zero, TrayGlyphRenderer.Render(Reading("42", 42d), 0, Palette)));

    private static Color[,] Render(TrayGlyphFrame frame, int size)
    {
        BitmapSource bitmap = TrayGlyphRenderer.RenderBitmap(frame, size, Palette)
            ?? throw new InvalidOperationException("The renderer produced no bitmap.");

        byte[] raw = new byte[size * size * 4];
        bitmap.CopyPixels(raw, size * 4, 0);

        Color[,] pixels = new Color[size, size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int index = ((y * size) + x) * 4;
                pixels[x, y] = Color.FromArgb(raw[index + 3], raw[index + 2], raw[index + 1], raw[index]);
            }
        }

        return pixels;
    }

    /// <summary>
    /// Whether a pixel is that role's colour. At sixteen pixels almost nothing is fully opaque -
    /// a digit stroke is one pixel wide and a four-pixel capital is mostly its own edge - so the
    /// premultiplication is undone and the comparison allows a little rounding. The tolerance is
    /// far tighter than the gap between any two roles in this palette, so a pixel where two of
    /// them meet matches neither.
    /// </summary>
    private static bool Same(Color pixel, Color role)
    {
        const int tolerance = 24;

        if (pixel.A < 64)
        {
            return false;
        }

        return Near(pixel.R, pixel.A, role.R) && Near(pixel.G, pixel.A, role.G) && Near(pixel.B, pixel.A, role.B);

        static bool Near(byte channel, byte alpha, byte expected) =>
            Math.Abs(Math.Min(255, channel * 255 / alpha) - expected) <= tolerance;
    }

    private static Color[] All(Color[,] pixels, int size) =>
        [.. Enumerable.Range(0, size).SelectMany(y => Enumerable.Range(0, size).Select(x => pixels[x, y]))];

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [Theory]
    [InlineData(ThemeVariant.Light)]
    [InlineData(ThemeVariant.Dark)]
    [InlineData(ThemeVariant.HighContrast)]
    public void EveryVariantDefinesAllThreeFlagColours(ThemeVariant variant) => wpf.Invoke(() =>
    {
        TrayGlyphPalette palette = TrayGlyphPalette.For(variant);

        // Read() falls back to Gray for a missing key, so Gray is the tell for a token that is absent.
        foreach (int slot in (int[])[0, 1, 2])
        {
            Assert.NotEqual(Colors.Gray, palette.FlagColor(slot));
        }
    });

    /// <summary>
    /// The flag's three slots, at 16px. Hard-coded rather than derived, because the point of the
    /// assertion is that the arithmetic lands where the spec says and not merely where the code says.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 7)]
    [InlineData(2, 13)]
    public void FlagSitsInItsSlot(int slot, int expectedX) => wpf.Invoke(() =>
    {
        Color[,] pixels = Render(new(slot, "47", 47d, QuotaBarFill.Accent, TrayFrameKind.Reading), 16);

        for (int x = 0; x < 16; x++)
        {
            bool inside = x >= expectedX && x < expectedX + Flag;
            bool lit = Same(pixels[x, 0], Palette.FlagColor(slot));

            Assert.True(inside == lit, $"x={x} slot={slot}");
        }
    });

    /// <summary>
    /// The slot arithmetic is re-derived per size rather than scaled from sixteen, and the whole
    /// point of doing it that way is the sizes that are not sixteen. The expected columns are copied
    /// from the spec's table rather than from the implementation, so this fails if the two drift.
    /// </summary>
    [Theory]
    [InlineData(20, 1, 8)]
    [InlineData(20, 2, 16)]
    [InlineData(24, 1, 10)]
    [InlineData(24, 2, 19)]
    [InlineData(32, 1, 13)]
    [InlineData(32, 2, 26)]
    public void FlagSitsInItsSlotAtEverySize(int size, int slot, int expectedX) => wpf.Invoke(() =>
    {
        Color[,] pixels = Render(new(slot, "47", 47d, QuotaBarFill.Accent, TrayFrameKind.Reading), size);
        int flag = Math.Max(1, (int)Math.Round(size * 0.1875d, MidpointRounding.AwayFromZero));

        Assert.True(Same(pixels[expectedX, 0], Palette.FlagColor(slot)), $"no flag at x={expectedX} ({size}px)");
        Assert.True(Same(pixels[expectedX + flag - 1, 0], Palette.FlagColor(slot)), $"flag is short at {size}px");
        Assert.False(Same(pixels[expectedX - 1, 0], Palette.FlagColor(slot)), $"flag starts early at {size}px");
    });

    /// <summary>The flag and the figure share columns but no rows, at every size the shell asks for.</summary>
    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    public void FlagNeverOverlapsTheFigure(int size) => wpf.Invoke(() =>
    {
        Color[,] pixels = Render(new(1, "88", 88d, QuotaBarFill.High, TrayFrameKind.Reading), size);
        int flag = Math.Max(1, (int)Math.Round(size * 0.1875d, MidpointRounding.AwayFromZero));

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < flag; y++)
            {
                Assert.False(Same(pixels[x, y], Palette.Ink), $"figure ink in the flag band at {x},{y} ({size}px)");
            }
        }
    });

    /// <summary>
    /// The figure gets ten of sixteen rows - up from eight - and must not quietly give up more. This
    /// is the number the whole redesign was chosen for.
    /// </summary>
    [Fact]
    public void FigureUsesTheTenPixelInkBox() => wpf.Invoke(() =>
    {
        Color[,] pixels = Render(Reading("88", 88d, QuotaBarFill.High), 16);
        int top = -1, bottom = -1;

        for (int y = 0; y < GaugeTop; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                if (!Same(pixels[x, y], Palette.Ink)) { continue; }
                if (top < 0) { top = y; }
                bottom = y;
            }
        }

        // At 16px the box starts on 3.5 and ends on 13.5, so its ten-pixel height touches eleven
        // device rows after WPF antialiasing. The gauge rows are intentionally excluded above.
        Assert.Equal(11, bottom - top + 1);
    });

    /// <summary>At the limit the square floods and prints nothing; the flag knocks out in the layer colour.</summary>
    [Fact]
    public void AtLimitFloodsAndPrintsNoFigure() => wpf.Invoke(() =>
    {
        Color[,] pixels = Render(new(0, null, 100d, QuotaBarFill.Exhausted, TrayFrameKind.AtLimit), 16);

        Assert.True(Same(pixels[8, 8], Palette.Exhausted));
        Assert.True(Same(pixels[0, 0], Palette.Layer));
        Assert.DoesNotContain(All(pixels, 16), pixel => Same(pixel, Palette.Ink));
    });

    /// <summary>
    /// Stale lifts the gauge a pixel off the left edge, so the cue is a difference in alpha rather
    /// than hue and survives high contrast collapsing every fill to one colour.
    /// </summary>
    [Fact]
    public void StaleLiftsTheGaugeOffTheLeftEdge() => wpf.Invoke(() =>
    {
        Color[,] pixels = Render(Reading("61", 61d, QuotaBarFill.Stale), 16);

        Assert.False(Same(pixels[0, Base], Palette.Stale));
        Assert.True(Same(pixels[1, Base], Palette.Stale));
    });

    /// <summary>The flag stays lit when the probe fails - which tool broke is exactly the question.</summary>
    [Fact]
    public void FailureKeepsTheFlagAndLeavesTheGaugeClear() => wpf.Invoke(() =>
    {
        Color[,] pixels = Render(new(2, null, null, QuotaBarFill.Accent, TrayFrameKind.Failed), 16);

        Assert.True(Same(pixels[13, 0], Palette.FlagColor(2)));
        Assert.True(Same(pixels[8, Base], Palette.Bad));

        for (int x = 0; x < 16; x++)
        {
            Assert.False(Same(pixels[x, GaugeTop - 1], Palette.Bad), $"mark touches the gauge at x={x}");
        }
    });

    /// <summary>Nothing read yet is never a zero: bare track, and a rule instead of a figure.</summary>
    [Fact]
    public void WaitingDrawsBareTrackAndNoFigure() => wpf.Invoke(() =>
    {
        Color[,] pixels = Render(new(0, null, null, QuotaBarFill.Accent, TrayFrameKind.Waiting), 16);

        Assert.False(Same(pixels[1, Base], Palette.Accent));
        Assert.DoesNotContain(All(pixels, 16), pixel => Same(pixel, Palette.Accent));
    });

    [Fact]
    public void FlagColoursAreDistinctWhereColourIsAllowedToCarryMeaning() => wpf.Invoke(() =>
    {
        TrayGlyphPalette palette = TrayGlyphPalette.For(ThemeVariant.Dark);

        Assert.Equal(3, new[] { palette.FlagColor(0), palette.FlagColor(1), palette.FlagColor(2) }.Distinct().Count());
    });

    /// <summary>
    /// High contrast resolves every fill to one system colour on purpose, so the flag's hue carries
    /// nothing there and the slot's position is the only thing left naming the provider. Asserting the
    /// collapse keeps anyone from "fixing" it back into three hues.
    /// </summary>
    [Fact]
    public void HighContrastCollapsesTheFlagToOneSystemColour() => wpf.Invoke(() =>
    {
        TrayGlyphPalette palette = TrayGlyphPalette.For(ThemeVariant.HighContrast);

        Assert.Equal(palette.FlagColor(0), palette.FlagColor(1));
        Assert.Equal(palette.FlagColor(1), palette.FlagColor(2));
    });
}
