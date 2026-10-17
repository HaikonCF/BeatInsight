using BeatInsight.Integration.Game;
using BeatInsight.Models.AnalysisCache;
using BeatInsight.Models.Identity;
using BeatInsight.Models.Sections;

namespace BeatInsight.Overlay;

public enum OverlayGameState
{
    Idle,
    SongSelect,
    Gameplay,
    Results,
    Unknown
}

public static class OverlayWidgetIds
{
    public const string MapOverview = "MapOverview";
    public const string Skills = "Skills";
    public const string GameplayHud = "GameplayHud";
    public const string Patterns = "Patterns";
    public const string DifficultyTimeline = "DifficultyTimeline";
    public const string CurrentSection = "CurrentSection";
    public const string UpNext = "UpNext";
}

public sealed record OverlayTimelineSnapshot(
    GameplaySectionV2Analysis? CurrentSection = null,
    GameplayIdentityV3Pattern? CurrentPattern = null,
    double? CurrentDifficulty = null,
    GameplaySectionV2Analysis? NextSection = null,
    GameplayIdentityV3Pattern? NextPattern = null,
    GameplaySectionV2Analysis? NextSpike = null);

/// <summary>
/// Provider-neutral input consumed by the overlay layer. All analysis data is the
/// existing cached/runtime snapshot; the overlay never parses or analyzes a map.
/// Live gameplay, section, and timeline data are composed before publication;
/// the overlay never reads a provider or resolves analysis itself.
/// </summary>
public sealed record OverlaySnapshot(
    GameStateSnapshot Game,
    OverlayGameState GameMode,
    BeatmapAnalysisSnapshot? Analysis = null,
    TimeSpan? CurrentPosition = null,
    OverlayTimelineSnapshot? Timeline = null,
    LiveGameplaySnapshot? LiveGameplay = null,
    CurrentSectionSnapshot? CurrentSection = null,
    NextSectionSnapshot? NextSection = null,
    DifficultyTimelineSnapshot? DifficultyTimeline = null,
    LivePatternsSnapshot? LivePatterns = null,
    LiveGameplayStatsSnapshot? LiveStats = null,
    LivePerformanceSnapshot? Performance = null)
{
    public bool HasAvailableGame => Game.IsAvailable;
}

public sealed record OverlayWidgetDefinition(
    string Id,
    string DisplayName,
    IReadOnlySet<OverlayGameState> SupportedStates,
    bool RequiresAnalysisSnapshot = false);

public sealed class OverlayWidgetRegistration
{
    private readonly Action<OverlaySnapshot?>? snapshotReceiver;
    private readonly Func<OverlaySnapshot, bool>? availability;

    public OverlayWidgetRegistration(
        OverlayWidgetDefinition definition,
        Action<OverlaySnapshot?>? snapshotReceiver = null,
        Func<OverlaySnapshot, bool>? availability = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.Id))
        {
            throw new ArgumentException("A stable widget ID is required.", nameof(definition));
        }

        Definition = definition;
        this.snapshotReceiver = snapshotReceiver;
        this.availability = availability;
    }

    public OverlayWidgetDefinition Definition { get; }

    public bool IsEnabled { get; set; } = true;

    public OverlaySnapshot? CurrentSnapshot { get; private set; }

    internal bool CanDisplay(OverlaySnapshot snapshot)
    {
        if (!IsEnabled || !Definition.SupportedStates.Contains(snapshot.GameMode))
        {
            return false;
        }

        if (Definition.RequiresAnalysisSnapshot && snapshot.Analysis is null)
        {
            return false;
        }

        try
        {
            return availability?.Invoke(snapshot) ?? true;
        }
        catch
        {
            return false;
        }
    }

    internal void Publish(OverlaySnapshot? snapshot)
    {
        CurrentSnapshot = snapshot;
        snapshotReceiver?.Invoke(snapshot);
    }
}

public static class OverlayWidgetCatalog
{
    public static IReadOnlyList<OverlayWidgetRegistration> CreateFoundationWidgets() =>
    [
        Widget(OverlayWidgetIds.MapOverview, "Map overview", true,
            OverlayGameState.SongSelect),
        Widget(OverlayWidgetIds.Skills, "Skills", true,
            OverlayGameState.SongSelect),
        Widget(OverlayWidgetIds.GameplayHud, "Gameplay HUD", true,
            OverlayGameState.Gameplay,
            OverlayGameState.Results)
    ];

    private static OverlayWidgetRegistration Widget(
        string id,
        string name,
        bool requiresAnalysis,
        params OverlayGameState[] states) =>
        new(new OverlayWidgetDefinition(
            id,
            name,
            new HashSet<OverlayGameState>(states),
            requiresAnalysis),
            availability: snapshot => snapshot.HasAvailableGame);
}
