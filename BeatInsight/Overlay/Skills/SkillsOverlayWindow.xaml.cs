using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace BeatInsight.Overlay.Skills;

public partial class SkillsOverlayWindow : Window, ISkillsOverlayView, IEditableOverlayView
{
    private readonly SkillsOverlayViewModel viewModel = new();
    private bool applyingPlacement;
    private bool editable;

    public event EventHandler<OverlayPlacementChangedEventArgs>? PlacementChanged;

    public SkillsOverlayWindow()
    {
        InitializeComponent();
        DataContext = viewModel;
        MinWidth = 260;
        MinHeight = 180;
        LocationChanged += (_, _) => PublishPlacement(resized: false);
        SizeChanged += (_, _) => PublishPlacement(resized: true);
    }

    public void Apply(
        SkillsOverlayState state,
        OverlayWidgetPlacement placement,
        OverlayTheme theme)
    {
        applyingPlacement = true;
        try
        {
            viewModel.Apply(state, theme);
            Left = placement.X;
            Top = placement.Y;
            Width = placement.Width;
            Height = placement.Height;
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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        OverlayWindowInteraction.Apply(this, OverlayWindowInteractionMode.Passive);
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

        Width = Math.Max(MinWidth, ActualWidth + e.HorizontalChange);
        Height = Math.Max(MinHeight, ActualHeight + e.VerticalChange);
    }

    private void PublishPlacement(bool resized)
    {
        if (!editable || applyingPlacement || !double.IsFinite(Left) || !double.IsFinite(Top))
            return;

        PlacementChanged?.Invoke(this, new(
            OverlayWidgetIds.Skills, Left, Top, ActualWidth, ActualHeight, resized));
    }
}
