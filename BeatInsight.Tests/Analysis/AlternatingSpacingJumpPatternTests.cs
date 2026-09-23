using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Models;
using BeatInsight.Models.JumpPatterns;

namespace BeatInsight.Tests.Analysis;

public sealed class AlternatingSpacingJumpPatternTests
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

    private static AlternatingSpacingJumpAnalysis Analyze(Beatmap map) =>
        JumpPatternAnalyzer.AnalyzeAlternatingSpacing(map.HitObjects, GameplayAnalyzer.Analyze(map));

    [Fact]
    public void SmallLargeAlternation_IsRecognizedAsExpandingFirst()
    {
        var result = Analyze(MapFromDistances(90, 170, 100, 180, 95, 175));

        var sequence = Assert.Single(result.Sequences);
        Assert.Equal(AlternatingSpacingStart.ExpandingFirst, sequence.Start);
        Assert.Equal(7, result.AlternatingSpacingObjectCount);
        Assert.Equal(1, result.Coverage, 12);
        Assert.Equal(1, sequence.AlternationConsistency, 12);
    }

    [Fact]
    public void LargeSmallAlternation_IsRecognizedAsContractingFirst()
    {
        var result = Analyze(MapFromDistances(180, 95, 170, 100, 175, 90));

        Assert.Equal(AlternatingSpacingStart.ContractingFirst, Assert.Single(result.Sequences).Start);
        Assert.Equal(1, result.ContractingFirstSequenceCount);
    }

    [Fact]
    public void ConstantMonotoneAndRandomSpacing_AreRejected()
    {
        Assert.Empty(Analyze(MapFromDistances(120, 120, 120, 120, 120)).Sequences);
        Assert.Empty(Analyze(MapFromDistances(90, 110, 140, 170, 200)).Sequences);
        Assert.Empty(Analyze(MapFromDistances(200, 170, 140, 110, 90)).Sequences);
        Assert.Empty(Analyze(MapFromDistances(90, 180, 120, 210, 80, 160)).Sequences);
    }

    [Fact]
    public void ShortAndMicroOscillationRuns_AreRejected()
    {
        Assert.Empty(Analyze(MapFromDistances(90, 170, 100, 180)).Sequences);
        Assert.Empty(Analyze(MapFromDistances(100, 110, 101, 111, 102, 112)).Sequences);
    }

    [Fact]
    public void SingleLargeChangeFollowedByAPlateau_IsNotAnAlternatingPattern()
    {
        Assert.Empty(Analyze(MapFromDistances(96, 96, 96, 184, 184, 184)).Sequences);
    }

    [Fact]
    public void OneInteriorAnomalyCanBeTolerated_WhenOverallConsistencyIsThreeQuarters()
    {
        var sequence = Assert.Single(Analyze(MapFromDistances(90, 170, 100, 180, 240, 170)).Sequences);

        Assert.Equal(.75, sequence.AlternationConsistency, 12);
        Assert.True(sequence.SpacingContrast >= .20);
    }

    [Fact]
    public void TooManyAnomalies_AreRejected() =>
        Assert.Empty(Analyze(MapFromDistances(90, 170, 100, 180, 220, 280)).Sequences);

    [Fact]
    public void NeutralDeltaDoesNotSupportAlternation()
    {
        var withNine = Assert.Single(Analyze(MapFromDistances(70, 79, 60, 80, 60, 80)).Sequences);
        var withTen = Assert.Single(Analyze(MapFromDistances(70, 80, 60, 80, 60, 80)).Sequences);
        var withEleven = Assert.Single(Analyze(MapFromDistances(70, 81, 60, 80, 60, 80)).Sequences);

        Assert.Equal(.75, withNine.AlternationConsistency, 12);
        Assert.Equal(1, withTen.AlternationConsistency, 12);
        Assert.Equal(1, withEleven.AlternationConsistency, 12);
    }

    [Fact]
    public void PersistentBreak_SegmentsTheRunBeforeTheTrailingNonAlternation()
    {
        var result = Analyze(MapFromDistances(90, 170, 100, 180, 95, 175, 190, 205));

        var sequence = Assert.Single(result.Sequences);
        Assert.Equal((0, 6), (sequence.StartObjectIndex, sequence.EndObjectIndex));
    }

    [Fact]
    public void StreamMaskAndCoverageDeduplication_AreRespected()
    {
        var map = MapFromDistances(90, 170, 100, 180, 95, 30, 90, 170, 100, 180, 95);
        var result = Analyze(map);
        Assert.Equal(2, result.AlternatingSpacingSequenceCount);
        Assert.Equal(12, result.AlternatingSpacingObjectCount);
        Assert.Equal(1, result.Coverage, 12);

        var blocked = JumpPatternAnalyzer.AnalyzeAlternatingSpacing(map.HitObjects,
            new GameplayProfile { StreamSequences = [new(0, map.HitObjects.Count - 1)] });
        Assert.Empty(blocked.Sequences);
    }

    [Theory]
    [InlineData(new[] { 59, 170, 100, 180, 95 }, false)]
    [InlineData(new[] { 60, 170, 100, 180, 95, 175 }, true)]
    [InlineData(new[] { 61, 170, 100, 180, 95, 175 }, true)]
    public void MinimumDistanceBoundary_IsInclusive(int[] distances, bool expected) =>
        Assert.Equal(expected, Analyze(MapFromDistances(distances)).Sequences.Count != 0);

    [Fact]
    public void AlternationConsistencyBoundary_IsInclusiveAtThreeQuarters()
    {
        Assert.Empty(Analyze(MapFromDistances(90, 170, 100, 180, 220, 280)).Sequences);
        Assert.Single(Analyze(MapFromDistances(90, 170, 100, 180, 240, 170)).Sequences);
        Assert.Single(Analyze(MapFromDistances(90, 170, 100, 180, 240, 170, 250)).Sequences);
    }

    [Fact]
    public void SpacingContrastBoundary_IsInclusiveAtTwentyPercent()
    {
        Assert.Empty(Analyze(MapFromDistances(91, 110, 91, 110, 91, 110)).Sequences);
        var atThreshold = Assert.Single(Analyze(MapFromDistances(90, 110, 90, 110, 90, 110)).Sequences);
        Assert.Equal(.20, atThreshold.SpacingContrast, 12);
        Assert.Single(Analyze(MapFromDistances(89, 110, 89, 110, 89, 110)).Sequences);
    }

    [Fact]
    public void OscillationMagnitudeConsistencyBoundary_IsInclusiveAtOneHalf()
    {
        Assert.Empty(Analyze(MapFromDistances(100, 200, 151, 251, 202, 302)).Sequences);
        var atThreshold = Assert.Single(Analyze(MapFromDistances(100, 200, 150, 250, 200, 300)).Sequences);
        Assert.Equal(.50, atThreshold.OscillationMagnitudeConsistency, 12);
        Assert.Single(Analyze(MapFromDistances(100, 200, 149, 249, 198, 298)).Sequences);
    }

    [Fact]
    public void AlternatingSpacingCanCoexistWithOneTwoSpacedLinearAndArc()
    {
        var oneTwo = Map((0, 0), (110, 0), (-70, 0), (45, 0), (-130, 0), (-18, 0), (160, 0));
        var oneTwoMatches = JumpPatternAnalyzer.Analyze(oneTwo.HitObjects, GameplayAnalyzer.Analyze(oneTwo));
        Assert.Contains(oneTwoMatches, match => match.Type == JumpPatternType.AlternatingSpacing);
        Assert.Contains(oneTwoMatches, match => match.Type == JumpPatternType.OneTwo);

        var spaced = MapFromDistances(160, 220, 170, 230, 165, 225);
        var spacedMatches = JumpPatternAnalyzer.Analyze(spaced.HitObjects, GameplayAnalyzer.Analyze(spaced));
        Assert.Contains(spacedMatches, match => match.Type == JumpPatternType.AlternatingSpacing);
        Assert.Contains(spacedMatches, match => match.Type == JumpPatternType.Spaced);
        Assert.Contains(spacedMatches, match => match.Type == JumpPatternType.Linear);

        var arc = CurvedMap(90, 170, 100, 180, 95, 175);
        var arcMatches = JumpPatternAnalyzer.Analyze(arc.HitObjects, GameplayAnalyzer.Analyze(arc));
        Assert.Contains(arcMatches, match => match.Type == JumpPatternType.AlternatingSpacing);
        Assert.Contains(arcMatches, match => match.Type == JumpPatternType.Arc);
    }

    [Fact]
    public void AlternatingSpacingIsNotConfusedWithAcceleratingOrDecelerating()
    {
        var map = MapFromDistances(90, 170, 100, 180, 95, 175);
        var trends = JumpPatternAnalyzer.AnalyzeSpacingTrend(map.HitObjects, GameplayAnalyzer.Analyze(map));

        Assert.Empty(trends.Sequences);
        Assert.Single(Analyze(map).Sequences);
    }

    [Fact]
    public void ConfidenceIsBoundedAndDebugIsDisabledByDefault()
    {
        var result = Analyze(MapFromDistances(90, 170, 100, 180, 95, 175));
        Assert.InRange(result.Confidence, 0, 1);
        Assert.False(BeatInsight.Diagnostics.GameplayDebug.AlternatingSpacingJumpEnabled);
        Assert.False(BeatInsight.Diagnostics.GameplayDebug.AlternatingSpacingJumpDetailedEnabled);
        BeatInsight.Diagnostics.GameplayDebug.AlternatingSpacingJump(result);
    }

    private static Beatmap CurvedMap(params int[] distances)
    {
        double x = 0, y = 0, heading = 0;
        var points = new List<(int X, int Y)> { (0, 0) };
        foreach (int distance in distances)
        {
            x += Math.Cos(heading * Math.PI / 180) * distance;
            y += Math.Sin(heading * Math.PI / 180) * distance;
            points.Add(((int)Math.Round(x), (int)Math.Round(y)));
            heading += 30;
        }
        return Map(points.ToArray());
    }
}
