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
/// At 16px the zones are band rows 0-8, one row of air at 9, and the plinth at rows 10-15. The
/// initials are inset a pixel into the plinth, so they ink rows 11-14 and rows 10 and 15 carry
/// nothing but track and fill. Several assertions below read row 15 for exactly that reason.
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
        Layer: Color.FromRgb(0xFF, 0xFF, 0xFF));

    private const int Band = 9;
    private const int PlinthTop = 10;
    private const int Base = 15;

    private static TrayGlyphFrame Reading(string digits, double used, QuotaBarFill fill = QuotaBarFill.Accent) =>
        new("CC", digits, used, fill, TrayFrameKind.Reading);

    private static IEnumerable<TrayGlyphFrame> Frames() =>
    [
        Reading("8", 8d),
        Reading("92", 92d, QuotaBarFill.High),
        Reading("61", 61d, QuotaBarFill.Stale),
        new("CC", "100", 100d, QuotaBarFill.Exhausted, TrayFrameKind.AtLimit),
        new("CX", null, null, QuotaBarFill.Accent, TrayFrameKind.Failed),
        new("CR", null, null, QuotaBarFill.Accent, TrayFrameKind.Waiting)
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

    /// <summary>
    /// The figure gives up three of its eleven rows to make room for the plinth, and that is the
    /// cost the whole design was chosen against. It must not quietly give up more.
    /// </summary>
    [Fact]
    public void TheFigureInksAtLeastSevenOfTheNineRowsItIsGiven() => wpf.Invoke(() =>
    {
        Color[,] pixels = Render(Reading("92", 92d, QuotaBarFill.High), 16);

        int[] rows = [.. Enumerable.Range(0, Band)
            .Where(y => Enumerable.Range(0, 16).Any(x => Same(pixels[x, y], Palette.Ink)))];

        Assert.NotEmpty(rows);
        Assert.True(rows.Max() - rows.Min() + 1 >= 7, $"the figure inked {rows.Max() - rows.Min() + 1} of 9 rows");
    });

    [Fact]
    public void NeitherTwoFiguresNorThreeLeaveTheSquare() => wpf.Invoke(() =>
    {
        foreach (string digits in (string[])["8", "92", "100"])
        {
            Color[,] pixels = Render(Reading(digits, 50d), 16);
            int columns = Enumerable.Range(0, 16)
                .Count(x => Enumerable.Range(0, Band).Any(y => pixels[x, y].A > 0));

            Assert.True(columns > 0, $"'{digits}' drew nothing");
            Assert.True(columns <= 16, $"'{digits}' spanned {columns} columns");
        }
    });

    /// <summary>
    /// Two capitals are wider per em than two figures, so the monogram is the string most likely to
    /// be clipped. It is also the one the whole identity story now rests on, at every scaling
    /// factor the shell can ask for.
    /// </summary>
    [Fact]
    public void TheMonogramFitsTheSquareAtEverySize() => wpf.Invoke(() =>
    {
        foreach (int size in (int[])[16, 20, 24, 32])
        {
            // Measured against a plinth filled end to end, so every letter is knocked out and the
            // count is the monogram's own width rather than the fill's.
            Color[,] pixels = Render(new("CC", "100", 100d, QuotaBarFill.Exhausted, TrayFrameKind.AtLimit), size);

            int columns = Enumerable.Range(0, size).Count(x => Enumerable.Range(size - (size / 4), size / 4)
                .Any(y => pixels[x, y].A > 0 && !Same(pixels[x, y], Palette.Exhausted)));

            Assert.True(columns > 0, $"the monogram drew nothing at {size}px");
            Assert.True(columns <= size, $"the monogram spanned {columns} columns of {size}");
        }
    });

    /// <summary>
    /// One row of air, and nothing may creep into it: it is all that keeps an eight-pixel figure
    /// off a six-pixel plinth.
    /// </summary>
    [Fact]
    public void OneRowOfAirSeparatesTheFigureFromThePlinth() => wpf.Invoke(() =>
    {
        Color[,] pixels = Render(Reading("92", 92d, QuotaBarFill.High), 16);

        Assert.All(Enumerable.Range(0, 16), x => Assert.Equal(0, pixels[x, Band].A));
    });

    /// <summary>
    /// The row the letters give up, and the reason every other plinth assertion below can be
    /// written at all: it is the one line along the plinth carrying nothing but fill and track, so
    /// it is the only place the fill's width can be measured without a letter in the way. Every
    /// pixel of it must be one tone or the other and never a blend of a letter with either.
    /// </summary>
    [Fact]
    public void ThePlinthsBottomRowCarriesNoLetter() => wpf.Invoke(() =>
    {
        Color[,] pixels = Render(Reading("42", 42d), 16);

        Assert.All(Enumerable.Range(0, 16), x => Assert.True(
            Same(pixels[x, Base], Palette.Accent) || Same(pixels[x, Base], Palette.Ink),
            $"x={x} on the bottom row was neither fill nor track"));
    });

    [Fact]
    public void ThePlinthFillsToTheValueAndTakesItsToneFromTheBand() => wpf.Invoke(() =>
    {
        int Filled(Color tone, double used, QuotaBarFill fill) =>
            Enumerable.Range(0, 16).Count(x => Same(Render(Reading("x", used, fill), 16)[x, Base], tone));

        Assert.Equal(8, Filled(Palette.Accent, 50d, QuotaBarFill.Accent));
        Assert.Equal(14, Filled(Palette.High, 88d, QuotaBarFill.High));
        Assert.Equal(16, Filled(Palette.Exhausted, 100d, QuotaBarFill.Exhausted));
    });

    /// <summary>
    /// The signature of this design, and the reason the plinth can stay translucent over a taskbar
    /// whose colour this application cannot query: one string, two clips, split at the fill's edge.
    /// A single knocked-out colour would be unreadable on one side of it or the other.
    /// <para>
    /// Counted as columns <em>lifted off the fill</em> rather than as pixels matching the layer
    /// colour. Nothing here is ever fully inked - a five-pixel capital has strokes narrower than a
    /// pixel - so an exact match would be measuring antialiasing. A column carrying anything that
    /// is neither the fill nor the track has a letter cut into it, and that is the whole claim.
    /// </para>
    /// </summary>
    [Fact]
    public void TheInitialsInvertAtTheFillsEdge() => wpf.Invoke(() =>
    {
        int[] KnockedOut(double used)
        {
            Color[,] pixels = Render(Reading("50", used), 16);

            return [.. Enumerable.Range(0, 16).Where(x => Enumerable.Range(PlinthTop, 5).Any(y =>
                pixels[x, y].A > 0
                && !Same(pixels[x, y], Palette.Accent)
                && !Same(pixels[x, y], Palette.Ink)))];
        }

        // Nothing has reached the letters, so none of them is knocked out.
        Assert.Empty(KnockedOut(6d));

        // The fill has passed them, so they are.
        Assert.NotEmpty(KnockedOut(99d));

        // Half way, the knocked-out columns are exactly the ones the fill has reached.
        int[] half = KnockedOut(50d);

        Assert.NotEmpty(half);
        Assert.All(half, x => Assert.True(x < 8, $"column {x} inverted past the fill's edge at 8"));
        Assert.True(half.Length < KnockedOut(99d).Length, "half a fill knocked out as much as a whole one");
    });

    /// <summary>Missing is not zero: a waiting frame draws track and no fill whatsoever.</summary>
    [Fact]
    public void AWaitingFrameDrawsBareTrackAndNoFill() => wpf.Invoke(() =>
    {
        Color[] pixels = All(Render(new("CR", null, null, QuotaBarFill.Accent, TrayFrameKind.Waiting), 16), 16);

        Assert.DoesNotContain(pixels, pixel => Same(pixel, Palette.Accent));
        Assert.Contains(pixels, pixel => pixel.A > 0);
    });

    /// <summary>
    /// A rule, drawn rather than typed. Scaling a dash to the band's ink height would have produced
    /// a slab the width of the square - a second bar, in the one frame that has no reading to bar.
    /// </summary>
    [Fact]
    public void AWaitingFrameMarksItsBandWithAShortRuleRatherThanASlab() => wpf.Invoke(() =>
    {
        Color[,] pixels = Render(new("CR", null, null, QuotaBarFill.Accent, TrayFrameKind.Waiting), 16);

        int[] lit = [.. Enumerable.Range(0, Band)
            .Select(y => Enumerable.Range(0, 16).Count(x => Same(pixels[x, y], Palette.Ink)))];

        Assert.True(lit.Count(count => count > 0) <= 3, "the rule was taller than three rows");
        Assert.InRange(lit.Max(), 3, 12);
    });

    /// <summary>
    /// A texture is never mistaken for a fill, however short a fill gets. Solid would have been
    /// ambiguous against a low reading; the gaps are the signal.
    /// </summary>
    [Fact]
    public void AFailedFrameDrawsADottedPlinthRatherThanASolidOne() => wpf.Invoke(() =>
    {
        Color[,] pixels = Render(new("CX", null, null, QuotaBarFill.Accent, TrayFrameKind.Failed), 16);
        bool[] lit = [.. Enumerable.Range(0, 16).Select(x => Same(pixels[x, Base], Palette.Bad))];

        Assert.Contains(true, lit);
        Assert.Contains(false, lit);

        // On, off, on, off ... rather than one run at the left edge.
        int runs = Enumerable.Range(1, 15).Count(x => lit[x] != lit[x - 1]) + 1;
        Assert.True(runs >= 4, $"the dotted plinth had {runs} runs");
    });

    /// <summary>
    /// The failure mark. Vertical where the waiting rule is horizontal, and in the bad tone, so the
    /// two frames that both lack a figure are never confused for one another.
    /// </summary>
    [Fact]
    public void AFailedFrameMarksItsBandWithAnUprightInTheBadTone() => wpf.Invoke(() =>
    {
        Color[,] pixels = Render(new("CX", null, null, QuotaBarFill.Accent, TrayFrameKind.Failed), 16);

        int rows = Enumerable.Range(0, Band)
            .Count(y => Enumerable.Range(0, 16).Any(x => Same(pixels[x, y], Palette.Bad)));
        int columns = Enumerable.Range(0, 16)
            .Count(x => Enumerable.Range(0, Band).Any(y => Same(pixels[x, y], Palette.Bad)));

        Assert.True(rows >= 6, $"the mark inked {rows} of 9 rows");
        Assert.True(columns <= 4, $"the mark spanned {columns} columns");
    });

    /// <summary>
    /// The limit frame is the loudest thing this application draws, and deliberately: a block that
    /// reaches its edges, with a hundred knocked out of it, over a plinth filled end to end.
    /// <para>
    /// "A block" is asserted as opacity rather than as a count of exhausted-toned pixels: the
    /// figures knocked out of it are mostly their own antialiased edges at sixteen pixels, so much
    /// of the band is a blend of the two tones and matches neither exactly.
    /// </para>
    /// </summary>
    [Fact]
    public void TheLimitFrameKnocksAHundredOutOfASolidBlock() => wpf.Invoke(() =>
    {
        Color[,] limit = Render(new("CC", "100", 100d, QuotaBarFill.Exhausted, TrayFrameKind.AtLimit), 16);
        Color[,] reading = Render(Reading("92", 92d), 16);

        int knockout = 0;
        int filled = 0;
        int readingFilled = 0;

        for (int y = 0; y < Band; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                if (Same(limit[x, y], Palette.Layer)) knockout++;
                if (limit[x, y].A > 200) filled++;
                if (reading[x, y].A > 200) readingFilled++;
            }
        }

        Assert.True(filled >= (16 * Band) - 8, $"the block left {(16 * Band) - filled} of {16 * Band} band pixels unfilled");
        Assert.True(filled > readingFilled * 3, $"block filled {filled}, a reading filled {readingFilled}");
        Assert.True(knockout > 0, "the hundred was not knocked out of the block");

        // It reaches its edges. A mark centred in the band would leave these transparent.
        Assert.All((int[])[2, 8, 13], x =>
            Assert.True(Same(limit[x, 0], Palette.Exhausted), $"the top edge at x={x} was not the block"));
    });

    /// <summary>
    /// The band only, not the whole square. The plinth's track is the ink colour at a third of its
    /// alpha in <em>every</em> frame, stale or not, so searching the square for ink would find the
    /// track every time and prove nothing about the figures.
    /// </summary>
    [Fact]
    public void AStaleReadingIsGreyedRatherThanDrawnInInk() => wpf.Invoke(() =>
    {
        Color[] current = BandPixels(Render(Reading("63", 63d), 16));
        Color[] stale = BandPixels(Render(Reading("63", 63d, QuotaBarFill.Stale), 16));

        Assert.Contains(current, pixel => Same(pixel, Palette.Ink));
        Assert.DoesNotContain(stale, pixel => Same(pixel, Palette.Ink));
        Assert.Contains(stale, pixel => Same(pixel, Palette.Stale));
    });

    /// <summary>
    /// Not decoration: the offset is the one thing that distinguishes a stale plinth from a current
    /// one when every tone has resolved to the same system colour in high contrast.
    /// </summary>
    [Fact]
    public void AStalePlinthLiftsOffTheLeftEdgeSoItSurvivesOneHue() => wpf.Invoke(() =>
    {
        Color[,] stale = Render(Reading("63", 63d, QuotaBarFill.Stale), 16);
        Color[,] current = Render(Reading("63", 63d), 16);

        Assert.True(stale[0, Base].A > 0, "the track did not run beneath the offset");
        Assert.False(Same(stale[0, Base], Palette.Stale), "the stale plinth reached the left edge");
        Assert.True(Same(stale[1, Base], Palette.Stale), "the stale plinth did not start one pixel in");

        // The comparison that gives the offset its meaning: a current plinth does start at the edge.
        Assert.True(Same(current[0, Base], Palette.Accent), "a current plinth did not start at the edge");
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

    /// <summary>The nine rows the band is drawn in, at 16px. Excludes the air and the plinth.</summary>
    private static Color[] BandPixels(Color[,] pixels) =>
        [.. Enumerable.Range(0, Band).SelectMany(y => Enumerable.Range(0, 16).Select(x => pixels[x, y]))];

    private static Color[] All(Color[,] pixels, int size) =>
        [.. Enumerable.Range(0, size).SelectMany(y => Enumerable.Range(0, size).Select(x => pixels[x, y]))];

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
