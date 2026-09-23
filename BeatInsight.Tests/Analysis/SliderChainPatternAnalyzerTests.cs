using System.Text.Json;
using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Analysis.StreamPatterns;
using BeatInsight.Analysis.TechPatterns;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.TechPatterns;

namespace BeatInsight.Tests.Analysis;

public sealed class SliderChainPatternAnalyzerTests
{
    [Fact]
    public void MapWithoutSlidersHasNoCandidatesOrChains()
    {
        SliderChainPatternAnalysis result = Analyze("CCCC");

        Assert.Empty(result.Runs);
        Assert.Empty(result.Sequences);
        Assert.Equal(0, result.TotalSliders);
    }

    [Fact]
    public void OneSliderIsRejected()
    {
        SliderChainPatternSequence run = Assert.Single(Analyze("S").Runs);

        Assert.False(run.Qualified);
        Assert.Equal("TooFewSliders", run.Reason);
    }

    [Fact]
    public void TwoSlidersAreRejected()
    {
        SliderChainPatternSequence run = Assert.Single(Analyze("SS").Runs);

        Assert.False(run.Qualified);
        Assert.Equal(2, run.SliderCount);
        Assert.Equal("TooFewSliders", run.Reason);
    }

    [Fact]
    public void ExactlyThreeConsecutiveSlidersAreAccepted()
    {
        SliderChainPatternSequence sequence = Assert.Single(Analyze("SSS").Sequences);

        Assert.Equal(3, sequence.ObjectCount);
        Assert.Equal(3, sequence.SliderCount);
        Assert.Equal(1, sequence.SliderRatio);
        Assert.Equal("Qualified", sequence.Reason);
    }

    [Fact]
    public void FourConsecutiveSlidersAreAccepted()
    {
        SliderChainPatternSequence sequence = Assert.Single(Analyze("SSSS").Sequences);

        Assert.Equal(4, sequence.SliderCount);
    }

    [Fact]
    public void ThreeSlidersAndOneBridgeCircleMeetInclusiveRatioBoundary()
    {
        SliderChainPatternSequence sequence = Assert.Single(Analyze("SSCS").Sequences);

        Assert.Equal(3, sequence.SliderCount);
        Assert.Equal(1, sequence.CircleCount);
        Assert.Equal(0.75, sequence.SliderRatio, 10);
    }

    [Fact]
    public void SliderRatioJustBelowBoundaryIsRejected()
    {
        var options = new SliderChainPatternOptions(MinimumSliderRatio: 0.7501);
        SliderChainPatternSequence run = Assert.Single(Analyze("SSCS", options: options).Runs);

        Assert.Equal(0.75, run.SliderRatio, 10);
        Assert.False(run.Qualified);
        Assert.Equal("InsufficientSliderRatio", run.Reason);
    }

    [Fact]
    public void LowRatioSequenceIsRetainedOnlyAsRejectedDiagnostic()
    {
        SliderChainPatternAnalysis result = Analyze("SCSCS");
        SliderChainPatternSequence run = Assert.Single(result.Runs);

        Assert.Empty(result.Sequences);
        Assert.Equal(0.60, run.SliderRatio, 10);
        Assert.Equal("InsufficientSliderRatio", run.Reason);
    }

    [Fact]
    public void ProductionRatioBelowBoundaryIsRejected()
    {
        SliderChainPatternSequence run = Assert.Single(Analyze("SSCSSCSSCSS").Runs);

        Assert.Equal(8, run.SliderCount);
        Assert.Equal(8.0 / 11, run.SliderRatio, 10);
        Assert.False(run.Qualified);
        Assert.Equal("InsufficientSliderRatio", run.Reason);
    }

    [Fact]
    public void LargePauseSeparatesTwoChains()
    {
        SliderChainPatternAnalysis result = Analyze("SSSSSS", [100, 100, 1001, 100, 100]);

        Assert.Equal(2, result.SliderChainSequenceCount);
        Assert.All(result.Sequences, sequence => Assert.Equal(3, sequence.ObjectCount));
    }

    [Fact]
    public void MaximumObjectGapBoundaryIsInclusive()
    {
        Assert.Single(Analyze("SSS", [1000, 1000]).Sequences);
        Assert.Equal(2, Analyze("SSSSSS", [100, 100, 1001, 100, 100]).Sequences.Count);
    }

    [Fact]
    public void LongSliderSequenceRemainsOneMaximalChain()
    {
        SliderChainPatternSequence sequence = Assert.Single(Analyze(new string('S', 10)).Sequences);

        Assert.Equal(10, sequence.ObjectCount);
        Assert.Equal((0, 9), (sequence.StartIndex, sequence.EndIndex));
    }

    [Fact]
    public void AnalysisDoesNotArtificiallySplitLongChain()
    {
        SliderChainPatternAnalysis result = Analyze(new string('S', 30));

        Assert.Single(result.Runs);
        Assert.Single(result.Sequences);
        Assert.Equal(30, result.LongestSliderChain);
    }

    [Fact]
    public void LeadingAndTrailingCirclesDoNotPolluteChain()
    {
        SliderChainPatternSequence sequence = Assert.Single(Analyze("CCSSCSCC").Sequences);

        Assert.Equal((2, 5), (sequence.StartIndex, sequence.EndIndex));
        Assert.Equal(4, sequence.ObjectCount);
        Assert.Equal(1, sequence.CircleCount);
    }

    [Fact]
    public void TwoConsecutiveCirclesBreakLocalSequence()
    {
        SliderChainPatternAnalysis result = Analyze("SSCCS");

        Assert.Equal(2, result.Runs.Count);
        Assert.All(result.Runs, run => Assert.Equal("TooFewSliders", run.Reason));
        Assert.Empty(result.Sequences);
    }

    [Fact]
    public void SpinnerIsAHardBoundary()
    {
        SliderChainPatternAnalysis result = Analyze("SSSPSSS");

        Assert.Equal(2, result.SliderChainSequenceCount);
        Assert.All(result.Sequences, sequence => Assert.Equal(3, sequence.ObjectCount));
    }

    [Fact]
    public void CoverageCountsUniqueQualifiedObjectsOnly()
    {
        SliderChainPatternAnalysis result = Analyze("SSSCCSSS");

        Assert.Equal(6, result.SliderChainObjectCount);
        Assert.Equal(0.75, result.Coverage, 10);
    }

    [Fact]
    public void SliderCoverageUsesAllMapSlidersAsDenominator()
    {
        SliderChainPatternAnalysis result = Analyze("SSSCCS");

        Assert.Equal(4, result.TotalSliders);
        Assert.Equal(3, result.SliderChainSliderCount);
        Assert.Equal(0.75, result.SliderCoverage, 10);
    }

    [Fact]
    public void LongestAndAverageChainMetricsUseQualifiedChainsOnly()
    {
        SliderChainPatternAnalysis result = Analyze("SSSCCSSSS");

        Assert.Equal(4, result.LongestSliderChain);
        Assert.Equal(3.5, result.AverageChainLength, 10);
        Assert.Equal(3.5, result.AverageSliderCount, 10);
    }

    [Fact]
    public void AverageSliderRatioIsExact()
    {
        SliderChainPatternAnalysis result = Analyze("SSSCCSSCS");

        Assert.Equal(2, result.SliderChainSequenceCount);
        Assert.Equal(0.875, result.AverageSliderRatio, 10);
    }

    [Fact]
    public void GapAndDurationMetricsUseObjectStartTimes()
    {
        SliderChainPatternSequence sequence = Assert.Single(
            Analyze("SSCS", [50, 100, 150]).Sequences);

        Assert.Equal(new double[] { 50, 100, 150 }, sequence.ObjectGaps);
        Assert.Equal(100, sequence.AverageObjectGap);
        Assert.Equal(50, sequence.MinimumObjectGap);
        Assert.Equal(150, sequence.MaximumObjectGapObserved);
        Assert.Equal(300, sequence.Duration);
    }

    [Fact]
    public void ConfidenceUsesFrozenDescriptiveFormulaAndIsBounded()
    {
        SliderChainPatternAnalysis qualified = Analyze("SSS");
        SliderChainPatternAnalysis rejected = Analyze("SS");

        Assert.Equal(0.775, qualified.Confidence, 10);
        Assert.InRange(qualified.Confidence, 0, 1);
        Assert.Equal(0, rejected.Confidence);
    }

    [Fact]
    public void NoSliderCaseAvoidsDivisionByZero()
    {
        SliderChainPatternAnalysis result = Analyze("CCCC");

        Assert.Equal(0, result.Coverage);
        Assert.Equal(0, result.SliderCoverage);
        Assert.Equal(0, result.AverageSliderRatio);
        Assert.Equal(0, result.Confidence);
    }

    [Fact]
    public void DefaultsAndDebugSwitchesMatchSliderChainV1()
    {
        var options = new SliderChainPatternOptions();

        Assert.Equal(3, options.MinimumSliderChainSliders);
        Assert.Equal(0.75, options.MinimumSliderRatio);
        Assert.Equal(1000, options.MaximumObjectGap);
        Assert.Equal(1, options.MaximumConsecutiveNonSliders);
        Assert.False(GameplayDebug.SliderChainPatternEnabled);
        Assert.False(GameplayDebug.SliderChainPatternDetailedEnabled);
        GameplayDebug.SliderChainPattern(Analyze("SSS"));
    }

    [Fact]
    public void JumpAndStreamPatternsRemainUnchanged()
    {
        Beatmap map = Map("CCSSCSSSCCSSS");
        GameplayProfile profile = GameplayAnalyzer.Analyze(map);
        string jumpBefore = JsonSerializer.Serialize(JumpPatternAnalyzer.Analyze(map.HitObjects, profile));
        string streamBefore = SerializeStreamPatterns(map);

        _ = TechPatternAnalyzer.AnalyzeSliderChain(map.HitObjects);

        Assert.Equal(jumpBefore, JsonSerializer.Serialize(JumpPatternAnalyzer.Analyze(map.HitObjects, profile)));
        Assert.Equal(streamBefore, SerializeStreamPatterns(map));
    }

    [Fact]
    public void HistoricalTechTechV2IdentityAndRatingRemainUnchanged()
    {
        Beatmap map = Map("SSSCSSSCSSSC");
        GameplayProfile profile = GameplayAnalyzer.Analyze(map);
        string profileBefore = JsonSerializer.Serialize(profile);
        double ratingBefore = map.BeatInsightRating;
        string techV2Before = SerializeTechV2(map);
        string historicalPatternsBefore = JsonSerializer.Serialize(
            TechPatternRecognitionEngine.FindMatches(map.HitObjects));

        SliderChainPatternAnalysis first = TechPatternAnalyzer.AnalyzeSliderChain(map.HitObjects);
        SliderChainPatternAnalysis second = TechPatternAnalyzer.AnalyzeSliderChain(map.HitObjects);

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Equal(profileBefore, JsonSerializer.Serialize(profile));
        Assert.Equal(ratingBefore, map.BeatInsightRating);
        Assert.Equal(techV2Before, SerializeTechV2(map));
        Assert.Equal(historicalPatternsBefore, JsonSerializer.Serialize(
            TechPatternRecognitionEngine.FindMatches(map.HitObjects)));
        Assert.Same(profile, map.GameplayProfile);
    }

    private static SliderChainPatternAnalysis Analyze(
        string pattern,
        IReadOnlyList<int>? gaps = null,
        SliderChainPatternOptions? options = null) =>
        TechPatternAnalyzer.AnalyzeSliderChain(Map(pattern, gaps).HitObjects, options);

    private static Beatmap Map(string pattern, IReadOnlyList<int>? gaps = null)
    {
        char[] tokens = pattern.Where(character => !char.IsWhiteSpace(character)).ToArray();
        if (gaps is not null && gaps.Count != Math.Max(0, tokens.Length - 1))
            throw new ArgumentException("Gap count must be one less than object count.", nameof(gaps));

        var map = new Beatmap();
        int time = 0;
        for (int index = 0; index < tokens.Length; index++)
        {
            if (index > 0)
                time += gaps?[index - 1] ?? 100;

            map.HitObjects.Add(new HitObject
            {
                Type = tokens[index] switch
                {
                    'S' => 2,
                    'C' => 1,
                    'P' => 8,
                    _ => throw new ArgumentOutOfRangeException(nameof(pattern))
                },
                Time = time,
                X = index * 40,
                Y = index % 2 == 0 ? 80 : 120
            });
        }
        return map;
    }

    private static string SerializeStreamPatterns(Beatmap map) => JsonSerializer.Serialize(new object[]
    {
        StreamPatternAnalyzer.AnalyzeBurst(map.HitObjects),
        StreamPatternAnalyzer.AnalyzeShortStream(map.HitObjects),
        StreamPatternAnalyzer.AnalyzeLongStream(map.HitObjects),
        StreamPatternAnalyzer.AnalyzeDeathstream(map.HitObjects),
        StreamPatternAnalyzer.AnalyzeStaminaStream(map.HitObjects),
        StreamPatternAnalyzer.AnalyzeIrregularStream(map.HitObjects),
        StreamPatternAnalyzer.AnalyzeSpeedStream(map.HitObjects),
        StreamPatternAnalyzer.AnalyzeFlowStream(map.HitObjects),
        StreamPatternAnalyzer.AnalyzeAlternatingStream(map.HitObjects)
    });

    private static string SerializeTechV2(Beatmap map) => JsonSerializer.Serialize(
        TechDetectorV2Experimental.Analyze(
            map.HitObjects,
            GameplayAnalyzer.IsSpinner,
            GameplayAnalyzer.IsSlider,
            GameplayAnalyzer.IsComplexSlider,
            GameplayAnalyzer.GetObjectStartPosition,
            GameplayAnalyzer.GetObjectEndPosition,
            GameplayAnalyzer.GetTechTurnAngle));
}
