using BeatInsight.Models.Fusion;
using BeatInsight.Models.Sections;

namespace BeatInsight.Integration.Game;

/// <summary>
/// Provider-neutral projection of the first future playable analyzer section.
/// The analyzer-owned section is reused by reference.
/// </summary>
public sealed record NextSectionSnapshot(
    GameplaySectionV2Analysis Section,
    TimeSpan StartTime,
    TimeSpan EndTime,
    TimeSpan Duration,
    TimeSpan TimeUntilStart)
{
    public int Index => Section.SectionIndex;
    public GameplayStructureKind DominantStructure => Section.DominantStructure;
    public GameplayStructureKind? SecondaryStructure => Section.SecondaryStructure;
    public string Summary => Section.Summary;
}

/// <summary>
/// Stateless resolver for the first playable section whose start is strictly
/// after the live clock. Invalid, unordered, or overlapping timelines fail closed.
/// </summary>
public static class NextSectionResolver
{
    public static NextSectionSnapshot? Resolve(LiveGameplaySnapshot? live)
    {
        if (live is null || !live.IsGameplay || live.BeatmapAnalysis is null)
            return null;

        double currentMilliseconds = live.CurrentTime.TotalMilliseconds;
        if (!double.IsFinite(currentMilliseconds) || currentMilliseconds < 0)
            return null;

        IReadOnlyList<GameplaySectionV2Analysis>? sections =
            live.BeatmapAnalysis.SectionAnalysisV2?.Sections;
        if (sections is null || sections.Count == 0)
            return null;

        GameplaySectionV2Analysis? next = null;
        double previousEndTime = double.NegativeInfinity;
        foreach (GameplaySectionV2Analysis? section in sections)
        {
            if (section is null ||
                section.ObjectIndices is null ||
                !double.IsFinite(section.StartTime) ||
                !double.IsFinite(section.EndTime) ||
                section.StartTime < 0 ||
                section.EndTime <= section.StartTime ||
                section.StartTime < previousEndTime)
            {
                return null;
            }

            previousEndTime = section.EndTime;
            if (next is null &&
                section.StartTime > currentMilliseconds &&
                section.ObjectCount > 0)
            {
                next = section;
            }
        }

        if (next is null)
            return null;

        double durationMilliseconds = next.EndTime - next.StartTime;
        double untilStartMilliseconds = next.StartTime - currentMilliseconds;
        if (!double.IsFinite(durationMilliseconds) ||
            !double.IsFinite(untilStartMilliseconds) ||
            durationMilliseconds <= 0 ||
            untilStartMilliseconds <= 0)
        {
            return null;
        }

        try
        {
            return new(
                next,
                TimeSpan.FromMilliseconds(next.StartTime),
                TimeSpan.FromMilliseconds(next.EndTime),
                TimeSpan.FromMilliseconds(durationMilliseconds),
                TimeSpan.FromMilliseconds(untilStartMilliseconds));
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
