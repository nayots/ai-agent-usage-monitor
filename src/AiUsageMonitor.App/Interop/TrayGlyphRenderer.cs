using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AiUsageMonitor.App.Theming;
using AiUsageMonitor.Infrastructure.Settings;
using AiUsageMonitor.Infrastructure.Theming;

namespace AiUsageMonitor.App.Interop;

/// <summary>The one extra mark a glyph can carry. Mutually exclusive, per the design.</summary>
public enum TrayOverlay
{
    None,

    /// <summary>A quota window is exhausted: a triangle over the bars.</summary>
    Alert,

    /// <summary>A provider is failing, so the bars may not be telling the truth: a cross.</summary>
    Error
}

/// <param name="UsedPercent">Null when the provider reported no value; the bar renders as bare track.</param>
/// <param name="Fill">The band, resolved by the same selector the widget's own bars use.</param>
/// <param name="StartsGroup">True on the first bar of each provider after the first, which earns a wider gap.</param>
public readonly record struct TrayGlyphBar(double? UsedPercent, QuotaBarFill Fill, bool StartsGroup);

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
/// Draws <c>docs/design/TrayGlyph.dc.html</c> into a notification-area icon: a stack of bars, one
/// per quota window, with the worst percentage above them as digits and a state overlay on top.
/// <para>
/// Everything is measured in device pixels. The bitmap is created at 96 dpi so one drawing unit is
/// one pixel, and the caller passes the shell's own small-icon metric, which already accounts for
/// the display's scaling.
/// </para>
/// </summary>
public static class TrayGlyphRenderer
{
    /// <summary>
    /// Renders one glyph and returns an <c>HICON</c> the caller owns and must destroy. Returns
    /// <see cref="IntPtr.Zero"/> if GDI refuses the bitmap, which the caller treats as "keep the
    /// icon you have" rather than as a failure worth surfacing.
    /// </summary>
    public static IntPtr Render(
        IReadOnlyList<TrayGlyphBar> bars,
        string? digits,
        bool digitsAreStale,
        TrayOverlay overlay,
        int size,
        TrayGlyphPalette palette)
    {
        RenderTargetBitmap? bitmap = RenderBitmap(bars, digits, digitsAreStale, overlay, size, palette);
        return bitmap is null ? IntPtr.Zero : ToIcon(bitmap, size);
    }

    /// <summary>
    /// The glyph as pixels, before it becomes an icon handle. Separate because GDI turns a wrong
    /// layout into a handle indistinguishable from a right one, and a sixteen-pixel drawing is only
    /// ever really verified by looking at the pixels - by a test or by an eye.
    /// </summary>
    public static RenderTargetBitmap? RenderBitmap(
        IReadOnlyList<TrayGlyphBar> bars,
        string? digits,
        bool digitsAreStale,
        TrayOverlay overlay,
        int size,
        TrayGlyphPalette palette)
    {
        if (size <= 0)
        {
            return null;
        }

        DrawingVisual visual = new();

        using (DrawingContext context = visual.RenderOpen())
        {
            Draw(context, bars, digits, digitsAreStale, overlay, size, palette);
        }

        RenderTargetBitmap bitmap = new(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static void Draw(
        DrawingContext context,
        IReadOnlyList<TrayGlyphBar> bars,
        string? digits,
        bool digitsAreStale,
        TrayOverlay overlay,
        int size,
        TrayGlyphPalette palette)
    {
        double scale = size / 16d;
        double digitHeight = string.IsNullOrEmpty(digits) ? 0 : Round(8 * scale);
        Layout layout = Layout.Fit(bars, size - digitHeight, scale);

        // Digits sit on top of the bars and the whole group is bottom-aligned; with no digits the
        // bars are centred instead. Both come straight from the design's flex rules.
        double contentHeight = digitHeight + layout.Height;
        double y = digitHeight > 0 ? size - contentHeight : Round((size - contentHeight) / 2);

        if (digitHeight > 0)
        {
            // Greyed with the bars they came from. Crisp ink over grey bars would say the number is
            // current when the widget's own row has already stopped claiming that.
            DrawDigits(context, digits!, y, digitHeight, size, digitsAreStale ? palette.Stale : palette.Ink);
            y += digitHeight;
        }

        SolidColorBrush track = new(palette.TrackColor);

        for (int index = 0; index < layout.Count; index++)
        {
            TrayGlyphBar bar = bars[index];

            if (index > 0)
            {
                y += bar.StartsGroup ? layout.GroupGap : layout.WithinGap;
            }

            context.DrawRectangle(track, null, new Rect(0, y, size, layout.BarHeight));

            if (bar.UsedPercent is double used)
            {
                double width = Round(size * Math.Clamp(used / 100d, 0d, 1d));

                if (width > 0)
                {
                    context.DrawRectangle(
                        new SolidColorBrush(palette.BandColor(bar.Fill)),
                        null,
                        new Rect(0, y, width, layout.BarHeight));
                }
            }

            y += layout.BarHeight;
        }

        DrawOverlay(context, overlay, size, scale, palette);
    }

    /// <summary>
    /// The digits are drawn as glyph outlines rather than text so they can be centred on their own
    /// ink rather than on a line box: at an 8-pixel em the line box is half again as tall as the
    /// digits, and centring on it puts them visibly off-centre in a 16-pixel square.
    /// </summary>
    private static void DrawDigits(DrawingContext context, string digits, double y, double height, int size, Color ink)
    {
        FormattedText text = new(
            digits,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            height,
            new SolidColorBrush(ink),
            numberSubstitution: null,
            TextFormattingMode.Display,
            pixelsPerDip: 1d);

        Geometry geometry = text.BuildGeometry(new Point(0, 0));
        Rect bounds = geometry.Bounds;

        if (bounds.IsEmpty || bounds.Width <= 0)
        {
            return;
        }

        // Never let a three-digit reading spill out of the square; the widget itself carries the
        // exact number, and an unreadable glyph is worse than a slightly compressed one.
        double horizontal = Math.Min(1d, size / bounds.Width);

        TransformGroup transform = new();
        transform.Children.Add(new ScaleTransform(horizontal, 1d, bounds.X, bounds.Y));
        transform.Children.Add(new TranslateTransform(
            Round((size - (bounds.Width * horizontal)) / 2) - bounds.X,
            Round(y + ((height - bounds.Height) / 2)) - bounds.Y));

        geometry.Transform = transform;
        context.DrawGeometry(new SolidColorBrush(ink), null, geometry);
    }

    private static void DrawOverlay(DrawingContext context, TrayOverlay overlay, int size, double scale, TrayGlyphPalette palette)
    {
        switch (overlay)
        {
            case TrayOverlay.Error:
            {
                // A disc in the bottom-right corner with a cross struck through it. The design
                // writes the cross as a character; at six pixels a glyph is a smudge, so it is two
                // strokes here - the same mark, drawn at a size a font cannot reach.
                double diameter = Math.Max(4, Round(6 * scale));
                double radius = diameter / 2;
                Point centre = new(size - radius, size - radius);

                context.DrawEllipse(new SolidColorBrush(palette.Bad), null, centre, radius, radius);

                double arm = radius * 0.45;
                Pen pen = new(new SolidColorBrush(palette.Layer), Math.Max(1, Math.Floor(scale)))
                {
                    StartLineCap = PenLineCap.Square,
                    EndLineCap = PenLineCap.Square
                };

                context.DrawLine(pen, new Point(centre.X - arm, centre.Y - arm), new Point(centre.X + arm, centre.Y + arm));
                context.DrawLine(pen, new Point(centre.X + arm, centre.Y - arm), new Point(centre.X - arm, centre.Y + arm));
                break;
            }

            case TrayOverlay.Alert:
            {
                double half = Math.Max(2, 3.5 * scale);
                double height = Math.Max(4, Round(6 * scale));
                double centreX = size / 2d;
                double centreY = size / 2d;

                StreamGeometry triangle = new();

                using (StreamGeometryContext figure = triangle.Open())
                {
                    figure.BeginFigure(new Point(centreX, centreY - (height / 2)), isFilled: true, isClosed: true);
                    figure.LineTo(new Point(centreX + half, centreY + (height / 2)), isStroked: true, isSmoothJoin: false);
                    figure.LineTo(new Point(centreX - half, centreY + (height / 2)), isStroked: true, isSmoothJoin: false);
                }

                triangle.Freeze();

                // Outlined in the layer colour so it stays a triangle when it lands on a full red
                // bar, which is the case it exists for.
                context.DrawGeometry(
                    new SolidColorBrush(palette.Bad),
                    new Pen(new SolidColorBrush(palette.Layer), Math.Max(1, Math.Floor(scale))),
                    triangle);
                break;
            }
        }
    }

    /// <summary>
    /// How the bars share the height left over after the digits.
    /// <para>
    /// Space is given up in a fixed order, and the order is the point: gaps go first, then bar
    /// height, and only when a single pixel per bar will not fit does a bar get dropped. Dropping
    /// is last because a missing bar is a window the user cannot see at all, whereas a thinner one
    /// still reports its number honestly. At 16 pixels with digits there are 8 left, which is
    /// three bars with their gaps or four without.
    /// </para>
    /// </summary>
    internal readonly record struct Layout(int Count, double BarHeight, double WithinGap, double GroupGap, double Height)
    {
        public static Layout Fit(IReadOnlyList<TrayGlyphBar> bars, double available, double scale)
        {
            int count = bars.Count;

            if (count <= 0 || available <= 0)
            {
                return new Layout(0, 0, 0, 0, 0);
            }

            double bar = Math.Max(1, Round(2 * scale));
            double within = Math.Max(0, Round(scale));
            double group = Math.Max(0, Round(2 * scale));

            while (group > 0 && Total(bars, count, bar, within, group) > available)
            {
                group -= 1;
                within = Math.Min(within, group);
            }

            if (count * bar > available)
            {
                bar = Math.Max(1, Math.Floor(available / count));
            }

            while (count > 1 && Total(bars, count, bar, within, group) > available)
            {
                count -= 1;
            }

            return new Layout(count, bar, within, group, Total(bars, count, bar, within, group));
        }

        private static double Total(IReadOnlyList<TrayGlyphBar> bars, int count, double bar, double within, double group)
        {
            double total = count * bar;

            for (int index = 1; index < count; index++)
            {
                total += bars[index].StartsGroup ? group : within;
            }

            return total;
        }
    }

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
