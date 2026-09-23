using BeatInsight.Analysis;
using BeatInsight.ML.TechReview;

namespace BeatInsight.ML.Tests.Baselines;

public sealed class TechWindowFeatureMapperTests
{
    [Fact]
    public void From_MapsEveryDetectorFeatureWithoutChangingValues()
    {
        TechV2ExperimentalWindowResult window = new(
            4,
            15,
            1.25,
            2.5,
            0.1,
            0.2,
            0.3,
            0.4,
            0.5,
            0.49,
            0.6,
            3,
            new TechV2ExperimentalSliderTransitionTopology(
                11,
                0.1,
                0.2,
                0.3,
                0.4),
            3,
            0.5,
            2);

        TechWindowFeatures features = TechWindowFeatureMapper.From(window);

        Assert.Equal(window.GeometryIrregularity, features.GeometryIrregularity);
        Assert.Equal(window.RhythmIrregularity, features.RhythmIrregularity);
        Assert.Equal(window.SliderComplexity, features.SliderComplexity);
        Assert.Equal(window.Awkwardness, features.Awkwardness);
        Assert.Equal(window.SpacingVariation, features.SpacingVariation);
        Assert.Equal(window.AngleCoherenceWithSupport, features.AngleCoherenceWithSupport);
        Assert.Equal(window.ComplexSliderTransitionTopology.SliderToSliderRatio, features.SliderToSliderRatio);
        Assert.Equal(window.ComplexSliderTransitionTopology.SliderToCircleRatio, features.SliderToCircleRatio);
        Assert.Equal(window.ComplexSliderTransitionTopology.CircleToSliderRatio, features.CircleToSliderRatio);
        Assert.Equal(window.ComplexSliderTransitionTopology.CircleToCircleRatio, features.CircleToCircleRatio);
        Assert.Equal(window.ComplexSliderRunLength, features.ComplexSliderRunLength);
        Assert.Equal(window.CoupledTechnicalMotifContinuity, features.CoupledTechnicalMotifContinuity);
    }
}
