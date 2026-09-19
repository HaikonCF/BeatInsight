namespace BeatInsight.Models.StreamPatterns;

/// <summary>Configurable thresholds for repeated temporal irregularity inside rapid runs.</summary>
public sealed record IrregularStreamPatternOptions(
    int MinimumIrregularStreamObjects = 6,
    double MinimumSignificantIntervalDelta = 12,
    double MinimumCoefficientOfVariation = 0.10,
    double MinimumSignificantDeltaRatio = 0.30,
    int MinimumDistinctIrregularEvents = 2)
{
    /// <summary>
    /// Deviation from the run median required for an interval to be atypical.
    /// The legacy constructor name is retained for source compatibility; significant-delta
    /// metrics and their ratio remain diagnostic only.
    /// </summary>
    public double MinimumAtypicalIntervalDeviation => MinimumSignificantIntervalDelta;
}

/// <summary>One complete maximal rapid run evaluated for internal temporal irregularity.</summary>
public sealed record IrregularStreamPatternSequence(
    int StartIndex,
    int EndIndex,
    int StartTime,
    int EndTime,
    IReadOnlyList<double> Intervals,
    double MinimumSignificantIntervalDelta,
    bool Qualified,
    string Reason)
{
    public int ObjectCount => EndIndex - StartIndex + 1;
    public int LinkCount => Intervals.Count;
    public double Duration => EndTime - StartTime;
    public double AverageInterval => LinkCount == 0 ? 0 : Intervals.Average();
    public double MinimumInterval => LinkCount == 0 ? 0 : Intervals.Min();
    public double MaximumInterval => LinkCount == 0 ? 0 : Intervals.Max();
    public double IntervalRange => MaximumInterval - MinimumInterval;
    public double StandardDeviation => LinkCount == 0
        ? 0
        : Math.Sqrt(Intervals.Average(interval => Math.Pow(interval - AverageInterval, 2)));
    public double CoefficientOfVariation => AverageInterval <= 0
        ? 0
        : StandardDeviation / AverageInterval;
    public IReadOnlyList<double> IntervalDeltas => Enumerable.Range(0, Math.Max(0, LinkCount - 1))
        .Select(index => Math.Abs(Intervals[index + 1] - Intervals[index]))
        .ToArray();
    public double AverageIntervalDelta => IntervalDeltas.Count == 0 ? 0 : IntervalDeltas.Average();
    public double MaximumIntervalDelta => IntervalDeltas.Count == 0 ? 0 : IntervalDeltas.Max();
    public int SignificantDeltaCount => IntervalDeltas.Count(
        delta => delta >= MinimumSignificantIntervalDelta);
    public double SignificantDeltaRatio => IntervalDeltas.Count == 0
        ? 0
        : (double)SignificantDeltaCount / IntervalDeltas.Count;
    public double MedianInterval
    {
        get
        {
            if (LinkCount == 0) return 0;
            double[] sorted = Intervals.OrderBy(interval => interval).ToArray();
            int middle = sorted.Length / 2;
            return sorted.Length % 2 == 0
                ? (sorted[middle - 1] + sorted[middle]) / 2
                : sorted[middle];
        }
    }
    public IReadOnlyList<double> AtypicalIntervalDeviations => Intervals
        .Select(interval => Math.Abs(interval - MedianInterval))
        .ToArray();
    public IReadOnlyList<bool> AtypicalIntervalMask => AtypicalIntervalDeviations
        .Select(deviation => deviation >= MinimumSignificantIntervalDelta)
        .ToArray();
    public int AtypicalIntervalCount => AtypicalIntervalMask.Count(atypical => atypical);
    public double AtypicalIntervalRatio => LinkCount == 0
        ? 0
        : (double)AtypicalIntervalCount / LinkCount;
    public int DistinctIrregularEventCount
    {
        get
        {
            IReadOnlyList<bool> mask = AtypicalIntervalMask;
            return mask.Select((atypical, index) => atypical && (index == 0 || !mask[index - 1]))
                .Count(startsEvent => startsEvent);
        }
    }
    public double IrregularityScore
    {
        get
        {
            double variationSignal = Math.Clamp(CoefficientOfVariation / 0.30, 0, 1);
            double eventSignal = Math.Clamp(DistinctIrregularEventCount / 3.0, 0, 1);
            double atypicalSpreadSignal = Math.Clamp(AtypicalIntervalRatio / 0.50, 0, 1);
            return Math.Clamp((variationSignal + eventSignal + atypicalSpreadSignal) / 3, 0, 1);
        }
    }
}

/// <summary>
/// Descriptive temporal-irregularity result. It is independent from Tech and spatial analysis.
/// Coverage counts unique objects belonging to qualified maximal rapid runs.
/// </summary>
public sealed record IrregularStreamPatternAnalysis(
    int TotalObjects,
    int RapidRunCount,
    int IrregularStreamObjectCount,
    int LongestIrregularStream,
    double AverageIrregularStreamLength,
    double AverageInterval,
    double AverageCoefficientOfVariation,
    double AverageIntervalDelta,
    double AverageSignificantDeltaRatio,
    double AverageDistinctIrregularEvents,
    double AverageAtypicalIntervalRatio,
    double AverageIrregularityScore,
    double Confidence,
    IReadOnlyList<IrregularStreamPatternSequence> Runs)
{
    public IReadOnlyList<IrregularStreamPatternSequence> Sequences =>
        Runs.Where(run => run.Qualified).ToArray();

    public int IrregularStreamSequenceCount => Sequences.Count;
    public double Coverage => TotalObjects == 0 ? 0 : (double)IrregularStreamObjectCount / TotalObjects;
}
