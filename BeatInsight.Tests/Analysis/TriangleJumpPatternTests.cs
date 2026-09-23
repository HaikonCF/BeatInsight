using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Models;
using BeatInsight.Models.JumpPatterns;

namespace BeatInsight.Tests.Analysis;

public sealed class TriangleJumpPatternTests
{
    private static Beatmap Map(params (int X, int Y)[] points) => new()
    {
        CS = 4, SliderMultiplier = 1.4,
        TimingPoints = [new() { Time = 0, BeatLength = 500, Uninherited = true }],
        HitObjects = points.Select((point, index) => new HitObject
            { X = point.X, Y = point.Y, Time = index * 200, Type = 1 }).ToList()
    };

    private static TriangleJumpAnalysis Analyze(Beatmap map) =>
        JumpPatternAnalyzer.AnalyzeTriangle(map.HitObjects, GameplayAnalyzer.Analyze(map));

    [Fact]
    public void CollinearPoints_AreRejected() =>
        Assert.Empty(Analyze(Map((0, 0), (100, 0), (200, 0), (300, 0))).Matches);

    [Fact]
    public void TwoAdjacentBalancedTriangles_AreRecognized()
    {
        var result = Analyze(Map((0, 0), (100, 100), (200, 0), (300, 100)));
        var match = Assert.Single(result.Matches);

        Assert.Equal(JumpPatternType.Triangle, match.Type);
        Assert.Equal((0, 3), (match.StartObjectIndex, match.EndObjectIndex));
        Assert.Equal(2, result.TrianglePointCount);
        Assert.Equal(4, result.TriangleObjectCount);
        Assert.InRange(result.AverageHeight, 99, 101);
        Assert.InRange(result.AverageTriangleAngle, 89, 91);
    }

    [Fact]
    public void TooSmallAndNearlyCollinearTriangles_AreRejected()
    {
        Assert.Empty(Analyze(Map((0, 0), (60, 10), (120, 0), (180, 10))).Matches);
        Assert.Empty(Analyze(Map((0, 0), (100, 29), (200, 0), (300, 29))).Matches);
    }

    [Fact]
    public void ExactlyMinimumHeight_IsAcceptedWhenOtherGeometryIsValid()
    {
        var result = Analyze(Map((0, 0), (100, 30), (200, 0), (300, 30)));
        Assert.Single(result.Matches);
        Assert.All(result.Points, point => Assert.True(point.Height >= 30));
    }

    [Fact]
    public void IsolatedOccurrence_DoesNotFormPattern() =>
        Assert.Empty(Analyze(Map((0, 0), (100, 100), (200, 0), (300, -100))).Matches);

    [Fact]
    public void CoverageUsesUniqueObjects()
    {
        var result = Analyze(Map((0, 0), (100, 100), (200, 0), (300, 100), (400, 0)));
        Assert.Equal(5, result.TriangleObjectCount);
        Assert.Equal(1, result.Coverage, 12);
    }

    [Fact]
    public void StreamMaskIsRespected()
    {
        var map = Map((0, 0), (100, 100), (200, 0), (300, 100));
        var result = JumpPatternAnalyzer.AnalyzeTriangle(map.HitObjects,
            new GameplayProfile { StreamSequences = [new(0, 3)] });
        Assert.Empty(result.Matches);
    }

    [Fact]
    public void TriangleCoexistsWithSharpAndEdge()
    {
        var map = Map((20, 20), (492, 20), (20, 364), (492, 364));
        var matches = JumpPatternAnalyzer.Analyze(map.HitObjects, GameplayAnalyzer.Analyze(map));

        Assert.Contains(matches, match => match.Type == JumpPatternType.Triangle);
        Assert.Contains(matches, match => match.Type == JumpPatternType.Sharp);
        Assert.Contains(matches, match => match.Type == JumpPatternType.Edge);
    }

    [Fact]
    public void NonTriangleTripletInterruptsTheSequence()
    {
        var result = Analyze(Map((0, 0), (100, 100), (200, 0), (300, 100), (400, 200), (500, 300)));
        Assert.Single(result.Matches);
        Assert.Equal((0, 3), (result.Matches[0].StartObjectIndex, result.Matches[0].EndObjectIndex));
    }

    [Fact]
    public void ConfidenceAndDebugAreFiniteAndDisabledByDefault()
    {
        var result = Analyze(Map((0, 0), (100, 100), (200, 0), (300, 100)));
        Assert.InRange(result.Confidence, 0, 1);
        Assert.False(BeatInsight.Diagnostics.GameplayDebug.TriangleJumpEnabled);
        Assert.False(BeatInsight.Diagnostics.GameplayDebug.TriangleJumpDetailedEnabled);
        BeatInsight.Diagnostics.GameplayDebug.TriangleJump(result);
    }

    [Theory]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(31, true)]
    [InlineData(154, true)]
    // Integer osu! coordinates cannot encode 155° exactly; 154.999° is the
    // representable just-inside boundary used to verify inclusive acceptance.
    [InlineData(154.999, true)]
    [InlineData(156, false)]
    public void AngleBoundaries_AreInclusiveOnlyAtConfiguredLimits(double angle, bool expected)
    {
        var result = Analyze(AngleChain(angle));
        Assert.Equal(expected, result.Matches.Count > 0);
    }

    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(61, true)]
    public void MinimumLinkDistanceBoundary_IsRespected(int distance, bool expected)
    {
        var result = Analyze(Map((0, 0), (distance, 0), (distance, distance), (distance * 2, distance)));
        Assert.Equal(expected, result.Matches.Count > 0);
    }

    [Theory]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(31, true)]
    public void HeightBoundary_IsRespected(int height, bool expected)
    {
        var result = Analyze(Map((0, 0), (100, height), (200, 0), (300, height)));
        Assert.Equal(expected, result.Matches.Count > 0);
    }

    [Theory]
    [InlineData(9999, false)]
    [InlineData(10000, true)]
    [InlineData(10001, true)]
    public void SideBalanceBoundary_IsRespected(int shortSide, bool expected)
    {
        var result = Analyze(ImbalancedChain(shortSide, 40_000, 155));
        Assert.Equal(expected, result.Matches.Count > 0);
    }

    [Fact]
    public void VisuallyObviousIsolatedTriangle_IsRejectedOnlyByOccurrenceRule()
    {
        var map = Map((0, 0), (100, 100), (200, 0), (300, -100));
        var result = Analyze(map);

        // The first triplet is a balanced, tall right triangle; the following straight
        // continuation leaves it as a single occurrence, deliberately below the sequence minimum.
        Assert.Equal(90, GameplayAnalyzer.GetTurnAngle(map.HitObjects[0], map.HitObjects[1], map.HitObjects[2]), 8);
        Assert.Empty(result.Matches);
    }

    private static Beatmap AngleChain(double degrees)
    {
        const int length = 1_000_000;
        double radians = degrees * Math.PI / 180;
        int x = (int)Math.Round(length + length * Math.Cos(radians));
        int y = (int)Math.Round(length * Math.Sin(radians));
        return Map((0, 0), (length, 0), (x, y), (x + length, y));
    }

    private static Beatmap ImbalancedChain(int shortSide, int longSide, double angle)
    {
        double radians = angle * Math.PI / 180;
        int x = shortSide + (int)Math.Round(longSide * Math.Cos(radians));
        int y = (int)Math.Round(longSide * Math.Sin(radians));
        return Map((0, 0), (shortSide, 0), (x, y), (x + shortSide, y));
    }
}
