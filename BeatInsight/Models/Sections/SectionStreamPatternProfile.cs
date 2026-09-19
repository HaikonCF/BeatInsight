namespace BeatInsight.Models.Sections;

/// <summary>
/// Section-local projection of the nine frozen Stream Pattern V1 dimensions.
/// The four length metrics remain separate; the five orthogonal metrics may
/// overlap them and each other.
/// </summary>
public sealed record SectionStreamPatternProfile(
    SectionPatternMetric Burst,
    SectionPatternMetric Short,
    SectionPatternMetric Long,
    SectionPatternMetric Deathstream,
    SectionPatternMetric Stamina,
    SectionPatternMetric Irregular,
    SectionPatternMetric Speed,
    SectionPatternMetric Flow,
    SectionPatternMetric Alternating)
{
    public static SectionStreamPatternProfile Empty { get; } = new(
        SectionPatternMetric.Empty,
        SectionPatternMetric.Empty,
        SectionPatternMetric.Empty,
        SectionPatternMetric.Empty,
        SectionPatternMetric.Empty,
        SectionPatternMetric.Empty,
        SectionPatternMetric.Empty,
        SectionPatternMetric.Empty,
        SectionPatternMetric.Empty);
}
