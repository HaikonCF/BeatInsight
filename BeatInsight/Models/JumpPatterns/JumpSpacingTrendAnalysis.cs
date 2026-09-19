namespace BeatInsight.Models.JumpPatterns;

/// <summary>The spatial spacing direction of a local Jump run. This never represents tapping speed.</summary>
public enum JumpSpacingTrendDirection
{
    Accelerating,
    Decelerating
}

/// <summary>A validated local run whose Jump-link distances follow one dominant spatial trend.</summary>
public sealed record JumpSpacingTrendSequence(
    int StartObjectIndex,
    int EndObjectIndex,
    int StartTime,
    int EndTime,
    JumpSpacingTrendDirection Direction,
    IReadOnlyList<double> Distances,
    IReadOnlyList<double> Deltas,
    double TrendConsistency,
    double TrendMagnitude,
    double Confidence)
{
    public int LinkCount => Distances.Count;
    public int ObjectCount => LinkCount + 1;
}

/// <summary>Aggregate diagnostics for one spatial spacing direction.</summary>
public sealed record JumpSpacingTrendMetrics(
    int SequenceCount,
    int ObjectCount,
    int LongestSequence,
    double AverageStartDistance,
    double AverageEndDistance,
    double AverageTrendMagnitude,
    double AverageTrendConsistency,
    double AverageDistance,
    double Confidence);

/// <summary>
/// Accelerating and Decelerating Jump remain independent local pattern results.
/// Coverage counts unique objects in valid runs for each direction separately.
/// </summary>
public sealed record JumpSpacingTrendAnalysis(
    int TotalObjects,
    JumpSpacingTrendMetrics Accelerating,
    JumpSpacingTrendMetrics Decelerating,
    IReadOnlyList<JumpSpacingTrendSequence> Sequences)
{
    public int AcceleratingSequenceCount => Accelerating.SequenceCount;
    public int DeceleratingSequenceCount => Decelerating.SequenceCount;
    public int AcceleratingObjectCount => Accelerating.ObjectCount;
    public int DeceleratingObjectCount => Decelerating.ObjectCount;
    public int LongestAcceleratingSequence => Accelerating.LongestSequence;
    public int LongestDeceleratingSequence => Decelerating.LongestSequence;
    public double AcceleratingCoverage => TotalObjects == 0 ? 0 : (double)Accelerating.ObjectCount / TotalObjects;
    public double DeceleratingCoverage => TotalObjects == 0 ? 0 : (double)Decelerating.ObjectCount / TotalObjects;
}
