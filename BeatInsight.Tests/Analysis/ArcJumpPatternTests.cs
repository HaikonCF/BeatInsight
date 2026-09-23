using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Models;
using BeatInsight.Models.JumpPatterns;

namespace BeatInsight.Tests.Analysis;

public sealed class ArcJumpPatternTests
{
    private static ArcJumpAnalysis Analyze(Beatmap map) =>
        JumpPatternAnalyzer.AnalyzeArc(map.HitObjects, GameplayAnalyzer.Analyze(map));

    [Fact]
    public void StraightLineAndOneOrTwoTurns_AreNotCompleteArcs()
    {
        Assert.Empty(Analyze(Map((0, 0), (100, 0), (200, 0), (300, 0), (400, 0))).Matches);
        Assert.Empty(Analyze(Arc(45)).Matches);
        Assert.Empty(Analyze(Arc(45, 45)).Matches);
    }

    [Fact]
    public void ThreeConsecutiveLeftTurns_AreRecognized()
    {
        var result = Analyze(Arc(45, 45, 45));

        var match = Assert.Single(result.Matches);
        Assert.Equal(JumpPatternType.Arc, match.Type);
        Assert.Equal((0, 4), (match.StartObjectIndex, match.EndObjectIndex));
        Assert.Equal(3, result.ArcPointCount);
        Assert.Equal(5, result.ArcObjectCount);
        Assert.Equal(1, result.LeftSequenceCount);
        Assert.Equal(0, result.RightSequenceCount);
    }

    [Fact]
    public void ThreeConsecutiveRightTurns_AreRecognized()
    {
        var result = Analyze(Arc(-45, -45, -45));

        Assert.Single(result.Matches);
        Assert.Equal(0, result.LeftSequenceCount);
        Assert.Equal(1, result.RightSequenceCount);
    }

    [Fact]
    public void DirectionInversion_BreaksTheArcRun()
    {
        Assert.Empty(Analyze(Arc(45, -45, 45, -45)).Matches);
        Assert.Empty(Analyze(Arc(-45, 45, -45, 45)).Matches);
    }

    [Theory]
    [InlineData(19, false)]
    [InlineData(20, true)]
    [InlineData(21, true)]
    [InlineData(109, true)]
    [InlineData(110, true)]
    [InlineData(111, false)]
    public void AngleBandBoundaries_AreInclusiveWithinArcBand(int degrees, bool expected)
    {
        Beatmap map = ArcAtBandBoundary(degrees);
        ArcJumpAnalysis result = Analyze(map);
        Assert.True(expected == (result.SequenceCount != 0),
            $"requested={degrees}; measured={MeasuredTurns(map)}; consistency={result.AngleConsistency:F6}");
    }

    [Fact]
    public void ReversalIsNotAnArc() => Assert.Empty(Analyze(Arc(180, 180, 180)).Matches);

    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(61, true)]
    public void DistanceBoundary_IsInclusiveAt60(int length, bool expected)
    {
        ArcJumpAnalysis result = Analyze(Map((0, 0), (length, 0), (length, length), (0, length), (0, 0)));
        Assert.Equal(expected, result.SequenceCount != 0);
    }

    [Fact]
    public void StreamMaskAndCoverageDeduplication_AreRespected()
    {
        Beatmap map = Arc(45, 45, 45);
        ArcJumpAnalysis result = Analyze(map);
        Assert.Equal(1, result.Coverage, 12);
        Assert.Empty(JumpPatternAnalyzer.AnalyzeArc(map.HitObjects,
            new GameplayProfile { StreamSequences = [new(0, 4)] }).Matches);
    }

    [Fact]
    public void AngleConsistencyRejectsIrregularCurvatureAndAcceptsModerateVariation()
    {
        Assert.Empty(Analyze(Arc(30, 30, 61)).Matches); // 30 / 61 ~= .49
        var result = Analyze(Arc(30, 30, 59.99)); // integer-safe representation of .50
        Assert.Single(result.Matches);
        Assert.True(result.AngleConsistency >= .50);
        Assert.Single(Analyze(Arc(30, 30, 59)).Matches); // 30 / 59 ~= .51
    }

    [Fact]
    public void ArcCoexistsWithWideSpacedEdgeAndTriangleWhenGeometryApplies()
    {
        var wideTriangle = Arc(90, 90, 90);
        var matches = JumpPatternAnalyzer.Analyze(wideTriangle.HitObjects, GameplayAnalyzer.Analyze(wideTriangle));
        Assert.Contains(matches, match => match.Type == JumpPatternType.Arc);
        Assert.Contains(matches, match => match.Type == JumpPatternType.WideAngle);
        Assert.Contains(matches, match => match.Type == JumpPatternType.Triangle);

        var spaced = Arc(45, 45, 45, 200);
        matches = JumpPatternAnalyzer.Analyze(spaced.HitObjects, GameplayAnalyzer.Analyze(spaced));
        Assert.Contains(matches, match => match.Type == JumpPatternType.Arc);
        Assert.Contains(matches, match => match.Type == JumpPatternType.Spaced);

        var edge = Map((20, 20), (492, 20), (492, 364), (20, 364), (20, 20));
        matches = JumpPatternAnalyzer.Analyze(edge.HitObjects, GameplayAnalyzer.Analyze(edge));
        Assert.Contains(matches, match => match.Type == JumpPatternType.Arc);
        Assert.Contains(matches, match => match.Type == JumpPatternType.Edge);
    }

    [Fact]
    public void SameDirectionArc_IsNeitherOneTwoNorLinear()
    {
        Beatmap map = Arc(45, 45, 45);
        var matches = JumpPatternAnalyzer.Analyze(map.HitObjects, GameplayAnalyzer.Analyze(map));

        Assert.Contains(matches, match => match.Type == JumpPatternType.Arc);
        Assert.DoesNotContain(matches, match => match.Type == JumpPatternType.OneTwo);
        Assert.DoesNotContain(matches, match => match.Type == JumpPatternType.Linear);
    }

    [Fact]
    public void ConfidenceIsBoundedAndDebugDefaultsAreOff()
    {
        var result = Analyze(Arc(45, 45, 45));
        Assert.InRange(result.Confidence, 0, 1);
        Assert.True(result.AverageCumulativeTurn > 0);
        Assert.False(BeatInsight.Diagnostics.GameplayDebug.ArcJumpEnabled);
        Assert.False(BeatInsight.Diagnostics.GameplayDebug.ArcJumpDetailedEnabled);
        BeatInsight.Diagnostics.GameplayDebug.ArcJump(result);
    }

    private static Beatmap Arc(double firstTurn, double secondTurn, double thirdTurn, int length = 100_000) =>
        Arc([firstTurn, secondTurn, thirdTurn], length);

    private static Beatmap Arc(params double[] turns) => Arc(turns, 100_000);

    private static Beatmap ArcAtBandBoundary(int degrees) => degrees switch
    {
        // Integer osu! coordinates cannot represent every trigonometric boundary
        // exactly. These constructions lie just inside the inclusive band.
        20 => Arc([20.01, 20.02, 20.03], 100_000),
        110 => Arc([109.99, 109.98, 109.97], 100_000),
        _ => Arc(degrees, degrees, degrees)
    };

    private static Beatmap Arc(IReadOnlyList<double> turns, int length)
    {
        var points = new List<(int X, int Y)> { (0, 0) };
        double heading = 0;
        double x = 0, y = 0;
        for (int index = 0; index <= turns.Count; index++)
        {
            double radians = heading * Math.PI / 180;
            x += length * Math.Cos(radians);
            y += length * Math.Sin(radians);
            points.Add(((int)Math.Round(x), (int)Math.Round(y)));
            if (index < turns.Count) heading += turns[index];
        }
        return Map(points.ToArray());
    }

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

    private static string MeasuredTurns(Beatmap map) => string.Join(", ", Enumerable.Range(1, map.HitObjects.Count - 2)
        .Select(index => GameplayAnalyzer.GetTurnAngle(map.HitObjects[index - 1], map.HitObjects[index],
            map.HitObjects[index + 1]).ToString("F6", System.Globalization.CultureInfo.InvariantCulture)));
}
