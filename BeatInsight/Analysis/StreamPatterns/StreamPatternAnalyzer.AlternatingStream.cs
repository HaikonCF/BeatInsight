using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.StreamPatterns;

namespace BeatInsight.Analysis.StreamPatterns;

public static partial class StreamPatternAnalyzer
{
    private const double AlternatingStreamConfidenceFullLength = 16;

    private static AlternatingStreamPatternAnalysis AnalyzeAlternatingStreamCore(
        IReadOnlyList<HitObject> objects,
        AlternatingStreamPatternOptions options)
    {
        ArgumentNullException.ThrowIfNull(objects);
        Validate(options);

        AlternatingStreamPatternSequence[] runs = SegmentRapidCircleRuns(objects)
            .Select(run => CreateAlternatingStreamSequence(objects, run, options))
            .ToArray();
        AlternatingStreamPatternSequence[] sequences = runs.Where(run => run.Qualified).ToArray();

        var involved = new HashSet<int>();
        foreach (AlternatingStreamPatternSequence sequence in sequences)
            for (int index = sequence.StartIndex; index <= sequence.EndIndex; index++)
                involved.Add(index);

        int longest = sequences.Length == 0 ? 0 : sequences.Max(sequence => sequence.ObjectCount);
        double averageLength = sequences.Length == 0 ? 0 : sequences.Average(sequence => sequence.ObjectCount);
        double averageValidPivotRatio = sequences.Length == 0
            ? 0
            : sequences.Average(sequence => sequence.ValidAlternatingPivotRatio);
        double averageAlternationRatio = sequences.Length == 0
            ? 0
            : sequences.Average(sequence => sequence.AlternationRatio);
        double averageTurnAngle = sequences.Length == 0
            ? 0
            : sequences.Average(sequence => sequence.AverageValidTurnAngle);
        double averageSpacingConsistency = sequences.Length == 0
            ? 0
            : sequences.Average(sequence => sequence.SpacingConsistency);
        double averageAlternatingScore = sequences.Length == 0
            ? 0
            : sequences.Average(sequence => sequence.AlternatingScore);
        double coverage = objects.Count == 0 ? 0 : (double)involved.Count / objects.Count;
        double confidence = AlternatingStreamConfidence(
            sequences.Length,
            coverage,
            averageLength,
            averageAlternatingScore);

        var result = new AlternatingStreamPatternAnalysis(
            objects.Count,
            runs.Length,
            involved.Count,
            longest,
            averageLength,
            averageValidPivotRatio,
            averageAlternationRatio,
            averageTurnAngle,
            averageSpacingConsistency,
            averageAlternatingScore,
            confidence,
            runs);

        GameplayDebug.AlternatingStreamPattern(
            result,
            GameplayDebug.AlternatingStreamPatternEnabled,
            GameplayDebug.AlternatingStreamPatternDetailedEnabled);
        return result;
    }

    private static AlternatingStreamPatternSequence CreateAlternatingStreamSequence(
        IReadOnlyList<HitObject> objects,
        RapidStreamRun run,
        AlternatingStreamPatternOptions options)
    {
        double[] distances = Enumerable.Range(run.StartIndex, run.ObjectCount - 1)
            .Select(index => StreamLinkDistance(objects[index], objects[index + 1]))
            .ToArray();
        AlternatingStreamPivot[] pivots = Enumerable.Range(run.StartIndex + 1, Math.Max(0, run.ObjectCount - 2))
            .Select(index => CreateAlternatingStreamPivot(
                objects,
                index,
                distances[index - run.StartIndex - 1],
                distances[index - run.StartIndex],
                options))
            .ToArray();

        var provisional = new AlternatingStreamPatternSequence(
            run.StartIndex,
            run.EndIndex,
            run.StartTime,
            run.EndTime,
            run.Intervals,
            distances,
            pivots,
            false,
            "");

        string reason = provisional.ObjectCount < options.MinimumAlternatingStreamObjects
            ? "TooShort"
            : distances.Any(distance => distance < options.MinimumAlternatingLinkDistance)
                ? "InsufficientSpatialMovement"
                : provisional.ValidAlternatingPivotRatio < options.MinimumValidAlternatingPivotRatio
                    ? "TooFewValidPivots"
                    : provisional.AlternationRatio < options.MinimumAlternationRatio
                        ? "InsufficientAlternation"
                        : "Qualified";

        return provisional with
        {
            Qualified = reason == "Qualified",
            Reason = reason
        };
    }

    private static AlternatingStreamPivot CreateAlternatingStreamPivot(
        IReadOnlyList<HitObject> objects,
        int index,
        double previousDistance,
        double nextDistance,
        AlternatingStreamPatternOptions options)
    {
        HitObject previous = objects[index - 1];
        HitObject current = objects[index];
        HitObject next = objects[index + 1];
        bool spatiallyValid = previousDistance >= options.MinimumAlternatingLinkDistance
            && nextDistance >= options.MinimumAlternatingLinkDistance;
        double angle = GameplayAnalyzer.GetTurnAngle(previous, current, next);
        int sign = StreamTurnSign(previous, current, next);
        bool isValid = spatiallyValid
            && angle >= options.MinimumAlternatingTurnAngle
            && angle <= options.MaximumAlternatingTurnAngle
            && sign != 0;

        return new AlternatingStreamPivot(
            index,
            angle,
            sign,
            previousDistance,
            nextDistance,
            spatiallyValid,
            isValid);
    }

    private static void Validate(AlternatingStreamPatternOptions options)
    {
        if (options.MinimumAlternatingStreamObjects < 3)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumAlternatingStreamObjects must be at least 3.");
        if (options.MinimumAlternatingTurnAngle < 0 || options.MaximumAlternatingTurnAngle > 180
            || options.MinimumAlternatingTurnAngle > options.MaximumAlternatingTurnAngle)
            throw new ArgumentOutOfRangeException(nameof(options), "Alternating turn angles must define an ordered range in [0,180].");
        if (options.MinimumAlternatingLinkDistance < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumAlternatingLinkDistance must not be negative.");
        if (options.MinimumValidAlternatingPivotRatio < 0 || options.MinimumValidAlternatingPivotRatio > 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumValidAlternatingPivotRatio must be in [0,1].");
        if (options.MinimumAlternationRatio < 0 || options.MinimumAlternationRatio > 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumAlternationRatio must be in [0,1].");
    }

    private static double AlternatingStreamConfidence(
        int sequenceCount,
        double coverage,
        double averageLength,
        double averageAlternatingScore)
    {
        if (sequenceCount == 0)
            return 0;

        double sequenceSignal = (double)sequenceCount / (sequenceCount + 1);
        double lengthSignal = Math.Clamp(averageLength / AlternatingStreamConfidenceFullLength, 0, 1);
        return Math.Clamp(
            (sequenceSignal + coverage + lengthSignal + averageAlternatingScore) / 4,
            0,
            1);
    }
}
