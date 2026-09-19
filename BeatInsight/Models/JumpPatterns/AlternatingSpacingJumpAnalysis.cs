namespace BeatInsight.Models.JumpPatterns;

/// <summary>Whether a local alternating-spacing run begins by expanding or contracting.</summary>
public enum AlternatingSpacingStart
{
    ExpandingFirst,
    ContractingFirst
}

/// <summary>A local Jump run with repeatedly alternating, meaningful spacing changes.</summary>
public sealed record AlternatingSpacingJumpSequence(
    int StartObjectIndex,
    int EndObjectIndex,
    int StartTime,
    int EndTime,
    AlternatingSpacingStart Start,
    IReadOnlyList<double> Distances,
    IReadOnlyList<double> Deltas,
    double HighSpacingAverage,
    double LowSpacingAverage,
    double HighLowRatio,
    double SpacingContrast,
    double AlternationConsistency,
    double OscillationMagnitudeConsistency,
    double Confidence)
{
    public int LinkCount => Distances.Count;
    public int ObjectCount => LinkCount + 1;
}

/// <summary>Coverage counts unique objects participating in valid alternating-spacing runs.</summary>
public sealed record AlternatingSpacingJumpAnalysis(
    int TotalObjects,
    int AlternatingSpacingObjectCount,
    int LongestSequence,
    double AverageDistance,
    double AverageHighSpacing,
    double AverageLowSpacing,
    double AverageHighLowRatio,
    double AverageSpacingContrast,
    double AverageAlternationConsistency,
    double AverageOscillationMagnitudeConsistency,
    int ExpandingFirstSequenceCount,
    int ContractingFirstSequenceCount,
    double Confidence,
    IReadOnlyList<JumpPatternMatch> Matches,
    IReadOnlyList<AlternatingSpacingJumpSequence> Sequences)
{
    public int AlternatingSpacingSequenceCount => Sequences.Count;
    public double Coverage => TotalObjects == 0 ? 0 : (double)AlternatingSpacingObjectCount / TotalObjects;
}
