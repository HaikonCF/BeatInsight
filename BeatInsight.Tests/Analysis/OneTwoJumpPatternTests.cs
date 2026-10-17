using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Models;
using BeatInsight.Models.JumpPatterns;
using System.IO;

namespace BeatInsight.Tests.Analysis;

public sealed class OneTwoJumpPatternTests
{
    private static Beatmap Map(params (int X, int Y)[] points) => new()
    {
        CS = 4, SliderMultiplier = 1.4,
        TimingPoints = [new() { Time = 0, BeatLength = 500, Uninherited = true }],
        HitObjects = points.Select((point, index) => new HitObject
            { X = point.X, Y = point.Y, Time = index * 200, Type = 1 }).ToList()
    };

    private static OneTwoJumpAnalysis Analyze(Beatmap map) =>
        JumpPatternAnalyzer.AnalyzeOneTwo(map.HitObjects, GameplayAnalyzer.Analyze(map));

    [Fact]
    public void RegularLeftRightAlternation_IsRecognized()
    {
        var result = Analyze(Map((0, 0), (100, 100), (200, 0), (300, 100), (400, 0)));
        var match = Assert.Single(result.Matches);
        Assert.Equal(JumpPatternType.OneTwo, match.Type);
        Assert.Equal((0, 4), (match.StartObjectIndex, match.EndObjectIndex));
        Assert.Equal(3, result.OneTwoPointCount);
        Assert.Equal(5, result.OneTwoObjectCount);
        Assert.Equal(1, result.AlternationConsistency);
        Assert.Equal(2, result.CandidateBlockCount);
        Assert.Equal(2, result.AcceptedBlockCount);
        Assert.Empty(result.ResetObjectIndices);
    }

    [Fact]
    public void RepeatedBlocksSeparatedByOneGeometricReset_AreRecognized()
    {
        var result = Analyze(Map(
            (0, 0), (100, 0), (100, -100), (200, -100), (200, 0), (300, 0)));

        var match = Assert.Single(result.Matches);
        Assert.Equal((0, 5), (match.StartObjectIndex, match.EndObjectIndex));
        Assert.Equal(2, result.CandidateBlockCount);
        Assert.Equal(2, result.AcceptedBlockCount);
        Assert.Equal([3], result.ResetObjectIndices);
        Assert.Equal(2d / 3d, result.AlternationConsistency, 12);
        Assert.Equal([false, true, false, true],
            result.Points.Select(point => point.AlternatingWithPrevious).ToArray());
    }

    [Fact]
    public void RegularRightLeftAlternation_IsRecognized() =>
        Assert.Single(Analyze(Map((0, 0), (100, -100), (200, 0), (300, -100), (400, 0))).Matches);

    [Fact]
    public void StraightLine_SharpIsolatedReversalIsolatedAndTooShort_AreRejected()
    {
        Assert.Empty(Analyze(Map((0, 0), (100, 0), (200, 0), (300, 0))).Matches);
        Assert.Empty(Analyze(Map((0, 0), (100, 0), (30, 70), (-40, 140))).Matches);
        Assert.Empty(Analyze(Map((0, 0), (100, 0), (0, 0), (-100, 0))).Matches);
        Assert.Empty(Analyze(Map((0, 0), (100, 100), (200, 0))).Matches);
    }

    [Fact]
    public void RepeatedReversals_AreStableAlternatingChains()
    {
        var result = Analyze(Map((0, 0), (100, 0), (1, 1), (101, 1), (2, 2)));
        Assert.Single(result.Matches);
        Assert.All(result.Points, point => Assert.Equal(OneTwoTurnDirection.Reversal, point.TurnDirection));
    }

    [Fact]
    public void SameDirectionTurn_BreaksTheAlternation()
    {
        var result = Analyze(Map((0, 0), (100, 100), (200, 0), (300, -100)));
        Assert.Empty(result.Matches);
    }

    [Fact]
    public void TwoArbitraryResetRelations_BreakBlockRepetition()
    {
        var result = Analyze(Map(
            (0, 0), (100, 0), (100, -100), (200, -100),
            (200, 0), (100, 0), (100, 100)));

        Assert.Empty(result.Matches);
        Assert.Equal(2, result.CandidateBlockCount);
        Assert.Equal(2, result.RejectedIsolatedBlockCount);
    }

    [Fact]
    public void MultipleSingleResetGaps_DoNotMergeIntoOneOccurrence()
    {
        Beatmap map = Map(
            (3, 186), (353, 336), (114, 57), (465, 157),
            (77, 237), (431, 238), (99, 147), (384, 64));
        int[] times = [0, 147, 294, 442, 589, 736, 883, 1_030];
        for (int index = 0; index < times.Length; index++)
            map.HitObjects[index].Time = times[index];

        OneTwoJumpAnalysis result = Analyze(map);

        JumpPatternMatch match = Assert.Single(result.Matches);
        Assert.Equal((0, 5), (match.StartObjectIndex, match.EndObjectIndex));
        Assert.DoesNotContain(result.Matches,
            candidate => candidate.StartObjectIndex == 0 && candidate.EndObjectIndex == 7);
        Assert.Single(result.ResetObjectIndices);
    }

    [Fact]
    public void ReasonableDistanceAndTimingVariation_AreAccepted()
    {
        var map = Map((0, 0), (100, 100), (260, 0), (380, 100), (520, 0));
        int[] times = [0, 180, 400, 590, 800];
        for (int i = 0; i < times.Length; i++) map.HitObjects[i].Time = times[i];
        var result = Analyze(map);
        Assert.Single(result.Matches);
        Assert.True(result.DistanceConsistency >= .55);
        Assert.True(result.IntervalConsistency >= .55);
    }

    [Fact]
    public void ExtremelyInconsistentDistanceOrTiming_IsRejected()
    {
        Assert.Empty(Analyze(Map((0, 0), (60, 60), (500, 0), (560, 60), (1000, 0))).Matches);

        var timing = Map((0, 0), (100, 100), (200, 0), (300, 100), (400, 0));
        int[] times = [0, 50, 350, 400, 700];
        for (int i = 0; i < times.Length; i++) timing.HitObjects[i].Time = times[i];
        Assert.Empty(Analyze(timing).Matches);
    }

    [Fact]
    public void StreamMaskAndCoverageDeduplication_AreRespected()
    {
        var map = Map((0, 0), (100, 100), (200, 0), (300, 100), (400, 0));
        var result = Analyze(map);
        Assert.Equal(1, result.Coverage, 12);
        Assert.Empty(JumpPatternAnalyzer.AnalyzeOneTwo(map.HitObjects,
            new GameplayProfile { StreamSequences = [new(0, 4)] }).Matches);
    }

    [Fact]
    public void NegativeSharpZigzag_IsNotPromotedFromOneIsolatedBlock()
    {
        Beatmap map = Map((0, 0), (100, 0), (13, 50), (113, 50));
        var result = Analyze(map);

        Assert.Empty(result.Matches);
        Assert.Equal(1, result.CandidateBlockCount);
        Assert.Equal(1, result.RejectedIsolatedBlockCount);
        Assert.Contains(JumpPatternAnalyzer.Analyze(map.HitObjects, GameplayAnalyzer.Analyze(map)),
            match => match.Type == JumpPatternType.Sharp);
    }

    [Fact]
    public void NegativeTriangleChain_IsNotPromotedWithoutRepeatedAlternatingBlocks()
    {
        Beatmap map = Map((0, 0), (100, 0), (50, 87), (0, 0), (100, 0));
        var matches = JumpPatternAnalyzer.Analyze(map.HitObjects, GameplayAnalyzer.Analyze(map));

        Assert.Contains(matches, match => match.Type == JumpPatternType.Triangle);
        Assert.DoesNotContain(matches, match => match.Type == JumpPatternType.OneTwo);
    }

    [Fact]
    public void NegativeGenericSpacedChain_IsNotPromoted()
    {
        Beatmap map = Map((0, 0), (200, 0), (400, 0), (600, 0), (800, 0));
        var matches = JumpPatternAnalyzer.Analyze(map.HitObjects, GameplayAnalyzer.Analyze(map));

        Assert.Contains(matches, match => match.Type == JumpPatternType.Spaced);
        Assert.DoesNotContain(matches, match => match.Type == JumpPatternType.OneTwo);
    }

    [Fact]
    public void NegativeRandomDirectionChain_IsNotPromotedAcrossTwoResets()
    {
        Beatmap map = Map(
            (0, 0), (100, 0), (100, -100), (200, -100),
            (200, 0), (100, 0), (100, 100));

        Assert.Empty(Analyze(map).Matches);
    }

    [Fact]
    public void NegativeSameDirectionArc_IsNotPromoted()
    {
        Beatmap map = Map((0, 0), (100, 0), (100, 100), (0, 100), (0, 0));
        var matches = JumpPatternAnalyzer.Analyze(map.HitObjects, GameplayAnalyzer.Analyze(map));

        Assert.Contains(matches, match => match.Type == JumpPatternType.Arc);
        Assert.DoesNotContain(matches, match => match.Type == JumpPatternType.OneTwo);
    }

    [Fact]
    public void NegativeStreamOrBurstLikeSequence_IsRejectedByStreamMask()
    {
        Beatmap map = Map((0, 0), (100, 100), (200, 0), (300, 100), (400, 0));
        foreach (HitObject hitObject in map.HitObjects)
            hitObject.Time /= 2;

        var result = JumpPatternAnalyzer.AnalyzeOneTwo(map.HitObjects,
            new GameplayProfile { StreamSequences = [new(0, 4)] });

        Assert.Empty(result.Matches);
        Assert.Equal(0, result.CandidateBlockCount);
    }

    [Fact]
    public void NegativeIsolatedAlternatingPivotPair_IsRejected()
    {
        var result = Analyze(Map((0, 0), (100, 0), (100, -100), (200, -100)));

        Assert.Empty(result.Matches);
        Assert.Equal(1, result.CandidateBlockCount);
        Assert.Equal(1, result.RejectedIsolatedBlockCount);
    }

    [Fact]
    public void OneTwoCoexistsWithSharpEdgeAndTriangle()
    {
        var sharpEdge = Map((20, 20), (492, 20), (20, 20), (492, 20), (20, 20));
        var sharpEdgeMatches = JumpPatternAnalyzer.Analyze(sharpEdge.HitObjects, GameplayAnalyzer.Analyze(sharpEdge));
        Assert.Contains(sharpEdgeMatches, match => match.Type == JumpPatternType.OneTwo);
        Assert.Contains(sharpEdgeMatches, match => match.Type == JumpPatternType.Sharp);
        Assert.Contains(sharpEdgeMatches, match => match.Type == JumpPatternType.Edge);

        var triangle = Map((0, 0), (100, 100), (200, 0), (300, 100), (400, 0));
        var triangleMatches = JumpPatternAnalyzer.Analyze(triangle.HitObjects, GameplayAnalyzer.Analyze(triangle));
        Assert.Contains(triangleMatches, match => match.Type == JumpPatternType.OneTwo);
        Assert.Contains(triangleMatches, match => match.Type == JumpPatternType.Triangle);
    }

    [Fact]
    public void ConfidenceIsFiniteAndDebugDefaultsAreOff()
    {
        var result = Analyze(Map((0, 0), (100, 100), (200, 0), (300, 100), (400, 0)));
        Assert.InRange(result.Confidence, 0, 1);
        Assert.False(BeatInsight.Diagnostics.GameplayDebug.OneTwoJumpEnabled);
        Assert.False(BeatInsight.Diagnostics.GameplayDebug.OneTwoJumpDetailedEnabled);
        BeatInsight.Diagnostics.GameplayDebug.OneTwoJump(result);
    }

    [Fact]
    public void SotarksBlackRoverExtreme_IsAHighCoveragePositiveControl()
    {
        var map = FixtureLoader.Load("Vickeblanka - Black Rover (TV Size) (Sotarks) [Extreme].osu");
        var result = JumpPatternAnalyzer.AnalyzeOneTwo(map.HitObjects, map.GameplayProfile);

        Assert.True(result.Coverage >= .05);
        Assert.True(result.SequenceCount >= 3);
        Assert.True(result.LongestSequence >= 5);
    }

    [Fact]
    public void SotarksBlackRoverExtra_RepeatedBlocksAreDetectedOnExactFixture()
    {
        const string fixture = "Vickeblanka - Black Rover (TV Size) (Sotarks) [Extra].osu";
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps", fixture);
        byte[] fixtureBytes = File.ReadAllBytes(path);
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fixtureBytes));
        Assert.Equal("B031D137FD36442C3E9919203F88F8380106F158C076A86F33A8894729D15949", hash);
        Assert.Contains("BeatmapID:1642274", System.Text.Encoding.UTF8.GetString(fixtureBytes));

        var map = FixtureLoader.Load(fixture);
        var result = JumpPatternAnalyzer.AnalyzeOneTwo(map.HitObjects, map.GameplayProfile);

        Assert.Equal("Black Rover (TV Size)", map.Title);
        Assert.Equal("Sotarks", map.Creator);
        Assert.Equal("Extra", map.Version);
        Assert.True(result.SequenceCount > 0);
        Assert.True(result.Coverage > 0);
        Assert.True(result.Confidence > 0);
        Assert.Equal(result.SequenceCount, result.AcceptedRepeatedBlockGroupCount);
        Assert.True(result.AcceptedBlockCount >= result.SequenceCount * 2);
        Assert.NotEmpty(result.ResetObjectIndices);
    }

    [Fact]
    public void CanYouUnderstandMe_RemainsALowCoverageNegativeControl()
    {
        var map = FixtureLoader.Load("(can you) understand me [uhh].osu");
        var result = JumpPatternAnalyzer.AnalyzeOneTwo(map.HitObjects, map.GameplayProfile);

        Assert.InRange(result.Coverage, 0, .02);
        Assert.DoesNotContain(JumpPatternAnalyzer.Analyze(map.HitObjects, map.GameplayProfile),
            match => match.Type == JumpPatternType.OneTwo && match.ObjectCount > 5);
    }
}
