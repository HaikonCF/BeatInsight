using BeatInsight.Models;

namespace BeatInsight.Analysis;

internal sealed record TechV2ExperimentalSliderTransitionTopology(
    int TransitionCount,
    double SliderToSliderRatio,
    double SliderToCircleRatio,
    double CircleToSliderRatio,
    double CircleToCircleRatio);

internal sealed record TechV2ExperimentalWindowResult(
    int StartIndex,
    int EndIndex,
    double StartTime,
    double EndTime,
    double GeometryIrregularity,
    double RhythmIrregularity,
    double SliderComplexity,
    double Awkwardness,
    double SpacingVariation,
    double TechLikelihoodExperimental,
    double AngleCoherenceWithSupport,
    int AngleCoherenceStructuralPivotCount,
    TechV2ExperimentalSliderTransitionTopology ComplexSliderTransitionTopology,
    int ComplexSliderRunLength,
    double CoupledTechnicalMotifContinuity,
    int CoupledTechnicalMotifRunLength);

internal static class TechDetectorV2Experimental
{
    internal const int WindowSize = 12;
    internal const int Stride = 4;

    internal static IReadOnlyList<TechV2ExperimentalWindowResult> Analyze(
        IReadOnlyList<HitObject> objects,
        Func<HitObject, bool> isSpinner,
        Func<HitObject, bool> isSlider,
        Func<HitObject, bool> isComplexSlider,
        Func<HitObject, (double X, double Y)> getObjectStartPosition,
        Func<HitObject, (double X, double Y)> getObjectEndPosition,
        Func<HitObject, HitObject, HitObject, double> getTechTurnAngle)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(isSpinner);
        ArgumentNullException.ThrowIfNull(isSlider);
        ArgumentNullException.ThrowIfNull(isComplexSlider);
        ArgumentNullException.ThrowIfNull(getObjectStartPosition);
        ArgumentNullException.ThrowIfNull(getObjectEndPosition);
        ArgumentNullException.ThrowIfNull(getTechTurnAngle);

        if (objects.Count < WindowSize)
            return Array.Empty<TechV2ExperimentalWindowResult>();

        List<TechV2ExperimentalWindowResult> windows = [];

        for (int start = 0; start + WindowSize <= objects.Count; start += Stride)
        {
            int end = start + WindowSize - 1;

            (double geometry, double awkwardness, double spacingVariation) =
                ComputeGeometryAwkwardSpacing(
                    objects,
                    start,
                    end,
                    isSpinner,
                    getObjectStartPosition,
                    getObjectEndPosition,
                    getTechTurnAngle);

            double rhythmIrregularity =
                ComputeRhythmIrregularity(
                    objects,
                    start,
                    end,
                    isSpinner);

            double sliderComplexity =
                ComputeSliderComplexity(
                    objects,
                    start,
                    end,
                    isSlider,
                    isComplexSlider);

            // Observation-only diagnostics. These values deliberately do not
            // contribute to TechLikelihoodExperimental in Phase 1D.
            (double angleCoherenceWithSupport, int structuralPivotCount) =
                ComputeAngleCoherenceWithSupport(
                    objects,
                    start,
                    end,
                    isSpinner,
                    getObjectStartPosition,
                    getObjectEndPosition,
                    getTechTurnAngle);

            (TechV2ExperimentalSliderTransitionTopology sliderTopology,
                int complexSliderRunLength) =
                ComputeSliderTransitionTopology(
                    objects,
                    start,
                    end,
                    isSpinner,
                    isSlider,
                    isComplexSlider);

            (double coupledTechnicalMotifContinuity,
                int coupledTechnicalMotifRunLength) =
                ComputeCoupledTechnicalMotifContinuity(
                    objects,
                    start,
                    end,
                    isSpinner,
                    isComplexSlider,
                    getObjectStartPosition,
                    getObjectEndPosition,
                    getTechTurnAngle);

            double techLikelihoodExperimental = Math.Clamp(
                geometry * 0.25
                + rhythmIrregularity * 0.20
                + sliderComplexity * 0.20
                + awkwardness * 0.20
                + spacingVariation * 0.15,
                0,
                1);

            windows.Add(new TechV2ExperimentalWindowResult(
                start,
                end,
                objects[start].Time,
                objects[end].Time,
                geometry,
                rhythmIrregularity,
                sliderComplexity,
                awkwardness,
                spacingVariation,
                techLikelihoodExperimental,
                angleCoherenceWithSupport,
                structuralPivotCount,
                sliderTopology,
                complexSliderRunLength,
                coupledTechnicalMotifContinuity,
                coupledTechnicalMotifRunLength));
        }

        return windows;
    }

    internal static IReadOnlyList<TechV2ExperimentalWindowResult> TopByLikelihood(
        IReadOnlyList<TechV2ExperimentalWindowResult> windows,
        int count)
    {
        ArgumentNullException.ThrowIfNull(windows);

        if (count <= 0 || windows.Count == 0)
            return Array.Empty<TechV2ExperimentalWindowResult>();

        return windows
            .OrderByDescending(window => window.TechLikelihoodExperimental)
            .ThenBy(window => window.StartIndex)
            .Take(count)
            .ToArray();
    }

    private static (double Geometry, double Awkwardness, double SpacingVariation)
        ComputeGeometryAwkwardSpacing(
            IReadOnlyList<HitObject> objects,
            int start,
            int end,
            Func<HitObject, bool> isSpinner,
            Func<HitObject, (double X, double Y)> getObjectStartPosition,
            Func<HitObject, (double X, double Y)> getObjectEndPosition,
            Func<HitObject, HitObject, HitObject, double> getTechTurnAngle)
    {
        int validTransitions = 0;
        int structuralCount = 0;
        int compactCount = 0;
        int sharpCount = 0;
        int awkwardCount = 0;
        int spacingVariationCount = 0;

        for (int i = start + 1; i <= end - 1; i++)
        {
            HitObject previous = objects[i - 1];
            HitObject current = objects[i];
            HitObject next = objects[i + 1];

            if (isSpinner(previous) || isSpinner(current) || isSpinner(next))
                continue;

            double firstInterval = current.Time - previous.Time;
            double secondInterval = next.Time - current.Time;

            if (firstInterval <= 0 || secondInterval <= 0)
                continue;

            (double X, double Y) previousEnd = getObjectEndPosition(previous);
            (double X, double Y) currentStart = getObjectStartPosition(current);
            (double X, double Y) currentEnd = getObjectEndPosition(current);
            (double X, double Y) nextStart = getObjectStartPosition(next);

            double firstDistance = Distance(previousEnd, currentStart);
            double secondDistance = Distance(currentEnd, nextStart);

            if (firstDistance > 160 || secondDistance > 160)
                continue;

            validTransitions++;

            double turnAngle = getTechTurnAngle(previous, current, next);
            double averageDistance = (firstDistance + secondDistance) / 2.0;

            bool isStructural = turnAngle >= 110;
            bool isCompact = firstDistance <= 110 && secondDistance <= 110;
            bool isSharp = turnAngle >= 150 && averageDistance <= 125;
            bool isAwkward =
                turnAngle >= 45
                && turnAngle <= 150
                && averageDistance <= 125;
            bool hasSpacingVariation = Math.Abs(firstDistance - secondDistance) >= 35;

            if (isStructural)
                structuralCount++;

            if (isCompact)
                compactCount++;

            if (isSharp)
                sharpCount++;

            if (isAwkward)
                awkwardCount++;

            if (hasSpacingVariation)
                spacingVariationCount++;
        }

        if (validTransitions == 0)
            return (0, 0, 0);

        double structuralRatio = (double)structuralCount / validTransitions;
        double compactRatio = (double)compactCount / validTransitions;
        double sharpRatio = (double)sharpCount / validTransitions;
        double awkwardRatio = (double)awkwardCount / validTransitions;
        double spacingVariationRatio = (double)spacingVariationCount / validTransitions;

        double geometry = Math.Clamp(
            structuralRatio * 0.50
            + compactRatio * 0.20
            + sharpRatio * 0.30,
            0,
            1);

        return (geometry, awkwardRatio, spacingVariationRatio);
    }

    private static double ComputeRhythmIrregularity(
        IReadOnlyList<HitObject> objects,
        int start,
        int end,
        Func<HitObject, bool> isSpinner)
    {
        double previousInterval = -1;
        double totalVariation = 0;
        int variationCount = 0;

        for (int i = start + 1; i <= end; i++)
        {
            HitObject previous = objects[i - 1];
            HitObject current = objects[i];

            if (isSpinner(previous) || isSpinner(current))
                continue;

            double interval = current.Time - previous.Time;

            if (interval <= 0)
                continue;

            if (previousInterval > 0)
            {
                double variation = Math.Abs(interval - previousInterval)
                    / Math.Max(interval, previousInterval);

                totalVariation += Math.Clamp(variation, 0, 1);
                variationCount++;
            }

            previousInterval = interval;
        }

        if (variationCount == 0)
            return 0;

        return Math.Clamp(totalVariation / variationCount, 0, 1);
    }

    private static double ComputeSliderComplexity(
        IReadOnlyList<HitObject> objects,
        int start,
        int end,
        Func<HitObject, bool> isSlider,
        Func<HitObject, bool> isComplexSlider)
    {
        int sliderCount = 0;
        int complexSliderCount = 0;
        double slideIntensity = 0;

        for (int i = start; i <= end; i++)
        {
            HitObject hitObject = objects[i];

            if (!isSlider(hitObject))
                continue;

            sliderCount++;

            if (isComplexSlider(hitObject))
                complexSliderCount++;

            slideIntensity += Math.Clamp((hitObject.Slides - 1) / 3.0, 0, 1);
        }

        if (sliderCount == 0)
            return 0;

        double complexRatio = (double)complexSliderCount / sliderCount;
        double averageSlideIntensity = slideIntensity / sliderCount;

        return Math.Clamp(
            complexRatio * 0.80
            + averageSlideIntensity * 0.20,
            0,
            1);
    }

    private static (double CoherenceWithSupport, int StructuralPivotCount)
        ComputeAngleCoherenceWithSupport(
            IReadOnlyList<HitObject> objects,
            int start,
            int end,
            Func<HitObject, bool> isSpinner,
            Func<HitObject, (double X, double Y)> getObjectStartPosition,
            Func<HitObject, (double X, double Y)> getObjectEndPosition,
            Func<HitObject, HitObject, HitObject, double> getTechTurnAngle)
    {
        List<double> structuralAngles = [];

        for (int i = start + 1; i <= end - 1; i++)
        {
            HitObject previous = objects[i - 1];
            HitObject current = objects[i];
            HitObject next = objects[i + 1];

            if (isSpinner(previous) || isSpinner(current) || isSpinner(next))
                continue;

            double firstInterval = current.Time - previous.Time;
            double secondInterval = next.Time - current.Time;

            if (firstInterval <= 0 || secondInterval <= 0)
                continue;

            double firstDistance = Distance(
                getObjectEndPosition(previous),
                getObjectStartPosition(current));
            double secondDistance = Distance(
                getObjectEndPosition(current),
                getObjectStartPosition(next));

            if (firstDistance > 160 || secondDistance > 160)
                continue;

            double turnAngle = getTechTurnAngle(previous, current, next);

            // Reuses the existing V2 structural-angle definition.
            if (turnAngle >= 110)
                structuralAngles.Add(turnAngle);
        }

        if (structuralAngles.Count == 0)
            return (0, 0);

        double mean = structuralAngles.Average();
        double variance = structuralAngles.Average(
            angle => Math.Pow(angle - mean, 2));

        // Structural angles live in the existing [110, 180] range.
        double normalizedCoherence = 1 - Math.Clamp(
            variance / Math.Pow(180 - 110, 2),
            0,
            1);

        // A twelve-object window has at most ten pivot triples. This support
        // factor prevents one or two structurally valid pivots from reading as
        // highly coherent solely because their variance is zero or near zero.
        double support = (double)structuralAngles.Count / (WindowSize - 2);

        return (
            Math.Clamp(normalizedCoherence * support, 0, 1),
            structuralAngles.Count);
    }

    private static (
        TechV2ExperimentalSliderTransitionTopology Topology,
        int ComplexSliderRunLength)
        ComputeSliderTransitionTopology(
            IReadOnlyList<HitObject> objects,
            int start,
            int end,
            Func<HitObject, bool> isSpinner,
            Func<HitObject, bool> isSlider,
            Func<HitObject, bool> isComplexSlider)
    {
        int sliderToSlider = 0;
        int sliderToCircle = 0;
        int circleToSlider = 0;
        int circleToCircle = 0;
        int transitionCount = 0;
        int runLength = 0;
        int maxRunLength = 0;

        for (int i = start; i <= end; i++)
        {
            if (isComplexSlider(objects[i]))
            {
                runLength++;
                maxRunLength = Math.Max(maxRunLength, runLength);
            }
            else
            {
                runLength = 0;
            }

            if (i == end)
                continue;

            HitObject current = objects[i];
            HitObject next = objects[i + 1];

            if (isSpinner(current) || isSpinner(next))
                continue;

            bool currentIsSlider = isSlider(current);
            bool nextIsSlider = isSlider(next);

            transitionCount++;

            if (currentIsSlider && nextIsSlider)
                sliderToSlider++;
            else if (currentIsSlider)
                sliderToCircle++;
            else if (nextIsSlider)
                circleToSlider++;
            else
                circleToCircle++;
        }

        if (transitionCount == 0)
        {
            return (
                new TechV2ExperimentalSliderTransitionTopology(0, 0, 0, 0, 0),
                maxRunLength);
        }

        return (
            new TechV2ExperimentalSliderTransitionTopology(
                transitionCount,
                (double)sliderToSlider / transitionCount,
                (double)sliderToCircle / transitionCount,
                (double)circleToSlider / transitionCount,
                (double)circleToCircle / transitionCount),
            maxRunLength);
    }

    private static (double Continuity, int LongestRunLength)
        ComputeCoupledTechnicalMotifContinuity(
            IReadOnlyList<HitObject> objects,
            int start,
            int end,
            Func<HitObject, bool> isSpinner,
            Func<HitObject, bool> isComplexSlider,
            Func<HitObject, (double X, double Y)> getObjectStartPosition,
            Func<HitObject, (double X, double Y)> getObjectEndPosition,
            Func<HitObject, HitObject, HitObject, double> getTechTurnAngle)
    {
        int runLength = 0;
        int maxRunLength = 0;

        for (int i = start + 1; i <= end - 1; i++)
        {
            HitObject previous = objects[i - 1];
            HitObject current = objects[i];
            HitObject next = objects[i + 1];

            bool hasMotif = false;

            if (!isSpinner(previous) && !isSpinner(current) && !isSpinner(next))
            {
                double firstInterval = current.Time - previous.Time;
                double secondInterval = next.Time - current.Time;

                if (firstInterval > 0 && secondInterval > 0)
                {
                    double firstDistance = Distance(
                        getObjectEndPosition(previous),
                        getObjectStartPosition(current));
                    double secondDistance = Distance(
                        getObjectEndPosition(current),
                        getObjectStartPosition(next));

                    if (firstDistance <= 160 && secondDistance <= 160)
                    {
                        double turnAngle = getTechTurnAngle(previous, current, next);
                        double averageDistance =
                            (firstDistance + secondDistance) / 2;
                        double rhythmVariation = Math.Abs(
                            secondInterval - firstInterval)
                            / Math.Max(secondInterval, firstInterval);

                        bool structural = turnAngle >= 110;
                        bool directional =
                            turnAngle >= 45 && averageDistance <= 125;
                        bool complexSliderContext =
                            isComplexSlider(previous)
                            || isComplexSlider(current)
                            || isComplexSlider(next);

                        hasMotif =
                            structural
                            && directional
                            && complexSliderContext
                            && rhythmVariation > 0;
                    }
                }
            }

            runLength = hasMotif ? runLength + 1 : 0;
            maxRunLength = Math.Max(maxRunLength, runLength);
        }

        return (
            (double)maxRunLength / (WindowSize - 2),
            maxRunLength);
    }

    private static double Distance(
        (double X, double Y) first,
        (double X, double Y) second)
    {
        double x = second.X - first.X;
        double y = second.Y - first.Y;

        return Math.Sqrt(x * x + y * y);
    }
}
