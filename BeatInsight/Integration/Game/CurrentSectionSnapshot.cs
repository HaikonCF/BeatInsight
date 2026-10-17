using BeatInsight.Models.Fusion;
using BeatInsight.Models.Sections;

namespace BeatInsight.Integration.Game;

/// <summary>
/// Provider-neutral projection of the one analyzed section containing the
/// current gameplay time. The analyzer-owned section is reused by reference.
/// </summary>
public sealed record CurrentSectionSnapshot(
    GameplaySectionV2Analysis Section,
    TimeSpan StartTime,
    TimeSpan EndTime,
    TimeSpan Duration,
    double LocalProgress,
    TimeSpan TimeRemaining)
{
    public int SectionIndex => Section.SectionIndex;
    public GameplayStructureKind DominantStructure => Section.DominantStructure;
    public GameplayStructureKind? SecondaryStructure => Section.SecondaryStructure;
    public string Summary => Section.Summary;
}

/// <summary>
/// Stateless current-section resolution over analyzer-owned half-open ranges:
/// [StartTime, EndTime). A gap has no section and multiple matching ranges are
/// ambiguous, so both cases fail closed.
/// </summary>
public static class CurrentSectionResolver
{
    public static CurrentSectionSnapshot? Resolve(LiveGameplaySnapshot? live)
    {
        if (live is null || !live.IsGameplay || live.BeatmapAnalysis is null)
            return null;

        double currentMilliseconds = live.CurrentTime.TotalMilliseconds;
        if (!double.IsFinite(currentMilliseconds) ||
            currentMilliseconds < 0)
        {
            return null;
        }

        IReadOnlyList<GameplaySectionV2Analysis>? sections =
            live.BeatmapAnalysis.SectionAnalysisV2?.Sections;
        if (sections is null || sections.Count == 0)
            return null;

        GameplaySectionV2Analysis? active = null;
        double previousEndTime = double.NegativeInfinity;
        foreach (GameplaySectionV2Analysis? section in sections)
        {
            if (section is null ||
                !double.IsFinite(section.StartTime) ||
                !double.IsFinite(section.EndTime) ||
                section.StartTime < 0 ||
                section.EndTime <= section.StartTime ||
                section.StartTime < previousEndTime)
            {
                return null;
            }
            previousEndTime = section.EndTime;

            if (currentMilliseconds < section.StartTime ||
                currentMilliseconds >= section.EndTime)
            {
                continue;
            }

            if (active is not null)
                return null;

            active = section;
        }

        if (active is null)
            return null;

        double durationMilliseconds = active.EndTime - active.StartTime;
        double elapsedMilliseconds = currentMilliseconds - active.StartTime;
        double localProgress = elapsedMilliseconds / durationMilliseconds;
        double remainingMilliseconds = active.EndTime - currentMilliseconds;
        if (!double.IsFinite(localProgress) ||
            localProgress is < 0 or >= 1 ||
            !double.IsFinite(remainingMilliseconds) ||
            remainingMilliseconds <= 0)
        {
            return null;
        }

        return new(
            active,
            TimeSpan.FromMilliseconds(active.StartTime),
            TimeSpan.FromMilliseconds(active.EndTime),
            TimeSpan.FromMilliseconds(durationMilliseconds),
            localProgress,
            TimeSpan.FromMilliseconds(remainingMilliseconds));
    }
}
