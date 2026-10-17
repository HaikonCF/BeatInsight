using BeatInsight.Integration.Game;
using BeatInsight.Models.AnalysisCache;
using BeatInsight.Models.SkillPressure;
using BeatInsight.Models.Speed;
using BeatInsight.Overlay;
using BeatInsight.Overlay.MapOverview;
using BeatInsight.Overlay.Skills;
using BeatInsight.Tests.ViewModels;

namespace BeatInsight.Tests.Overlay;

public sealed class SkillsOverlayTests
{
    [Fact]
    public void ControllerRegistersSkillsWidget()
    {
        var engine = Engine();
        using var controller = Controller(engine, out _);

        Assert.True(engine.TryGetWidget(
            OverlayWidgetIds.Skills,
            out OverlayWidgetRegistration? widget));
        Assert.Equal("Skills", widget!.Definition.DisplayName);
        Assert.True(widget.Definition.RequiresAnalysisSnapshot);
    }

    [Fact]
    public void SnapshotCreatesSkillsStateFromExistingAnalysis()
    {
        SkillsOverlayState state = SkillsSnapshotProjector.Project(Snapshot("Map A"));

        Assert.True(state.HasContent);
        Assert.Equal(9, state.Metrics.Count);
    }

    [Fact]
    public void EveryAvailableRequestedMetricUsesItsExistingSnapshotValue()
    {
        SkillsOverlayState state = SkillsSnapshotProjector.Project(Snapshot("Map A"));

        Assert.Equal(
            ["Aim", "Speed", "Reading", "Tech", "Jump", "Stream", "Burst", "Stamina", "Local Speed Pressure"],
            state.Metrics.Select(metric => metric.Name));
        Assert.Equal(
            [35d, 63d, 55d, 20d, 20d, 50d, 10d, 70d, 59d],
            state.Metrics.Select(metric => metric.Value));
    }

    [Fact]
    public void MissingMetricIsNotInvented()
    {
        BeatmapAnalysisSnapshot analysis = Analysis("Partial") with
        {
            GameplayProfile = null!,
            SkillPressureV2 = Analysis("Partial").SkillPressureV2 with
            {
                AimControl = SkillPressureMetric.Unavailable("Aim unavailable")
            }
        };

        SkillsOverlayState state = SkillsSnapshotProjector.Project(
            new OverlaySnapshot(Game(), OverlayGameState.SongSelect, analysis));

        Assert.Null(state.Metrics.Single(metric => metric.Name == "Aim").Value);
        Assert.Null(state.Metrics.Single(metric => metric.Name == "Tech").Value);
        Assert.Null(state.Metrics.Single(metric => metric.Name == "Jump").Value);
    }

    [Fact]
    public void NullableMetricDisplaysExplicitUnavailablePlaceholder()
    {
        var metric = new SkillsOverlayMetricViewModel("Stamina", null);

        Assert.Equal(0, metric.BarValue);
        Assert.Equal("—", metric.DisplayValue);
    }

    [Fact]
    public void MapAThenMapBReplacesEverySkillValue()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);

        engine.PublishSnapshot(Snapshot("Map A", aim: 11, speed: 22, stamina: 33));
        engine.PublishSnapshot(Snapshot("Map B", aim: 71, speed: 82, stamina: 93));

        FakeSkillsView view = Assert.Single(views.Created);
        Assert.Equal(71, Value(view, "Aim"));
        Assert.Equal(82, Value(view, "Speed"));
        Assert.Equal(93, Value(view, "Stamina"));
        Assert.Equal([11d, 71d], view.AimHistory);
    }

    [Fact]
    public void MapBThenMapARestoresMapAWithoutStaleSkills()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);

        engine.PublishSnapshot(Snapshot("Map A", aim: 11, speed: 22, stamina: 33));
        engine.PublishSnapshot(Snapshot("Map B", aim: 71, speed: 82, stamina: 93));
        engine.PublishSnapshot(Snapshot("Map A", aim: 11, speed: 22, stamina: 33));

        FakeSkillsView view = Assert.Single(views.Created);
        Assert.Equal(11, Value(view, "Aim"));
        Assert.Equal(22, Value(view, "Speed"));
        Assert.Equal(33, Value(view, "Stamina"));
        Assert.Equal([11d, 71d, 11d], view.AimHistory);
    }

    [Fact]
    public void NoBeatmapKeepsSkillsHidden()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);

        engine.PublishSnapshot(new OverlaySnapshot(
            Game(available: true, path: null),
            OverlayGameState.SongSelect));

        Assert.Empty(views.Created);
        Assert.DoesNotContain(
            engine.GetVisibleWidgets(),
            widget => widget.Registration.Definition.Id == OverlayWidgetIds.Skills);
    }

    [Fact]
    public void NoClientClearsAndHidesExistingSkills()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot("Map A"));
        FakeSkillsView view = Assert.Single(views.Created);

        engine.PublishSnapshot(new OverlaySnapshot(
            Game(available: false),
            OverlayGameState.Idle,
            Analysis("stale")));

        Assert.False(view.IsVisible);
        Assert.Equal(SkillsOverlayState.Empty, view.State);
    }

    [Fact]
    public void NullSnapshotClearsAllStaleSkillValues()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot("Map A"));
        FakeSkillsView view = Assert.Single(views.Created);

        engine.PublishSnapshot(null);

        Assert.False(view.IsVisible);
        Assert.Equal(SkillsOverlayState.Empty, view.State);
        Assert.Empty(view.State!.Metrics);
    }

    [Fact]
    public void OfficialMinimalLayoutAppliesSkillsPlacement()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);

        engine.PublishSnapshot(Snapshot("Map A"));

        OverlayWidgetPlacement placement = Assert.Single(
            engine.CurrentLayout.Widgets,
            item => item.WidgetId == OverlayWidgetIds.Skills);
        Assert.Equal(placement, Assert.Single(views.Created).Placement);
        Assert.True(placement.IsVisible);
        Assert.True(placement.ZIndex > 0);
    }

    [Theory]
    [InlineData(OverlayThemeIds.Default)]
    [InlineData(OverlayThemeIds.Bia)]
    public void OfficialThemeIsAppliedWithoutWidgetSpecificBranch(string themeId)
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        OverlayTheme theme = OverlayThemeCatalog.Get(themeId);
        engine.ApplyTheme(theme);

        engine.PublishSnapshot(Snapshot("Map A"));

        Assert.Same(theme, Assert.Single(views.Created).Theme);
    }

    [Fact]
    public void MapOverviewAndSkillsReceiveTheSameSnapshotSimultaneously()
    {
        var engine = Engine();
        using var mapController = MapController(engine, out MapViewFactory mapViews);
        using var skillsController = Controller(engine, out ViewFactory skillsViews);
        OverlaySnapshot snapshot = Snapshot("Map A", aim: 44);

        engine.PublishSnapshot(snapshot);

        Assert.Equal(
            MapOverviewSnapshotProjector.Project(snapshot),
            Assert.Single(mapViews.Created).State);
        Assert.Equal(44, Value(Assert.Single(skillsViews.Created), "Aim"));
        Assert.Same(snapshot, engine.CurrentSnapshot);
    }

    [Fact]
    public void GlobalDisplayGateHidesBothWidgetsAndRestoresCurrentSnapshotWithoutStaleData()
    {
        var engine = Engine();
        using var mapController = MapController(engine, out MapViewFactory mapViews);
        using var skillsController = Controller(engine, out ViewFactory skillsViews);
        OverlaySnapshot mapA = Snapshot("Map A", aim: 11);
        OverlaySnapshot mapB = Snapshot("Map B", aim: 77);

        engine.PublishSnapshot(mapA);
        FakeMapView mapView = Assert.Single(mapViews.Created);
        FakeSkillsView skillsView = Assert.Single(skillsViews.Created);

        engine.SetDisplayCompatibility(
            OverlayDisplayCompatibility.FullscreenExclusive);

        Assert.False(engine.OverlaySupported);
        Assert.False(engine.OverlayVisible);
        Assert.False(mapView.IsVisible);
        Assert.False(skillsView.IsVisible);
        Assert.Same(mapA, engine.CurrentSnapshot);

        engine.PublishSnapshot(mapB);

        Assert.False(mapView.IsVisible);
        Assert.False(skillsView.IsVisible);
        Assert.Same(mapB, engine.CurrentSnapshot);

        engine.SetDisplayCompatibility(
            OverlayDisplayCompatibility.Supported(OverlayDisplayMode.Borderless));

        Assert.True(engine.OverlaySupported);
        Assert.True(engine.OverlayVisible);
        Assert.True(mapView.IsVisible);
        Assert.True(skillsView.IsVisible);
        Assert.Equal("Map B", mapView.State!.Title);
        Assert.Equal(77, Value(skillsView, "Aim"));
        Assert.Same(mapB, engine.CurrentSnapshot);
    }

    [Fact]
    public void ForegroundGateHidesBothWidgetsAndRestoresSnapshotWithoutRepublishingIt()
    {
        var engine = Engine();
        using var mapController = MapController(engine, out MapViewFactory mapViews);
        using var skillsController = Controller(engine, out ViewFactory skillsViews);
        OverlaySnapshot snapshot = Snapshot("Say Goodbye", aim: 56);
        engine.PublishSnapshot(snapshot);
        FakeMapView mapView = Assert.Single(mapViews.Created);
        FakeSkillsView skillsView = Assert.Single(skillsViews.Created);

        engine.SetForegroundState(false);

        Assert.False(engine.OverlayVisible);
        Assert.False(mapView.IsVisible);
        Assert.False(skillsView.IsVisible);
        Assert.Same(snapshot, engine.CurrentSnapshot);

        engine.SetForegroundState(true);

        Assert.True(engine.OverlayVisible);
        Assert.True(mapView.IsVisible);
        Assert.True(skillsView.IsVisible);
        Assert.Equal("Say Goodbye", mapView.State!.Title);
        Assert.Equal(56, Value(skillsView, "Aim"));
        Assert.Same(snapshot, engine.CurrentSnapshot);
    }

    [Fact]
    public void SkillsViewFailureDoesNotBreakMapOverview()
    {
        var engine = Engine();
        using var mapController = MapController(engine, out MapViewFactory mapViews);
        using var skillsController = new SkillsOverlayController(
            engine,
            static () => new ThrowingSkillsView());

        engine.PublishSnapshot(Snapshot("Map A"));

        Assert.True(Assert.Single(mapViews.Created).IsVisible);
        Assert.Contains(
            engine.GetVisibleWidgets(),
            widget => widget.Registration.Definition.Id == OverlayWidgetIds.MapOverview);
        Assert.DoesNotContain(
            engine.GetVisibleWidgets(),
            widget => widget.Registration.Definition.Id == OverlayWidgetIds.Skills);
    }

    [Fact]
    public void RemovingSkillsDoesNotBreakMapOverview()
    {
        var engine = Engine();
        using var mapController = MapController(engine, out MapViewFactory mapViews);
        var skillsController = Controller(engine, out _);
        engine.PublishSnapshot(Snapshot("Map A"));

        skillsController.Dispose();
        engine.PublishSnapshot(Snapshot("Map B"));

        Assert.False(engine.TryGetWidget(OverlayWidgetIds.Skills, out _));
        Assert.Equal("Map B", Assert.Single(mapViews.Created).State!.Title);
        Assert.True(mapViews.Created[0].IsVisible);
    }

    [Fact]
    public void ClosingSkillsWindowKeepsRegistrationAndNextSnapshotRecreatesIt()
    {
        var engine = Engine();
        using var controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot("Map A"));
        FakeSkillsView first = Assert.Single(views.Created);

        first.CloseByUser();
        engine.PublishSnapshot(Snapshot("Map B", aim: 77));

        Assert.True(engine.TryGetWidget(OverlayWidgetIds.Skills, out _));
        Assert.Equal(2, views.Created.Count);
        Assert.Equal(77, Value(views.Created[1], "Aim"));
        Assert.True(views.Created[1].IsVisible);
    }

    [Fact]
    public void DisposingControllerClosesWindowAndUnregistersSkills()
    {
        var engine = Engine();
        SkillsOverlayController controller = Controller(engine, out ViewFactory views);
        engine.PublishSnapshot(Snapshot("Map A"));
        FakeSkillsView view = Assert.Single(views.Created);

        controller.Dispose();

        Assert.True(view.WasClosed);
        Assert.False(engine.TryGetWidget(OverlayWidgetIds.Skills, out _));
    }

    private static OverlayEngine Engine()
    {
        var engine = new OverlayEngine(
            OverlayLayoutCatalog.Get(OverlayLayoutIds.Minimal),
            OverlayThemeCatalog.Get(OverlayThemeIds.Default));
        engine.SetForegroundState(true);
        return engine;
    }

    private static SkillsOverlayController Controller(
        OverlayEngine engine,
        out ViewFactory views)
    {
        views = new ViewFactory();
        return new SkillsOverlayController(engine, views.Create);
    }

    private static MapOverviewOverlayController MapController(
        OverlayEngine engine,
        out MapViewFactory views)
    {
        views = new MapViewFactory();
        return new MapOverviewOverlayController(engine, views.Create);
    }

    private static OverlaySnapshot Snapshot(
        string title,
        double aim = 35,
        double speed = 63,
        double stamina = 70) =>
        new(
            Game(path: $"{title}.osu"),
            OverlayGameState.SongSelect,
            Analysis(title, aim, speed, stamina));

    private static BeatmapAnalysisSnapshot Analysis(
        string title,
        double aim = 35,
        double speed = 63,
        double stamina = 70)
    {
        BeatmapAnalysisSnapshot source = OverviewViewModelTests.BuildSnapshot(
            tripletSpeedPercent: speed);
        return source with
        {
            Metadata = source.Metadata with { Title = title },
            SkillPressureV2 = source.SkillPressureV2 with
            {
                AimControl = Metric(aim),
                Stamina = Metric(stamina)
            }
        };
    }

    private static SkillPressureMetric Metric(double score) =>
        new(score, 0.8, true, true, new Dictionary<string, double>(), string.Empty);

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

    private static double? Value(FakeSkillsView view, string name) =>
        view.State!.Metrics.Single(metric => metric.Name == name).Value;

    private sealed class ViewFactory
    {
        public List<FakeSkillsView> Created { get; } = [];

        public ISkillsOverlayView Create()
        {
            var view = new FakeSkillsView();
            Created.Add(view);
            return view;
        }
    }

    private sealed class FakeSkillsView : ISkillsOverlayView
    {
        public event EventHandler? Closed;

        public bool IsVisible { get; private set; }
        public bool WasClosed { get; private set; }
        public SkillsOverlayState? State { get; private set; }
        public OverlayWidgetPlacement? Placement { get; private set; }
        public OverlayTheme? Theme { get; private set; }
        public List<double> AimHistory { get; } = [];

        public void Apply(
            SkillsOverlayState state,
            OverlayWidgetPlacement placement,
            OverlayTheme theme)
        {
            State = state;
            Placement = placement;
            Theme = theme;
            double? aim = state.Metrics.FirstOrDefault(metric => metric.Name == "Aim")?.Value;
            if (aim is { } value)
            {
                AimHistory.Add(value);
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

    private sealed class ThrowingSkillsView : ISkillsOverlayView
    {
        public event EventHandler? Closed;

        public bool IsVisible => false;

        public void Apply(
            SkillsOverlayState state,
            OverlayWidgetPlacement placement,
            OverlayTheme theme) => throw new InvalidOperationException("Skills render failure.");

        public void Show() { }

        public void Hide() { }

        public void Close() => Closed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class MapViewFactory
    {
        public List<FakeMapView> Created { get; } = [];

        public IMapOverviewOverlayView Create()
        {
            var view = new FakeMapView();
            Created.Add(view);
            return view;
        }
    }

    private sealed class FakeMapView : IMapOverviewOverlayView
    {
        public event EventHandler? Closed;

        public bool IsVisible { get; private set; }
        public MapOverviewOverlayState? State { get; private set; }

        public void Apply(
            MapOverviewOverlayState state,
            OverlayWidgetPlacement placement,
            OverlayTheme theme) => State = state;

        public void Show() => IsVisible = true;

        public void Hide() => IsVisible = false;

        public void Close()
        {
            IsVisible = false;
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}
