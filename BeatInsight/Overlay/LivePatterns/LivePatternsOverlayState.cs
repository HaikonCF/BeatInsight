using BeatInsight.Integration.Game;

namespace BeatInsight.Overlay.LivePatterns;

public sealed record LivePatternsOverlayState(
    bool HasContent,
    LivePatternsSnapshot? Snapshot)
{
    public bool HasDominantPatterns => Snapshot?.Patterns.Count > 0;

    public static LivePatternsOverlayState Empty { get; } = new(false, null);
}

public static class LivePatternsSnapshotProjector
{
    public static LivePatternsOverlayState Project(OverlaySnapshot? snapshot) =>
        snapshot is
        {
            HasAvailableGame: true,
            GameMode: OverlayGameState.Gameplay,
            LivePatterns: { } patterns
        }
            ? new(true, patterns)
            : LivePatternsOverlayState.Empty;
}
