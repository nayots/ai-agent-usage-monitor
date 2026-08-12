using System.Windows;
using System.Windows.Media;
using AiUsageMonitor.Domain;

namespace AiUsageMonitor.App.Controls;

public sealed class StateGlyph : FrameworkElement
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(ConnectionState), typeof(StateGlyph), new FrameworkPropertyMetadata(ConnectionState.Waiting, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty GlyphBrushProperty = DependencyProperty.Register(
        nameof(GlyphBrush), typeof(Brush), typeof(StateGlyph), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public ConnectionState State { get => (ConnectionState)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public Brush? GlyphBrush { get => (Brush?)GetValue(GlyphBrushProperty); set => SetValue(GlyphBrushProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(10, 10);

    /// <summary>
    /// Every glyph inscribes in the same 8x8 optical box, inset 1 inside the 10x10 element, and
    /// stroked shapes account for half their pen width so their outer edge lands on it too.
    /// <para>
    /// The chip's label sits at a fixed margin from this element, so a glyph that reaches the
    /// element's edge is optically touching its word while an inset one is not: Error and
    /// Unavailable spanned the full 10 and rendered as "XError" and "!Unavailable" next to a
    /// correctly spaced "* Connected". The gap has to come from a shared box rather than from each
    /// shape's own extent, or it drifts again the next time a state is added.
    /// </para>
    /// </summary>
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        Brush brush = GlyphBrush ?? Brushes.Transparent;
        Pen pen = new(brush, 1);
        Point center = new(ActualWidth / 2, ActualHeight / 2);

        switch (State)
        {
            case ConnectionState.Connected:
                drawingContext.DrawEllipse(brush, null, center, 4, 4);
                break;
            case ConnectionState.Discovering:
                pen.DashStyle = DashStyles.Dash;
                drawingContext.DrawEllipse(null, pen, center, 3.5, 3.5);
                break;
            case ConnectionState.Waiting:
            case ConnectionState.NotInstalled:
                drawingContext.DrawEllipse(null, pen, center, 3.5, 3.5);
                break;
            case ConnectionState.Stale:
                DrawPolygon(drawingContext, brush, [new Point(5, 1), new Point(9, 5), new Point(5, 9), new Point(1, 5)]);
                break;
            case ConnectionState.Unavailable:
                DrawPolygon(drawingContext, brush, [new Point(5, 1), new Point(9, 9), new Point(1, 9)]);
                break;
            case ConnectionState.Error:
                // 1.75/8.25 rather than 1/9: the 1.5 pen is centred on the line, so it reaches
                // 0.75 beyond each endpoint.
                drawingContext.DrawLine(new Pen(brush, 1.5), new Point(1.75, 1.75), new Point(8.25, 8.25));
                drawingContext.DrawLine(new Pen(brush, 1.5), new Point(8.25, 1.75), new Point(1.75, 8.25));
                break;
            case ConnectionState.Unsupported:
                drawingContext.DrawRectangle(brush, null, new Rect(1, 4, 8, 2));
                break;
        }
    }

    private static void DrawPolygon(DrawingContext drawingContext, Brush brush, IReadOnlyList<Point> points)
    {
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(points[0], true, true);
            context.PolyLineTo(points.Skip(1).ToArray(), true, true);
        }

        drawingContext.DrawGeometry(brush, null, geometry);
    }
}
