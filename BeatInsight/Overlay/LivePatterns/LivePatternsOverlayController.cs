namespace BeatInsight.Overlay.LivePatterns;

public interface ILivePatternsOverlayView
{
    event EventHandler? Closed;
    bool IsVisible { get; }
    void Apply(
        LivePatternsOverlayState state,
        OverlayWidgetPlacement placement,
        OverlayTheme theme);
    void Show();
    void Hide();
    void Close();
}

public sealed class LivePatternsOverlayController : IDisposable
{
    private readonly OverlayEngine engine;
    private readonly Func<ILivePatternsOverlayView> viewFactory;
    private readonly OverlayWidgetRegistration registration;
    private ILivePatternsOverlayView? view;
    private bool disposed;

    public LivePatternsOverlayController(
        OverlayEngine engine,
        Func<ILivePatternsOverlayView> viewFactory)
    {
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        this.viewFactory = viewFactory ?? throw new ArgumentNullException(nameof(viewFactory));
        registration = new OverlayWidgetRegistration(
            new OverlayWidgetDefinition(
                OverlayWidgetIds.Patterns,
                "Live patterns",
                new HashSet<OverlayGameState> { OverlayGameState.Gameplay },
                RequiresAnalysisSnapshot: true),
            _ => Refresh(),
            snapshot => snapshot.HasAvailableGame && snapshot.LivePatterns is not null);
        engine.Register(registration);
    }

    public void Refresh()
    {
        if (disposed)
            return;

        ResolvedOverlayWidget? resolved = engine.GetVisibleWidgets()
            .FirstOrDefault(item =>
                item.Registration.Definition.Id == OverlayWidgetIds.Patterns);
        LivePatternsOverlayState state = LivePatternsSnapshotProjector.Project(
            resolved?.Snapshot);
        if (resolved is null || !state.HasContent)
        {
            HideAndClear();
            return;
        }

        ILivePatternsOverlayView activeView = EnsureView();
        activeView.Apply(state, resolved.Placement, resolved.Theme);
        if (!activeView.IsVisible)
            activeView.Show();
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        engine.Unregister(OverlayWidgetIds.Patterns);
        CloseView();
    }

    private ILivePatternsOverlayView EnsureView()
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
            LivePatternsOverlayState.Empty,
            engine.CurrentLayout.Widgets.FirstOrDefault(item =>
                item.WidgetId == OverlayWidgetIds.Patterns)
                ?? new OverlayWidgetPlacement(
                    OverlayWidgetIds.Patterns, false, 0, 0, 1, 1, 0),
            engine.CurrentTheme);
        if (view.IsVisible)
            view.Hide();
    }

    private void CloseView()
    {
        ILivePatternsOverlayView? closing = view;
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
