using BeatInsight.Integration.Game;

namespace BeatInsight.Overlay.CurrentSection;

public sealed record CurrentSectionOverlayState(
    bool HasContent,
    string? DominantStructure,
    string? SecondaryStructure,
    double LocalProgress,
    TimeSpan TimeRemaining,
    string? Summary)
{
    public static CurrentSectionOverlayState Empty { get; } = new(
        false,
        null,
        null,
        0,
        TimeSpan.Zero,
        null);
}

public static class CurrentSectionSnapshotProjector
{
    public static CurrentSectionOverlayState Project(OverlaySnapshot? snapshot)
    {
        if (snapshot is not
            {
                HasAvailableGame: true,
                GameMode: OverlayGameState.Gameplay,
                CurrentSection: { } section
            } ||
            !double.IsFinite(section.LocalProgress) ||
            section.LocalProgress is < 0 or >= 1 ||
            section.TimeRemaining <= TimeSpan.Zero)
        {
            return CurrentSectionOverlayState.Empty;
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
            section.LocalProgress,
            section.TimeRemaining,
            summary);
    }
}
