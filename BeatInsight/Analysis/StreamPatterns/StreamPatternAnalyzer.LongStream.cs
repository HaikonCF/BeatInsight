using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.StreamPatterns;

namespace BeatInsight.Analysis.StreamPatterns;

public static partial class StreamPatternAnalyzer
{
    private const int LongStreamConfidenceFullLength = 32;

    private static LongStreamPatternAnalysis AnalyzeLongStreamCore(
        IReadOnlyList<HitObject> objects,
        LongStreamPatternOptions options)
    {
        ArgumentNullException.ThrowIfNull(objects);
        Validate(options);

        LongStreamPatternSequence[] runs = SegmentRapidCircleRuns(objects)
            .Select(run => new LongStreamPatternSequence(
                run.StartIndex,
                run.EndIndex,
                run.StartTime,
                run.EndTime,
                run.Intervals,
                run.ObjectCount >= options.MinimumLongStreamObjects
                    && run.ObjectCount <= options.MaximumLongStreamObjects))
            .ToArray();

        LongStreamPatternSequence[] sequences = runs.Where(run => run.Qualified).ToArray();
        var involved = new HashSet<int>();
        foreach (LongStreamPatternSequence sequence in sequences)
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
        double confidence = LongStreamConfidence(
            sequences.Length, coverage, averageLength, averageConsistency, longest);

        var result = new LongStreamPatternAnalysis(
            objects.Count,
            involved.Count,
            longest,
            averageLength,
            averageInterval,
            averageDuration,
            averageConsistency,
            confidence,
            runs);

        GameplayDebug.LongStreamPattern(
            result,
            GameplayDebug.LongStreamPatternEnabled,
            GameplayDebug.LongStreamPatternDetailedEnabled);
        return result;
    }

    private static void Validate(LongStreamPatternOptions options)
    {
        if (options.MinimumLongStreamObjects < 2)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumLongStreamObjects must be at least 2.");
        if (options.MaximumLongStreamObjects < options.MinimumLongStreamObjects)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumLongStreamObjects must not be below the minimum.");
    }

    private static double LongStreamConfidence(
        int sequenceCount,
        double coverage,
        double averageLength,
        double averageConsistency,
        int longest)
    {
        if (sequenceCount == 0)
            return 0;

        double countSignal = (double)sequenceCount / (sequenceCount + 1);
        double averageLengthSignal = Math.Clamp(averageLength / LongStreamConfidenceFullLength, 0, 1);
        double longestSignal = Math.Clamp((double)longest / LongStreamConfidenceFullLength, 0, 1);
        return Math.Clamp(
            (countSignal + coverage + averageLengthSignal + averageConsistency + longestSignal) / 5,
            0,
            1);
    }
}
