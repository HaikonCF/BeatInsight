using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.StreamPatterns;

namespace BeatInsight.Analysis.StreamPatterns;

public static partial class StreamPatternAnalyzer
{
    private const double SpeedStreamConfidenceFullLength = 16;

    private static SpeedStreamPatternAnalysis AnalyzeSpeedStreamCore(
        IReadOnlyList<HitObject> objects,
        SpeedStreamPatternOptions options)
    {
        ArgumentNullException.ThrowIfNull(objects);
        Validate(options);

        SpeedStreamPatternSequence[] runs = SegmentRapidCircleRuns(objects)
            .Select(run => CreateSpeedStreamSequence(run, options))
            .ToArray();
        SpeedStreamPatternSequence[] sequences = runs.Where(run => run.Qualified).ToArray();

        var involved = new HashSet<int>();
        foreach (SpeedStreamPatternSequence sequence in sequences)
            for (int index = sequence.StartIndex; index <= sequence.EndIndex; index++)
                involved.Add(index);

        int longest = sequences.Length == 0 ? 0 : sequences.Max(sequence => sequence.ObjectCount);
        double averageLength = sequences.Length == 0 ? 0 : sequences.Average(sequence => sequence.ObjectCount);
        double[] intervals = sequences.SelectMany(sequence => sequence.Intervals).ToArray();
        double averageInterval = intervals.Length == 0 ? 0 : intervals.Average();
        double fastestAverageInterval = sequences.Length == 0
            ? 0
            : sequences.Min(sequence => sequence.AverageInterval);
        double averageBpm = sequences.Length == 0 ? 0 : sequences.Average(sequence => sequence.StreamBpm);
        double maximumBpm = sequences.Length == 0 ? 0 : sequences.Max(sequence => sequence.StreamBpm);
        double averageIntensity = sequences.Length == 0
            ? 0
            : sequences.Average(sequence => sequence.SpeedIntensity);
        double averageConsistency = sequences.Length == 0
            ? 0
            : sequences.Average(sequence => sequence.IntervalConsistency);
        double coverage = objects.Count == 0 ? 0 : (double)involved.Count / objects.Count;
        double confidence = SpeedStreamConfidence(
            sequences.Length,
            coverage,
            averageLength,
            averageIntensity,
            averageConsistency);

        var result = new SpeedStreamPatternAnalysis(
            objects.Count,
            runs.Length,
            involved.Count,
            longest,
            averageLength,
            averageInterval,
            fastestAverageInterval,
            averageBpm,
            maximumBpm,
            averageIntensity,
            averageConsistency,
            confidence,
            runs);

        GameplayDebug.SpeedStreamPattern(
            result,
            GameplayDebug.SpeedStreamPatternEnabled,
            GameplayDebug.SpeedStreamPatternDetailedEnabled);
        return result;
    }

    private static SpeedStreamPatternSequence CreateSpeedStreamSequence(
        RapidStreamRun run,
        SpeedStreamPatternOptions options)
    {
        string reason = run.ObjectCount < options.MinimumSpeedStreamObjects
            ? "TooShort"
            : run.AverageInterval > options.MaximumAverageIntervalForSpeedStream
                ? "TooSlow"
                : "Qualified";

        return new SpeedStreamPatternSequence(
            run.StartIndex,
            run.EndIndex,
            run.StartTime,
            run.EndTime,
            run.Intervals,
            reason == "Qualified",
            reason);
    }

    private static void Validate(SpeedStreamPatternOptions options)
    {
        if (options.MinimumSpeedStreamObjects < 2)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumSpeedStreamObjects must be at least 2.");
        if (options.MaximumAverageIntervalForSpeedStream <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumAverageIntervalForSpeedStream must be positive.");
    }

    private static double SpeedStreamConfidence(
        int sequenceCount,
        double coverage,
        double averageLength,
        double averageSpeedIntensity,
        double averageIntervalConsistency)
    {
        if (sequenceCount == 0)
            return 0;

        double sequenceSignal = (double)sequenceCount / (sequenceCount + 1);
        double lengthSignal = Math.Clamp(averageLength / SpeedStreamConfidenceFullLength, 0, 1);
        return Math.Clamp(
            (sequenceSignal
                + coverage
                + lengthSignal
                + averageSpeedIntensity
                + averageIntervalConsistency) / 5,
            0,
            1);
    }
}
