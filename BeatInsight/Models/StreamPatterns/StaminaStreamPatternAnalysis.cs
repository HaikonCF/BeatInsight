namespace BeatInsight.Models.StreamPatterns;

/// <summary>Configurable V1 thresholds for repeated rapid-run pressure.</summary>
public sealed record StaminaStreamPatternOptions(
    int MinimumStaminaRunObjects = 9,
    int MinimumStaminaRuns = 2,
    int MinimumStaminaObjects = 24,
    double MaximumRecoveryGap = 1500);

/// <summary>A maximal rapid run eligible to contribute to a Stamina candidate.</summary>
public sealed record StaminaRapidRun(
    int StartIndex,
    int EndIndex,
    int StartTime,
    int EndTime,
    IReadOnlyList<double> Intervals)
{
    public int ObjectCount => EndIndex - StartIndex + 1;
    public int LinkCount => Intervals.Count;
    public double Duration => EndTime - StartTime;
    public double AverageInterval => LinkCount == 0 ? 0 : Intervals.Average();
    public double IntervalConsistency => LinkCount == 0 || Intervals.Max() <= 0
        ? 0
        : Intervals.Min() / Intervals.Max();
}

/// <summary>A group of eligible rapid runs separated by limited recovery.</summary>
public sealed record StaminaStreamPatternSequence(
    int StartIndex,
    int EndIndex,
    int StartTime,
    int EndTime,
    IReadOnlyList<StaminaRapidRun> Runs,
    IReadOnlyList<double> RecoveryGaps,
    bool Qualified,
    string Reason)
{
    public int RunCount => Runs.Count;
    public int TotalRapidObjects => Runs.Sum(run => run.ObjectCount);
    public double Duration => EndTime - StartTime;
    public double ActiveRapidDuration => Runs.Sum(run => run.Duration);
    public double TotalRecoveryDuration => RecoveryGaps.Sum();
    public double AverageRecoveryGap => RecoveryGaps.Count == 0 ? 0 : RecoveryGaps.Average();
    public double MaximumRecoveryGapObserved => RecoveryGaps.Count == 0 ? 0 : RecoveryGaps.Max();
    public double ActiveRatio => Duration <= 0
        ? 0
        : Math.Clamp(ActiveRapidDuration / Duration, 0, 1);
    public double AverageInterval
    {
        get
        {
            double[] intervals = Runs.SelectMany(run => run.Intervals).ToArray();
            return intervals.Length == 0 ? 0 : intervals.Average();
        }
    }
    public double AverageIntervalConsistency => Runs.Count == 0
        ? 0
        : Runs.Average(run => run.IntervalConsistency);
}

/// <summary>
/// Descriptive repeated Stream-pressure result. Recovery objects never contribute to Coverage.
/// This model is independent from gameplay identity, scores, rating, persistence and UI.
/// </summary>
public sealed record StaminaStreamPatternAnalysis(
    int TotalObjects,
    int EligibleRapidRunCount,
    int StaminaObjectCount,
    int LongestStaminaObjectCount,
    double LongestStaminaDuration,
    double AverageRunsPerSequence,
    double AverageObjectsPerSequence,
    double AverageSequenceDuration,
    double AverageRecoveryGap,
    double AverageActiveRatio,
    double AverageInterval,
    double AverageIntervalConsistency,
    double Confidence,
    IReadOnlyList<StaminaStreamPatternSequence> Candidates)
{
    public IReadOnlyList<StaminaStreamPatternSequence> Sequences =>
        Candidates.Where(candidate => candidate.Qualified).ToArray();

    public int StaminaSequenceCount => Sequences.Count;
    public double Coverage => TotalObjects == 0 ? 0 : (double)StaminaObjectCount / TotalObjects;
}
