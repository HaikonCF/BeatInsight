using System.Collections.ObjectModel;

namespace BeatInsight.Models.Sections;

/// <summary>
/// One cell of the global beatmap timeline.
///
/// Sections use half-open time bounds [StartTime, EndTime), except for the
/// final section whose EndTime is inclusive. ObjectIndices refer directly to
/// the source Beatmap.HitObjects collection and are never copied objects.
/// </summary>
public sealed record GameplaySectionAnalysis(
    int Index,
    double StartTime,
    double EndTime,
    int CircleCount,
    int SliderCount,
    int SpinnerCount,
    int OtherObjectCount,
    ReadOnlyCollection<int> ObjectIndices)
{
    /// <summary>
    /// Jump-pattern results projected onto this section. Segmentation creates
    /// an empty profile; SectionJumpPatternAggregator returns enriched copies.
    /// </summary>
    public SectionJumpPatternProfile JumpPatterns { get; init; }
        = SectionJumpPatternProfile.Empty;

    /// <summary>
    /// Stream-pattern results projected onto this section. Segmentation creates
    /// an empty profile; SectionStreamPatternAggregator returns enriched copies.
    /// </summary>
    public SectionStreamPatternProfile StreamPatterns { get; init; }
        = SectionStreamPatternProfile.Empty;

    public double Duration => EndTime - StartTime;

    public int ObjectCount => ObjectIndices.Count;
}
