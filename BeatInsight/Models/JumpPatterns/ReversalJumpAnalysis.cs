namespace BeatInsight.Models.JumpPatterns;

/// <summary>A central circle whose adjacent Jump-qualified links form a near-complete direction reversal.</summary>
public sealed record ReversalJumpPoint(
    int ObjectIndex,
    int Time,
    int PreviousX,
    int PreviousY,
    int CurrentX,
    int CurrentY,
    int NextX,
    int NextY,
    double Angle,
    double PreviousDistance,
    double NextDistance,
    double ReturnDistance);

/// <summary>Coverage is unique objects in valid Reversal sequences / all supplied hitobjects.</summary>
public sealed record ReversalJumpAnalysis(
    int TotalObjects,
    int ReversalObjectCount,
    int LongestSequence,
    double AverageReversalAngle,
    double MinimumReversalAngleObserved,
    double MaximumReversalAngleObserved,
    double AverageJumpDistance,
    double DistanceConsistency,
    double AverageReturnDistance,
    double Confidence,
    IReadOnlyList<JumpPatternMatch> Matches,
    IReadOnlyList<ReversalJumpPoint> Points)
{
    public int ReversalPointCount => Points.Count;
    public int SequenceCount => Matches.Count;
    public double Coverage => TotalObjects == 0 ? 0 : (double)ReversalObjectCount / TotalObjects;
}
