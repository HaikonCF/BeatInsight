namespace BeatInsight.Models.Sections;

/// <summary>
/// Section-local projection of the eleven frozen Jump Pattern V1 dimensions.
/// SpacingTrend combines accelerating and decelerating occurrence coverage;
/// the two directional sequence counts remain available separately.
/// </summary>
public sealed record SectionJumpPatternProfile(
    SectionPatternMetric Linear,
    SectionPatternMetric Edge,
    SectionPatternMetric Sharp,
    SectionPatternMetric Triangle,
    SectionPatternMetric OneTwo,
    SectionPatternMetric Spaced,
    SectionPatternMetric WideAngle,
    SectionPatternMetric Reversal,
    SectionPatternMetric Arc,
    SectionPatternMetric SpacingTrend,
    int AcceleratingSequenceCount,
    int DeceleratingSequenceCount,
    SectionPatternMetric AlternatingSpacing)
{
    public static SectionJumpPatternProfile Empty { get; } = new(
        SectionPatternMetric.Empty,
        SectionPatternMetric.Empty,
        SectionPatternMetric.Empty,
        SectionPatternMetric.Empty,
        SectionPatternMetric.Empty,
        SectionPatternMetric.Empty,
        SectionPatternMetric.Empty,
        SectionPatternMetric.Empty,
        SectionPatternMetric.Empty,
        SectionPatternMetric.Empty,
        0,
        0,
        SectionPatternMetric.Empty);
}
