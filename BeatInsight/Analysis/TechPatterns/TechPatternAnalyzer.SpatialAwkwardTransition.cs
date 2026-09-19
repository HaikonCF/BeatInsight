using BeatInsight.Diagnostics;
using BeatInsight.Models;
using System.Numerics;
using BeatInsight.Models.TechPatterns;

namespace BeatInsight.Analysis.TechPatterns;

public static partial class TechPatternAnalyzer
{
    private static SpatialAwkwardTransitionPatternAnalysis AnalyzeSpatialAwkwardTransitionCore(
        IReadOnlyList<HitObject> objects,
        SpatialAwkwardTransitionPatternOptions options)
    {
        ArgumentNullException.ThrowIfNull(objects);
        Validate(options);

        SpatialAwkwardTransitionPatternOccurrence[] rawOccurrences = Enumerable.Range(0, objects.Count)
            .Select(index => CreateSpatialAwkwardOccurrence(objects, index, options))
            .ToArray();
        SpatialAwkwardTransitionPatternOccurrence[] occurrences = DeduplicateSpatialAwkwardTransitions(
            rawOccurrences);
        SpatialAwkwardTransitionPatternOccurrence[] transitions = occurrences
            .Where(occurrence => occurrence.Qualified)
            .ToArray();

        var involvedObjects = new HashSet<int>();
        foreach (SpatialAwkwardTransitionPatternOccurrence transition in transitions)
        {
            for (int index = transition.StartIndex; index <= transition.EndIndex; index++)
                involvedObjects.Add(index);
        }

        int count = transitions.Length;
        double coverage = objects.Count == 0 ? 0 : (double)involvedObjects.Count / objects.Count;
        double averageIncoming = count == 0
            ? 0
            : transitions.Average(transition => transition.IncomingDistance);
        double averageOutgoing = count == 0
            ? 0
            : transitions.Average(transition => transition.OutgoingDistance);
        double averageTurn = count == 0
            ? 0
            : transitions.Average(transition => transition.TurnAngle);
        double averageAsymmetry = count == 0
            ? 0
            : transitions.Average(transition => transition.Asymmetry);
        double averageScore = count == 0
            ? 0
            : transitions.Average(transition => transition.SpatialAwkwardScore);
        double maximumScore = count == 0
            ? 0
            : transitions.Max(transition => transition.SpatialAwkwardScore);
        double countSignal = count == 0 ? 0 : (double)count / (count + 1);
        double confidence = count == 0
            ? 0
            : Math.Clamp((countSignal + coverage + averageScore) / 3, 0, 1);

        var result = new SpatialAwkwardTransitionPatternAnalysis(
            objects.Count,
            count,
            involvedObjects.Count,
            averageIncoming,
            averageOutgoing,
            averageTurn,
            averageAsymmetry,
            averageScore,
            maximumScore,
            confidence,
            occurrences);

        GameplayDebug.SpatialAwkwardTransitionPattern(
            result,
            GameplayDebug.SpatialAwkwardTransitionPatternEnabled,
            GameplayDebug.SpatialAwkwardTransitionPatternDetailedEnabled);
        return result;
    }

    private static SpatialAwkwardTransitionPatternOccurrence CreateSpatialAwkwardOccurrence(
        IReadOnlyList<HitObject> objects,
        int pivotIndex,
        SpatialAwkwardTransitionPatternOptions options)
    {
        int startIndex = pivotIndex - options.SpatialWindowSize;
        int endIndex = pivotIndex + options.SpatialWindowSize;
        if (startIndex < 0 || endIndex >= objects.Count)
            return UnavailableSpatialAwkwardOccurrence(
                objects,
                pivotIndex,
                Math.Max(0, startIndex),
                Math.Min(objects.Count - 1, endIndex),
                "InsufficientContext");
        if (Enumerable.Range(startIndex, endIndex - startIndex + 1)
            .Any(index => GameplayAnalyzer.IsSpinner(objects[index])))
            return UnavailableSpatialAwkwardOccurrence(
                objects,
                pivotIndex,
                startIndex,
                endIndex,
                "IntervalBreak");
        if (pivotIndex == 0 || pivotIndex >= objects.Count - 1)
            return UnavailableSpatialAwkwardOccurrence(
                objects,
                pivotIndex,
                startIndex,
                endIndex,
                "InsufficientContext");

        HitObject prev = objects[pivotIndex - 1];
        HitObject curr = objects[pivotIndex];
        HitObject next = objects[pivotIndex + 1];

        int incomingGap = curr.Time - prev.Time;
        int outgoingGap = next.Time - curr.Time;
        if (incomingGap < 0 || outgoingGap < 0 ||
            incomingGap > options.MaximumSpatialTransitionGap ||
            outgoingGap > options.MaximumSpatialTransitionGap)
            return UnavailableSpatialAwkwardOccurrence(
                objects,
                pivotIndex,
                startIndex,
                endIndex,
                "IntervalBreak");

        (double prevEndX, double prevEndY) = GameplayAnalyzer.GetObjectEndPosition(prev);
        Vector2 prevEnd = new((float)prevEndX, (float)prevEndY);
        Vector2 currStart = new(curr.X, curr.Y);
        (double currEndX, double currEndY) = GameplayAnalyzer.GetObjectEndPosition(curr);
        Vector2 currEnd = new((float)currEndX, (float)currEndY);
        Vector2 nextStart = new(next.X, next.Y);

        double incomingDistance = Vector2.Distance(prevEnd, currStart);
        double outgoingDistance = Vector2.Distance(currEnd, nextStart);
        double averageDistance = (incomingDistance + outgoingDistance) / 2;

        if (!double.IsFinite(incomingDistance) || !double.IsFinite(outgoingDistance) ||
            incomingDistance <= 0 || outgoingDistance <= 0)
            return UnavailableSpatialAwkwardOccurrence(
                objects,
                pivotIndex,
                startIndex,
                endIndex,
                "UnreliableGeometry",
                incomingDistance,
                outgoingDistance);

        double turnAngle = CalculateSpatialTransitionAngle(prevEnd, currStart, currEnd, nextStart);

        double distanceSignal = Math.Clamp(
            (averageDistance - options.MinimumRelevantDistance) / 120.0,
            0,
            1);
        double turnSignal = Math.Clamp(
            (turnAngle - options.MinimumAwkwardTurnAngle) / 90.0,
            0,
            1);

        double distanceRatio = Math.Min(incomingDistance, outgoingDistance) / Math.Max(incomingDistance, outgoingDistance);
        double asymmetry = 1 - distanceRatio;
        double asymmetrySignal = Math.Clamp(asymmetry / 0.60, 0, 1);

        int activeSignals = (distanceSignal > 0 ? 1 : 0) +
            (turnSignal > 0 ? 1 : 0) +
            (asymmetrySignal > 0 ? 1 : 0);
        double score = (distanceSignal + turnSignal + asymmetrySignal) / 3;
        bool hasSpatialSignal = distanceSignal > 0 || asymmetrySignal > 0;
        bool qualified = score >= options.MinimumSpatialAwkwardScore &&
            turnAngle >= options.MinimumAwkwardTurnAngle &&
            hasSpatialSignal;

        string reason = qualified
            ? "Qualified"
            : averageDistance < options.MinimumRelevantDistance
                ? "BelowDistanceRelevance"
                : !hasSpatialSignal
                ? "InsufficientCombinedSignals"
                : score < options.MinimumSpatialAwkwardScore
                    ? activeSignals < 2
                        ? "InsufficientCombinedSignals"
                        : "BelowAwkwardThreshold"
                    : "BelowAwkwardThreshold";

        return new SpatialAwkwardTransitionPatternOccurrence(
            pivotIndex,
            startIndex,
            endIndex,
            objects[startIndex].Time,
            objects[endIndex].Time,
            incomingDistance,
            outgoingDistance,
            averageDistance,
            turnAngle,
            distanceRatio,
            asymmetry,
            distanceSignal,
            turnSignal,
            asymmetrySignal,
            score,
            qualified,
            reason);
    }

    private static SpatialAwkwardTransitionPatternOccurrence UnavailableSpatialAwkwardOccurrence(
        IReadOnlyList<HitObject> objects,
        int pivotIndex,
        int startIndex,
        int endIndex,
        string reason,
        double? incomingDistance = null,
        double? outgoingDistance = null) =>
        new(
            pivotIndex,
            startIndex,
            endIndex,
            objects[startIndex].Time,
            objects[endIndex].Time,
            incomingDistance ?? 0,
            outgoingDistance ?? 0,
            0,
            0,
            0,
            0,
            null,
            null,
            null,
            0,
            false,
            reason);

    private static SpatialAwkwardTransitionPatternOccurrence[] DeduplicateSpatialAwkwardTransitions(
        IReadOnlyList<SpatialAwkwardTransitionPatternOccurrence> occurrences)
    {
        var selected = new List<SpatialAwkwardTransitionPatternOccurrence>();
        foreach (SpatialAwkwardTransitionPatternOccurrence candidate in occurrences
            .Where(occurrence => occurrence.Qualified)
            .OrderByDescending(occurrence => occurrence.SpatialAwkwardScore)
            .ThenByDescending(occurrence => occurrence.TurnAngle)
            .ThenByDescending(occurrence => occurrence.AverageDistance)
            .ThenBy(occurrence => occurrence.PivotIndex))
        {
            bool sameLocalRupture = selected.Any(existing =>
                Math.Abs(existing.PivotIndex - candidate.PivotIndex) <= 1);
            if (!sameLocalRupture)
                selected.Add(candidate);
        }

        HashSet<int> selectedPivots = selected
            .Select(occurrence => occurrence.PivotIndex)
            .ToHashSet();
        return occurrences
            .Where(occurrence => !occurrence.Qualified || selectedPivots.Contains(occurrence.PivotIndex))
            .OrderBy(occurrence => occurrence.PivotIndex)
            .ToArray();
    }

    private static void Validate(SpatialAwkwardTransitionPatternOptions options)
    {
        if (options.SpatialWindowSize != 1)
            throw new ArgumentOutOfRangeException(nameof(options), "SpatialWindowSize must be 1 in V1.");
        if (!double.IsFinite(options.MinimumRelevantDistance) || options.MinimumRelevantDistance <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumRelevantDistance must be positive.");
        if (!double.IsFinite(options.MinimumAwkwardTurnAngle) || options.MinimumAwkwardTurnAngle < 0 || options.MinimumAwkwardTurnAngle > 180)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumAwkwardTurnAngle must be in [0,180].");
        if (!double.IsFinite(options.MinimumSpatialAwkwardScore) || options.MinimumSpatialAwkwardScore < 0 || options.MinimumSpatialAwkwardScore > 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumSpatialAwkwardScore must be in [0,1].");
        if (!double.IsFinite(options.MaximumSpatialTransitionGap) || options.MaximumSpatialTransitionGap <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumSpatialTransitionGap must be positive.");
    }

    private static double CalculateSpatialTransitionAngle(
        Vector2 previousEnd,
        Vector2 currentStart,
        Vector2 currentEnd,
        Vector2 nextStart)
    {
        Vector2 incoming = currentStart - previousEnd;
        Vector2 outgoing = nextStart - currentEnd;
        double denominator = incoming.Length() * outgoing.Length();
        if (denominator <= 0 || !double.IsFinite(denominator))
            return 0;

        double cosine = Math.Clamp(Vector2.Dot(incoming, outgoing) / denominator, -1, 1);
        return Math.Acos(cosine) * 180 / Math.PI;
    }
}
