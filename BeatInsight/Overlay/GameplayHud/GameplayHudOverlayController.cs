namespace BeatInsight.Overlay.GameplayHud;

public interface IGameplayHudOverlayView
{
    event EventHandler? Closed;
    bool IsVisible { get; }
    void Apply(
        GameplayHudOverlayState state,
        OverlayWidgetPlacement placement,
        OverlayTheme theme);
    void Show();
    void Hide();
    void Close();
}

public sealed class GameplayHudOverlayController : IDisposable
{
    private readonly OverlayEngine engine;
    private readonly Func<IGameplayHudOverlayView> viewFactory;
    private GameplayHudDisplayOptions options;
    private readonly OverlayWidgetRegistration registration;
    private IGameplayHudOverlayView? view;
    private bool disposed;

    public GameplayHudOverlayController(
        OverlayEngine engine,
        Func<IGameplayHudOverlayView> viewFactory,
        GameplayHudDisplayOptions? options = null)
    {
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        this.viewFactory = viewFactory ?? throw new ArgumentNullException(nameof(viewFactory));
        this.options = options ?? GameplayHudDisplayOptions.Default;
        registration = new OverlayWidgetRegistration(
            new OverlayWidgetDefinition(
                OverlayWidgetIds.GameplayHud,
                "Gameplay HUD",
                new HashSet<OverlayGameState>
                {
                    OverlayGameState.Gameplay,
                    OverlayGameState.Results
                },
                RequiresAnalysisSnapshot: true),
            _ => Refresh(),
            snapshot => snapshot.HasAvailableGame &&
                GameplayHudSnapshotProjector.Project(snapshot, this.options).HasContent);
        engine.Register(registration);
    }

    public void ApplyDisplayOptions(GameplayHudDisplayOptions displayOptions)
    {
        ArgumentNullException.ThrowIfNull(displayOptions);
        if (options == displayOptions)
            return;

        options = displayOptions;
        Refresh();
    }

    public void Refresh()
    {
        if (disposed)
            return;

        ResolvedOverlayWidget? resolved = engine.GetVisibleWidgets()
            .FirstOrDefault(item =>
                item.Registration.Definition.Id == OverlayWidgetIds.GameplayHud);
        GameplayHudOverlayState state = engine.IsOverlayEditMode
            ? OverlayEditPreview.GameplayHud(options)
            : GameplayHudSnapshotProjector.Project(resolved?.Snapshot, options);
        if (resolved is null || !state.HasContent)
        {
            HideAndClear();
            return;
        }

        IGameplayHudOverlayView activeView = EnsureView();
        activeView.Apply(state, resolved.Placement, resolved.Theme);
        ApplyInteractionMode(activeView);
        if (!activeView.IsVisible)
            activeView.Show();
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        engine.Unregister(OverlayWidgetIds.GameplayHud);
        CloseView();
    }

    private IGameplayHudOverlayView EnsureView()
    {
        if (view is not null)
            return view;

        view = viewFactory();
        view.Closed += ViewClosed;
        if (view is IEditableOverlayView editable)
            editable.PlacementChanged += ViewPlacementChanged;
        return view;
    }

    private void HideAndClear()
    {
        if (view is null)
            return;

        view.Apply(
            GameplayHudOverlayState.Empty,
            engine.CurrentLayout.Widgets.FirstOrDefault(item =>
                item.WidgetId == OverlayWidgetIds.GameplayHud)
                ?? new OverlayWidgetPlacement(
                    OverlayWidgetIds.GameplayHud, false, 0, 0, 1, 1, 0),
            engine.CurrentTheme);
        if (view.IsVisible)
            view.Hide();
    }

    private void CloseView()
    {
        IGameplayHudOverlayView? closing = view;
        view = null;
        if (closing is null)
            return;

        closing.Closed -= ViewClosed;
        if (closing is IEditableOverlayView editable)
            editable.PlacementChanged -= ViewPlacementChanged;
        closing.Close();
    }

    private void ViewClosed(object? sender, EventArgs e)
    {
        if (view is null)
            return;

        view.Closed -= ViewClosed;
        if (view is IEditableOverlayView editable)
            editable.PlacementChanged -= ViewPlacementChanged;
        view = null;
    }

    private void ApplyInteractionMode(IGameplayHudOverlayView activeView)
    {
        if (activeView is IEditableOverlayView editable)
            editable.SetInteractionMode(engine.IsOverlayEditMode
                ? OverlayWindowInteractionMode.Editable
                : OverlayWindowInteractionMode.Passive);
    }

    private void ViewPlacementChanged(object? sender, OverlayPlacementChangedEventArgs e) =>
        engine.UpdateWorkingPlacement(e.WidgetId, e.X, e.Y, e.Width, e.Height, e.Resized);
}
