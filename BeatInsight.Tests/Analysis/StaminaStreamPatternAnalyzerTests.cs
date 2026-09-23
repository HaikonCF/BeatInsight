using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Analysis.StreamPatterns;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.StreamPatterns;

namespace BeatInsight.Tests.Analysis;

public sealed class StaminaStreamPatternAnalyzerTests
{
    [Fact]
    public void IsolatedLongStream_IsNotStamina()
    {
        Assert.Single(AnalyzeLongStream(MapRuns(20)).Sequences);
        StaminaStreamPatternAnalysis stamina = AnalyzeStamina(MapRuns(20));

        Assert.Empty(stamina.Sequences);
        Assert.Equal("TooFewRuns", Assert.Single(stamina.Candidates).Reason);
    }

    [Fact]
    public void IsolatedHundredObjectDeathstream_IsNotStamina()
    {
        Beatmap map = MapRuns(100);

        Assert.Single(StreamPatternAnalyzer.AnalyzeDeathstream(map.HitObjects).Sequences);
        Assert.Empty(AnalyzeStamina(map).Sequences);
    }

    [Fact]
    public void TwoShortStreamsWithInsufficientObjects_AreRejected()
    {
        StaminaStreamPatternAnalysis result = AnalyzeStamina(MapRuns(new[] { 9, 9 }, new[] { 500 }));

        StaminaStreamPatternSequence candidate = Assert.Single(result.Candidates);
        Assert.False(candidate.Qualified);
        Assert.Equal("TooFewObjects", candidate.Reason);
        Assert.Equal(2, candidate.RunCount);
        Assert.Equal(18, candidate.TotalRapidObjects);
    }

    [Fact]
    public void MultipleNearbyRunsWithEnoughObjects_FormStamina()
    {
        StaminaStreamPatternSequence sequence = Assert.Single(
            AnalyzeStamina(MapRuns(new[] { 12, 12 }, new[] { 500 })).Sequences);

        Assert.Equal(2, sequence.RunCount);
        Assert.Equal(24, sequence.TotalRapidObjects);
        Assert.Equal("Qualified", sequence.Reason);
    }

    [Fact]
    public void LongAndShortStreamsCanFormStamina()
    {
        Beatmap map = MapRuns(new[] { 20, 9 }, new[] { 500 });

        Assert.Single(StreamPatternAnalyzer.AnalyzeLongStream(map.HitObjects).Sequences);
        Assert.Single(StreamPatternAnalyzer.AnalyzeShortStream(map.HitObjects).Sequences);
        Assert.Single(AnalyzeStamina(map).Sequences);
    }

    [Fact]
    public void DeathstreamAndLongStreamCanFormStamina()
    {
        Beatmap map = MapRuns(new[] { 40, 20 }, new[] { 500 });

        Assert.Single(StreamPatternAnalyzer.AnalyzeDeathstream(map.HitObjects).Sequences);
        Assert.Single(StreamPatternAnalyzer.AnalyzeLongStream(map.HitObjects).Sequences);
        Assert.Single(AnalyzeStamina(map).Sequences);
    }

    [Fact]
    public void RecoveryGapBoundaryIsInclusiveAndJustAboveBreaksTheGroup()
    {
        Assert.Single(AnalyzeStamina(MapRuns(new[] { 12, 12 }, new[] { 1500 })).Sequences);

        StaminaStreamPatternAnalysis separated =
            AnalyzeStamina(MapRuns(new[] { 12, 12 }, new[] { 1501 }));
        Assert.Empty(separated.Sequences);
        Assert.Equal(2, separated.Candidates.Count);
        Assert.All(separated.Candidates, candidate => Assert.Equal("TooFewRuns", candidate.Reason));
    }

    [Fact]
    public void ThreeNearbyRunsFormOneStaminaSequenceWithExactMetrics()
    {
        StaminaStreamPatternAnalysis result =
            AnalyzeStamina(MapRuns(new[] { 20, 20, 20 }, new[] { 500, 500 }));

        StaminaStreamPatternSequence sequence = Assert.Single(result.Sequences);
        Assert.Equal(3, sequence.RunCount);
        Assert.Equal(60, sequence.TotalRapidObjects);
        Assert.Equal(new double[] { 500, 500 }, sequence.RecoveryGaps);
        Assert.Equal(4560, sequence.ActiveRapidDuration, 10);
        Assert.Equal(1000, sequence.TotalRecoveryDuration, 10);
        Assert.Equal(500, sequence.AverageRecoveryGap, 10);
        Assert.Equal(500, sequence.MaximumRecoveryGapObserved, 10);
        Assert.Equal(5560, sequence.Duration, 10);
        Assert.Equal(4560.0 / 5560.0, sequence.ActiveRatio, 10);
        Assert.Equal(80, sequence.AverageInterval, 10);
        Assert.Equal(1, sequence.AverageIntervalConsistency, 10);
    }

    [Fact]
    public void LargePauseCreatesTwoQualifiedStaminaSequences()
    {
        Beatmap map = MapRuns(
            new[] { 12, 12, 15, 15 },
            new[] { 500, 2000, 500 });

        StaminaStreamPatternAnalysis result = AnalyzeStamina(map);

        Assert.Equal(2, result.StaminaSequenceCount);
        Assert.Equal(new[] { 24, 30 }, result.Sequences.Select(sequence => sequence.TotalRapidObjects));
    }

    [Fact]
    public void BurstsAreIgnoredEvenWhenRepeatedNearby()
    {
        Beatmap map = MapRuns(new[] { 8, 8, 8, 8 }, new[] { 500, 500, 500 });

        StaminaStreamPatternAnalysis result = AnalyzeStamina(map);

        Assert.Equal(4, StreamPatternAnalyzer.AnalyzeBurst(map.HitObjects).BurstSequenceCount);
        Assert.Equal(0, result.EligibleRapidRunCount);
        Assert.Empty(result.Candidates);
        Assert.Empty(result.Sequences);
    }

    [Fact]
    public void CoverageCountsOnlyUniqueRapidObjectsAndExcludesRecoveryObject()
    {
        Beatmap map = MapWithRecoveryObject();

        StaminaStreamPatternAnalysis result = AnalyzeStamina(map);

        Assert.Equal(25, result.TotalObjects);
        Assert.Equal(24, result.StaminaObjectCount);
        Assert.Equal(24.0 / 25.0, result.Coverage, 10);
        StaminaStreamPatternSequence sequence = Assert.Single(result.Sequences);
        Assert.Equal(500, sequence.TotalRecoveryDuration, 10);
        Assert.DoesNotContain(12, sequence.Runs.SelectMany(
            run => Enumerable.Range(run.StartIndex, run.ObjectCount)));
    }

    [Fact]
    public void AggregateMetricsUseQualifiedSequencesOnly()
    {
        Beatmap map = MapRuns(
            new[] { 12, 12, 15, 15 },
            new[] { 500, 2000, 500 });

        StaminaStreamPatternAnalysis result = AnalyzeStamina(map);

        Assert.Equal(4, result.EligibleRapidRunCount);
        Assert.Equal(54, result.StaminaObjectCount);
        Assert.Equal(30, result.LongestStaminaObjectCount);
        Assert.Equal(2740, result.LongestStaminaDuration, 10);
        Assert.Equal(2, result.AverageRunsPerSequence, 10);
        Assert.Equal(27, result.AverageObjectsPerSequence, 10);
        Assert.Equal(2500, result.AverageSequenceDuration, 10);
        Assert.Equal(500, result.AverageRecoveryGap, 10);
        Assert.InRange(result.AverageActiveRatio, 0, 1);
        Assert.Equal(80, result.AverageInterval, 10);
        Assert.Equal(1, result.AverageIntervalConsistency, 10);
    }

    [Fact]
    public void InternalIntervalVariationIsTolerated()
    {
        int[] variation = { 80, 82, 78, 81, 79, 85, 80, 83, 77, 84, 80 };
        Beatmap map = MapRunsWithIntervals(variation, variation, 500);

        StaminaStreamPatternSequence sequence = Assert.Single(AnalyzeStamina(map).Sequences);

        Assert.Equal(77.0 / 85.0, sequence.AverageIntervalConsistency, 10);
        Assert.Equal(variation.Average(), sequence.AverageInterval, 10);
    }

    [Fact]
    public void ConfidenceAndActiveRatiosAreAlwaysBounded()
    {
        foreach (StaminaStreamPatternAnalysis result in new[]
        {
            AnalyzeStamina(MapRuns(20)),
            AnalyzeStamina(MapRuns(new[] { 9, 9 }, new[] { 500 })),
            AnalyzeStamina(MapRuns(new[] { 12, 12 }, new[] { 1500 })),
            AnalyzeStamina(MapRuns(new[] { 40, 20 }, new[] { 500 }))
        })
        {
            Assert.InRange(result.Confidence, 0, 1);
            Assert.InRange(result.AverageActiveRatio, 0, 1);
            Assert.All(result.Candidates, candidate => Assert.InRange(candidate.ActiveRatio, 0, 1));
        }
    }

    [Fact]
    public void ExistingLengthPatternsRemainUnchanged()
    {
        Beatmap map = MapRuns(new[] { 8, 12, 20, 40 }, new[] { 500, 500, 500 });
        string before = SerializeLengthPatterns(map);

        _ = AnalyzeStamina(map);

        Assert.Equal(before, SerializeLengthPatterns(map));
    }

    [Fact]
    public void AnalysisDoesNotChangeGameplayIdentityRatingOrJumpPatterns()
    {
        Beatmap map = MapRuns(new[] { 20, 20, 20 }, new[] { 500, 500 });
        GameplayProfile profile = GameplayAnalyzer.Analyze(map);
        string identity = System.Text.Json.JsonSerializer.Serialize(profile.Identity);
        double rating = map.BeatInsightRating;
        var jumpBefore = JumpPatternAnalyzer.Analyze(map.HitObjects, profile);

        StaminaStreamPatternAnalysis first = AnalyzeStamina(map);
        StaminaStreamPatternAnalysis second = AnalyzeStamina(map);

        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(first),
            System.Text.Json.JsonSerializer.Serialize(second));
        Assert.Equal(identity, System.Text.Json.JsonSerializer.Serialize(profile.Identity));
        Assert.Equal(rating, map.BeatInsightRating);
        Assert.Equal(jumpBefore, JumpPatternAnalyzer.Analyze(map.HitObjects, profile));
        Assert.Same(profile, map.GameplayProfile);
    }

    [Fact]
    public void DefaultsUseNoMinimumDurationAndDebugIsDisabled()
    {
        var options = new StaminaStreamPatternOptions();
        Assert.Equal(9, options.MinimumStaminaRunObjects);
        Assert.Equal(2, options.MinimumStaminaRuns);
        Assert.Equal(24, options.MinimumStaminaObjects);
        Assert.Equal(1500, options.MaximumRecoveryGap);
        Assert.DoesNotContain(typeof(StaminaStreamPatternOptions).GetProperties(),
            property => property.Name.Contains("Duration", StringComparison.OrdinalIgnoreCase));
        Assert.False(GameplayDebug.StaminaStreamPatternEnabled);
        Assert.False(GameplayDebug.StaminaStreamPatternDetailedEnabled);
        GameplayDebug.StaminaStreamPattern(AnalyzeStamina(MapRuns(new[] { 12, 12 }, new[] { 500 })));
    }

    private static StaminaStreamPatternAnalysis AnalyzeStamina(Beatmap map) =>
        StreamPatternAnalyzer.AnalyzeStaminaStream(map.HitObjects);

    private static LongStreamPatternAnalysis AnalyzeLongStream(Beatmap map) =>
        StreamPatternAnalyzer.AnalyzeLongStream(map.HitObjects);

    private static string SerializeLengthPatterns(Beatmap map) =>
        System.Text.Json.JsonSerializer.Serialize(new object[]
        {
            StreamPatternAnalyzer.AnalyzeBurst(map.HitObjects),
            StreamPatternAnalyzer.AnalyzeShortStream(map.HitObjects),
            StreamPatternAnalyzer.AnalyzeLongStream(map.HitObjects),
            StreamPatternAnalyzer.AnalyzeDeathstream(map.HitObjects)
        });

    private static Beatmap MapRuns(params int[] runCounts) => MapRuns(runCounts, []);

    private static Beatmap MapRuns(int[] runCounts, int[] recoveryGaps)
    {
        var map = new Beatmap();
        int time = 0;
        int x = 0;
        for (int runIndex = 0; runIndex < runCounts.Length; runIndex++)
        {
            if (runIndex > 0)
                time += recoveryGaps[runIndex - 1];

            map.HitObjects.Add(new HitObject { Type = 1, Time = time, X = x, Y = 0 });
            x += 10;
            for (int objectIndex = 1; objectIndex < runCounts[runIndex]; objectIndex++)
            {
                time += 80;
                map.HitObjects.Add(new HitObject { Type = 1, Time = time, X = x, Y = 0 });
                x += 10;
            }
        }
        return map;
    }

    private static Beatmap MapWithRecoveryObject()
    {
        Beatmap map = MapRuns(12);
        int lastTime = map.HitObjects[^1].Time;
        map.HitObjects.Add(new HitObject { Type = 1, Time = lastTime + 250, X = 120, Y = 0 });
        int time = lastTime + 500;
        map.HitObjects.Add(new HitObject { Type = 1, Time = time, X = 130, Y = 0 });
        for (int index = 1; index < 12; index++)
        {
            time += 80;
            map.HitObjects.Add(new HitObject { Type = 1, Time = time, X = 130 + index * 10, Y = 0 });
        }
        return map;
    }

    private static Beatmap MapRunsWithIntervals(
        IReadOnlyList<int> firstIntervals,
        IReadOnlyList<int> secondIntervals,
        int recoveryGap)
    {
        var map = new Beatmap();
        int time = 0;
        int x = 0;
        map.HitObjects.Add(new HitObject { Type = 1, Time = time, X = x, Y = 0 });
        foreach (int interval in firstIntervals)
        {
            time += interval;
            x += 10;
            map.HitObjects.Add(new HitObject { Type = 1, Time = time, X = x, Y = 0 });
        }
        time += recoveryGap;
        x += 10;
        map.HitObjects.Add(new HitObject { Type = 1, Time = time, X = x, Y = 0 });
        foreach (int interval in secondIntervals)
        {
            time += interval;
            x += 10;
            map.HitObjects.Add(new HitObject { Type = 1, Time = time, X = x, Y = 0 });
        }
        return map;
    }
}
