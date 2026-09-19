using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.TechPatterns;

namespace BeatInsight.Analysis.TechPatterns;

public static partial class TechPatternAnalyzer
{
    private const double CircleSliderConfidenceFullLength = 8;

    private static CircleSliderTransitionPatternAnalysis AnalyzeCircleSliderTransitionCore(
        IReadOnlyList<HitObject> objects,
        CircleSliderTransitionPatternOptions options)
    {
        ArgumentNullException.ThrowIfNull(objects);
        Validate(options);

        CircleSliderTransitionPatternSequence[] runs = SegmentCircleSliderCandidates(objects, options)
            .Select(candidate => CreateCircleSliderSequence(
                objects,
                candidate.StartIndex,
                candidate.EndIndex,
                options))
            .ToArray();
        CircleSliderTransitionPatternSequence[] sequences = runs
            .Where(run => run.Qualified)
            .ToArray();

        var involvedObjects = new HashSet<int>();
        foreach (CircleSliderTransitionPatternSequence sequence in sequences)
        {
            for (int index = sequence.StartIndex; index <= sequence.EndIndex; index++)
                involvedObjects.Add(index);
        }

        int count = sequences.Length;
        double coverage = objects.Count == 0 ? 0 : (double)involvedObjects.Count / objects.Count;
        double averageRatio = count == 0 ? 0 : sequences.Average(sequence => sequence.TransitionRatio);
        double averageCount = count == 0 ? 0 : sequences.Average(sequence => sequence.TransitionCount);
        int longest = count == 0 ? 0 : sequences.Max(sequence => sequence.ObjectCount);
        double averageLength = count == 0 ? 0 : sequences.Average(sequence => sequence.ObjectCount);
        double averageScore = count == 0 ? 0 : sequences.Average(sequence => sequence.TransitionScore);
        double sequenceSignal = count == 0 ? 0 : (double)count / (count + 1);
        double lengthSignal = Math.Clamp(
            averageLength / CircleSliderConfidenceFullLength,
            0,
            1);
        double confidence = count == 0
            ? 0
            : Math.Clamp(
                (sequenceSignal + coverage + lengthSignal + averageScore) / 4,
                0,
                1);

        var result = new CircleSliderTransitionPatternAnalysis(
            objects.Count,
            runs.Length,
            count,
            involvedObjects.Count,
            sequences.Sum(sequence => sequence.CircleToSliderCount),
            sequences.Sum(sequence => sequence.SliderToCircleCount),
            averageRatio,
            averageCount,
            longest,
            averageLength,
            averageScore,
            confidence,
            runs);

        GameplayDebug.CircleSliderTransitionPattern(
            result,
            GameplayDebug.CircleSliderTransitionPatternEnabled,
            GameplayDebug.CircleSliderTransitionPatternDetailedEnabled);
        return result;
    }

    private static IReadOnlyList<CircleSliderCandidate> SegmentCircleSliderCandidates(
        IReadOnlyList<HitObject> objects,
        CircleSliderTransitionPatternOptions options)
    {
        var candidates = new List<CircleSliderCandidate>();
        int startIndex = -1;
        int endIndex = -1;
        bool previousWasSlider = false;
        int consecutiveSameType = 0;

        void FinalizeCandidate()
        {
            if (startIndex >= 0)
                candidates.Add(new CircleSliderCandidate(startIndex, endIndex));
            startIndex = -1;
            endIndex = -1;
            consecutiveSameType = 0;
        }

        for (int index = 0; index < objects.Count; index++)
        {
            HitObject current = objects[index];
            bool slider = IsSlider(current);
            bool circle = IsCircle(current);
            if (!slider && !circle)
            {
                FinalizeCandidate();
                continue;
            }

            if (startIndex >= 0)
            {
                double gap = current.Time - objects[index - 1].Time;
                if (gap < 0 || gap > options.MaximumTransitionObjectGap)
                    FinalizeCandidate();
            }

            if (startIndex < 0)
            {
                startIndex = index;
                endIndex = index;
                previousWasSlider = slider;
                consecutiveSameType = 1;
                continue;
            }

            if (slider == previousWasSlider)
            {
                if (consecutiveSameType >= options.MaximumConsecutiveSameType)
                {
                    FinalizeCandidate();
                    startIndex = index;
                    endIndex = index;
                    previousWasSlider = slider;
                    consecutiveSameType = 1;
                    continue;
                }
                consecutiveSameType++;
            }
            else
            {
                previousWasSlider = slider;
                consecutiveSameType = 1;
            }
            endIndex = index;
        }

        FinalizeCandidate();
        return candidates;
    }

    private static CircleSliderTransitionPatternSequence CreateCircleSliderSequence(
        IReadOnlyList<HitObject> objects,
        int startIndex,
        int endIndex,
        CircleSliderTransitionPatternOptions options)
    {
        int objectCount = endIndex - startIndex + 1;
        int[] objectTypes = Enumerable.Range(startIndex, objectCount)
            .Select(index => objects[index].Type)
            .ToArray();
        double[] gaps = Enumerable.Range(startIndex + 1, objectCount - 1)
            .Select(index => (double)(objects[index].Time - objects[index - 1].Time))
            .ToArray();
        int circleCount = Enumerable.Range(startIndex, objectCount)
            .Count(index => IsCircle(objects[index]));
        int sliderCount = objectCount - circleCount;
        int circleToSliderCount = 0;
        int sliderToCircleCount = 0;
        int longestSameTypeRun = objectCount == 0 ? 0 : 1;
        int currentSameTypeRun = objectCount == 0 ? 0 : 1;

        for (int index = startIndex + 1; index <= endIndex; index++)
        {
            bool previousSlider = IsSlider(objects[index - 1]);
            bool currentSlider = IsSlider(objects[index]);
            if (!previousSlider && currentSlider)
                circleToSliderCount++;
            else if (previousSlider && !currentSlider)
                sliderToCircleCount++;

            if (previousSlider == currentSlider)
                currentSameTypeRun++;
            else
                currentSameTypeRun = 1;
            longestSameTypeRun = Math.Max(longestSameTypeRun, currentSameTypeRun);
        }

        int transitionCount = circleToSliderCount + sliderToCircleCount;
        int possibleTransitions = Math.Max(0, objectCount - 1);
        double transitionRatio = possibleTransitions == 0
            ? 0
            : (double)transitionCount / possibleTransitions;
        int maximumDirectionCount = Math.Max(circleToSliderCount, sliderToCircleCount);
        double? balance = maximumDirectionCount == 0
            ? null
            : (double)Math.Min(circleToSliderCount, sliderToCircleCount) / maximumDirectionCount;
        double countSignal = Math.Clamp(transitionCount / 6.0, 0, 1);
        double[] scoreSignals = balance.HasValue
            ? [transitionRatio, countSignal, balance.Value]
            : [transitionRatio, countSignal];
        double score = Math.Clamp(scoreSignals.Average(), 0, 1);

        string reason = objectCount < options.MinimumTransitionSequenceObjects
            ? "TooShort"
            : transitionCount < options.MinimumTransitionCount
                ? "TooFewTransitions"
                : transitionRatio < options.MinimumTransitionRatio
                    ? "InsufficientTransitionRatio"
                    : "Qualified";

        return new CircleSliderTransitionPatternSequence(
            startIndex,
            endIndex,
            objects[startIndex].Time,
            objects[endIndex].Time,
            objectTypes,
            gaps,
            circleCount,
            sliderCount,
            transitionCount,
            circleToSliderCount,
            sliderToCircleCount,
            longestSameTypeRun,
            balance,
            score,
            reason == "Qualified",
            reason);
    }

    private static void Validate(CircleSliderTransitionPatternOptions options)
    {
        if (options.MinimumTransitionSequenceObjects < 2)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumTransitionSequenceObjects must be at least 2.");
        if (options.MinimumTransitionCount < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumTransitionCount must be at least 1.");
        if (!double.IsFinite(options.MinimumTransitionRatio) ||
            options.MinimumTransitionRatio < 0 ||
            options.MinimumTransitionRatio > 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumTransitionRatio must be in [0,1].");
        if (!double.IsFinite(options.MaximumTransitionObjectGap) ||
            options.MaximumTransitionObjectGap < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumTransitionObjectGap must not be negative.");
        if (options.MaximumConsecutiveSameType < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumConsecutiveSameType must be at least 1.");
    }

    private sealed record CircleSliderCandidate(int StartIndex, int EndIndex);
}
