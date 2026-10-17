namespace BeatInsight.Overlay.Skills;

public interface ISkillsOverlayView
{
    event EventHandler? Closed;

    bool IsVisible { get; }

    void Apply(
        SkillsOverlayState state,
        OverlayWidgetPlacement placement,
        OverlayTheme theme);

    void Show();

    void Hide();

    void Close();
}

public sealed class SkillsOverlayController : IDisposable
{
    private readonly OverlayEngine engine;
    private readonly Func<ISkillsOverlayView> viewFactory;
    private readonly OverlayWidgetRegistration registration;
    private ISkillsOverlayView? view;
    private bool disposed;

    public SkillsOverlayController(
        OverlayEngine engine,
        Func<ISkillsOverlayView> viewFactory)
    {
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        this.viewFactory = viewFactory ?? throw new ArgumentNullException(nameof(viewFactory));
        registration = new OverlayWidgetRegistration(
            new OverlayWidgetDefinition(
                OverlayWidgetIds.Skills,
                "Skills",
                new HashSet<OverlayGameState> { OverlayGameState.SongSelect },
                RequiresAnalysisSnapshot: true),
            _ => Refresh(),
            snapshot => snapshot.HasAvailableGame);
        engine.Register(registration);
    }

    public void Refresh()
    {
        if (disposed)
        {
            return;
        }

        ResolvedOverlayWidget? resolved = engine.GetVisibleWidgets()
            .FirstOrDefault(item =>
                item.Registration.Definition.Id == OverlayWidgetIds.Skills);
        SkillsOverlayState state = engine.IsOverlayEditMode
            ? OverlayEditPreview.Skills
            : SkillsSnapshotProjector.Project(resolved?.Snapshot);
        if (resolved is null || !state.HasContent)
        {
            HideAndClear();
            return;
        }

        ISkillsOverlayView activeView = EnsureView();
        activeView.Apply(state, resolved.Placement, resolved.Theme);
        ApplyInteractionMode(activeView);
        if (!activeView.IsVisible)
        {
            activeView.Show();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        engine.Unregister(OverlayWidgetIds.Skills);
        CloseView();
    }

    private ISkillsOverlayView EnsureView()
    {
        if (view is not null)
        {
            return view;
        }

        view = viewFactory();
        view.Closed += View_Closed;
        if (view is IEditableOverlayView editable)
            editable.PlacementChanged += View_PlacementChanged;
        return view;
    }

    private void HideAndClear()
    {
        if (view is null)
        {
            return;
        }

        view.Apply(
            SkillsOverlayState.Empty,
            engine.CurrentLayout.Widgets.FirstOrDefault(item =>
                item.WidgetId == OverlayWidgetIds.Skills)
                ?? new OverlayWidgetPlacement(
                    OverlayWidgetIds.Skills,
                    false,
                    0, 0, 1, 1, 0),
            engine.CurrentTheme);
        if (view.IsVisible)
        {
            view.Hide();
        }
    }

    private void CloseView()
    {
        ISkillsOverlayView? closing = view;
        view = null;
        if (closing is null)
        {
            return;
        }

        closing.Closed -= View_Closed;
        if (closing is IEditableOverlayView editable)
            editable.PlacementChanged -= View_PlacementChanged;
        closing.Close();
    }

    private void View_Closed(object? sender, EventArgs e)
    {
        if (view is not null)
        {
            view.Closed -= View_Closed;
            if (view is IEditableOverlayView editable)
                editable.PlacementChanged -= View_PlacementChanged;
            view = null;
        }
    }

    private void ApplyInteractionMode(ISkillsOverlayView activeView)
    {
        if (activeView is IEditableOverlayView editable)
            editable.SetInteractionMode(engine.IsOverlayEditMode
                ? OverlayWindowInteractionMode.Editable
                : OverlayWindowInteractionMode.Passive);
    }

    private void View_PlacementChanged(object? sender, OverlayPlacementChangedEventArgs e) =>
        engine.UpdateWorkingPlacement(e.WidgetId, e.X, e.Y, e.Width, e.Height, e.Resized);
}
