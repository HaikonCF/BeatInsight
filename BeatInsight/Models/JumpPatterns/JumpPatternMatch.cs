namespace BeatInsight.Models.JumpPatterns;

/// <summary>A local template match; indices are inclusive in Beatmap.HitObjects.</summary>
public sealed record JumpPatternMatch(
    JumpPatternType Type,
    int StartObjectIndex,
    int EndObjectIndex,
    int StartTime,
    int EndTime,
    double Confidence)
{
    public int ObjectCount => EndObjectIndex - StartObjectIndex + 1;
}
