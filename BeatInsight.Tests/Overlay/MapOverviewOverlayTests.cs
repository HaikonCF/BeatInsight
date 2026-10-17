using BeatInsight.Integration.Game;
using BeatInsight.Models.AnalysisCache;
using BeatInsight.Overlay;
using BeatInsight.Overlay.MapOverview;
using BeatInsight.Tests.ViewModels;

namespace BeatInsight.Tests.Overlay;

public sealed class MapOverviewOverlayTests
{
    [Fact]
    public void ControllerRegistersMapOverviewWidget()
    {
        var engine = Engine();
        using var controller = Controller(engine, out _);

        Assert.True(engine.TryGetWidget(
            OverlayWidgetIds.MapOverview,
            out OverlayWidgetRegistration? widget));
        Assert.Equal("Map overview", widget!.Definition.DisplayName);
    }

    [Fact]
    public void SnapshotCreatesVisibleRenderingStateFromExistingAnalysis()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);

        engine.PublishSnapshot(Snapshot("Say Goodbye"));

        FakeView view = Assert.Single(views.Created);
        Assert.True(view.IsVisible);
        Assert.Equal("Say Goodbye", view.State!.Title);
        Assert.Equal("Version", view.State.Difficulty);
        Assert.Equal("Artist", view.State.Artist);
        Assert.Equal("Creator", view.State.Mapper);
        Assert.Equal(6.5, view.State.StarRating);
        Assert.Equal(5.4, view.State.BeatInsightRating);
        Assert.Equal("Stream", view.State.GameplayIdentity);
    }

    [Fact]
    public void MapAThenMapBReplacesEveryRenderedValue()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);

        engine.PublishSnapshot(Snapshot("Map A", artist: "Artist A", rating: 4.2));
        engine.PublishSnapshot(Snapshot("Map B", artist: "Artist B", rating: 7.8));

        FakeView view = Assert.Single(views.Created);
        Assert.Equal("Map B", view.State!.Title);
        Assert.Equal("Artist B", view.State.Artist);
        Assert.Equal(7.8, view.State.BeatInsightRating);
        Assert.Equal("Map B", view.RenderedTitles[^1]);
    }

    [Fact]
    public void MapBThenMapARestoresMapAWithoutStaleMapBData()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);

        engine.PublishSnapshot(Snapshot("Map A", artist: "Artist A"));
        engine.PublishSnapshot(Snapshot("Map B", artist: "Artist B"));
        engine.PublishSnapshot(Snapshot("Map A", artist: "Artist A"));

        FakeView view = Assert.Single(views.Created);
        Assert.Equal("Map A", view.State!.Title);
        Assert.Equal("Artist A", view.State.Artist);
        Assert.Equal(["Map A", "Map B", "Map A"], view.RenderedTitles);
    }

    [Fact]
    public void AvailableClientWithoutBeatmapAnalysisStaysHidden()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);

        engine.PublishSnapshot(new OverlaySnapshot(
            Game(available: true, path: null),
            OverlayGameState.SongSelect));

        Assert.Empty(views.Created);
        Assert.Empty(engine.GetVisibleWidgets());
    }

    [Fact]
    public void NoClientClearsAndHidesExistingOverlay()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot("Map A"));
        FakeView view = Assert.Single(views.Created);

        engine.PublishSnapshot(new OverlaySnapshot(
            Game(available: false),
            OverlayGameState.Idle,
            Analysis("stale")));

        Assert.False(view.IsVisible);
        Assert.Equal(MapOverviewOverlayState.Empty, view.State);
        Assert.Empty(engine.GetVisibleWidgets());
    }

    [Fact]
    public void MissingOptionalMetadataIsNotInvented()
    {
        BeatmapAnalysisSnapshot analysis = Analysis("Partial") with
        {
            Metadata = Analysis("Partial").Metadata with
            {
                Artist = null!,
                Creator = null!,
                Version = null!,
                PrimaryBpm = null,
                Bpm = 0,
                OsuMaxCombo = null,
                MaxCombo = 0,
                LengthTicks = 0,
                OsuStarRating = double.NaN
            },
            RatingV2 = null!,
            IdentityV3 = null!
        };

        MapOverviewOverlayState state = MapOverviewSnapshotProjector.Project(
            new OverlaySnapshot(Game(), OverlayGameState.SongSelect, analysis));

        Assert.True(state.HasContent);
        Assert.Equal("Partial", state.Title);
        Assert.Null(state.Artist);
        Assert.Null(state.Mapper);
        Assert.Null(state.Difficulty);
        Assert.Null(state.Bpm);
        Assert.Null(state.MaxCombo);
        Assert.Null(state.Length);
        Assert.Null(state.StarRating);
        Assert.Null(state.BeatInsightRating);
        Assert.Null(state.GameplayIdentity);
    }

    [Fact]
    public void NullSnapshotClearsStaleDataBeforeNextMap()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot("Map A"));
        FakeView view = Assert.Single(views.Created);

        engine.PublishSnapshot(null);

        Assert.False(view.IsVisible);
        Assert.Equal(MapOverviewOverlayState.Empty, view.State);
        Assert.Null(engine.CurrentSnapshot);
    }

    [Fact]
    public void LayoutControlsWindowBounds()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        var placement = new OverlayWidgetPlacement(
            OverlayWidgetIds.MapOverview,
            true,
            81, 42, 512, 220, 17);
        engine.ApplyLayout(new OverlayLayout("Custom.Map", [placement]));

        engine.PublishSnapshot(Snapshot("Map A"));

        Assert.Equal(placement, Assert.Single(views.Created).Placement);
    }

    [Fact]
    public void ThemeIsPassedIndependentlyToWindow()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        var theme = new OverlayTheme(
            "Custom.Map",
            new OverlayThemeTokens
            {
                Text = new Dictionary<string, string> { ["primary"] = "#FFFFFFFF" },
                Cards = new Dictionary<string, string> { ["background"] = "#CC000000" },
                Borders = new Dictionary<string, string> { ["default"] = "#FF00FFFF" },
                Backgrounds = new Dictionary<string, string> { ["window"] = "#55000000" }
            },
            new Dictionary<string, string>());
        engine.ApplyTheme(theme);

        engine.PublishSnapshot(Snapshot("Map A"));

        Assert.Same(theme, Assert.Single(views.Created).Theme);
    }

    [Fact]
    public void RuntimeThemeSwitchReusesExistingWindowInBothDirections()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot("Map A"));
        FakeView view = Assert.Single(views.Created);

        engine.ApplyTheme(OverlayThemeCatalog.Get(OverlayThemeIds.Bia));

        Assert.Same(view, Assert.Single(views.Created));
        Assert.Equal(OverlayThemeIds.Bia, view.Theme!.Id);

        engine.ApplyTheme(OverlayThemeCatalog.Get(OverlayThemeIds.Default));

        Assert.Same(view, Assert.Single(views.Created));
        Assert.Equal(OverlayThemeIds.Default, view.Theme!.Id);
        Assert.Equal("Map A", view.State!.Title);
    }

    [Fact]
    public void ClosingOverlayDoesNotDisposeEngineAndNextSnapshotCanReopenIt()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot("Map A"));
        FakeView first = Assert.Single(views.Created);

        first.CloseByUser();
        Assert.True(engine.TryGetWidget(OverlayWidgetIds.MapOverview, out _));

        engine.PublishSnapshot(Snapshot("Map B"));

        Assert.Equal(2, views.Created.Count);
        Assert.Equal("Map B", views.Created[1].State!.Title);
        Assert.True(views.Created[1].IsVisible);
    }

    [Fact]
    public void DisposingControllerClosesOverlayAndUnregistersWidget()
    {
        var engine = Engine();
        MapOverviewOverlayController controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot("Map A"));
        FakeView view = Assert.Single(views.Created);

        controller.Dispose();

        Assert.True(view.WasClosed);
        Assert.False(engine.TryGetWidget(OverlayWidgetIds.MapOverview, out _));
    }

    private static OverlayEngine Engine()
    {
        var engine = new OverlayEngine(
            OverlayLayoutCatalog.Get(OverlayLayoutIds.Minimal),
            OverlayThemeCatalog.Get(OverlayThemeIds.Default));
        engine.SetForegroundState(true);
        return engine;
    }

    private static MapOverviewOverlayController Controller(
        OverlayEngine engine,
        out ViewFactory views)
    {
        views = new ViewFactory();
        return new MapOverviewOverlayController(engine, views.Create);
    }

    private static OverlaySnapshot Snapshot(
        string title,
        string artist = "Artist",
        double rating = 5.4) =>
        new(
            Game(path: $"{title}.osu"),
            OverlayGameState.SongSelect,
            Analysis(title, artist, rating));

    private static BeatmapAnalysisSnapshot Analysis(
        string title,
        string artist = "Artist",
        double rating = 5.4)
    {
        BeatmapAnalysisSnapshot source = OverviewViewModelTests.BuildSnapshot(
            ratingScore: rating);
        return source with
        {
            Metadata = source.Metadata with
            {
                Title = title,
                Artist = artist
            }
        };
    }

    private static GameStateSnapshot Game(
        bool available = true,
        string? path = "map.osu") =>
        new(
            "test",
            available ? "osu!" : null,
            available ? 42 : null,
            available ? "process" : null,
            available ? path : null,
            available ? "1" : null,
            available ? "2" : null,
            DateTimeOffset.UtcNow,
            available ? GameStateAvailability.Available : GameStateAvailability.NoProcess,
            available ? null : "No client.");

    private sealed class ViewFactory
    {
        public List<FakeView> Created { get; } = [];

        public IMapOverviewOverlayView Create()
        {
            var view = new FakeView();
            Created.Add(view);
            return view;
        }
    }

    private sealed class FakeView : IMapOverviewOverlayView
    {
        public event EventHandler? Closed;

        public bool IsVisible { get; private set; }
        public bool WasClosed { get; private set; }
        public MapOverviewOverlayState? State { get; private set; }
        public OverlayWidgetPlacement? Placement { get; private set; }
        public OverlayTheme? Theme { get; private set; }
        public List<string> RenderedTitles { get; } = [];

        public void Apply(
            MapOverviewOverlayState state,
            OverlayWidgetPlacement placement,
            OverlayTheme theme)
        {
            State = state;
            Placement = placement;
            Theme = theme;
            if (state.Title is not null)
            {
                RenderedTitles.Add(state.Title);
            }
        }

        public void Show() => IsVisible = true;

        public void Hide() => IsVisible = false;

        public void Close()
        {
            WasClosed = true;
            IsVisible = false;
            Closed?.Invoke(this, EventArgs.Empty);
        }

        public void CloseByUser()
        {
            IsVisible = false;
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}
