using System.Text.Json;
using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Analysis.StreamPatterns;
using BeatInsight.Analysis.TechPatterns;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.TechPatterns;

namespace BeatInsight.Tests.Analysis;

public sealed class SliderDirectionChangePatternAnalyzerTests
{
    [Fact]
    public void SliderAloneHasNoReliableTransition()
    {
        SliderDirectionChangePatternOccurrence occurrence = Only([Slider()]);

        Assert.False(occurrence.Qualified);
        Assert.Null(occurrence.IncomingToSliderAngle);
        Assert.Null(occurrence.SliderToOutgoingAngle);
        Assert.Equal("NoReliableTransition", occurrence.Reason);
    }

    [Fact]
    public void AlignedCircleToSliderHasNoEntryChange()
    {
        SliderDirectionChangePatternOccurrence occurrence = Only([
            Circle(-100, 0), Slider()
        ]);

        Assert.Equal(0, occurrence.IncomingToSliderAngle!.Value, 10);
        Assert.False(occurrence.EntryDirectionChange);
        Assert.Equal("BelowDirectionThreshold", occurrence.Reason);
    }

    [Fact]
    public void AlignedSliderToCircleHasNoExitChange()
    {
        SliderDirectionChangePatternOccurrence occurrence = Only([
            Slider(), Circle(200, 0, 100)
        ]);

        Assert.Equal(0, occurrence.SliderToOutgoingAngle!.Value, 10);
        Assert.False(occurrence.ExitDirectionChange);
        Assert.Equal("BelowDirectionThreshold", occurrence.Reason);
    }

    [Fact]
    public void EntryAngleAtInclusiveSixtyDegreeBoundaryIsAccepted()
    {
        HitObject[] objects = [Circle(-1_000_000, -1_732_051), Slider()];
        double measured = Only(objects).IncomingToSliderAngle!.Value;
        var options = new SliderDirectionChangePatternOptions(
            MinimumDirectionChangeAngle: measured);

        SliderDirectionChangePatternOccurrence occurrence = Only(objects, options);

        Assert.Equal(60, measured, 3);
        Assert.True(occurrence.EntryDirectionChange);
        Assert.True(occurrence.Qualified);
    }

    [Fact]
    public void ExitAngleAtInclusiveSixtyDegreeBoundaryIsAccepted()
    {
        HitObject[] objects = [Slider(), Circle(1_000_100, 1_732_051, 100)];
        double measured = Only(objects).SliderToOutgoingAngle!.Value;
        var options = new SliderDirectionChangePatternOptions(
            MinimumDirectionChangeAngle: measured);

        SliderDirectionChangePatternOccurrence occurrence = Only(objects, options);

        Assert.Equal(60, measured, 3);
        Assert.True(occurrence.ExitDirectionChange);
        Assert.True(occurrence.Qualified);
    }

    [Fact]
    public void AngleJustBelowSixtyDegreesIsRejected()
    {
        SliderDirectionChangePatternOccurrence occurrence = Only([
            Circle(-1_000_000, -1_732_050), Slider()
        ]);

        Assert.True(occurrence.IncomingToSliderAngle < 60);
        Assert.False(occurrence.Qualified);
        Assert.Equal("BelowDirectionThreshold", occurrence.Reason);
    }

    [Fact]
    public void StrongEntryOnlyIsDetected()
    {
        SliderDirectionChangePatternOccurrence occurrence = Only([
            Circle(0, -100), Slider(), Circle(200, 0, 100)
        ]);

        Assert.True(occurrence.EntryDirectionChange);
        Assert.False(occurrence.ExitDirectionChange);
        Assert.Equal(1, occurrence.DirectionChangeCount);
    }

    [Fact]
    public void StrongExitOnlyIsDetected()
    {
        SliderDirectionChangePatternOccurrence occurrence = Only([
            Circle(-100, 0), Slider(), Circle(100, 100, 100)
        ]);

        Assert.False(occurrence.EntryDirectionChange);
        Assert.True(occurrence.ExitDirectionChange);
        Assert.Equal(1, occurrence.DirectionChangeCount);
    }

    [Fact]
    public void StrongEntryAndExitAreBothDetected()
    {
        SliderDirectionChangePatternOccurrence occurrence = Only([
            Circle(0, -100), Slider(), Circle(100, 100, 100)
        ]);

        Assert.True(occurrence.EntryDirectionChange);
        Assert.True(occurrence.ExitDirectionChange);
        Assert.Equal(2, occurrence.DirectionChangeCount);
        Assert.Equal(90, occurrence.AverageDirectionChangeAngle, 10);
    }

    [Fact]
    public void FirstObjectSliderCanUseExitOnly()
    {
        SliderDirectionChangePatternOccurrence occurrence = Only([
            Slider(), Circle(100, 100, 100)
        ]);

        Assert.Null(occurrence.PreviousObjectIndex);
        Assert.Null(occurrence.IncomingToSliderAngle);
        Assert.True(occurrence.ExitDirectionChange);
    }

    [Fact]
    public void LastObjectSliderCanUseEntryOnly()
    {
        SliderDirectionChangePatternOccurrence occurrence = Only([
            Circle(0, -100), Slider()
        ]);

        Assert.Null(occurrence.NextObjectIndex);
        Assert.Null(occurrence.SliderToOutgoingAngle);
        Assert.True(occurrence.EntryDirectionChange);
    }

    [Fact]
    public void TransitionBelowMinimumDistanceIsUnreliable()
    {
        SliderDirectionChangePatternOccurrence occurrence = Only([
            Circle(0, -19), Slider()
        ]);

        Assert.Null(occurrence.IncomingToSliderAngle);
        Assert.False(occurrence.Qualified);
        Assert.Equal("NoReliableTransition", occurrence.Reason);
    }

    [Fact]
    public void TransitionAtMinimumDistanceIsReliable()
    {
        SliderDirectionChangePatternOccurrence occurrence = Only([
            Circle(0, -20), Slider()
        ]);

        Assert.Equal(90, occurrence.IncomingToSliderAngle!.Value, 10);
        Assert.True(occurrence.Qualified);
    }

    [Fact]
    public void RepeatUsesActualFinalPositionAndReversedTerminalDirection()
    {
        SliderDirectionChangePatternOccurrence occurrence = Only([
            Slider(repeatCount: 1), Circle(-100, 0, 100)
        ]);

        Assert.Equal(new SliderPatternPosition(0, 0), occurrence.SliderFinalPosition);
        Assert.Equal(0, occurrence.SliderToOutgoingAngle!.Value, 10);
        Assert.False(occurrence.ExitDirectionChange);
    }

    [Fact]
    public void RepeatKeepsBasePathEndDistinctFromFinalPosition()
    {
        SliderDirectionChangePatternOccurrence occurrence = Only([
            Slider(repeatCount: 1)
        ]);

        Assert.Equal(new SliderPatternPosition(100, 0), occurrence.SliderBaseEndPosition);
        Assert.Equal(new SliderPatternPosition(0, 0), occurrence.SliderFinalPosition);
        Assert.NotEqual(occurrence.SliderBaseEndPosition, occurrence.SliderFinalPosition);
    }

    [Fact]
    public void ScoreAndConfidenceAreBounded()
    {
        SliderDirectionChangePatternAnalysis result = Analyze([
            Circle(100, 0), Slider(), Circle(0, 0, 100)
        ]);

        Assert.All(result.Occurrences, occurrence =>
            Assert.InRange(occurrence.DirectionChangeScore, 0, 1));
        Assert.InRange(result.Confidence, 0, 1);
    }

    [Fact]
    public void CoverageDeduplicatesObjectsSharedByTwoQualifiedSliders()
    {
        HitObject first = Slider(startX: 0, startY: 0, endX: 100, endY: 0);
        HitObject second = Slider(startX: 100, startY: 100, endX: 200, endY: 100, time: 100);

        SliderDirectionChangePatternAnalysis result = Analyze([first, second]);

        Assert.Equal(2, result.DirectionChangeSliderCount);
        Assert.Equal(2, result.DirectionChangeObjectCount);
        Assert.Equal(1, result.Coverage, 10);
    }

    [Fact]
    public void SliderCoverageUsesAllSlidersAsDenominator()
    {
        SliderDirectionChangePatternAnalysis result = Analyze([
            Circle(0, -100), Slider(),
            Slider(startX: 300, startY: 0, endX: 400, endY: 0, time: 200)
        ]);

        Assert.Equal(2, result.TotalSliders);
        Assert.Equal(1, result.DirectionChangeSliderCount);
        Assert.Equal(0.5, result.SliderCoverage, 10);
    }

    [Fact]
    public void GlobalCountsAnglesAndConfidenceUseQualifiedChanges()
    {
        SliderDirectionChangePatternAnalysis result = Analyze([
            Circle(0, -100), Slider(), Circle(100, 100, 100)
        ]);

        Assert.Equal(1, result.DirectionChangeSequenceCount);
        Assert.Equal(1, result.EntryChangeCount);
        Assert.Equal(1, result.ExitChangeCount);
        Assert.Equal(1, result.BothChangeCount);
        Assert.Equal(90, result.AverageDirectionChangeAngle, 10);
        Assert.Equal(90, result.MaximumDirectionChangeAngle, 10);
        Assert.Equal(0.5, result.AverageDirectionChangeScore, 10);
        Assert.Equal((0.5 + 1 + 1 + 0.5) / 4, result.Confidence, 10);
    }

    [Fact]
    public void DirectionChangeCanCoexistWithSliderChain()
    {
        HitObject[] objects = [
            Circle(0, -100),
            Slider(time: 100),
            Slider(startX: 200, endX: 300, time: 200),
            Slider(startX: 400, endX: 500, time: 300)
        ];

        Assert.Single(TechPatternAnalyzer.AnalyzeSliderChain(objects).Sequences);
        Assert.NotEmpty(Analyze(objects).QualifiedOccurrences);
    }

    [Fact]
    public void DirectionChangeCanCoexistWithComplexSlider()
    {
        HitObject complex = StrongComplexSlider(time: 100);
        HitObject[] objects = [Circle(0, -100), complex];

        Assert.Single(TechPatternAnalyzer.AnalyzeComplexSlider(objects).ComplexSliders);
        Assert.Single(Analyze(objects).QualifiedOccurrences);
    }

    [Fact]
    public void RunningDirectionChangeLeavesSliderChainAndComplexSliderUnchanged()
    {
        HitObject[] objects = [
            Circle(0, -100), StrongComplexSlider(100),
            Slider(startX: 200, endX: 300, time: 200),
            Slider(startX: 400, endX: 500, time: 300)
        ];
        string chainBefore = JsonSerializer.Serialize(TechPatternAnalyzer.AnalyzeSliderChain(objects));
        string complexBefore = JsonSerializer.Serialize(TechPatternAnalyzer.AnalyzeComplexSlider(objects));

        _ = Analyze(objects);

        Assert.Equal(chainBefore, JsonSerializer.Serialize(TechPatternAnalyzer.AnalyzeSliderChain(objects)));
        Assert.Equal(complexBefore, JsonSerializer.Serialize(TechPatternAnalyzer.AnalyzeComplexSlider(objects)));
    }

    [Fact]
    public void RunningDirectionChangeLeavesStreamAndJumpV1Unchanged()
    {
        Beatmap map = Map([
            Circle(0, -100), Slider(time: 100), Circle(100, 100, 200), Circle(200, 100, 300)
        ]);
        GameplayProfile profile = GameplayAnalyzer.Analyze(map);
        string streamBefore = SerializeStreamPatterns(map);
        string jumpBefore = JsonSerializer.Serialize(JumpPatternAnalyzer.Analyze(map.HitObjects, profile));

        _ = TechPatternAnalyzer.AnalyzeSliderDirectionChange(map);

        Assert.Equal(streamBefore, SerializeStreamPatterns(map));
        Assert.Equal(jumpBefore, JsonSerializer.Serialize(JumpPatternAnalyzer.Analyze(map.HitObjects, profile)));
    }

    [Fact]
    public void HistoricalTechTechV2IdentityRatingAndUiStateRemainUnchanged()
    {
        Beatmap map = Map([Circle(0, -100), StrongComplexSlider(100), Circle(0, 100, 200)]);
        GameplayProfile profile = GameplayAnalyzer.Analyze(map);
        string profileBefore = JsonSerializer.Serialize(profile);
        double ratingBefore = map.BeatInsightRating;
        string techV2Before = SerializeTechV2(map);
        string historicalBefore = JsonSerializer.Serialize(
            TechPatternRecognitionEngine.FindMatches(map.HitObjects));

        SliderDirectionChangePatternAnalysis first =
            TechPatternAnalyzer.AnalyzeSliderDirectionChange(map);
        SliderDirectionChangePatternAnalysis second =
            TechPatternAnalyzer.AnalyzeSliderDirectionChange(map);

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Equal(profileBefore, JsonSerializer.Serialize(profile));
        Assert.Equal(ratingBefore, map.BeatInsightRating);
        Assert.Equal(techV2Before, SerializeTechV2(map));
        Assert.Equal(historicalBefore, JsonSerializer.Serialize(
            TechPatternRecognitionEngine.FindMatches(map.HitObjects)));
        Assert.Same(profile, map.GameplayProfile);
    }

    [Fact]
    public void DefaultsDurationAndDebugSwitchesMatchV1()
    {
        var options = new SliderDirectionChangePatternOptions();
        Beatmap map = Map([Slider(), Circle(100, 100, 100)]);
        map.SliderMultiplier = 1.4;
        map.TimingPoints.Add(new TimingPoint { Time = 0, BeatLength = 500, Uninherited = true });

        SliderDirectionChangePatternOccurrence occurrence = Assert.Single(
            TechPatternAnalyzer.AnalyzeSliderDirectionChange(map).Occurrences);

        Assert.Equal(60, options.MinimumDirectionChangeAngle);
        Assert.Equal(20, options.MinimumTransitionDistance);
        Assert.NotNull(occurrence.EndTime);
        Assert.False(GameplayDebug.SliderDirectionChangePatternEnabled);
        Assert.False(GameplayDebug.SliderDirectionChangePatternDetailedEnabled);
        GameplayDebug.SliderDirectionChangePattern(
            TechPatternAnalyzer.AnalyzeSliderDirectionChange(map));
    }

    private static SliderDirectionChangePatternAnalysis Analyze(
        IReadOnlyList<HitObject> objects,
        SliderDirectionChangePatternOptions? options = null) =>
        TechPatternAnalyzer.AnalyzeSliderDirectionChange(objects, options);

    private static SliderDirectionChangePatternOccurrence Only(
        IReadOnlyList<HitObject> objects,
        SliderDirectionChangePatternOptions? options = null) =>
        Assert.Single(Analyze(objects, options).Occurrences);

    private static HitObject Circle(int x, int y, int time = 0) => new()
    {
        Type = 1,
        X = x,
        Y = y,
        Time = time
    };

    private static HitObject Slider(
        int startX = 0,
        int startY = 0,
        int endX = 100,
        int endY = 0,
        int time = 0,
        int repeatCount = 0)
    {
        bool returnsToStart = repeatCount % 2 == 1;
        return new HitObject
        {
            Type = 2,
            X = startX,
            Y = startY,
            Time = time,
            Slides = repeatCount + 1,
            Length = Math.Sqrt(
                Math.Pow(endX - startX, 2) +
                Math.Pow(endY - startY, 2)),
            SliderCurveType = "L",
            SliderControlPoints =
            [
                new SliderControlPoint { X = endX, Y = endY }
            ],
            SliderEndPosition = new SliderControlPoint
            {
                X = returnsToStart ? startX : endX,
                Y = returnsToStart ? startY : endY
            }
        };
    }

    private static HitObject StrongComplexSlider(int time = 0) => new()
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

    private static Beatmap Map(IEnumerable<HitObject> objects)
    {
        var map = new Beatmap();
        map.HitObjects.AddRange(objects);
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
