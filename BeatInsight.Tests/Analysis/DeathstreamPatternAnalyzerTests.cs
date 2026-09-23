using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Analysis.StreamPatterns;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.StreamPatterns;

namespace BeatInsight.Tests.Analysis;

public sealed class DeathstreamPatternAnalyzerTests
{
    [Fact]
    public void ThirtyTwoObjects_AreLongStreamOnly()
    {
        Assert.Single(AnalyzeLongStream(32).Sequences);
        Assert.Empty(AnalyzeDeathstream(32).Sequences);
        Assert.Empty(AnalyzeShortStream(32).Sequences);
        Assert.Empty(AnalyzeBurst(32).Sequences);
    }

    [Theory]
    [InlineData(33)]
    [InlineData(34)]
    [InlineData(50)]
    [InlineData(64)]
    [InlineData(100)]
    public void RunsAtOrAboveMinimum_AreSingleUnslicedDeathstreams(int objectCount)
    {
        DeathstreamPatternAnalysis result = AnalyzeDeathstream(objectCount);
        DeathstreamPatternSequence sequence = Assert.Single(result.Sequences);

        Assert.Single(result.Runs);
        Assert.Equal(objectCount, sequence.ObjectCount);
        Assert.Equal(objectCount - 1, sequence.LinkCount);
        Assert.Equal((0, objectCount - 1), (sequence.StartIndex, sequence.EndIndex));
        Assert.Equal((0, (objectCount - 1) * 80), (sequence.StartTime, sequence.EndTime));
        Assert.Equal((double)(objectCount - 1) * 80, sequence.Duration);
    }

    [Fact]
    public void DefaultLengthPartitionIsExclusiveAndGapless()
    {
        var longOptions = new LongStreamPatternOptions();
        var deathstreamOptions = new DeathstreamPatternOptions();

        Assert.Equal(longOptions.MaximumLongStreamObjects + 1,
            deathstreamOptions.MinimumDeathstreamObjects);

        foreach ((int count, int expectedCategory) in new[]
        {
            (3, 0), (8, 0),
            (9, 1), (16, 1),
            (17, 2), (31, 2), (32, 2),
            (33, 3), (34, 3)
        })
        {
            int[] matches =
            {
                AnalyzeBurst(count).BurstSequenceCount,
                AnalyzeShortStream(count).ShortStreamSequenceCount,
                AnalyzeLongStream(count).LongStreamSequenceCount,
                AnalyzeDeathstream(count).DeathstreamSequenceCount
            };

            Assert.Equal(1, matches.Sum());
            Assert.Equal(1, matches[expectedCategory]);
        }
    }

    [Fact]
    public void TemporalBreak_SeparatesTwoDeathstreams()
    {
        int[] intervals = Repeat(80, 39).Concat(new[] { 210 }).Concat(Repeat(80, 39)).ToArray();

        DeathstreamPatternAnalysis result = AnalyzeIntervals(intervals);

        Assert.Equal(new[] { (0, 39), (40, 79) },
            result.Sequences.Select(sequence => (sequence.StartIndex, sequence.EndIndex)));
    }

    [Fact]
    public void TwoSeparatedLongStreams_DoNotBecomeDeathstream()
    {
        int[] intervals = Repeat(80, 24).Concat(new[] { 210 }).Concat(Repeat(80, 24)).ToArray();
        Beatmap map = Map(intervals);

        Assert.Equal(2, StreamPatternAnalyzer.AnalyzeLongStream(map.HitObjects).LongStreamSequenceCount);
        Assert.Empty(StreamPatternAnalyzer.AnalyzeDeathstream(map.HitObjects).Sequences);
    }

    [Fact]
    public void ModerateIntervalVariation_IsToleratedAndMeasured()
    {
        int[] intervals = Enumerable.Range(0, 32)
            .Select(index => new[] { 80, 82, 78, 81, 79, 85 }[index % 6])
            .ToArray();

        DeathstreamPatternSequence sequence = Assert.Single(AnalyzeIntervals(intervals).Sequences);

        Assert.Equal(intervals.Average(), sequence.AverageInterval, 10);
        Assert.Equal(78, sequence.MinimumInterval);
        Assert.Equal(85, sequence.MaximumInterval);
        Assert.Equal(78.0 / 85.0, sequence.IntervalConsistency, 10);
    }

    [Fact]
    public void OutOfRangeIntervalBreaksTheRun()
    {
        int[] intervals = Repeat(80, 20).Concat(new[] { 210 }).Concat(Repeat(80, 20)).ToArray();

        DeathstreamPatternAnalysis result = AnalyzeIntervals(intervals);

        Assert.Empty(result.Sequences);
        Assert.Equal(new[] { 21, 21 }, result.Runs.Select(run => run.ObjectCount));
    }

    [Fact]
    public void SlowObjects_AreRejected()
    {
        DeathstreamPatternAnalysis result = AnalyzeIntervals(Repeat(180, 40));

        Assert.Empty(result.Sequences);
        Assert.Empty(result.Runs);
    }

    [Fact]
    public void CoverageAndAggregateMetricsUseQualifiedRunsOnly()
    {
        int[] intervals = Repeat(80, 39).Concat(new[] { 210 }).Concat(Repeat(80, 49)).ToArray();

        DeathstreamPatternAnalysis result = AnalyzeIntervals(intervals);

        Assert.Equal(2, result.DeathstreamSequenceCount);
        Assert.Equal(90, result.DeathstreamObjectCount);
        Assert.Equal(1, result.Coverage, 10);
        Assert.Equal(50, result.LongestDeathstream);
        Assert.Equal(45, result.AverageDeathstreamLength, 10);
        Assert.Equal(80, result.AverageInterval, 10);
        Assert.Equal(3520, result.AverageDuration, 10);
        Assert.Equal(1, result.AverageIntervalConsistency, 10);
    }

    [Fact]
    public void ConfidenceIsBoundedAndLengthSignalSaturates()
    {
        foreach (DeathstreamPatternAnalysis result in new[]
        {
            AnalyzeDeathstream(1),
            AnalyzeDeathstream(32),
            AnalyzeDeathstream(33),
            AnalyzeDeathstream(64),
            AnalyzeDeathstream(100)
        })
            Assert.InRange(result.Confidence, 0, 1);

        Assert.Equal(AnalyzeDeathstream(64).Confidence, AnalyzeDeathstream(100).Confidence, 10);
    }

    [Fact]
    public void CircleOnlyConventionIsPreserved()
    {
        Beatmap map = Map(Repeat(80, 49));
        map.HitObjects[25].Type = 2;

        Assert.Empty(StreamPatternAnalyzer.AnalyzeDeathstream(map.HitObjects).Sequences);
    }

    [Fact]
    public void GeometryDoesNotAffectTemporalRecognition()
    {
        Beatmap map = Map(Repeat(80, 49));
        for (int index = 0; index < map.HitObjects.Count; index++)
            map.HitObjects[index].X = index * 300;

        Assert.Single(StreamPatternAnalyzer.AnalyzeDeathstream(map.HitObjects).Sequences);
    }

    [Fact]
    public void UsesExistingInclusiveStreamTemporalQualification()
    {
        int[] valid = Enumerable.Range(0, 32)
            .Select(index => index % 2 == 0 ? 30 : 130)
            .ToArray();

        Assert.Single(AnalyzeIntervals(valid).Sequences);
        Assert.Empty(AnalyzeIntervals(Repeat(29, 33)).Sequences);
        Assert.Empty(AnalyzeIntervals(Repeat(131, 33)).Sequences);
    }

    [Fact]
    public void ResultHasNoStaminaRecoveryOrFatigueMetrics()
    {
        string[] propertyNames = typeof(DeathstreamPatternAnalysis)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(propertyNames, name => name.Contains("Stamina", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Recovery", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Fatigue", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AnalysisDoesNotChangeGameplayIdentityRatingOrJumpPatterns()
    {
        Beatmap map = Map(Repeat(80, 49));
        GameplayProfile profile = GameplayAnalyzer.Analyze(map);
        string identity = System.Text.Json.JsonSerializer.Serialize(profile.Identity);
        double rating = map.BeatInsightRating;
        var jumpBefore = JumpPatternAnalyzer.Analyze(map.HitObjects, profile);

        DeathstreamPatternAnalysis first = StreamPatternAnalyzer.AnalyzeDeathstream(map.HitObjects);
        DeathstreamPatternAnalysis second = StreamPatternAnalyzer.AnalyzeDeathstream(map.HitObjects);

        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(first),
            System.Text.Json.JsonSerializer.Serialize(second));
        Assert.Equal(identity, System.Text.Json.JsonSerializer.Serialize(profile.Identity));
        Assert.Equal(rating, map.BeatInsightRating);
        Assert.Equal(jumpBefore, JumpPatternAnalyzer.Analyze(map.HitObjects, profile));
        Assert.Same(profile, map.GameplayProfile);
    }

    [Fact]
    public void DefaultsHaveNoMaximumAndDebugIsDisabled()
    {
        var options = new DeathstreamPatternOptions();
        Assert.Equal(33, options.MinimumDeathstreamObjects);
        Assert.DoesNotContain(typeof(DeathstreamPatternOptions).GetProperties(),
            property => property.Name.Contains("Maximum", StringComparison.OrdinalIgnoreCase));
        Assert.False(GameplayDebug.DeathstreamPatternEnabled);
        Assert.False(GameplayDebug.DeathstreamPatternDetailedEnabled);
        GameplayDebug.DeathstreamPattern(AnalyzeDeathstream(33));
    }

    private static DeathstreamPatternAnalysis AnalyzeDeathstream(int objectCount) =>
        AnalyzeIntervals(Repeat(80, Math.Max(0, objectCount - 1)));

    private static DeathstreamPatternAnalysis AnalyzeIntervals(params int[] intervals) =>
        StreamPatternAnalyzer.AnalyzeDeathstream(Map(intervals).HitObjects);

    private static LongStreamPatternAnalysis AnalyzeLongStream(int objectCount) =>
        StreamPatternAnalyzer.AnalyzeLongStream(Map(Repeat(80, objectCount - 1)).HitObjects);

    private static ShortStreamPatternAnalysis AnalyzeShortStream(int objectCount) =>
        StreamPatternAnalyzer.AnalyzeShortStream(Map(Repeat(80, objectCount - 1)).HitObjects);

    private static BurstPatternAnalysis AnalyzeBurst(int objectCount) =>
        StreamPatternAnalyzer.AnalyzeBurst(Map(Repeat(80, objectCount - 1)).HitObjects);

    private static int[] Repeat(int interval, int count) => Enumerable.Repeat(interval, count).ToArray();

    private static Beatmap Map(params int[] intervals)
    {
        var map = new Beatmap();
        int time = 0;
        map.HitObjects.Add(new HitObject { Type = 1, Time = time, X = 0, Y = 0 });
        for (int index = 0; index < intervals.Length; index++)
        {
            time += intervals[index];
            map.HitObjects.Add(new HitObject { Type = 1, Time = time, X = (index + 1) * 10, Y = 0 });
        }
        return map;
    }
}
