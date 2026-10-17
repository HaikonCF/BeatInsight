using BeatInsight.Integration.Game;

namespace BeatInsight.Overlay.DifficultyTimeline;

public sealed record DifficultyTimelineOverlayState(
    bool HasContent,
    DifficultyTimelineModel? Model,
    double MarkerRatio,
    int? CurrentSectionIndex)
{
    public static DifficultyTimelineOverlayState Empty { get; } = new(
        false,
        null,
        0,
        null);
}

public static class DifficultyTimelineSnapshotProjector
{
    public static DifficultyTimelineOverlayState Project(OverlaySnapshot? snapshot)
    {
        if (snapshot is not
            {
                HasAvailableGame: true,
                GameMode: OverlayGameState.Gameplay,
                DifficultyTimeline: { } timeline
            } ||
            timeline.Model.Segments.Count == 0 ||
            !double.IsFinite(timeline.MarkerRatio) ||
            timeline.MarkerRatio is < 0 or > 1)
        {
            return DifficultyTimelineOverlayState.Empty;
        }

        return new(
            true,
            timeline.Model,
            timeline.MarkerRatio,
            timeline.CurrentSectionIndex);
    }
}
