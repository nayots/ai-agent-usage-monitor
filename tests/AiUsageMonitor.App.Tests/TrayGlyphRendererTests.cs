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
        Layer: Color.FromRgb(0xFF, 0xFF, 0xFF),
        Flag0: Color.FromRgb(0x11, 0x22, 0x33),
        Flag1: Color.FromRgb(0x44, 0x55, 0x66),
        Flag2: Color.FromRgb(0x77, 0x88, 0x99));

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
