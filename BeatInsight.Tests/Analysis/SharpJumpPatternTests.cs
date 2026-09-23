using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Models;
using BeatInsight.Models.JumpPatterns;

namespace BeatInsight.Tests.Analysis;

public sealed class SharpJumpPatternTests
{
    private static Beatmap Map(params (int X, int Y)[] points) => new()
    {
        CS = 4, SliderMultiplier = 1.4,
        TimingPoints = [new() { Time = 0, BeatLength = 500, Uninherited = true }],
        HitObjects = points.Select((p, i) => new HitObject
            { X = p.X, Y = p.Y, Time = i * 200, Type = 1 }).ToList()
    };

    private static SharpJumpAnalysis Analyze(Beatmap map) =>
        JumpPatternAnalyzer.AnalyzeSharp(map.HitObjects, GameplayAnalyzer.Analyze(map));

    [Fact]
    public void StraightLine_IsNotSharp() =>
        Assert.Empty(Analyze(Map((0, 0), (100, 0), (200, 0), (300, 0))).Matches);

    [Fact]
    public void TwoConsecutiveMarkedTurns_AreRecognized()
    {
        var result = Analyze(Map((0, 0), (100, 0), (30, 70), (130, 70)));
        var match = Assert.Single(result.Matches);
        Assert.Equal(JumpPatternType.Sharp, match.Type);
        Assert.Equal((0, 3), (match.StartObjectIndex, match.EndObjectIndex));
        Assert.Equal(2, result.SharpPointCount);
        Assert.Equal(4, result.SharpObjectCount);
        Assert.InRange(result.AverageSharpAngle, 120, 180);
    }

    [Fact]
    public void SingleSharpPivot_IsNotACompleteSharpSequence() =>
        Assert.Empty(Analyze(Map((0, 0), (100, 0), (30, 70), (-40, 140))).Matches);

    [Fact]
    public void RepeatedReversals_AreSharpAndAreDiagnosedSeparately()
    {
        var result = Analyze(Map((0, 0), (100, 0), (0, 0), (100, 0)));
        Assert.Single(result.Matches);
        Assert.Equal(2, result.ReversalPointCount);
        Assert.Equal(180, result.StrongestSharpAngle, 8);
    }

    [Fact]
    public void TrivialDistances_AreRejected() =>
        Assert.Empty(Analyze(Map((0, 0), (59, 0), (18, 42), (77, 42))).Matches);

    [Fact]
    public void ExistingStreamMask_IsRespected()
    {
        var map = Map((0, 0), (100, 0), (30, 70), (130, 70));
        var result = JumpPatternAnalyzer.AnalyzeSharp(map.HitObjects,
            new GameplayProfile { StreamSequences = [new(0, 3)] });
        Assert.Empty(result.Matches);
    }

    [Fact]
    public void CoverageUsesUniqueObjectsAcrossSeparateSequences()
    {
        var map = Map((0, 0), (100, 0), (30, 70), (130, 70),
            (230, 70), (160, 140), (260, 140));
        map.HitObjects[4].Time = 2_000; // breaks all triples around the boundary.
        map.HitObjects[5].Time = 2_200;
        map.HitObjects[6].Time = 2_400;
        // Add a second complete four-object Sharp run after the timing break.
        map.HitObjects.AddRange(new[]
        {
            new HitObject { X = 360, Y = 140, Time = 2_600, Type = 1 },
            new HitObject { X = 290, Y = 210, Time = 2_800, Type = 1 },
            new HitObject { X = 390, Y = 210, Time = 3_000, Type = 1 }
        });
        var result = Analyze(map);
        Assert.True(result.SharpObjectCount <= map.HitObjects.Count);
        Assert.Equal(result.Matches.SelectMany(match => Enumerable.Range(match.StartObjectIndex, match.ObjectCount)).Distinct().Count(),
            result.SharpObjectCount);
        Assert.Equal(result.SharpObjectCount / (double)map.HitObjects.Count, result.Coverage, 12);
    }

    [Fact]
    public void SharpAndEdge_Coexist()
    {
        var map = Map((20, 20), (220, 20), (70, 64), (270, 64));
        var profile = GameplayAnalyzer.Analyze(map);
        var matches = JumpPatternAnalyzer.Analyze(map.HitObjects, profile);
        Assert.Contains(matches, match => match.Type == JumpPatternType.Edge);
        Assert.Contains(matches, match => match.Type == JumpPatternType.Sharp);
    }

    [Fact]
    public void SharpAndLinear_CoexistAsIndependentMatches()
    {
        var map = Map(
            (0, 0), (100, 0), (200, 0), (300, 0),
            (0, 0), (100, 0), (30, 70), (130, 70));
        map.HitObjects[4].Time = 2_000;
        map.HitObjects[5].Time = 2_200;
        map.HitObjects[6].Time = 2_400;
        map.HitObjects[7].Time = 2_600;

        var matches = JumpPatternAnalyzer.Analyze(map.HitObjects, GameplayAnalyzer.Analyze(map));

        Assert.Contains(matches, match => match.Type == JumpPatternType.Linear);
        Assert.Contains(matches, match => match.Type == JumpPatternType.Sharp);
    }

    [Fact]
    public void AngleImmediatelyBelowThreshold_IsRejectedAndAboveIsAccepted()
    {
        // Integer coordinates cannot represent exactly 120°. These are ~119.97° and ~120.00°.
        var below = Analyze(Map((0, 0), (1000, 0), (500, 867), (1500, 867)));
        var above = Analyze(Map((0, 0), (1000, 0), (500, 866), (1500, 866)));
        Assert.Empty(below.Matches);
        Assert.Single(above.Matches);
        Assert.True(above.Points.All(point => point.Angle >= 120));
    }

    [Fact]
    public void NonSharpObject_InterruptsTheRun()
    {
        var result = Analyze(Map((0, 0), (100, 0), (30, 70), (130, 70), (230, 70)));
        Assert.Single(result.Matches);
        Assert.Equal((0, 3), (result.Matches[0].StartObjectIndex, result.Matches[0].EndObjectIndex));
    }

    [Fact]
    public void ConfidenceAndDebugAreFiniteAndDisabledByDefault()
    {
        var result = Analyze(Map((0, 0), (100, 0), (30, 70), (130, 70)));
        Assert.InRange(result.Confidence, 0, 1);
        Assert.False(BeatInsight.Diagnostics.GameplayDebug.SharpJumpEnabled);
        Assert.False(BeatInsight.Diagnostics.GameplayDebug.SharpJumpDetailedEnabled);
        BeatInsight.Diagnostics.GameplayDebug.SharpJump(result);
    }
}
