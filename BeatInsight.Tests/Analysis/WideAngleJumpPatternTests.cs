using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Models;
using BeatInsight.Models.JumpPatterns;

namespace BeatInsight.Tests.Analysis;

public sealed class WideAngleJumpPatternTests
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

    private static WideAngleJumpAnalysis Analyze(Beatmap map) =>
        JumpPatternAnalyzer.AnalyzeWideAngle(map.HitObjects, GameplayAnalyzer.Analyze(map));

    [Fact]
    public void StraightLine_IsNotWide() =>
        Assert.Empty(Analyze(Map((0, 0), (100, 0), (200, 0), (300, 0))).Matches);

    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(61, true)]
    [InlineData(90, true)]
    [InlineData(114, true)]
    [InlineData(115, true)]
    [InlineData(116, false)]
    public void AngleBandBoundaries_AreInclusiveOnlyInsideTheWideBand(int degrees, bool expected)
    {
        var map = TwoTurns(degrees);
        double firstAngle = GameplayAnalyzer.GetTurnAngle(map.HitObjects[0], map.HitObjects[1], map.HitObjects[2]);
        double secondAngle = GameplayAnalyzer.GetTurnAngle(map.HitObjects[1], map.HitObjects[2], map.HitObjects[3]);
        var result = Analyze(map);

        Assert.True(expected == (result.Matches.Count != 0),
            $"expected={expected}; angles={firstAngle:F10}/{secondAngle:F10}");
        if (expected)
            Assert.All(result.Points, point => Assert.InRange(point.Angle, 60, 115));
    }

    [Fact]
    public void SharpTurns_AreNotWide()
    {
        // 121° is deliberately inside Sharp's inclusive >= 120° band. Integer
        // coordinates cannot represent the trigonometric 120° boundary exactly.
        var map = TwoTurns(121);
        var profile = GameplayAnalyzer.Analyze(map);

        Assert.Empty(JumpPatternAnalyzer.AnalyzeWideAngle(map.HitObjects, profile).Matches);
        Assert.NotEmpty(JumpPatternAnalyzer.AnalyzeSharp(map.HitObjects, profile).Matches);
    }

    [Fact]
    public void Reversal_IsNotWide() =>
        Assert.Empty(Analyze(TwoTurns(180)).Matches);

    [Fact]
    public void OneWidePoint_IsNotACompletePattern() =>
        Assert.Empty(Analyze(Map((0, 0), (100, 0), (100, 100))).Matches);

    [Fact]
    public void TwoAdjacentWidePoints_AreRecognized()
    {
        var result = Analyze(Map((0, 0), (100, 0), (100, 100), (200, 100)));

        var match = Assert.Single(result.Matches);
        Assert.Equal(JumpPatternType.WideAngle, match.Type);
        Assert.Equal((0, 3), (match.StartObjectIndex, match.EndObjectIndex));
        Assert.Equal(2, result.WidePointCount);
        Assert.Equal(4, result.WideObjectCount);
        Assert.InRange(result.AverageWideAngle, 89.999, 90.001);
    }

    [Fact]
    public void NonWideAngle_InterruptsTheRun()
    {
        var result = Analyze(Map((0, 0), (100, 0), (100, 100), (200, 100), (300, 100)));

        Assert.Single(result.Matches);
        Assert.Equal((0, 3), (result.Matches[0].StartObjectIndex, result.Matches[0].EndObjectIndex));
    }

    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(61, true)]
    public void DistanceBoundary_IsInclusiveAt60Pixels(int distance, bool expected)
    {
        var result = Analyze(Map((0, 0), (distance, 0), (distance, distance), (distance * 2, distance)));

        Assert.Equal(expected, result.Matches.Count != 0);
    }

    [Fact]
    public void ExistingStreamMask_IsRespected()
    {
        var map = Map((0, 0), (100, 0), (100, 100), (200, 100));
        var result = JumpPatternAnalyzer.AnalyzeWideAngle(map.HitObjects,
            new GameplayProfile { StreamSequences = [new(0, 3)] });

        Assert.Empty(result.Matches);
    }

    [Fact]
    public void CoverageUsesUniqueObjects()
    {
        var result = Analyze(Map((0, 0), (100, 0), (100, 100), (0, 100), (0, 200)));

        Assert.Single(result.Matches);
        Assert.Equal(3, result.WidePointCount);
        Assert.Equal(5, result.WideObjectCount);
        Assert.Equal(1, result.Coverage, 12);
    }

    [Fact]
    public void WideAndSpaced_Coexist()
    {
        var map = Map((0, 0), (200, 0), (200, 200), (400, 200));
        var matches = JumpPatternAnalyzer.Analyze(map.HitObjects, GameplayAnalyzer.Analyze(map));

        Assert.Contains(matches, match => match.Type == JumpPatternType.WideAngle);
        Assert.Contains(matches, match => match.Type == JumpPatternType.Spaced);
    }

    [Fact]
    public void WideAndEdge_Coexist()
    {
        var map = Map((20, 20), (492, 20), (492, 364), (20, 364));
        var matches = JumpPatternAnalyzer.Analyze(map.HitObjects, GameplayAnalyzer.Analyze(map));

        Assert.Contains(matches, match => match.Type == JumpPatternType.WideAngle);
        Assert.Contains(matches, match => match.Type == JumpPatternType.Edge);
    }

    [Fact]
    public void WideAndTriangle_Coexist()
    {
        var map = Map((0, 0), (150, 0), (150, 150), (0, 150));
        var matches = JumpPatternAnalyzer.Analyze(map.HitObjects, GameplayAnalyzer.Analyze(map));

        Assert.Contains(matches, match => match.Type == JumpPatternType.WideAngle);
        Assert.Contains(matches, match => match.Type == JumpPatternType.Triangle);
    }

    [Fact]
    public void WideAndOneTwo_Coexist()
    {
        var map = Map((0, 0), (100, 0), (100, 100), (200, 100), (200, 200));
        var matches = JumpPatternAnalyzer.Analyze(map.HitObjects, GameplayAnalyzer.Analyze(map));

        Assert.Contains(matches, match => match.Type == JumpPatternType.WideAngle);
        Assert.Contains(matches, match => match.Type == JumpPatternType.OneTwo);
    }

    [Fact]
    public void WideAndSharp_DoNotSharePivotsBecauseTheirBandsAreDisjoint()
    {
        var map = TwoTurns(90);
        var matches = JumpPatternAnalyzer.Analyze(map.HitObjects, GameplayAnalyzer.Analyze(map));

        Assert.Contains(matches, match => match.Type == JumpPatternType.WideAngle);
        Assert.DoesNotContain(matches, match => match.Type == JumpPatternType.Sharp);
    }

    [Fact]
    public void ConfidenceIsFiniteAndDebugIsDisabledByDefault()
    {
        var result = Analyze(Map((0, 0), (100, 0), (100, 100), (200, 100)));

        Assert.InRange(result.Confidence, 0, 1);
        Assert.False(BeatInsight.Diagnostics.GameplayDebug.WideAngleJumpEnabled);
        Assert.False(BeatInsight.Diagnostics.GameplayDebug.WideAngleJumpDetailedEnabled);
        BeatInsight.Diagnostics.GameplayDebug.WideAngleJump(result);
    }

    private static Beatmap TwoTurns(double degrees)
    {
        // Keep synthetic vectors below integer multiplication overflow in the
        // existing GetTurnAngle helper while retaining sub-degree precision.
        const int length = 10_000;
        // Integer osu! coordinates cannot encode every exact trigonometric boundary.
        // These two vectors deliberately round outward, so GetTurnAngle measures
        // the inclusive policy boundary rather than a value infinitesimally below it.
        if (degrees == 60)
            return Map((0, 0), (length, 0), (15_000, 8_662), (10_000, 17_320));
        if (degrees == 120)
            return Map((0, 0), (length, 0), (5_000, 8_661), (0, 0));

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
