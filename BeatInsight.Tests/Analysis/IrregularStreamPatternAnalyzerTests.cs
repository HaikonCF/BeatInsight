using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Analysis.StreamPatterns;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.StreamPatterns;

namespace BeatInsight.Tests.Analysis;

public sealed class IrregularStreamPatternAnalyzerTests
{
    private static readonly int[] ObviousIrregular = { 80, 105, 78, 118, 82, 110, 76 };
    private static readonly int[] StrongAlternation = { 70, 110, 72, 108, 69, 112, 71 };

    [Fact]
    public void RunBelowMinimumLength_IsRejected()
    {
        IrregularStreamPatternSequence run = Assert.Single(Analyze(80, 110, 70, 115).Runs);

        Assert.False(run.Qualified);
        Assert.Equal("TooShort", run.Reason);
    }

    [Fact]
    public void ConstantAndMicroVariationRuns_AreRejectedAsRegular()
    {
        IrregularStreamPatternSequence constant = Assert.Single(Analyze(80, 80, 80, 80, 80, 80).Runs);
        IrregularStreamPatternSequence noisy = Assert.Single(Analyze(79, 81, 80, 79, 82, 80).Runs);

        Assert.Equal("TooRegular", constant.Reason);
        Assert.Equal("TooRegular", noisy.Reason);
        Assert.Empty(Analyze(65, 65, 65, 65, 65, 65, 65).Sequences);
    }

    [Fact]
    public void ObviousIrregularityAndStrongAlternation_AreAccepted()
    {
        Assert.Single(Analyze(ObviousIrregular).Sequences);
        Assert.Single(Analyze(StrongAlternation).Sequences);
    }

    [Fact]
    public void PopulationStatisticsAndDeltasAreCalculatedExactly()
    {
        int[] intervals = { 80, 100, 60, 100, 60 };

        IrregularStreamPatternSequence sequence = Assert.Single(Analyze(intervals).Runs);

        double mean = intervals.Average();
        double populationDeviation = Math.Sqrt(intervals.Average(value => Math.Pow(value - mean, 2)));
        Assert.Equal(80, sequence.AverageInterval, 10);
        Assert.Equal(60, sequence.MinimumInterval);
        Assert.Equal(100, sequence.MaximumInterval);
        Assert.Equal(40, sequence.IntervalRange);
        Assert.Equal(populationDeviation, sequence.StandardDeviation, 10);
        Assert.Equal(populationDeviation / mean, sequence.CoefficientOfVariation, 10);
        Assert.Equal(new double[] { 20, 40, 40, 40 }, sequence.IntervalDeltas);
        Assert.Equal(35, sequence.AverageIntervalDelta, 10);
        Assert.Equal(40, sequence.MaximumIntervalDelta);
        Assert.Equal(4, sequence.SignificantDeltaCount);
        Assert.Equal(1, sequence.SignificantDeltaRatio, 10);
    }

    [Fact]
    public void SignificantDeltaThresholdIsInclusive()
    {
        var options = new IrregularStreamPatternOptions(
            MinimumSignificantIntervalDelta: 12,
            MinimumCoefficientOfVariation: 0,
            MinimumSignificantDeltaRatio: 1);

        IrregularStreamPatternSequence sequence = Assert.Single(
            StreamPatternAnalyzer.AnalyzeIrregularStream(
                Map(80, 92, 80, 92, 80).HitObjects,
                options).Sequences);

        Assert.Equal(new double[] { 12, 12, 12, 12 }, sequence.IntervalDeltas);
        Assert.Equal(4, sequence.SignificantDeltaCount);
        Assert.Equal(1, sequence.SignificantDeltaRatio, 10);
    }

    [Fact]
    public void CoefficientOfVariationThresholdIsInclusive()
    {
        IrregularStreamPatternSequence baseline = Assert.Single(Analyze(ObviousIrregular).Sequences);
        var inclusive = new IrregularStreamPatternOptions(
            MinimumCoefficientOfVariation: baseline.CoefficientOfVariation);
        var above = inclusive with
        {
            MinimumCoefficientOfVariation = baseline.CoefficientOfVariation + 0.000001
        };

        Assert.Single(StreamPatternAnalyzer.AnalyzeIrregularStream(Map(ObviousIrregular).HitObjects, inclusive).Sequences);
        Assert.Equal("TooRegular", Assert.Single(
            StreamPatternAnalyzer.AnalyzeIrregularStream(Map(ObviousIrregular).HitObjects, above).Runs).Reason);
    }

    [Fact]
    public void SignificantDeltaRatioRemainsDiagnosticOnly()
    {
        int[] intervals = { 67, 68, 67, 102, 101, 102, 101, 101, 102, 67, 68 };
        var permissive = new IrregularStreamPatternOptions(MinimumSignificantDeltaRatio: 0);
        var impossible = permissive with { MinimumSignificantDeltaRatio = 1 };

        IrregularStreamPatternSequence first = Assert.Single(
            StreamPatternAnalyzer.AnalyzeIrregularStream(Map(intervals).HitObjects, permissive).Sequences);
        IrregularStreamPatternSequence second = Assert.Single(
            StreamPatternAnalyzer.AnalyzeIrregularStream(Map(intervals).HitObjects, impossible).Sequences);

        Assert.Equal(0.2, first.SignificantDeltaRatio, 10);
        Assert.Equal(first.SignificantDeltaRatio, second.SignificantDeltaRatio);
        Assert.Equal(first.DistinctIrregularEventCount, second.DistinctIrregularEventCount);
    }

    [Fact]
    public void SingleSpikeIsWeakerThanRepeatedIrregularity()
    {
        IrregularStreamPatternSequence spike = Assert.Single(Analyze(80, 80, 80, 125, 80, 80, 80).Runs);
        IrregularStreamPatternSequence repeated = Assert.Single(Analyze(StrongAlternation).Sequences);

        Assert.False(spike.Qualified);
        Assert.Equal(1, spike.DistinctIrregularEventCount);
        Assert.Equal("TooFewDistinctIrregularEvents", spike.Reason);
        Assert.True(spike.IrregularityScore < repeated.IrregularityScore);
        Assert.True(spike.IrregularityScore < 0.60);
    }

    [Fact]
    public void MedianUsesMiddleValueForOddIntervalsAndMiddleAverageForEvenIntervals()
    {
        IrregularStreamPatternSequence odd = Assert.Single(
            Analyze(80, 105, 78, 118, 82, 110, 76).Runs);
        IrregularStreamPatternSequence even = Assert.Single(
            Analyze(70, 110, 72, 108, 69, 112).Runs);

        Assert.Equal(82, odd.MedianInterval, 10);
        Assert.Equal(90, even.MedianInterval, 10);
    }

    [Fact]
    public void AtypicalDeviationBoundaryAt12MillisecondsIsInclusive()
    {
        int[] intervals = { 80, 92, 80, 80, 92, 80 };
        var options = new IrregularStreamPatternOptions(MinimumCoefficientOfVariation: 0);

        IrregularStreamPatternSequence sequence = Assert.Single(
            StreamPatternAnalyzer.AnalyzeIrregularStream(Map(intervals).HitObjects, options).Sequences);

        Assert.Equal(80, sequence.MedianInterval);
        Assert.Equal(new double[] { 0, 12, 0, 0, 12, 0 }, sequence.AtypicalIntervalDeviations);
        Assert.Equal(new[] { false, true, false, false, true, false }, sequence.AtypicalIntervalMask);
        Assert.Equal(2, sequence.AtypicalIntervalCount);
        Assert.Equal(2, sequence.DistinctIrregularEventCount);
    }

    [Fact]
    public void DeviationBelow12MillisecondsIsNotAtypical()
    {
        int[] intervals = { 80, 91, 80, 80, 91, 80 };
        var options = new IrregularStreamPatternOptions(MinimumCoefficientOfVariation: 0);

        IrregularStreamPatternSequence sequence = Assert.Single(
            StreamPatternAnalyzer.AnalyzeIrregularStream(Map(intervals).HitObjects, options).Runs);

        Assert.Equal(new[] { false, false, false, false, false, false }, sequence.AtypicalIntervalMask);
        Assert.Equal(0, sequence.DistinctIrregularEventCount);
        Assert.Equal("TooFewDistinctIrregularEvents", sequence.Reason);
    }

    [Fact]
    public void AdjacentAtypicalIntervalsFormOneEventAndSeparatedRegionsFormTwo()
    {
        var options = new IrregularStreamPatternOptions(MinimumCoefficientOfVariation: 0);
        IrregularStreamPatternSequence adjacent = Assert.Single(
            StreamPatternAnalyzer.AnalyzeIrregularStream(
                Map(80, 105, 105, 80, 80, 80).HitObjects, options).Runs);
        IrregularStreamPatternSequence separated = Assert.Single(
            StreamPatternAnalyzer.AnalyzeIrregularStream(
                Map(105, 80, 80, 105, 80, 80).HitObjects, options).Sequences);

        Assert.Equal(1, adjacent.DistinctIrregularEventCount);
        Assert.Equal("TooFewDistinctIrregularEvents", adjacent.Reason);
        Assert.Equal(2, separated.DistinctIrregularEventCount);
    }

    [Fact]
    public void CoefficientOfVariationAtExactlyPointOneIsInclusive()
    {
        int[] intervals = { 80, 100, 100, 120, 100, 100, 100, 100 };
        var inclusive = new IrregularStreamPatternOptions();
        var above = inclusive with { MinimumCoefficientOfVariation = 0.100001 };

        IrregularStreamPatternSequence accepted = Assert.Single(
            StreamPatternAnalyzer.AnalyzeIrregularStream(Map(intervals).HitObjects, inclusive).Sequences);
        IrregularStreamPatternSequence rejected = Assert.Single(
            StreamPatternAnalyzer.AnalyzeIrregularStream(Map(intervals).HitObjects, above).Runs);

        Assert.Equal(0.10, accepted.CoefficientOfVariation, 10);
        Assert.Equal(2, accepted.DistinctIrregularEventCount);
        Assert.Equal("TooRegular", rejected.Reason);
    }

    [Fact]
    public void MinimumDistinctEventBoundaryRejectsOneAndAcceptsTwo()
    {
        int[] intervals = { 105, 80, 80, 105, 80, 80 };
        var two = new IrregularStreamPatternOptions(MinimumCoefficientOfVariation: 0);
        var three = two with { MinimumDistinctIrregularEvents = 3 };

        Assert.Single(StreamPatternAnalyzer.AnalyzeIrregularStream(Map(intervals).HitObjects, two).Sequences);
        IrregularStreamPatternSequence rejected = Assert.Single(
            StreamPatternAnalyzer.AnalyzeIrregularStream(Map(intervals).HitObjects, three).Runs);
        Assert.Equal("TooFewDistinctIrregularEvents", rejected.Reason);
    }

    [Fact]
    public void RequiredSyntheticControlsHaveExpectedQualification()
    {
        Assert.Empty(Analyze(80, 80, 80, 80, 80, 80, 80).Sequences);
        Assert.Empty(Analyze(79, 81, 80, 79, 82, 80, 81).Sequences);
        Assert.Empty(Analyze(80, 80, 80, 125, 80, 80, 80).Sequences);
        Assert.Single(Analyze(80, 105, 78, 118, 82, 110, 76).Sequences);
        Assert.Single(Analyze(70, 110, 72, 108, 69, 112, 71).Sequences);
    }

    [Fact]
    public void ExactRoadOfResistanceCandidateIsQualified()
    {
        int[] times = { 25_546, 25_613, 25_681, 25_748, 25_850, 25_951, 26_053, 26_154, 26_255, 26_357, 26_424, 26_492 };
        (int X, int Y)[] positions =
        {
            (233, 355), (236, 337), (239, 320), (243, 302),
            (233, 254), (246, 210), (279, 178), (323, 164),
            (369, 174), (404, 204), (403, 217), (403, 230)
        };
        var map = new Beatmap();
        for (int index = 0; index < times.Length; index++)
            map.HitObjects.Add(new HitObject
            {
                Type = index is 3 or 9 ? 5 : 1,
                Time = times[index],
                X = positions[index].X,
                Y = positions[index].Y
            });

        IrregularStreamPatternSequence sequence = Assert.Single(
            StreamPatternAnalyzer.AnalyzeIrregularStream(map.HitObjects).Sequences);

        Assert.Equal(25_546, sequence.StartTime);
        Assert.Equal(26_492, sequence.EndTime);
        Assert.Equal(new double[] { 67, 68, 67, 102, 101, 102, 101, 101, 102, 67, 68 }, sequence.Intervals);
        Assert.Equal(101, sequence.MedianInterval);
        Assert.Equal(5, sequence.AtypicalIntervalCount);
        Assert.Equal(2, sequence.DistinctIrregularEventCount);
        Assert.Equal("Qualified", sequence.Reason);
    }

    [Fact]
    public void RegularShortStreamIsNotIrregularButIrregularShortStreamCanCoexist()
    {
        Beatmap regular = Map(Enumerable.Repeat(80, 8).ToArray());
        Beatmap irregular = Map(RepeatToLength(StrongAlternation, 8));

        Assert.Single(StreamPatternAnalyzer.AnalyzeShortStream(regular.HitObjects).Sequences);
        Assert.Empty(StreamPatternAnalyzer.AnalyzeIrregularStream(regular.HitObjects).Sequences);
        Assert.Single(StreamPatternAnalyzer.AnalyzeShortStream(irregular.HitObjects).Sequences);
        Assert.Single(StreamPatternAnalyzer.AnalyzeIrregularStream(irregular.HitObjects).Sequences);
    }

    [Fact]
    public void IrregularLongAndDeathstreamRunsCanCoexistWithLengthPatterns()
    {
        Beatmap longMap = Map(RepeatToLength(StrongAlternation, 19));
        Beatmap deathMap = Map(RepeatToLength(StrongAlternation, 32));

        Assert.Single(StreamPatternAnalyzer.AnalyzeLongStream(longMap.HitObjects).Sequences);
        Assert.Single(StreamPatternAnalyzer.AnalyzeIrregularStream(longMap.HitObjects).Sequences);
        Assert.Single(StreamPatternAnalyzer.AnalyzeDeathstream(deathMap.HitObjects).Sequences);
        Assert.Single(StreamPatternAnalyzer.AnalyzeIrregularStream(deathMap.HitObjects).Sequences);
    }

    [Fact]
    public void IrregularCanCoexistWithSpeedFlowAndAlternatingStreams()
    {
        Beatmap speed = Map(StrongAlternation);
        Beatmap flow = MapWithHeadings(StrongAlternation, 0, 30, 60, 90, 120, 150, 180);
        Beatmap alternating = MapWithHeadings(StrongAlternation, 30, -30, 30, -30, 30, -30, 30);

        Assert.Single(StreamPatternAnalyzer.AnalyzeIrregularStream(speed.HitObjects).Sequences);
        Assert.Single(StreamPatternAnalyzer.AnalyzeSpeedStream(speed.HitObjects).Sequences);
        Assert.Single(StreamPatternAnalyzer.AnalyzeIrregularStream(flow.HitObjects).Sequences);
        Assert.Single(StreamPatternAnalyzer.AnalyzeFlowStream(flow.HitObjects).Sequences);
        Assert.Single(StreamPatternAnalyzer.AnalyzeIrregularStream(alternating.HitObjects).Sequences);
        Assert.Single(StreamPatternAnalyzer.AnalyzeAlternatingStream(alternating.HitObjects).Sequences);
    }

    [Fact]
    public void StaminaAnalysisRemainsUnchanged()
    {
        Beatmap map = MapTwoRuns(
            RepeatToLength(StrongAlternation, 11),
            RepeatToLength(ObviousIrregular, 11),
            500);
        string before = System.Text.Json.JsonSerializer.Serialize(
            StreamPatternAnalyzer.AnalyzeStaminaStream(map.HitObjects));

        _ = StreamPatternAnalyzer.AnalyzeIrregularStream(map.HitObjects);

        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(
            StreamPatternAnalyzer.AnalyzeStaminaStream(map.HitObjects)));
    }

    [Fact]
    public void CoverageAndAggregateMetricsUseQualifiedRunsOnly()
    {
        Beatmap map = MapTwoRuns(ObviousIrregular, StrongAlternation, 200);

        IrregularStreamPatternAnalysis result =
            StreamPatternAnalyzer.AnalyzeIrregularStream(map.HitObjects);

        Assert.Equal(2, result.IrregularStreamSequenceCount);
        Assert.Equal(16, result.IrregularStreamObjectCount);
        Assert.Equal(1, result.Coverage, 10);
        Assert.Equal(8, result.LongestIrregularStream);
        Assert.Equal(8, result.AverageIrregularStreamLength, 10);
        Assert.InRange(result.AverageCoefficientOfVariation, 0.10, 1);
        Assert.True(result.AverageIntervalDelta > 0);
        Assert.True(result.AverageSignificantDeltaRatio >= 0.30);
        Assert.True(result.AverageDistinctIrregularEvents >= 2);
        Assert.InRange(result.AverageAtypicalIntervalRatio, 0, 1);
        Assert.InRange(result.AverageIrregularityScore, 0, 1);
    }

    [Fact]
    public void ScoreAndConfidenceAreAlwaysBounded()
    {
        foreach (IrregularStreamPatternAnalysis result in new[]
        {
            Analyze(80, 80, 80, 80, 80),
            Analyze(ObviousIrregular),
            Analyze(StrongAlternation),
            Analyze(80, 80, 80, 125, 80, 80, 80)
        })
        {
            Assert.InRange(result.Confidence, 0, 1);
            Assert.All(result.Runs, run => Assert.InRange(run.IrregularityScore, 0, 1));
        }
    }

    [Fact]
    public void CircleOnlyConventionAndSpatialIndependenceArePreserved()
    {
        Beatmap withSlider = Map(ObviousIrregular);
        withSlider.HitObjects[3].Type = 2;
        Assert.Empty(StreamPatternAnalyzer.AnalyzeIrregularStream(withSlider.HitObjects).Sequences);

        Beatmap spaced = Map(ObviousIrregular);
        for (int index = 0; index < spaced.HitObjects.Count; index++)
            spaced.HitObjects[index].X = index * 300;
        Assert.Single(StreamPatternAnalyzer.AnalyzeIrregularStream(spaced.HitObjects).Sequences);
    }

    [Fact]
    public void AnalysisDoesNotChangeGameplayIdentityRatingOrJumpPatterns()
    {
        Beatmap map = Map(RepeatToLength(StrongAlternation, 19));
        GameplayProfile profile = GameplayAnalyzer.Analyze(map);
        string identity = System.Text.Json.JsonSerializer.Serialize(profile.Identity);
        double rating = map.BeatInsightRating;
        var jumpBefore = JumpPatternAnalyzer.Analyze(map.HitObjects, profile);

        IrregularStreamPatternAnalysis first = StreamPatternAnalyzer.AnalyzeIrregularStream(map.HitObjects);
        IrregularStreamPatternAnalysis second = StreamPatternAnalyzer.AnalyzeIrregularStream(map.HitObjects);

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
        var options = new IrregularStreamPatternOptions();
        Assert.Equal(6, options.MinimumIrregularStreamObjects);
        Assert.Equal(12, options.MinimumSignificantIntervalDelta);
        Assert.Equal(12, options.MinimumAtypicalIntervalDeviation);
        Assert.Equal(0.10, options.MinimumCoefficientOfVariation);
        Assert.Equal(0.30, options.MinimumSignificantDeltaRatio);
        Assert.Equal(2, options.MinimumDistinctIrregularEvents);
        Assert.False(GameplayDebug.IrregularStreamPatternEnabled);
        Assert.False(GameplayDebug.IrregularStreamPatternDetailedEnabled);
        GameplayDebug.IrregularStreamPattern(Analyze(ObviousIrregular));
    }

    private static IrregularStreamPatternAnalysis Analyze(params int[] intervals) =>
        StreamPatternAnalyzer.AnalyzeIrregularStream(Map(intervals).HitObjects);

    private static int[] RepeatToLength(IReadOnlyList<int> source, int length) =>
        Enumerable.Range(0, length).Select(index => source[index % source.Count]).ToArray();

    private static Beatmap Map(params int[] intervals)
    {
        var map = new Beatmap();
        AddRun(map, intervals, 0);
        return map;
    }

    private static Beatmap MapWithHeadings(IReadOnlyList<int> intervals, params double[] headings)
    {
        Assert.Equal(intervals.Count, headings.Length);
        var map = new Beatmap();
        double x = 256;
        double y = 192;
        int time = 0;
        map.HitObjects.Add(new HitObject { Type = 1, Time = time, X = (int)x, Y = (int)y });
        for (int index = 0; index < intervals.Count; index++)
        {
            time += intervals[index];
            double radians = headings[index] * Math.PI / 180;
            x += 50 * Math.Cos(radians);
            y += 50 * Math.Sin(radians);
            map.HitObjects.Add(new HitObject
            {
                Type = 1,
                Time = time,
                X = (int)Math.Round(x),
                Y = (int)Math.Round(y)
            });
        }
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
