using BeatInsight.Analysis;

namespace BeatInsight.ML.TechReview;

/// <summary>
/// Maps deterministic Tech V2 window output into the ML-only feature schema.
/// This intentionally excludes likelihood, labels, identities and map metadata.
/// </summary>
internal static class TechWindowFeatureMapper
{
    internal static TechWindowFeatures From(TechV2ExperimentalWindowResult window)
    {
        ArgumentNullException.ThrowIfNull(window);

        return new TechWindowFeatures(
            window.GeometryIrregularity,
            window.RhythmIrregularity,
            window.SliderComplexity,
            window.Awkwardness,
            window.SpacingVariation,
            window.AngleCoherenceWithSupport,
            window.ComplexSliderTransitionTopology.SliderToSliderRatio,
            window.ComplexSliderTransitionTopology.SliderToCircleRatio,
            window.ComplexSliderTransitionTopology.CircleToSliderRatio,
            window.ComplexSliderTransitionTopology.CircleToCircleRatio,
            window.ComplexSliderRunLength,
            window.CoupledTechnicalMotifContinuity);
    }
}
