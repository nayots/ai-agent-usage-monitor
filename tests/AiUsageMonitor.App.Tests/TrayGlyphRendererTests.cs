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
        Layer: Color.FromRgb(0xFF, 0xFF, 0xFF));

    private static TrayGlyphFrame Reading(string digits, double used, QuotaBarFill fill = QuotaBarFill.Accent) =>
        new("CC", digits, used, fill, TrayFrameKind.Reading);

    [Fact]
    public void EveryFrameYieldsAnIconHandleThatCanBeDestroyed() => wpf.Invoke(() =>
    {
        foreach (int size in (int[])[16, 20, 24, 32])
        {
            foreach (TrayGlyphFrame frame in Frames())
            {
                foreach (bool showsName in (bool[])[false, true])
                {
                    IntPtr icon = TrayGlyphRenderer.Render(frame, showsName, size, Palette);

                    Assert.NotEqual(IntPtr.Zero, icon);
                    Assert.True(DestroyIcon(icon), $"{size}px, {frame.Kind}, name {showsName}");
                }
            }
        }
    });

    private static IEnumerable<TrayGlyphFrame> Frames() =>
    [
        Reading("8", 8d),
        Reading("92", 92d, QuotaBarFill.High),
        Reading("61", 61d, QuotaBarFill.Stale),
        new("CC", null, 100d, QuotaBarFill.Exhausted, TrayFrameKind.AtLimit),
        new("CX", null, null, QuotaBarFill.Accent, TrayFrameKind.Failed),
        new("CR", null, null, QuotaBarFill.Accent, TrayFrameKind.Waiting)
    ];

    /// <summary>
    /// The whole point of the change. Today's glyph gives the digits eight of sixteen rows and
    /// spends the other eight on bars that collapse to a pixel each; this one gives the figure
    /// twelve and the bar two, so the number has to actually be taller.
    /// </summary>
    [Fact]
    public void TheFigureInksAtLeastTenOfTheTwelveRowsItIsGiven() => wpf.Invoke(() =>
    {
        Color[,] pixels = Render(Reading("92", 92d, QuotaBarFill.High), showsName: false, 16);

        int[] rows = [.. Enumerable.Range(0, 12)
            .Where(y => Enumerable.Range(0, 16).Any(x => Same(pixels[x, y], Palette.Ink)))];

        Assert.NotEmpty(rows);
        Assert.True(rows.Max() - rows.Min() + 1 >= 10, $"the figure inked {rows.Max() - rows.Min() + 1} of 12 rows");
    });

    [Fact]
    public void NeitherTwoFiguresNorThreeLeaveTheSquare() => wpf.Invoke(() =>
    {
        foreach (string digits in (string[])["8", "92", "100"])
        {
            Color[,] pixels = Render(Reading(digits, 50d), showsName: false, 16);

            Assert.True(InkColumns(pixels) <= 16, $"'{digits}' spanned {InkColumns(pixels)} columns");
            Assert.True(InkColumns(pixels) > 0, $"'{digits}' drew nothing");
        }
    });

    /// <summary>
    /// Two capitals are wider per em than two figures, so the monogram is the string most likely to
    /// be clipped. It is also the one the whole identity story rests on.
    /// </summary>
    [Fact]
    public void TheMonogramFitsTheSquareAtEverySize() => wpf.Invoke(() =>
    {
        foreach (int size in (int[])[16, 20, 24, 32])
        {
            Color[,] pixels = Render(Reading("92", 40d), showsName: true, size);
            int columns = Enumerable.Range(0, size)
                .Count(x => Enumerable.Range(0, size).Any(y => Same(pixels[x, y], Palette.Ink)));

            Assert.True(columns > 0, $"the monogram drew nothing at {size}px");
            Assert.True(columns <= size, $"the monogram spanned {columns} columns of {size}");
        }
    });

    /// <summary>
    /// The complaint that started this: the number and the bar were glued together. Two rows of
    /// nothing sit between them and nothing may creep into that gap.
    /// </summary>
    [Fact]
    public void TwoRowsOfAirSeparateTheFigureFromTheBar() => wpf.Invoke(() =>
    {
        Color[,] pixels = Render(Reading("92", 92d, QuotaBarFill.High), showsName: false, 16);

        foreach (int y in (int[])[12, 13])
        {
            Assert.All(Enumerable.Range(0, 16), x => Assert.Equal(0, pixels[x, y].A));
        }
    });

    [Fact]
    public void TheBarFillsToTheValueAndTakesItsToneFromTheBand() => wpf.Invoke(() =>
    {
        int Filled(Color tone, double used, QuotaBarFill fill) =>
            Enumerable.Range(0, 16).Count(x => Same(Render(Reading("x", used, fill), false, 16)[x, 15], tone));

        Assert.Equal(8, Filled(Palette.Accent, 50d, QuotaBarFill.Accent));
        Assert.Equal(14, Filled(Palette.High, 88d, QuotaBarFill.High));
        Assert.Equal(16, Filled(Palette.Exhausted, 100d, QuotaBarFill.Exhausted));
    });

    /// <summary>Missing is not zero: a waiting frame draws track and no fill whatsoever.</summary>
    [Fact]
    public void AWaitingFrameDrawsBareTrackAndNoFill() => wpf.Invoke(() =>
    {
        Color[] pixels = All(Render(new("CR", null, null, QuotaBarFill.Accent, TrayFrameKind.Waiting), false, 16), 16);

        Assert.DoesNotContain(pixels, pixel => Same(pixel, Palette.Accent));
        Assert.Contains(pixels, pixel => pixel.A > 0);
    });

    /// <summary>
    /// A texture is never mistaken for a fill, however short a fill gets. Solid would have been
    /// ambiguous against a low reading; the gaps are the signal.
    /// </summary>
    [Fact]
    public void AFailedFrameDrawsADottedBarRatherThanASolidOne() => wpf.Invoke(() =>
    {
        Color[,] pixels = Render(new("CX", null, null, QuotaBarFill.Accent, TrayFrameKind.Failed), false, 16);
        bool[] lit = [.. Enumerable.Range(0, 16).Select(x => Same(pixels[x, 15], Palette.Bad))];

        Assert.Contains(true, lit);
        Assert.Contains(false, lit);

        // On, off, on, off ... rather than one run at the left edge.
        int runs = Enumerable.Range(1, 15).Count(x => lit[x] != lit[x - 1]) + 1;
        Assert.True(runs >= 4, $"the dotted bar had {runs} runs");
    });

    /// <summary>
    /// The one state that inverts, and the only thing on a taskbar that will be doing it.
    /// <para>
    /// "A block" is asserted as opacity rather than as a count of exhausted-toned pixels: the name
    /// knocked out of it is mostly its own antialiased edge at sixteen pixels, so most of the band
    /// is a blend of the two tones and matches neither exactly. What separates a block from a badge
    /// or an outline is that the whole band is filled and reaches its edges - which a reading, whose
    /// band is empty but for the strokes of its figures, conspicuously is not.
    /// </para>
    /// </summary>
    [Fact]
    public void TheLimitFrameKnocksItsNameOutOfASolidBlock() => wpf.Invoke(() =>
    {
        Color[,] limit = Render(new("CC", null, 100d, QuotaBarFill.Exhausted, TrayFrameKind.AtLimit), false, 16);
        Color[,] reading = Render(Reading("92", 92d), false, 16);

        int knockout = 0;
        int filled = 0;
        int readingFilled = 0;

        for (int y = 0; y < 12; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                if (Same(limit[x, y], Palette.Layer)) knockout++;
                if (limit[x, y].A > 200) filled++;
                if (reading[x, y].A > 200) readingFilled++;
            }
        }

        Assert.True(filled >= (16 * 12) - 8, $"the block left {(16 * 12) - filled} of 192 band pixels unfilled");
        Assert.True(filled > readingFilled * 3, $"block filled {filled}, a reading filled {readingFilled}");
        Assert.True(knockout > 0, "the name was not knocked out of the block");

        // It reaches its edges. A mark centred in the band would leave these transparent.
        Assert.All((int[])[2, 8, 13], x =>
            Assert.True(Same(limit[x, 0], Palette.Exhausted), $"the top edge at x={x} was not the block"));
    });

    /// <summary>
    /// The band only, not the whole square. The bar's track is the ink colour at a third of its
    /// alpha in <em>every</em> frame, stale or not, so searching the square for ink would find the
    /// track every time and prove nothing about the figures.
    /// </summary>
    [Fact]
    public void AStaleReadingIsGreyedRatherThanDrawnInInk() => wpf.Invoke(() =>
    {
        Color[] current = Band(Render(Reading("63", 63d), false, 16));
        Color[] stale = Band(Render(Reading("63", 63d, QuotaBarFill.Stale), false, 16));

        Assert.Contains(current, pixel => Same(pixel, Palette.Ink));
        Assert.DoesNotContain(stale, pixel => Same(pixel, Palette.Ink));
        Assert.Contains(stale, pixel => Same(pixel, Palette.Stale));
    });

    /// <summary>
    /// Not decoration: the offset is the one thing that distinguishes a stale bar from a current
    /// one when every tone has resolved to the same system colour in high contrast.
    /// <para>
    /// The track still runs the full width beneath it, so what the eye reads at the left edge is
    /// one pixel of track where a current bar would have had fill. That survives a single hue
    /// because the track is that hue at a third of the alpha, which is why this asserts the edge
    /// pixel is present but is not the stale tone, rather than asserting it is empty.
    /// </para>
    /// </summary>
    [Fact]
    public void AStaleBarLiftsOffTheLeftEdgeSoItSurvivesOneHue() => wpf.Invoke(() =>
    {
        Color[,] stale = Render(Reading("63", 63d, QuotaBarFill.Stale), false, 16);
        Color[,] current = Render(Reading("63", 63d), false, 16);

        Assert.True(stale[0, 15].A > 0, "the track did not run beneath the offset");
        Assert.False(Same(stale[0, 15], Palette.Stale), "the stale bar reached the left edge");
        Assert.True(Same(stale[1, 15], Palette.Stale), "the stale bar did not start one pixel in");

        // The comparison that gives the offset its meaning: a current bar does start at the edge.
        Assert.True(Same(current[0, 15], Palette.Accent), "a current bar did not start at the edge");
    });

    [Fact]
    public void NothingIsDrawnForAnIconWithNoSize() => wpf.Invoke(() =>
        Assert.Equal(IntPtr.Zero, TrayGlyphRenderer.Render(Reading("42", 42d), false, 0, Palette)));

    private static Color[,] Render(TrayGlyphFrame frame, bool showsName, int size)
    {
        BitmapSource bitmap = TrayGlyphRenderer.RenderBitmap(frame, showsName, size, Palette)
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
    /// a digit stroke is one pixel wide and a six-pixel disc is mostly its own edge - so the
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

    /// <summary>The twelve rows the text is drawn in, at 16px. Excludes the gap and the bar.</summary>
    private static Color[] Band(Color[,] pixels) =>
        [.. Enumerable.Range(0, 12).SelectMany(y => Enumerable.Range(0, 16).Select(x => pixels[x, y]))];

    private static Color[] All(Color[,] pixels, int size) =>
        [.. Enumerable.Range(0, size).SelectMany(y => Enumerable.Range(0, size).Select(x => pixels[x, y]))];

    private static int InkColumns(Color[,] pixels) =>
        Enumerable.Range(0, pixels.GetLength(0))
            .Count(x => Enumerable.Range(0, pixels.GetLength(1)).Any(y => pixels[x, y].A > 0));

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
