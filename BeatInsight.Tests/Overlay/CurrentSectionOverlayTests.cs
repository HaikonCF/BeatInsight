using BeatInsight.Integration.Game;
using BeatInsight.Models.AnalysisCache;
using BeatInsight.Models.Fusion;
using BeatInsight.Models.Sections;
using BeatInsight.Overlay;
using BeatInsight.Overlay.CurrentSection;
using BeatInsight.Tests.ViewModels;

namespace BeatInsight.Tests.Overlay;

public sealed class CurrentSectionOverlayTests
{
    [Fact]
    public void ControllerRegistersGameplayOnlyCurrentSectionWidget()
    {
        var engine = Engine();
        using var controller = Controller(engine, out _);

        Assert.True(engine.TryGetWidget(
            OverlayWidgetIds.CurrentSection,
            out OverlayWidgetRegistration? widget));
        Assert.Equal("Current section", widget!.Definition.DisplayName);
        Assert.Equal([OverlayGameState.Gameplay], widget.Definition.SupportedStates);
        Assert.True(widget.Definition.RequiresAnalysisSnapshot);
    }

    [Fact]
    public void ValidSnapshotProjectsAnalyzerOwnedSectionData()
    {
        OverlaySnapshot snapshot = Snapshot(
            dominant: GameplayStructureKind.Tech,
            secondary: GameplayStructureKind.Stream,
            progress: 0.42,
            remainingSeconds: 7.25,
            summary: "  Technical transitions  ");

        CurrentSectionOverlayState state =
            CurrentSectionSnapshotProjector.Project(snapshot);

        Assert.True(state.HasContent);
        Assert.Equal("Tech", state.DominantStructure);
        Assert.Equal("Stream", state.SecondaryStructure);
        Assert.Equal(0.42, state.LocalProgress);
        Assert.Equal(TimeSpan.FromSeconds(7.25), state.TimeRemaining);
        Assert.Equal("Technical transitions", state.Summary);
    }

    [Fact]
    public void ValidCurrentSectionCreatesAndShowsView()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);

        engine.PublishSnapshot(Snapshot());

        FakeView view = Assert.Single(views.Created);
        Assert.True(view.IsVisible);
        Assert.True(view.State!.HasContent);
        Assert.Equal("Jump", view.State.DominantStructure);
    }

    [Fact]
    public void PrivateLegacyLayoutStillSupportsCurrentSectionControllerTests()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);

        engine.PublishSnapshot(Snapshot());

        FakeView view = Assert.Single(views.Created);
        Assert.True(view.IsVisible);
        Assert.Equal(
            OverlayWidgetIds.CurrentSection,
            view.Placement!.WidgetId);
    }

    [Fact]
    public void MissingCurrentSectionDoesNotCreateView()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);

        engine.PublishSnapshot(Snapshot() with { CurrentSection = null });

        Assert.Empty(views.Created);
        Assert.Empty(engine.GetVisibleWidgets());
    }

    [Fact]
    public void SongSelectClearsAndHidesPreviouslyVisibleSection()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot());
        FakeView view = Assert.Single(views.Created);

        engine.PublishSnapshot(Snapshot() with
        {
            GameMode = OverlayGameState.SongSelect,
            LiveGameplay = null,
            CurrentSection = null
        });

        Assert.False(view.IsVisible);
        Assert.Equal(CurrentSectionOverlayState.Empty, view.State);
        Assert.Empty(engine.GetVisibleWidgets());
    }

    [Fact]
    public void GameplayExitClearsAndHidesPreviouslyVisibleSection()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot());
        FakeView view = Assert.Single(views.Created);

        engine.PublishSnapshot(null);

        Assert.False(view.IsVisible);
        Assert.Equal(CurrentSectionOverlayState.Empty, view.State);
        Assert.Null(engine.CurrentSnapshot);
    }

    [Fact]
    public void SectionChangeReplacesStructureProgressAndRemainingTime()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot(
            dominant: GameplayStructureKind.Jump,
            progress: 0.2,
            remainingSeconds: 8));

        engine.PublishSnapshot(Snapshot(
            dominant: GameplayStructureKind.Stream,
            secondary: GameplayStructureKind.Tech,
            progress: 0.75,
            remainingSeconds: 2));

        FakeView view = Assert.Single(views.Created);
        Assert.Equal("Stream", view.State!.DominantStructure);
        Assert.Equal("Tech", view.State.SecondaryStructure);
        Assert.Equal(0.75, view.State.LocalProgress);
        Assert.Equal(TimeSpan.FromSeconds(2), view.State.TimeRemaining);
    }

    [Fact]
    public void MapChangeGapClearsOldSectionBeforeNewMapSectionAppears()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot("Map A", GameplayStructureKind.Jump));
        FakeView view = Assert.Single(views.Created);

        engine.PublishSnapshot(Snapshot("Map B", GameplayStructureKind.Stream) with
        {
            LiveGameplay = null,
            CurrentSection = null
        });

        Assert.False(view.IsVisible);
        Assert.Equal(CurrentSectionOverlayState.Empty, view.State);

        engine.PublishSnapshot(Snapshot("Map B", GameplayStructureKind.Stream));

        Assert.True(view.IsVisible);
        Assert.Equal("Stream", view.State!.DominantStructure);
        Assert.DoesNotContain("Jump", view.RenderedDominantStructures[^1]);
    }

    [Fact]
    public void UnavailableClientCannotRetainLastSection()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot());
        FakeView view = Assert.Single(views.Created);

        engine.PublishSnapshot(Snapshot() with
        {
            Game = Game(available: false),
            GameMode = OverlayGameState.Idle,
            LiveGameplay = null,
            CurrentSection = null
        });

        Assert.False(view.IsVisible);
        Assert.Equal(CurrentSectionOverlayState.Empty, view.State);
    }

    [Fact]
    public void ViewModelFormatsProgressAndRemainingTime()
    {
        var viewModel = new CurrentSectionOverlayViewModel();

        viewModel.Apply(
            new CurrentSectionOverlayState(
                true,
                "Stream",
                null,
                0.724,
                TimeSpan.FromSeconds(12.4),
                null),
            OverlayThemeCatalog.Get(OverlayThemeIds.Default));

        Assert.Equal(72.4, viewModel.ProgressValue, 12);
        Assert.Equal("72%", viewModel.ProgressText);
        Assert.Equal("12.4 s remaining", viewModel.RemainingText);
    }

    [Fact]
    public void ViewModelFormatsMinuteScaleRemainingTime()
    {
        var viewModel = new CurrentSectionOverlayViewModel();

        viewModel.Apply(
            new CurrentSectionOverlayState(
                true,
                "Stream",
                null,
                0.1,
                TimeSpan.FromSeconds(75),
                null),
            OverlayThemeCatalog.Get(OverlayThemeIds.Default));

        Assert.Equal("1:15 remaining", viewModel.RemainingText);
    }

    [Fact]
    public void PrivateLegacyLayoutControlsCurrentSectionPlacement()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);

        engine.PublishSnapshot(Snapshot());

        OverlayWidgetPlacement expected = Assert.Single(engine.CurrentLayout.Widgets);
        Assert.Equal(expected, Assert.Single(views.Created).Placement);
    }

    [Fact]
    public void DisabledWidgetClearsAndHidesView()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot());
        FakeView view = Assert.Single(views.Created);
        Assert.True(engine.TryGetWidget(
            OverlayWidgetIds.CurrentSection,
            out OverlayWidgetRegistration? widget));

        widget!.IsEnabled = false;
        controller.Refresh();

        Assert.False(view.IsVisible);
        Assert.Equal(CurrentSectionOverlayState.Empty, view.State);
    }

    [Fact]
    public void DisposingControllerClosesViewAndUnregistersWidget()
    {
        var engine = Engine();
        CurrentSectionOverlayController controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot());
        FakeView view = Assert.Single(views.Created);

        controller.Dispose();

        Assert.True(view.WasClosed);
        Assert.False(engine.TryGetWidget(OverlayWidgetIds.CurrentSection, out _));
    }

    private static OverlayEngine Engine()
    {
        var engine = new OverlayEngine(
            new OverlayLayout(
                "Legacy.CurrentSection.Test",
                [new OverlayWidgetPlacement(
                    OverlayWidgetIds.CurrentSection,
                    true,
                    24,
                    216,
                    354,
                    150,
                    1)]),
            OverlayThemeCatalog.Get(OverlayThemeIds.Default));
        engine.SetForegroundState(true);
        return engine;
    }

    private static CurrentSectionOverlayController Controller(
        OverlayEngine engine,
        out ViewFactory views)
    {
        views = new ViewFactory();
        return new CurrentSectionOverlayController(engine, views.Create);
    }

    private static OverlaySnapshot Snapshot(
        string title = "Map A",
        GameplayStructureKind dominant = GameplayStructureKind.Jump,
        GameplayStructureKind? secondary = null,
        double progress = 0.5,
        double remainingSeconds = 5,
        string? summary = null)
    {
        BeatmapAnalysisSnapshot source = OverviewViewModelTests.BuildSnapshot();
        GameplaySectionV2Analysis sourceSection = source.SectionAnalysisV2.Sections[0];
        GameplaySectionV2Analysis section = sourceSection with
        {
            SectionIndex = 3,
            StartTime = 10_000,
            EndTime = 20_000,
            DominantStructure = dominant,
            SecondaryStructure = secondary,
            Summary = summary ?? $"{title}: {dominant}"
        };
        BeatmapAnalysisSnapshot analysis = source with
        {
            Metadata = source.Metadata with
            {
                Title = title,
                BeatmapId = 123,
                BeatmapSetId = 456,
                LengthTicks = TimeSpan.FromMinutes(2).Ticks
            },
            SectionAnalysisV2 = source.SectionAnalysisV2 with
            {
                Sections = [section]
            }
        };
        TimeSpan duration = TimeSpan.FromSeconds(10);
        TimeSpan remaining = TimeSpan.FromSeconds(remainingSeconds);
        TimeSpan current = TimeSpan.FromMilliseconds(
            section.StartTime + (duration.TotalMilliseconds * progress));
        var live = new LiveGameplaySnapshot(
            true,
            current,
            TimeSpan.FromMinutes(2),
            current.TotalMilliseconds / TimeSpan.FromMinutes(2).TotalMilliseconds,
            analysis,
            123,
            456,
            "42@generation",
            DateTimeOffset.UtcNow);
        var currentSection = new CurrentSectionSnapshot(
            section,
            TimeSpan.FromMilliseconds(section.StartTime),
            TimeSpan.FromMilliseconds(section.EndTime),
            duration,
            progress,
            remaining);

        return new(
            Game(path: $"{title}.osu"),
            OverlayGameState.Gameplay,
            analysis,
            current,
            LiveGameplay: live,
            CurrentSection: currentSection);
    }

    private static GameStateSnapshot Game(
        bool available = true,
        string? path = "map.osu") =>
        new(
            "test",
            available ? "osu!" : null,
            available ? 42 : null,
            available ? "42@generation" : null,
            available ? path : null,
            available ? "123" : null,
            available ? "456" : null,
            DateTimeOffset.UtcNow,
            available ? GameStateAvailability.Available : GameStateAvailability.NoProcess,
            available ? null : "No client.");

    private sealed class ViewFactory
    {
        public List<FakeView> Created { get; } = [];

        public ICurrentSectionOverlayView Create()
        {
            var view = new FakeView();
            Created.Add(view);
            return view;
        }
    }

    private sealed class FakeView : ICurrentSectionOverlayView
    {
        public event EventHandler? Closed;

        public bool IsVisible { get; private set; }
        public bool WasClosed { get; private set; }
        public CurrentSectionOverlayState? State { get; private set; }
        public OverlayWidgetPlacement? Placement { get; private set; }
        public OverlayTheme? Theme { get; private set; }
        public List<string> RenderedDominantStructures { get; } = [];

        public void Apply(
            CurrentSectionOverlayState state,
            OverlayWidgetPlacement placement,
            OverlayTheme theme)
        {
            State = state;
            Placement = placement;
            Theme = theme;
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
