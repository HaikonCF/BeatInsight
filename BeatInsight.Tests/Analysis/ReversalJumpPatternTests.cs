using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Models;
using BeatInsight.Models.JumpPatterns;

namespace BeatInsight.Tests.Analysis;

public sealed class ReversalJumpPatternTests
{
    private static Beatmap Map(params (int X, int Y)[] points) => new()
    {
        CS = 4,
        SliderMultiplier = 1.4,
        TimingPoints = [new() { Time = 0, BeatLength = 500, Uninherited = true }],
        HitObjects = points.Select((point, index) => new HitObject
        {
            X = point.X,
            Y = point.Y,
            Time = index * 200,
            Type = 1
        }).ToList()
    };

    private static ReversalJumpAnalysis Analyze(Beatmap map) =>
        JumpPatternAnalyzer.AnalyzeReversal(map.HitObjects, GameplayAnalyzer.Analyze(map));

    [Fact]
    public void StraightWideAndSharpTurnsBelow170_AreNotReversals()
    {
        Assert.Empty(Analyze(Map((0, 0), (100, 0), (200, 0), (300, 0))).Matches);
        Assert.Empty(Analyze(TwoTurns(90)).Matches);
        Assert.Empty(Analyze(TwoTurns(150)).Matches);
        Assert.Empty(Analyze(TwoTurns(169)).Matches);
    }

    [Theory]
    [InlineData(170)]
    [InlineData(171)]
    [InlineData(179)]
    [InlineData(180)]
    public void ReversalBand_IsInclusiveFrom170Degrees(int degrees)
    {
        var result = Analyze(TwoTurns(degrees));

        Assert.Single(result.Matches);
        Assert.All(result.Points, point => Assert.True(point.Angle >= 170));
    }

    [Fact]
    public void AnIsolatedReversalPivot_IsNotACompletePattern() =>
        Assert.Empty(Analyze(Map((0, 0), (100, 0), (0, 0))).Matches);

    [Fact]
    public void TwoAdjacentReversalPivots_FormAPatternWithUniqueCoverage()
    {
        var result = Analyze(Map((0, 0), (100, 0), (0, 0), (100, 0)));

        var match = Assert.Single(result.Matches);
        Assert.Equal(JumpPatternType.Reversal, match.Type);
        Assert.Equal((0, 3), (match.StartObjectIndex, match.EndObjectIndex));
        Assert.Equal(2, result.ReversalPointCount);
        Assert.Equal(4, result.ReversalObjectCount);
        Assert.Equal(1, result.Coverage, 12);
        Assert.Equal(0, result.AverageReturnDistance, 12);
    }

    [Fact]
    public void NonReversalPivot_InterruptsTheRun()
    {
        var result = Analyze(Map((0, 0), (100, 0), (0, 0), (100, 0), (100, 100), (0, 100)));

        Assert.Single(result.Matches);
        Assert.Equal((0, 3), (result.Matches[0].StartObjectIndex, result.Matches[0].EndObjectIndex));
    }

    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(61, true)]
    public void DistanceBoundary_IsInclusiveAt60Pixels(int distance, bool expected)
    {
        var result = Analyze(Map((0, 0), (distance, 0), (0, 0), (distance, 0)));
        Assert.Equal(expected, result.SequenceCount != 0);
    }

    [Fact]
    public void ExistingStreamMask_IsRespected()
    {
        var map = Map((0, 0), (100, 0), (0, 0), (100, 0));
        var result = JumpPatternAnalyzer.AnalyzeReversal(map.HitObjects,
            new GameplayProfile { StreamSequences = [new(0, 3)] });

        Assert.Empty(result.Matches);
    }

    [Fact]
    public void ReversalCoexistsWithSharpAndCanCoexistWithOneTwo()
    {
        var map = Map((0, 0), (100, 0), (0, 0), (100, 0), (0, 0));
        var matches = JumpPatternAnalyzer.Analyze(map.HitObjects, GameplayAnalyzer.Analyze(map));

        Assert.Contains(matches, match => match.Type == JumpPatternType.Reversal);
        Assert.Contains(matches, match => match.Type == JumpPatternType.Sharp);
        Assert.Contains(matches, match => match.Type == JumpPatternType.OneTwo);
    }

    [Fact]
    public void ReversalDoesNotCreateLinearMatch()
    {
        var map = Map((0, 0), (100, 0), (0, 0), (100, 0));
        var matches = JumpPatternAnalyzer.Analyze(map.HitObjects, GameplayAnalyzer.Analyze(map));

        Assert.Contains(matches, match => match.Type == JumpPatternType.Reversal);
        Assert.DoesNotContain(matches, match => match.Type == JumpPatternType.Linear);
    }

    [Fact]
    public void ConfidenceIsBoundedAndDebugDefaultsAreOff()
    {
        var result = Analyze(Map((0, 0), (100, 0), (0, 0), (100, 0)));

        Assert.InRange(result.Confidence, 0, 1);
        Assert.True(result.DistanceConsistency > 0);
        Assert.False(BeatInsight.Diagnostics.GameplayDebug.ReversalJumpEnabled);
        Assert.False(BeatInsight.Diagnostics.GameplayDebug.ReversalJumpDetailedEnabled);
        BeatInsight.Diagnostics.GameplayDebug.ReversalJump(result);
    }

    private static Beatmap TwoTurns(int degrees)
    {
        const int length = 100_000;
        // The 170° construction rounds marginally inside the inclusive policy
        // boundary for both pivots under osu!'s integer coordinate convention.
        if (degrees == 170)
            return Map((0, 0), (length, 0), (1_519, 17_364), (95_488, -16_836));

        double radians = degrees * Math.PI / 180;
        var first = (0, 0);
        var second = (length, 0);
        var third = (second.Item1 + (int)Math.Round(length * Math.Cos(radians)),
            (int)Math.Round(length * Math.Sin(radians)));
        var fourth = (third.Item1 + (int)Math.Round(length * Math.Cos(2 * radians)),
            third.Item2 + (int)Math.Round(length * Math.Sin(2 * radians)));
        return Map(first, second, third, fourth);
    }
}
