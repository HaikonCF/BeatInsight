using BeatInsight.Overlay.CurrentSection;
using BeatInsight.Overlay.DifficultyTimeline;
using BeatInsight.Overlay.LivePatterns;
using BeatInsight.Overlay.UpNext;

namespace BeatInsight.Overlay.GameplayHud;

public sealed record GameplayHudOverlayState(
    CurrentSectionOverlayState CurrentSection,
    UpNextOverlayState UpNext,
    DifficultyTimelineOverlayState DifficultyTimeline,
    LivePatternsOverlayState LivePatterns,
    LiveGameplayStatsOverlayState LiveStats,
    LivePerformanceOverlayState Performance,
    bool IsCurrentSectionVisible,
    bool IsUpNextVisible,
    bool IsDifficultyTimelineVisible,
    bool IsLivePatternsVisible,
    bool IsLiveStatsVisible,
    bool IsPerformanceVisible)
{
    public bool HasContent =>
        IsCurrentSectionVisible ||
        IsUpNextVisible ||
        IsDifficultyTimelineVisible ||
        IsLivePatternsVisible ||
        IsLiveStatsVisible ||
        IsPerformanceVisible;

    public static GameplayHudOverlayState Empty { get; } = new(
        CurrentSectionOverlayState.Empty,
        UpNextOverlayState.Empty,
        DifficultyTimelineOverlayState.Empty,
        LivePatternsOverlayState.Empty,
        LiveGameplayStatsOverlayState.Empty,
        LivePerformanceOverlayState.Empty,
        false,
        false,
        false,
        false,
        false,
        false);
}

public static class GameplayHudSnapshotProjector
{
    public static GameplayHudOverlayState Project(
        OverlaySnapshot? snapshot,
        GameplayHudDisplayOptions? options = null)
    {
        options ??= GameplayHudDisplayOptions.Default;

        CurrentSectionOverlayState current = options.ShowCurrentSection
            ? CurrentSectionSnapshotProjector.Project(snapshot)
            : CurrentSectionOverlayState.Empty;
        UpNextOverlayState next = options.ShowUpNext
            ? NextSectionSnapshotProjector.Project(snapshot)
            : UpNextOverlayState.Empty;
        DifficultyTimelineOverlayState timeline = options.ShowDifficultyTimeline
            ? DifficultyTimelineSnapshotProjector.Project(snapshot)
            : DifficultyTimelineOverlayState.Empty;
        LivePatternsOverlayState patterns = options.ShowLivePatterns
            ? LivePatternsSnapshotProjector.Project(snapshot)
            : LivePatternsOverlayState.Empty;
        LiveGameplayStatsOverlayState stats = options.ShowLiveStats
            ? LiveGameplayStatsOverlayState.Project(snapshot)
            : LiveGameplayStatsOverlayState.Empty;
        LivePerformanceOverlayState performance = options.ShowPerformance
            ? LivePerformanceOverlayState.Project(snapshot)
            : LivePerformanceOverlayState.Empty;

        return new(
            current,
            next,
            timeline,
            patterns,
            stats,
            performance,
            current.HasContent,
            next.HasContent,
            timeline.HasContent,
            patterns.HasContent,
            stats.HasContent,
            performance.HasContent);
    }
}
