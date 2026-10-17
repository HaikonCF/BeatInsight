using BeatInsight.Integration.Game;

namespace BeatInsight.Overlay.UpNext;

public sealed record UpNextOverlayState(
    bool HasContent,
    string? DominantStructure,
    string? SecondaryStructure,
    TimeSpan TimeUntilStart,
    string? Summary)
{
    public static UpNextOverlayState Empty { get; } = new(
        false, null, null, TimeSpan.Zero, null);
}

public static class NextSectionSnapshotProjector
{
    public static UpNextOverlayState Project(OverlaySnapshot? snapshot)
    {
        if (snapshot is not
            {
                HasAvailableGame: true,
                GameMode: OverlayGameState.Gameplay,
                NextSection: { } section
            } ||
            !double.IsFinite(section.TimeUntilStart.TotalMilliseconds) ||
            section.TimeUntilStart <= TimeSpan.Zero)
        {
            return UpNextOverlayState.Empty;
        }

        string dominant = section.DominantStructure.ToString();
        string? secondary = section.SecondaryStructure is { } secondaryStructure &&
                            secondaryStructure != section.DominantStructure
            ? secondaryStructure.ToString()
            : null;
        string? summary = string.IsNullOrWhiteSpace(section.Summary)
            ? null
            : section.Summary.Trim();

        return new(
            true,
            dominant,
            secondary,
            section.TimeUntilStart,
            summary);
    }
}
