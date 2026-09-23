using BeatInsight.Analysis;
using BeatInsight.Models;

namespace BeatInsight.Tests.Analysis;

public sealed class TechDetectorV2ExperimentalTests
{
    [Fact]
    public void Analyze_UsesWindowSize12Stride4()
    {
        Beatmap map = FixtureLoader.Load("Tower Of Heaven [Extra].osu");
        IReadOnlyList<HitObject> objects = map.HitObjects;

        IReadOnlyList<TechV2ExperimentalWindowResult> windows =
            TechDetectorV2Experimental.Analyze(
                objects,
                GameplayAnalyzer.IsSpinner,
                GameplayAnalyzer.IsSlider,
                GameplayAnalyzer.IsComplexSlider,
                GameplayAnalyzer.GetObjectStartPosition,
                GameplayAnalyzer.GetObjectEndPosition,
                GameplayAnalyzer.GetTechTurnAngle);

        int expectedCount = objects.Count < TechDetectorV2Experimental.WindowSize
            ? 0
            : ((objects.Count - TechDetectorV2Experimental.WindowSize)
               / TechDetectorV2Experimental.Stride) + 1;

        Assert.Equal(expectedCount, windows.Count);

        if (windows.Count >= 2)
        {
            Assert.Equal(0, windows[0].StartIndex);
            Assert.Equal(11, windows[0].EndIndex);
            Assert.Equal(4, windows[1].StartIndex);
            Assert.Equal(15, windows[1].EndIndex);
        }
    }

    [Fact]
    public void Analyze_AllFeaturesAndLikelihoodRemainNormalized()
    {
        Beatmap map = FixtureLoader.Load("Tower Of Heaven [Extra].osu");

        IReadOnlyList<TechV2ExperimentalWindowResult> windows =
            TechDetectorV2Experimental.Analyze(
                map.HitObjects,
                GameplayAnalyzer.IsSpinner,
                GameplayAnalyzer.IsSlider,
                GameplayAnalyzer.IsComplexSlider,
                GameplayAnalyzer.GetObjectStartPosition,
                GameplayAnalyzer.GetObjectEndPosition,
                GameplayAnalyzer.GetTechTurnAngle);

        Assert.NotEmpty(windows);

        foreach (TechV2ExperimentalWindowResult window in windows)
        {
            Assert.InRange(window.GeometryIrregularity, 0.0, 1.0);
            Assert.InRange(window.RhythmIrregularity, 0.0, 1.0);
            Assert.InRange(window.SliderComplexity, 0.0, 1.0);
            Assert.InRange(window.Awkwardness, 0.0, 1.0);
            Assert.InRange(window.SpacingVariation, 0.0, 1.0);
            Assert.InRange(window.TechLikelihoodExperimental, 0.0, 1.0);
            Assert.InRange(window.AngleCoherenceWithSupport, 0.0, 1.0);
            Assert.InRange(window.CoupledTechnicalMotifContinuity, 0.0, 1.0);
            Assert.InRange(
                window.AngleCoherenceStructuralPivotCount,
                0,
                TechDetectorV2Experimental.WindowSize - 2);
            Assert.InRange(
                window.ComplexSliderRunLength,
                0,
                TechDetectorV2Experimental.WindowSize);

            TechV2ExperimentalSliderTransitionTopology topology =
                window.ComplexSliderTransitionTopology;

            if (topology.TransitionCount == 0)
            {
                Assert.Equal(0, topology.SliderToSliderRatio);
                Assert.Equal(0, topology.SliderToCircleRatio);
                Assert.Equal(0, topology.CircleToSliderRatio);
                Assert.Equal(0, topology.CircleToCircleRatio);
            }
            else
            {
                Assert.Equal(
                    1.0,
                    topology.SliderToSliderRatio
                    + topology.SliderToCircleRatio
                    + topology.CircleToSliderRatio
                    + topology.CircleToCircleRatio,
                    precision: 10);
            }
        }
    }

    [Fact]
    public void Analyze_CanYouUnderstandMe_DoesNotFlagAllWindowsAsStrongTech()
    {
        Beatmap map = FixtureLoader.Load("(can you) understand me [uhh].osu");

        IReadOnlyList<TechV2ExperimentalWindowResult> windows =
            TechDetectorV2Experimental.Analyze(
                map.HitObjects,
                GameplayAnalyzer.IsSpinner,
                GameplayAnalyzer.IsSlider,
                GameplayAnalyzer.IsComplexSlider,
                GameplayAnalyzer.GetObjectStartPosition,
                GameplayAnalyzer.GetObjectEndPosition,
                GameplayAnalyzer.GetTechTurnAngle);

        Assert.NotEmpty(windows);

        int highCount = windows.Count(window => window.TechLikelihoodExperimental >= 0.70);
        double highRatio = (double)highCount / windows.Count;

        Assert.True(highRatio < 0.50);
    }

    [Fact]
    public void Analyze_TowerOfHeaven_RegularStreamKeepsRhythmIrregularityModerate()
    {
        Beatmap map = FixtureLoader.Load("Tower Of Heaven [Extra].osu");

        IReadOnlyList<TechV2ExperimentalWindowResult> windows =
            TechDetectorV2Experimental.Analyze(
                map.HitObjects,
                GameplayAnalyzer.IsSpinner,
                GameplayAnalyzer.IsSlider,
                GameplayAnalyzer.IsComplexSlider,
                GameplayAnalyzer.GetObjectStartPosition,
                GameplayAnalyzer.GetObjectEndPosition,
                GameplayAnalyzer.GetTechTurnAngle);

        Assert.NotEmpty(windows);

        double averageRhythm = windows.Average(window => window.RhythmIrregularity);

        Assert.True(averageRhythm < 0.60);
    }
}
