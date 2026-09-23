using BeatInsight.Analysis.Fusion;
using BeatInsight.Analysis.Rating;
using BeatInsight.Analysis.Speed;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.Speed;

namespace BeatInsight.Tests.Analysis;

public sealed class GlobalTripletSpeedAnalyzerTests
{
    [Fact] public void EmptyMapReturnsZero() => AssertEmpty(Analyze());
    [Fact] public void OneObjectReturnsZero() => AssertEmpty(Analyze(0));
    [Fact] public void TwoObjectsReturnZero() => AssertEmpty(Analyze(0, 100));

    [Fact]
    public void ExactlyThreeObjectsProduceOneTriplet()
    {
        GlobalTripletSpeedAnalysis result = Analyze(0, 150, 300);
        Assert.Equal(1, result.TotalTripletCount);
        Assert.Equal(1, result.ValidTripletCount);
        Assert.Equal(50, result.SpeedPercent, 10);
    }

    [Fact] public void FourObjectsProduceTwoSlidingTriplets() => Assert.Equal(2, Analyze(0, 100, 200, 300).TotalTripletCount);
    [Fact] public void FiveObjectsProduceThreeSlidingTriplets() => Assert.Equal(3, Analyze(0, 100, 200, 300, 400).TotalTripletCount);

    [Fact]
    public void SixObjectsProduceExactSlidingWindowOrder()
    {
        GlobalTripletSpeedAnalysis result = Analyze(true, 0, 100, 210, 330, 460, 600);
        Assert.Equal(4, result.TotalTripletCount);
        Assert.Equal([(0, 1, 2), (1, 2, 3), (2, 3, 4), (3, 4, 5)],
            result.Diagnostics.Select(item => (item.StartIndex, item.MiddleIndex, item.EndIndex)).ToArray());
    }

    [Theory]
    [InlineData(250, 0)]
    [InlineData(150, 50)]
    [InlineData(50, 100)]
    public void ConstantIntervalsMapToExpectedSpeed(double interval, double expected)
    {
        Assert.Equal(expected, Analyze(0, (int)interval, (int)(2 * interval)).SpeedPercent, 10);
    }

    [Fact]
    public void ShorterIntervalAlwaysRaisesSpeed()
    {
        Assert.True(Analyze(0, 75, 150).SpeedPercent > Analyze(0, 150, 300).SpeedPercent);
    }

    [Theory]
    [InlineData(-1000)] [InlineData(0)] [InlineData(25)] [InlineData(50)] [InlineData(250)] [InlineData(1000)]
    public void MappingIsBounded(double interval) => Assert.InRange(GlobalTripletSpeedAnalyzer.IntervalToSpeedPercent(interval), 0, 100);

    [Fact]
    public void OutputsAreAlwaysFinite()
    {
        GlobalTripletSpeedAnalysis result = Analyze(0, 1, 2, 1000000);
        double[] values = [result.SpeedPercent, result.AverageTripletIntervalMs, result.MinTripletIntervalMs,
            result.MaxTripletIntervalMs, result.MedianTripletIntervalMs, result.Confidence,
            result.EquivalentAverageBpm, result.AverageConsistency];
        Assert.All(values, value => Assert.True(double.IsFinite(value)));
        Assert.Equal(0, GlobalTripletSpeedAnalyzer.IntervalToSpeedPercent(double.NaN));
        Assert.Equal(0, GlobalTripletSpeedAnalyzer.IntervalToSpeedPercent(double.PositiveInfinity));
    }

    [Fact]
    public void EqualTimestampsExcludeTriplet()
    {
        GlobalTripletSpeedAnalysis result = Analyze(0, 0, 100);
        Assert.Equal(0, result.ValidTripletCount);
        Assert.Equal(1, result.ExcludedTripletCount);
    }

    [Fact]
    public void DecreasingTimestampsExcludeTriplet()
    {
        GlobalTripletSpeedAnalysis result = Analyze(100, 50, 200);
        Assert.Equal(0, result.ValidTripletCount);
        Assert.Equal(1, result.ExcludedTripletCount);
    }

    [Fact]
    public void SlidersAreIncludedByStartTime()
    {
        HitObject[] objects = [Object(0), Object(150, 2), Object(300)];
        GlobalTripletSpeedAnalysis result = GlobalTripletSpeedAnalyzer.Analyze(objects, true);
        Assert.Equal(1, result.ValidTripletCount);
        Assert.Equal(50, result.SpeedPercent, 10);
    }

    [Fact]
    public void SpinnerBreaksEveryTripletContainingIt()
    {
        HitObject[] objects = [Object(0), Object(100), Object(200, 8), Object(300), Object(400), Object(500)];
        GlobalTripletSpeedAnalysis result = GlobalTripletSpeedAnalyzer.Analyze(objects, true);
        Assert.Equal(4, result.TotalTripletCount);
        Assert.Equal(1, result.ValidTripletCount);
        Assert.Equal(3, result.ExcludedTripletCount);
        Assert.Equal("SpinnerBreak", result.Diagnostics[0].ExclusionReason);
    }

    [Fact]
    public void LongPositiveGapsRemainValidAndContributeZero()
    {
        GlobalTripletSpeedAnalysis result = Analyze(0, 1000, 2000);
        Assert.Equal(1, result.ValidTripletCount);
        Assert.Equal(0, result.SpeedPercent);
    }

    [Fact]
    public void GlobalSpeedIsExactArithmeticMeanOfKnownLocalSpeeds()
    {
        GlobalTripletSpeedAnalysis result = FromDeltas(250, 170, 170, 90, 90);
        Assert.Equal([20d, 40d, 60d, 80d], result.Diagnostics.Select(item => item.LocalSpeedPercent).ToArray());
        Assert.Equal(50, result.SpeedPercent, 10);
    }

    [Fact]
    public void AggregationHasNoPeakPercentileOrTopNWeighting()
    {
        GlobalTripletSpeedAnalysis result = FromDeltas(250, 250, 250, 50, 50);
        Assert.Equal([0d, 0d, 50d, 100d], result.Diagnostics.Select(item => item.LocalSpeedPercent).ToArray());
        Assert.Equal(37.5, result.SpeedPercent, 10);
    }

    [Fact]
    public void IrregularAndRegularTripletsWithSameAverageHaveSameSpeed()
    {
        GlobalTripletSpeedAnalysis regular = Analyze(0, 80, 160);
        GlobalTripletSpeedAnalysis irregular = Analyze(0, 40, 160);
        Assert.Equal(regular.SpeedPercent, irregular.SpeedPercent, 10);
        Assert.NotEqual(regular.AverageConsistency, irregular.AverageConsistency);
    }

    [Fact]
    public void ConfidenceDoesNotChangeSpeed()
    {
        GlobalTripletSpeedAnalysis one = Analyze(0, 150, 300);
        GlobalTripletSpeedAnalysis many = Analyze(0, 150, 300, 450, 600, 750);
        Assert.Equal(one.SpeedPercent, many.SpeedPercent, 10);
        Assert.True(many.Confidence > one.Confidence);
    }

    [Fact]
    public void DiagnosticsAreOptIn()
    {
        Assert.Empty(GlobalTripletSpeedAnalyzer.Analyze(Objects(0, 100, 200)) .Diagnostics);
        Assert.Single(GlobalTripletSpeedAnalyzer.Analyze(Objects(0, 100, 200), true).Diagnostics);
    }

    [Fact]
    public void RealAdapterDoesNotChangeHistoricalSpeedRatingsOrFusion()
    {
        Beatmap map = FixtureLoader.Load("Tower Of Heaven [Extra].osu");
        double oldSpeed = map.GameplayProfile.SpeedScore;
        double oldRating = map.BeatInsightRating;
        double ratingV2 = BeatInsightRatingV2Analyzer.Analyze(map).Score;
        double fusionSpeed = GameplayFusionAnalyzer.Analyze(map).SpeedScore;
        _ = GlobalTripletSpeedAnalyzer.Analyze(map);
        Assert.Equal(oldSpeed, map.GameplayProfile.SpeedScore);
        Assert.Equal(oldRating, map.BeatInsightRating);
        Assert.Equal(ratingV2, BeatInsightRatingV2Analyzer.Analyze(map).Score);
        Assert.Equal(fusionSpeed, GameplayFusionAnalyzer.Analyze(map).SpeedScore);
    }

    [Fact]
    public void DebugIsOptInAndDisabledByDefault()
    {
        Assert.False(GameplayDebug.GlobalTripletSpeedEnabled);
        GameplayDebug.GlobalTripletSpeed(Analyze(0, 100, 200));
    }

    private static void AssertEmpty(GlobalTripletSpeedAnalysis result)
    {
        Assert.Equal(0, result.SpeedPercent);
        Assert.Equal(0, result.TotalTripletCount);
        Assert.Equal(0, result.ValidTripletCount);
        Assert.Equal(0, result.Confidence);
    }

    private static GlobalTripletSpeedAnalysis Analyze(params int[] times) => GlobalTripletSpeedAnalyzer.Analyze(Objects(times), true);
    private static GlobalTripletSpeedAnalysis Analyze(bool diagnostics, params int[] times) => GlobalTripletSpeedAnalyzer.Analyze(Objects(times), diagnostics);
    private static GlobalTripletSpeedAnalysis FromDeltas(params int[] deltas)
    {
        var times = new List<int> { 0 };
        foreach (int delta in deltas) times.Add(times[^1] + delta);
        return Analyze(times.ToArray());
    }
    private static HitObject[] Objects(params int[] times) => times.Select(time => Object(time)).ToArray();
    private static HitObject Object(int time, int type = 1) => new() { Time = time, Type = type };
}
