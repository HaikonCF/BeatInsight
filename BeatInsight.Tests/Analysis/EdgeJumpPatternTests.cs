using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Models;
using BeatInsight.Models.JumpPatterns;

namespace BeatInsight.Tests.Analysis;

public sealed class EdgeJumpPatternTests
{
    private static Beatmap Map(params (int X, int Y)[] points) => new()
    {
        CS = 4, SliderMultiplier = 1.4,
        TimingPoints = [new() { Time = 0, BeatLength = 500, Uninherited = true }],
        HitObjects = points.Select((p, i) => new HitObject
            { X = p.X, Y = p.Y, Type = 1, Time = i * 200 }).ToList()
    };

    private static EdgeJumpAnalysis Analyze(Beatmap map) =>
        JumpPatternAnalyzer.AnalyzeEdge(map.HitObjects, GameplayAnalyzer.Analyze(map));

    [Fact]
    public void ThreeObjectsAcrossField_AreEnoughWithoutLegacyFourObjectMinimum()
    {
        var result = Analyze(Map((20, 192), (490, 192), (20, 192)));
        Assert.Single(result.Matches);
        Assert.Equal(2, result.EdgeTransitionCount);
        Assert.Equal(2, result.StrongTransitionCount);
        Assert.Equal(3, result.EdgeObjectCount);
        Assert.Equal(3, result.LongestSequence);
        Assert.Equal(1, result.Coverage);
    }

    [Fact]
    public void IsolatedTransition_DoesNotContribute()
    {
        var result = Analyze(Map((0, 192), (512, 192)));
        Assert.Empty(result.Matches);
        Assert.Equal(0, result.Coverage);
        Assert.Equal(0, result.EdgeTransitionCount);
        Assert.Equal(0, result.Confidence);
    }

    [Fact]
    public void BorderProximityWithoutJumpDistance_IsInsufficient() =>
        Assert.Empty(Analyze(Map((0, 100), (0, 140), (0, 180), (0, 220))).Matches);

    [Fact]
    public void CentralObjectBreaksSequence() =>
        Assert.Empty(Analyze(Map((0, 192), (256, 192), (512, 192))).Matches);

    [Fact]
    public void SameEdgeSequence_IsValidWithoutStrongTraversals()
    {
        var result = Analyze(Map((100, 64), (220, 64), (340, 64)));
        Assert.Single(result.Matches);
        Assert.Equal(0, result.StrongTransitionCount);
        Assert.InRange(result.Confidence, 0.01, 1);
        Assert.True(Analyze(Map((0, 192), (512, 192), (0, 192))).Confidence > result.Confidence);
    }

    [Fact]
    public void CoverageCountsOnlyValidSequencesAndNeverDoubleCountsObjects()
    {
        var result = Analyze(Map((0, 192), (512, 192), (0, 192), (256, 192),
            (0, 192), (512, 192), (0, 192), (0, 193)));
        Assert.Equal(2, result.Matches.Count);
        Assert.Equal(6, result.EdgeObjectCount);
        Assert.Equal(4, result.EdgeTransitionCount);
        Assert.Equal(.75, result.Coverage);
    }

    [Fact]
    public void LinearAndEdgeCoexistWithoutIdentityMutation()
    {
        var map = Map((20, 20), (140, 20), (260, 20), (380, 20));
        var profile = GameplayAnalyzer.Analyze(map);
        var before = System.Text.Json.JsonSerializer.Serialize(profile);
        var matches = JumpPatternAnalyzer.Analyze(map.HitObjects, profile);
        Assert.Contains(matches, m => m.Type == JumpPatternType.Linear);
        Assert.Contains(matches, m => m.Type == JumpPatternType.Edge);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(profile));
        Assert.Equal(matches, JumpPatternAnalyzer.Analyze(map.HitObjects, profile));
    }

    [Theory]
    [InlineData(64, 192, EdgeZone.Left)]
    [InlineData(448, 192, EdgeZone.Right)]
    [InlineData(256, 64, EdgeZone.Top)]
    [InlineData(256, 320, EdgeZone.Bottom)]
    [InlineData(64, 64, EdgeZone.TopLeft)]
    [InlineData(448, 64, EdgeZone.TopRight)]
    [InlineData(64, 320, EdgeZone.BottomLeft)]
    [InlineData(448, 320, EdgeZone.BottomRight)]
    [InlineData(65, 65, EdgeZone.None)]
    public void ZonesHaveInclusiveBoundaries(int x, int y, EdgeZone expected) =>
        Assert.Equal(expected, JumpPatternAnalyzer.GetEdgeZone(new() { X = x, Y = y }));

    [Theory]
    [InlineData(0, 0, 512, 384)]
    [InlineData(512, 0, 0, 384)]
    [InlineData(256, 0, 256, 384)]
    public void OppositeCornersAndVerticalEdgesAreStrong(int x1, int y1, int x2, int y2) =>
        Assert.Equal(2, Analyze(Map((x1, y1), (x2, y2), (x1, y1))).StrongTransitionCount);

    [Fact]
    public void SliderSpinnerAndTimingBreaksAreNotBridged()
    {
        foreach (int type in new[] { 2, 8 })
        {
            var map = Map((0, 192), (512, 192), (0, 192));
            map.HitObjects[1].Type = type;
            Assert.Empty(Analyze(map).Matches);
        }
        var gap = Map((0, 192), (512, 192), (0, 192));
        gap.HitObjects[2].Time = 2000;
        Assert.Empty(Analyze(gap).Matches);
    }

    [Fact]
    public void ExistingStreamMaskIsRespected()
    {
        var map = Map((0, 192), (512, 192), (0, 192));
        Assert.Empty(JumpPatternAnalyzer.AnalyzeEdge(map.HitObjects,
            new GameplayProfile { StreamSequences = [new(0, 2)] }).Matches);
    }

    [Fact]
    public void EmptyInputIsFiniteAndDebugDefaultsAreOff()
    {
        var result = Analyze(Map());
        Assert.Equal(0, result.Coverage);
        Assert.Equal(0, result.Confidence);
        Assert.False(BeatInsight.Diagnostics.GameplayDebug.EdgeJumpEnabled);
        Assert.False(BeatInsight.Diagnostics.GameplayDebug.EdgeJumpDetailedEnabled);
        BeatInsight.Diagnostics.GameplayDebug.EdgeJump(result);
    }
}
