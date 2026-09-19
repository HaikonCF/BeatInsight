using BeatInsight.Models.StreamPatterns;

namespace BeatInsight.Models.Sections;

/// <summary>The nine Stream Pattern V1 dimensions understood by Section Analysis.</summary>
public enum SectionStreamPatternType
{
    Burst,
    Short,
    Long,
    Deathstream,
    Stamina,
    Irregular,
    Speed,
    Flow,
    Alternating
}

/// <summary>
/// Adapter shape consumed by section aggregation. ObjectIndices need not be
/// contiguous: Stamina contains only the objects of its constituent rapid runs.
/// </summary>
public sealed record SectionStreamPatternOccurrence(
    SectionStreamPatternType Type,
    int StartObjectIndex,
    IReadOnlyList<int> ObjectIndices,
    double Confidence);

/// <summary>A bundle of already-calculated frozen Stream Pattern results.</summary>
public sealed record SectionStreamPatternAnalysisResults(
    BurstPatternAnalysis Burst,
    ShortStreamPatternAnalysis Short,
    LongStreamPatternAnalysis Long,
    DeathstreamPatternAnalysis Deathstream,
    StaminaStreamPatternAnalysis Stamina,
    IrregularStreamPatternAnalysis Irregular,
    SpeedStreamPatternAnalysis Speed,
    FlowStreamPatternAnalysis Flow,
    AlternatingStreamPatternAnalysis Alternating);
