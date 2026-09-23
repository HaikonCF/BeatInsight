using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BeatInsight.Converters;
using BeatInsight.ViewModels;

namespace BeatInsight.Controls;

/// <summary>
/// Lightweight timeline projection of the section data already prepared by the Overview.
/// This control performs presentation-only coordinate mapping; it does not analyze sections.
/// </summary>
public sealed class SectionTimelineGraphControl : FrameworkElement
{
    private const double horizontalPadding = 12.0;
    private const double legendTop = 6.0;
    private const double legendHeight = 16.0;
    private const double plotTop = 28.0;
    private const double bandHeight = 18.0;
    private const double bandBottomPadding = 7.0;
    private const double bandGap = 8.0;
    private const double hitRadius = 10.0;
    private const double bandCornerRadius = 3.0;

    private static readonly Pen GridPen = CreatePen(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF), 1.0);
    private static readonly Pen LinePen = CreatePen(Color.FromRgb(0xFF, 0x2E, 0x93), 2.25);
    private static readonly Pen PointRingPen = CreatePen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF), 1.0);
    private static readonly Pen HoverBandPen = CreatePen(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF), 1.5);
    private static readonly Pen SelectedBandPen = CreatePen(Color.FromRgb(0xFF, 0x2E, 0x93), 2.25);
    private static readonly Brush BackgroundBrush = CreateBrush(Color.FromRgb(0x0F, 0x11, 0x17));
    private static readonly Brush BandBackgroundBrush = CreateBrush(Color.FromRgb(0x18, 0x1A, 0x22));
    private static readonly Brush HoverGlowBrush = CreateBrush(Color.FromArgb(0x55, 0xFF, 0x2E, 0x93));
    private static readonly Brush SelectedBandBrush = CreateBrush(Color.FromArgb(0x45, 0xFF, 0x2E, 0x93));
    private static readonly Brush LabelBrush = CreateBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
    private static readonly Brush LegendTextBrush = CreateBrush(Color.FromRgb(0xA0, 0xA5, 0xB5));
    private static readonly Brush AreaBrush = CreateAreaBrush();
    private static readonly Typeface LabelTypeface = new("Segoe UI Semibold");
    private static readonly Typeface LegendTypeface = new("Segoe UI");
    private static readonly StructureToBrushConverter StructureBrushConverter = new();
    private static readonly string[] LegendStructures = ["Jump", "Stream", "Tech", "ClassicMixed"];

    private int hoveredSectionIndex = -1;

    public static readonly DependencyProperty SectionsProperty = DependencyProperty.Register(
        nameof(Sections),
        typeof(IEnumerable<SectionTimelineItemViewModel>),
        typeof(SectionTimelineGraphControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSectionsChanged));

    public IEnumerable<SectionTimelineItemViewModel>? Sections
    {
        get => (IEnumerable<SectionTimelineItemViewModel>?)GetValue(SectionsProperty);
        set => SetValue(SectionsProperty, value);
    }

    public static readonly DependencyProperty SelectedSectionProperty = DependencyProperty.Register(
        nameof(SelectedSection),
        typeof(SectionTimelineItemViewModel),
        typeof(SectionTimelineGraphControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public SectionTimelineItemViewModel? SelectedSection
    {
        get => (SectionTimelineItemViewModel?)GetValue(SelectedSectionProperty);
        set => SetValue(SelectedSectionProperty, value);
    }

    internal int HoveredSectionIndex => hoveredSectionIndex;
    internal int SelectedSectionIndex => GetSectionIndex(SelectedSection);

    private static void OnSectionsChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not SectionTimelineGraphControl control)
            return;

        if (args.OldValue is INotifyCollectionChanged oldCollection)
            oldCollection.CollectionChanged -= control.OnCollectionChanged;
        if (args.NewValue is INotifyCollectionChanged newCollection)
            newCollection.CollectionChanged += control.OnCollectionChanged;

        control.ClearHover();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        ClearHover();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 10 || height <= 10)
            return;

        drawingContext.DrawRoundedRectangle(BackgroundBrush, null, new Rect(0, 0, width, height), 8, 8);
        TimelineLayout layout = BuildLayout(width, height);
        if (layout.Points.Count == 0)
            return;

        DrawLegend(drawingContext, width);
        DrawGrid(drawingContext, layout.Metrics, width);
        DrawDifficultyCurve(drawingContext, layout);
        DrawStructureBand(drawingContext, layout);
        DrawPoints(drawingContext, layout.Points);
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        base.OnMouseMove(args);
        UpdateHover(args.GetPosition(this));
    }

    protected override void OnMouseLeave(MouseEventArgs args)
    {
        base.OnMouseLeave(args);
        ClearHover();
    }

    internal IReadOnlyList<RenderPoint> GetRenderPointsForTesting() =>
        BuildLayout(ActualWidth, ActualHeight).Points;

    internal IReadOnlyList<StructureBandSegment> GetStructureBandSegmentsForTesting() =>
        BuildLayout(ActualWidth, ActualHeight).MergedBandSegments;

    internal IReadOnlyList<Rect> GetSectionBandRectsForTesting() =>
        BuildLayout(ActualWidth, ActualHeight).SectionBandRects;

    internal int HitTestSectionIndex(Point position)
    {
        TimelineLayout layout = BuildLayout(ActualWidth, ActualHeight);
        double bestDistanceSquared = hitRadius * hitRadius;
        int bestIndex = -1;

        foreach (RenderPoint point in layout.Points)
        {
            Vector delta = point.Position - position;
            double distanceSquared = delta.X * delta.X + delta.Y * delta.Y;
            if (distanceSquared <= bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                bestIndex = point.SourceIndex;
            }
        }

        if (bestIndex >= 0)
            return bestIndex;

        for (int index = 0; index < layout.SectionBandRects.Count; index++)
        {
            if (layout.SectionBandRects[index].Contains(position))
                return index;
        }

        return -1;
    }

    internal void UpdateHover(Point position)
    {
        int newIndex = HitTestSectionIndex(position);
        if (newIndex == hoveredSectionIndex)
            return;

        hoveredSectionIndex = newIndex;
        SectionTimelineItemViewModel? section = newIndex >= 0
            ? Sections?.ElementAtOrDefault(newIndex)
            : null;
        ToolTip = section is null
            ? null
            : $"{section.TimeRangeDisplay}\n{section.Structure}\nLocal Difficulty: {section.LocalDifficultyDisplay}";
        Cursor = section is null ? Cursors.Arrow : Cursors.Hand;
        ToolTipService.SetInitialShowDelay(this, 100);
        InvalidateVisual();
    }

    private void DrawLegend(DrawingContext drawingContext, double width)
    {
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double x = horizontalPadding;
        foreach (string structure in LegendStructures)
        {
            FormattedText text = CreateText(structure, 9.0, LegendTypeface, LegendTextBrush, pixelsPerDip);
            double itemWidth = 8 + 5 + text.WidthIncludingTrailingWhitespace + 12;
            if (x + itemWidth > width - horizontalPadding)
                break;

            Brush brush = ResolveStructureBrush(structure);
            drawingContext.DrawEllipse(brush, null, new Point(x + 4, legendTop + legendHeight / 2), 3.5, 3.5);
            drawingContext.DrawText(text, new Point(x + 13, legendTop + (legendHeight - text.Height) / 2));
            x += itemWidth;
        }
    }

    private static void DrawGrid(DrawingContext drawingContext, GraphMetrics metrics, double width)
    {
        for (int index = 0; index <= 4; index++)
        {
            double y = metrics.PlotBottom - index / 4.0 * (metrics.PlotBottom - metrics.PlotTop);
            drawingContext.DrawLine(GridPen, new Point(horizontalPadding, y), new Point(width - horizontalPadding, y));
        }
    }

    private static void DrawDifficultyCurve(DrawingContext drawingContext, TimelineLayout layout)
    {
        if (layout.Points.Count < 2)
            return;

        var area = new StreamGeometry();
        using (StreamGeometryContext context = area.Open())
        {
            context.BeginFigure(new Point(layout.Points[0].Position.X, layout.Metrics.PlotBottom), true, true);
            context.LineTo(layout.Points[0].Position, true, false);
            for (int index = 1; index < layout.Points.Count; index++)
                context.LineTo(layout.Points[index].Position, true, false);
            context.LineTo(new Point(layout.Points[^1].Position.X, layout.Metrics.PlotBottom), true, false);
        }
        area.Freeze();
        drawingContext.DrawGeometry(AreaBrush, null, area);

        for (int index = 0; index < layout.Points.Count - 1; index++)
            drawingContext.DrawLine(LinePen, layout.Points[index].Position, layout.Points[index + 1].Position);
    }

    private void DrawStructureBand(DrawingContext drawingContext, TimelineLayout layout)
    {
        Rect bandBounds = new(
            horizontalPadding,
            layout.Metrics.BandTop,
            Math.Max(0, ActualWidth - horizontalPadding * 2),
            bandHeight);
        drawingContext.DrawRoundedRectangle(BandBackgroundBrush, null, bandBounds, bandCornerRadius, bandCornerRadius);

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (StructureBandSegment segment in layout.MergedBandSegments)
        {
            Rect visibleBounds = InsetHorizontally(segment.Bounds, 0.5);
            drawingContext.DrawRoundedRectangle(
                ResolveStructureBrush(segment.Structure),
                null,
                visibleBounds,
                bandCornerRadius,
                bandCornerRadius);

            if (segment.ShowsLabel)
            {
                FormattedText text = CreateText(segment.Structure, 9.0, LabelTypeface, LabelBrush, pixelsPerDip);
                drawingContext.DrawText(text, new Point(
                    segment.Bounds.Left + (segment.Bounds.Width - text.Width) / 2,
                    segment.Bounds.Top + (segment.Bounds.Height - text.Height) / 2));
            }
        }

        int selectedSectionIndex = SelectedSectionIndex;
        if (selectedSectionIndex >= 0 && selectedSectionIndex < layout.SectionBandRects.Count)
        {
            Rect selectedBounds = InsetHorizontally(
                layout.SectionBandRects[selectedSectionIndex],
                0.75);

            drawingContext.DrawRoundedRectangle(
                SelectedBandBrush,
                SelectedBandPen,
                selectedBounds,
                bandCornerRadius,
                bandCornerRadius);
        }

        if (hoveredSectionIndex >= 0 && hoveredSectionIndex < layout.SectionBandRects.Count)
        {
            Rect hoveredBounds = InsetHorizontally(
                layout.SectionBandRects[hoveredSectionIndex],
                0.75);

            drawingContext.DrawRoundedRectangle(
                HoverBandBrush,
                HoverBandPen,
                hoveredBounds,
                bandCornerRadius,
                bandCornerRadius);
        }
    }

    private void DrawPoints(DrawingContext drawingContext, IReadOnlyList<RenderPoint> points)
    {
        double pointRadius = points.Count > 100 ? 2.25 : points.Count > 25 ? 3.0 : 4.0;
        int selectedSectionIndex = SelectedSectionIndex;
        foreach (RenderPoint point in points)
        {
            bool hovered = point.SourceIndex == hoveredSectionIndex;
            bool selected = point.SourceIndex == selectedSectionIndex;
            if (selected)
                drawingContext.DrawEllipse(SelectedBandBrush, null, point.Position, pointRadius + 6, pointRadius + 6);
            if (hovered)
                drawingContext.DrawEllipse(HoverGlowBrush, null, point.Position, pointRadius + 5, pointRadius + 5);

            drawingContext.DrawEllipse(
                ResolveStructureBrush(point.Section.Structure),
                hovered ? PointRingPen : selected ? SelectedBandPen : null,
                point.Position,
                hovered || selected ? pointRadius + 2 : pointRadius,
                hovered || selected ? pointRadius + 2 : pointRadius);
        }
    }

    private int GetSectionIndex(SectionTimelineItemViewModel? section)
    {
        if (section is null || Sections is null)
            return -1;

        int index = 0;
        foreach (SectionTimelineItemViewModel candidate in Sections)
        {
            if (ReferenceEquals(candidate, section))
                return index;
            index++;
        }

        return -1;
    }

    private void ClearHover()
    {
        if (hoveredSectionIndex < 0 && ToolTip is null)
            return;

        hoveredSectionIndex = -1;
        ToolTip = null;
        Cursor = Cursors.Arrow;
        InvalidateVisual();
    }

    private TimelineLayout BuildLayout(double width, double height)
    {
        SectionTimelineItemViewModel[] sections = Sections?.ToArray() ?? [];
        GraphMetrics metrics = GetMetrics(height);
        if (sections.Length == 0 || width <= horizontalPadding * 2 || metrics.PlotBottom <= metrics.PlotTop)
            return TimelineLayout.Empty(metrics);

        double finiteStart = sections.Where(section => double.IsFinite(section.StartTimeMs))
            .Select(section => section.StartTimeMs).DefaultIfEmpty(0).Min();
        double finiteEnd = sections.Where(section => double.IsFinite(section.EndTimeMs))
            .Select(section => section.EndTimeMs).DefaultIfEmpty(finiteStart).Max();
        double timeSpan = finiteEnd - finiteStart;
        bool useTime = double.IsFinite(timeSpan) && timeSpan > 0;
        double plotWidth = width - horizontalPadding * 2;
        double plotHeight = metrics.PlotBottom - metrics.PlotTop;
        var sectionBandRects = new Rect[sections.Length];
        var points = new RenderPoint[sections.Length];

        for (int index = 0; index < sections.Length; index++)
        {
            SectionTimelineItemViewModel section = sections[index];
            (double normalizedStart, double normalizedEnd) = NormalizeSectionTime(
                section, index, sections.Length, finiteStart, timeSpan, useTime);
            double left = horizontalPadding + normalizedStart * plotWidth;
            double right = horizontalPadding + normalizedEnd * plotWidth;
            var bandRect = new Rect(left, metrics.BandTop, Math.Max(0.5, right - left), bandHeight);
            sectionBandRects[index] = bandRect;

            double difficulty = double.IsFinite(section.LocalDifficulty)
                ? Math.Clamp(section.LocalDifficulty, 0, 100)
                : 0;
            double x = bandRect.Left + bandRect.Width / 2;
            double y = metrics.PlotBottom - difficulty / 100.0 * plotHeight;
            points[index] = new RenderPoint(index, section, new Point(x, y));
        }

        IReadOnlyList<StructureBandSegment> mergedSegments = MergeAdjacentSegments(sections, sectionBandRects);
        return new TimelineLayout(metrics, points, sectionBandRects, mergedSegments);
    }

    private IReadOnlyList<StructureBandSegment> MergeAdjacentSegments(
        IReadOnlyList<SectionTimelineItemViewModel> sections,
        IReadOnlyList<Rect> sectionRects)
    {
        var result = new List<StructureBandSegment>();
        int firstIndex = 0;
        for (int index = 1; index <= sections.Count; index++)
        {
            bool continues = index < sections.Count
                && string.Equals(sections[index].Structure, sections[firstIndex].Structure, StringComparison.OrdinalIgnoreCase)
                && sectionRects[index].Left <= sectionRects[index - 1].Right + 1.0;
            if (continues)
                continue;

            Rect bounds = Rect.Union(sectionRects[firstIndex], sectionRects[index - 1]);
            string structure = sections[firstIndex].Structure;
            bool showsLabel = CanShowLabel(structure, bounds.Width);
            result.Add(new StructureBandSegment(firstIndex, index - 1, structure, bounds, showsLabel));
            firstIndex = index;
        }

        return result;
    }

    private bool CanShowLabel(string structure, double availableWidth)
    {
        FormattedText text = CreateText(
            structure,
            9.0,
            LabelTypeface,
            LabelBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return availableWidth >= text.Width + 12;
    }

    private static (double Start, double End) NormalizeSectionTime(
        SectionTimelineItemViewModel section,
        int index,
        int count,
        double finiteStart,
        double timeSpan,
        bool useTime)
    {
        if (useTime
            && double.IsFinite(section.StartTimeMs)
            && double.IsFinite(section.EndTimeMs)
            && section.EndTimeMs > section.StartTimeMs)
        {
            return (
                Math.Clamp((section.StartTimeMs - finiteStart) / timeSpan, 0, 1),
                Math.Clamp((section.EndTimeMs - finiteStart) / timeSpan, 0, 1));
        }

        return ((double)index / count, (double)(index + 1) / count);
    }

    private static GraphMetrics GetMetrics(double height)
    {
        double availableBottom = Math.Max(plotTop, height - bandBottomPadding);

        double bandTop = Math.Max(
            plotTop,
            availableBottom - bandHeight);

        double plotBottom = Math.Max(
            plotTop,
            bandTop - bandGap);

        return new GraphMetrics(
            plotTop,
            plotBottom,
            bandTop);
    }

    private static Rect InsetHorizontally(Rect rect, double amount)
    {
        double inset = Math.Min(amount, rect.Width / 2);
        return new Rect(rect.Left + inset, rect.Top, Math.Max(0, rect.Width - inset * 2), rect.Height);
    }

    private static FormattedText CreateText(
        string text,
        double fontSize,
        Typeface typeface,
        Brush brush,
        double pixelsPerDip) => new(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            brush,
            pixelsPerDip);

    internal static Brush ResolveStructureBrush(string structure) =>
        (Brush)StructureBrushConverter.Convert(structure, typeof(Brush), null, CultureInfo.InvariantCulture);

    private static Pen CreatePen(Color color, double thickness)
    {
        var pen = new Pen(CreateBrush(color), thickness);
        pen.Freeze();
        return pen;
    }
    private static readonly Brush HoverBandBrush =
    CreateBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
    private static Brush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Brush CreateAreaBrush()
    {
        var brush = new LinearGradientBrush(
            Color.FromArgb(0x3D, 0xFF, 0x2E, 0x93),
            Color.FromArgb(0x02, 0xFF, 0x2E, 0x93),
            90);
        brush.Freeze();
        return brush;
    }

    internal readonly record struct RenderPoint(
        int SourceIndex,
        SectionTimelineItemViewModel Section,
        Point Position);

    internal readonly record struct StructureBandSegment(
        int FirstSourceIndex,
        int LastSourceIndex,
        string Structure,
        Rect Bounds,
        bool ShowsLabel);

    private readonly record struct GraphMetrics(double PlotTop, double PlotBottom, double BandTop);

    private sealed record TimelineLayout(
        GraphMetrics Metrics,
        IReadOnlyList<RenderPoint> Points,
        IReadOnlyList<Rect> SectionBandRects,
        IReadOnlyList<StructureBandSegment> MergedBandSegments)
    {
        public static TimelineLayout Empty(GraphMetrics metrics) => new(
            metrics,
            Array.Empty<RenderPoint>(),
            Array.Empty<Rect>(),
            Array.Empty<StructureBandSegment>());
    }
    public event EventHandler<SectionClickedEventArgs>? SectionClicked;
    public sealed class SectionClickedEventArgs : EventArgs
    {
        public SectionClickedEventArgs(int index)
        {
            Index = index;
        }

        public int Index { get; }
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs args)
    {
        base.OnMouseLeftButtonDown(args);

        int index = HitTestSectionIndex(args.GetPosition(this));
        if (index < 0)
            return;

        SectionClicked?.Invoke(this, new SectionClickedEventArgs(index));
    }
}
