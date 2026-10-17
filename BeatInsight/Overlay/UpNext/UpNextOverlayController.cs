namespace BeatInsight.Overlay.UpNext;

public interface IUpNextOverlayView
{
    event EventHandler? Closed;
    bool IsVisible { get; }
    void Apply(UpNextOverlayState state, OverlayWidgetPlacement placement, OverlayTheme theme);
    void Show();
    void Hide();
    void Close();
}

public sealed class UpNextOverlayController : IDisposable
{
    private readonly OverlayEngine engine;
    private readonly Func<IUpNextOverlayView> viewFactory;
    private readonly OverlayWidgetRegistration registration;
    private IUpNextOverlayView? view;
    private bool disposed;

    public UpNextOverlayController(
        OverlayEngine engine,
        Func<IUpNextOverlayView> viewFactory)
    {
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        this.viewFactory = viewFactory ?? throw new ArgumentNullException(nameof(viewFactory));
        registration = new OverlayWidgetRegistration(
            new OverlayWidgetDefinition(
                OverlayWidgetIds.UpNext,
                "Up next",
                new HashSet<OverlayGameState> { OverlayGameState.Gameplay },
                RequiresAnalysisSnapshot: true),
            _ => Refresh(),
            snapshot => snapshot.HasAvailableGame && snapshot.NextSection is not null);
        engine.Register(registration);
    }

    public void Refresh()
    {
        if (disposed)
            return;

        ResolvedOverlayWidget? resolved = engine.GetVisibleWidgets()
            .FirstOrDefault(item =>
                item.Registration.Definition.Id == OverlayWidgetIds.UpNext);
        UpNextOverlayState state = NextSectionSnapshotProjector.Project(resolved?.Snapshot);
        if (resolved is null || !state.HasContent)
        {
            HideAndClear();
            return;
        }

        IUpNextOverlayView activeView = EnsureView();
        activeView.Apply(state, resolved.Placement, resolved.Theme);
        if (!activeView.IsVisible)
            activeView.Show();
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        engine.Unregister(OverlayWidgetIds.UpNext);
        CloseView();
    }

    private IUpNextOverlayView EnsureView()
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
            UpNextOverlayState.Empty,
            engine.CurrentLayout.Widgets.FirstOrDefault(item =>
                item.WidgetId == OverlayWidgetIds.UpNext)
                ?? new OverlayWidgetPlacement(
                    OverlayWidgetIds.UpNext, false, 0, 0, 1, 1, 0),
            engine.CurrentTheme);
        if (view.IsVisible)
            view.Hide();
    }

    private void CloseView()
    {
        IUpNextOverlayView? closing = view;
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
