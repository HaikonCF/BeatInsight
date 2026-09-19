using System.Collections.ObjectModel;

namespace BeatInsight.Models.Sections;

/// <summary>
/// The contribution of one global pattern result to one timeline section.
/// ObjectIndices are unique source Beatmap.HitObjects indices. SequenceCount
/// counts only occurrences whose StartObjectIndex belongs to this section.
/// </summary>
public sealed record SectionPatternMetric(
    int SequenceCount,
    double Coverage,
    double AverageConfidence,
    ReadOnlyCollection<int> ObjectIndices)
{
    public static SectionPatternMetric Empty { get; } = new(
        0,
        0,
        0,
        Array.AsReadOnly(Array.Empty<int>()));

    public int ObjectCount => ObjectIndices.Count;
}
