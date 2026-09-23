namespace BeatInsight.ML.TechReview;

/// <summary>
/// Features locales autorisées pour le futur classifieur Tech / Not Tech.
/// Les métadonnées, labels et scores fusionnés ne font pas partie de ce type.
/// </summary>
internal sealed record TechWindowFeatures(
    double GeometryIrregularity,
    double RhythmIrregularity,
    double SliderComplexity,
    double Awkwardness,
    double SpacingVariation,
    double AngleCoherenceWithSupport,
    double SliderToSliderRatio,
    double SliderToCircleRatio,
    double CircleToSliderRatio,
    double CircleToCircleRatio,
    int ComplexSliderRunLength,
    double CoupledTechnicalMotifContinuity);
