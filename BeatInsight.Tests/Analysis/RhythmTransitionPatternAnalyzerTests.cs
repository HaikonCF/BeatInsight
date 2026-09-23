using System.Text.Json;
using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Analysis.StreamPatterns;
using BeatInsight.Analysis.TechPatterns;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.TechPatterns;

namespace BeatInsight.Tests.Analysis;

public sealed class RhythmTransitionPatternAnalyzerTests
{
    [Fact]
    public void InsufficientContextIsRejectedWithoutInventedMetrics()
    {
        RhythmTransitionPatternAnalysis result = Analyze([100, 100, 100]);

        Assert.Empty(result.Transitions);
        Assert.All(result.Occurrences, occurrence =>
            Assert.Equal("InsufficientContext", occurrence.Reason));
        Assert.All(result.Occurrences, occurrence =>
            Assert.Null(occurrence.AbsoluteIntervalChange));
    }

    [Fact]
    public void ConstantRhythmIsRejected()
    {
        RhythmTransitionPatternOccurrence occurrence = Pivot(Analyze([100, 100, 100, 100]), 2);

        Assert.False(occurrence.Qualified);
        Assert.Equal("BelowAbsoluteThreshold", occurrence.Reason);
        Assert.Equal(0, occurrence.AbsoluteIntervalChange);
    }

    [Fact]
    public void CleanAccelerationIsAccepted()
    {
        RhythmTransitionPatternOccurrence transition = Assert.Single(
            Analyze([100, 100, 100, 50, 50, 50]).Transitions);

        Assert.Equal(new double[] { 100, 100 }, transition.BeforeIntervals);
        Assert.Equal(new double[] { 50, 50 }, transition.AfterIntervals);
        Assert.Equal(RhythmTransitionDirection.Faster, transition.Direction);
    }

    [Fact]
    public void CleanSlowdownIsAccepted()
    {
        RhythmTransitionPatternOccurrence transition = Assert.Single(
            Analyze([50, 50, 50, 100, 100]).Transitions);

        Assert.Equal(RhythmTransitionDirection.Slower, transition.Direction);
    }

    [Fact]
    public void AbsoluteTwentyMillisecondBoundaryIsInclusive()
    {
        RhythmTransitionPatternOccurrence transition = Assert.Single(
            Analyze([80, 80, 60, 60]).Transitions);

        Assert.Equal(20, transition.AbsoluteIntervalChange);
        Assert.Equal(0.25, transition.RelativeIntervalChange!.Value, 10);
    }

    [Fact]
    public void AbsoluteChangeJustBelowTwentyIsRejected()
    {
        RhythmTransitionPatternOccurrence occurrence = Pivot(Analyze([79, 79, 60, 60]), 2);

        Assert.Equal(19, occurrence.AbsoluteIntervalChange);
        Assert.Equal("BelowAbsoluteThreshold", occurrence.Reason);
    }

    [Fact]
    public void RelativeQuarterBoundaryIsInclusive()
    {
        RhythmTransitionPatternOccurrence transition = Assert.Single(
            Analyze([80, 80, 60, 60]).Transitions);

        Assert.Equal(0.25, transition.RelativeIntervalChange!.Value, 10);
    }

    [Fact]
    public void RelativeChangeJustBelowQuarterIsRejected()
    {
        RhythmTransitionPatternOccurrence occurrence = Pivot(Analyze([81, 81, 61, 61]), 2);

        Assert.Equal(20, occurrence.AbsoluteIntervalChange);
        Assert.True(occurrence.RelativeIntervalChange < 0.25);
        Assert.Equal("BelowRelativeThreshold", occurrence.Reason);
    }

    [Fact]
    public void BeforeConsistencyAtInclusiveBoundaryIsAccepted()
    {
        RhythmTransitionPatternOccurrence transition = Assert.Single(
            Analyze([80, 60, 40, 40]).Transitions);

        Assert.Equal(0.75, transition.BeforeConsistency!.Value, 10);
    }

    [Fact]
    public void AfterConsistencyAtInclusiveBoundaryIsAccepted()
    {
        RhythmTransitionPatternOccurrence transition = Assert.Single(
            Analyze([80, 80, 60, 45]).Transitions);

        Assert.Equal(0.75, transition.AfterConsistency!.Value, 10);
    }

    [Fact]
    public void InconsistentBeforeSideIsRejected()
    {
        RhythmTransitionPatternOccurrence occurrence = Pivot(Analyze([100, 50, 30, 30]), 2);

        Assert.Equal("InconsistentBefore", occurrence.Reason);
    }

    [Fact]
    public void InconsistentAfterSideIsRejected()
    {
        RhythmTransitionPatternOccurrence occurrence = Pivot(Analyze([100, 100, 60, 30]), 2);

        Assert.Equal("InconsistentAfter", occurrence.Reason);
    }

    [Fact]
    public void LargeGapCreatesIntervalBreakInsteadOfTransition()
    {
        RhythmTransitionPatternAnalysis result = Analyze([100, 100, 1500, 100, 100]);

        Assert.Empty(result.Transitions);
        Assert.Contains(result.Occurrences, occurrence => occurrence.Reason == "IntervalBreak");
    }

    [Fact]
    public void NegativeGapCreatesIntervalBreakInsteadOfTransition()
    {
        RhythmTransitionPatternAnalysis result = Analyze([100, 100, -1, 100, 100]);

        Assert.Empty(result.Transitions);
        Assert.Contains(result.Occurrences, occurrence => occurrence.Reason == "IntervalBreak");
    }

    [Fact]
    public void MaximumRhythmIntervalBoundaryRemainsUsable()
    {
        RhythmTransitionPatternOccurrence transition = Assert.Single(
            Analyze([1000, 1000, 500, 500]).Transitions);

        Assert.Equal(1000, transition.BeforeAverageInterval);
        Assert.Equal(RhythmTransitionDirection.Faster, transition.Direction);
    }

    [Fact]
    public void SpinnerBreaksLocalContext()
    {
        Beatmap map = Map([100, 100, 50, 50]);
        map.HitObjects[2].Type = 8;

        RhythmTransitionPatternAnalysis result =
            TechPatternAnalyzer.AnalyzeRhythmTransition(map.HitObjects);

        Assert.Empty(result.Transitions);
        Assert.Contains(result.Occurrences, occurrence => occurrence.Reason == "IntervalBreak");
    }

    [Fact]
    public void FasterAndSlowerCountsAreExact()
    {
        RhythmTransitionPatternAnalysis result = Analyze([100, 100, 50, 50, 50, 100, 100]);

        Assert.Equal(2, result.RhythmTransitionCount);
        Assert.Equal(1, result.FasterTransitionCount);
        Assert.Equal(1, result.SlowerTransitionCount);
    }

    [Fact]
    public void ScoresAndConfidenceAreBounded()
    {
        RhythmTransitionPatternAnalysis result = Analyze([100, 100, 50, 50]);

        Assert.All(result.Occurrences, occurrence =>
            Assert.InRange(occurrence.RhythmTransitionScore, 0, 1));
        Assert.InRange(result.Confidence, 0, 1);
        Assert.InRange(result.MaximumTransitionScore, 0, 1);
    }

    [Fact]
    public void CoverageDeduplicatesOverlappingTransitionWindows()
    {
        RhythmTransitionPatternAnalysis result = Analyze([100, 100, 50, 50, 50, 100, 100]);

        Assert.Equal(2, result.RhythmTransitionCount);
        Assert.Equal(8, result.RhythmTransitionObjectCount);
        Assert.Equal(1, result.Coverage, 10);
    }

    [Fact]
    public void AdjacentPivotsForSameRuptureKeepHighestScoreOnly()
    {
        RhythmTransitionPatternAnalysis result = Analyze([100, 100, 80, 60, 60]);
        RhythmTransitionPatternOccurrence transition = Assert.Single(result.Transitions);

        Assert.Equal(3, transition.PivotIndex);
    }

    [Fact]
    public void TwoSeparatedRealRupturesRemainTwoEvents()
    {
        RhythmTransitionPatternAnalysis result = Analyze([100, 100, 50, 50, 50, 100, 100]);

        Assert.Equal(new[] { 2, 5 }, result.Transitions.Select(transition => transition.PivotIndex));
    }

    [Fact]
    public void MicroVariationIsNotARhythmTransition()
    {
        Assert.Empty(Analyze([100, 98, 101, 99, 100]).Transitions);
    }

    [Fact]
    public void SustainedChaosIsRejectedAsInconsistentRegimes()
    {
        RhythmTransitionPatternOccurrence occurrence = Pivot(Analyze([80, 120, 60, 100]), 2);

        Assert.False(occurrence.Qualified);
        Assert.Equal("InconsistentBefore", occurrence.Reason);
    }

    [Fact]
    public void RhythmTransitionCanCoexistWithCircleSliderTransition()
    {
        Beatmap map = Map([100, 100, 50, 50], "CSCSC");

        Assert.Single(TechPatternAnalyzer.AnalyzeRhythmTransition(map.HitObjects).Transitions);
        Assert.Single(TechPatternAnalyzer.AnalyzeCircleSliderTransition(map.HitObjects).Sequences);
    }

    [Fact]
    public void RhythmTransitionCanCoexistWithSliderDirectionChange()
    {
        Beatmap map = Map([100, 100, 50, 50], "CSCCC");
        map.HitObjects[0].X = 100;
        map.HitObjects[0].Y = -100;

        Assert.Single(TechPatternAnalyzer.AnalyzeRhythmTransition(map.HitObjects).Transitions);
        Assert.NotEmpty(TechPatternAnalyzer.AnalyzeSliderDirectionChange(map.HitObjects).QualifiedOccurrences);
    }

    [Fact]
    public void RhythmTransitionCanCoexistWithIrregularStream()
    {
        Beatmap map = Map([80, 80, 40, 40, 80, 40, 80]);

        Assert.NotEmpty(TechPatternAnalyzer.AnalyzeRhythmTransition(map.HitObjects).Transitions);
        Assert.NotEmpty(StreamPatternAnalyzer.AnalyzeIrregularStream(map.HitObjects).Sequences);
    }

    [Fact]
    public void ExistingTechPatternResultsRemainUnchanged()
    {
        Beatmap map = Map([100, 100, 50, 50], "CSCSC");
        string chainBefore = JsonSerializer.Serialize(TechPatternAnalyzer.AnalyzeSliderChain(map.HitObjects));
        string complexBefore = JsonSerializer.Serialize(TechPatternAnalyzer.AnalyzeComplexSlider(map.HitObjects));
        string directionBefore = JsonSerializer.Serialize(
            TechPatternAnalyzer.AnalyzeSliderDirectionChange(map.HitObjects));
        string typeTransitionBefore = JsonSerializer.Serialize(
            TechPatternAnalyzer.AnalyzeCircleSliderTransition(map.HitObjects));

        _ = TechPatternAnalyzer.AnalyzeRhythmTransition(map.HitObjects);

        Assert.Equal(chainBefore, JsonSerializer.Serialize(TechPatternAnalyzer.AnalyzeSliderChain(map.HitObjects)));
        Assert.Equal(complexBefore, JsonSerializer.Serialize(TechPatternAnalyzer.AnalyzeComplexSlider(map.HitObjects)));
        Assert.Equal(directionBefore, JsonSerializer.Serialize(
            TechPatternAnalyzer.AnalyzeSliderDirectionChange(map.HitObjects)));
        Assert.Equal(typeTransitionBefore, JsonSerializer.Serialize(
            TechPatternAnalyzer.AnalyzeCircleSliderTransition(map.HitObjects)));
    }

    [Fact]
    public void StreamAndJumpV1RemainUnchanged()
    {
        Beatmap map = Map([100, 100, 50, 50, 80, 40, 80]);
        GameplayProfile profile = GameplayAnalyzer.Analyze(map);
        string streamBefore = SerializeStreamPatterns(map);
        string jumpBefore = JsonSerializer.Serialize(JumpPatternAnalyzer.Analyze(map.HitObjects, profile));

        _ = TechPatternAnalyzer.AnalyzeRhythmTransition(map.HitObjects);

        Assert.Equal(streamBefore, SerializeStreamPatterns(map));
        Assert.Equal(jumpBefore, JsonSerializer.Serialize(JumpPatternAnalyzer.Analyze(map.HitObjects, profile)));
    }

    [Fact]
    public void HistoricalTechTechV2IdentityRatingAndUiStateRemainUnchanged()
    {
        Beatmap map = Map([100, 100, 50, 50], "CSCSC");
        GameplayProfile profile = GameplayAnalyzer.Analyze(map);
        string profileBefore = JsonSerializer.Serialize(profile);
        double ratingBefore = map.BeatInsightRating;
        string techV2Before = SerializeTechV2(map);
        string historicalBefore = JsonSerializer.Serialize(
            TechPatternRecognitionEngine.FindMatches(map.HitObjects));

        RhythmTransitionPatternAnalysis first =
            TechPatternAnalyzer.AnalyzeRhythmTransition(map.HitObjects);
        RhythmTransitionPatternAnalysis second =
            TechPatternAnalyzer.AnalyzeRhythmTransition(map.HitObjects);

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Equal(profileBefore, JsonSerializer.Serialize(profile));
        Assert.Equal(ratingBefore, map.BeatInsightRating);
        Assert.Equal(techV2Before, SerializeTechV2(map));
        Assert.Equal(historicalBefore, JsonSerializer.Serialize(
            TechPatternRecognitionEngine.FindMatches(map.HitObjects)));
        Assert.Same(profile, map.GameplayProfile);
    }

    [Fact]
    public void DefaultsScoreAggregatesAndDebugSwitchesMatchV1()
    {
        var options = new RhythmTransitionPatternOptions();
        RhythmTransitionPatternAnalysis result = Analyze([100, 100, 50, 50]);
        RhythmTransitionPatternOccurrence transition = Assert.Single(result.Transitions);
        double expectedScore = ((50.0 / 60) + 1 + 1) / 3;

        Assert.Equal(2, options.RhythmWindowSize);
        Assert.Equal(20, options.MinimumAbsoluteIntervalChange);
        Assert.Equal(0.25, options.MinimumRelativeIntervalChange);
        Assert.Equal(0.75, options.MinimumRhythmSideConsistency);
        Assert.Equal(1000, options.MaximumRhythmInterval);
        Assert.Equal(expectedScore, transition.RhythmTransitionScore, 10);
        Assert.Equal(50, result.AverageAbsoluteIntervalChange);
        Assert.Equal(0.5, result.AverageRelativeIntervalChange, 10);
        Assert.Equal(1, result.AverageBeforeConsistency);
        Assert.Equal(1, result.AverageAfterConsistency);
        Assert.Equal(expectedScore, result.AverageTransitionScore, 10);
        Assert.Equal(expectedScore, result.MaximumTransitionScore, 10);
        Assert.False(GameplayDebug.RhythmTransitionPatternEnabled);
        Assert.False(GameplayDebug.RhythmTransitionPatternDetailedEnabled);
        GameplayDebug.RhythmTransitionPattern(result);
    }

    private static RhythmTransitionPatternAnalysis Analyze(IReadOnlyList<int> intervals) =>
        TechPatternAnalyzer.AnalyzeRhythmTransition(Map(intervals).HitObjects);

    private static RhythmTransitionPatternOccurrence Pivot(
        RhythmTransitionPatternAnalysis result,
        int pivotIndex) =>
        Assert.Single(result.Occurrences, occurrence => occurrence.PivotIndex == pivotIndex);

    private static Beatmap Map(IReadOnlyList<int> intervals, string? pattern = null)
    {
        int objectCount = intervals.Count + 1;
        string types = pattern ?? new string('C', objectCount);
        if (types.Length != objectCount)
            throw new ArgumentException("Pattern length must equal interval count plus one.", nameof(pattern));

        var map = new Beatmap();
        int time = 0;
        for (int index = 0; index < objectCount; index++)
        {
            if (index > 0)
                time += intervals[index - 1];
            map.HitObjects.Add(types[index] switch
            {
                'C' => new HitObject { Type = 1, X = index * 100, Y = 0, Time = time },
                'S' => Slider(index * 100, time),
                _ => throw new ArgumentOutOfRangeException(nameof(pattern))
            });
        }
        return map;
    }

    private static HitObject Slider(int x, int time) => new()
    {
        Type = 2,
        X = x,
        Y = 0,
        Time = time,
        Slides = 1,
        Length = 100,
        SliderCurveType = "L",
        SliderControlPoints = [new SliderControlPoint { X = x + 100, Y = 0 }],
        SliderEndPosition = new SliderControlPoint { X = x + 100, Y = 0 }
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
