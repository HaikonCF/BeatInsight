using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Models;
using BeatInsight.Models.JumpPatterns;

namespace BeatInsight.Tests.Analysis;

public sealed class SpacedJumpPatternTests
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

    private static SpacedJumpAnalysis Analyze(Beatmap map) =>
        JumpPatternAnalyzer.AnalyzeSpaced(map.HitObjects, GameplayAnalyzer.Analyze(map));

    [Fact]
    public void IsolatedLargeJump_IsNotASpacedPattern() =>
        Assert.Empty(Analyze(Map((0, 0), (200, 0))).Matches);

    [Fact]
    public void TwoLargeLinks_AreNotASpacedPattern() =>
        Assert.Empty(Analyze(Map((0, 0), (150, 0), (300, 0))).Matches);

    [Fact]
    public void ThreeLargeLinks_ProduceASpacedSequence()
    {
        var result = Analyze(Map((0, 0), (150, 0), (300, 0), (450, 0)));

        var match = Assert.Single(result.Matches);
        Assert.Equal(JumpPatternType.Spaced, match.Type);
        Assert.Equal((0, 3), (match.StartObjectIndex, match.EndObjectIndex));
        Assert.Equal(3, result.SpacedTransitionCount);
        Assert.Equal(4, result.SpacedObjectCount);
        Assert.Equal(1, result.Coverage, 12);
    }

    [Fact]
    public void ShortJumps_AreRejected() =>
        Assert.Empty(Analyze(Map((0, 0), (149, 0), (298, 0), (447, 0))).Matches);

    [Fact]
    public void SmallBigSmall_DoesNotBecomeASpacedSequence() =>
        Assert.Empty(Analyze(Map((0, 0), (100, 0), (300, 0), (400, 0))).Matches);

    [Fact]
    public void ModeratelyVariableLargeDistances_AreAccepted()
    {
        var result = Analyze(Map((0, 0), (150, 0), (350, 0), (580, 0)));

        Assert.Single(result.Matches);
        Assert.InRange(result.DistanceConsistency, .45, 1);
    }

    [Fact]
    public void ExtremeDistanceOutlier_FailsTheConsistencyGuard() =>
        Assert.Empty(Analyze(Map((0, 0), (150, 0), (300, 0), (1300, 0))).Matches);

    [Fact]
    public void ExistingStreamMask_IsRespected()
    {
        var map = Map((0, 0), (150, 0), (300, 0), (450, 0));
        var result = JumpPatternAnalyzer.AnalyzeSpaced(map.HitObjects,
            new GameplayProfile { StreamSequences = [new(0, 3)] });

        Assert.Empty(result.Matches);
    }

    [Fact]
    public void CoverageUsesUniqueObjects()
    {
        var result = Analyze(Map((0, 0), (150, 0), (300, 0), (450, 0), (600, 0)));

        Assert.Single(result.Matches);
        Assert.Equal(4, result.SpacedTransitionCount);
        Assert.Equal(5, result.SpacedObjectCount);
        Assert.Equal(1, result.Coverage, 12);
    }

    [Fact]
    public void NonSpacedLink_InterruptsAndSeparatesSequences()
    {
        var result = Analyze(Map(
            (0, 0), (150, 0), (300, 0), (450, 0),
            (550, 0), (700, 0), (850, 0), (1000, 0)));

        Assert.Equal(2, result.SequenceCount);
        Assert.Equal(8, result.SpacedObjectCount);
        Assert.Equal(1, result.Coverage, 12);
    }

    [Theory]
    [InlineData(149, false)]
    [InlineData(150, true)]
    [InlineData(151, true)]
    public void DistanceBoundary_IsInclusiveAt150Pixels(int distance, bool expected)
    {
        var result = Analyze(Map((0, 0), (distance, 0), (distance * 2, 0), (distance * 3, 0)));

        Assert.Equal(expected, result.Matches.Count != 0);
    }

    [Fact]
    public void SpacedAndLinear_Coexist()
    {
        var map = Map((0, 0), (200, 0), (400, 0), (600, 0));
        var matches = JumpPatternAnalyzer.Analyze(map.HitObjects, GameplayAnalyzer.Analyze(map));

        Assert.Contains(matches, match => match.Type == JumpPatternType.Spaced);
        Assert.Contains(matches, match => match.Type == JumpPatternType.Linear);
    }

    [Fact]
    public void SpacedAndSharp_Coexist()
    {
        var map = Map((0, 0), (200, 0), (0, 200), (200, 200));
        var matches = JumpPatternAnalyzer.Analyze(map.HitObjects, GameplayAnalyzer.Analyze(map));

        Assert.Contains(matches, match => match.Type == JumpPatternType.Spaced);
        Assert.Contains(matches, match => match.Type == JumpPatternType.Sharp);
    }

    [Fact]
    public void SpacedAndTriangle_Coexist()
    {
        var map = Map((0, 0), (200, 200), (400, 0), (600, 200));
        var matches = JumpPatternAnalyzer.Analyze(map.HitObjects, GameplayAnalyzer.Analyze(map));

        Assert.Contains(matches, match => match.Type == JumpPatternType.Spaced);
        Assert.Contains(matches, match => match.Type == JumpPatternType.Triangle);
    }

    [Fact]
    public void SpacedAndEdge_Coexist()
    {
        var map = Map((20, 20), (492, 20), (20, 20), (492, 20));
        var matches = JumpPatternAnalyzer.Analyze(map.HitObjects, GameplayAnalyzer.Analyze(map));

        Assert.Contains(matches, match => match.Type == JumpPatternType.Spaced);
        Assert.Contains(matches, match => match.Type == JumpPatternType.Edge);
    }

    [Fact]
    public void SpacedAndOneTwo_Coexist()
    {
        var map = Map((0, 0), (200, 200), (400, 0), (600, 200), (800, 0));
        var matches = JumpPatternAnalyzer.Analyze(map.HitObjects, GameplayAnalyzer.Analyze(map));

        Assert.Contains(matches, match => match.Type == JumpPatternType.Spaced);
        Assert.Contains(matches, match => match.Type == JumpPatternType.OneTwo);
    }

    [Fact]
    public void ConfidenceIsFiniteAndDebugIsDisabledByDefault()
    {
        var result = Analyze(Map((0, 0), (150, 0), (300, 0), (450, 0)));

        Assert.InRange(result.Confidence, 0, 1);
        Assert.False(BeatInsight.Diagnostics.GameplayDebug.SpacedJumpEnabled);
        Assert.False(BeatInsight.Diagnostics.GameplayDebug.SpacedJumpDetailedEnabled);
        BeatInsight.Diagnostics.GameplayDebug.SpacedJump(result);
    }
}
