using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.StreamPatterns;

namespace BeatInsight.Analysis.StreamPatterns;

public static partial class StreamPatternAnalyzer
{
    private const int ShortStreamConfidenceFullLength = 16;

    private static ShortStreamPatternAnalysis AnalyzeShortStreamCore(
        IReadOnlyList<HitObject> objects,
        ShortStreamPatternOptions options)
    {
        ArgumentNullException.ThrowIfNull(objects);
        Validate(options);

        ShortStreamPatternSequence[] runs = SegmentRapidCircleRuns(objects)
            .Select(run => new ShortStreamPatternSequence(
                run.StartIndex,
                run.EndIndex,
                run.StartTime,
                run.EndTime,
                run.Intervals,
                run.ObjectCount >= options.MinimumShortStreamObjects
                    && run.ObjectCount <= options.MaximumShortStreamObjects))
            .ToArray();

        ShortStreamPatternSequence[] sequences = runs.Where(run => run.Qualified).ToArray();
        var involved = new HashSet<int>();
        foreach (ShortStreamPatternSequence sequence in sequences)
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
        double confidence = ShortStreamConfidence(
            sequences.Length, coverage, averageLength, averageConsistency, longest);

        var result = new ShortStreamPatternAnalysis(
            objects.Count,
            involved.Count,
            longest,
            averageLength,
            averageInterval,
            averageDuration,
            averageConsistency,
            confidence,
            runs);

        GameplayDebug.ShortStreamPattern(
            result,
            GameplayDebug.ShortStreamPatternEnabled,
            GameplayDebug.ShortStreamPatternDetailedEnabled);
        return result;
    }

    private static void Validate(ShortStreamPatternOptions options)
    {
        if (options.MinimumShortStreamObjects < 2)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumShortStreamObjects must be at least 2.");
        if (options.MaximumShortStreamObjects < options.MinimumShortStreamObjects)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumShortStreamObjects must not be below the minimum.");
    }

    private static double ShortStreamConfidence(
        int sequenceCount,
        double coverage,
        double averageLength,
        double averageConsistency,
        int longest)
    {
        if (sequenceCount == 0)
            return 0;

        double countSignal = (double)sequenceCount / (sequenceCount + 1);
        double averageLengthSignal = Math.Clamp(averageLength / ShortStreamConfidenceFullLength, 0, 1);
        double longestSignal = Math.Clamp((double)longest / ShortStreamConfidenceFullLength, 0, 1);
        return Math.Clamp(
            (countSignal + coverage + averageLengthSignal + averageConsistency + longestSignal) / 5,
            0,
            1);
    }
}
