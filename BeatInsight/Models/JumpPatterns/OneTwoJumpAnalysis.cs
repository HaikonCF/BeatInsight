namespace BeatInsight.Models.JumpPatterns;

public enum OneTwoTurnDirection
{
    Neutral,
    Left,
    Right,
    Reversal
}

/// <summary>A central object in a local alternating Jump chain.</summary>
public sealed record OneTwoJumpPoint(
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
    double Angle,
    int PreviousInterval,
    int NextInterval,
    OneTwoTurnDirection TurnDirection,
    bool AlternatingWithPrevious);

/// <summary>Coverage is unique objects in valid OneTwo sequences / all supplied hitobjects.</summary>
public sealed record OneTwoJumpAnalysis(
    int TotalObjects,
    int OneTwoObjectCount,
    int LongestSequence,
    double AverageJumpDistance,
    double DistanceConsistency,
    double AverageInterval,
    double IntervalConsistency,
    double AlternationConsistency,
    double Confidence,
    IReadOnlyList<JumpPatternMatch> Matches,
    IReadOnlyList<OneTwoJumpPoint> Points)
{
    public int OneTwoPointCount => Points.Count;
    public int SequenceCount => Matches.Count;
    public double Coverage => TotalObjects == 0 ? 0 : (double)OneTwoObjectCount / TotalObjects;
    public int CandidateBlockCount { get; init; }
    public int AcceptedBlockCount { get; init; }
    public int AcceptedRepeatedBlockGroupCount => Matches.Count;
    public int RejectedIsolatedBlockCount { get; init; }
    public int RejectedInconsistentBlockGroupCount { get; init; }
    public IReadOnlyList<int> ResetObjectIndices { get; init; } = [];
}
