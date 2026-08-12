using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AiUsageMonitor.App.Interop;
using AiUsageMonitor.Infrastructure.Theming;

namespace AiUsageMonitor.App.Tests;

/// <summary>
/// A sixteen-pixel drawing is only really verified by looking at the pixels, so these assert on the
/// bitmap rather than on the icon handle. A handle proves GDI accepted the bytes; it says nothing
/// about whether a bar was silently squeezed out of the square.
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

    [Fact]
    public void EveryShapeOfGlyphYieldsAnIconHandleThatCanBeDestroyed() => wpf.Invoke(() =>
    {
        foreach (int size in (int[])[16, 20, 24, 32])
        {
            foreach (int count in (int[])[0, 1, 2, 3, 4])
            {
                foreach (TrayOverlay overlay in Enum.GetValues<TrayOverlay>())
                {
                    foreach (string? digits in (string?[])[null, "7", "92"])
                    {
                        // One window without a value in every set, because a provider reporting no
                        // percentage is the case that must not be drawn as zero or skipped.
                        TrayGlyphBar[] bars = [.. Enumerable.Range(0, count).Select(index =>
                            new TrayGlyphBar(index == 1 ? null : index * 30d, QuotaBarFill.Accent, index == 2))];

                        IntPtr icon = TrayGlyphRenderer.Render(bars, digits, false, overlay, size, Palette);

                        Assert.NotEqual(IntPtr.Zero, icon);
                        Assert.True(DestroyIcon(icon), $"{size}px, {count} bars, {overlay}, digits {digits ?? "none"}");
                    }
                }
            }
        }
    });

    [Fact]
    public void EveryWindowStillGetsItsOwnBarInTheSmallestIconAlongsideDigits() => wpf.Invoke(() =>
    {
        // Sixteen pixels with digits leaves eight for the bars, which is four two-pixel rows only
        // once every gap is gone. The layout gives its gaps up before it gives up a window.
        TrayGlyphBar[] bars =
        [
            new(25d, QuotaBarFill.Accent, false),
            new(50d, QuotaBarFill.Accent, true),
            new(75d, QuotaBarFill.Accent, false),
            new(100d, QuotaBarFill.Accent, true)
        ];

        Color[,] pixels = Render(bars, "99", TrayOverlay.None, 16);
        HashSet<int> widths = [];

        for (int y = 0; y < 16; y++)
        {
            int width = Enumerable.Range(0, 16).Count(x => Same(pixels[x, y], Palette.Accent));

            if (width > 0)
            {
                widths.Add(width);
            }
        }

        Assert.Equal([4, 8, 12, 16], widths.Order());
    });

    [Fact]
    public void ABarWithNoValueIsBareTrackRatherThanAnEmptyBarOrNoBarAtAll() => wpf.Invoke(() =>
    {
        Color[,] pixels = Render([new TrayGlyphBar(null, QuotaBarFill.Accent, false)], null, TrayOverlay.None, 16);

        Assert.DoesNotContain(All(pixels, 16), pixel => Same(pixel, Palette.Accent));
        Assert.Contains(All(pixels, 16), pixel => pixel.A > 0);
    });

    [Fact]
    public void ATwoDigitReadingIsWiderThanOneAndNeitherLeavesTheSquare() => wpf.Invoke(() =>
    {
        int single = InkColumns(Render([], "7", TrayOverlay.None, 16));
        int pair = InkColumns(Render([], "99", TrayOverlay.None, 16));

        Assert.True(single > 0, "a single digit drew nothing");
        Assert.True(pair > single, $"'99' spanned {pair} columns and '7' spanned {single}");
        Assert.True(pair <= 16, $"'99' spanned {pair} columns of a 16 pixel icon");
    });

    [Fact]
    public void AStaleReadingIsGreyedRatherThanDrawnInInk() => wpf.Invoke(() =>
    {
        Color[] current = All(Render([], "63", TrayOverlay.None, 16), 16);
        Color[] stale = All(RenderBitmap([], "63", digitsAreStale: true, TrayOverlay.None, 16), 16);

        Assert.Contains(current, pixel => Same(pixel, Palette.Ink));
        Assert.DoesNotContain(stale, pixel => Same(pixel, Palette.Ink));
        Assert.Contains(stale, pixel => Same(pixel, Palette.Stale));
    });

    [Fact]
    public void TheErrorMarkTakesTheCornerAndTheAlertMarkTakesTheMiddle() => wpf.Invoke(() =>
    {
        TrayGlyphBar[] bars = [new(40d, QuotaBarFill.Accent, false), new(60d, QuotaBarFill.Accent, true)];

        (double X, double Y) error = Centroid(Render(bars, "60", TrayOverlay.Error, 16));
        (double X, double Y) alert = Centroid(Render(bars, null, TrayOverlay.Alert, 16));

        Assert.True(error.X > 8 && error.Y > 8, $"the error mark sat at {error}");
        Assert.InRange(alert.X, 6, 10);
        Assert.InRange(alert.Y, 5, 11);
    });

    [Fact]
    public void NothingIsDrawnForAnIconWithNoSize() =>
        wpf.Invoke(() => Assert.Equal(IntPtr.Zero, TrayGlyphRenderer.Render([], "42", false, TrayOverlay.None, 0, Palette)));

    private static Color[,] Render(IReadOnlyList<TrayGlyphBar> bars, string? digits, TrayOverlay overlay, int size) =>
        RenderBitmap(bars, digits, false, overlay, size);

    private static Color[,] RenderBitmap(IReadOnlyList<TrayGlyphBar> bars, string? digits, bool digitsAreStale, TrayOverlay overlay, int size)
    {
        BitmapSource bitmap = TrayGlyphRenderer.RenderBitmap(bars, digits, digitsAreStale, overlay, size, Palette)
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

    private static Color[] All(Color[,] pixels, int size) =>
        [.. Enumerable.Range(0, size).SelectMany(y => Enumerable.Range(0, size).Select(x => pixels[x, y]))];

    private static int InkColumns(Color[,] pixels) =>
        Enumerable.Range(0, pixels.GetLength(0))
            .Count(x => Enumerable.Range(0, pixels.GetLength(1)).Any(y => pixels[x, y].A > 0));

    /// <summary>Where the overlay's colour sits, averaged. The overlay is the only red in the palette.</summary>
    private static (double X, double Y) Centroid(Color[,] pixels)
    {
        double x = 0;
        double y = 0;
        int count = 0;

        for (int row = 0; row < pixels.GetLength(1); row++)
        {
            for (int column = 0; column < pixels.GetLength(0); column++)
            {
                if (Same(pixels[column, row], Palette.Bad))
                {
                    x += column + 0.5;
                    y += row + 0.5;
                    count++;
                }
            }
        }

        Assert.True(count > 0, "the overlay drew nothing");
        return (x / count, y / count);
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
