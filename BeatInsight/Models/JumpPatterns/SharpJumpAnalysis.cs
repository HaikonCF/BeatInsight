namespace BeatInsight.Models.JumpPatterns;

/// <summary>A central object whose two adjacent jump links form a sharp turn.
/// Points are retained only when they belong to a valid multi-point Sharp sequence.</summary>
public sealed record SharpJumpPoint(
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
    bool IsReversal);

/// <summary>Coverage is unique objects in valid Sharp sequences / all supplied hitobjects.
/// Angles use BeatInsight's convention: 0° straight, 180° reversal.</summary>
public sealed record SharpJumpAnalysis(
    int TotalObjects,
    int SharpObjectCount,
    int LongestSequence,
    double AverageSharpAngle,
    double StrongestSharpAngle,
    double Confidence,
    IReadOnlyList<JumpPatternMatch> Matches,
    IReadOnlyList<SharpJumpPoint> Points)
{
    public int SharpPointCount => Points.Count;
    public int ReversalPointCount => Points.Count(point => point.IsReversal);
    public double Coverage => TotalObjects == 0 ? 0 : (double)SharpObjectCount / TotalObjects;
}
