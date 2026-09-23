using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Analysis.StreamPatterns;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.StreamPatterns;

namespace BeatInsight.Tests.Analysis;

public sealed class LongStreamPatternAnalyzerTests
{
    [Fact]
    public void SixteenObjects_AreShortStreamOnly()
    {
        Assert.Single(AnalyzeShortStream(16).Sequences);
        Assert.Empty(AnalyzeLongStream(16).Sequences);
        Assert.Empty(AnalyzeBurst(16).Sequences);
    }

    [Theory]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(24)]
    [InlineData(31)]
    [InlineData(32)]
    public void RunsWithinInclusiveBounds_AreLongStreams(int objectCount)
    {
        LongStreamPatternSequence sequence = Assert.Single(AnalyzeLongStream(objectCount).Sequences);

        Assert.Equal(objectCount, sequence.ObjectCount);
        Assert.Equal(objectCount - 1, sequence.LinkCount);
        Assert.Equal((0, objectCount - 1), (sequence.StartIndex, sequence.EndIndex));
        Assert.Equal((0, (objectCount - 1) * 80), (sequence.StartTime, sequence.EndTime));
        Assert.Equal((double)(objectCount - 1) * 80, sequence.Duration);
    }

    [Theory]
    [InlineData(33)]
    [InlineData(40)]
    public void RunAboveMaximum_IsPreservedAndNotArtificiallySplit(int objectCount)
    {
        LongStreamPatternAnalysis longStream = AnalyzeLongStream(objectCount);

        Assert.Empty(longStream.Sequences);
        LongStreamPatternSequence run = Assert.Single(longStream.Runs);
        Assert.Equal(objectCount, run.ObjectCount);
        Assert.Equal(objectCount - 1, run.LinkCount);
        Assert.False(run.Qualified);
        Assert.Empty(AnalyzeBurst(objectCount).Sequences);
        Assert.Empty(AnalyzeShortStream(objectCount).Sequences);
    }

    [Fact]
    public void DefaultRangesAreExclusiveAndAdjacentWithoutGaps()
    {
        var burstOptions = new BurstPatternOptions();
        var shortOptions = new ShortStreamPatternOptions();
        var longOptions = new LongStreamPatternOptions();

        Assert.Equal(burstOptions.MaximumBurstObjects + 1, shortOptions.MinimumShortStreamObjects);
        Assert.Equal(shortOptions.MaximumShortStreamObjects + 1, longOptions.MinimumLongStreamObjects);

        Assert.Single(AnalyzeBurst(8).Sequences);
        Assert.Empty(AnalyzeShortStream(8).Sequences);
        Assert.Empty(AnalyzeLongStream(8).Sequences);

        Assert.Empty(AnalyzeBurst(9).Sequences);
        Assert.Single(AnalyzeShortStream(9).Sequences);
        Assert.Empty(AnalyzeLongStream(9).Sequences);

        Assert.Empty(AnalyzeBurst(17).Sequences);
        Assert.Empty(AnalyzeShortStream(17).Sequences);
        Assert.Single(AnalyzeLongStream(17).Sequences);
    }

    [Fact]
    public void TemporalBreak_SeparatesTwoCompleteRuns()
    {
        int[] intervals = Repeat(80, 16).Concat(new[] { 210 }).Concat(Repeat(80, 16)).ToArray();

        LongStreamPatternAnalysis result = AnalyzeIntervals(intervals);

        Assert.Equal(new[] { (0, 16), (17, 33) },
            result.Sequences.Select(sequence => (sequence.StartIndex, sequence.EndIndex)));
    }

    [Fact]
    public void SmallIntervalVariation_IsToleratedAndMeasured()
    {
        int[] intervals = Enumerable.Range(0, 16)
            .Select(index => new[] { 80, 82, 79, 81, 85, 78 }[index % 6])
            .ToArray();

        LongStreamPatternSequence sequence = Assert.Single(AnalyzeIntervals(intervals).Sequences);

        Assert.Equal(78.0 / 85.0, sequence.IntervalConsistency, 10);
        Assert.Equal(intervals.Average(), sequence.AverageInterval, 10);
        Assert.Equal(78, sequence.MinimumInterval);
        Assert.Equal(85, sequence.MaximumInterval);
    }

    [Fact]
    public void SlowObjects_AreRejected()
    {
        LongStreamPatternAnalysis result = AnalyzeIntervals(Repeat(180, 32));

        Assert.Empty(result.Sequences);
        Assert.Empty(result.Runs);
    }

    [Fact]
    public void CoverageAndAggregateMetricsUseQualifiedRunsOnly()
    {
        int[] intervals = Repeat(80, 16).Concat(new[] { 210 }).Concat(Repeat(80, 19)).ToArray();

        LongStreamPatternAnalysis result = AnalyzeIntervals(intervals);

        Assert.Equal(2, result.LongStreamSequenceCount);
        Assert.Equal(37, result.LongStreamObjectCount);
        Assert.Equal(1, result.Coverage, 10);
        Assert.Equal(20, result.LongestLongStream);
        Assert.Equal(18.5, result.AverageLongStreamLength, 10);
        Assert.Equal(80, result.AverageInterval, 10);
        Assert.Equal(1400, result.AverageDuration, 10);
        Assert.Equal(1, result.AverageIntervalConsistency, 10);
    }

    [Fact]
    public void ConfidenceIsAlwaysBounded()
    {
        foreach (LongStreamPatternAnalysis result in new[]
        {
            AnalyzeLongStream(1),
            AnalyzeLongStream(16),
            AnalyzeLongStream(17),
            AnalyzeLongStream(32),
            AnalyzeLongStream(33)
        })
            Assert.InRange(result.Confidence, 0, 1);
    }

    [Fact]
    public void CircleOnlyConventionIsPreserved()
    {
        Beatmap map = Map(Repeat(80, 23));
        map.HitObjects[12].Type = 2;

        Assert.Empty(StreamPatternAnalyzer.AnalyzeLongStream(map.HitObjects).Sequences);
    }

    [Fact]
    public void GeometryDoesNotAffectTemporalRecognition()
    {
        Beatmap map = Map(Repeat(80, 23));
        for (int index = 0; index < map.HitObjects.Count; index++)
            map.HitObjects[index].X = index * 300;

        Assert.Single(StreamPatternAnalyzer.AnalyzeLongStream(map.HitObjects).Sequences);
    }

    [Fact]
    public void UsesExistingInclusiveStreamTemporalQualification()
    {
        int[] valid = Enumerable.Range(0, 16)
            .Select(index => index % 2 == 0 ? 30 : 130)
            .ToArray();

        Assert.Single(AnalyzeIntervals(valid).Sequences);
        Assert.Empty(AnalyzeIntervals(Repeat(29, 17)).Sequences);
        Assert.Empty(AnalyzeIntervals(Repeat(131, 17)).Sequences);
    }

    [Fact]
    public void AnalysisDoesNotChangeGameplayIdentityRatingOrJumpPatterns()
    {
        Beatmap map = Map(Repeat(80, 23));
        GameplayProfile profile = GameplayAnalyzer.Analyze(map);
        string identity = System.Text.Json.JsonSerializer.Serialize(profile.Identity);
        double rating = map.BeatInsightRating;
        var jumpBefore = JumpPatternAnalyzer.Analyze(map.HitObjects, profile);

        LongStreamPatternAnalysis first = StreamPatternAnalyzer.AnalyzeLongStream(map.HitObjects);
        LongStreamPatternAnalysis second = StreamPatternAnalyzer.AnalyzeLongStream(map.HitObjects);

        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(first),
            System.Text.Json.JsonSerializer.Serialize(second));
        Assert.Equal(identity, System.Text.Json.JsonSerializer.Serialize(profile.Identity));
        Assert.Equal(rating, map.BeatInsightRating);
        Assert.Equal(jumpBefore, JumpPatternAnalyzer.Analyze(map.HitObjects, profile));
        Assert.Same(profile, map.GameplayProfile);
    }

    [Fact]
    public void DefaultsAndDebugAreDisabled()
    {
        var options = new LongStreamPatternOptions();
        Assert.Equal(17, options.MinimumLongStreamObjects);
        Assert.Equal(32, options.MaximumLongStreamObjects);
        Assert.False(GameplayDebug.LongStreamPatternEnabled);
        Assert.False(GameplayDebug.LongStreamPatternDetailedEnabled);
        GameplayDebug.LongStreamPattern(AnalyzeLongStream(17));
    }

    private static LongStreamPatternAnalysis AnalyzeLongStream(int objectCount) =>
        AnalyzeIntervals(Repeat(80, Math.Max(0, objectCount - 1)));

    private static LongStreamPatternAnalysis AnalyzeIntervals(params int[] intervals) =>
        StreamPatternAnalyzer.AnalyzeLongStream(Map(intervals).HitObjects);

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
