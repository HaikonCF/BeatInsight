using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Models;
using BeatInsight.Models.JumpPatterns;

namespace BeatInsight.Tests.Analysis;

public sealed class JumpSpacingTrendPatternTests
{
    private static Beatmap MapFromDistances(params int[] distances)
    {
        int x = 0;
        var points = new List<(int X, int Y)> { (x, 0) };
        foreach (int distance in distances)
        {
            x += distance;
            points.Add((x, 0));
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
            X = point.X, Y = point.Y, Time = index * 200, Type = 1
        }).ToList()
    };

    private static JumpSpacingTrendAnalysis Analyze(Beatmap map) =>
        JumpPatternAnalyzer.AnalyzeSpacingTrend(map.HitObjects, GameplayAnalyzer.Analyze(map));

    [Fact]
    public void StrictlyIncreasingDistances_AreAccelerating()
    {
        var result = Analyze(MapFromDistances(80, 110, 145, 185, 230));

        var sequence = Assert.Single(result.Sequences);
        Assert.Equal(JumpSpacingTrendDirection.Accelerating, sequence.Direction);
        Assert.Equal(1, result.AcceleratingSequenceCount);
        Assert.Equal(0, result.DeceleratingSequenceCount);
        Assert.Equal(6, result.AcceleratingObjectCount);
        Assert.Equal(1, result.AcceleratingCoverage, 12);
    }

    [Fact]
    public void StrictlyDecreasingDistances_AreDecelerating()
    {
        var result = Analyze(MapFromDistances(230, 185, 145, 110, 80));

        Assert.Equal(JumpSpacingTrendDirection.Decelerating, Assert.Single(result.Sequences).Direction);
        Assert.Equal(1, result.DeceleratingSequenceCount);
        Assert.Equal(0, result.AcceleratingSequenceCount);
        Assert.Equal(6, result.DeceleratingObjectCount);
    }

    [Fact]
    public void ConstantAndRandomDistances_AreRejected()
    {
        Assert.Empty(Analyze(MapFromDistances(120, 120, 120, 120)).Sequences);
        Assert.Empty(Analyze(MapFromDistances(100, 180, 90, 170, 110)).Sequences);
    }

    [Fact]
    public void TooShortAndInsignificantProgression_AreRejected()
    {
        Assert.Empty(Analyze(MapFromDistances(80, 110, 145)).Sequences);
        Assert.Empty(Analyze(MapFromDistances(100, 105, 110, 115, 120)).Sequences);
    }

    [Fact]
    public void SingleLargeStepFollowedByAPlateau_IsNotAProgressiveTrend()
    {
        // One meaningful delta among flat links is a spacing step, not an accelerating run.
        Assert.Empty(Analyze(MapFromDistances(96, 96, 96, 184, 184, 184, 184)).Sequences);
    }

    [Fact]
    public void StrongProgressionAndOneSmallAnomaly_AreAccepted()
    {
        var result = Analyze(MapFromDistances(90, 120, 150, 145, 190));

        var sequence = Assert.Single(result.Sequences);
        Assert.Equal(JumpSpacingTrendDirection.Accelerating, sequence.Direction);
        Assert.Equal(.75, sequence.TrendConsistency, 12); // -5px is neutral, not supporting evidence.
        Assert.True(sequence.TrendMagnitude >= .25);
    }

    [Fact]
    public void TooManyDirectionAnomalies_AreRejected() =>
        Assert.Empty(Analyze(MapFromDistances(90, 140, 100, 150, 110)).Sequences);

    [Fact]
    public void ClearInversion_SplitsAcceleratingAndDeceleratingRuns()
    {
        var result = Analyze(MapFromDistances(80, 110, 145, 185, 150, 115, 80, 65));

        Assert.Contains(result.Sequences, sequence => sequence.Direction == JumpSpacingTrendDirection.Accelerating);
        Assert.Contains(result.Sequences, sequence => sequence.Direction == JumpSpacingTrendDirection.Decelerating);
        Assert.Equal(2, result.Sequences.Count);
    }

    [Fact]
    public void AcceleratingAndDeceleratingCanCoexistWithSpaced()
    {
        var accelerating = MapFromDistances(160, 190, 220, 250, 280);
        var decelerating = MapFromDistances(280, 250, 220, 190, 160);
        var accelMatches = JumpPatternAnalyzer.Analyze(accelerating.HitObjects, GameplayAnalyzer.Analyze(accelerating));
        var decelMatches = JumpPatternAnalyzer.Analyze(decelerating.HitObjects, GameplayAnalyzer.Analyze(decelerating));

        Assert.Contains(accelMatches, match => match.Type == JumpPatternType.Accelerating);
        Assert.Contains(accelMatches, match => match.Type == JumpPatternType.Spaced);
        Assert.Contains(decelMatches, match => match.Type == JumpPatternType.Decelerating);
        Assert.Contains(decelMatches, match => match.Type == JumpPatternType.Spaced);
    }

    [Fact]
    public void AcceleratingCanBeNonSpacedAndLinear()
    {
        var map = MapFromDistances(70, 90, 115, 140, 170);
        var matches = JumpPatternAnalyzer.Analyze(map.HitObjects, GameplayAnalyzer.Analyze(map));

        Assert.Contains(matches, match => match.Type == JumpPatternType.Accelerating);
        Assert.Contains(matches, match => match.Type == JumpPatternType.Linear);
        Assert.DoesNotContain(matches, match => match.Type == JumpPatternType.Spaced);
    }

    [Fact]
    public void AcceleratingCanCoexistWithArc()
    {
        var map = Map((0, 0), (80, 0), (154, 74), (154, 204), (44, 314));
        var matches = JumpPatternAnalyzer.Analyze(map.HitObjects, GameplayAnalyzer.Analyze(map));

        Assert.Contains(matches, match => match.Type == JumpPatternType.Accelerating);
        Assert.Contains(matches, match => match.Type == JumpPatternType.Arc);
    }

    [Fact]
    public void AcceleratingCanCoexistWithOneTwo()
    {
        var map = Map((0, 0), (100, 0), (-10, 0), (110, 0), (-20, 0), (120, 0));
        var matches = JumpPatternAnalyzer.Analyze(map.HitObjects, GameplayAnalyzer.Analyze(map));

        Assert.Contains(matches, match => match.Type == JumpPatternType.Accelerating);
        Assert.Contains(matches, match => match.Type == JumpPatternType.OneTwo);
    }

    [Fact]
    public void StreamMaskAndCoverageDeduplication_AreRespected()
    {
        var map = MapFromDistances(80, 110, 145, 185, 30, 80, 110, 145, 185);
        var result = Analyze(map);
        Assert.Equal(2, result.AcceleratingSequenceCount);
        Assert.Equal(10, result.AcceleratingObjectCount);
        Assert.Equal(1, result.AcceleratingCoverage, 12);

        var blocked = JumpPatternAnalyzer.AnalyzeSpacingTrend(map.HitObjects,
            new GameplayProfile { StreamSequences = [new(0, map.HitObjects.Count - 1)] });
        Assert.Empty(blocked.Sequences);
    }

    [Theory]
    [InlineData(new[] { 59, 59, 59, 59 }, false)]
    [InlineData(new[] { 60, 80, 105, 135, 170 }, true)]
    [InlineData(new[] { 61, 81, 106, 136, 171 }, true)]
    public void MinimumDistanceBoundary_IsInclusive(int[] distances, bool expected) =>
        Assert.Equal(expected, Analyze(MapFromDistances(distances)).Sequences.Count != 0);

    [Fact]
    public void TrendConsistencyBoundary_IsInclusiveAtThreeQuarters()
    {
        // With discrete delta counts, .667 is the nearest value below .75 and .80 the nearest value above it.
        Assert.Empty(Analyze(MapFromDistances(80, 150, 220, 150)).Sequences); // 2/3
        Assert.Single(Analyze(MapFromDistances(80, 110, 140, 170, 130)).Sequences); // 3/4
        Assert.Single(Analyze(MapFromDistances(80, 110, 140, 170, 200, 160)).Sequences); // 4/5
    }

    [Fact]
    public void TrendMagnitudeBoundary_IsInclusiveAtQuarter()
    {
        Assert.Empty(Analyze(MapFromDistances(110, 120, 130, 140)).Sequences);
        var atOrAbove = Assert.Single(Analyze(MapFromDistances(105, 115, 125, 135)).Sequences);
        Assert.True(atOrAbove.TrendMagnitude >= .25);
        Assert.Single(Analyze(MapFromDistances(100, 110, 120, 130)).Sequences);
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(11, true)]
    public void DeltaStepBoundary_UsesAnInclusiveTenPixelSignificanceGate(int step, bool expected)
    {
        var result = Analyze(MapFromDistances(100, 100 + step, 100 + 2 * step,
            100 + 3 * step, 100 + 4 * step));
        Assert.Equal(expected, result.Sequences.Count != 0);
    }

    [Fact]
    public void TimingDoesNotChangeASpatialTrend_WhenBothCadencesAreJumpQualified()
    {
        Beatmap fast = MapFromDistances(80, 110, 145, 185, 230);
        Beatmap slow = MapFromDistances(80, 110, 145, 185, 230);
        for (int index = 0; index < fast.HitObjects.Count; index++)
        {
            fast.HitObjects[index].Time = index * 40;
            slow.HitObjects[index].Time = index * 600;
        }

        var fastResult = Analyze(fast);
        var slowResult = Analyze(slow);
        Assert.Equal(fastResult.AcceleratingSequenceCount, slowResult.AcceleratingSequenceCount);
        Assert.Equal(fastResult.AcceleratingCoverage, slowResult.AcceleratingCoverage, 12);
        Assert.Equal(fastResult.Accelerating.AverageTrendMagnitude,
            slowResult.Accelerating.AverageTrendMagnitude, 12);
    }

    [Fact]
    public void ConfidenceIsBoundedAndDebugIsDisabledByDefault()
    {
        var result = Analyze(MapFromDistances(80, 110, 145, 185, 230));

        Assert.InRange(result.Accelerating.Confidence, 0, 1);
        Assert.InRange(result.Decelerating.Confidence, 0, 1);
        Assert.False(BeatInsight.Diagnostics.GameplayDebug.JumpSpacingTrendEnabled);
        Assert.False(BeatInsight.Diagnostics.GameplayDebug.JumpSpacingTrendDetailedEnabled);
        BeatInsight.Diagnostics.GameplayDebug.JumpSpacingTrend(result);
    }
}
