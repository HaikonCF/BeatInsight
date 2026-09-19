using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using BeatInsight.ViewModels;

namespace BeatInsight.Controls;

public sealed class SpikeGraphControl : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty =
        DependencyProperty.Register(
            nameof(Points),
            typeof(IEnumerable<DifficultyPointViewModel>),
            typeof(SpikeGraphControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnPointsChanged));

    public IEnumerable<DifficultyPointViewModel>? Points
    {
        get => (IEnumerable<DifficultyPointViewModel>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    private static void OnPointsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SpikeGraphControl control)
        {
            if (e.OldValue is INotifyCollectionChanged oldColl)
                oldColl.CollectionChanged -= control.OnCollectionChanged;
            if (e.NewValue is INotifyCollectionChanged newColl)
                newColl.CollectionChanged += control.OnCollectionChanged;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateVisual();
    }

    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), 1.0);
    private static readonly Pen LinePen = new(new SolidColorBrush(Color.FromRgb(0xFF, 0x2E, 0x93)), 2.5);
    private static readonly Brush AreaBrush = new LinearGradientBrush(
        Color.FromArgb(0x44, 0xFF, 0x2E, 0x93),
        Color.FromArgb(0x02, 0xFF, 0x2E, 0x93),
        90.0);
    private static readonly Brush SpikeDotBrush = new SolidColorBrush(Color.FromRgb(0xF4, 0x3F, 0x5E));
    private static readonly Pen SpikeRingPen = new(new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)), 1.5);
    private static readonly Brush NormalDotBrush = new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA));

    static SpikeGraphControl()
    {
        GridPen.Freeze();
        LinePen.Freeze();
        AreaBrush.Freeze();
        SpikeDotBrush.Freeze();
        SpikeRingPen.Freeze();
        NormalDotBrush.Freeze();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 10 || height <= 10) return;

        // Draw horizontal grid lines (0%, 25%, 50%, 75%, 100%)
        for (int i = 0; i <= 4; i++)
        {
            double y = height - (i / 4.0 * (height - 12)) - 6;
            dc.DrawLine(GridPen, new Point(0, y), new Point(width, y));
        }

        var pointList = Points?.ToList();
        if (pointList is null || pointList.Count == 0) return;

        int count = pointList.Count;
        Point[] screenPoints = new Point[count];

        for (int i = 0; i < count; i++)
        {
            double x = count == 1 ? width / 2.0 : (double)i / (count - 1) * (width - 20) + 10;
            double localDifficulty = double.IsFinite(pointList[i].LocalDifficulty)
                ? pointList[i].LocalDifficulty
                : 0.0;
            double normalizedDiff = Math.Clamp(localDifficulty / 100.0, 0.0, 1.0);
            double y = (height - 12) - (normalizedDiff * (height - 20)) + 4;
            screenPoints[i] = new Point(x, y);
        }

        // Fill under curve
        if (count >= 2)
        {
            PathGeometry areaGeometry = new();
            PathFigure figure = new() { StartPoint = new Point(screenPoints[0].X, height - 6) };
            figure.Segments.Add(new LineSegment(screenPoints[0], isStroked: false));

            for (int i = 1; i < count; i++)
            {
                figure.Segments.Add(new LineSegment(screenPoints[i], isStroked: false));
            }

            figure.Segments.Add(new LineSegment(new Point(screenPoints[^1].X, height - 6), isStroked: false));
            figure.IsClosed = true;
            areaGeometry.Figures.Add(figure);
            dc.DrawGeometry(AreaBrush, null, areaGeometry);

            // Draw line
            for (int i = 0; i < count - 1; i++)
            {
                dc.DrawLine(LinePen, screenPoints[i], screenPoints[i + 1]);
            }
        }

        // Draw dots and spike markers
        for (int i = 0; i < count; i++)
        {
            var pt = pointList[i];
            Point sp = screenPoints[i];

            if (pt.IsSpike)
            {
                dc.DrawEllipse(SpikeDotBrush, SpikeRingPen, sp, 5.0, 5.0);
            }
            else
            {
                dc.DrawEllipse(NormalDotBrush, null, sp, 2.5, 2.5);
            }
        }
    }
}
