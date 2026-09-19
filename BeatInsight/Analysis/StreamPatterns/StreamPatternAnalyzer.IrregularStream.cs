using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.StreamPatterns;

namespace BeatInsight.Analysis.StreamPatterns;

public static partial class StreamPatternAnalyzer
{
    private const double IrregularStreamConfidenceFullLength = 16;

    private static IrregularStreamPatternAnalysis AnalyzeIrregularStreamCore(
        IReadOnlyList<HitObject> objects,
        IrregularStreamPatternOptions options)
    {
        ArgumentNullException.ThrowIfNull(objects);
        Validate(options);

        IrregularStreamPatternSequence[] runs = SegmentRapidCircleRuns(objects)
            .Select(run => CreateIrregularStreamSequence(run, options))
            .ToArray();
        IrregularStreamPatternSequence[] sequences = runs.Where(run => run.Qualified).ToArray();

        var involved = new HashSet<int>();
        foreach (IrregularStreamPatternSequence sequence in sequences)
            for (int index = sequence.StartIndex; index <= sequence.EndIndex; index++)
                involved.Add(index);

        int longest = sequences.Length == 0 ? 0 : sequences.Max(sequence => sequence.ObjectCount);
        double averageLength = sequences.Length == 0 ? 0 : sequences.Average(sequence => sequence.ObjectCount);
        double[] intervals = sequences.SelectMany(sequence => sequence.Intervals).ToArray();
        double averageInterval = intervals.Length == 0 ? 0 : intervals.Average();
        double averageCv = sequences.Length == 0
            ? 0
            : sequences.Average(sequence => sequence.CoefficientOfVariation);
        double[] intervalDeltas = sequences.SelectMany(sequence => sequence.IntervalDeltas).ToArray();
        double averageDelta = intervalDeltas.Length == 0 ? 0 : intervalDeltas.Average();
        double averageSignificantRatio = sequences.Length == 0
            ? 0
            : sequences.Average(sequence => sequence.SignificantDeltaRatio);
        double averageDistinctEvents = sequences.Length == 0
            ? 0
            : sequences.Average(sequence => sequence.DistinctIrregularEventCount);
        double averageAtypicalRatio = sequences.Length == 0
            ? 0
            : sequences.Average(sequence => sequence.AtypicalIntervalRatio);
        double averageScore = sequences.Length == 0
            ? 0
            : sequences.Average(sequence => sequence.IrregularityScore);
        double coverage = objects.Count == 0 ? 0 : (double)involved.Count / objects.Count;
        double confidence = IrregularStreamConfidence(
            sequences.Length,
            coverage,
            averageLength,
            averageScore,
            averageDistinctEvents);

        var result = new IrregularStreamPatternAnalysis(
            objects.Count,
            runs.Length,
            involved.Count,
            longest,
            averageLength,
            averageInterval,
            averageCv,
            averageDelta,
            averageSignificantRatio,
            averageDistinctEvents,
            averageAtypicalRatio,
            averageScore,
            confidence,
            runs);

        GameplayDebug.IrregularStreamPattern(
            result,
            GameplayDebug.IrregularStreamPatternEnabled,
            GameplayDebug.IrregularStreamPatternDetailedEnabled);
        return result;
    }

    private static IrregularStreamPatternSequence CreateIrregularStreamSequence(
        RapidStreamRun run,
        IrregularStreamPatternOptions options)
    {
        var provisional = new IrregularStreamPatternSequence(
            run.StartIndex,
            run.EndIndex,
            run.StartTime,
            run.EndTime,
            run.Intervals,
            options.MinimumSignificantIntervalDelta,
            false,
            "");

        string reason = provisional.ObjectCount < options.MinimumIrregularStreamObjects
            ? "TooShort"
            : provisional.CoefficientOfVariation < options.MinimumCoefficientOfVariation
                ? "TooRegular"
                : provisional.DistinctIrregularEventCount < options.MinimumDistinctIrregularEvents
                    ? "TooFewDistinctIrregularEvents"
                    : "Qualified";

        return provisional with
        {
            Qualified = reason == "Qualified",
            Reason = reason
        };
    }

    private static void Validate(IrregularStreamPatternOptions options)
    {
        if (options.MinimumIrregularStreamObjects < 3)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumIrregularStreamObjects must be at least 3.");
        if (options.MinimumSignificantIntervalDelta < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumSignificantIntervalDelta must not be negative.");
        if (options.MinimumCoefficientOfVariation < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumCoefficientOfVariation must not be negative.");
        if (options.MinimumSignificantDeltaRatio < 0 || options.MinimumSignificantDeltaRatio > 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumSignificantDeltaRatio must be in [0,1].");
        if (options.MinimumDistinctIrregularEvents < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumDistinctIrregularEvents must be at least 1.");
    }

    private static double IrregularStreamConfidence(
        int sequenceCount,
        double coverage,
        double averageLength,
        double averageIrregularityScore,
        double averageDistinctIrregularEvents)
    {
        if (sequenceCount == 0)
            return 0;

        double sequenceSignal = (double)sequenceCount / (sequenceCount + 1);
        double lengthSignal = Math.Clamp(averageLength / IrregularStreamConfidenceFullLength, 0, 1);
        double eventSignal = Math.Clamp(averageDistinctIrregularEvents / 3.0, 0, 1);
        return Math.Clamp(
            (sequenceSignal
                + coverage
                + lengthSignal
                + averageIrregularityScore
                + eventSignal) / 5,
            0,
            1);
    }
}
