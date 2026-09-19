using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.StreamPatterns;

namespace BeatInsight.Analysis.StreamPatterns;

public static partial class StreamPatternAnalyzer
{
    private const double FlowStreamConfidenceFullLength = 16;

    private static FlowStreamPatternAnalysis AnalyzeFlowStreamCore(
        IReadOnlyList<HitObject> objects,
        FlowStreamPatternOptions options)
    {
        ArgumentNullException.ThrowIfNull(objects);
        Validate(options);

        FlowStreamPatternSequence[] runs = SegmentRapidCircleRuns(objects)
            .Select(run => CreateFlowStreamSequence(objects, run, options))
            .ToArray();
        FlowStreamPatternSequence[] sequences = runs.Where(run => run.Qualified).ToArray();

        var involved = new HashSet<int>();
        foreach (FlowStreamPatternSequence sequence in sequences)
            for (int index = sequence.StartIndex; index <= sequence.EndIndex; index++)
                involved.Add(index);

        int longest = sequences.Length == 0 ? 0 : sequences.Max(sequence => sequence.ObjectCount);
        double averageLength = sequences.Length == 0 ? 0 : sequences.Average(sequence => sequence.ObjectCount);
        double averagePivotRatio = sequences.Length == 0 ? 0 : sequences.Average(sequence => sequence.FlowPivotRatio);
        double averageDirectionConsistency = sequences.Length == 0
            ? 0
            : sequences.Average(sequence => sequence.TurnDirectionConsistency);
        double averageTurnAngle = sequences.Length == 0 ? 0 : sequences.Average(sequence => sequence.AverageTurnAngle);
        double averageSpacingConsistency = sequences.Length == 0
            ? 0
            : sequences.Average(sequence => sequence.SpacingConsistency);
        double averageFlowScore = sequences.Length == 0 ? 0 : sequences.Average(sequence => sequence.FlowScore);
        double coverage = objects.Count == 0 ? 0 : (double)involved.Count / objects.Count;
        double confidence = FlowStreamConfidence(sequences.Length, coverage, averageLength, averageFlowScore);

        var result = new FlowStreamPatternAnalysis(
            objects.Count,
            runs.Length,
            involved.Count,
            longest,
            averageLength,
            averagePivotRatio,
            averageDirectionConsistency,
            averageTurnAngle,
            averageSpacingConsistency,
            averageFlowScore,
            confidence,
            runs);

        GameplayDebug.FlowStreamPattern(
            result,
            GameplayDebug.FlowStreamPatternEnabled,
            GameplayDebug.FlowStreamPatternDetailedEnabled);
        return result;
    }

    private static FlowStreamPatternSequence CreateFlowStreamSequence(
        IReadOnlyList<HitObject> objects,
        RapidStreamRun run,
        FlowStreamPatternOptions options)
    {
        double[] distances = Enumerable.Range(run.StartIndex, run.ObjectCount - 1)
            .Select(index => StreamLinkDistance(objects[index], objects[index + 1]))
            .ToArray();
        FlowStreamPivot[] pivots = Enumerable.Range(run.StartIndex + 1, Math.Max(0, run.ObjectCount - 2))
            .Select(index => CreateFlowStreamPivot(objects, index, distances[index - run.StartIndex - 1],
                distances[index - run.StartIndex], options))
            .ToArray();

        var provisional = new FlowStreamPatternSequence(
            run.StartIndex,
            run.EndIndex,
            run.StartTime,
            run.EndTime,
            run.Intervals,
            distances,
            pivots,
            false,
            "");

        string reason = provisional.ObjectCount < options.MinimumFlowStreamObjects
            ? "TooShort"
            : distances.Any(distance => distance < options.MinimumFlowLinkDistance)
                ? "InsufficientSpatialMovement"
                : provisional.FlowPivotRatio < options.MinimumFlowPivotRatio
                    ? "TooFewFlowPivots"
                    : provisional.TurnDirectionConsistency < options.MinimumTurnDirectionConsistency
                        ? "InconsistentTurnDirection"
                        : "Qualified";

        return provisional with
        {
            Qualified = reason == "Qualified",
            Reason = reason
        };
    }

    private static FlowStreamPivot CreateFlowStreamPivot(
        IReadOnlyList<HitObject> objects,
        int index,
        double previousDistance,
        double nextDistance,
        FlowStreamPatternOptions options)
    {
        HitObject previous = objects[index - 1];
        HitObject current = objects[index];
        HitObject next = objects[index + 1];
        bool spatiallyValid = previousDistance >= options.MinimumFlowLinkDistance
            && nextDistance >= options.MinimumFlowLinkDistance;
        double angle = GameplayAnalyzer.GetTurnAngle(previous, current, next);
        int sign = StreamTurnSign(previous, current, next);
        bool isFlowTurn = spatiallyValid
            && angle >= options.MinimumFlowTurnAngle
            && angle <= options.MaximumFlowTurnAngle
            && sign != 0;

        return new FlowStreamPivot(index, angle, sign, previousDistance, nextDistance, spatiallyValid, isFlowTurn);
    }

    private static double StreamLinkDistance(HitObject first, HitObject second)
    {
        double x = (double)second.X - first.X;
        double y = (double)second.Y - first.Y;
        return Math.Sqrt(x * x + y * y);
    }

    private static int StreamTurnSign(HitObject previous, HitObject current, HitObject next)
    {
        double cross = ((double)current.X - previous.X) * (next.Y - current.Y)
            - ((double)current.Y - previous.Y) * (next.X - current.X);
        return cross > 0 ? 1 : cross < 0 ? -1 : 0;
    }

    private static void Validate(FlowStreamPatternOptions options)
    {
        if (options.MinimumFlowStreamObjects < 3)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumFlowStreamObjects must be at least 3.");
        if (options.MinimumFlowTurnAngle < 0 || options.MaximumFlowTurnAngle > 180
            || options.MinimumFlowTurnAngle > options.MaximumFlowTurnAngle)
            throw new ArgumentOutOfRangeException(nameof(options), "Flow turn angles must define an ordered range in [0,180].");
        if (options.MinimumFlowLinkDistance < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumFlowLinkDistance must not be negative.");
        if (options.MinimumFlowPivotRatio < 0 || options.MinimumFlowPivotRatio > 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumFlowPivotRatio must be in [0,1].");
        if (options.MinimumTurnDirectionConsistency < 0 || options.MinimumTurnDirectionConsistency > 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumTurnDirectionConsistency must be in [0,1].");
    }

    private static double FlowStreamConfidence(
        int sequenceCount,
        double coverage,
        double averageLength,
        double averageFlowScore)
    {
        if (sequenceCount == 0)
            return 0;

        double sequenceSignal = (double)sequenceCount / (sequenceCount + 1);
        double lengthSignal = Math.Clamp(averageLength / FlowStreamConfidenceFullLength, 0, 1);
        return Math.Clamp((sequenceSignal + coverage + lengthSignal + averageFlowScore) / 4, 0, 1);
    }
}
