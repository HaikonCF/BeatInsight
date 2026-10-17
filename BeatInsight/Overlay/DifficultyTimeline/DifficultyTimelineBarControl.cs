using System.Globalization;
using System.Windows;
using System.Windows.Media;
using BeatInsight.Converters;
using BeatInsight.Integration.Game;

namespace BeatInsight.Overlay.DifficultyTimeline;

public sealed class DifficultyTimelineBarControl : FrameworkElement
{
    private static readonly StructureToBrushConverter StructureBrushConverter = new();
    private static readonly Brush BreakBackground = CreateBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
    private DrawingGroup? cachedBars;
    private DifficultyTimelineModel? cachedModel;
    private Size cachedSize;

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model),
        typeof(DifficultyTimelineModel),
        typeof(DifficultyTimelineBarControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnModelChanged));

    public static readonly DependencyProperty MarkerRatioProperty = DependencyProperty.Register(
        nameof(MarkerRatio),
        typeof(double),
        typeof(DifficultyTimelineBarControl),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CurrentSectionIndexProperty = DependencyProperty.Register(
        nameof(CurrentSectionIndex),
        typeof(int),
        typeof(DifficultyTimelineBarControl),
        new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MarkerBrushProperty = DependencyProperty.Register(
        nameof(MarkerBrush),
        typeof(Brush),
        typeof(DifficultyTimelineBarControl),
        new FrameworkPropertyMetadata(Brushes.Cyan, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HighlightBrushProperty = DependencyProperty.Register(
        nameof(HighlightBrush),
        typeof(Brush),
        typeof(DifficultyTimelineBarControl),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public DifficultyTimelineModel? Model
    {
        get => (DifficultyTimelineModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    public double MarkerRatio
    {
        get => (double)GetValue(MarkerRatioProperty);
        set => SetValue(MarkerRatioProperty, value);
    }

    public int CurrentSectionIndex
    {
        get => (int)GetValue(CurrentSectionIndexProperty);
        set => SetValue(CurrentSectionIndexProperty, value);
    }

    public Brush MarkerBrush
    {
        get => (Brush)GetValue(MarkerBrushProperty);
        set => SetValue(MarkerBrushProperty, value);
    }

    public Brush HighlightBrush
    {
        get => (Brush)GetValue(HighlightBrushProperty);
        set => SetValue(HighlightBrushProperty, value);
    }

    internal int BarBuildCount { get; private set; }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (Model is null || ActualWidth <= 0 || ActualHeight <= 0)
            return;

        if (!ReferenceEquals(cachedModel, Model) || cachedSize != RenderSize || cachedBars is null)
            RebuildBars();

        drawingContext.DrawDrawing(cachedBars);
        DrawCurrentSectionHighlight(drawingContext);
        DrawMarker(drawingContext);
    }

    internal IReadOnlyList<Rect> GetSegmentRectsForTesting() =>
        Model is null ? [] : BuildSegmentRects(Model, RenderSize);

    internal Rect? GetCurrentHighlightRectForTesting()
    {
        int index = FindCurrentSegmentPosition();
        return Model is null || index < 0
            ? null
            : BuildSegmentRects(Model, RenderSize)[index];
    }

    private void RebuildBars()
    {
        cachedModel = Model;
        cachedSize = RenderSize;
        cachedBars = new DrawingGroup();
        if (Model is null)
            return;

        IReadOnlyList<Rect> rects = BuildSegmentRects(Model, RenderSize);
        using (DrawingContext context = cachedBars.Open())
        {
            for (int index = 0; index < Model.Segments.Count; index++)
            {
                DifficultyTimelineSegment segment = Model.Segments[index];
                Rect rect = rects[index];
                if (segment.IsEmpty)
                {
                    context.DrawRectangle(BreakBackground, null, new Rect(
                        rect.Left,
                        0,
                        rect.Width,
                        RenderSize.Height));
                    context.PushOpacity(0.28);
                }

                context.DrawRectangle(ResolveStructureBrush(segment.DominantStructure), null, rect);
                if (segment.IsEmpty)
                    context.Pop();
            }
        }

        cachedBars.Freeze();
        BarBuildCount++;
    }

    private void DrawCurrentSectionHighlight(DrawingContext drawingContext)
    {
        int index = FindCurrentSegmentPosition();
        if (index < 0)
            return;

        Rect rect = SegmentRect(Model!.Segments[index], RenderSize);
        var pen = new Pen(HighlightBrush, 1.5);
        drawingContext.DrawRectangle(null, pen, rect);
    }

    private void DrawMarker(DrawingContext drawingContext)
    {
        double ratio = double.IsFinite(MarkerRatio) ? Math.Clamp(MarkerRatio, 0, 1) : 0;
        double x = ratio * ActualWidth;
        var pen = new Pen(MarkerBrush, 1.5);
        drawingContext.DrawLine(pen, new Point(x, 0), new Point(x, ActualHeight));
    }

    private int FindCurrentSegmentPosition()
    {
        if (Model is null || CurrentSectionIndex < 0)
            return -1;

        for (int index = 0; index < Model.Segments.Count; index++)
        {
            if (Model.Segments[index].SectionIndex == CurrentSectionIndex)
                return index;
        }

        return -1;
    }

    private static IReadOnlyList<Rect> BuildSegmentRects(
        DifficultyTimelineModel model,
        Size size)
    {
        var result = new Rect[model.Segments.Count];
        for (int index = 0; index < model.Segments.Count; index++)
            result[index] = SegmentRect(model.Segments[index], size);

        return result;
    }

    private static Rect SegmentRect(DifficultyTimelineSegment segment, Size size)
    {
        double left = segment.StartRatio * size.Width;
        double right = segment.EndRatio * size.Width;
        double height = segment.DifficultyRatio * size.Height;
        return new Rect(
            left,
            size.Height - height,
            Math.Max(0, right - left),
            Math.Max(0, height));
    }

    private static Brush ResolveStructureBrush(object structure) =>
        (Brush)StructureBrushConverter.Convert(
            structure,
            typeof(Brush),
            null,
            CultureInfo.InvariantCulture);

    private static Brush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static void OnModelChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is DifficultyTimelineBarControl control)
        {
            control.cachedBars = null;
            control.cachedModel = null;
        }
    }
}
