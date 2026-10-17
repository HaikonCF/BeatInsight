using BeatInsight.Integration.Game;
using BeatInsight.Models.AnalysisCache;
using BeatInsight.Models.Sections;
using BeatInsight.Overlay;
using BeatInsight.Overlay.DifficultyTimeline;
using BeatInsight.Tests.ViewModels;
using System.Runtime.ExceptionServices;
using System.Windows;

namespace BeatInsight.Tests.Overlay;

public sealed class DifficultyTimelineOverlayTests
{
    [Fact]
    public void ControllerRegistersGameplayOnlyTimelineWidget()
    {
        var engine = Engine();
        using var controller = Controller(engine, out _);

        Assert.True(engine.TryGetWidget(
            OverlayWidgetIds.DifficultyTimeline,
            out OverlayWidgetRegistration? widget));
        Assert.Equal("Difficulty timeline", widget!.Definition.DisplayName);
        Assert.Equal([OverlayGameState.Gameplay], widget.Definition.SupportedStates);
        Assert.True(widget.Definition.RequiresAnalysisSnapshot);
    }

    [Fact]
    public void GameplayWithValidModelCreatesVisibleView()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);

        engine.PublishSnapshot(Snapshot());

        FakeView view = Assert.Single(views.Created);
        Assert.True(view.IsVisible);
        Assert.True(view.State!.HasContent);
        Assert.Equal(OverlayWidgetIds.DifficultyTimeline, view.Placement!.WidgetId);
        Assert.Equal((24d, 24d, 732d, 180d),
            (view.Placement.X, view.Placement.Y, view.Placement.Width, view.Placement.Height));
    }

    [Fact]
    public void ValidCurrentSectionIndexIsProjectedForHighlight()
    {
        DifficultyTimelineOverlayState state =
            DifficultyTimelineSnapshotProjector.Project(Snapshot(currentSectionIndex: 7));

        Assert.True(state.HasContent);
        Assert.Equal(7, state.CurrentSectionIndex);
    }

    [Fact]
    public void NullCurrentSectionKeepsTimelineVisibleWithoutHighlight()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);

        engine.PublishSnapshot(Snapshot(currentSectionIndex: null));

        FakeView view = Assert.Single(views.Created);
        Assert.True(view.IsVisible);
        Assert.Null(view.State!.CurrentSectionIndex);
    }

    [Fact]
    public void MissingTimelineDoesNotCreateView()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);

        engine.PublishSnapshot(Snapshot() with { DifficultyTimeline = null });

        Assert.Empty(views.Created);
    }

    [Theory]
    [InlineData(OverlayGameState.SongSelect)]
    [InlineData(OverlayGameState.Results)]
    [InlineData(OverlayGameState.Unknown)]
    public void NonGameplayRoutesClearAndHideTimeline(OverlayGameState route)
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot());
        FakeView view = Assert.Single(views.Created);

        engine.PublishSnapshot(Snapshot() with
        {
            GameMode = route,
            LiveGameplay = null,
            DifficultyTimeline = null
        });

        Assert.False(view.IsVisible);
        Assert.Equal(DifficultyTimelineOverlayState.Empty, view.State);
    }

    [Fact]
    public void PauseKeepsGameplayRouteAndMarkerStable()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        OverlaySnapshot paused = Snapshot(markerRatio: 0.42);

        engine.PublishSnapshot(paused);
        engine.PublishSnapshot(paused);

        FakeView view = Assert.Single(views.Created);
        Assert.True(view.IsVisible);
        Assert.Equal(0.42, view.State!.MarkerRatio);
    }

    [Fact]
    public void SuccessiveTicksReuseModelAndOnlyUpdateMarker()
    {
        var viewModel = new DifficultyTimelineOverlayViewModel();
        OverlaySnapshot first = Snapshot(markerRatio: 0.2);
        DifficultyTimelineModel model = first.DifficultyTimeline!.Model;
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        viewModel.Apply(
            DifficultyTimelineSnapshotProjector.Project(first),
            OverlayThemeCatalog.Get(OverlayThemeIds.Default));
        changed.Clear();
        viewModel.Apply(
            DifficultyTimelineSnapshotProjector.Project(Snapshot(model, markerRatio: 0.3)),
            OverlayThemeCatalog.Get(OverlayThemeIds.Default));

        Assert.Same(model, viewModel.Model);
        Assert.Contains(nameof(DifficultyTimelineOverlayViewModel.MarkerRatio), changed);
        Assert.DoesNotContain(nameof(DifficultyTimelineOverlayViewModel.Model), changed);
    }

    [Fact]
    public void LayoutWithoutTimelineDoesNotCreateView()
    {
        var engine = new OverlayEngine(
            new OverlayLayout("NoTimeline", []),
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
        Assert.True(engine.TryGetWidget(
            OverlayWidgetIds.DifficultyTimeline,
            out OverlayWidgetRegistration? widget));

        widget!.IsEnabled = false;
        controller.Refresh();

        Assert.False(view.IsVisible);
        Assert.Equal(DifficultyTimelineOverlayState.Empty, view.State);
    }

    [Fact]
    public void ViewModelClearsModelAndHighlightForEmptyState()
    {
        var viewModel = new DifficultyTimelineOverlayViewModel();
        viewModel.Apply(
            DifficultyTimelineSnapshotProjector.Project(Snapshot()),
            OverlayThemeCatalog.Get(OverlayThemeIds.Default));

        viewModel.Apply(
            DifficultyTimelineOverlayState.Empty,
            OverlayThemeCatalog.Get(OverlayThemeIds.Default));

        Assert.Null(viewModel.Model);
        Assert.Equal(0, viewModel.MarkerRatio);
        Assert.Equal(-1, viewModel.CurrentSectionIndex);
    }

    [Fact]
    public void RenderControlUsesTemporalWidthsDifficultyHeightsAndCurrentHighlight() =>
        RunSta(() =>
    {
        OverlaySnapshot snapshot = Snapshot();
        DifficultyTimelineSegment first = snapshot.DifficultyTimeline!.Model.Segments[0];
        GameplaySectionV2Analysis secondSection = first.Section with
        {
            SectionIndex = 9,
            StartTime = 6_000,
            EndTime = 16_000,
            LocalDifficultyScore = 50
        };
        var model = new DifficultyTimelineModel(
            1_000,
            16_000,
            Array.AsReadOnly(new[]
            {
                new DifficultyTimelineSegment(first.Section, 0, 1d / 3, 0.75),
                new DifficultyTimelineSegment(secondSection, 1d / 3, 1, 0.5)
            }));
        var control = new DifficultyTimelineBarControl
        {
            Model = model,
            CurrentSectionIndex = 9
        };
        control.Measure(new Size(300, 40));
        control.Arrange(new Rect(0, 0, 300, 40));

        IReadOnlyList<Rect> rects = control.GetSegmentRectsForTesting();

        Assert.Equal(100, rects[0].Width, 10);
        Assert.Equal(30, rects[0].Height, 10);
        Assert.Equal(200, rects[1].Width, 10);
        Assert.Equal(20, rects[1].Height, 10);
        Assert.Equal(rects[1], control.GetCurrentHighlightRectForTesting());
    });

    [Fact]
    public void RenderControlHasNoHighlightDuringGap() =>
        RunSta(() =>
    {
        var control = new DifficultyTimelineBarControl
        {
            Model = Snapshot().DifficultyTimeline!.Model,
            CurrentSectionIndex = -1
        };
        control.Measure(new Size(300, 40));
        control.Arrange(new Rect(0, 0, 300, 40));

        Assert.Null(control.GetCurrentHighlightRectForTesting());
    });

    [Fact]
    public void DisposingControllerClosesViewAndUnregistersWidget()
    {
        var engine = Engine();
        DifficultyTimelineOverlayController controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot());
        FakeView view = Assert.Single(views.Created);

        controller.Dispose();

        Assert.True(view.WasClosed);
        Assert.False(engine.TryGetWidget(OverlayWidgetIds.DifficultyTimeline, out _));
    }

    private static OverlayEngine Engine()
    {
        var engine = new OverlayEngine(
            new OverlayLayout(
                "Legacy.DifficultyTimeline.Test",
                [new OverlayWidgetPlacement(
                    OverlayWidgetIds.DifficultyTimeline,
                    true,
                    24,
                    24,
                    732,
                    180,
                    0)]),
            OverlayThemeCatalog.Get(OverlayThemeIds.Default));
        engine.SetForegroundState(true);
        return engine;
    }

    private static DifficultyTimelineOverlayController Controller(
        OverlayEngine engine,
        out ViewFactory views)
    {
        views = new ViewFactory();
        return new DifficultyTimelineOverlayController(engine, views.Create);
    }

    private static OverlaySnapshot Snapshot(
        DifficultyTimelineModel? model = null,
        double markerRatio = 0.25,
        int? currentSectionIndex = 7)
    {
        BeatmapAnalysisSnapshot analysis = OverviewViewModelTests.BuildSnapshot();
        GameplaySectionV2Analysis source = analysis.SectionAnalysisV2.Sections[0];
        GameplaySectionV2Analysis section = source with
        {
            SectionIndex = 7,
            StartTime = 1_000,
            EndTime = 6_000,
            ObjectIndices = [0],
            LocalDifficultyScore = 75
        };
        model ??= new DifficultyTimelineModel(
            1_000,
            6_000,
            Array.AsReadOnly(new[]
            {
                new DifficultyTimelineSegment(section, 0, 1, 0.75)
            }));
        TimeSpan current = TimeSpan.FromMilliseconds(
            model.AxisStart + (model.AxisEnd - model.AxisStart) * markerRatio);
        var live = new LiveGameplaySnapshot(
            true,
            current,
            TimeSpan.FromMinutes(2),
            0.1,
            analysis,
            123,
            456,
            "42@generation",
            DateTimeOffset.UtcNow);
        var timeline = new DifficultyTimelineSnapshot(
            model,
            markerRatio,
            currentSectionIndex);

        return new(
            Game(),
            OverlayGameState.Gameplay,
            analysis,
            current,
            LiveGameplay: live,
            DifficultyTimeline: timeline);
    }

    private static GameStateSnapshot Game() =>
        new(
            "test",
            "osu!",
            42,
            "42@generation",
            "map.osu",
            "123",
            "456",
            DateTimeOffset.UtcNow,
            GameStateAvailability.Available,
            null);

    private sealed class ViewFactory
    {
        public List<FakeView> Created { get; } = [];

        public IDifficultyTimelineOverlayView Create()
        {
            var view = new FakeView();
            Created.Add(view);
            return view;
        }
    }

    private sealed class FakeView : IDifficultyTimelineOverlayView
    {
        public event EventHandler? Closed;

        public bool IsVisible { get; private set; }
        public bool WasClosed { get; private set; }
        public DifficultyTimelineOverlayState? State { get; private set; }
        public OverlayWidgetPlacement? Placement { get; private set; }

        public void Apply(
            DifficultyTimelineOverlayState state,
            OverlayWidgetPlacement placement,
            OverlayTheme theme)
        {
            State = state;
            Placement = placement;
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
