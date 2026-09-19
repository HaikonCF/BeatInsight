namespace BeatInsight.Models.JumpPatterns;

/// <summary>A central circle whose two adjacent Jump-qualified links form an open, non-Sharp turn.</summary>
public sealed record WideAngleJumpPoint(
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
    double NextDistance);

/// <summary>Coverage is unique objects in valid Wide Angle sequences / all supplied hitobjects.</summary>
public sealed record WideAngleJumpAnalysis(
    int TotalObjects,
    int WideObjectCount,
    int LongestSequence,
    double AverageWideAngle,
    double MinimumWideAngleObserved,
    double MaximumWideAngleObserved,
    double AverageJumpDistance,
    double Confidence,
    IReadOnlyList<JumpPatternMatch> Matches,
    IReadOnlyList<WideAngleJumpPoint> Points)
{
    public int WidePointCount => Points.Count;
    public int SequenceCount => Matches.Count;
    public double Coverage => TotalObjects == 0 ? 0 : (double)WideObjectCount / TotalObjects;
}
