namespace BeatInsight.Models.TechPatterns;
public sealed record TechPatternMatch(TechPatternType Type, int StartIndex, int EndIndex, double StartTime, double EndTime, double Similarity, double GeometrySimilarity, double RhythmSimilarity, double SpacingSimilarity, double SliderContextSimilarity = 0)
{
    public string PatternId => Type.ToString();
    public IReadOnlyList<int> SupportingEventIndices { get; init; } = Array.Empty<int>();
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
    public IReadOnlyList<TechInternalEvidence> InternalEvidence { get; init; } = Array.Empty<TechInternalEvidence>();
}

public enum TechEvidenceFamily { Direction, Spacing, Timing, SliderTraversal, Handoff, CoupledSequence }

/// <summary>One causal constraint with explicit supporting events, not a similarity-derived vote.</summary>
public sealed record TechInternalEvidence(TechEvidenceFamily Family, IReadOnlyList<int> EventIndices, string Reason);
