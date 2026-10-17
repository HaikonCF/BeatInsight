using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace BeatInsight.Overlay.GameplayHud;

public partial class GameplayHudWindow : Window, IGameplayHudOverlayView, IEditableOverlayView
{
    private readonly GameplayHudViewModel viewModel = new();
    private bool applyingPlacement;
    private bool editable;

    public event EventHandler<OverlayPlacementChangedEventArgs>? PlacementChanged;

    public GameplayHudWindow()
    {
        InitializeComponent();
        DataContext = viewModel;
        MinWidth = 280;
        MinHeight = 180;
        LocationChanged += (_, _) => PublishPlacement(resized: false);
        SizeChanged += (_, _) => PublishPlacement(resized: true);
    }

    public void Apply(
        GameplayHudOverlayState state,
        OverlayWidgetPlacement placement,
        OverlayTheme theme)
    {
        applyingPlacement = true;
        try
        {
            viewModel.Apply(state, theme);
            CurrentSectionBlock.Visibility = ToVisibility(state.IsCurrentSectionVisible);
            UpNextBlock.Visibility = ToVisibility(state.IsUpNextVisible);
            DifficultyTimelineBlock.Visibility = ToVisibility(state.IsDifficultyTimelineVisible);
            LivePatternsBlock.Visibility = ToVisibility(state.IsLivePatternsVisible);
            LiveStatsBlock.Visibility = ToVisibility(state.IsLiveStatsVisible);
            PerformanceBlock.Visibility = ToVisibility(state.IsPerformanceVisible);
            Left = placement.X;
            Top = placement.Y;
            Width = placement.Width;
            if (placement.HasCustomSize)
            {
                SizeToContent = SizeToContent.Manual;
                Height = placement.Height;
            }
            else
            {
                Height = double.NaN;
                SizeToContent = SizeToContent.Height;
            }
        }
        finally
        {
            applyingPlacement = false;
        }
    }

    public void SetInteractionMode(OverlayWindowInteractionMode mode)
    {
        editable = mode == OverlayWindowInteractionMode.Editable;
        EditChrome.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;
        OverlayWindowInteraction.Apply(this, mode);
    }

    private static Visibility ToVisibility(bool isVisible) =>
        isVisible ? Visibility.Visible : Visibility.Collapsed;

    internal (Visibility Current, Visibility Next, Visibility Timeline, Visibility Patterns)
        GetBlockVisibilitiesForTesting() =>
        (
            CurrentSectionBlock.Visibility,
            UpNextBlock.Visibility,
            DifficultyTimelineBlock.Visibility,
            LivePatternsBlock.Visibility
        );

    internal Visibility GetLiveStatsVisibilityForTesting() => LiveStatsBlock.Visibility;
    internal Visibility GetPerformanceVisibilityForTesting() => PerformanceBlock.Visibility;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        OverlayWindowInteraction.Apply(
            this,
            editable
                ? OverlayWindowInteractionMode.Editable
                : OverlayWindowInteractionMode.Passive);
    }

    private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (editable && e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!editable)
            return;

        if (SizeToContent != SizeToContent.Manual)
        {
            Width = Math.Max(MinWidth, ActualWidth);
            Height = Math.Max(MinHeight, ActualHeight);
            SizeToContent = SizeToContent.Manual;
        }
        Width = Math.Max(MinWidth, ActualWidth + e.HorizontalChange);
        Height = Math.Max(MinHeight, ActualHeight + e.VerticalChange);
    }

    private void PublishPlacement(bool resized)
    {
        if (!editable || applyingPlacement || !double.IsFinite(Left) || !double.IsFinite(Top))
            return;

        PlacementChanged?.Invoke(this, new(
            OverlayWidgetIds.GameplayHud, Left, Top, ActualWidth, ActualHeight, resized));
    }
}
