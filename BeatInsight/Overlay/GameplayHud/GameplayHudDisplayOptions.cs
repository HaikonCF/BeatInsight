namespace BeatInsight.Overlay.GameplayHud;

public sealed record GameplayHudDisplayOptions(
    bool ShowCurrentSection = true,
    bool ShowUpNext = true,
    bool ShowDifficultyTimeline = true,
    bool ShowLivePatterns = true,
    bool ShowLiveStats = true,
    bool ShowPerformance = true)
{
    public static GameplayHudDisplayOptions Default { get; } = new();
}
