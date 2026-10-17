using BeatInsight.Integration.Game;
using BeatInsight.Models.AnalysisCache;
using BeatInsight.Models.Fusion;
using BeatInsight.Models.Sections;
using BeatInsight.Overlay;
using BeatInsight.Overlay.UpNext;
using BeatInsight.Tests.ViewModels;

namespace BeatInsight.Tests.Overlay;

public sealed class UpNextOverlayTests
{
    [Fact]
    public void ControllerRegistersGameplayOnlyUpNextWidget()
    {
        var engine = Engine();
        using var controller = Controller(engine, out _);

        Assert.True(engine.TryGetWidget(
            OverlayWidgetIds.UpNext,
            out OverlayWidgetRegistration? widget));
        Assert.Equal("Up next", widget!.Definition.DisplayName);
        Assert.Equal([OverlayGameState.Gameplay], widget.Definition.SupportedStates);
        Assert.True(widget.Definition.RequiresAnalysisSnapshot);
    }

    [Fact]
    public void ValidSnapshotProjectsAnalyzerOwnedNextSectionData()
    {
        OverlaySnapshot snapshot = Snapshot(
            dominant: GameplayStructureKind.Tech,
            secondary: GameplayStructureKind.Stream,
            untilStartSeconds: 7.25,
            summary: "  Technical transitions  ");

        UpNextOverlayState state = NextSectionSnapshotProjector.Project(snapshot);

        Assert.True(state.HasContent);
        Assert.Equal("Tech", state.DominantStructure);
        Assert.Equal("Stream", state.SecondaryStructure);
        Assert.Equal(TimeSpan.FromSeconds(7.25), state.TimeUntilStart);
        Assert.Equal("Technical transitions", state.Summary);
    }

    [Fact]
    public void PrivateLegacyLayoutStillSupportsUpNextControllerTests()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);

        engine.PublishSnapshot(Snapshot());

        FakeView view = Assert.Single(views.Created);
        Assert.True(view.IsVisible);
        Assert.True(view.State!.HasContent);
        Assert.Equal(OverlayWidgetIds.UpNext, view.Placement!.WidgetId);
        Assert.Equal((390d, 216d, 366d, 150d),
            (view.Placement.X, view.Placement.Y, view.Placement.Width, view.Placement.Height));
    }

    [Fact]
    public void MissingNextSectionDoesNotCreateView()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);

        engine.PublishSnapshot(Snapshot() with { NextSection = null });

        Assert.Empty(views.Created);
        Assert.Empty(engine.GetVisibleWidgets());
    }

    [Theory]
    [InlineData(OverlayGameState.SongSelect)]
    [InlineData(OverlayGameState.Results)]
    [InlineData(OverlayGameState.Unknown)]
    public void NonGameplayRoutesClearAndHideVisibleUpNext(OverlayGameState route)
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot());
        FakeView view = Assert.Single(views.Created);

        engine.PublishSnapshot(Snapshot() with
        {
            GameMode = route,
            LiveGameplay = null,
            NextSection = null
        });

        Assert.False(view.IsVisible);
        Assert.Equal(UpNextOverlayState.Empty, view.State);
        Assert.Empty(engine.GetVisibleWidgets());
    }

    [Fact]
    public void PauseKeepsGameplayRouteAndFrozenCountdownVisible()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        OverlaySnapshot paused = Snapshot(untilStartSeconds: 8.4);

        engine.PublishSnapshot(paused);
        engine.PublishSnapshot(paused);

        FakeView view = Assert.Single(views.Created);
        Assert.True(view.IsVisible);
        Assert.Equal(TimeSpan.FromSeconds(8.4), view.State!.TimeUntilStart);
    }

    [Fact]
    public void RouteSequenceOnlyShowsUpNextDuringGameplayIncludingPause()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        OverlaySnapshot gameplay = Snapshot();

        engine.PublishSnapshot(gameplay with
        {
            GameMode = OverlayGameState.SongSelect,
            LiveGameplay = null,
            NextSection = null
        });
        Assert.Empty(views.Created);

        engine.PublishSnapshot(gameplay);
        FakeView view = Assert.Single(views.Created);
        Assert.True(view.IsVisible);

        engine.PublishSnapshot(gameplay); // Pause remains on the Gameplay route.
        Assert.True(view.IsVisible);

        engine.PublishSnapshot(gameplay);
        Assert.True(view.IsVisible);

        engine.PublishSnapshot(gameplay with
        {
            GameMode = OverlayGameState.Results,
            LiveGameplay = null,
            NextSection = null
        });
        Assert.False(view.IsVisible);

        engine.PublishSnapshot(gameplay with
        {
            GameMode = OverlayGameState.SongSelect,
            LiveGameplay = null,
            NextSection = null
        });
        Assert.False(view.IsVisible);
    }

    [Fact]
    public void SectionChangeReplacesAllDisplayedData()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot(
            dominant: GameplayStructureKind.Jump,
            untilStartSeconds: 8));

        engine.PublishSnapshot(Snapshot(
            dominant: GameplayStructureKind.Stream,
            secondary: GameplayStructureKind.Tech,
            untilStartSeconds: 2));

        FakeView view = Assert.Single(views.Created);
        Assert.Equal("Stream", view.State!.DominantStructure);
        Assert.Equal("Tech", view.State.SecondaryStructure);
        Assert.Equal(TimeSpan.FromSeconds(2), view.State.TimeUntilStart);
        Assert.DoesNotContain("Jump", view.RenderedDominantStructures[^1]);
    }

    [Fact]
    public void LayoutWithoutUpNextDoesNotCreateView()
    {
        var layout = new OverlayLayout("NoUpNext", []);
        var engine = new OverlayEngine(
            layout,
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
            OverlayWidgetIds.UpNext,
            out OverlayWidgetRegistration? widget));

        widget!.IsEnabled = false;
        controller.Refresh();

        Assert.False(view.IsVisible);
        Assert.Equal(UpNextOverlayState.Empty, view.State);
    }

    [Theory]
    [InlineData(12.01, "Starts in 13s")]
    [InlineData(10.0, "Starts in 10s")]
    [InlineData(9.94, "Starts in 9.9s")]
    [InlineData(0.04, "Starts in 0.0s")]
    public void ViewModelFormatsCountdown(double seconds, string expected)
    {
        var viewModel = new UpNextOverlayViewModel();

        viewModel.Apply(
            new UpNextOverlayState(true, "Jump", null, TimeSpan.FromSeconds(seconds), null),
            OverlayThemeCatalog.Get(OverlayThemeIds.Default));

        Assert.Equal(expected, viewModel.CountdownText);
    }

    [Fact]
    public void DisposingControllerClosesViewAndUnregistersWidget()
    {
        var engine = Engine();
        UpNextOverlayController controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot());
        FakeView view = Assert.Single(views.Created);

        controller.Dispose();

        Assert.True(view.WasClosed);
        Assert.False(engine.TryGetWidget(OverlayWidgetIds.UpNext, out _));
    }

    private static OverlayEngine Engine()
    {
        var engine = new OverlayEngine(
            new OverlayLayout(
                "Legacy.UpNext.Test",
                [new OverlayWidgetPlacement(
                    OverlayWidgetIds.UpNext,
                    true,
                    390,
                    216,
                    366,
                    150,
                    2)]),
            OverlayThemeCatalog.Get(OverlayThemeIds.Default));
        engine.SetForegroundState(true);
        return engine;
    }

    private static UpNextOverlayController Controller(
        OverlayEngine engine,
        out ViewFactory views)
    {
        views = new ViewFactory();
        return new UpNextOverlayController(engine, views.Create);
    }

    private static OverlaySnapshot Snapshot(
        GameplayStructureKind dominant = GameplayStructureKind.Jump,
        GameplayStructureKind? secondary = null,
        double untilStartSeconds = 5,
        string? summary = null)
    {
        BeatmapAnalysisSnapshot source = OverviewViewModelTests.BuildSnapshot();
        GameplaySectionV2Analysis sourceSection = source.SectionAnalysisV2.Sections[0];
        GameplaySectionV2Analysis section = sourceSection with
        {
            SectionIndex = 4,
            StartTime = 20_000,
            EndTime = 25_000,
            ObjectIndices = [4],
            DominantStructure = dominant,
            SecondaryStructure = secondary,
            Summary = summary ?? $"Next: {dominant}"
        };
        BeatmapAnalysisSnapshot analysis = source with
        {
            SectionAnalysisV2 = source.SectionAnalysisV2 with { Sections = [section] }
        };
        TimeSpan untilStart = TimeSpan.FromSeconds(untilStartSeconds);
        TimeSpan start = TimeSpan.FromMilliseconds(section.StartTime);
        TimeSpan current = start - untilStart;
        TimeSpan duration = TimeSpan.FromMinutes(2);
        var live = new LiveGameplaySnapshot(
            true,
            current,
            duration,
            current.TotalMilliseconds / duration.TotalMilliseconds,
            analysis,
            123,
            456,
            "42@generation",
            DateTimeOffset.UtcNow);
        var next = new NextSectionSnapshot(
            section,
            start,
            TimeSpan.FromMilliseconds(section.EndTime),
            TimeSpan.FromMilliseconds(section.EndTime - section.StartTime),
            untilStart);

        return new(
            Game(),
            OverlayGameState.Gameplay,
            analysis,
            current,
            LiveGameplay: live,
            NextSection: next);
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

        public IUpNextOverlayView Create()
        {
            var view = new FakeView();
            Created.Add(view);
            return view;
        }
    }

    private sealed class FakeView : IUpNextOverlayView
    {
        public event EventHandler? Closed;

        public bool IsVisible { get; private set; }
        public bool WasClosed { get; private set; }
        public UpNextOverlayState? State { get; private set; }
        public OverlayWidgetPlacement? Placement { get; private set; }
        public List<string> RenderedDominantStructures { get; } = [];

        public void Apply(
            UpNextOverlayState state,
            OverlayWidgetPlacement placement,
            OverlayTheme theme)
        {
            State = state;
            Placement = placement;
            if (state.DominantStructure is not null)
                RenderedDominantStructures.Add(state.DominantStructure);
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
