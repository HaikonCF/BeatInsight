using System.Text.Json;
using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Analysis.StreamPatterns;
using BeatInsight.Analysis.TechPatterns;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.TechPatterns;

namespace BeatInsight.Tests.Analysis;

public sealed class OverlapPatternAnalyzerTests
{
    [Fact]
    public void FewerThanTwoObjectsProduceNoLinks()
    {
        Assert.Empty(Analyze().Links);
        Assert.Empty(Analyze(Circle(0, 0, 0)).Links);
    }

    [Fact]
    public void SpinnerBreaksContinuity()
    {
        OverlapPatternAnalysis result = Analyze(
            Circle(0, 0, 0), Spinner(100), Circle(0, 0, 200));

        Assert.All(result.Links, link => Assert.Equal("IntervalBreak", link.Reason));
        Assert.Empty(result.Runs);
    }

    [Fact]
    public void NegativeGapBreaksContinuity()
    {
        OverlapPatternLink link = OnlyLink(Circle(0, 0, 100), Circle(0, 0, 99));

        Assert.Equal("IntervalBreak", link.Reason);
        Assert.False(link.Qualified);
    }

    [Fact]
    public void GapAboveOneSecondBreaksContinuity()
    {
        OverlapPatternLink link = OnlyLink(Circle(0, 0, 0), Circle(0, 0, 1001));

        Assert.Equal("IntervalBreak", link.Reason);
    }

    [Fact]
    public void OneSecondGapBoundaryIsValid()
    {
        OverlapPatternLink link = OnlyLink(Circle(0, 0, 0), Circle(0, 0, 1000));

        Assert.True(link.Qualified);
        Assert.Equal("QualifiedLink", link.Reason);
    }

    [Fact]
    public void ZeroDistanceHasMaximumStrength()
    {
        OverlapPatternLink link = OnlyLink(Circle(0, 0, 0), Circle(0, 0, 100));

        Assert.Equal(0, link.Distance);
        Assert.Equal(1, link.OverlapStrength);
    }

    [Fact]
    public void TwentyPixelsHasHalfStrength()
    {
        OverlapPatternLink link = OnlyLink(Circle(0, 0, 0), Circle(20, 0, 100));

        Assert.Equal(20, link.Distance);
        Assert.Equal(0.5, link.OverlapStrength);
    }

    [Fact]
    public void FortyPixelBoundaryIsInclusiveWithZeroStrength()
    {
        OverlapPatternLink link = OnlyLink(Circle(0, 0, 0), Circle(40, 0, 100));

        Assert.True(link.Qualified);
        Assert.Equal(0, link.OverlapStrength);
    }

    [Fact]
    public void DistanceJustAboveFortyIsRejected()
    {
        OverlapPatternLink link = OnlyLink(Circle(0, 0, 0), Circle(40, 1, 100));

        Assert.True(link.Distance > 40);
        Assert.False(link.Qualified);
        Assert.Equal("AboveOverlapDistance", link.Reason);
    }

    [Fact]
    public void IsolatedOverlapLinkRemainsDiagnosticOnly()
    {
        OverlapPatternAnalysis result = Analyze(Circle(0, 0, 0), Circle(10, 0, 100));
        OverlapPatternSequence run = Assert.Single(result.Runs);

        Assert.Equal(1, result.OverlapLinkCount);
        Assert.Empty(result.Sequences);
        Assert.False(run.Qualified);
        Assert.Equal("TooFewOverlapLinks", run.Reason);
    }

    [Fact]
    public void TwoConsecutiveLinksQualifyOneSequence()
    {
        OverlapPatternSequence sequence = Assert.Single(Analyze(
            Circle(0, 0, 0), Circle(10, 0, 100), Circle(25, 0, 200)).Sequences);

        Assert.Equal(2, sequence.LinkCount);
        Assert.Equal(3, sequence.ObjectCount);
    }

    [Fact]
    public void ThreeConsecutiveLinksFormOneMaximalSequence()
    {
        OverlapPatternSequence sequence = Assert.Single(Analyze(
            Circle(0, 0, 0), Circle(10, 0, 100),
            Circle(20, 0, 200), Circle(30, 0, 300)).Sequences);

        Assert.Equal(3, sequence.LinkCount);
        Assert.Equal(0, sequence.StartIndex);
        Assert.Equal(3, sequence.EndIndex);
    }

    [Fact]
    public void NonOverlapLinkCutsTheSequence()
    {
        OverlapPatternAnalysis result = Analyze(
            Circle(0, 0, 0), Circle(10, 0, 100), Circle(20, 0, 200),
            Circle(100, 0, 300), Circle(110, 0, 400));

        Assert.Equal(2, result.Runs.Count);
        Assert.Single(result.Sequences);
        Assert.Equal((0, 2), (result.Sequences[0].StartIndex, result.Sequences[0].EndIndex));
    }

    [Fact]
    public void TwoSeparatedSequencesRemainTwoSequences()
    {
        OverlapPatternAnalysis result = Analyze(
            Circle(0, 0, 0), Circle(10, 0, 100), Circle(20, 0, 200),
            Circle(100, 0, 300), Circle(110, 0, 400), Circle(120, 0, 500));

        Assert.Equal(2, result.QualifiedOverlapSequenceCount);
        Assert.Equal(new[] { 0, 3 }, result.Sequences.Select(sequence => sequence.StartIndex));
    }

    [Fact]
    public void CircleToCircleTypeIsExposed() =>
        Assert.Equal(OverlapTransitionType.CircleCircle,
            OnlyLink(Circle(0, 0, 0), Circle(0, 0, 100)).TransitionType);

    [Fact]
    public void CircleToSliderTypeIsExposed() =>
        Assert.Equal(OverlapTransitionType.CircleSlider,
            OnlyLink(Circle(0, 0, 0), Slider(0, 0, 0, 0, 100)).TransitionType);

    [Fact]
    public void SliderToCircleTypeIsExposed() =>
        Assert.Equal(OverlapTransitionType.SliderCircle,
            OnlyLink(Slider(0, 0, 0, 0, 0), Circle(0, 0, 100)).TransitionType);

    [Fact]
    public void SliderToSliderTypeIsExposed() =>
        Assert.Equal(OverlapTransitionType.SliderSlider,
            OnlyLink(Slider(0, 0, 0, 0, 0), Slider(0, 0, 0, 0, 100)).TransitionType);

    [Fact]
    public void SequenceCountsEveryTransitionType()
    {
        OverlapPatternSequence sequence = Assert.Single(Analyze(
            Circle(0, 0, 0),
            Slider(0, 0, 0, 0, 100),
            Slider(0, 0, 0, 0, 200),
            Circle(0, 0, 300),
            Circle(0, 0, 400)).Sequences);

        Assert.Equal(1, sequence.CircleCircleCount);
        Assert.Equal(1, sequence.CircleSliderCount);
        Assert.Equal(1, sequence.SliderCircleCount);
        Assert.Equal(1, sequence.SliderSliderCount);
    }

    [Fact]
    public void SliderFinalPositionDrivesOverlapDistance()
    {
        OverlapPatternLink link = OnlyLink(
            Slider(0, 0, 100, 0, 0), Circle(110, 0, 100));

        Assert.Equal(10, link.Distance);
        Assert.True(link.Qualified);
    }

    [Fact]
    public void StrengthIsAlwaysBounded()
    {
        OverlapPatternAnalysis result = Analyze(
            Circle(0, 0, 0), Circle(0, 0, 100),
            Circle(20, 0, 200), Circle(100, 0, 300));

        Assert.All(result.Links.Where(link => link.OverlapStrength.HasValue), link =>
            Assert.InRange(link.OverlapStrength!.Value, 0, 1));
    }

    [Fact]
    public void SequenceScoreIsAlwaysBounded()
    {
        OverlapPatternAnalysis result = Analyze(
            Circle(0, 0, 0), Circle(0, 0, 100),
            Circle(20, 0, 200), Circle(40, 0, 300));

        Assert.All(result.Runs, sequence =>
            Assert.InRange(sequence.OverlapSequenceScore, 0, 1));
    }

    [Fact]
    public void CoverageDeduplicatesObjectsWithinQualifiedSequence()
    {
        OverlapPatternAnalysis result = Analyze(
            Circle(0, 0, 0), Circle(10, 0, 100),
            Circle(20, 0, 200), Circle(100, 0, 300));

        Assert.Equal(3, result.QualifiedOverlapObjectCount);
        Assert.Equal(0.75, result.Coverage, 10);
    }

    [Fact]
    public void AverageDistanceUsesQualifiedOverlapLinks()
    {
        OverlapPatternAnalysis result = Analyze(
            Circle(0, 0, 0), Circle(10, 0, 100), Circle(30, 0, 200));

        Assert.Equal(15, result.AverageDistance, 10);
    }

    [Fact]
    public void MinimumDistanceUsesQualifiedOverlapLinks()
    {
        OverlapPatternAnalysis result = Analyze(
            Circle(0, 0, 0), Circle(10, 0, 100), Circle(30, 0, 200));

        Assert.Equal(10, result.MinimumDistance, 10);
    }

    [Fact]
    public void AverageStrengthUsesQualifiedOverlapLinks()
    {
        OverlapPatternAnalysis result = Analyze(
            Circle(0, 0, 0), Circle(10, 0, 100), Circle(30, 0, 200));

        Assert.Equal(0.625, result.AverageOverlapStrength, 10);
    }

    [Fact]
    public void AverageSequenceLengthUsesQualifiedObjectCounts()
    {
        OverlapPatternAnalysis result = Analyze(
            Circle(0, 0, 0), Circle(10, 0, 100), Circle(20, 0, 200),
            Circle(100, 0, 300), Circle(110, 0, 400),
            Circle(120, 0, 500), Circle(130, 0, 600));

        Assert.Equal(3.5, result.AverageSequenceLength, 10);
    }

    [Fact]
    public void ConfidenceUsesFrozenDescriptiveFormulaAndIsBounded()
    {
        OverlapPatternAnalysis result = Analyze(
            Circle(0, 0, 0), Circle(10, 0, 100), Circle(20, 0, 200));
        double expected = (0.5 + 1 + 0.75) / 3;

        Assert.Equal(expected, result.Confidence, 10);
        Assert.InRange(result.Confidence, 0, 1);
    }

    [Fact]
    public void SequenceScoreUsesStrengthLengthAndConsistency()
    {
        OverlapPatternSequence sequence = Assert.Single(Analyze(
            Circle(0, 0, 0), Circle(10, 0, 100), Circle(20, 0, 200)).Sequences);
        double expected = (0.75 + 0.4 + 1) / 3;

        Assert.Equal(expected, sequence.OverlapSequenceScore, 10);
    }

    [Fact]
    public void OverlapCoexistsWithCircleSliderTransition()
    {
        HitObject[] objects =
        [
            Circle(0, 0, 0), Slider(0, 0, 0, 0, 100),
            Circle(0, 0, 200), Slider(0, 0, 0, 0, 300)
        ];

        Assert.Single(Analyze(objects).Sequences);
        Assert.Single(TechPatternAnalyzer.AnalyzeCircleSliderTransition(objects).Sequences);
    }

    [Fact]
    public void OverlapCoexistsWithRhythmTransition()
    {
        HitObject[] objects = ObjectsAtIntervals([100, 100, 100, 50, 50, 50]);

        Assert.Single(Analyze(objects).Sequences);
        Assert.Single(TechPatternAnalyzer.AnalyzeRhythmTransition(objects).Transitions);
    }

    [Fact]
    public void OverlapCoexistsWithSliderDirectionChange()
    {
        HitObject[] objects =
        [
            Circle(0, -100, 0), Slider(0, 0, 100, 0, 100), Circle(100, 100, 200),
            Circle(500, 0, 300), Circle(510, 0, 400), Circle(520, 0, 500)
        ];

        Assert.Single(Analyze(objects).Sequences);
        Assert.NotEmpty(TechPatternAnalyzer.AnalyzeSliderDirectionChange(objects).QualifiedOccurrences);
    }

    [Fact]
    public void OverlapCoexistsWithSpatialAwkwardTransition()
    {
        HitObject[] objects =
        [
            Circle(-120, 0, 0), Circle(0, 0, 100), Circle(-120, 0, 200),
            Circle(1000, 0, 1500), Circle(1010, 0, 1600), Circle(1020, 0, 1700)
        ];

        Assert.Single(Analyze(objects).Sequences);
        Assert.NotEmpty(TechPatternAnalyzer.AnalyzeSpatialAwkwardTransition(objects).Transitions);
    }

    [Fact]
    public void StrongSpatialAwkwardCanExistWithoutOverlap()
    {
        HitObject[] objects =
        [
            Circle(-300, 0, 0), Circle(0, 0, 100), Circle(-60, 0, 200)
        ];

        Assert.Empty(Analyze(objects).Sequences);
        Assert.Single(TechPatternAnalyzer.AnalyzeSpatialAwkwardTransition(objects).Transitions);
    }

    [Fact]
    public void StrongOverlapCanExistWithoutSpatialAwkward()
    {
        HitObject[] objects =
        [
            Circle(0, 0, 0), Circle(10, 0, 100), Circle(20, 0, 200)
        ];

        Assert.Single(Analyze(objects).Sequences);
        Assert.Empty(TechPatternAnalyzer.AnalyzeSpatialAwkwardTransition(objects).Transitions);
    }

    [Fact]
    public void OverlapCoexistsWithSliderChain()
    {
        HitObject[] objects =
        [
            Slider(0, 0, 0, 0, 0), Slider(0, 0, 0, 0, 100), Slider(0, 0, 0, 0, 200)
        ];

        Assert.Single(Analyze(objects).Sequences);
        Assert.Single(TechPatternAnalyzer.AnalyzeSliderChain(objects).Sequences);
    }

    [Fact]
    public void OverlapCoexistsWithComplexSlider()
    {
        HitObject[] objects =
        [
            StrongComplexSlider(0), Circle(0, 0, 100), Circle(0, 0, 200)
        ];

        Assert.Single(Analyze(objects).Sequences);
        Assert.Single(TechPatternAnalyzer.AnalyzeComplexSlider(objects).ComplexSliders);
    }

    [Fact]
    public void ExistingTechPatternsRemainUnchanged()
    {
        Beatmap map = Map(ObjectsAtIntervals([100, 100, 50, 50]));
        string before = SerializeExistingTechPatterns(map);

        _ = TechPatternAnalyzer.AnalyzeOverlap(map.HitObjects);

        Assert.Equal(before, SerializeExistingTechPatterns(map));
    }

    [Fact]
    public void JumpPatternV1RemainsUnchanged()
    {
        Beatmap map = Map(ObjectsAtIntervals([100, 100, 50, 50, 80, 40, 80]));
        GameplayProfile profile = GameplayAnalyzer.Analyze(map);
        string before = JsonSerializer.Serialize(JumpPatternAnalyzer.Analyze(map.HitObjects, profile));

        _ = TechPatternAnalyzer.AnalyzeOverlap(map.HitObjects);

        Assert.Equal(before, JsonSerializer.Serialize(JumpPatternAnalyzer.Analyze(map.HitObjects, profile)));
    }

    [Fact]
    public void StreamPatternV1RemainsUnchanged()
    {
        Beatmap map = Map(ObjectsAtIntervals([100, 100, 50, 50, 80, 40, 80]));
        string before = SerializeStreamPatterns(map);

        _ = TechPatternAnalyzer.AnalyzeOverlap(map.HitObjects);

        Assert.Equal(before, SerializeStreamPatterns(map));
    }

    [Fact]
    public void HistoricalTechTechV2IdentityRatingAndUiRemainUnchanged()
    {
        Beatmap map = Map(
            Circle(0, 0, 0), Circle(10, 0, 100), Circle(20, 0, 200));
        GameplayProfile profile = GameplayAnalyzer.Analyze(map);
        string profileBefore = JsonSerializer.Serialize(profile);
        string historicalBefore = JsonSerializer.Serialize(
            TechPatternRecognitionEngine.FindMatches(map.HitObjects));
        string techV2Before = SerializeTechV2(map);
        double ratingBefore = map.BeatInsightRating;

        OverlapPatternAnalysis first = TechPatternAnalyzer.AnalyzeOverlap(map.HitObjects);
        OverlapPatternAnalysis second = TechPatternAnalyzer.AnalyzeOverlap(map.HitObjects);

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Equal(profileBefore, JsonSerializer.Serialize(profile));
        Assert.Equal(historicalBefore, JsonSerializer.Serialize(
            TechPatternRecognitionEngine.FindMatches(map.HitObjects)));
        Assert.Equal(techV2Before, SerializeTechV2(map));
        Assert.Equal(ratingBefore, map.BeatInsightRating);
        Assert.Same(profile, map.GameplayProfile);
    }

    [Fact]
    public void DefaultsAndDebugSwitchesMatchV1()
    {
        var options = new OverlapPatternOptions();
        OverlapPatternAnalysis result = Analyze(
            Circle(0, 0, 0), Circle(10, 0, 100), Circle(20, 0, 200));

        Assert.Equal(40, options.MaximumOverlapDistance);
        Assert.Equal(1000, options.MaximumOverlapGap);
        Assert.Equal(2, options.MinimumOverlapSequenceLinks);
        Assert.False(GameplayDebug.OverlapPatternEnabled);
        Assert.False(GameplayDebug.OverlapPatternDetailedEnabled);
        GameplayDebug.OverlapPattern(result);
    }

    private static OverlapPatternAnalysis Analyze(params HitObject[] objects) =>
        TechPatternAnalyzer.AnalyzeOverlap(objects);

    private static OverlapPatternLink OnlyLink(params HitObject[] objects) =>
        Assert.Single(Analyze(objects).Links);

    private static HitObject Circle(int x, int y, int time) => new()
    {
        Type = 1,
        X = x,
        Y = y,
        Time = time
    };

    private static HitObject Spinner(int time) => new()
    {
        Type = 8,
        Time = time
    };

    private static HitObject Slider(
        int startX,
        int startY,
        int endX,
        int endY,
        int time) => new()
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
        X = 0,
        Y = 0,
        Time = time,
        Slides = 4,
        Length = 240,
        SliderCurveType = "B",
        SliderControlPoints =
        [
            new SliderControlPoint { X = 50, Y = 80 },
            new SliderControlPoint { X = 100, Y = -80 },
            new SliderControlPoint { X = 150, Y = 0 }
        ],
        SliderEndPosition = new SliderControlPoint { X = 0, Y = 0 }
    };

    private static HitObject[] ObjectsAtIntervals(IReadOnlyList<int> intervals)
    {
        var objects = new List<HitObject> { Circle(0, 0, 0) };
        int time = 0;
        foreach (int interval in intervals)
        {
            time += interval;
            objects.Add(Circle(0, 0, time));
        }
        return objects.ToArray();
    }

    private static Beatmap Map(params HitObject[] objects) => new()
    {
        CS = 4,
        SliderMultiplier = 1.4,
        TimingPoints = [new TimingPoint { Time = 0, BeatLength = 500, Uninherited = true }],
        HitObjects = objects.ToList()
    };

    private static string SerializeExistingTechPatterns(Beatmap map) => JsonSerializer.Serialize(new object[]
    {
        TechPatternAnalyzer.AnalyzeSliderChain(map.HitObjects),
        TechPatternAnalyzer.AnalyzeComplexSlider(map),
        TechPatternAnalyzer.AnalyzeSliderDirectionChange(map),
        TechPatternAnalyzer.AnalyzeCircleSliderTransition(map.HitObjects),
        TechPatternAnalyzer.AnalyzeRhythmTransition(map.HitObjects),
        TechPatternAnalyzer.AnalyzeSpatialAwkwardTransition(map.HitObjects)
    });

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
