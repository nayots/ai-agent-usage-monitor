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
    Color Layer,
    Color Flag0,
    Color Flag1,
    Color Flag2)
{
    /// <summary>
    /// The flag's colour for a slot. Colour and position come from the same integer, so the two can
    /// never disagree about which provider is being drawn.
    /// </summary>
    public Color FlagColor(int slot) => slot switch
    {
        <= 0 => Flag0,
        1 => Flag1,
        _ => Flag2
    };

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
            Read(dictionary, "WidgetLayerBackgroundBrush"),
            Read(dictionary, "TrayFlagSlot0Brush"),
            Read(dictionary, "TrayFlagSlot1Brush"),
            Read(dictionary, "TrayFlagSlot2Brush"));
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
/// Draws one provider into a notification-area icon: a flag, figure and gauge.
/// <para>
/// The flag's position and colour name the provider, the figure states its usage and the gauge
/// reinforces the state without taking a second text element.
/// </para>
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

    /// <summary>
    /// The flag band, the figure's ink box and the gauge, in device pixels, for one size.
    /// <para>
    /// Everything derives from the icon's own size. Each size is re-derived rather than scaled from
    /// sixteen, which is what keeps the flag and the gauge on whole device pixels at 125% and 150%
    /// scaling instead of straddling two.
    /// </para>
    /// </summary>
    private readonly record struct Zones(
        int Size, int Unit, int Flag, int Bar, int BarY, double InkTop, double InkHeight)
    {
        public static Zones For(int size)
        {
            int unit = Math.Max(1, (int)Round(size / 16d));
            int flag = Math.Max(1, (int)Round(size * 0.1875d));
            int bar = Math.Max(1, (int)Round(size * 0.125d));
            int barY = size - bar;
            double inkTop = flag + (unit / 2d);

            return new Zones(size, unit, flag, bar, barY, inkTop, barY - (unit / 2d) - inkTop);
        }

        /// <summary>
        /// Where the flag sits. Three slots, chosen by the provider rather than by its position in
        /// the visible list, so hiding one card never renumbers the others.
        /// </summary>
        public double SlotX(int slot) => slot switch
        {
            <= 0 => 0,
            1 => Round((Size - Flag) / 2d),
            _ => Size - Flag
        };

        public Rect FlagArea(int slot) => new(SlotX(slot), 0, Flag, Flag);

        /// <summary>
        /// Where the figure is drawn: inset a pixel at each end, because only a three-figure string
        /// is ever wide enough to notice and without the margin it runs into both edges.
        /// </summary>
        public Rect FigureArea() => new(Unit, InkTop, Size - (2 * Unit), InkHeight);

        public Rect GaugeArea(double x, double width) => new(x, BarY, width, Bar);
    }

    /// <summary>
    /// Renders one frame and returns an <c>HICON</c> the caller owns and must destroy. Returns
    /// <see cref="IntPtr.Zero"/> if GDI refuses the bitmap, which the caller treats as "keep the
    /// icon you have" rather than as a failure worth surfacing.
    /// </summary>
    public static IntPtr Render(TrayGlyphFrame frame, int size, TrayGlyphPalette palette)
    {
        RenderTargetBitmap? bitmap = RenderBitmap(frame, size, palette);
        return bitmap is null ? IntPtr.Zero : ToIcon(bitmap, size);
    }

    /// <summary>
    /// The glyph as pixels, before it becomes an icon handle. Separate because GDI turns a wrong
    /// layout into a handle indistinguishable from a right one, and a sixteen-pixel drawing is only
    /// ever really verified by looking at the pixels - by a test or by an eye.
    /// </summary>
    public static RenderTargetBitmap? RenderBitmap(TrayGlyphFrame frame, int size, TrayGlyphPalette palette)
    {
        if (size <= 0)
        {
            return null;
        }

        DrawingVisual visual = new();

        using (DrawingContext context = visual.RenderOpen())
        {
            Draw(context, frame, size, palette);
        }

        RenderTargetBitmap bitmap = new(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static void Draw(DrawingContext context, TrayGlyphFrame frame, int size, TrayGlyphPalette palette)
    {
        Zones zones = Zones.For(size);
        bool flooded = frame.Kind == TrayFrameKind.AtLimit;

        if (flooded)
        {
            context.DrawRoundedRectangle(
                new SolidColorBrush(palette.Exhausted), null, new Rect(0, 0, size, size), zones.Unit, zones.Unit);
        }

        // The flood owns the hue, so the flag knocks out in the layer colour and its slot - which the
        // flood cannot take away - goes on naming the provider. The same fallback high contrast needs.
        context.DrawRectangle(
            new SolidColorBrush(flooded ? palette.Layer : palette.FlagColor(frame.Slot)),
            null,
            zones.FlagArea(frame.Slot));

        DrawFigure(context, frame, zones, palette);
        DrawGauge(context, frame, size, zones, palette, flooded);
    }

    /// <summary>
    /// The figure, or - for the three kinds that have none - a drawn mark, or nothing at all at the
    /// limit, where the flood is the whole statement.
    /// </summary>
    private static void DrawFigure(DrawingContext context, TrayGlyphFrame frame, Zones zones, TrayGlyphPalette palette)
    {
        if (frame.Digits is string digits)
        {
            Color ink = frame.Fill == QuotaBarFill.Stale ? palette.Stale : palette.Ink;
            DrawText(context, digits, zones.FigureArea(), zones.InkHeight, ink, Face);
            return;
        }

        if (frame.Kind == TrayFrameKind.Failed)
        {
            DrawUpright(context, zones, palette.Bad);
        }
        else if (frame.Kind == TrayFrameKind.Waiting)
        {
            DrawRule(context, zones, palette.Ink);
        }
    }

    /// <summary>
    /// The failure mark: a stem, a unit of air, and a square dot. Snapped to whole device pixels and
    /// kept a unit clear of the gauge - a mark drawn on a half pixel is the exact smudge this
    /// drawing exists to remove.
    /// </summary>
    private static void DrawUpright(DrawingContext context, Zones zones, Color ink)
    {
        double width = Math.Max(2 * zones.Unit, Round(zones.Size / 8d));
        double dot = Math.Max(zones.Unit, Round(zones.Size / 16d));
        double total = Round(zones.InkHeight) - zones.Unit;
        double stem = total - dot - zones.Unit;

        if (stem <= 0)
        {
            return;
        }

        SolidColorBrush brush = new(ink);
        double x = Round((zones.Size - width) / 2);
        double y = Round(zones.InkTop);

        context.DrawRectangle(brush, null, new Rect(x, y, width, stem));
        context.DrawRectangle(brush, null, new Rect(x, y + stem + zones.Unit, width, dot));
    }

    /// <summary>Nothing read yet: a short rule, which is never a zero and never a second bar.</summary>
    private static void DrawRule(DrawingContext context, Zones zones, Color ink)
    {
        double width = Round(zones.Size * 0.375);
        double height = Math.Max(zones.Unit, Round(zones.Size / 16d));

        context.DrawRectangle(
            new SolidColorBrush(ink), null,
            new Rect(
                Round((zones.Size - width) / 2),
                Round(zones.InkTop + ((zones.InkHeight - height) / 2)),
                width,
                height));
    }

    /// <summary>
    /// Drawn as glyph outlines rather than text so it can be centred on its own ink rather than on
    /// a line box: at these sizes the line box is half again as tall as the figures, and centring
    /// on it puts them visibly high.
    /// <para>
    /// Width, not the area's height, sets the size. Two figures at a twelve-pixel height are
    /// eighteen pixels wide, so the geometry is measured once at a reference em, scaled uniformly
    /// to reach <paramref name="inkHeight"/>, then condensed to fit the area - and only when
    /// condensing would pass <see cref="CondenseFloor"/> does it give up height instead. That is
    /// why <c>100</c> is shorter than <c>92</c>.
    /// </para>
    /// </summary>
    private static void DrawText(DrawingContext context, string text, Rect area, double inkHeight, Color ink, Typeface face)
    {
        const double Reference = 100d;

        if (inkHeight <= 0 || area.Width <= 0)
        {
            return;
        }

        Geometry geometry = Text(text, Reference, ink, face).BuildGeometry(new Point(0, 0));
        Rect bounds = geometry.Bounds;

        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        double scale = inkHeight / bounds.Height;
        double condense = Math.Min(1d, area.Width / (bounds.Width * scale));

        if (condense < CondenseFloor)
        {
            condense = CondenseFloor;
            scale = area.Width / (bounds.Width * CondenseFloor);
        }

        double drawnWidth = bounds.Width * scale * condense;
        double drawnHeight = bounds.Height * scale;

        TransformGroup transform = new();
        transform.Children.Add(new ScaleTransform(scale * condense, scale, bounds.X, bounds.Y));
        transform.Children.Add(new TranslateTransform(
            area.X + Round((area.Width - drawnWidth) / 2) - bounds.X,
            area.Y + Round((area.Height - drawnHeight) / 2) - bounds.Y));

        geometry.Transform = transform;
        context.DrawGeometry(new SolidColorBrush(ink), null, geometry);
    }

    /// <summary>
    /// The gauge: bare track, then the fill. A stale fill lifts a unit off the left edge, so what the
    /// eye reads there is track where a current fill would have had colour - a difference in alpha
    /// rather than in hue, which is what keeps it legible once high contrast has resolved every tone
    /// to one system colour.
    /// </summary>
    private static void DrawGauge(
        DrawingContext context, TrayGlyphFrame frame, int size, Zones zones, TrayGlyphPalette palette, bool flooded)
    {
        context.DrawRectangle(new SolidColorBrush(palette.TrackColor), null, zones.GaugeArea(0, size));

        if (frame.Kind == TrayFrameKind.Failed)
        {
            context.DrawRectangle(new SolidColorBrush(palette.Bad), null, zones.GaugeArea(0, size));
            return;
        }

        if (flooded)
        {
            context.DrawRectangle(new SolidColorBrush(palette.Layer), null, zones.GaugeArea(0, size));
            return;
        }

        // Waiting draws bare track and never a zero-width fill.
        if (frame.Kind == TrayFrameKind.Waiting || frame.UsedPercent is not double used)
        {
            return;
        }

        double left = frame.Fill == QuotaBarFill.Stale ? zones.Unit : 0;
        double width = Math.Max(zones.Unit, Round(size * Math.Clamp(used / 100d, 0d, 1d)));

        context.DrawRectangle(
            new SolidColorBrush(palette.BandColor(frame.Fill)), null,
            zones.GaugeArea(left, Math.Min(width, size - left)));
    }

    private static FormattedText Text(string value, double em, Color ink, Typeface face) => new(
        value,
        CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight,
        face,
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
