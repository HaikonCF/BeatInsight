namespace BeatInsight.Models.JumpPatterns;

public enum EdgeZone
{
    None, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight
}

public sealed record EdgeJumpTransition(
    int StartObjectIndex, int EndObjectIndex, int StartTime, int EndTime,
    EdgeZone From, EdgeZone To, double Distance, bool Strong);

/// <summary>Only transitions in valid sequences are included. Coverage is unique participating
/// objects / all supplied hitobjects (0..1). LongestSequence counts objects.</summary>
public sealed record EdgeJumpAnalysis(int TotalObjects, int EdgeObjectCount,
    int LongestSequence, double Confidence, IReadOnlyList<JumpPatternMatch> Matches,
    IReadOnlyList<EdgeJumpTransition> Transitions)
{
    public int EdgeTransitionCount => Transitions.Count;
    public int StrongTransitionCount => Transitions.Count(t => t.Strong);
    public double Coverage => TotalObjects == 0 ? 0 : (double)EdgeObjectCount / TotalObjects;
}
