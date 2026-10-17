using BeatInsight.Integration.Game;
using BeatInsight.Models.AnalysisCache;
using BeatInsight.Models.Fusion;
using BeatInsight.Models.Sections;
using BeatInsight.Overlay;
using BeatInsight.Overlay.GameplayHud;
using BeatInsight.Tests.ViewModels;
using System.Runtime.ExceptionServices;
using System.Windows;

namespace BeatInsight.Tests.Overlay;

public sealed class GameplayHudOverlayTests
{
    [Fact]
    public void ControllerRegistersOneGameplayAndResultsHudWidget()
    {
        var engine = Engine();
        using var controller = Controller(engine, out _);

        Assert.True(engine.TryGetWidget(OverlayWidgetIds.GameplayHud, out OverlayWidgetRegistration? widget));
        Assert.Equal("Gameplay HUD", widget!.Definition.DisplayName);
        Assert.Equal(
            new HashSet<OverlayGameState> { OverlayGameState.Gameplay, OverlayGameState.Results },
            widget.Definition.SupportedStates);
        Assert.True(widget.Definition.RequiresAnalysisSnapshot);
        Assert.Single(engine.Widgets);
    }

    [Fact]
    public void DefaultOptionsShowAllFourValidBlocksInOneView()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);

        engine.PublishSnapshot(Snapshot());

        FakeView view = Assert.Single(views.Created);
        Assert.True(view.IsVisible);
        Assert.True(view.State!.IsCurrentSectionVisible);
        Assert.True(view.State.IsUpNextVisible);
        Assert.True(view.State.IsDifficultyTimelineVisible);
        Assert.True(view.State.IsLivePatternsVisible);
        Assert.Equal(OverlayWidgetIds.GameplayHud, view.Placement!.WidgetId);
    }

    [Fact]
    public void TimelineOptionCollapsesOnlyTimeline()
    {
        GameplayHudOverlayState state = Project(new(ShowDifficultyTimeline: false));

        Assert.True(state.IsCurrentSectionVisible);
        Assert.True(state.IsUpNextVisible);
        Assert.False(state.IsDifficultyTimelineVisible);
        Assert.True(state.IsLivePatternsVisible);
    }

    [Fact]
    public void PatternsOptionCollapsesOnlyPatterns()
    {
        GameplayHudOverlayState state = Project(new(ShowLivePatterns: false));

        Assert.True(state.IsCurrentSectionVisible);
        Assert.True(state.IsUpNextVisible);
        Assert.True(state.IsDifficultyTimelineVisible);
        Assert.False(state.IsLivePatternsVisible);
    }

    [Fact]
    public void ValidPerformanceIsVisibleAndProjected()
    {
        OverlaySnapshot snapshot = Snapshot() with
        {
            Performance = new LivePerformanceSnapshot(182.4, 214.2, 168.1, 246.4, 100, DateTimeOffset.UtcNow)
        };

        GameplayHudOverlayState state = GameplayHudSnapshotProjector.Project(snapshot);

        Assert.True(state.IsPerformanceVisible);
        Assert.Equal("182pp", state.Performance.Current);
        Assert.Equal("214pp", state.Performance.Fc);
        Assert.Equal("168pp", state.Performance.Accuracy95Fc);
        Assert.Equal("246pp", state.Performance.Ss);
    }

    [Fact]
    public void ResultsProjectsOnlyFinalPerformanceInTheSameHudPlacement()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        OverlaySnapshot gameplay = Snapshot() with
        {
            Performance = new LivePerformanceSnapshot(182, 214, 168, 246, 100, DateTimeOffset.UtcNow)
        };
        engine.PublishSnapshot(gameplay);
        FakeView view = Assert.Single(views.Created);
        OverlayWidgetPlacement gameplayPlacement = view.Placement!;

        engine.PublishSnapshot(gameplay with { GameMode = OverlayGameState.Results });

        Assert.True(view.IsVisible);
        Assert.Equal(gameplayPlacement, view.Placement);
        Assert.True(view.State!.IsPerformanceVisible);
        Assert.False(view.State.IsCurrentSectionVisible);
        Assert.False(view.State.IsUpNextVisible);
        Assert.False(view.State.IsDifficultyTimelineVisible);
        Assert.False(view.State.IsLivePatternsVisible);
        Assert.False(view.State.IsLiveStatsVisible);
        Assert.Equal("182pp", view.State.Performance.Current);
    }

    [Fact]
    public void PerformanceOptionFalseCollapsesOnlyPerformance()
    {
        OverlaySnapshot snapshot = Snapshot() with
        {
            Performance = new LivePerformanceSnapshot(1, 2, 3, 4, 5, DateTimeOffset.UtcNow)
        };

        GameplayHudOverlayState state = GameplayHudSnapshotProjector.Project(
            snapshot,
            new GameplayHudDisplayOptions(ShowPerformance: false));

        Assert.False(state.IsPerformanceVisible);
        Assert.True(state.IsCurrentSectionVisible);
    }

    [Fact]
    public void MissingPerformanceNeverReusesPreviousValue()
    {
        var viewModel = new GameplayHudViewModel();
        OverlaySnapshot valid = Snapshot() with
        {
            Performance = new LivePerformanceSnapshot(182, 214, 168, 246, 100, DateTimeOffset.UtcNow)
        };
        viewModel.Apply(GameplayHudSnapshotProjector.Project(valid), OverlayThemeCatalog.Get(OverlayThemeIds.Default));

        viewModel.Apply(GameplayHudSnapshotProjector.Project(Snapshot()), OverlayThemeCatalog.Get(OverlayThemeIds.Default));

        Assert.False(viewModel.IsPerformanceVisible);
        Assert.Equal("—", viewModel.Performance.Current);
    }

    [Fact]
    public void CurrentSectionOnlyOptionProducesCompactSingleBlockState()
    {
        GameplayHudOverlayState state = Project(new(
            ShowCurrentSection: true,
            ShowUpNext: false,
            ShowDifficultyTimeline: false,
            ShowLivePatterns: false));

        Assert.True(state.HasContent);
        Assert.True(state.IsCurrentSectionVisible);
        Assert.False(state.IsUpNextVisible);
        Assert.False(state.IsDifficultyTimelineVisible);
        Assert.False(state.IsLivePatternsVisible);
    }

    [Fact]
    public void AllOptionsFalseKeepHudGloballyHidden()
    {
        var engine = Engine();
        using var controller = Controller(
            engine,
            out ViewFactory views,
            new(false, false, false, false));

        engine.PublishSnapshot(Snapshot());

        Assert.Empty(views.Created);
        Assert.Empty(engine.GetVisibleWidgets());
    }

    [Fact]
    public void DisplayOptionsApplyImmediatelyAndReuseExistingView()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot());
        FakeView view = Assert.Single(views.Created);

        controller.ApplyDisplayOptions(new(true, true, false, false));

        Assert.True(view.IsVisible);
        Assert.False(view.State!.IsDifficultyTimelineVisible);
        Assert.False(view.State.IsLivePatternsVisible);

        controller.ApplyDisplayOptions(new(false, false, false, false));
        Assert.False(view.IsVisible);

        controller.ApplyDisplayOptions(new(true, false, true, false));
        Assert.True(view.IsVisible);
        Assert.True(view.State!.IsCurrentSectionVisible);
        Assert.False(view.State.IsUpNextVisible);
        Assert.True(view.State.IsDifficultyTimelineVisible);
        Assert.Single(views.Created);
    }

    [Fact]
    public void LayoutChangeAppliesImmediatelyToExistingView()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot());
        FakeView view = Assert.Single(views.Created);

        engine.ApplyLayout(OverlayLayoutCatalog.Get(OverlayLayoutIds.Analysis));

        Assert.Equal(396, view.Placement!.X);
        Assert.Single(views.Created);
    }

    [Fact]
    public void MissingCurrentAndNextStillAllowsIndependentTimelineAndPatterns()
    {
        OverlaySnapshot snapshot = Snapshot() with
        {
            CurrentSection = null,
            NextSection = null
        };

        GameplayHudOverlayState state = GameplayHudSnapshotProjector.Project(snapshot);

        Assert.False(state.IsCurrentSectionVisible);
        Assert.False(state.IsUpNextVisible);
        Assert.True(state.IsDifficultyTimelineVisible);
        Assert.True(state.IsLivePatternsVisible);
    }

    [Fact]
    public void NoDominantPatternKeepsPatternsBlockVisible()
    {
        OverlaySnapshot snapshot = Snapshot(patterns: []);

        GameplayHudOverlayState state = GameplayHudSnapshotProjector.Project(snapshot);

        Assert.True(state.IsLivePatternsVisible);
        Assert.False(state.LivePatterns.HasDominantPatterns);
        var viewModel = new GameplayHudViewModel();
        viewModel.Apply(state, OverlayThemeCatalog.Get(OverlayThemeIds.Default));
        Assert.Equal("No dominant pattern", viewModel.LivePatterns.NeutralText);
    }

    [Fact]
    public void AllInvalidSnapshotsHideHudAndClearExistingView()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot());
        FakeView view = Assert.Single(views.Created);

        engine.PublishSnapshot(Snapshot() with
        {
            CurrentSection = null,
            NextSection = null,
            DifficultyTimeline = null,
            LivePatterns = null
        });

        Assert.False(view.IsVisible);
        Assert.Equal(GameplayHudOverlayState.Empty, view.State);
        Assert.Empty(engine.GetVisibleWidgets());
        Assert.False(view.WasClosed);

        engine.PublishSnapshot(Snapshot());

        Assert.True(view.IsVisible);
        Assert.Single(views.Created);
        Assert.False(view.WasClosed);
    }

    [Theory]
    [InlineData(OverlayGameState.SongSelect)]
    [InlineData(OverlayGameState.Unknown)]
    public void NonGameplayRoutesHideWholeHud(OverlayGameState route)
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot());
        FakeView view = Assert.Single(views.Created);

        engine.PublishSnapshot(Snapshot() with { GameMode = route });

        Assert.False(view.IsVisible);
        Assert.Equal(GameplayHudOverlayState.Empty, view.State);
    }

    [Fact]
    public void PauseReusesOneVisibleViewAndStableBlockData()
    {
        OverlaySnapshot paused = Snapshot();
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);

        engine.PublishSnapshot(paused);
        engine.PublishSnapshot(paused);

        FakeView view = Assert.Single(views.Created);
        Assert.True(view.IsVisible);
        Assert.Equal(2, view.States.Count);
        Assert.Equal(view.States[0], view.States[1]);
    }

    [Fact]
    public void PauseFreezesPerformanceAndReusesHudWindow()
    {
        OverlaySnapshot paused = Snapshot() with
        {
            Performance = new LivePerformanceSnapshot(182, 214, 168, 246, 100, DateTimeOffset.UtcNow)
        };
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);

        engine.PublishSnapshot(paused);
        engine.PublishSnapshot(paused);

        FakeView view = Assert.Single(views.Created);
        Assert.Equal("182pp", view.States[0].Performance.Current);
        Assert.Equal(view.States[0].Performance, view.States[1].Performance);
    }

    [Fact]
    public void RetryOrMapChangeReplacesPerformanceWithoutStaleValue()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot() with
        {
            Performance = new LivePerformanceSnapshot(182, 214, 168, 246, 100, DateTimeOffset.UtcNow)
        });

        engine.PublishSnapshot(Snapshot() with
        {
            Performance = new LivePerformanceSnapshot(4, 220, 170, 255, 1, DateTimeOffset.UtcNow)
        });

        FakeView view = Assert.Single(views.Created);
        Assert.Equal("4pp", view.State!.Performance.Current);
        Assert.Equal("255pp", view.State.Performance.Ss);
    }

    [Fact]
    public void FullscreenExclusiveHidesAndBorderlessRestoresSameHudWindow()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot());
        FakeView view = Assert.Single(views.Created);

        engine.SetDisplayCompatibility(OverlayDisplayCompatibility.FullscreenExclusive);
        Assert.False(view.IsVisible);

        engine.SetDisplayCompatibility(OverlayDisplayCompatibility.Supported(OverlayDisplayMode.Borderless));
        Assert.True(view.IsVisible);
        Assert.Single(views.Created);
    }

    [Fact]
    public void ViewModelAggregatesExistingBlockViewModelsWithoutReplacingThem()
    {
        var viewModel = new GameplayHudViewModel();
        object current = viewModel.CurrentSection;
        object next = viewModel.UpNext;
        object timeline = viewModel.DifficultyTimeline;
        object patterns = viewModel.LivePatterns;

        viewModel.Apply(Project(GameplayHudDisplayOptions.Default), OverlayThemeCatalog.Get(OverlayThemeIds.Default));

        Assert.Same(current, viewModel.CurrentSection);
        Assert.Same(next, viewModel.UpNext);
        Assert.Same(timeline, viewModel.DifficultyTimeline);
        Assert.Same(patterns, viewModel.LivePatterns);
        Assert.Equal("Jump", viewModel.CurrentSection.DominantStructure);
        Assert.Equal("Stream", viewModel.UpNext.DominantStructure);
        Assert.NotNull(viewModel.DifficultyTimeline.Model);
        Assert.NotEmpty(viewModel.LivePatterns.Patterns);
    }

    [Fact]
    public void WindowCollapsesUpNextContainerWhenNextSectionEnds() => RunSta(() =>
    {
        var window = new GameplayHudWindow();
        OverlayWidgetPlacement placement = Assert.Single(
            OverlayLayoutCatalog.Get(OverlayLayoutIds.Minimal).Widgets,
            item => item.WidgetId == OverlayWidgetIds.GameplayHud);
        GameplayHudOverlayState state = GameplayHudSnapshotProjector.Project(
            Snapshot() with { NextSection = null });

        window.Apply(state, placement, OverlayThemeCatalog.Get(OverlayThemeIds.Default));
        window.Measure(new Size(placement.Width, double.PositiveInfinity));
        window.Arrange(new Rect(0, 0, placement.Width, window.DesiredSize.Height));

        var visibility = window.GetBlockVisibilitiesForTesting();
        Assert.Equal(Visibility.Visible, visibility.Current);
        Assert.Equal(Visibility.Collapsed, visibility.Next);
        Assert.Equal(Visibility.Visible, visibility.Timeline);
        Assert.Equal(Visibility.Visible, visibility.Patterns);
        window.Close();
    });

    [Fact]
    public void DisposingControllerClosesSingleViewAndUnregistersHud()
    {
        var engine = Engine();
        GameplayHudOverlayController controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot());
        FakeView view = Assert.Single(views.Created);

        controller.Dispose();

        Assert.True(view.WasClosed);
        Assert.False(engine.TryGetWidget(OverlayWidgetIds.GameplayHud, out _));
    }

    private static GameplayHudOverlayState Project(GameplayHudDisplayOptions options) =>
        GameplayHudSnapshotProjector.Project(Snapshot(), options);

    private static OverlayEngine Engine()
    {
        var engine = new OverlayEngine(
            OverlayLayoutCatalog.Get(OverlayLayoutIds.Minimal),
            OverlayThemeCatalog.Get(OverlayThemeIds.Default));
        engine.SetForegroundState(true);
        return engine;
    }

    private static GameplayHudOverlayController Controller(
        OverlayEngine engine,
        out ViewFactory views,
        GameplayHudDisplayOptions? options = null)
    {
        views = new ViewFactory();
        return new GameplayHudOverlayController(engine, views.Create, options);
    }

    private static OverlaySnapshot Snapshot(IReadOnlyList<LivePatternItem>? patterns = null)
    {
        BeatmapAnalysisSnapshot source = OverviewViewModelTests.BuildSnapshot();
        GameplaySectionV2Analysis firstSource = source.SectionAnalysisV2.Sections[0];
        GameplaySectionV2Analysis currentSection = firstSource with
        {
            SectionIndex = 1,
            StartTime = 1_000,
            EndTime = 6_000,
            DominantStructure = GameplayStructureKind.Jump,
            SecondaryStructure = null,
            ObjectIndices = [0],
            LocalDifficultyScore = 70,
            Summary = "Jump section"
        };
        GameplaySectionV2Analysis nextSection = firstSource with
        {
            SectionIndex = 2,
            StartTime = 6_000,
            EndTime = 11_000,
            DominantStructure = GameplayStructureKind.Stream,
            SecondaryStructure = null,
            ObjectIndices = [0],
            LocalDifficultyScore = 80,
            Summary = "Stream section"
        };
        BeatmapAnalysisSnapshot analysis = source with
        {
            Metadata = source.Metadata with
            {
                BeatmapId = 123,
                BeatmapSetId = 456,
                LengthTicks = TimeSpan.FromSeconds(10).Ticks
            },
            SectionAnalysisV2 = source.SectionAnalysisV2 with
            {
                Sections = [currentSection, nextSection]
            }
        };
        var current = new CurrentSectionSnapshot(
            currentSection,
            TimeSpan.FromMilliseconds(1_000),
            TimeSpan.FromMilliseconds(6_000),
            TimeSpan.FromMilliseconds(5_000),
            .5,
            TimeSpan.FromMilliseconds(2_500));
        var next = new NextSectionSnapshot(
            nextSection,
            TimeSpan.FromMilliseconds(6_000),
            TimeSpan.FromMilliseconds(11_000),
            TimeSpan.FromMilliseconds(5_000),
            TimeSpan.FromMilliseconds(2_500));
        var model = new DifficultyTimelineModel(
            1_000,
            11_000,
            Array.AsReadOnly(new[]
            {
                new DifficultyTimelineSegment(currentSection, 0, .5, .7),
                new DifficultyTimelineSegment(nextSection, .5, 1, .8)
            }));
        var timeline = new DifficultyTimelineSnapshot(model, .25, currentSection.SectionIndex);
        patterns ??=
        [
            new LivePatternItem("OneTwo", "1-2 Jump", FusionPatternFamily.Jump, .6, 80)
        ];
        var livePatterns = new LivePatternsSnapshot(currentSection, patterns);

        return new(
            Game(),
            OverlayGameState.Gameplay,
            analysis,
            TimeSpan.FromMilliseconds(3_500),
            CurrentSection: current,
            NextSection: next,
            DifficultyTimeline: timeline,
            LivePatterns: livePatterns);
    }

    private static GameStateSnapshot Game() => new(
        "test", "osu!", 42, "42@generation", "map.osu", "123", "456",
        DateTimeOffset.UtcNow, GameStateAvailability.Available, null);

    private sealed class ViewFactory
    {
        public List<FakeView> Created { get; } = [];

        public IGameplayHudOverlayView Create()
        {
            var view = new FakeView();
            Created.Add(view);
            return view;
        }
    }

    private sealed class FakeView : IGameplayHudOverlayView
    {
        public event EventHandler? Closed;
        public bool IsVisible { get; private set; }
        public bool WasClosed { get; private set; }
        public GameplayHudOverlayState? State { get; private set; }
        public OverlayWidgetPlacement? Placement { get; private set; }
        public List<GameplayHudOverlayState> States { get; } = [];

        public void Apply(
            GameplayHudOverlayState state,
            OverlayWidgetPlacement placement,
            OverlayTheme theme)
        {
            State = state;
            Placement = placement;
            States.Add(state);
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

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)), "WPF test timed out");
        if (error is not null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }
}
