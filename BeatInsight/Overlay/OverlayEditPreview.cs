using BeatInsight.Overlay.CurrentSection;
using BeatInsight.Overlay.GameplayHud;
using BeatInsight.Overlay.LivePatterns;
using BeatInsight.Overlay.MapOverview;
using BeatInsight.Overlay.Skills;
using BeatInsight.Overlay.UpNext;

namespace BeatInsight.Overlay;

public static class OverlayEditPreview
{
    public static MapOverviewOverlayState MapOverview { get; } = new(
        true,
        "Artist - Title",
        "Difficulty",
        "Artist",
        "Mapper",
        5.42,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null);

    public static SkillsOverlayState Skills { get; } = new(
        true,
        [
            new("Aim", 78),
            new("Speed", 65),
            new("Reading", 71)
        ]);

    public static GameplayHudOverlayState GameplayHud(GameplayHudDisplayOptions options)
    {
        var current = new CurrentSectionOverlayState(
            true, "Jump", "Aim", .42, TimeSpan.FromSeconds(8), "Jump / Aim");
        var next = new UpNextOverlayState(
            true, "Stream", "Stamina", TimeSpan.FromSeconds(8), "Stream / Stamina");
        var stats = new LiveGameplayStatsOverlayState(
            true, "2", "4", "0", "428x", "97.42%", "NM");
        var performance = new LivePerformanceOverlayState(
            true, "182pp", "214pp", "168pp", "246pp");
        var patterns = new LivePatternsOverlayState(true, null);

        return new(
            current,
            next,
            DifficultyTimeline.DifficultyTimelineOverlayState.Empty,
            patterns,
            stats,
            performance,
            options.ShowCurrentSection,
            options.ShowUpNext,
            false,
            options.ShowLivePatterns,
            options.ShowLiveStats,
            options.ShowPerformance);
    }
}
