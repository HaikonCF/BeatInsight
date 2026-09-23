using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Analysis.StreamPatterns;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.StreamPatterns;

namespace BeatInsight.Tests.Analysis;

public sealed class BurstPatternAnalyzerTests
{
    [Fact]
    public void ShortRapidSequence_IsBurstWithExpectedOccurrenceMetrics()
    {
        BurstPatternAnalysis result = Analyze(80, 80, 82, 79);

        BurstPatternSequence sequence = Assert.Single(result.Sequences);
        Assert.Equal((0, 4), (sequence.StartIndex, sequence.EndIndex));
        Assert.Equal((0, 321), (sequence.StartTime, sequence.EndTime));
        Assert.Equal(5, sequence.ObjectCount);
        Assert.Equal(4, sequence.LinkCount);
        Assert.Equal(80.25, sequence.AverageInterval, 10);
        Assert.Equal(79, sequence.MinimumInterval);
        Assert.Equal(82, sequence.MaximumInterval);
        Assert.Equal(79.0 / 82.0, sequence.IntervalConsistency, 10);
        Assert.Equal(321, sequence.Duration);
    }

    [Fact]
    public void TwoObjects_AreRejectedAsTooShort()
    {
        BurstPatternAnalysis result = Analyze(80);

        Assert.Empty(result.Sequences);
        Assert.False(Assert.Single(result.Runs).Qualified);
    }

    [Fact]
    public void LongRapidRun_IsPreservedAndNotArtificiallySplit()
    {
        BurstPatternAnalysis result = Analyze(Enumerable.Repeat(80, 12).ToArray());

        Assert.Empty(result.Sequences);
        BurstPatternSequence run = Assert.Single(result.Runs);
        Assert.Equal(13, run.ObjectCount);
        Assert.False(run.Qualified);
    }

    [Fact]
    public void ClearTemporalBreak_SeparatesTwoRuns()
    {
        BurstPatternAnalysis result = Analyze(80, 80, 200, 80, 80);

        Assert.Equal(new[] { (0, 2), (3, 5) },
            result.Sequences.Select(sequence => (sequence.StartIndex, sequence.EndIndex)));
    }

    [Fact]
    public void SmallIntervalVariation_IsToleratedAndMeasured()
    {
        BurstPatternSequence sequence = Assert.Single(Analyze(80, 85, 78, 83).Sequences);

        Assert.Equal(78.0 / 85.0, sequence.IntervalConsistency, 10);
    }

    [Fact]
    public void ExcessiveVariationBreaksTheSequence()
    {
        BurstPatternAnalysis result = Analyze(80, 80, 200, 80);

        BurstPatternSequence sequence = Assert.Single(result.Sequences);
        Assert.Equal((0, 2), (sequence.StartIndex, sequence.EndIndex));
        Assert.Contains(result.Runs, run => !run.Qualified && run.StartIndex == 3 && run.EndIndex == 4);
    }

    [Fact]
    public void SlowObjects_AreRejected()
    {
        BurstPatternAnalysis result = Analyze(200, 210, 180);

        Assert.Empty(result.Sequences);
        Assert.Empty(result.Runs);
    }

    [Fact]
    public void CoverageCountsUniqueObjectsAcrossSeparatedBursts()
    {
        BurstPatternAnalysis result = Analyze(80, 80, 200, 80, 80);

        Assert.Equal(6, result.BurstObjectCount);
        Assert.Equal(1, result.Coverage, 10);
    }

    [Fact]
    public void AggregateLengthMetricsUseQualifiedRunsOnly()
    {
        BurstPatternAnalysis result = Analyze(80, 80, 200, 80, 80, 80, 80);

        Assert.Equal(5, result.LongestBurst);
        Assert.Equal(4, result.AverageBurstLength, 10);
    }

    [Fact]
    public void ConfidenceIsAlwaysBounded()
    {
        foreach (BurstPatternAnalysis result in new[]
        {
            Analyze(),
            Analyze(80),
            Analyze(80, 85, 78, 83),
            Analyze(Enumerable.Repeat(80, 12).ToArray())
        })
            Assert.InRange(result.Confidence, 0, 1);
    }

    [Fact]
    public void CircleOnlyConventionIsPreserved()
    {
        var objects = Objects(80, 80, 80);
        objects[2].Type = 2;

        BurstPatternAnalysis result = StreamPatternAnalyzer.AnalyzeBurst(objects);

        Assert.Empty(result.Sequences);
    }

    [Fact]
    public void GeometryDoesNotAffectTemporalBurstRecognition()
    {
        var objects = Objects(80, 80, 80);
        for (int index = 0; index < objects.Count; index++)
            objects[index].X = index * 300;

        Assert.Single(StreamPatternAnalyzer.AnalyzeBurst(objects).Sequences);
    }

    [Fact]
    public void UsesExistingStreamTemporalQualification()
    {
        BurstPatternSequence sequence = Assert.Single(Analyze(30, 130).Sequences);
        Assert.Equal(new double[] { 30, 130 }, sequence.Intervals);

        Assert.Empty(Analyze(29, 29, 29).Sequences);
        Assert.Empty(Analyze(131, 131, 131).Sequences);
    }

    [Fact]
    public void AnalysisDoesNotChangeGameplayIdentityRatingOrJumpPatterns()
    {
        Beatmap map = Map(80, 80, 82, 79);
        GameplayProfile profile = GameplayAnalyzer.Analyze(map);
        string identity = System.Text.Json.JsonSerializer.Serialize(profile.Identity);
        double rating = map.BeatInsightRating;
        var jumpBefore = JumpPatternAnalyzer.Analyze(map.HitObjects, profile);

        BurstPatternAnalysis first = StreamPatternAnalyzer.AnalyzeBurst(map.HitObjects);
        BurstPatternAnalysis second = StreamPatternAnalyzer.AnalyzeBurst(map.HitObjects);

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
        var options = new BurstPatternOptions();
        Assert.Equal(3, options.MinimumBurstObjects);
        Assert.Equal(8, options.MaximumBurstObjects);
        Assert.False(GameplayDebug.BurstPatternEnabled);
        Assert.False(GameplayDebug.BurstPatternDetailedEnabled);
        GameplayDebug.BurstPattern(Analyze(80, 80));
    }

    private static BurstPatternAnalysis Analyze(params int[] intervals) =>
        StreamPatternAnalyzer.AnalyzeBurst(Objects(intervals));

    private static List<HitObject> Objects(params int[] intervals) => Map(intervals).HitObjects;

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
