namespace BeatInsight.Models.StreamPatterns;

/// <summary>Configurable object-count bounds for a short rapid Burst run.</summary>
public sealed record BurstPatternOptions(
    int MinimumBurstObjects = 3,
    int MaximumBurstObjects = 8);

/// <summary>
/// One complete maximal run of consecutive circles joined by rapid temporal links.
/// Unqualified runs are retained for diagnostics but never contribute to aggregate metrics.
/// </summary>
public sealed record BurstPatternSequence(
    int StartIndex,
    int EndIndex,
    int StartTime,
    int EndTime,
    IReadOnlyList<double> Intervals,
    bool Qualified)
{
    public int ObjectCount => EndIndex - StartIndex + 1;
    public int LinkCount => Intervals.Count;
    public double AverageInterval => LinkCount == 0 ? 0 : Intervals.Average();
    public double MinimumInterval => LinkCount == 0 ? 0 : Intervals.Min();
    public double MaximumInterval => LinkCount == 0 ? 0 : Intervals.Max();
    public double IntervalConsistency => MaximumInterval <= 0 ? 0 : MinimumInterval / MaximumInterval;
    public double Duration => EndTime - StartTime;
}

/// <summary>
/// Descriptive Burst-pattern result. Coverage counts unique objects belonging to qualified runs.
/// This model is independent from gameplay identity, scores, rating, persistence and UI.
/// </summary>
public sealed record BurstPatternAnalysis(
    int TotalObjects,
    int BurstObjectCount,
    int LongestBurst,
    double AverageBurstLength,
    double AverageInterval,
    double AverageDuration,
    double AverageIntervalConsistency,
    double Confidence,
    IReadOnlyList<BurstPatternSequence> Runs)
{
    public IReadOnlyList<BurstPatternSequence> Sequences =>
        Runs.Where(run => run.Qualified).ToArray();

    public int BurstSequenceCount => Sequences.Count;
    public double Coverage => TotalObjects == 0 ? 0 : (double)BurstObjectCount / TotalObjects;
}
