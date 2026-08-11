using System.Windows;
using System.Windows.Media;
using AiUsageMonitor.Infrastructure.Theming;

namespace AiUsageMonitor.App.Controls;

public sealed class QuotaBar : FrameworkElement
{
    public static readonly DependencyProperty UsedPercentProperty = Register<double?>(nameof(UsedPercent));
    public static readonly DependencyProperty ElapsedFractionProperty = Register<double?>(nameof(ElapsedFraction));
    public static readonly DependencyProperty LimitReachedProperty = Register<bool>(nameof(LimitReached));
    public static readonly DependencyProperty IsStaleProperty = Register<bool>(nameof(IsStale));
    public static readonly DependencyProperty ColorBarsByUsageProperty = Register(nameof(ColorBarsByUsage), true);
    public static readonly DependencyProperty TrackBrushProperty = Register<Brush?>(nameof(TrackBrush));
    public static readonly DependencyProperty AccentFillBrushProperty = Register<Brush?>(nameof(AccentFillBrush));
    public static readonly DependencyProperty HighFillBrushProperty = Register<Brush?>(nameof(HighFillBrush));
    public static readonly DependencyProperty ExhaustedFillBrushProperty = Register<Brush?>(nameof(ExhaustedFillBrush));
    public static readonly DependencyProperty StaleFillBrushProperty = Register<Brush?>(nameof(StaleFillBrush));
    public static readonly DependencyProperty HatchBrushProperty = Register<Brush?>(nameof(HatchBrush));
    public static readonly DependencyProperty MarkerBrushProperty = Register<Brush?>(nameof(MarkerBrush));
    public static readonly DependencyProperty MarkerGapBrushProperty = Register<Brush?>(nameof(MarkerGapBrush));

    static QuotaBar()
    {
        UseLayoutRoundingProperty.OverrideMetadata(typeof(QuotaBar), new FrameworkPropertyMetadata(true));
        SnapsToDevicePixelsProperty.OverrideMetadata(typeof(QuotaBar), new FrameworkPropertyMetadata(true));
    }

    public QuotaBar()
    {
        Focusable = false;
        IsHitTestVisible = false;
        Height = 11;
    }

    public double? UsedPercent { get => (double?)GetValue(UsedPercentProperty); set => SetValue(UsedPercentProperty, value); }
    public double? ElapsedFraction { get => (double?)GetValue(ElapsedFractionProperty); set => SetValue(ElapsedFractionProperty, value); }
    public bool LimitReached { get => (bool)GetValue(LimitReachedProperty); set => SetValue(LimitReachedProperty, value); }
    public bool IsStale { get => (bool)GetValue(IsStaleProperty); set => SetValue(IsStaleProperty, value); }
    public bool ColorBarsByUsage { get => (bool)GetValue(ColorBarsByUsageProperty); set => SetValue(ColorBarsByUsageProperty, value); }
    public Brush? TrackBrush { get => (Brush?)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public Brush? AccentFillBrush { get => (Brush?)GetValue(AccentFillBrushProperty); set => SetValue(AccentFillBrushProperty, value); }
    public Brush? HighFillBrush { get => (Brush?)GetValue(HighFillBrushProperty); set => SetValue(HighFillBrushProperty, value); }
    public Brush? ExhaustedFillBrush { get => (Brush?)GetValue(ExhaustedFillBrushProperty); set => SetValue(ExhaustedFillBrushProperty, value); }
    public Brush? StaleFillBrush { get => (Brush?)GetValue(StaleFillBrushProperty); set => SetValue(StaleFillBrushProperty, value); }
    public Brush? HatchBrush { get => (Brush?)GetValue(HatchBrushProperty); set => SetValue(HatchBrushProperty, value); }
    public Brush? MarkerBrush { get => (Brush?)GetValue(MarkerBrushProperty); set => SetValue(MarkerBrushProperty, value); }
    public Brush? MarkerGapBrush { get => (Brush?)GetValue(MarkerGapBrushProperty); set => SetValue(MarkerGapBrushProperty, value); }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 0)
        {
            return;
        }

        double trackY = Math.Round((ActualHeight - 5) / 2);
        Rect track = new(0, trackY, ActualWidth, 5);
        drawingContext.DrawRoundedRectangle(TrackBrush, null, track, 1, 1);

        if (UsedPercent is double used)
        {
            double fillWidth = ActualWidth * Math.Clamp(used / 100d, 0d, 1d);
            if (fillWidth > 0)
            {
                Rect fill = new(0, trackY, fillWidth, 5);
                QuotaBarFill role = QuotaBarFillSelector.Select(UsedPercent, LimitReached, ColorBarsByUsage, IsStale);
                drawingContext.PushClip(new RectangleGeometry(fill));
                drawingContext.DrawRoundedRectangle(FillBrushFor(role), null, track, 1, 1);
                drawingContext.Pop();

                if (role is QuotaBarFill.Exhausted && HatchBrush is not null)
                {
                    drawingContext.PushClip(new RectangleGeometry(fill));
                    Pen hatchPen = new(HatchBrush, 1);
                    for (double x = fill.Left - fill.Height; x < fill.Right; x += 5)
                    {
                        drawingContext.DrawLine(hatchPen, new Point(x, fill.Bottom), new Point(x + fill.Height, fill.Top));
                    }

                    drawingContext.Pop();
                }
            }
        }

        if (ElapsedFraction is double elapsed)
        {
            double x = ElapsedMarkerLayout.OffsetFor(elapsed, ActualWidth);
            drawingContext.DrawRectangle(MarkerGapBrush, null, new Rect(x, 0, 1, 11));
            drawingContext.DrawRectangle(MarkerBrush, null, new Rect(x + 1, 0, 2, 11));
            drawingContext.DrawRectangle(MarkerGapBrush, null, new Rect(x + 3, 0, 1, 11));
        }
    }

    private Brush? FillBrushFor(QuotaBarFill fill) => fill switch
    {
        QuotaBarFill.High => HighFillBrush,
        QuotaBarFill.Exhausted => ExhaustedFillBrush,
        QuotaBarFill.Stale => StaleFillBrush,
        _ => AccentFillBrush
    };

    private static DependencyProperty Register<T>(string name) =>
        DependencyProperty.Register(name, typeof(T), typeof(QuotaBar), new FrameworkPropertyMetadata(FrameworkPropertyMetadataOptions.AffectsRender));

    private static DependencyProperty Register(string name, bool defaultValue) =>
        DependencyProperty.Register(name, typeof(bool), typeof(QuotaBar), new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));
}
