namespace BeatInsight.Models.JumpPatterns;

/// <summary>A Jump-qualified, sufficiently large local link retained in a valid Spaced sequence.</summary>
public sealed record SpacedJumpTransition(
    int StartObjectIndex,
    int EndObjectIndex,
    int StartTime,
    int EndTime,
    int FromX,
    int FromY,
    int ToX,
    int ToY,
    double Distance,
    int SequenceId);

/// <summary>Coverage is unique objects in valid Spaced sequences / all supplied hitobjects.</summary>
public sealed record SpacedJumpAnalysis(
    int TotalObjects,
    int SpacedObjectCount,
    int LongestSequence,
    double AverageDistance,
    double MinimumDistance,
    double MaximumDistance,
    double DistanceConsistency,
    double Confidence,
    IReadOnlyList<JumpPatternMatch> Matches,
    IReadOnlyList<SpacedJumpTransition> Transitions)
{
    public int SpacedTransitionCount => Transitions.Count;
    public int SequenceCount => Matches.Count;
    public double Coverage => TotalObjects == 0 ? 0 : (double)SpacedObjectCount / TotalObjects;
}
