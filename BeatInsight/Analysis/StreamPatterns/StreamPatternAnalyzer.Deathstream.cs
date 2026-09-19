using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.StreamPatterns;

namespace BeatInsight.Analysis.StreamPatterns;

public static partial class StreamPatternAnalyzer
{
    private const int DeathstreamConfidenceFullLength = 64;

    private static DeathstreamPatternAnalysis AnalyzeDeathstreamCore(
        IReadOnlyList<HitObject> objects,
        DeathstreamPatternOptions options)
    {
        ArgumentNullException.ThrowIfNull(objects);
        Validate(options);

        DeathstreamPatternSequence[] runs = SegmentRapidCircleRuns(objects)
            .Select(run => new DeathstreamPatternSequence(
                run.StartIndex,
                run.EndIndex,
                run.StartTime,
                run.EndTime,
                run.Intervals,
                run.ObjectCount >= options.MinimumDeathstreamObjects))
            .ToArray();

        DeathstreamPatternSequence[] sequences = runs.Where(run => run.Qualified).ToArray();
        var involved = new HashSet<int>();
        foreach (DeathstreamPatternSequence sequence in sequences)
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
        double confidence = DeathstreamConfidence(
            sequences.Length,
            coverage,
            averageLength,
            averageConsistency,
            longest,
            options.MinimumDeathstreamObjects);

        var result = new DeathstreamPatternAnalysis(
            objects.Count,
            involved.Count,
            longest,
            averageLength,
            averageInterval,
            averageDuration,
            averageConsistency,
            confidence,
            runs);

        GameplayDebug.DeathstreamPattern(
            result,
            GameplayDebug.DeathstreamPatternEnabled,
            GameplayDebug.DeathstreamPatternDetailedEnabled);
        return result;
    }

    private static void Validate(DeathstreamPatternOptions options)
    {
        if (options.MinimumDeathstreamObjects < 2)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumDeathstreamObjects must be at least 2.");
    }

    private static double DeathstreamConfidence(
        int sequenceCount,
        double coverage,
        double averageLength,
        double averageConsistency,
        int longest,
        int minimumLength)
    {
        if (sequenceCount == 0)
            return 0;

        double countSignal = (double)sequenceCount / (sequenceCount + 1);
        double averageLengthSignal = DeathstreamLengthSignal(averageLength, minimumLength);
        double longestSignal = DeathstreamLengthSignal(longest, minimumLength);
        return Math.Clamp(
            (countSignal + coverage + averageLengthSignal + averageConsistency + longestSignal) / 5,
            0,
            1);
    }

    private static double DeathstreamLengthSignal(double length, int minimumLength)
    {
        int saturationSpan = Math.Max(1, DeathstreamConfidenceFullLength - minimumLength + 1);
        return Math.Clamp((length - minimumLength + 1) / saturationSpan, 0, 1);
    }
}
