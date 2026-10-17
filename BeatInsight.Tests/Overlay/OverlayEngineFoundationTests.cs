using System.IO;
using System.Text.Json;
using BeatInsight.Integration.Game;
using BeatInsight.Overlay;
using BeatInsight.Tests.ViewModels;

namespace BeatInsight.Tests.Overlay;

public sealed class OverlayEngineFoundationTests
{
    [Fact]
    public void RegistrationLookupAndRemovalUseStableId()
    {
        var engine = EngineWith(Layout("test", Place("map", 0)));
        OverlayWidgetRegistration widget = Widget("map", OverlayGameState.Gameplay);

        engine.Register(widget);

        Assert.True(engine.TryGetWidget("map", out OverlayWidgetRegistration? found));
        Assert.Same(widget, found);
        Assert.True(engine.Unregister("map"));
        Assert.False(engine.TryGetWidget("map", out _));
        Assert.False(engine.Unregister("map"));
    }

    [Fact]
    public void DuplicateWidgetIdIsRejected()
    {
        var engine = new OverlayEngine();
        engine.Register(Widget("map", OverlayGameState.Gameplay));

        Assert.Throws<InvalidOperationException>(
            () => engine.Register(Widget("map", OverlayGameState.Gameplay)));
    }

    [Fact]
    public void StateFilteringSupportsMultipleIndependentWidgets()
    {
        var engine = EngineWith(Layout("test", Place("map", 1), Place("timeline", 2)));
        engine.Register(Widget("map", OverlayGameState.SongSelect, OverlayGameState.Gameplay));
        engine.Register(Widget("timeline", OverlayGameState.Gameplay));

        engine.PublishSnapshot(Snapshot(OverlayGameState.SongSelect));
        Assert.Equal(["map"], VisibleIds(engine));

        engine.PublishSnapshot(Snapshot(OverlayGameState.Gameplay));
        Assert.Equal(["map", "timeline"], VisibleIds(engine));
    }

    [Fact]
    public void SnapshotIsPropagatedAndNullClearsEveryWidget()
    {
        var received = new List<OverlaySnapshot?>();
        var widget = new OverlayWidgetRegistration(
            Definition("map", OverlayGameState.Gameplay),
            received.Add);
        var engine = EngineWith(Layout("test", Place("map", 0)));
        engine.Register(widget);
        OverlaySnapshot snapshot = Snapshot(OverlayGameState.Gameplay);

        engine.PublishSnapshot(snapshot);
        engine.PublishSnapshot(null);

        Assert.Same(snapshot, received[^2]);
        Assert.Null(received[^1]);
        Assert.Null(engine.CurrentSnapshot);
        Assert.Null(widget.CurrentSnapshot);
        Assert.Empty(engine.GetVisibleWidgets());
    }

    [Fact]
    public void PartialSnapshotHidesOnlyWidgetsThatRequireAnalysis()
    {
        var engine = EngineWith(Layout("test", Place("status", 0), Place("analysis", 1)));
        engine.Register(Widget("status", OverlayGameState.Gameplay));
        engine.Register(new OverlayWidgetRegistration(
            Definition("analysis", true, OverlayGameState.Gameplay)));

        engine.PublishSnapshot(Snapshot(OverlayGameState.Gameplay));

        Assert.Equal(["status"], VisibleIds(engine));
    }

    [Fact]
    public void FaultedWidgetDoesNotPreventOtherWidgets()
    {
        var engine = EngineWith(Layout("test", Place("broken", 0), Place("healthy", 1)));
        engine.Register(new OverlayWidgetRegistration(
            Definition("broken", OverlayGameState.Gameplay),
            _ => throw new InvalidOperationException()));
        engine.Register(Widget("healthy", OverlayGameState.Gameplay));

        engine.PublishSnapshot(Snapshot(OverlayGameState.Gameplay));

        Assert.Equal(["healthy"], VisibleIds(engine));
    }

    [Fact]
    public void VisibilityAndZIndexControlResolvedOrder()
    {
        var engine = EngineWith(Layout(
            "test",
            Place("top", 20),
            Place("hidden", 0, false),
            Place("bottom", -2)));
        engine.Register(Widget("top", OverlayGameState.Gameplay));
        engine.Register(Widget("hidden", OverlayGameState.Gameplay));
        engine.Register(Widget("bottom", OverlayGameState.Gameplay));
        engine.PublishSnapshot(Snapshot(OverlayGameState.Gameplay));

        Assert.Equal(["bottom", "top"], VisibleIds(engine));
    }

    [Fact]
    public void UnknownLayoutWidgetIsIgnored()
    {
        var engine = EngineWith(Layout("test", Place("known", 0), Place("future", 1)));
        engine.Register(Widget("known", OverlayGameState.Gameplay));
        engine.PublishSnapshot(Snapshot(OverlayGameState.Gameplay));

        Assert.Equal(["known"], VisibleIds(engine));
    }

    [Fact]
    public void ValidLayoutJsonLoadsWithoutThemeData()
    {
        const string json = """
            {
              "id": "Custom.Test",
              "widgets": [
                { "widgetId": "MapOverview", "isVisible": true, "x": 10, "y": 20,
                  "width": 300, "height": 120, "zIndex": 3,
                  "configuration": { "density": "compact" } }
              ]
            }
            """;

        OverlayLoadResult<OverlayLayout> result = OverlayLayoutResolver.ResolveJson(json);

        Assert.False(result.UsedFallback);
        Assert.Equal("Custom.Test", result.Value.Id);
        Assert.Equal(3, result.Value.Widgets[0].ZIndex);
        Assert.Equal("compact", result.Value.Widgets[0].Configuration!["density"].GetString());
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"id\":\"bad\",\"widgets\":[{\"widgetId\":\"x\",\"isVisible\":true,\"x\":0,\"y\":0,\"width\":0,\"height\":10,\"zIndex\":0}]}")]
    [InlineData("{\"id\":\"bad\",\"widgets\":[{\"widgetId\":\"x\",\"isVisible\":true,\"x\":0,\"y\":0,\"width\":10,\"height\":10,\"zIndex\":0},{\"widgetId\":\"x\",\"isVisible\":true,\"x\":0,\"y\":0,\"width\":10,\"height\":10,\"zIndex\":1}]}")]
    public void InvalidLayoutFallsBackToMinimal(string json)
    {
        OverlayLoadResult<OverlayLayout> result = OverlayLayoutResolver.ResolveJson(json);

        Assert.True(result.UsedFallback);
        Assert.Equal(OverlayLayoutIds.Minimal, result.Value.Id);
        Assert.False(string.IsNullOrWhiteSpace(result.Diagnostic));
    }

    [Fact]
    public void OfficialLayoutAndWidgetIdsAreRegistered()
    {
        Assert.Equal(
            [OverlayLayoutIds.Minimal, OverlayLayoutIds.Compact, OverlayLayoutIds.Analysis,
             OverlayLayoutIds.Timeline, OverlayLayoutIds.Custom],
            OverlayLayoutCatalog.Ids);
        Assert.Equal(
            [OverlayWidgetIds.MapOverview, OverlayWidgetIds.Skills, OverlayWidgetIds.GameplayHud],
            OverlayWidgetCatalog.CreateFoundationWidgets().Select(widget => widget.Definition.Id));
    }

    [Fact]
    public void ValidThemeJsonLoadsDataOnlyTokensAndAssets()
    {
        const string json = """
            {
              "id": "Custom.Neon",
              "tokens": { "colors": { "accent.primary": "#00FFFF" } },
              "assets": { "logo": "assets/logo.png" }
            }
            """;

        OverlayLoadResult<OverlayTheme> result = OverlayThemeResolver.ResolveJson(json);

        Assert.False(result.UsedFallback);
        Assert.Equal("Custom.Neon", result.Value.Id);
        Assert.Equal("#00FFFF", result.Value.Tokens.Colors["accent.primary"]);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"id\":\"Unsafe\",\"tokens\":{},\"assets\":{\"logo\":\"../secret.png\"}}")]
    public void InvalidThemeFallsBackToDefault(string json)
    {
        OverlayLoadResult<OverlayTheme> result = OverlayThemeResolver.ResolveJson(json);

        Assert.True(result.UsedFallback);
        Assert.Equal(OverlayThemeIds.Default, result.Value.Id);
    }

    [Fact]
    public void DefaultAndBiaThemesAreIndependentAndBiaAssetIsOptional()
    {
        OverlayTheme defaultTheme = OverlayThemeCatalog.Get(OverlayThemeIds.Default);
        OverlayTheme biaTheme = OverlayThemeCatalog.Get(OverlayThemeIds.Bia);

        Assert.NotSame(defaultTheme, biaTheme);
        Assert.Empty(defaultTheme.Assets);
        Assert.Contains("mascot.idle", biaTheme.Assets.Keys);
    }

    [Fact]
    public void AppThemeAndOverlayThemeSelectionsAreIndependent()
    {
        var selection = new ThemeSelection("App.HighContrast", OverlayThemeIds.Bia);
        var engine = new OverlayEngine();

        engine.ApplyTheme(OverlayThemeCatalog.Get(selection.OverlayThemeId));

        Assert.Equal("App.HighContrast", selection.AppThemeId);
        Assert.Equal(OverlayThemeIds.Bia, engine.CurrentTheme.Id);
    }

    [Fact]
    public void MissingCustomAssetFallsBackToDefaultAsset()
    {
        string root = Path.Combine(Path.GetTempPath(), $"BeatInsightOverlay-{Guid.NewGuid():N}");
        string customRoot = Path.Combine(root, "custom");
        string defaultRoot = Path.Combine(root, "default");
        Directory.CreateDirectory(customRoot);
        Directory.CreateDirectory(Path.Combine(defaultRoot, "assets"));
        string fallbackAsset = Path.Combine(defaultRoot, "assets", "logo.png");
        File.WriteAllText(fallbackAsset, "test");
        try
        {
            OverlayTheme custom = Theme("Custom", ("logo", "assets/missing.png"));
            OverlayTheme fallback = Theme("Default", ("logo", "assets/logo.png"));

            string? resolved = OverlayThemeAssetResolver.Resolve(
                "logo", custom, customRoot, fallback, defaultRoot);

            Assert.Equal(fallbackAsset, resolved);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MissingAssetInBothThemesReturnsNull()
    {
        string root = Path.Combine(Path.GetTempPath(), $"BeatInsightOverlay-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            Assert.Null(OverlayThemeAssetResolver.Resolve(
                "mascot",
                Theme("Custom", ("mascot", "assets/missing.png")),
                root,
                OverlayThemeCatalog.Get(OverlayThemeIds.Default),
                root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void FoundationWidgetsFailClosedWithoutAnalysisOrAvailableGame()
    {
        var engine = new OverlayEngine(OverlayLayoutCatalog.Get(OverlayLayoutIds.Analysis));
        foreach (OverlayWidgetRegistration widget in OverlayWidgetCatalog.CreateFoundationWidgets())
        {
            engine.Register(widget);
        }

        engine.PublishSnapshot(Snapshot(
            OverlayGameState.SongSelect,
            available: false,
            analysis: OverviewViewModelTests.BuildSnapshot()));

        Assert.Empty(engine.GetVisibleWidgets());
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void DisplayAndForegroundGatesCombineGlobally(
        bool displaySupported,
        bool osuForeground,
        bool expectedVisible)
    {
        var engine = new OverlayEngine(Layout("test", Place("map", 0)));
        engine.Register(Widget("map", OverlayGameState.Gameplay));
        engine.PublishSnapshot(Snapshot(OverlayGameState.Gameplay));
        engine.SetDisplayCompatibility(displaySupported
            ? OverlayDisplayCompatibility.Supported(OverlayDisplayMode.Borderless)
            : OverlayDisplayCompatibility.FullscreenExclusive);
        engine.SetForegroundState(osuForeground);

        Assert.Equal(expectedVisible, engine.OverlayVisible);
        Assert.Equal(expectedVisible, engine.GetVisibleWidgets().Count == 1);
    }

    private static OverlayEngine EngineWith(OverlayLayout layout)
    {
        var engine = new OverlayEngine(layout);
        engine.SetForegroundState(true);
        return engine;
    }

    private static OverlayWidgetRegistration Widget(string id, params OverlayGameState[] states) =>
        new(Definition(id, states));

    private static OverlayWidgetDefinition Definition(string id, params OverlayGameState[] states) =>
        Definition(id, false, states);

    private static OverlayWidgetDefinition Definition(
        string id,
        bool requiresAnalysis,
        params OverlayGameState[] states) =>
        new(id, id, new HashSet<OverlayGameState>(states), requiresAnalysis);

    private static OverlayLayout Layout(string id, params OverlayWidgetPlacement[] placements) => new(id, placements);

    private static OverlayWidgetPlacement Place(string id, int zIndex, bool visible = true) =>
        new(id, visible, 0, 0, 100, 100, zIndex);

    private static OverlaySnapshot Snapshot(
        OverlayGameState state,
        bool available = true,
        BeatInsight.Models.AnalysisCache.BeatmapAnalysisSnapshot? analysis = null) =>
        new(
            new GameStateSnapshot(
                "test",
                available ? "osu!" : null,
                available ? 42 : null,
                available ? "process" : null,
                available ? "map.osu" : null,
                available ? "1" : null,
                available ? "2" : null,
                DateTimeOffset.UtcNow,
                available ? GameStateAvailability.Available : GameStateAvailability.NoProcess,
                available ? null : "No client."),
            state,
            analysis);

    private static string[] VisibleIds(OverlayEngine engine) =>
        engine.GetVisibleWidgets()
            .Select(item => item.Registration.Definition.Id)
            .ToArray();

    private static OverlayTheme Theme(string id, params (string Key, string Path)[] assets) =>
        new(
            id,
            new OverlayThemeTokens(),
            assets.ToDictionary(item => item.Key, item => item.Path, StringComparer.Ordinal));
}
