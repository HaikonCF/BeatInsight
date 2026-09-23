using BeatInsight.Analysis;
using BeatInsight.Models;

namespace BeatInsight.Tests.Analysis;

public sealed class BeatmapBpmV2AnalyzerTests
{
    private static TimingPoint Tempo(double time, double length, bool uninherited = true) =>
        new() { Time = time, BeatLength = length, Uninherited = uninherited };

    [Fact]
    public void ConstantAndRepeatedTempoHasOneBpm()
    {
        BeatmapBpmV2 bpm = BeatmapBpmV2Analyzer.Analyze(
            [Tempo(-1000, 500), Tempo(5000, 500)], 1000, 10000);

        Assert.Equal(120, bpm.PrimaryBpm);
        Assert.Equal(120, bpm.MinBpm);
        Assert.Equal(120, bpm.MaxBpm);
        Assert.False(bpm.IsVariableBpm);
    }

    [Fact]
    public void DominantTempoSumsRepeatedIntervalsByDurationAndIgnoresInheritedPoints()
    {
        BeatmapBpmV2 bpm = BeatmapBpmV2Analyzer.Analyze(
            [Tempo(0, 500), Tempo(3000, 400), Tempo(4000, -50, false),
             Tempo(6000, 500), Tempo(7000, 400), Tempo(11000, 600)],
            1000, 12000);

        Assert.Equal(150, bpm.PrimaryBpm);
        Assert.Equal(100, bpm.MinBpm);
        Assert.Equal(150, bpm.MaxBpm);
        Assert.True(bpm.IsVariableBpm);
    }

    [Fact]
    public void TempoBeforeFirstObjectIsClippedAndTempoAfterLastObjectIsExcluded()
    {
        BeatmapBpmV2 bpm = BeatmapBpmV2Analyzer.Analyze(
            [Tempo(-10000, 1000), Tempo(0, 500), Tempo(2000, 400), Tempo(20000, 300)],
            5000, 10000);

        Assert.Equal(150, bpm.PrimaryBpm);
        Assert.Equal(150, bpm.MinBpm);
        Assert.Equal(150, bpm.MaxBpm);
        Assert.False(bpm.IsVariableBpm);
    }

    [Fact]
    public void BriefTempoChangeAffectsRangeButNotDominantTempo()
    {
        BeatmapBpmV2 bpm = BeatmapBpmV2Analyzer.Analyze(
            [Tempo(0, 500), Tempo(5000, 400), Tempo(5100, 500)], 0, 10000);

        Assert.Equal(120, bpm.PrimaryBpm);
        Assert.Equal(120, bpm.MinBpm);
        Assert.Equal(150, bpm.MaxBpm);
        Assert.True(bpm.IsVariableBpm);
    }

    [Fact]
    public void InvalidBeatLengthsAndNonFiniteTimingAreIgnored()
    {
        BeatmapBpmV2 bpm = BeatmapBpmV2Analyzer.Analyze(
            [Tempo(0, 0), Tempo(0, -100), Tempo(0, double.NaN),
             Tempo(double.PositiveInfinity, 200), Tempo(0, 500)], 0, 10000);

        Assert.Equal(120, bpm.PrimaryBpm);
        Assert.False(bpm.IsVariableBpm);
        Assert.Equal(BeatmapBpmV2.Unavailable,
            BeatmapBpmV2Analyzer.Analyze([Tempo(0, 0), Tempo(0, double.NaN)], 0, 1000));
    }

    [Fact]
    public void TinyBeatLengthNoiseDoesNotCreateVariableTempo()
    {
        BeatmapBpmV2 bpm = BeatmapBpmV2Analyzer.Analyze(
            [Tempo(0, 500), Tempo(5000, 500.0000001)], 0, 10000);

        Assert.Equal(120, bpm.PrimaryBpm);
        Assert.False(bpm.IsVariableBpm);
    }

    [Fact]
    public void SingleObjectUsesActiveTempo()
    {
        BeatmapBpmV2 bpm = BeatmapBpmV2Analyzer.Analyze([Tempo(0, 500)], 1000, 1000);

        Assert.Equal(120, bpm.PrimaryBpm);
        Assert.False(bpm.IsVariableBpm);
    }
    [Fact]
    public void FutureTempoIsNotAppliedBeforeItsActivation()
    {
        Assert.Equal(BeatmapBpmV2.Unavailable,
            BeatmapBpmV2Analyzer.Analyze([Tempo(2000, 500)], 0, 1000));
        Assert.Equal(BeatmapBpmV2.Unavailable,
            BeatmapBpmV2Analyzer.Analyze([], 0, 1000));
        var bpm = BeatmapBpmV2Analyzer.Analyze([Tempo(900, 500)], 0, 1000);
        Assert.Equal(120, bpm.PrimaryBpm);
    }

    [Fact]
    public void ZeroDurationDoesNotIncludeChangesAfterTheObject()
    {
        var bpm = BeatmapBpmV2Analyzer.Analyze(
            [Tempo(0, 500), Tempo(1000.5, 400)], 1000, 1000);
        Assert.Equal(120, bpm.PrimaryBpm);
        Assert.False(bpm.IsVariableBpm);
    }

    [Fact]
    public void EqualDurationsPreferFirstActiveTempoAndEndBoundaryIsExcluded()
    {
        var bpm = BeatmapBpmV2Analyzer.Analyze(
            [Tempo(1000, 400), Tempo(0, 500), Tempo(2000, 300)], 0, 2000);
        Assert.Equal(120, bpm.PrimaryBpm);
        Assert.Equal(120, bpm.MinBpm);
        Assert.Equal(150, bpm.MaxBpm);
    }

    [Fact]
    public void SimultaneousPointsUseLastTempoWithoutZeroDurationExtremes()
    {
        var bpm = BeatmapBpmV2Analyzer.Analyze(
            [Tempo(0, 500), Tempo(1000, 100), Tempo(1000, 400)], 0, 2000);
        Assert.Equal(150, bpm.MaxBpm);
    }
}
