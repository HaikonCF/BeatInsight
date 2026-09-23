using System.Text.Json;
using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Analysis.StreamPatterns;
using BeatInsight.Analysis.TechPatterns;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.TechPatterns;

namespace BeatInsight.Tests.Analysis;

public sealed class CircleSliderTransitionPatternAnalyzerTests
{
    [Fact]
    public void CirclesOnlyAreRejected()
    {
        CircleSliderTransitionPatternAnalysis result = Analyze("CCCC");

        Assert.Empty(result.Sequences);
        Assert.All(result.Runs, run => Assert.False(run.Qualified));
    }

    [Fact]
    public void SlidersOnlyAreRejected()
    {
        CircleSliderTransitionPatternAnalysis result = Analyze("SSSS");

        Assert.Empty(result.Sequences);
        Assert.All(result.Runs, run => Assert.False(run.Qualified));
    }

    [Fact]
    public void SingleCircleSliderTransitionIsTooShort()
    {
        CircleSliderTransitionPatternSequence run = Assert.Single(Analyze("CS").Runs);

        Assert.False(run.Qualified);
        Assert.Equal("TooShort", run.Reason);
    }

    [Theory]
    [InlineData("CSCS")]
    [InlineData("SCSC")]
    public void FourAlternatingObjectsAreAccepted(string pattern)
    {
        CircleSliderTransitionPatternSequence sequence = Assert.Single(Analyze(pattern).Sequences);

        Assert.Equal(4, sequence.ObjectCount);
        Assert.Equal(3, sequence.TransitionCount);
        Assert.Equal(1, sequence.TransitionRatio);
    }

    [Fact]
    public void TransitionRatioAtInclusiveSixtyPercentBoundaryIsAccepted()
    {
        CircleSliderTransitionPatternSequence sequence = Assert.Single(Analyze("CCSSCS").Sequences);

        Assert.Equal(3, sequence.TransitionCount);
        Assert.Equal(5, sequence.PossibleTransitions);
        Assert.Equal(0.60, sequence.TransitionRatio, 10);
    }

    [Fact]
    public void TransitionRatioJustBelowSixtyPercentIsRejected()
    {
        CircleSliderTransitionPatternSequence run = Assert.Single(Analyze("CCSSCCS").Runs);

        Assert.Equal(0.50, run.TransitionRatio, 10);
        Assert.False(run.Qualified);
        Assert.Equal("InsufficientTransitionRatio", run.Reason);
    }

    [Fact]
    public void ExactlyThreeTransitionsAreAccepted()
    {
        CircleSliderTransitionPatternSequence sequence = Assert.Single(Analyze("CSCS").Sequences);

        Assert.Equal(3, sequence.TransitionCount);
    }

    [Fact]
    public void FewerThanThreeTransitionsAreRejectedBeforeRatio()
    {
        CircleSliderTransitionPatternSequence run = Assert.Single(Analyze("CCSC").Runs);

        Assert.Equal(2, run.TransitionCount);
        Assert.True(run.TransitionRatio >= 0.60);
        Assert.Equal("TooFewTransitions", run.Reason);
    }

    [Fact]
    public void LongAlternationRemainsOneMaximalSequence()
    {
        CircleSliderTransitionPatternAnalysis result = Analyze("CSCSCSCSCS");
        CircleSliderTransitionPatternSequence sequence = Assert.Single(result.Sequences);

        Assert.Single(result.Runs);
        Assert.Equal(10, sequence.ObjectCount);
        Assert.Equal((0, 9), (sequence.StartIndex, sequence.EndIndex));
    }

    [Fact]
    public void ThirdConsecutiveSameTypeStartsANewCandidate()
    {
        CircleSliderTransitionPatternAnalysis result = Analyze("CSCCCSC");

        Assert.Equal(2, result.Runs.Count);
        Assert.Equal((0, 3), (result.Runs[0].StartIndex, result.Runs[0].EndIndex));
        Assert.Equal((4, 6), (result.Runs[1].StartIndex, result.Runs[1].EndIndex));
        Assert.Empty(result.Sequences);
    }

    [Fact]
    public void SpinnerBreaksSequenceImmediately()
    {
        CircleSliderTransitionPatternAnalysis result = Analyze("CSCPSCS");

        Assert.Equal(2, result.Runs.Count);
        Assert.All(result.Runs, run => Assert.Equal(3, run.ObjectCount));
        Assert.Empty(result.Sequences);
    }

    [Fact]
    public void GapAboveOneSecondBreaksSequence()
    {
        CircleSliderTransitionPatternAnalysis result = Analyze(
            "CSCSCS", [100, 100, 1001, 100, 100]);

        Assert.Equal(2, result.Runs.Count);
        Assert.All(result.Runs, run => Assert.Equal(3, run.ObjectCount));
    }

    [Fact]
    public void OneSecondGapRemainsContinuous()
    {
        CircleSliderTransitionPatternSequence sequence = Assert.Single(
            Analyze("CSCS", [100, 1000, 100]).Sequences);

        Assert.Equal(1000, sequence.MaximumObjectGapObserved);
        Assert.Equal(400, sequence.AverageObjectGap, 10);
        Assert.Equal(1200, sequence.Duration);
    }

    [Fact]
    public void DirectionalTransitionCountsAreExact()
    {
        CircleSliderTransitionPatternSequence sequence = Assert.Single(Analyze("CSCSC").Sequences);

        Assert.Equal(2, sequence.CircleToSliderCount);
        Assert.Equal(2, sequence.SliderToCircleCount);
    }

    [Fact]
    public void TransitionDirectionBalanceIsDescriptiveAndExact()
    {
        CircleSliderTransitionPatternSequence sequence = Assert.Single(Analyze("CSCS").Sequences);

        Assert.Equal(2, sequence.CircleToSliderCount);
        Assert.Equal(1, sequence.SliderToCircleCount);
        Assert.Equal(0.5, sequence.TransitionDirectionBalance!.Value, 10);
    }

    [Fact]
    public void CoverageUsesUniqueQualifiedObjectIndices()
    {
        CircleSliderTransitionPatternAnalysis result = Analyze("CSCSPSCSC");

        Assert.Equal(2, result.TransitionSequenceCount);
        Assert.Equal(8, result.TransitionObjectCount);
        Assert.Equal(8.0 / 9, result.Coverage, 10);
    }

    [Fact]
    public void TransitionScoresAreBoundedAndUseAvailableSignals()
    {
        CircleSliderTransitionPatternAnalysis result = Analyze("CSCS");

        Assert.All(result.Runs, run => Assert.InRange(run.TransitionScore, 0, 1));
        CircleSliderTransitionPatternSequence sequence = result.Runs[0];
        Assert.Equal((1 + 0.5 + 0.5) / 3, sequence.TransitionScore, 10);
    }

    [Fact]
    public void ConfidenceUsesFrozenFormulaAndIsBounded()
    {
        CircleSliderTransitionPatternAnalysis result = Analyze("CSCS");

        double expectedScore = (1 + 0.5 + 0.5) / 3;
        double expectedConfidence = (0.5 + 1 + 0.5 + expectedScore) / 4;
        Assert.Equal(expectedConfidence, result.Confidence, 10);
        Assert.InRange(result.Confidence, 0, 1);
    }

    [Fact]
    public void AggregateMetricsUseQualifiedSequencesOnly()
    {
        CircleSliderTransitionPatternAnalysis result = Analyze("CSCSPSCSCS");

        Assert.Equal(2, result.TransitionSequenceCount);
        Assert.Equal(4.5, result.AverageSequenceLength, 10);
        Assert.Equal(5, result.LongestTransitionSequence);
        Assert.Equal(3.5, result.AverageTransitionCount, 10);
        Assert.Equal(1, result.AverageTransitionRatio, 10);
    }

    [Fact]
    public void PatternCanCoexistWithSliderChainInSameMap()
    {
        Beatmap map = Map("SSSPCSCS");

        Assert.Single(TechPatternAnalyzer.AnalyzeSliderChain(map.HitObjects).Sequences);
        Assert.Single(TechPatternAnalyzer.AnalyzeCircleSliderTransition(map.HitObjects).Sequences);
    }

    [Fact]
    public void ComplexSliderDoesNotChangeTypeTransitionQualification()
    {
        Beatmap map = Map("CSCS");
        map.HitObjects[1] = StrongComplexSlider(map.HitObjects[1].Time);

        Assert.Single(TechPatternAnalyzer.AnalyzeComplexSlider(map.HitObjects).ComplexSliders);
        Assert.Single(TechPatternAnalyzer.AnalyzeCircleSliderTransition(map.HitObjects).Sequences);
    }

    [Fact]
    public void DirectionChangeDoesNotChangeTypeTransitionQualification()
    {
        Beatmap map = Map("CSCS");
        map.HitObjects[0].X = 100;
        map.HitObjects[0].Y = -100;

        Assert.NotEmpty(TechPatternAnalyzer.AnalyzeSliderDirectionChange(map.HitObjects).QualifiedOccurrences);
        Assert.Single(TechPatternAnalyzer.AnalyzeCircleSliderTransition(map.HitObjects).Sequences);
    }

    [Fact]
    public void ExistingTechPatternResultsRemainUnchanged()
    {
        Beatmap map = Map("CSCSSSCS");
        map.HitObjects[1] = StrongComplexSlider(map.HitObjects[1].Time);
        string chainBefore = JsonSerializer.Serialize(TechPatternAnalyzer.AnalyzeSliderChain(map.HitObjects));
        string complexBefore = JsonSerializer.Serialize(TechPatternAnalyzer.AnalyzeComplexSlider(map.HitObjects));
        string directionBefore = JsonSerializer.Serialize(
            TechPatternAnalyzer.AnalyzeSliderDirectionChange(map.HitObjects));

        _ = TechPatternAnalyzer.AnalyzeCircleSliderTransition(map.HitObjects);

        Assert.Equal(chainBefore, JsonSerializer.Serialize(TechPatternAnalyzer.AnalyzeSliderChain(map.HitObjects)));
        Assert.Equal(complexBefore, JsonSerializer.Serialize(TechPatternAnalyzer.AnalyzeComplexSlider(map.HitObjects)));
        Assert.Equal(directionBefore, JsonSerializer.Serialize(
            TechPatternAnalyzer.AnalyzeSliderDirectionChange(map.HitObjects)));
    }

    [Fact]
    public void StreamAndJumpV1RemainUnchanged()
    {
        Beatmap map = Map("CSCSCS");
        GameplayProfile profile = GameplayAnalyzer.Analyze(map);
        string streamBefore = SerializeStreamPatterns(map);
        string jumpBefore = JsonSerializer.Serialize(JumpPatternAnalyzer.Analyze(map.HitObjects, profile));

        _ = TechPatternAnalyzer.AnalyzeCircleSliderTransition(map.HitObjects);

        Assert.Equal(streamBefore, SerializeStreamPatterns(map));
        Assert.Equal(jumpBefore, JsonSerializer.Serialize(JumpPatternAnalyzer.Analyze(map.HitObjects, profile)));
    }

    [Fact]
    public void HistoricalTechTechV2IdentityRatingAndUiStateRemainUnchanged()
    {
        Beatmap map = Map("CSCSCS");
        GameplayProfile profile = GameplayAnalyzer.Analyze(map);
        string profileBefore = JsonSerializer.Serialize(profile);
        double ratingBefore = map.BeatInsightRating;
        string techV2Before = SerializeTechV2(map);
        string historicalBefore = JsonSerializer.Serialize(
            TechPatternRecognitionEngine.FindMatches(map.HitObjects));

        CircleSliderTransitionPatternAnalysis first =
            TechPatternAnalyzer.AnalyzeCircleSliderTransition(map.HitObjects);
        CircleSliderTransitionPatternAnalysis second =
            TechPatternAnalyzer.AnalyzeCircleSliderTransition(map.HitObjects);

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Equal(profileBefore, JsonSerializer.Serialize(profile));
        Assert.Equal(ratingBefore, map.BeatInsightRating);
        Assert.Equal(techV2Before, SerializeTechV2(map));
        Assert.Equal(historicalBefore, JsonSerializer.Serialize(
            TechPatternRecognitionEngine.FindMatches(map.HitObjects)));
        Assert.Same(profile, map.GameplayProfile);
    }

    [Fact]
    public void DefaultsAndDebugSwitchesMatchV1()
    {
        var options = new CircleSliderTransitionPatternOptions();

        Assert.Equal(4, options.MinimumTransitionSequenceObjects);
        Assert.Equal(3, options.MinimumTransitionCount);
        Assert.Equal(0.60, options.MinimumTransitionRatio);
        Assert.Equal(1000, options.MaximumTransitionObjectGap);
        Assert.Equal(2, options.MaximumConsecutiveSameType);
        Assert.False(GameplayDebug.CircleSliderTransitionPatternEnabled);
        Assert.False(GameplayDebug.CircleSliderTransitionPatternDetailedEnabled);
        GameplayDebug.CircleSliderTransitionPattern(Analyze("CSCS"));
    }

    private static CircleSliderTransitionPatternAnalysis Analyze(
        string pattern,
        IReadOnlyList<int>? gaps = null,
        CircleSliderTransitionPatternOptions? options = null) =>
        TechPatternAnalyzer.AnalyzeCircleSliderTransition(Map(pattern, gaps).HitObjects, options);

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
            map.HitObjects.Add(tokens[index] switch
            {
                'C' => Circle(index * 100, index % 2 == 0 ? 0 : 100, time),
                'S' => Slider(index * 100, 0, index * 100 + 100, 0, time),
                'P' => new HitObject { Type = 8, Time = time, X = index * 100, Y = 0 },
                _ => throw new ArgumentOutOfRangeException(nameof(pattern))
            });
        }
        return map;
    }

    private static HitObject Circle(int x, int y, int time) => new()
    {
        Type = 1,
        X = x,
        Y = y,
        Time = time
    };

    private static HitObject Slider(int startX, int startY, int endX, int endY, int time) => new()
    {
        Type = 2,
        X = startX,
        Y = startY,
        Time = time,
        Slides = 1,
        Length = Math.Sqrt(Math.Pow(endX - startX, 2) + Math.Pow(endY - startY, 2)),
        SliderCurveType = "L",
        SliderControlPoints = [new SliderControlPoint { X = endX, Y = endY }],
        SliderEndPosition = new SliderControlPoint { X = endX, Y = endY }
    };

    private static HitObject StrongComplexSlider(int time) => new()
    {
        Type = 2,
        X = 100,
        Y = 0,
        Time = time,
        Slides = 4,
        Length = 240,
        SliderCurveType = "B",
        SliderControlPoints =
        [
            new SliderControlPoint { X = 150, Y = 80 },
            new SliderControlPoint { X = 200, Y = -80 },
            new SliderControlPoint { X = 250, Y = 0 }
        ],
        SliderEndPosition = new SliderControlPoint { X = 100, Y = 0 }
    };

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
