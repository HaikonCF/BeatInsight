using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.StreamPatterns;

namespace BeatInsight.Analysis.StreamPatterns;

public static partial class StreamPatternAnalyzer
{
    private static StaminaStreamPatternAnalysis AnalyzeStaminaStreamCore(
        IReadOnlyList<HitObject> objects,
        StaminaStreamPatternOptions options)
    {
        ArgumentNullException.ThrowIfNull(objects);
        Validate(options);

        StaminaRapidRun[] eligibleRuns = SegmentRapidCircleRuns(objects)
            .Where(run => run.ObjectCount >= options.MinimumStaminaRunObjects)
            .Select(run => new StaminaRapidRun(
                run.StartIndex,
                run.EndIndex,
                run.StartTime,
                run.EndTime,
                run.Intervals))
            .ToArray();

        var candidates = new List<StaminaStreamPatternSequence>();
        var currentRuns = new List<StaminaRapidRun>();
        var currentGaps = new List<double>();

        foreach (StaminaRapidRun run in eligibleRuns)
        {
            if (currentRuns.Count == 0)
            {
                currentRuns.Add(run);
                continue;
            }

            double recoveryGap = run.StartTime - currentRuns[^1].EndTime;
            if (recoveryGap >= 0 && recoveryGap <= options.MaximumRecoveryGap)
            {
                currentGaps.Add(recoveryGap);
                currentRuns.Add(run);
                continue;
            }

            candidates.Add(CreateStaminaCandidate(currentRuns, currentGaps, options));
            currentRuns = [run];
            currentGaps = [];
        }

        if (currentRuns.Count > 0)
            candidates.Add(CreateStaminaCandidate(currentRuns, currentGaps, options));

        StaminaStreamPatternSequence[] sequences = candidates.Where(candidate => candidate.Qualified).ToArray();
        var involved = new HashSet<int>();
        foreach (StaminaStreamPatternSequence sequence in sequences)
            foreach (StaminaRapidRun run in sequence.Runs)
                for (int index = run.StartIndex; index <= run.EndIndex; index++)
                    involved.Add(index);

        int longestObjects = sequences.Length == 0 ? 0 : sequences.Max(sequence => sequence.TotalRapidObjects);
        double longestDuration = sequences.Length == 0 ? 0 : sequences.Max(sequence => sequence.Duration);
        double averageRuns = sequences.Length == 0 ? 0 : sequences.Average(sequence => sequence.RunCount);
        double averageObjects = sequences.Length == 0 ? 0 : sequences.Average(sequence => sequence.TotalRapidObjects);
        double averageDuration = sequences.Length == 0 ? 0 : sequences.Average(sequence => sequence.Duration);
        double[] recoveryGaps = sequences.SelectMany(sequence => sequence.RecoveryGaps).ToArray();
        double averageRecovery = recoveryGaps.Length == 0 ? 0 : recoveryGaps.Average();
        double averageActiveRatio = sequences.Length == 0 ? 0 : sequences.Average(sequence => sequence.ActiveRatio);
        double[] intervals = sequences.SelectMany(sequence => sequence.Runs)
            .SelectMany(run => run.Intervals)
            .ToArray();
        double averageInterval = intervals.Length == 0 ? 0 : intervals.Average();
        double averageConsistency = sequences.Length == 0
            ? 0
            : sequences.Average(sequence => sequence.AverageIntervalConsistency);
        double coverage = objects.Count == 0 ? 0 : (double)involved.Count / objects.Count;
        double confidence = StaminaConfidence(
            sequences.Length,
            coverage,
            averageObjects,
            averageActiveRatio,
            averageRecovery,
            averageConsistency,
            options);

        var result = new StaminaStreamPatternAnalysis(
            objects.Count,
            eligibleRuns.Length,
            involved.Count,
            longestObjects,
            longestDuration,
            averageRuns,
            averageObjects,
            averageDuration,
            averageRecovery,
            averageActiveRatio,
            averageInterval,
            averageConsistency,
            confidence,
            candidates.ToArray());

        GameplayDebug.StaminaStreamPattern(
            result,
            GameplayDebug.StaminaStreamPatternEnabled,
            GameplayDebug.StaminaStreamPatternDetailedEnabled);
        return result;
    }

    private static StaminaStreamPatternSequence CreateStaminaCandidate(
        IReadOnlyList<StaminaRapidRun> runs,
        IReadOnlyList<double> recoveryGaps,
        StaminaStreamPatternOptions options)
    {
        int objectCount = runs.Sum(run => run.ObjectCount);
        string reason = runs.Count < options.MinimumStaminaRuns
            ? "TooFewRuns"
            : objectCount < options.MinimumStaminaObjects
                ? "TooFewObjects"
                : "Qualified";

        return new StaminaStreamPatternSequence(
            runs[0].StartIndex,
            runs[^1].EndIndex,
            runs[0].StartTime,
            runs[^1].EndTime,
            runs.ToArray(),
            recoveryGaps.ToArray(),
            reason == "Qualified",
            reason);
    }

    private static void Validate(StaminaStreamPatternOptions options)
    {
        if (options.MinimumStaminaRunObjects < 2)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumStaminaRunObjects must be at least 2.");
        if (options.MinimumStaminaRuns < 2)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumStaminaRuns must be at least 2.");
        if (options.MinimumStaminaObjects < options.MinimumStaminaRunObjects)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumStaminaObjects must cover at least one eligible run.");
        if (options.MaximumRecoveryGap < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumRecoveryGap must not be negative.");
    }

    private static double StaminaConfidence(
        int sequenceCount,
        double coverage,
        double averageObjects,
        double averageActiveRatio,
        double averageRecoveryGap,
        double averageConsistency,
        StaminaStreamPatternOptions options)
    {
        if (sequenceCount == 0)
            return 0;

        double sequenceSignal = (double)sequenceCount / (sequenceCount + 1);
        double objectSignal = Math.Clamp(
            averageObjects / (options.MinimumStaminaObjects * 2.0), 0, 1);
        double recoverySignal = options.MaximumRecoveryGap <= 0
            ? 1
            : 1 - Math.Clamp(averageRecoveryGap / options.MaximumRecoveryGap, 0, 1);
        return Math.Clamp(
            (sequenceSignal
                + coverage
                + objectSignal
                + averageActiveRatio
                + recoverySignal
                + averageConsistency) / 6,
            0,
            1);
    }
}
