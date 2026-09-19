namespace BeatInsight.Models.StreamPatterns;

/// <summary>Configurable minimum object count for a very long rapid Stream run.</summary>
public sealed record DeathstreamPatternOptions(
    int MinimumDeathstreamObjects = 33);

/// <summary>
/// One complete maximal run of consecutive circles joined by rapid temporal links.
/// Unqualified runs are retained for diagnostics but never contribute to aggregate metrics.
/// </summary>
public sealed record DeathstreamPatternSequence(
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
/// Descriptive Deathstream result. Coverage counts unique objects belonging to qualified runs.
/// This model describes local runs only and contains no stamina or recovery semantics.
/// </summary>
public sealed record DeathstreamPatternAnalysis(
    int TotalObjects,
    int DeathstreamObjectCount,
    int LongestDeathstream,
    double AverageDeathstreamLength,
    double AverageInterval,
    double AverageDuration,
    double AverageIntervalConsistency,
    double Confidence,
    IReadOnlyList<DeathstreamPatternSequence> Runs)
{
    public IReadOnlyList<DeathstreamPatternSequence> Sequences =>
        Runs.Where(run => run.Qualified).ToArray();

    public int DeathstreamSequenceCount => Sequences.Count;
    public double Coverage => TotalObjects == 0 ? 0 : (double)DeathstreamObjectCount / TotalObjects;
}
