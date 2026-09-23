using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Models;
using BeatInsight.Models.JumpPatterns;

namespace BeatInsight.Tests.Analysis;

public sealed class OneTwoJumpPatternTests
{
    private static Beatmap Map(params (int X, int Y)[] points) => new()
    {
        CS = 4, SliderMultiplier = 1.4,
        TimingPoints = [new() { Time = 0, BeatLength = 500, Uninherited = true }],
        HitObjects = points.Select((point, index) => new HitObject
            { X = point.X, Y = point.Y, Time = index * 200, Type = 1 }).ToList()
    };

    private static OneTwoJumpAnalysis Analyze(Beatmap map) =>
        JumpPatternAnalyzer.AnalyzeOneTwo(map.HitObjects, GameplayAnalyzer.Analyze(map));

    [Fact]
    public void RegularLeftRightAlternation_IsRecognized()
    {
        var result = Analyze(Map((0, 0), (100, 100), (200, 0), (300, 100), (400, 0)));
        var match = Assert.Single(result.Matches);
        Assert.Equal(JumpPatternType.OneTwo, match.Type);
        Assert.Equal((0, 4), (match.StartObjectIndex, match.EndObjectIndex));
        Assert.Equal(3, result.OneTwoPointCount);
        Assert.Equal(5, result.OneTwoObjectCount);
        Assert.Equal(1, result.AlternationConsistency);
    }

    [Fact]
    public void RegularRightLeftAlternation_IsRecognized() =>
        Assert.Single(Analyze(Map((0, 0), (100, -100), (200, 0), (300, -100), (400, 0))).Matches);

    [Fact]
    public void StraightLine_SharpIsolatedReversalIsolatedAndTooShort_AreRejected()
    {
        Assert.Empty(Analyze(Map((0, 0), (100, 0), (200, 0), (300, 0))).Matches);
        Assert.Empty(Analyze(Map((0, 0), (100, 0), (30, 70), (-40, 140))).Matches);
        Assert.Empty(Analyze(Map((0, 0), (100, 0), (0, 0), (-100, 0))).Matches);
        Assert.Empty(Analyze(Map((0, 0), (100, 100), (200, 0))).Matches);
    }

    [Fact]
    public void RepeatedReversals_AreStableAlternatingChains()
    {
        var result = Analyze(Map((0, 0), (100, 0), (1, 1), (101, 1), (2, 2)));
        Assert.Single(result.Matches);
        Assert.All(result.Points, point => Assert.Equal(OneTwoTurnDirection.Reversal, point.TurnDirection));
    }

    [Fact]
    public void SameDirectionTurn_BreaksTheAlternation()
    {
        var result = Analyze(Map((0, 0), (100, 100), (200, 0), (300, -100)));
        Assert.Empty(result.Matches);
    }

    [Fact]
    public void ReasonableDistanceAndTimingVariation_AreAccepted()
    {
        var map = Map((0, 0), (100, 100), (260, 0), (380, 100), (520, 0));
        int[] times = [0, 180, 400, 590, 800];
        for (int i = 0; i < times.Length; i++) map.HitObjects[i].Time = times[i];
        var result = Analyze(map);
        Assert.Single(result.Matches);
        Assert.True(result.DistanceConsistency >= .55);
        Assert.True(result.IntervalConsistency >= .55);
    }

    [Fact]
    public void ExtremelyInconsistentDistanceOrTiming_IsRejected()
    {
        Assert.Empty(Analyze(Map((0, 0), (60, 60), (500, 0), (560, 60), (1000, 0))).Matches);

        var timing = Map((0, 0), (100, 100), (200, 0), (300, 100), (400, 0));
        int[] times = [0, 50, 350, 400, 700];
        for (int i = 0; i < times.Length; i++) timing.HitObjects[i].Time = times[i];
        Assert.Empty(Analyze(timing).Matches);
    }

    [Fact]
    public void StreamMaskAndCoverageDeduplication_AreRespected()
    {
        var map = Map((0, 0), (100, 100), (200, 0), (300, 100), (400, 0));
        var result = Analyze(map);
        Assert.Equal(1, result.Coverage, 12);
        Assert.Empty(JumpPatternAnalyzer.AnalyzeOneTwo(map.HitObjects,
            new GameplayProfile { StreamSequences = [new(0, 4)] }).Matches);
    }

    [Fact]
    public void OneTwoCoexistsWithSharpEdgeAndTriangle()
    {
        var sharpEdge = Map((20, 20), (492, 20), (20, 20), (492, 20), (20, 20));
        var sharpEdgeMatches = JumpPatternAnalyzer.Analyze(sharpEdge.HitObjects, GameplayAnalyzer.Analyze(sharpEdge));
        Assert.Contains(sharpEdgeMatches, match => match.Type == JumpPatternType.OneTwo);
        Assert.Contains(sharpEdgeMatches, match => match.Type == JumpPatternType.Sharp);
        Assert.Contains(sharpEdgeMatches, match => match.Type == JumpPatternType.Edge);

        var triangle = Map((0, 0), (100, 100), (200, 0), (300, 100), (400, 0));
        var triangleMatches = JumpPatternAnalyzer.Analyze(triangle.HitObjects, GameplayAnalyzer.Analyze(triangle));
        Assert.Contains(triangleMatches, match => match.Type == JumpPatternType.OneTwo);
        Assert.Contains(triangleMatches, match => match.Type == JumpPatternType.Triangle);
    }

    [Fact]
    public void ConfidenceIsFiniteAndDebugDefaultsAreOff()
    {
        var result = Analyze(Map((0, 0), (100, 100), (200, 0), (300, 100), (400, 0)));
        Assert.InRange(result.Confidence, 0, 1);
        Assert.False(BeatInsight.Diagnostics.GameplayDebug.OneTwoJumpEnabled);
        Assert.False(BeatInsight.Diagnostics.GameplayDebug.OneTwoJumpDetailedEnabled);
        BeatInsight.Diagnostics.GameplayDebug.OneTwoJump(result);
    }

    [Fact]
    public void SotarksBlackRoverExtreme_IsAHighCoveragePositiveControl()
    {
        var map = FixtureLoader.Load("Vickeblanka - Black Rover (TV Size) (Sotarks) [Extreme].osu");
        var result = JumpPatternAnalyzer.AnalyzeOneTwo(map.HitObjects, map.GameplayProfile);

        Assert.True(result.Coverage >= .05);
        Assert.True(result.SequenceCount >= 3);
        Assert.True(result.LongestSequence >= 5);
    }

    [Fact]
    public void CanYouUnderstandMe_RemainsALowCoverageNegativeControl()
    {
        var map = FixtureLoader.Load("(can you) understand me [uhh].osu");
        var result = JumpPatternAnalyzer.AnalyzeOneTwo(map.HitObjects, map.GameplayProfile);

        Assert.InRange(result.Coverage, 0, .02);
        Assert.DoesNotContain(JumpPatternAnalyzer.Analyze(map.HitObjects, map.GameplayProfile),
            match => match.Type == JumpPatternType.OneTwo && match.ObjectCount > 5);
    }
}
