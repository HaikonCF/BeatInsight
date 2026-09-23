using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Models;
using BeatInsight.Models.JumpPatterns;

namespace BeatInsight.Tests.Analysis;

public sealed class JumpPatternAnalyzerTests
{
    private static Beatmap Map(params (int X, int Y)[] points) => new()
    {
        CS = 4, SliderMultiplier = 1.4,
        TimingPoints = [new() { Time = 0, BeatLength = 500, Uninherited = true }],
        HitObjects = points.Select((p, i) => new HitObject
            { X = p.X, Y = p.Y, Time = i * 200, Type = 1 }).ToList()
    };

    private static IReadOnlyList<JumpPatternMatch> Analyze(Beatmap map)
    {
        var profile = GameplayAnalyzer.Analyze(map);
        return JumpPatternAnalyzer.Analyze(map.HitObjects, profile);
    }

    [Fact]
    public void StraightFourJumps_AreDetectedWithInclusiveIndicesAndTimes()
    {
        var match = Assert.Single(Analyze(Map((0, 0), (100, 0), (200, 0), (300, 0))));
        Assert.Equal(JumpPatternType.Linear, match.Type);
        Assert.Equal(0, match.StartObjectIndex);
        Assert.Equal(3, match.EndObjectIndex);
        Assert.Equal(4, match.ObjectCount);
        Assert.Equal(0, match.StartTime);
        Assert.Equal(600, match.EndTime);
        Assert.Equal(1, match.Confidence);
    }

    [Fact]
    public void SlightlyImperfectLine_IsDetected()
    {
        var match = Assert.Single(Analyze(Map((0, 0), (100, 5), (200, -3), (300, 2))));
        Assert.InRange(match.Confidence, 0, 1);
        Assert.True(match.Confidence < 1);
    }

    [Fact]
    public void StrongBendWithoutFourAlignedObjects_IsRejectedAsLinear() =>
        Assert.DoesNotContain(Analyze(Map((0, 0), (100, 0), (100, 100), (200, 100))),
            match => match.Type == JumpPatternType.Linear);

    [Fact]
    public void StrongBend_SplitsTwoValidLinesAtSharedPivot()
    {
        var matches = Analyze(Map((0, 0), (100, 0), (200, 0), (300, 0),
            (300, 100), (300, 200), (300, 300)));
        Assert.Equal(new[] { (0, 3), (3, 6) },
            matches.Select(m => (m.StartObjectIndex, m.EndObjectIndex)));
    }

    [Fact]
    public void ProgressiveCurve_IsStoppedByGlobalDirectionDespiteSmallLocalTurns()
    {
        // Headings approximately 0, 24, 48, 72 degrees: every local turn <25,
        // but the full sequence deviates >30 from its overall displacement axis.
        var matches = Analyze(Map((0, 0), (100, 0), (191, 41), (258, 115), (289, 210)));
        // Arc may independently recognize this progressive same-direction curve;
        // this test only verifies that Linear still stops at the global deviation.
        var match = Assert.Single(matches, match => match.Type == JumpPatternType.Linear);
        Assert.Equal(3, match.EndObjectIndex);
    }

    [Fact]
    public void CompactStream_IsNotJumpQualified()
    {
        var map = Map((0, 0), (10, 0), (20, 0), (30, 0), (40, 0), (50, 0));
        foreach (var obj in map.HitObjects) obj.Time /= 2;
        Assert.Empty(Analyze(map));
    }

    [Fact]
    public void AlignedSpacedStream_IsExcludedByExistingStreamQualification()
    {
        var map = Map((0, 0), (40, 0), (80, 0), (120, 0), (160, 0), (200, 0));
        foreach (var obj in map.HitObjects) obj.Time /= 2;
        var profile = GameplayAnalyzer.Analyze(map);
        Assert.NotEmpty(profile.StreamSequences);
        Assert.Empty(JumpPatternAnalyzer.Analyze(map.HitObjects, profile));
    }

    [Fact]
    public void IrregularSpacing_OnLine_IsStillDetected() =>
        Assert.Single(Analyze(Map((0, 0), (40, 0), (180, 0), (250, 0))));

    [Fact]
    public void IrregularRhythm_WithinExistingJumpBounds_IsStillDetected()
    {
        var map = Map((0, 0), (100, 0), (200, 0), (300, 0));
        int[] times = [0, 80, 420, 570];
        for (int i = 0; i < times.Length; i++) map.HitObjects[i].Time = times[i];
        Assert.Single(Analyze(map));
    }

    [Fact]
    public void ReversedDirectionAcrossAngleWrap_IsStillLinear() =>
        Assert.Single(Analyze(Map((300, 0), (200, 2), (100, -1), (0, 0))));

    [Fact]
    public void EmptyOrTooShort_ReturnsNoMatches()
    {
        Assert.Empty(Analyze(Map()));
        Assert.Empty(Analyze(Map((0, 0), (100, 0), (200, 0))));
    }

    [Fact]
    public void Recognition_IsDeterministicAndDoesNotMutateProfile()
    {
        var map = Map((0, 0), (100, 0), (200, 0), (300, 0));
        var profile = GameplayAnalyzer.Analyze(map);
        var before = System.Text.Json.JsonSerializer.Serialize(profile);
        var first = JumpPatternAnalyzer.Analyze(map.HitObjects, profile);
        Assert.Equal(first, JumpPatternAnalyzer.Analyze(map.HitObjects, profile));
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(profile));
    }
}
