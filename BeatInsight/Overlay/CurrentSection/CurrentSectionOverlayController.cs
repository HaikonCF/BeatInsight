namespace BeatInsight.Overlay.CurrentSection;

public interface ICurrentSectionOverlayView
{
    event EventHandler? Closed;

    bool IsVisible { get; }

    void Apply(
        CurrentSectionOverlayState state,
        OverlayWidgetPlacement placement,
        OverlayTheme theme);

    void Show();

    void Hide();

    void Close();
}

public sealed class CurrentSectionOverlayController : IDisposable
{
    private readonly OverlayEngine engine;
    private readonly Func<ICurrentSectionOverlayView> viewFactory;
    private readonly OverlayWidgetRegistration registration;
    private ICurrentSectionOverlayView? view;
    private bool disposed;

    public CurrentSectionOverlayController(
        OverlayEngine engine,
        Func<ICurrentSectionOverlayView> viewFactory)
    {
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        this.viewFactory = viewFactory ?? throw new ArgumentNullException(nameof(viewFactory));
        registration = new OverlayWidgetRegistration(
            new OverlayWidgetDefinition(
                OverlayWidgetIds.CurrentSection,
                "Current section",
                new HashSet<OverlayGameState> { OverlayGameState.Gameplay },
                RequiresAnalysisSnapshot: true),
            _ => Refresh(),
            snapshot => snapshot.HasAvailableGame && snapshot.CurrentSection is not null);
        engine.Register(registration);
    }

    public void Refresh()
    {
        if (disposed)
            return;

        ResolvedOverlayWidget? resolved = engine.GetVisibleWidgets()
            .FirstOrDefault(item =>
                item.Registration.Definition.Id == OverlayWidgetIds.CurrentSection);
        CurrentSectionOverlayState state = CurrentSectionSnapshotProjector.Project(
            resolved?.Snapshot);
        if (resolved is null || !state.HasContent)
        {
            HideAndClear();
            return;
        }

        ICurrentSectionOverlayView activeView = EnsureView();
        activeView.Apply(state, resolved.Placement, resolved.Theme);
        if (!activeView.IsVisible)
            activeView.Show();
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        engine.Unregister(OverlayWidgetIds.CurrentSection);
        CloseView();
    }

    private ICurrentSectionOverlayView EnsureView()
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
            CurrentSectionOverlayState.Empty,
            engine.CurrentLayout.Widgets.FirstOrDefault(item =>
                item.WidgetId == OverlayWidgetIds.CurrentSection)
                ?? new OverlayWidgetPlacement(
                    OverlayWidgetIds.CurrentSection,
                    false,
                    0, 0, 1, 1, 0),
            engine.CurrentTheme);
        if (view.IsVisible)
            view.Hide();
    }

    private void CloseView()
    {
        ICurrentSectionOverlayView? closing = view;
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
