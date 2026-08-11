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
                drawingContext.DrawEllipse(null, pen, center, 4.5, 4.5);
                break;
            case ConnectionState.Waiting:
            case ConnectionState.NotInstalled:
                drawingContext.DrawEllipse(null, pen, center, 4, 4);
                break;
            case ConnectionState.Stale:
                DrawPolygon(drawingContext, brush, [new Point(5, 1.5), new Point(8.5, 5), new Point(5, 8.5), new Point(1.5, 5)]);
                break;
            case ConnectionState.Unavailable:
                DrawPolygon(drawingContext, brush, [new Point(5, 0.5), new Point(10, 9.5), new Point(0, 9.5)]);
                break;
            case ConnectionState.Error:
                drawingContext.DrawLine(new Pen(brush, 1.5), new Point(0.5, 0.5), new Point(9.5, 9.5));
                drawingContext.DrawLine(new Pen(brush, 1.5), new Point(9.5, 0.5), new Point(0.5, 9.5));
                break;
            case ConnectionState.Unsupported:
                drawingContext.DrawRectangle(brush, null, new Rect(0.5, 4, 9, 2));
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
