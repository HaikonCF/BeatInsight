using BeatInsight.Integration.Game;
using BeatInsight.Models.AnalysisCache;
using BeatInsight.Models.Fusion;
using BeatInsight.Models.Sections;
using BeatInsight.Overlay;
using BeatInsight.Overlay.LivePatterns;
using BeatInsight.Tests.ViewModels;

namespace BeatInsight.Tests.Overlay;

public sealed class LivePatternsOverlayTests
{
    [Fact]
    public void ControllerRegistersGameplayOnlyPatternsWidget()
    {
        var engine = Engine();
        using var controller = Controller(engine, out _);

        Assert.True(engine.TryGetWidget(OverlayWidgetIds.Patterns, out OverlayWidgetRegistration? widget));
        Assert.Equal("Live patterns", widget!.Definition.DisplayName);
        Assert.Equal([OverlayGameState.Gameplay], widget.Definition.SupportedStates);
        Assert.True(widget.Definition.RequiresAnalysisSnapshot);
    }

    [Fact]
    public void ExposedLayoutsUseUnifiedHudWithoutLegacyPatternSlot()
    {
        foreach (string presetId in OverlayLayoutCatalog.ExposedIds)
        {
            OverlayLayout layout = OverlayLayoutCatalog.Get(presetId);
            Assert.Contains(layout.Widgets, item => item.WidgetId == OverlayWidgetIds.GameplayHud);
            Assert.DoesNotContain(
                layout.Widgets,
                item => item.WidgetId is OverlayWidgetIds.Patterns or
                    OverlayWidgetIds.CurrentSection or
                    OverlayWidgetIds.UpNext or
                    OverlayWidgetIds.DifficultyTimeline);
        }
    }

    [Fact]
    public void GameplayPatternsCreateVisibleView()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);

        engine.PublishSnapshot(Snapshot());

        FakeView view = Assert.Single(views.Created);
        Assert.True(view.IsVisible);
        Assert.True(view.State!.HasDominantPatterns);
        Assert.Equal(OverlayWidgetIds.Patterns, view.Placement!.WidgetId);
    }

    [Fact]
    public void ValidSectionWithoutDominantPatternStaysVisibleWithNeutralState()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);

        engine.PublishSnapshot(Snapshot(patterns: []));

        FakeView view = Assert.Single(views.Created);
        Assert.True(view.IsVisible);
        Assert.True(view.State!.HasContent);
        Assert.False(view.State.HasDominantPatterns);

        var viewModel = new LivePatternsOverlayViewModel();
        viewModel.Apply(view.State, OverlayThemeCatalog.Get(OverlayThemeIds.Default));
        Assert.Equal("No dominant pattern", viewModel.NeutralText);
    }

    [Theory]
    [InlineData(OverlayGameState.SongSelect)]
    [InlineData(OverlayGameState.Results)]
    [InlineData(OverlayGameState.Unknown)]
    public void NonGameplayRoutesClearAndHidePatterns(OverlayGameState route)
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot());
        FakeView view = Assert.Single(views.Created);

        engine.PublishSnapshot(Snapshot() with
        {
            GameMode = route,
            CurrentSection = null,
            LivePatterns = null
        });

        Assert.False(view.IsVisible);
        Assert.Equal(LivePatternsOverlayState.Empty, view.State);
    }

    [Fact]
    public void NullCurrentSectionProjectionDoesNotCreateView()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);

        engine.PublishSnapshot(Snapshot() with
        {
            CurrentSection = null,
            LivePatterns = null
        });

        Assert.Empty(views.Created);
    }

    [Fact]
    public void PauseKeepsGameplayContentAndViewModelRowsStable()
    {
        OverlaySnapshot paused = Snapshot();
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(paused);
        engine.PublishSnapshot(paused);

        FakeView view = Assert.Single(views.Created);
        Assert.Equal(2, view.AppliedSnapshots.Count);
        Assert.Same(view.AppliedSnapshots[0], view.AppliedSnapshots[1]);

        var viewModel = new LivePatternsOverlayViewModel();
        LivePatternsOverlayState state = LivePatternsSnapshotProjector.Project(paused);
        viewModel.Apply(state, OverlayThemeCatalog.Get(OverlayThemeIds.Default));
        IReadOnlyList<LivePatternRowViewModel> rows = viewModel.Patterns;
        viewModel.Apply(state, OverlayThemeCatalog.Get(OverlayThemeIds.Default));
        Assert.Same(rows, viewModel.Patterns);
    }

    [Fact]
    public void ViewModelDisplaysCoverageRatherThanStrength()
    {
        var viewModel = new LivePatternsOverlayViewModel();

        viewModel.Apply(
            LivePatternsSnapshotProjector.Project(Snapshot(
                patterns: [new("OneTwo", "1-2 Jump", FusionPatternFamily.Jump, .425, 91)])),
            OverlayThemeCatalog.Get(OverlayThemeIds.Default));

        LivePatternRowViewModel row = Assert.Single(viewModel.Patterns);
        Assert.Equal("1-2 Jump", row.DisplayName);
        Assert.Equal("42.5%", row.CoverageText);
        Assert.DoesNotContain("91", row.CoverageText);
    }

    [Fact]
    public void LayoutWithoutPatternsDoesNotCreateView()
    {
        var engine = new OverlayEngine(
            new OverlayLayout("NoPatterns", []),
            OverlayThemeCatalog.Get(OverlayThemeIds.Default));
        engine.SetForegroundState(true);
        using var controller = Controller(engine, out ViewFactory views);

        engine.PublishSnapshot(Snapshot());

        Assert.Empty(views.Created);
    }

    [Fact]
    public void DisabledWidgetClearsAndHidesView()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot());
        FakeView view = Assert.Single(views.Created);
        Assert.True(engine.TryGetWidget(OverlayWidgetIds.Patterns, out OverlayWidgetRegistration? widget));

        widget!.IsEnabled = false;
        controller.Refresh();

        Assert.False(view.IsVisible);
        Assert.Equal(LivePatternsOverlayState.Empty, view.State);
    }

    [Fact]
    public void DisposingControllerClosesViewAndUnregistersWidget()
    {
        var engine = Engine();
        LivePatternsOverlayController controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot());
        FakeView view = Assert.Single(views.Created);

        controller.Dispose();

        Assert.True(view.WasClosed);
        Assert.False(engine.TryGetWidget(OverlayWidgetIds.Patterns, out _));
    }

    private static OverlayEngine Engine()
    {
        var engine = new OverlayEngine(
            new OverlayLayout(
                "Legacy.LivePatterns.Test",
                [new OverlayWidgetPlacement(
                    OverlayWidgetIds.Patterns,
                    true,
                    396,
                    24,
                    360,
                    352,
                    2)]),
            OverlayThemeCatalog.Get(OverlayThemeIds.Default));
        engine.SetForegroundState(true);
        return engine;
    }

    private static LivePatternsOverlayController Controller(
        OverlayEngine engine,
        out ViewFactory views)
    {
        views = new ViewFactory();
        return new LivePatternsOverlayController(engine, views.Create);
    }

    private static OverlaySnapshot Snapshot(IReadOnlyList<LivePatternItem>? patterns = null)
    {
        BeatmapAnalysisSnapshot analysis = OverviewViewModelTests.BuildSnapshot();
        GameplaySectionV2Analysis section = analysis.SectionAnalysisV2.Sections[0];
        var current = new CurrentSectionSnapshot(
            section,
            TimeSpan.FromMilliseconds(section.StartTime),
            TimeSpan.FromMilliseconds(section.EndTime),
            TimeSpan.FromMilliseconds(section.Duration),
            .5,
            TimeSpan.FromMilliseconds(section.Duration / 2));
        patterns ??=
        [
            new LivePatternItem("OneTwo", "1-2 Jump", FusionPatternFamily.Jump, .42, 78)
        ];
        var livePatterns = new LivePatternsSnapshot(section, patterns);

        return new(
            Game(),
            OverlayGameState.Gameplay,
            analysis,
            CurrentSection: current,
            LivePatterns: livePatterns);
    }

    private static GameStateSnapshot Game() => new(
        "test", "osu!", 42, "42@generation", "map.osu", "123", "456",
        DateTimeOffset.UtcNow, GameStateAvailability.Available, null);

    private sealed class ViewFactory
    {
        public List<FakeView> Created { get; } = [];

        public ILivePatternsOverlayView Create()
        {
            var view = new FakeView();
            Created.Add(view);
            return view;
        }
    }

    private sealed class FakeView : ILivePatternsOverlayView
    {
        public event EventHandler? Closed;
        public bool IsVisible { get; private set; }
        public bool WasClosed { get; private set; }
        public LivePatternsOverlayState? State { get; private set; }
        public OverlayWidgetPlacement? Placement { get; private set; }
        public List<LivePatternsSnapshot?> AppliedSnapshots { get; } = [];

        public void Apply(
            LivePatternsOverlayState state,
            OverlayWidgetPlacement placement,
            OverlayTheme theme)
        {
            State = state;
            Placement = placement;
            AppliedSnapshots.Add(state.Snapshot);
        }

        public void Show() => IsVisible = true;
        public void Hide() => IsVisible = false;
        public void Close()
        {
            WasClosed = true;
            IsVisible = false;
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}
