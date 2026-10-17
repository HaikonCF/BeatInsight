namespace BeatInsight.Overlay.DifficultyTimeline;

public interface IDifficultyTimelineOverlayView
{
    event EventHandler? Closed;
    bool IsVisible { get; }
    void Apply(
        DifficultyTimelineOverlayState state,
        OverlayWidgetPlacement placement,
        OverlayTheme theme);
    void Show();
    void Hide();
    void Close();
}

public sealed class DifficultyTimelineOverlayController : IDisposable
{
    private readonly OverlayEngine engine;
    private readonly Func<IDifficultyTimelineOverlayView> viewFactory;
    private readonly OverlayWidgetRegistration registration;
    private IDifficultyTimelineOverlayView? view;
    private bool disposed;

    public DifficultyTimelineOverlayController(
        OverlayEngine engine,
        Func<IDifficultyTimelineOverlayView> viewFactory)
    {
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        this.viewFactory = viewFactory ?? throw new ArgumentNullException(nameof(viewFactory));
        registration = new OverlayWidgetRegistration(
            new OverlayWidgetDefinition(
                OverlayWidgetIds.DifficultyTimeline,
                "Difficulty timeline",
                new HashSet<OverlayGameState> { OverlayGameState.Gameplay },
                RequiresAnalysisSnapshot: true),
            _ => Refresh(),
            snapshot => snapshot.HasAvailableGame && snapshot.DifficultyTimeline is not null);
        engine.Register(registration);
    }

    public void Refresh()
    {
        if (disposed)
            return;

        ResolvedOverlayWidget? resolved = engine.GetVisibleWidgets()
            .FirstOrDefault(item =>
                item.Registration.Definition.Id == OverlayWidgetIds.DifficultyTimeline);
        DifficultyTimelineOverlayState state =
            DifficultyTimelineSnapshotProjector.Project(resolved?.Snapshot);
        if (resolved is null || !state.HasContent)
        {
            HideAndClear();
            return;
        }

        IDifficultyTimelineOverlayView activeView = EnsureView();
        activeView.Apply(state, resolved.Placement, resolved.Theme);
        if (!activeView.IsVisible)
            activeView.Show();
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        engine.Unregister(OverlayWidgetIds.DifficultyTimeline);
        CloseView();
    }

    private IDifficultyTimelineOverlayView EnsureView()
    {
        if (view is not null)
            return view;

        view = viewFactory();
        view.Closed += View_Closed;
        return view;
    }

    private void HideAndClear()
    {
        if (view is null)
            return;

        view.Apply(
            DifficultyTimelineOverlayState.Empty,
            engine.CurrentLayout.Widgets.FirstOrDefault(item =>
                item.WidgetId == OverlayWidgetIds.DifficultyTimeline)
                ?? new OverlayWidgetPlacement(
                    OverlayWidgetIds.DifficultyTimeline, false, 0, 0, 1, 1, 0),
            engine.CurrentTheme);
        if (view.IsVisible)
            view.Hide();
    }

    private void CloseView()
    {
        IDifficultyTimelineOverlayView? closing = view;
        view = null;
        if (closing is null)
            return;

        closing.Closed -= View_Closed;
        closing.Close();
    }

    private void View_Closed(object? sender, EventArgs e)
    {
        if (view is null)
            return;

        view.Closed -= View_Closed;
        view = null;
    }
}
