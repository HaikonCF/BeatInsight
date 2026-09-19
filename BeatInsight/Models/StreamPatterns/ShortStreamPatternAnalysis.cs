namespace BeatInsight.Models.StreamPatterns;

/// <summary>Configurable object-count bounds for an intermediate rapid Stream run.</summary>
public sealed record ShortStreamPatternOptions(
    int MinimumShortStreamObjects = 9,
    int MaximumShortStreamObjects = 16);

/// <summary>
/// One complete maximal run of consecutive circles joined by rapid temporal links.
/// Unqualified runs are retained for diagnostics but never contribute to aggregate metrics.
/// </summary>
public sealed record ShortStreamPatternSequence(
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
/// Descriptive Short Stream result. Coverage counts unique objects belonging to qualified runs.
/// This model is independent from gameplay identity, scores, rating, persistence and UI.
/// </summary>
public sealed record ShortStreamPatternAnalysis(
    int TotalObjects,
    int ShortStreamObjectCount,
    int LongestShortStream,
    double AverageShortStreamLength,
    double AverageInterval,
    double AverageDuration,
    double AverageIntervalConsistency,
    double Confidence,
    IReadOnlyList<ShortStreamPatternSequence> Runs)
{
    public IReadOnlyList<ShortStreamPatternSequence> Sequences =>
        Runs.Where(run => run.Qualified).ToArray();

    public int ShortStreamSequenceCount => Sequences.Count;
    public double Coverage => TotalObjects == 0 ? 0 : (double)ShortStreamObjectCount / TotalObjects;
}
