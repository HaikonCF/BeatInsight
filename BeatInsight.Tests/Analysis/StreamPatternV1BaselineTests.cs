using BeatInsight.Analysis;
using BeatInsight.Models.StreamPatterns;

namespace BeatInsight.Tests.Analysis;

/// <summary>Locks the official production configuration of Stream Pattern V1.</summary>
public sealed class StreamPatternV1BaselineTests
{
    [Fact]
    public void ProductionDefaultsMatchFrozenV1Baseline()
    {
        Assert.Equal(new BurstPatternOptions(3, 8), new BurstPatternOptions());
        Assert.Equal(new ShortStreamPatternOptions(9, 16), new ShortStreamPatternOptions());
        Assert.Equal(new LongStreamPatternOptions(17, 32), new LongStreamPatternOptions());
        Assert.Equal(new DeathstreamPatternOptions(33), new DeathstreamPatternOptions());

        Assert.Equal(
            new StaminaStreamPatternOptions(9, 2, 24, 1500),
            new StaminaStreamPatternOptions());
        Assert.Equal(
            new IrregularStreamPatternOptions(6, 12, 0.10, 0.30, 2),
            new IrregularStreamPatternOptions());
        Assert.Equal(
            new SpeedStreamPatternOptions(6, 90),
            new SpeedStreamPatternOptions());
        Assert.Equal(
            new FlowStreamPatternOptions(6, 15, 110, 18, 0.70, 0.75),
            new FlowStreamPatternOptions());
        Assert.Equal(
            new AlternatingStreamPatternOptions(6, 20, 160, 20, 0.70, 0.75),
            new AlternatingStreamPatternOptions());
    }

    [Theory]
    [InlineData(29.999, false)]
    [InlineData(30, true)]
    [InlineData(130, true)]
    [InlineData(130.001, false)]
    public void CommonRapidRunTimingBoundariesRemainInclusive(double interval, bool expected)
    {
        Assert.Equal(expected, GameplayAnalyzer.IsStreamTimingInterval(interval));
    }
}
