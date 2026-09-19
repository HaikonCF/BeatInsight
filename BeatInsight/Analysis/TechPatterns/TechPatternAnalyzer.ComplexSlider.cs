using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.TechPatterns;

namespace BeatInsight.Analysis.TechPatterns;

public static partial class TechPatternAnalyzer
{
    private const double GeometryEpsilon = 1e-8;

    private static ComplexSliderPatternAnalysis AnalyzeComplexSliderCore(
        IReadOnlyList<HitObject> objects,
        ComplexSliderPatternOptions options,
        IReadOnlyDictionary<int, SliderTiming>? timings)
    {
        ArgumentNullException.ThrowIfNull(objects);
        Validate(options);

        ComplexSliderPatternOccurrence[] sliders = objects
            .Select((hitObject, index) => (hitObject, index))
            .Where(item => IsSlider(item.hitObject))
            .Select(item => CreateComplexSliderOccurrence(
                item.hitObject,
                item.index,
                options,
                timings is not null && timings.TryGetValue(item.index, out SliderTiming? timing)
                    ? timing
                    : null))
            .ToArray();
        ComplexSliderPatternOccurrence[] qualified = sliders
            .Where(slider => slider.Qualified)
            .ToArray();

        int count = qualified.Length;
        double coverage = objects.Count == 0 ? 0 : (double)count / objects.Count;
        double sliderCoverage = sliders.Length == 0 ? 0 : (double)count / sliders.Length;
        double averageScore = count == 0 ? 0 : qualified.Average(slider => slider.SliderComplexityScore);
        double maximumScore = count == 0 ? 0 : qualified.Max(slider => slider.SliderComplexityScore);
        double averageControlPoints = count == 0 ? 0 : qualified.Average(slider => slider.ControlPointCount);
        double averageRepeats = count == 0 ? 0 : qualified.Average(slider => slider.RepeatCount);
        double[] ratios = qualified
            .Where(slider => slider.PathToChordRatio.HasValue)
            .Select(slider => slider.PathToChordRatio!.Value)
            .ToArray();
        double averageRatio = ratios.Length == 0 ? 0 : ratios.Average();
        double countSignal = count == 0 ? 0 : (double)count / (count + 1);
        double confidence = count == 0
            ? 0
            : Math.Clamp((countSignal + coverage + sliderCoverage + averageScore) / 4, 0, 1);

        var result = new ComplexSliderPatternAnalysis(
            objects.Count,
            sliders.Length,
            count,
            averageScore,
            maximumScore,
            averageControlPoints,
            averageRepeats,
            averageRatio,
            confidence,
            sliders);

        GameplayDebug.ComplexSliderPattern(
            result,
            GameplayDebug.ComplexSliderPatternEnabled,
            GameplayDebug.ComplexSliderPatternDetailedEnabled);
        return result;
    }

    private static ComplexSliderPatternOccurrence CreateComplexSliderOccurrence(
        HitObject slider,
        int objectIndex,
        ComplexSliderPatternOptions options,
        SliderTiming? timing)
    {
        var start = new SliderPatternPosition(slider.X, slider.Y);
        var effectivePoints = new List<SliderPatternPosition> { start };
        foreach (SliderControlPoint controlPoint in slider.SliderControlPoints)
        {
            var point = new SliderPatternPosition(controlPoint.X, controlPoint.Y);
            if (Distance(effectivePoints[^1], point) > GeometryEpsilon)
                effectivePoints.Add(point);
        }

        int effectiveControlPointCount = effectivePoints.Count - 1;
        SliderPatternPosition basePathEnd = effectivePoints[^1];
        var parsedEnd = new SliderPatternPosition(slider.SliderEndPosition.X, slider.SliderEndPosition.Y);
        int repeatCount = Math.Max(0, slider.Slides - 1);
        double chordLength = Distance(start, basePathEnd);
        double? pathLength = double.IsFinite(slider.Length) && slider.Length > GeometryEpsilon
            ? slider.Length
            : null;
        double? pathToChordRatio = pathLength.HasValue && chordLength > GeometryEpsilon
            ? Math.Min(pathLength.Value / chordLength, options.MaximumPathToChordRatio)
            : null;

        double[] turns = Enumerable.Range(1, Math.Max(0, effectivePoints.Count - 2))
            .Select(index => DirectionChange(
                effectivePoints[index - 1],
                effectivePoints[index],
                effectivePoints[index + 1]))
            .Where(double.IsFinite)
            .ToArray();
        int internalTurnCount = turns.Count(angle => angle >= options.MinimumInternalTurnAngle);
        double averageTurn = turns.Length == 0 ? 0 : turns.Average();
        double maximumTurn = turns.Length == 0 ? 0 : turns.Max();

        double controlPointSignal = Math.Clamp(
            (double)(effectiveControlPointCount - 1) /
            Math.Max(1, options.MinimumComplexControlPoints - 1),
            0,
            1);
        double repeatSignal = Math.Clamp((repeatCount - 1) / 2.0, 0, 1);
        double? pathSignal = pathToChordRatio.HasValue
            ? Math.Clamp(
                (pathToChordRatio.Value - 1) /
                (options.MinimumComplexPathToChordRatio - 1),
                0,
                1)
            : null;
        double? directionSignal = turns.Length > 0
            ? Math.Clamp(maximumTurn / options.MinimumInternalTurnAngle, 0, 1)
            : null;
        double[] availableSignals = new double?[]
            {
                controlPointSignal,
                repeatSignal,
                pathSignal,
                directionSignal
            }
            .Where(signal => signal.HasValue)
            .Select(signal => signal!.Value)
            .ToArray();
        double score = availableSignals.Length == 0
            ? 0
            : Math.Clamp(availableSignals.Average(), 0, 1);
        bool qualified = score >= options.MinimumSliderComplexityScore;

        return new ComplexSliderPatternOccurrence(
            objectIndex,
            slider.Time,
            timing?.EndTime,
            timing?.Duration,
            start,
            parsedEnd,
            basePathEnd,
            slider.SliderCurveType,
            slider.SliderControlPoints.Count,
            effectiveControlPointCount,
            repeatCount,
            pathLength,
            chordLength,
            pathToChordRatio,
            internalTurnCount,
            averageTurn,
            maximumTurn,
            controlPointSignal,
            repeatSignal,
            pathSignal,
            directionSignal,
            score,
            qualified,
            qualified ? "Qualified" : "TooSimple");
    }

    private static double Distance(SliderPatternPosition first, SliderPatternPosition second)
    {
        double x = second.X - first.X;
        double y = second.Y - first.Y;
        return Math.Sqrt(x * x + y * y);
    }

    private static double DirectionChange(
        SliderPatternPosition previous,
        SliderPatternPosition current,
        SliderPatternPosition next)
    {
        double firstX = current.X - previous.X;
        double firstY = current.Y - previous.Y;
        double secondX = next.X - current.X;
        double secondY = next.Y - current.Y;
        double denominator = Math.Sqrt(firstX * firstX + firstY * firstY) *
            Math.Sqrt(secondX * secondX + secondY * secondY);
        if (denominator <= GeometryEpsilon)
            return double.NaN;
        double cosine = Math.Clamp((firstX * secondX + firstY * secondY) / denominator, -1, 1);
        return Math.Acos(cosine) * 180 / Math.PI;
    }

    private static void Validate(ComplexSliderPatternOptions options)
    {
        if (options.MinimumComplexControlPoints < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumComplexControlPoints must be at least 1.");
        if (!double.IsFinite(options.MinimumComplexPathToChordRatio) ||
            options.MinimumComplexPathToChordRatio <= 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumComplexPathToChordRatio must be greater than 1.");
        if (!double.IsFinite(options.MaximumPathToChordRatio) ||
            options.MaximumPathToChordRatio < options.MinimumComplexPathToChordRatio)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumPathToChordRatio must be finite and at least MinimumComplexPathToChordRatio.");
        if (!double.IsFinite(options.MinimumInternalTurnAngle) ||
            options.MinimumInternalTurnAngle <= 0 ||
            options.MinimumInternalTurnAngle > 180)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumInternalTurnAngle must be in (0,180].");
        if (!double.IsFinite(options.MinimumSliderComplexityScore) ||
            options.MinimumSliderComplexityScore < 0 ||
            options.MinimumSliderComplexityScore > 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumSliderComplexityScore must be in [0,1].");
    }

    private sealed record SliderTiming(double EndTime, double Duration);
}
