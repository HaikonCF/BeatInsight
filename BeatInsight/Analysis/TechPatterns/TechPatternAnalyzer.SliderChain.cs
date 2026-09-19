using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.TechPatterns;

namespace BeatInsight.Analysis.TechPatterns;

public static partial class TechPatternAnalyzer
{
    private const double SliderChainConfidenceFullLength = 8;

    private static SliderChainPatternAnalysis AnalyzeSliderChainCore(
        IReadOnlyList<HitObject> objects,
        SliderChainPatternOptions options)
    {
        ArgumentNullException.ThrowIfNull(objects);
        Validate(options);

        SliderChainPatternSequence[] runs = SegmentSliderChainCandidates(objects, options)
            .Select(candidate => CreateSequence(objects, candidate.StartIndex, candidate.EndIndex, options))
            .ToArray();
        SliderChainPatternSequence[] sequences = runs.Where(run => run.Qualified).ToArray();

        var involvedObjects = new HashSet<int>();
        var involvedSliders = new HashSet<int>();
        foreach (SliderChainPatternSequence sequence in sequences)
        {
            for (int index = sequence.StartIndex; index <= sequence.EndIndex; index++)
            {
                involvedObjects.Add(index);
                if (IsSlider(objects[index]))
                    involvedSliders.Add(index);
            }
        }

        int totalSliders = objects.Count(IsSlider);
        int longest = sequences.Length == 0 ? 0 : sequences.Max(sequence => sequence.ObjectCount);
        double averageLength = sequences.Length == 0 ? 0 : sequences.Average(sequence => sequence.ObjectCount);
        double averageSliders = sequences.Length == 0 ? 0 : sequences.Average(sequence => sequence.SliderCount);
        double averageRatio = sequences.Length == 0 ? 0 : sequences.Average(sequence => sequence.SliderRatio);
        double averageDuration = sequences.Length == 0 ? 0 : sequences.Average(sequence => sequence.Duration);
        double coverage = objects.Count == 0 ? 0 : (double)involvedObjects.Count / objects.Count;
        double sliderCoverage = totalSliders == 0 ? 0 : (double)involvedSliders.Count / totalSliders;
        double confidence = SliderChainConfidence(
            sequences.Length,
            coverage,
            sliderCoverage,
            averageRatio,
            averageLength);

        var result = new SliderChainPatternAnalysis(
            objects.Count,
            totalSliders,
            runs.Length,
            involvedObjects.Count,
            involvedSliders.Count,
            longest,
            averageLength,
            averageSliders,
            averageRatio,
            averageDuration,
            confidence,
            runs);

        GameplayDebug.SliderChainPattern(
            result,
            GameplayDebug.SliderChainPatternEnabled,
            GameplayDebug.SliderChainPatternDetailedEnabled);
        return result;
    }

    private static IReadOnlyList<SliderChainCandidate> SegmentSliderChainCandidates(
        IReadOnlyList<HitObject> objects,
        SliderChainPatternOptions options)
    {
        var candidates = new List<SliderChainCandidate>();
        int startIndex = -1;
        int lastSliderIndex = -1;
        int consecutiveNonSliders = 0;

        void FinalizeCandidate()
        {
            if (startIndex >= 0)
                candidates.Add(new SliderChainCandidate(startIndex, lastSliderIndex));
            startIndex = -1;
            lastSliderIndex = -1;
            consecutiveNonSliders = 0;
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

            if (startIndex >= 0 && index > 0)
            {
                double gap = current.Time - objects[index - 1].Time;
                if (gap < 0 || gap > options.MaximumObjectGap)
                    FinalizeCandidate();
            }

            if (slider)
            {
                if (startIndex < 0)
                    startIndex = index;
                lastSliderIndex = index;
                consecutiveNonSliders = 0;
                continue;
            }

            if (startIndex < 0)
                continue;

            consecutiveNonSliders++;
            if (consecutiveNonSliders > options.MaximumConsecutiveNonSliders)
                FinalizeCandidate();
        }

        FinalizeCandidate();
        return candidates;
    }

    private static SliderChainPatternSequence CreateSequence(
        IReadOnlyList<HitObject> objects,
        int startIndex,
        int endIndex,
        SliderChainPatternOptions options)
    {
        double[] gaps = Enumerable.Range(startIndex + 1, endIndex - startIndex)
            .Select(index => (double)(objects[index].Time - objects[index - 1].Time))
            .ToArray();
        int sliderCount = Enumerable.Range(startIndex, endIndex - startIndex + 1)
            .Count(index => IsSlider(objects[index]));
        int circleCount = endIndex - startIndex + 1 - sliderCount;
        double sliderRatio = (double)sliderCount / (endIndex - startIndex + 1);
        string reason = sliderCount < options.MinimumSliderChainSliders
            ? "TooFewSliders"
            : sliderRatio < options.MinimumSliderRatio
                ? "InsufficientSliderRatio"
                : "Qualified";

        return new SliderChainPatternSequence(
            startIndex,
            endIndex,
            objects[startIndex].Time,
            objects[endIndex].Time,
            gaps,
            sliderCount,
            circleCount,
            reason == "Qualified",
            reason);
    }

    private static void Validate(SliderChainPatternOptions options)
    {
        if (options.MinimumSliderChainSliders < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumSliderChainSliders must be at least 1.");
        if (options.MinimumSliderRatio < 0 || options.MinimumSliderRatio > 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumSliderRatio must be in [0,1].");
        if (options.MaximumObjectGap < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumObjectGap must not be negative.");
        if (options.MaximumConsecutiveNonSliders < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumConsecutiveNonSliders must not be negative.");
    }

    private static double SliderChainConfidence(
        int sequenceCount,
        double coverage,
        double sliderCoverage,
        double averageSliderRatio,
        double averageChainLength)
    {
        if (sequenceCount == 0)
            return 0;

        double sequenceSignal = (double)sequenceCount / (sequenceCount + 1);
        double lengthSignal = Math.Clamp(averageChainLength / SliderChainConfidenceFullLength, 0, 1);
        return Math.Clamp(
            (sequenceSignal + coverage + sliderCoverage + averageSliderRatio + lengthSignal) / 5,
            0,
            1);
    }

    private static bool IsSlider(HitObject hitObject) => (hitObject.Type & 2) == 2;
    private static bool IsCircle(HitObject hitObject) => (hitObject.Type & 1) == 1;

    private sealed record SliderChainCandidate(int StartIndex, int EndIndex);
}
