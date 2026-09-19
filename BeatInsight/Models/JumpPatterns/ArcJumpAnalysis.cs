namespace BeatInsight.Models.JumpPatterns;

public enum ArcTurnDirection
{
    Neutral,
    Left,
    Right
}

/// <summary>A central circle in a same-direction, progressively curved Jump trajectory.</summary>
public sealed record ArcJumpPoint(
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
    ArcTurnDirection TurnDirection);

/// <summary>Coverage is unique objects in valid Arc sequences / all supplied hitobjects.</summary>
public sealed record ArcJumpAnalysis(
    int TotalObjects,
    int ArcObjectCount,
    int LongestSequence,
    double AverageTurnAngle,
    double MinimumTurnAngleObserved,
    double MaximumTurnAngleObserved,
    double AngleConsistency,
    double AverageJumpDistance,
    double DistanceConsistency,
    double AverageCumulativeTurn,
    int LeftSequenceCount,
    int RightSequenceCount,
    double Confidence,
    IReadOnlyList<JumpPatternMatch> Matches,
    IReadOnlyList<ArcJumpPoint> Points)
{
    public int ArcPointCount => Points.Count;
    public int SequenceCount => Matches.Count;
    // osu! playfield coordinates grow downward on Y: a positive cross-product (Left)
    // is clockwise when rendered on screen.
    public int ClockwiseSequenceCount => LeftSequenceCount;
    public int CounterClockwiseSequenceCount => RightSequenceCount;
    public double Coverage => TotalObjects == 0 ? 0 : (double)ArcObjectCount / TotalObjects;
}
