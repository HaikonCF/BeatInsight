namespace BeatInsight.Models.StreamPatterns;

/// <summary>Configurable V1 thresholds for sustained absolute tapping speed.</summary>
public sealed record SpeedStreamPatternOptions(
    int MinimumSpeedStreamObjects = 6,
    double MaximumAverageIntervalForSpeedStream = 90);

/// <summary>One complete maximal rapid run evaluated for sustained tapping speed.</summary>
public sealed record SpeedStreamPatternSequence(
    int StartIndex,
    int EndIndex,
    int StartTime,
    int EndTime,
    IReadOnlyList<double> Intervals,
    bool Qualified,
    string Reason)
{
    private const double SlowBoundary = 130;
    private const double FastBoundary = 60;

    public int ObjectCount => EndIndex - StartIndex + 1;
    public int LinkCount => Intervals.Count;
    public double Duration => EndTime - StartTime;
    public double AverageInterval => LinkCount == 0 ? 0 : Intervals.Average();
    public double MinimumInterval => LinkCount == 0 ? 0 : Intervals.Min();
    public double MaximumInterval => LinkCount == 0 ? 0 : Intervals.Max();
    public double IntervalConsistency => MaximumInterval <= 0 ? 0 : MinimumInterval / MaximumInterval;

    /// <summary>Descriptive osu! standard 1/4-stream BPM, not the beatmap's musical BPM.</summary>
    public double StreamBpm => AverageInterval <= 0 ? 0 : 60000 / (AverageInterval * 4);

    public double SpeedIntensity => Math.Clamp(
        (SlowBoundary - AverageInterval) / (SlowBoundary - FastBoundary),
        0,
        1);
}

/// <summary>
/// Descriptive sustained tapping-speed result, independent from gameplay Speed and beatmap BPM.
/// Coverage counts unique objects belonging to qualified maximal rapid runs.
/// </summary>
public sealed record SpeedStreamPatternAnalysis(
    int TotalObjects,
    int RapidRunCount,
    int SpeedStreamObjectCount,
    int LongestSpeedStream,
    double AverageSpeedStreamLength,
    double AverageInterval,
    double FastestAverageInterval,
    double AverageStreamBpm,
    double MaximumStreamBpm,
    double AverageSpeedIntensity,
    double AverageIntervalConsistency,
    double Confidence,
    IReadOnlyList<SpeedStreamPatternSequence> Runs)
{
    public IReadOnlyList<SpeedStreamPatternSequence> Sequences =>
        Runs.Where(run => run.Qualified).ToArray();

    public int SpeedStreamSequenceCount => Sequences.Count;
    public double Coverage => TotalObjects == 0 ? 0 : (double)SpeedStreamObjectCount / TotalObjects;
}
