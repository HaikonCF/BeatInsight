using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using BeatInsight.Models.AnalysisCache;
using BeatInsight.Models.Fusion;
using BeatInsight.Models.Sections;

namespace BeatInsight.Integration.Game;

public sealed record DifficultyTimelineSegment(
    GameplaySectionV2Analysis Section,
    double StartRatio,
    double EndRatio,
    double DifficultyRatio)
{
    public int SectionIndex => Section.SectionIndex;
    public double LocalDifficultyScore => Section.LocalDifficultyScore;
    public GameplayStructureKind DominantStructure => Section.DominantStructure;
    public int ObjectCount => Section.ObjectCount;
    public bool IsEmpty => ObjectCount == 0;
}

public sealed record DifficultyTimelineModel(
    double AxisStart,
    double AxisEnd,
    ReadOnlyCollection<DifficultyTimelineSegment> Segments);

public sealed record DifficultyTimelineSnapshot(
    DifficultyTimelineModel Model,
    double MarkerRatio,
    int? CurrentSectionIndex);

/// <summary>
/// Builds one immutable timeline model per analysis reference. Successive live
/// ticks only compute the marker and current-section projection.
/// </summary>
public sealed class DifficultyTimelineResolver
{
    private readonly ConditionalWeakTable<BeatmapAnalysisSnapshot, CacheEntry> models = new();

    public DifficultyTimelineSnapshot? Resolve(
        LiveGameplaySnapshot? live,
        CurrentSectionSnapshot? currentSection)
    {
        if (live is null || !live.IsGameplay || live.BeatmapAnalysis is null)
            return null;

        double currentMilliseconds = live.CurrentTime.TotalMilliseconds;
        if (!double.IsFinite(currentMilliseconds) || currentMilliseconds < 0)
            return null;

        DifficultyTimelineModel? model = models.GetValue(
            live.BeatmapAnalysis,
            static analysis => new CacheEntry(BuildModel(analysis))).Model;
        if (model is null)
            return null;

        double markerRatio = Math.Clamp(
            (currentMilliseconds - model.AxisStart) / (model.AxisEnd - model.AxisStart),
            0,
            1);
        if (!double.IsFinite(markerRatio))
            return null;

        int? currentSectionIndex = currentSection is not null &&
            model.Segments.Any(segment => ReferenceEquals(segment.Section, currentSection.Section))
                ? currentSection.SectionIndex
                : null;

        return new(model, markerRatio, currentSectionIndex);
    }

    private static DifficultyTimelineModel? BuildModel(BeatmapAnalysisSnapshot analysis)
    {
        IReadOnlyList<GameplaySectionV2Analysis>? sections =
            analysis.SectionAnalysisV2?.Sections;
        if (sections is null || sections.Count == 0)
            return null;

        GameplaySectionV2Analysis? first = sections[0];
        GameplaySectionV2Analysis? last = sections[^1];
        if (first is null || last is null)
            return null;

        double axisStart = first.StartTime;
        double axisEnd = last.EndTime;
        if (!double.IsFinite(axisStart) ||
            !double.IsFinite(axisEnd) ||
            axisStart < 0 ||
            axisEnd <= axisStart)
        {
            return null;
        }

        double axisDuration = axisEnd - axisStart;
        var segments = new DifficultyTimelineSegment[sections.Count];
        double previousEnd = double.NegativeInfinity;
        for (int index = 0; index < sections.Count; index++)
        {
            GameplaySectionV2Analysis? section = sections[index];
            if (section is null ||
                section.ObjectIndices is null ||
                !double.IsFinite(section.StartTime) ||
                !double.IsFinite(section.EndTime) ||
                !double.IsFinite(section.LocalDifficultyScore) ||
                section.StartTime < 0 ||
                section.EndTime <= section.StartTime ||
                section.StartTime < previousEnd)
            {
                return null;
            }

            double startRatio = (section.StartTime - axisStart) / axisDuration;
            double endRatio = (section.EndTime - axisStart) / axisDuration;
            double difficultyRatio = Math.Clamp(section.LocalDifficultyScore, 0, 100) / 100;
            if (!double.IsFinite(startRatio) ||
                !double.IsFinite(endRatio) ||
                startRatio < 0 ||
                endRatio > 1 ||
                endRatio <= startRatio)
            {
                return null;
            }

            segments[index] = new(
                section,
                startRatio,
                endRatio,
                difficultyRatio);
            previousEnd = section.EndTime;
        }

        return new(axisStart, axisEnd, Array.AsReadOnly(segments));
    }

    private sealed record CacheEntry(DifficultyTimelineModel? Model);
}
