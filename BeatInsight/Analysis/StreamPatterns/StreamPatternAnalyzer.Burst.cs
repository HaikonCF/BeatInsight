using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.StreamPatterns;

namespace BeatInsight.Analysis.StreamPatterns;

public static partial class StreamPatternAnalyzer
{
    private const int BurstConfidenceFullLength = 8;

    private static BurstPatternAnalysis AnalyzeBurstCore(
        IReadOnlyList<HitObject> objects,
        BurstPatternOptions options)
    {
        ArgumentNullException.ThrowIfNull(objects);
        Validate(options);

        BurstPatternSequence[] runs = SegmentRapidCircleRuns(objects)
            .Select(run => new BurstPatternSequence(
                run.StartIndex,
                run.EndIndex,
                run.StartTime,
                run.EndTime,
                run.Intervals,
                run.ObjectCount >= options.MinimumBurstObjects
                    && run.ObjectCount <= options.MaximumBurstObjects))
            .ToArray();

        BurstPatternSequence[] sequences = runs.Where(run => run.Qualified).ToArray();
        var involved = new HashSet<int>();
        foreach (BurstPatternSequence sequence in sequences)
            for (int index = sequence.StartIndex; index <= sequence.EndIndex; index++)
                involved.Add(index);

        int longest = sequences.Length == 0 ? 0 : sequences.Max(sequence => sequence.ObjectCount);
        double averageLength = sequences.Length == 0 ? 0 : sequences.Average(sequence => sequence.ObjectCount);
        double[] qualifiedIntervals = sequences.SelectMany(sequence => sequence.Intervals).ToArray();
        double averageInterval = qualifiedIntervals.Length == 0 ? 0 : qualifiedIntervals.Average();
        double averageDuration = sequences.Length == 0 ? 0 : sequences.Average(sequence => sequence.Duration);
        double averageConsistency = sequences.Length == 0
            ? 0
            : sequences.Average(sequence => sequence.IntervalConsistency);
        double coverage = objects.Count == 0 ? 0 : (double)involved.Count / objects.Count;
        double confidence = Confidence(
            sequences.Length, coverage, averageLength, averageConsistency, longest);

        var result = new BurstPatternAnalysis(
            objects.Count,
            involved.Count,
            longest,
            averageLength,
            averageInterval,
            averageDuration,
            averageConsistency,
            confidence,
            runs);

        GameplayDebug.BurstPattern(
            result,
            GameplayDebug.BurstPatternEnabled,
            GameplayDebug.BurstPatternDetailedEnabled);
        return result;
    }

    private static void Validate(BurstPatternOptions options)
    {
        if (options.MinimumBurstObjects < 2)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumBurstObjects must be at least 2.");
        if (options.MaximumBurstObjects < options.MinimumBurstObjects)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumBurstObjects must not be below the minimum.");
    }

    private static double Confidence(
        int sequenceCount,
        double coverage,
        double averageLength,
        double averageConsistency,
        int longest)
    {
        if (sequenceCount == 0)
            return 0;

        double countSignal = (double)sequenceCount / (sequenceCount + 1);
        double averageLengthSignal = Math.Clamp(averageLength / BurstConfidenceFullLength, 0, 1);
        double longestSignal = Math.Clamp((double)longest / BurstConfidenceFullLength, 0, 1);
        return Math.Clamp(
            (countSignal + coverage + averageLengthSignal + averageConsistency + longestSignal) / 5,
            0,
            1);
    }
}
