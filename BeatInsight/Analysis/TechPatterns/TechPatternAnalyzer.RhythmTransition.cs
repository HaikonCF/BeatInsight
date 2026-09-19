using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.TechPatterns;

namespace BeatInsight.Analysis.TechPatterns;

public static partial class TechPatternAnalyzer
{
    private static RhythmTransitionPatternAnalysis AnalyzeRhythmTransitionCore(
        IReadOnlyList<HitObject> objects,
        RhythmTransitionPatternOptions options)
    {
        ArgumentNullException.ThrowIfNull(objects);
        Validate(options);

        RhythmTransitionPatternOccurrence[] rawOccurrences = Enumerable.Range(0, objects.Count)
            .Where(index => !IsRhythmBreakObject(objects[index]))
            .Select(index => CreateRhythmTransitionOccurrence(objects, index, options))
            .ToArray();
        RhythmTransitionPatternOccurrence[] occurrences = DeduplicateRhythmTransitions(
            rawOccurrences,
            options.RhythmWindowSize);
        RhythmTransitionPatternOccurrence[] transitions = occurrences
            .Where(occurrence => occurrence.Qualified)
            .ToArray();

        var involvedObjects = new HashSet<int>();
        foreach (RhythmTransitionPatternOccurrence transition in transitions)
        {
            for (int index = transition.StartIndex; index <= transition.EndIndex; index++)
                involvedObjects.Add(index);
        }

        int count = transitions.Length;
        double coverage = objects.Count == 0 ? 0 : (double)involvedObjects.Count / objects.Count;
        double averageAbsolute = count == 0
            ? 0
            : transitions.Average(transition => transition.AbsoluteIntervalChange!.Value);
        double averageRelative = count == 0
            ? 0
            : transitions.Average(transition => transition.RelativeIntervalChange!.Value);
        double averageBeforeConsistency = count == 0
            ? 0
            : transitions.Average(transition => transition.BeforeConsistency!.Value);
        double averageAfterConsistency = count == 0
            ? 0
            : transitions.Average(transition => transition.AfterConsistency!.Value);
        double averageScore = count == 0
            ? 0
            : transitions.Average(transition => transition.RhythmTransitionScore);
        double maximumScore = count == 0
            ? 0
            : transitions.Max(transition => transition.RhythmTransitionScore);
        double countSignal = count == 0 ? 0 : (double)count / (count + 1);
        double confidence = count == 0
            ? 0
            : Math.Clamp((countSignal + coverage + averageScore) / 3, 0, 1);

        var result = new RhythmTransitionPatternAnalysis(
            objects.Count,
            count,
            involvedObjects.Count,
            transitions.Count(transition => transition.Direction == RhythmTransitionDirection.Faster),
            transitions.Count(transition => transition.Direction == RhythmTransitionDirection.Slower),
            averageAbsolute,
            averageRelative,
            averageBeforeConsistency,
            averageAfterConsistency,
            averageScore,
            maximumScore,
            confidence,
            occurrences);

        GameplayDebug.RhythmTransitionPattern(
            result,
            GameplayDebug.RhythmTransitionPatternEnabled,
            GameplayDebug.RhythmTransitionPatternDetailedEnabled);
        return result;
    }

    private static RhythmTransitionPatternOccurrence CreateRhythmTransitionOccurrence(
        IReadOnlyList<HitObject> objects,
        int pivotIndex,
        RhythmTransitionPatternOptions options)
    {
        int requestedStart = pivotIndex - options.RhythmWindowSize;
        int requestedEnd = pivotIndex + options.RhythmWindowSize;
        int startIndex = Math.Max(0, requestedStart);
        int endIndex = Math.Min(objects.Count - 1, requestedEnd);
        if (Enumerable.Range(startIndex, endIndex - startIndex + 1)
            .Any(index => IsRhythmBreakObject(objects[index])))
        {
            return UnavailableRhythmOccurrence(
                objects,
                pivotIndex,
                startIndex,
                endIndex,
                "IntervalBreak");
        }
        if (requestedStart < 0 || requestedEnd >= objects.Count)
            return UnavailableRhythmOccurrence(
                objects,
                pivotIndex,
                startIndex,
                endIndex,
                "InsufficientContext");

        double[] beforeIntervals = Enumerable.Range(
                pivotIndex - options.RhythmWindowSize,
                options.RhythmWindowSize)
            .Select(index => (double)(objects[index + 1].Time - objects[index].Time))
            .ToArray();
        double[] afterIntervals = Enumerable.Range(pivotIndex, options.RhythmWindowSize)
            .Select(index => (double)(objects[index + 1].Time - objects[index].Time))
            .ToArray();
        if (beforeIntervals.Concat(afterIntervals).Any(interval =>
            interval < 0 || interval > options.MaximumRhythmInterval))
        {
            return UnavailableRhythmOccurrence(
                objects,
                pivotIndex,
                startIndex,
                endIndex,
                "IntervalBreak",
                beforeIntervals,
                afterIntervals);
        }

        double beforeAverage = beforeIntervals.Average();
        double afterAverage = afterIntervals.Average();
        double? beforeConsistency = IntervalConsistency(beforeIntervals);
        double? afterConsistency = IntervalConsistency(afterIntervals);
        double absoluteChange = Math.Abs(afterAverage - beforeAverage);
        double largestAverage = Math.Max(beforeAverage, afterAverage);
        double? relativeChange = largestAverage > 0 ? absoluteChange / largestAverage : null;
        double absoluteSignal = Math.Clamp(absoluteChange / 60, 0, 1);
        double? relativeSignal = relativeChange.HasValue
            ? Math.Clamp(relativeChange.Value / 0.50, 0, 1)
            : null;
        double? consistencySignal = beforeConsistency.HasValue && afterConsistency.HasValue
            ? (beforeConsistency.Value + afterConsistency.Value) / 2
            : null;
        double[] scoreSignals = new double?[]
            {
                absoluteSignal,
                relativeSignal,
                consistencySignal
            }
            .Where(signal => signal.HasValue)
            .Select(signal => signal!.Value)
            .ToArray();
        double score = scoreSignals.Length == 0
            ? 0
            : Math.Clamp(scoreSignals.Average(), 0, 1);

        string reason = beforeConsistency is null ||
            beforeConsistency < options.MinimumRhythmSideConsistency
                ? "InconsistentBefore"
                : afterConsistency is null ||
                    afterConsistency < options.MinimumRhythmSideConsistency
                    ? "InconsistentAfter"
                    : absoluteChange < options.MinimumAbsoluteIntervalChange
                        ? "BelowAbsoluteThreshold"
                        : relativeChange is null ||
                            relativeChange < options.MinimumRelativeIntervalChange
                            ? "BelowRelativeThreshold"
                            : "Qualified";
        bool qualified = reason == "Qualified";
        RhythmTransitionDirection? direction = !qualified || beforeAverage == afterAverage
            ? null
            : afterAverage < beforeAverage
                ? RhythmTransitionDirection.Faster
                : RhythmTransitionDirection.Slower;

        return new RhythmTransitionPatternOccurrence(
            pivotIndex,
            startIndex,
            endIndex,
            objects[startIndex].Time,
            objects[endIndex].Time,
            beforeIntervals,
            afterIntervals,
            beforeAverage,
            afterAverage,
            beforeConsistency,
            afterConsistency,
            absoluteChange,
            relativeChange,
            direction,
            score,
            qualified,
            reason);
    }

    private static RhythmTransitionPatternOccurrence UnavailableRhythmOccurrence(
        IReadOnlyList<HitObject> objects,
        int pivotIndex,
        int startIndex,
        int endIndex,
        string reason,
        IReadOnlyList<double>? beforeIntervals = null,
        IReadOnlyList<double>? afterIntervals = null) =>
        new(
            pivotIndex,
            startIndex,
            endIndex,
            objects[startIndex].Time,
            objects[endIndex].Time,
            beforeIntervals ?? Array.Empty<double>(),
            afterIntervals ?? Array.Empty<double>(),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            0,
            false,
            reason);

    private static RhythmTransitionPatternOccurrence[] DeduplicateRhythmTransitions(
        IReadOnlyList<RhythmTransitionPatternOccurrence> occurrences,
        int windowSize)
    {
        var selected = new List<RhythmTransitionPatternOccurrence>();
        foreach (RhythmTransitionPatternOccurrence candidate in occurrences
            .Where(occurrence => occurrence.Qualified)
            .OrderByDescending(occurrence => occurrence.RhythmTransitionScore)
            .ThenByDescending(occurrence => occurrence.AbsoluteIntervalChange)
            .ThenBy(occurrence => occurrence.PivotIndex))
        {
            bool sameLocalRupture = selected.Any(existing =>
                existing.Direction == candidate.Direction &&
                Math.Abs(existing.PivotIndex - candidate.PivotIndex) <= windowSize);
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

    private static double? IntervalConsistency(IReadOnlyList<double> intervals)
    {
        if (intervals.Count == 0)
            return null;
        double maximum = intervals.Max();
        return maximum > 0 ? intervals.Min() / maximum : null;
    }

    private static bool IsRhythmBreakObject(HitObject hitObject) =>
        (hitObject.Type & 8) == 8;

    private static void Validate(RhythmTransitionPatternOptions options)
    {
        if (options.RhythmWindowSize < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "RhythmWindowSize must be at least 1.");
        if (!double.IsFinite(options.MinimumAbsoluteIntervalChange) ||
            options.MinimumAbsoluteIntervalChange < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumAbsoluteIntervalChange must not be negative.");
        if (!double.IsFinite(options.MinimumRelativeIntervalChange) ||
            options.MinimumRelativeIntervalChange < 0 ||
            options.MinimumRelativeIntervalChange > 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumRelativeIntervalChange must be in [0,1].");
        if (!double.IsFinite(options.MinimumRhythmSideConsistency) ||
            options.MinimumRhythmSideConsistency < 0 ||
            options.MinimumRhythmSideConsistency > 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumRhythmSideConsistency must be in [0,1].");
        if (!double.IsFinite(options.MaximumRhythmInterval) || options.MaximumRhythmInterval <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumRhythmInterval must be greater than 0.");
    }
}
