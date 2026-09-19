namespace BeatInsight.Models.JumpPatterns;

/// <summary>One central object whose adjacent Jump links form a non-degenerate triangle.</summary>
public sealed record TriangleJumpPoint(
    int ObjectIndex,
    int Time,
    int PreviousX,
    int PreviousY,
    int CurrentX,
    int CurrentY,
    int NextX,
    int NextY,
    double PreviousDistance,
    double NextDistance,
    double OppositeDistance,
    double Angle,
    double Height,
    double SideBalance);

/// <summary>Coverage is unique objects in valid multi-occurrence Triangle sequences / all supplied hitobjects.</summary>
public sealed record TriangleJumpAnalysis(
    int TotalObjects,
    int TriangleObjectCount,
    int LongestSequence,
    double AverageTriangleAngle,
    double AverageHeight,
    double Confidence,
    IReadOnlyList<JumpPatternMatch> Matches,
    IReadOnlyList<TriangleJumpPoint> Points)
{
    public int TrianglePointCount => Points.Count;
    public double Coverage => TotalObjects == 0 ? 0 : (double)TriangleObjectCount / TotalObjects;
}
