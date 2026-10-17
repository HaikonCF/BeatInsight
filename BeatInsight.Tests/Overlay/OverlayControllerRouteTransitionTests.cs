using BeatInsight.Integration.Game;
using BeatInsight.Models.AnalysisCache;
using BeatInsight.Models.Sections;
using BeatInsight.Overlay;
using BeatInsight.Overlay.CurrentSection;
using BeatInsight.Overlay.MapOverview;
using BeatInsight.Overlay.Skills;
using BeatInsight.Tests.ViewModels;

namespace BeatInsight.Tests.Overlay;

public sealed class OverlayControllerRouteTransitionTests
{
    [Fact]
    public void SongSelectGameplayPauseGameplayResultsSongSelectRoutesEachWidget()
    {
        OverlayEngine engine = Engine();
        var mapView = new MapView();
        var skillsView = new SkillsView();
        var currentSectionView = new CurrentSectionView();
        using var mapController = new MapOverviewOverlayController(engine, () => mapView);
        using var skillsController = new SkillsOverlayController(engine, () => skillsView);
        using var currentSectionController = new CurrentSectionOverlayController(
            engine,
            () => currentSectionView);
        BeatmapAnalysisSnapshot analysis = Analysis("Map A");

        engine.PublishSnapshot(SongSelect(analysis));
        AssertVisibility(mapView, skillsView, currentSectionView, true, true, false);

        OverlaySnapshot gameplay = Gameplay(analysis, progress: 0.25);
        engine.PublishSnapshot(gameplay);
        AssertVisibility(mapView, skillsView, currentSectionView, false, false, true);
        Assert.Equal(MapOverviewOverlayState.Empty, mapView.State);
        Assert.Equal(SkillsOverlayState.Empty, skillsView.State);

        // A paused clock retains its last value and therefore remains on the
        // Gameplay route. SongSelect widgets must stay hidden.
        engine.PublishSnapshot(gameplay);
        Assert.Equal(OverlayGameState.Gameplay, engine.CurrentGameState);
        AssertVisibility(mapView, skillsView, currentSectionView, false, false, true);

        engine.PublishSnapshot(Gameplay(analysis, progress: 0.75));
        AssertVisibility(mapView, skillsView, currentSectionView, false, false, true);
        Assert.Equal(0.75, currentSectionView.State!.LocalProgress);

        engine.PublishSnapshot(Route(analysis, OverlayGameState.Results));
        AssertVisibility(mapView, skillsView, currentSectionView, false, false, false);
        Assert.Equal(CurrentSectionOverlayState.Empty, currentSectionView.State);

        engine.PublishSnapshot(SongSelect(analysis));
        AssertVisibility(mapView, skillsView, currentSectionView, true, true, false);
        Assert.True(mapView.State!.HasContent);
        Assert.True(skillsView.State!.HasContent);
        Assert.Equal(CurrentSectionOverlayState.Empty, currentSectionView.State);
    }

    [Fact]
    public void MapChangeClientExitAndRestartNeverReuseHiddenRouteState()
    {
        OverlayEngine engine = Engine();
        var mapView = new MapView();
        var skillsView = new SkillsView();
        var currentSectionView = new CurrentSectionView();
        using var mapController = new MapOverviewOverlayController(engine, () => mapView);
        using var skillsController = new SkillsOverlayController(engine, () => skillsView);
        using var currentSectionController = new CurrentSectionOverlayController(
            engine,
            () => currentSectionView);

        BeatmapAnalysisSnapshot mapA = Analysis("Map A");
        BeatmapAnalysisSnapshot mapB = Analysis("Map B");
        engine.PublishSnapshot(SongSelect(mapA));
        engine.PublishSnapshot(Gameplay(mapA, progress: 0.4));
        engine.PublishSnapshot(Route(mapA, OverlayGameState.Unknown));

        AssertVisibility(mapView, skillsView, currentSectionView, false, false, false);
        Assert.Equal(MapOverviewOverlayState.Empty, mapView.State);
        Assert.Equal(SkillsOverlayState.Empty, skillsView.State);
        Assert.Equal(CurrentSectionOverlayState.Empty, currentSectionView.State);

        engine.PublishSnapshot(SongSelect(mapB, processIdentity: "84@restart"));

        AssertVisibility(mapView, skillsView, currentSectionView, true, true, false);
        Assert.Equal("Map B", mapView.State!.Title);
        Assert.DoesNotContain("Map A", mapView.RenderedTitles[^1]);
    }

    [Fact]
    public void QuitBeforeFinishRoutesDirectlyBackToSongSelectWithoutResultsState()
    {
        OverlayEngine engine = Engine();
        var mapView = new MapView();
        var skillsView = new SkillsView();
        var currentSectionView = new CurrentSectionView();
        using var mapController = new MapOverviewOverlayController(engine, () => mapView);
        using var skillsController = new SkillsOverlayController(engine, () => skillsView);
        using var currentSectionController = new CurrentSectionOverlayController(
            engine,
            () => currentSectionView);
        BeatmapAnalysisSnapshot analysis = Analysis("Quit map");

        engine.PublishSnapshot(Gameplay(analysis, progress: 0.4));
        engine.PublishSnapshot(SongSelect(analysis));

        Assert.Equal(OverlayGameState.SongSelect, engine.CurrentGameState);
        AssertVisibility(mapView, skillsView, currentSectionView, true, true, false);
    }

    private static void AssertVisibility(
        MapView map,
        SkillsView skills,
        CurrentSectionView currentSection,
        bool mapVisible,
        bool skillsVisible,
        bool currentSectionVisible)
    {
        Assert.Equal(mapVisible, map.IsVisible);
        Assert.Equal(skillsVisible, skills.IsVisible);
        Assert.Equal(currentSectionVisible, currentSection.IsVisible);
    }

    private static OverlayEngine Engine()
    {
        var layout = new OverlayLayout(
            "RouteTransition",
            [
                Place(OverlayWidgetIds.MapOverview, 0),
                Place(OverlayWidgetIds.Skills, 1),
                Place(OverlayWidgetIds.CurrentSection, 2)
            ]);
        var engine = new OverlayEngine(
            layout,
            OverlayThemeCatalog.Get(OverlayThemeIds.Default));
        engine.SetForegroundState(true);
        return engine;
    }

    private static OverlayWidgetPlacement Place(string id, int zIndex) =>
        new(id, true, 0, 0, 360, 180, zIndex);

    private static OverlaySnapshot SongSelect(
        BeatmapAnalysisSnapshot analysis,
        string processIdentity = "42@generation") =>
        Route(analysis, OverlayGameState.SongSelect, processIdentity);

    private static OverlaySnapshot Route(
        BeatmapAnalysisSnapshot analysis,
        OverlayGameState route,
        string processIdentity = "42@generation") =>
        new(
            Game(analysis, processIdentity),
            route,
            analysis);

    private static OverlaySnapshot Gameplay(
        BeatmapAnalysisSnapshot analysis,
        double progress)
    {
        GameplaySectionV2Analysis source = analysis.SectionAnalysisV2.Sections[0];
        GameplaySectionV2Analysis section = source with
        {
            StartTime = 10_000,
            EndTime = 20_000
        };
        TimeSpan currentTime = TimeSpan.FromMilliseconds(
            section.StartTime + ((section.EndTime - section.StartTime) * progress));
        var live = new LiveGameplaySnapshot(
            true,
            currentTime,
            TimeSpan.FromMinutes(2),
            currentTime.TotalMilliseconds / TimeSpan.FromMinutes(2).TotalMilliseconds,
            analysis,
            123,
            456,
            "42@generation",
            DateTimeOffset.UtcNow);
        var currentSection = new CurrentSectionSnapshot(
            section,
            TimeSpan.FromMilliseconds(section.StartTime),
            TimeSpan.FromMilliseconds(section.EndTime),
            TimeSpan.FromMilliseconds(section.EndTime - section.StartTime),
            progress,
            TimeSpan.FromMilliseconds(
                (section.EndTime - section.StartTime) * (1 - progress)));
        return new(
            Game(analysis),
            OverlayGameState.Gameplay,
            analysis,
            currentTime,
            LiveGameplay: live,
            CurrentSection: currentSection);
    }

    private static BeatmapAnalysisSnapshot Analysis(string title)
    {
        BeatmapAnalysisSnapshot source = OverviewViewModelTests.BuildSnapshot();
        return source with
        {
            Metadata = source.Metadata with
            {
                Title = title,
                BeatmapId = 123,
                BeatmapSetId = 456,
                LengthTicks = TimeSpan.FromMinutes(2).Ticks
            }
        };
    }

    private static GameStateSnapshot Game(
        BeatmapAnalysisSnapshot analysis,
        string processIdentity = "42@generation") =>
        new(
            "test",
            "osu!lazer",
            42,
            processIdentity,
            $"{analysis.Metadata.Title}.osu",
            "123",
            "456",
            DateTimeOffset.UtcNow,
            GameStateAvailability.Available,
            null);

    private sealed class MapView : IMapOverviewOverlayView
    {
        public event EventHandler? Closed;
        public bool IsVisible { get; private set; }
        public MapOverviewOverlayState? State { get; private set; }
        public List<string> RenderedTitles { get; } = [];

        public void Apply(
            MapOverviewOverlayState state,
            OverlayWidgetPlacement placement,
            OverlayTheme theme)
        {
            State = state;
            if (state.Title is not null)
                RenderedTitles.Add(state.Title);
        }

        public void Show() => IsVisible = true;
        public void Hide() => IsVisible = false;
        public void Close()
        {
            IsVisible = false;
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class SkillsView : ISkillsOverlayView
    {
        public event EventHandler? Closed;
        public bool IsVisible { get; private set; }
        public SkillsOverlayState? State { get; private set; }

        public void Apply(
            SkillsOverlayState state,
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

    private sealed class CurrentSectionView : ICurrentSectionOverlayView
    {
        public event EventHandler? Closed;
        public bool IsVisible { get; private set; }
        public CurrentSectionOverlayState? State { get; private set; }

        public void Apply(
            CurrentSectionOverlayState state,
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
