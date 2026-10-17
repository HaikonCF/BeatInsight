using BeatInsight.Integration.Game;
using BeatInsight.Overlay;
using BeatInsight.Tests.ViewModels;

namespace BeatInsight.Tests.Overlay;

public sealed class OverlayGameStateRoutingTests
{
    [Theory]
    [InlineData(OverlayGameState.SongSelect, "MapOverview")]
    [InlineData(OverlayGameState.Gameplay, "GameplayOnly")]
    [InlineData(OverlayGameState.Results, "")]
    [InlineData(OverlayGameState.Unknown, "")]
    public void WidgetPoliciesFilterEachGameState(
        OverlayGameState gameState,
        string expectedIds)
    {
        OverlayEngine engine = CreateEngine();

        engine.PublishSnapshot(Snapshot(gameState));

        Assert.Equal(ParseIds(expectedIds), VisibleIds(engine));
    }

    [Theory]
    [InlineData(false, true, OverlayGameState.Gameplay, false)]
    [InlineData(true, false, OverlayGameState.Gameplay, false)]
    [InlineData(true, true, OverlayGameState.SongSelect, false)]
    [InlineData(true, true, OverlayGameState.Gameplay, true)]
    public void DisplayForegroundAndGameStateGatesCompose(
        bool displaySupported,
        bool osuForeground,
        OverlayGameState gameState,
        bool expectedGameplayWidgetVisible)
    {
        OverlayEngine engine = CreateEngine();
        engine.SetDisplayCompatibility(displaySupported
            ? OverlayDisplayCompatibility.Supported(OverlayDisplayMode.Borderless)
            : OverlayDisplayCompatibility.FullscreenExclusive);
        engine.SetForegroundState(osuForeground);

        engine.PublishSnapshot(Snapshot(gameState));

        Assert.Equal(
            expectedGameplayWidgetVisible,
            VisibleIds(engine).Contains("GameplayOnly", StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(OverlayGameState.Idle, OverlayGameState.SongSelect, "", "MapOverview")]
    [InlineData(OverlayGameState.SongSelect, OverlayGameState.Gameplay, "MapOverview", "GameplayOnly")]
    [InlineData(OverlayGameState.Gameplay, OverlayGameState.Results, "GameplayOnly", "")]
    [InlineData(OverlayGameState.Results, OverlayGameState.SongSelect, "", "MapOverview")]
    [InlineData(OverlayGameState.Gameplay, OverlayGameState.Idle, "GameplayOnly", "")]
    [InlineData(OverlayGameState.Unknown, OverlayGameState.Gameplay, "", "GameplayOnly")]
    [InlineData(OverlayGameState.Gameplay, OverlayGameState.Unknown, "GameplayOnly", "")]
    public void StateTransitionsRefreshOnlyAllowedWidgets(
        OverlayGameState initialState,
        OverlayGameState nextState,
        string expectedBefore,
        string expectedAfter)
    {
        OverlayEngine engine = CreateEngine();
        engine.PublishSnapshot(Snapshot(initialState));
        Assert.Equal(ParseIds(expectedBefore), VisibleIds(engine));

        Assert.True(engine.SetGameState(nextState));

        Assert.Equal(nextState, engine.CurrentGameState);
        Assert.Equal(ParseIds(expectedAfter), VisibleIds(engine));
    }

    [Fact]
    public void StateTransitionPreservesSnapshotPayload()
    {
        OverlayEngine engine = CreateEngine();
        OverlayTimelineSnapshot timeline = new(CurrentDifficulty: 6.25);
        var analysis = OverviewViewModelTests.BuildSnapshot();
        OverlaySnapshot snapshot = Snapshot(
            OverlayGameState.SongSelect,
            analysis,
            currentPosition: TimeSpan.FromSeconds(37),
            timeline: timeline);
        engine.PublishSnapshot(snapshot);

        engine.SetGameState(OverlayGameState.Gameplay);

        OverlaySnapshot routed = Assert.IsType<OverlaySnapshot>(engine.CurrentSnapshot);
        Assert.Equal(OverlayGameState.Gameplay, routed.GameMode);
        Assert.Same(snapshot.Game, routed.Game);
        Assert.Same(snapshot.Analysis, routed.Analysis);
        Assert.Equal(snapshot.CurrentPosition, routed.CurrentPosition);
        Assert.Same(timeline, routed.Timeline);
    }

    [Fact]
    public void StateRoutingDoesNotRequestAnotherAnalysis()
    {
        int analysisRequests = 0;
        OverlaySnapshot CreateAnalyzedSnapshot()
        {
            analysisRequests++;
            return Snapshot(
                OverlayGameState.SongSelect,
                OverviewViewModelTests.BuildSnapshot());
        }

        OverlayEngine engine = CreateEngine();
        engine.PublishSnapshot(CreateAnalyzedSnapshot());

        engine.SetGameState(OverlayGameState.Gameplay);
        engine.SetGameState(OverlayGameState.Results);
        engine.SetGameState(OverlayGameState.Unknown);

        Assert.Equal(1, analysisRequests);
    }

    [Fact]
    public void UnknownCanBeEnabledOnlyByExplicitWidgetPolicy()
    {
        OverlayEngine engine = CreateEngine(includeUnknownWidget: true);

        engine.PublishSnapshot(Snapshot(OverlayGameState.Unknown));

        Assert.Equal(["UnknownStatus"], VisibleIds(engine));
    }

    [Fact]
    public void FoundationCatalogDeclaresExistingAndFuturePoliciesExplicitly()
    {
        IReadOnlyDictionary<string, OverlayWidgetDefinition> definitions =
            OverlayWidgetCatalog.CreateFoundationWidgets()
                .ToDictionary(widget => widget.Definition.Id, widget => widget.Definition);

        Assert.Equal(
            States(OverlayGameState.SongSelect),
            definitions[OverlayWidgetIds.MapOverview].SupportedStates);
        Assert.Equal(
            States(OverlayGameState.SongSelect),
            definitions[OverlayWidgetIds.Skills].SupportedStates);
        Assert.Equal(
            States(OverlayGameState.Gameplay, OverlayGameState.Results),
            definitions[OverlayWidgetIds.GameplayHud].SupportedStates);
        Assert.DoesNotContain(OverlayWidgetIds.Patterns, definitions.Keys);
        Assert.DoesNotContain(OverlayWidgetIds.DifficultyTimeline, definitions.Keys);
        Assert.DoesNotContain(OverlayWidgetIds.CurrentSection, definitions.Keys);
        Assert.DoesNotContain(OverlayWidgetIds.UpNext, definitions.Keys);
    }

    private static OverlayEngine CreateEngine(bool includeUnknownWidget = false)
    {
        var placements = new List<OverlayWidgetPlacement>
        {
            Place("MapOverview", 1),
            Place("GameplayOnly", 0)
        };
        if (includeUnknownWidget)
        {
            placements.Add(Place("UnknownStatus", 2));
        }

        var engine = new OverlayEngine(new OverlayLayout("routing", placements));
        engine.Register(Widget(
            "MapOverview",
            OverlayGameState.SongSelect));
        engine.Register(Widget("GameplayOnly", OverlayGameState.Gameplay));
        if (includeUnknownWidget)
        {
            engine.Register(Widget("UnknownStatus", OverlayGameState.Unknown));
        }

        engine.SetForegroundState(true);
        return engine;
    }

    private static OverlayWidgetRegistration Widget(string id, params OverlayGameState[] states) =>
        new(new OverlayWidgetDefinition(id, id, States(states)));

    private static HashSet<OverlayGameState> States(params OverlayGameState[] states) => new(states);

    private static OverlayWidgetPlacement Place(string id, int zIndex) =>
        new(id, true, 0, 0, 100, 100, zIndex);

    private static OverlaySnapshot Snapshot(
        OverlayGameState state,
        BeatInsight.Models.AnalysisCache.BeatmapAnalysisSnapshot? analysis = null,
        TimeSpan? currentPosition = null,
        OverlayTimelineSnapshot? timeline = null) =>
        new(
            new GameStateSnapshot(
                "test",
                "osu!",
                42,
                "process",
                "map.osu",
                "1",
                "2",
                DateTimeOffset.UtcNow,
                GameStateAvailability.Available,
                null),
            state,
            analysis,
            CurrentPosition: currentPosition,
            Timeline: timeline);

    private static string[] ParseIds(string ids) =>
        string.IsNullOrEmpty(ids) ? [] : ids.Split(',');

    private static string[] VisibleIds(OverlayEngine engine) =>
        engine.GetVisibleWidgets()
            .Select(item => item.Registration.Definition.Id)
            .ToArray();
}
