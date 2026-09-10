using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AiUsageMonitor.App.Theming;
using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Infrastructure.Settings;
using AiUsageMonitor.Infrastructure.Theming;

namespace AiUsageMonitor.App.Interop;

/// <summary>
/// The colours the glyph draws with, taken from the same theme dictionaries the window uses so
/// there is one definition of every token. Resolved against the <em>taskbar</em>'s theme rather
/// than the application's: see <see cref="SystemTheme.TaskbarUsesLightTheme"/>.
/// </summary>
public sealed record TrayGlyphPalette(
    Color Ink,
    Color Accent,
    Color High,
    Color Exhausted,
    Color Stale,
    Color Bad,
    Color Layer)
{
    /// <summary>
    /// The bar track, as the ink colour at low opacity.
    /// <para>
    /// Deliberately not <c>QuotaBarTrackBrush</c>. That token is defined as a barely-there lift
    /// from the widget's own layer - #E4E4E4 on white - and there is no layer here: the glyph is
    /// drawn straight onto a taskbar whose colour this application does not know and cannot query,
    /// where the same value disappears entirely. Ink at low alpha lifts in the right direction
    /// against any background the taskbar happens to have.
    /// </para>
    /// </summary>
    public Color TrackColor => Color.FromArgb(0x44, Ink.R, Ink.G, Ink.B);

    public static ThemeVariant TaskbarVariant => ThemeResolver.Resolve(
        ThemePreference.System,
        SystemTheme.TaskbarUsesLightTheme,
        SystemTheme.IsHighContrast);

    /// <summary>
    /// Loaded on each call rather than cached. The high-contrast dictionary resolves to live
    /// <c>SystemColors</c>, which change under the user when they switch high-contrast theme, so a
    /// cached palette would be wrong exactly for the users who can least afford it.
    /// </summary>
    public static TrayGlyphPalette For(ThemeVariant variant)
    {
        ResourceDictionary dictionary = new()
        {
            Source = new Uri(
                $"pack://application:,,,/AiUsageMonitor.App;component/Themes/{variant}.xaml",
                UriKind.Absolute)
        };

        return new TrayGlyphPalette(
            Read(dictionary, "TextPrimaryBrush"),
            Read(dictionary, "QuotaBarFillBrush"),
            Read(dictionary, "QuotaBarHighFillBrush"),
            Read(dictionary, "QuotaBarExhaustedFillBrush"),
            Read(dictionary, "QuotaBarStaleFillBrush"),
            Read(dictionary, "StateBadBrush"),
            Read(dictionary, "WidgetLayerBackgroundBrush"));
    }

    public Color BandColor(QuotaBarFill fill) => fill switch
    {
        QuotaBarFill.High => High,
        QuotaBarFill.Exhausted => Exhausted,
        QuotaBarFill.Stale => Stale,
        _ => Accent
    };

    private static Color Read(ResourceDictionary dictionary, string key) =>
        dictionary[key] is SolidColorBrush brush ? brush.Color : Colors.Gray;
}

/// <summary>
/// Draws one provider into a notification-area icon: a large percentage over a single bar whose
/// tone follows the band the reading falls in, with two pixels of air between them.
/// <para>
/// Everything is measured in device pixels. The bitmap is created at 96 dpi so one drawing unit is
/// one pixel, and the caller passes the shell's own small-icon metric, which already accounts for
/// the display's scaling.
/// </para>
/// </summary>
public static class TrayGlyphRenderer
{
    /// <summary>
    /// How far the text may be squeezed horizontally before it gives up height instead. Below this
    /// the vertical stems of a condensed figure thin out faster than the extra height is worth.
    /// </summary>
    private const double CondenseFloor = 0.72d;

    private static readonly Typeface Face = new(
        new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    /// <summary>The band, the gap and the bar, in device pixels, for one icon size.</summary>
    private readonly record struct Zones(int Unit, int Band, int Bar, int BarY)
    {
        public static Zones For(int size)
        {
            int unit = Math.Max(1, (int)Round(size / 16d));
            int bar = Math.Max(2, (int)Round(size / 8d));

            // The gap is the bar's own height: one expression, and it keeps the number, the air
            // and the bar in a fixed proportion at every scaling factor.
            return new Zones(unit, size - bar - bar, bar, size - bar);
        }
    }

    /// <summary>
    /// Renders one frame and returns an <c>HICON</c> the caller owns and must destroy. Returns
    /// <see cref="IntPtr.Zero"/> if GDI refuses the bitmap, which the caller treats as "keep the
    /// icon you have" rather than as a failure worth surfacing.
    /// </summary>
    public static IntPtr Render(TrayGlyphFrame frame, bool showsName, int size, TrayGlyphPalette palette)
    {
        RenderTargetBitmap? bitmap = RenderBitmap(frame, showsName, size, palette);
        return bitmap is null ? IntPtr.Zero : ToIcon(bitmap, size);
    }

    /// <summary>
    /// The glyph as pixels, before it becomes an icon handle. Separate because GDI turns a wrong
    /// layout into a handle indistinguishable from a right one, and a sixteen-pixel drawing is only
    /// ever really verified by looking at the pixels - by a test or by an eye.
    /// </summary>
    public static RenderTargetBitmap? RenderBitmap(TrayGlyphFrame frame, bool showsName, int size, TrayGlyphPalette palette)
    {
        if (size <= 0)
        {
            return null;
        }

        DrawingVisual visual = new();

        using (DrawingContext context = visual.RenderOpen())
        {
            Draw(context, frame, showsName, size, palette);
        }

        RenderTargetBitmap bitmap = new(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static void Draw(DrawingContext context, TrayGlyphFrame frame, bool showsName, int size, TrayGlyphPalette palette)
    {
        Zones zones = Zones.For(size);
        bool atLimit = frame.Kind == TrayFrameKind.AtLimit;

        if (atLimit)
        {
            context.DrawRoundedRectangle(
                new SolidColorBrush(palette.Exhausted), null,
                new Rect(0, 0, size, zones.Band), zones.Unit, zones.Unit);
        }

        // The band shows the number unless the number carries nothing - at the limit it is always
        // 100, on a failure there is none, before the first read there is none yet.
        string text = showsName || frame.NamesItselfAlways || frame.Digits is null
            ? frame.Monogram
            : frame.Digits;

        Color ink = atLimit ? palette.Layer
            : frame.Fill == QuotaBarFill.Stale ? palette.Stale
            : palette.Ink;

        DrawText(context, text, size, zones.Band, ink);
        DrawBar(context, frame, size, zones, palette);
    }

    /// <summary>
    /// Drawn as glyph outlines rather than text so it can be centred on its own ink rather than on
    /// a line box: at these sizes the line box is half again as tall as the figures, and centring
    /// on it puts them visibly high.
    /// <para>
    /// Width, not the band, sets the size. Two figures at a twelve-pixel height are eighteen pixels
    /// wide, so the geometry is measured once at a reference em, scaled uniformly to reach the
    /// target ink height, then condensed to fit the square - and only when condensing would pass
    /// <see cref="CondenseFloor"/> does it give up height instead. That is why <c>100</c> is
    /// shorter than <c>92</c>.
    /// </para>
    /// </summary>
    private static void DrawText(DrawingContext context, string text, int size, int band, Color ink)
    {
        const double Reference = 100d;

        Geometry geometry = Text(text, Reference, ink).BuildGeometry(new Point(0, 0));
        Rect bounds = geometry.Bounds;

        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        double scale = Round(band * 0.92) / bounds.Height;
        double condense = Math.Min(1d, size / (bounds.Width * scale));

        if (condense < CondenseFloor)
        {
            condense = CondenseFloor;
            scale = size / (bounds.Width * CondenseFloor);
        }

        double drawnWidth = bounds.Width * scale * condense;
        double drawnHeight = bounds.Height * scale;

        TransformGroup transform = new();
        transform.Children.Add(new ScaleTransform(scale * condense, scale, bounds.X, bounds.Y));
        transform.Children.Add(new TranslateTransform(
            Round((size - drawnWidth) / 2) - bounds.X,
            Round((band - drawnHeight) / 2) - bounds.Y));

        geometry.Transform = transform;
        context.DrawGeometry(new SolidColorBrush(ink), null, geometry);
    }

    private static void DrawBar(DrawingContext context, TrayGlyphFrame frame, int size, Zones zones, TrayGlyphPalette palette)
    {
        context.DrawRectangle(new SolidColorBrush(palette.TrackColor), null, new Rect(0, zones.BarY, size, zones.Bar));

        if (frame.Kind == TrayFrameKind.Failed)
        {
            // Dotted, not solid: a texture is never mistaken for a fill, however short a fill gets.
            SolidColorBrush bad = new(palette.Bad);

            for (int x = 0; x < size; x += 4 * zones.Unit)
            {
                context.DrawRectangle(bad, null,
                    new Rect(x, zones.BarY, Math.Min(2 * zones.Unit, size - x), zones.Bar));
            }

            return;
        }

        if (frame.UsedPercent is not double used)
        {
            return;
        }

        // The track runs the full width beneath it, so a stale bar reads as one pixel of track
        // where a current bar would have had fill - a difference in alpha rather than in hue, which
        // is what keeps it legible once high contrast has resolved every tone to one system colour.
        bool stale = frame.Fill == QuotaBarFill.Stale;
        double left = stale ? zones.Unit : 0;
        double width = frame.Kind == TrayFrameKind.AtLimit
            ? size
            : Math.Max(1, Round(size * Math.Clamp(used / 100d, 0d, 1d)));

        context.DrawRectangle(
            new SolidColorBrush(palette.BandColor(frame.Fill)), null,
            new Rect(left, zones.BarY, Math.Min(width, size - left), zones.Bar));
    }

    private static FormattedText Text(string value, double em, Color ink) => new(
        value,
        CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight,
        Face,
        em,
        new SolidColorBrush(ink),
        numberSubstitution: null,
        TextFormattingMode.Display,
        pixelsPerDip: 1d);

    private static double Round(double value) => Math.Round(value, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Turns the rendered pixels into an icon. The colour bitmap is a top-down 32-bit DIB holding
    /// the premultiplied BGRA the shell wants; the mask is left all zeros so the alpha channel
    /// alone decides what shows, which is what every modern shell reads.
    /// </summary>
    private static IntPtr ToIcon(RenderTargetBitmap bitmap, int size)
    {
        int stride = size * 4;
        byte[] pixels = new byte[stride * size];
        bitmap.CopyPixels(pixels, stride, 0);

        BITMAPINFO info = new()
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = size,
                biHeight = -size,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BI_RGB,
                biSizeImage = 0,
                biXPelsPerMeter = 0,
                biYPelsPerMeter = 0,
                biClrUsed = 0,
                biClrImportant = 0
            },
            bmiColors = 0
        };

        IntPtr color = CreateDIBSection(IntPtr.Zero, ref info, DIB_RGB_COLORS, out IntPtr bits, IntPtr.Zero, 0);

        if (color == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        // CreateBitmap word-aligns each scan line, unlike the DWORD alignment a DIB would use.
        IntPtr mask = IntPtr.Zero;

        try
        {
            Marshal.Copy(pixels, 0, bits, pixels.Length);
            mask = CreateBitmap(size, size, 1, 1, new byte[(size + 15) / 16 * 2 * size]);

            if (mask == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            ICONINFO icon = new()
            {
                fIcon = true,
                xHotspot = 0,
                yHotspot = 0,
                hbmMask = mask,
                hbmColor = color
            };

            return CreateIconIndirect(ref icon);
        }
        finally
        {
            // CreateIconIndirect copies both bitmaps, so neither is needed once it returns.
            DeleteObject(color);

            if (mask != IntPtr.Zero)
            {
                DeleteObject(mask);
            }
        }
    }

    private const int BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;

        /// <summary>Unused at 32 bits per pixel, but the structure the API documents carries one entry.</summary>
        public int bmiColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO info, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, byte[] bits);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO info);
}
