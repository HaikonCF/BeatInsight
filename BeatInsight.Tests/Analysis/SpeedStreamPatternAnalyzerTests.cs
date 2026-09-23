using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Analysis.StreamPatterns;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.StreamPatterns;

namespace BeatInsight.Tests.Analysis;

public sealed class SpeedStreamPatternAnalyzerTests
{
    [Fact]
    public void VeryFastRunBelowMinimumLength_IsRejected()
    {
        SpeedStreamPatternSequence run = Assert.Single(Analyze(60, 60, 60, 60).Runs);

        Assert.False(run.Qualified);
        Assert.Equal("TooShort", run.Reason);
    }

    [Fact]
    public void SufficientlyLongButSlowRun_IsRejected()
    {
        SpeedStreamPatternSequence run = Assert.Single(Analyze(110, 110, 110, 110, 110).Runs);

        Assert.False(run.Qualified);
        Assert.Equal("TooSlow", run.Reason);
    }

    [Fact]
    public void AverageIntervalThresholdIsInclusive()
    {
        SpeedStreamPatternSequence threshold = Assert.Single(Analyze(90, 90, 90, 90, 90).Sequences);
        SpeedStreamPatternSequence above = Assert.Single(Analyze(90, 90, 90, 90, 91).Runs);

        Assert.Equal(90, threshold.AverageInterval, 10);
        Assert.Equal(90.2, above.AverageInterval, 10);
        Assert.False(above.Qualified);
        Assert.Equal("TooSlow", above.Reason);
    }

    [Fact]
    public void RegularFastRun_IsSpeedButNotIrregular()
    {
        Beatmap map = Map(75, 75, 75, 75, 75, 75);

        Assert.Single(StreamPatternAnalyzer.AnalyzeSpeedStream(map.HitObjects).Sequences);
        Assert.Empty(StreamPatternAnalyzer.AnalyzeIrregularStream(map.HitObjects).Sequences);
    }

    [Fact]
    public void SlowIrregularRun_IsIrregularButNotSpeed()
    {
        Beatmap map = Map(100, 130, 98, 125, 102, 128, 96);

        Assert.Single(StreamPatternAnalyzer.AnalyzeIrregularStream(map.HitObjects).Sequences);
        Assert.Empty(StreamPatternAnalyzer.AnalyzeSpeedStream(map.HitObjects).Sequences);
    }

    [Fact]
    public void FastIrregularRun_CanBeSpeedAndIrregular()
    {
        Beatmap map = Map(70, 95, 68, 98, 72, 96, 69);

        Assert.Single(StreamPatternAnalyzer.AnalyzeSpeedStream(map.HitObjects).Sequences);
        Assert.Single(StreamPatternAnalyzer.AnalyzeIrregularStream(map.HitObjects).Sequences);
    }

    [Fact]
    public void OneVeryFastIntervalDoesNotOverrideSlowAverage()
    {
        SpeedStreamPatternSequence run = Assert.Single(Analyze(120, 120, 60, 120, 120).Runs);

        Assert.Equal(60, run.MinimumInterval);
        Assert.Equal(108, run.AverageInterval, 10);
        Assert.False(run.Qualified);
    }

    [Fact]
    public void SpeedCanCoexistWithShortLongAndDeathstreamLengths()
    {
        Beatmap shortMap = Map(Repeat(75, 8));
        Beatmap longMap = Map(Repeat(75, 16));
        Beatmap deathMap = Map(Repeat(75, 32));

        Assert.Single(StreamPatternAnalyzer.AnalyzeShortStream(shortMap.HitObjects).Sequences);
        Assert.Single(StreamPatternAnalyzer.AnalyzeSpeedStream(shortMap.HitObjects).Sequences);
        Assert.Single(StreamPatternAnalyzer.AnalyzeLongStream(longMap.HitObjects).Sequences);
        Assert.Single(StreamPatternAnalyzer.AnalyzeSpeedStream(longMap.HitObjects).Sequences);
        Assert.Single(StreamPatternAnalyzer.AnalyzeDeathstream(deathMap.HitObjects).Sequences);
        Assert.Single(StreamPatternAnalyzer.AnalyzeSpeedStream(deathMap.HitObjects).Sequences);
    }

    [Fact]
    public void StaminaAnalysisRemainsUnchanged()
    {
        Beatmap map = MapTwoRuns(Repeat(75, 11), Repeat(80, 11), 500);
        string before = System.Text.Json.JsonSerializer.Serialize(
            StreamPatternAnalyzer.AnalyzeStaminaStream(map.HitObjects));

        _ = StreamPatternAnalyzer.AnalyzeSpeedStream(map.HitObjects);

        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(
            StreamPatternAnalyzer.AnalyzeStaminaStream(map.HitObjects)));
        Assert.Equal(2, StreamPatternAnalyzer.AnalyzeSpeedStream(map.HitObjects).SpeedStreamSequenceCount);
    }

    [Fact]
    public void SpeedIntensityUsesSlowAndFastBoundariesAndIsBounded()
    {
        Assert.Equal(0, Assert.Single(Analyze(Repeat(130, 5)).Runs).SpeedIntensity, 10);
        Assert.Equal(1, Assert.Single(Analyze(Repeat(60, 5)).Runs).SpeedIntensity, 10);
        Assert.Equal(1, Assert.Single(Analyze(Repeat(30, 5)).Runs).SpeedIntensity, 10);
        Assert.Equal(40.0 / 70.0, Assert.Single(Analyze(Repeat(90, 5)).Runs).SpeedIntensity, 10);

        foreach (SpeedStreamPatternSequence run in new[]
        {
            Assert.Single(Analyze(Repeat(130, 5)).Runs),
            Assert.Single(Analyze(Repeat(90, 5)).Runs),
            Assert.Single(Analyze(Repeat(60, 5)).Runs),
            Assert.Single(Analyze(Repeat(30, 5)).Runs)
        })
            Assert.InRange(run.SpeedIntensity, 0, 1);
    }

    [Fact]
    public void QuarterStreamBpmUsesObjectIntervalsNotBeatmapBpm()
    {
        Beatmap map = Map(90, 90, 90, 90, 90);
        map.BPM = 300;

        SpeedStreamPatternSequence sequence = Assert.Single(
            StreamPatternAnalyzer.AnalyzeSpeedStream(map.HitObjects).Sequences);

        Assert.Equal(60000.0 / (90 * 4), sequence.StreamBpm, 10);
        Assert.NotEqual(map.BPM, sequence.StreamBpm);
    }

    [Fact]
    public void CoverageAndAggregateMetricsUseQualifiedRunsOnly()
    {
        Beatmap map = MapTwoRuns(Repeat(80, 5), Repeat(75, 7), 200);

        SpeedStreamPatternAnalysis result = StreamPatternAnalyzer.AnalyzeSpeedStream(map.HitObjects);

        Assert.Equal(2, result.SpeedStreamSequenceCount);
        Assert.Equal(14, result.SpeedStreamObjectCount);
        Assert.Equal(1, result.Coverage, 10);
        Assert.Equal(8, result.LongestSpeedStream);
        Assert.Equal(7, result.AverageSpeedStreamLength, 10);
        Assert.Equal(925.0 / 12.0, result.AverageInterval, 10);
        Assert.Equal(75, result.FastestAverageInterval, 10);
        Assert.Equal((187.5 + 200) / 2, result.AverageStreamBpm, 10);
        Assert.Equal(200, result.MaximumStreamBpm, 10);
        Assert.InRange(result.AverageSpeedIntensity, 0, 1);
        Assert.Equal(1, result.AverageIntervalConsistency, 10);
    }

    [Fact]
    public void ConfidenceIsAlwaysBounded()
    {
        foreach (SpeedStreamPatternAnalysis result in new[]
        {
            Analyze(110, 110, 110, 110, 110),
            Analyze(90, 90, 90, 90, 90),
            Analyze(75, 75, 75, 75, 75),
            Analyze(70, 90, 68, 88, 72, 86)
        })
            Assert.InRange(result.Confidence, 0, 1);
    }

    [Fact]
    public void CircleOnlyConventionAndSpatialIndependenceArePreserved()
    {
        Beatmap withSlider = Map(75, 75, 75, 75, 75, 75);
        withSlider.HitObjects[3].Type = 2;
        Assert.Empty(StreamPatternAnalyzer.AnalyzeSpeedStream(withSlider.HitObjects).Sequences);

        Beatmap spaced = Map(75, 75, 75, 75, 75, 75);
        for (int index = 0; index < spaced.HitObjects.Count; index++)
            spaced.HitObjects[index].X = index * 300;
        Assert.Single(StreamPatternAnalyzer.AnalyzeSpeedStream(spaced.HitObjects).Sequences);
    }

    [Fact]
    public void AnalysisDoesNotChangeGameplayIdentityRatingOrJumpPatterns()
    {
        Beatmap map = Map(Repeat(75, 16));
        GameplayProfile profile = GameplayAnalyzer.Analyze(map);
        string identity = System.Text.Json.JsonSerializer.Serialize(profile.Identity);
        double rating = map.BeatInsightRating;
        var jumpBefore = JumpPatternAnalyzer.Analyze(map.HitObjects, profile);

        SpeedStreamPatternAnalysis first = StreamPatternAnalyzer.AnalyzeSpeedStream(map.HitObjects);
        SpeedStreamPatternAnalysis second = StreamPatternAnalyzer.AnalyzeSpeedStream(map.HitObjects);

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
        var options = new SpeedStreamPatternOptions();
        Assert.Equal(6, options.MinimumSpeedStreamObjects);
        Assert.Equal(90, options.MaximumAverageIntervalForSpeedStream);
        Assert.False(GameplayDebug.SpeedStreamPatternEnabled);
        Assert.False(GameplayDebug.SpeedStreamPatternDetailedEnabled);
        GameplayDebug.SpeedStreamPattern(Analyze(75, 75, 75, 75, 75));
    }

    private static SpeedStreamPatternAnalysis Analyze(params int[] intervals) =>
        StreamPatternAnalyzer.AnalyzeSpeedStream(Map(intervals).HitObjects);

    private static int[] Repeat(int interval, int count) => Enumerable.Repeat(interval, count).ToArray();

    private static Beatmap Map(params int[] intervals)
    {
        var map = new Beatmap();
        AddRun(map, intervals, 0);
        return map;
    }

    private static Beatmap MapTwoRuns(
        IReadOnlyList<int> firstIntervals,
        IReadOnlyList<int> secondIntervals,
        int gap)
    {
        var map = new Beatmap();
        int time = AddRun(map, firstIntervals, 0);
        AddRun(map, secondIntervals, time + gap);
        return map;
    }

    private static int AddRun(Beatmap map, IReadOnlyList<int> intervals, int startTime)
    {
        int time = startTime;
        int x = map.HitObjects.Count * 10;
        map.HitObjects.Add(new HitObject { Type = 1, Time = time, X = x, Y = 0 });
        foreach (int interval in intervals)
        {
            time += interval;
            x += 10;
            map.HitObjects.Add(new HitObject { Type = 1, Time = time, X = x, Y = 0 });
        }
        return time;
    }
}
