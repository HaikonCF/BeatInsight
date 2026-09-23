using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Analysis.StreamPatterns;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.StreamPatterns;

namespace BeatInsight.Tests.Analysis;

public sealed class ShortStreamPatternAnalyzerTests
{
    [Fact]
    public void RunJustBelowMinimum_IsNotShortStreamAndRemainsBurst()
    {
        ShortStreamPatternAnalysis shortStream = Analyze(8);
        BurstPatternAnalysis burst = AnalyzeBurst(8);

        Assert.Empty(shortStream.Sequences);
        Assert.Equal(8, Assert.Single(shortStream.Runs).ObjectCount);
        Assert.Single(burst.Sequences);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(12)]
    [InlineData(16)]
    public void RunsWithinInclusiveBounds_AreAccepted(int objectCount)
    {
        ShortStreamPatternSequence sequence = Assert.Single(Analyze(objectCount).Sequences);

        Assert.Equal(objectCount, sequence.ObjectCount);
        Assert.Equal(objectCount - 1, sequence.LinkCount);
        Assert.Equal((0, objectCount - 1), (sequence.StartIndex, sequence.EndIndex));
        Assert.Equal((0, (objectCount - 1) * 80), (sequence.StartTime, sequence.EndTime));
        Assert.Equal((double)(objectCount - 1) * 80, sequence.Duration);
    }

    [Fact]
    public void RunAboveMaximum_IsPreservedAndRejectedWithoutArtificialSplitting()
    {
        ShortStreamPatternAnalysis shortStream = Analyze(17);
        BurstPatternAnalysis burst = AnalyzeBurst(17);

        Assert.Empty(shortStream.Sequences);
        Assert.Empty(burst.Sequences);
        ShortStreamPatternSequence run = Assert.Single(shortStream.Runs);
        Assert.Equal(17, run.ObjectCount);
        Assert.False(run.Qualified);
        Assert.Equal(17, Assert.Single(burst.Runs).ObjectCount);
    }

    [Fact]
    public void DefaultBurstAndShortStreamRangesAreExclusiveAndAdjacent()
    {
        var burstOptions = new BurstPatternOptions();
        var shortStreamOptions = new ShortStreamPatternOptions();

        Assert.Equal(burstOptions.MaximumBurstObjects + 1, shortStreamOptions.MinimumShortStreamObjects);
        Assert.Single(AnalyzeBurst(burstOptions.MaximumBurstObjects).Sequences);
        Assert.Empty(Analyze(burstOptions.MaximumBurstObjects).Sequences);
        Assert.Empty(AnalyzeBurst(shortStreamOptions.MinimumShortStreamObjects).Sequences);
        Assert.Single(Analyze(shortStreamOptions.MinimumShortStreamObjects).Sequences);
    }

    [Fact]
    public void ClearTemporalBreak_SeparatesTwoCompleteRuns()
    {
        int[] intervals = Repeat(80, 8).Concat(new[] { 210 }).Concat(Repeat(80, 8)).ToArray();

        ShortStreamPatternAnalysis result = AnalyzeIntervals(intervals);

        Assert.Equal(new[] { (0, 8), (9, 17) },
            result.Sequences.Select(sequence => (sequence.StartIndex, sequence.EndIndex)));
    }

    [Fact]
    public void SmallIntervalVariation_IsToleratedAndMeasured()
    {
        int[] intervals = new[] { 80, 82, 79, 81, 85, 80, 83, 78 };

        ShortStreamPatternSequence sequence = Assert.Single(AnalyzeIntervals(intervals).Sequences);

        Assert.Equal(78.0 / 85.0, sequence.IntervalConsistency, 10);
        Assert.Equal(intervals.Average(), sequence.AverageInterval, 10);
        Assert.Equal(78, sequence.MinimumInterval);
        Assert.Equal(85, sequence.MaximumInterval);
    }

    [Fact]
    public void SlowObjects_AreRejected()
    {
        Assert.Empty(AnalyzeIntervals(Repeat(180, 12)).Sequences);
        Assert.Empty(AnalyzeIntervals(Repeat(180, 12)).Runs);
    }

    [Fact]
    public void CoverageAndAggregateMetricsUseOnlyQualifiedRuns()
    {
        int[] intervals = Repeat(80, 8).Concat(new[] { 210 }).Concat(Repeat(80, 11)).ToArray();

        ShortStreamPatternAnalysis result = AnalyzeIntervals(intervals);

        Assert.Equal(2, result.ShortStreamSequenceCount);
        Assert.Equal(21, result.ShortStreamObjectCount);
        Assert.Equal(21.0 / 21.0, result.Coverage, 10);
        Assert.Equal(12, result.LongestShortStream);
        Assert.Equal(10.5, result.AverageShortStreamLength, 10);
        Assert.Equal(80, result.AverageInterval, 10);
        Assert.Equal(760, result.AverageDuration, 10);
        Assert.Equal(1, result.AverageIntervalConsistency, 10);
    }

    [Fact]
    public void ConfidenceIsAlwaysBounded()
    {
        foreach (ShortStreamPatternAnalysis result in new[]
        {
            Analyze(1),
            Analyze(8),
            Analyze(9),
            Analyze(16),
            Analyze(17)
        })
            Assert.InRange(result.Confidence, 0, 1);
    }

    [Fact]
    public void CircleOnlyConventionIsPreserved()
    {
        Beatmap map = Map(Repeat(80, 11));
        map.HitObjects[5].Type = 2;

        Assert.Empty(StreamPatternAnalyzer.AnalyzeShortStream(map.HitObjects).Sequences);
    }

    [Fact]
    public void GeometryDoesNotAffectTemporalRecognition()
    {
        Beatmap map = Map(Repeat(80, 11));
        for (int index = 0; index < map.HitObjects.Count; index++)
            map.HitObjects[index].X = index * 300;

        Assert.Single(StreamPatternAnalyzer.AnalyzeShortStream(map.HitObjects).Sequences);
    }

    [Fact]
    public void UsesExistingStreamTemporalQualification()
    {
        int[] valid = new[] { 30 }.Concat(Repeat(130, 7)).ToArray();
        Assert.Single(AnalyzeIntervals(valid).Sequences);

        Assert.Empty(AnalyzeIntervals(Repeat(29, 9)).Sequences);
        Assert.Empty(AnalyzeIntervals(Repeat(131, 9)).Sequences);
    }

    [Fact]
    public void AnalysisDoesNotChangeGameplayIdentityRatingOrJumpPatterns()
    {
        Beatmap map = Map(Repeat(80, 11));
        GameplayProfile profile = GameplayAnalyzer.Analyze(map);
        string identity = System.Text.Json.JsonSerializer.Serialize(profile.Identity);
        double rating = map.BeatInsightRating;
        var jumpBefore = JumpPatternAnalyzer.Analyze(map.HitObjects, profile);

        ShortStreamPatternAnalysis first = StreamPatternAnalyzer.AnalyzeShortStream(map.HitObjects);
        ShortStreamPatternAnalysis second = StreamPatternAnalyzer.AnalyzeShortStream(map.HitObjects);

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
        var options = new ShortStreamPatternOptions();
        Assert.Equal(9, options.MinimumShortStreamObjects);
        Assert.Equal(16, options.MaximumShortStreamObjects);
        Assert.False(GameplayDebug.ShortStreamPatternEnabled);
        Assert.False(GameplayDebug.ShortStreamPatternDetailedEnabled);
        GameplayDebug.ShortStreamPattern(Analyze(9));
    }

    private static ShortStreamPatternAnalysis Analyze(int objectCount) =>
        AnalyzeIntervals(Repeat(80, Math.Max(0, objectCount - 1)));

    private static ShortStreamPatternAnalysis AnalyzeIntervals(params int[] intervals) =>
        StreamPatternAnalyzer.AnalyzeShortStream(Map(intervals).HitObjects);

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
