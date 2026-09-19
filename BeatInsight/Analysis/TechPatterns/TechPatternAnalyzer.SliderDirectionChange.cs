using System.Numerics;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.TechPatterns;

namespace BeatInsight.Analysis.TechPatterns;

public static partial class TechPatternAnalyzer
{
    private static SliderDirectionChangePatternAnalysis AnalyzeSliderDirectionChangeCore(
        Beatmap beatmap,
        SliderDirectionChangePatternOptions options)
    {
        ArgumentNullException.ThrowIfNull(beatmap);
        Validate(options);

        IReadOnlyList<GameplayEvent> events = GameplayEventBuilder.Build(beatmap);
        SliderDirectionChangePatternOccurrence[] occurrences = events
            .Where(gameplayEvent => gameplayEvent.IsSlider)
            .Select(slider => CreateDirectionChangeOccurrence(
                beatmap.HitObjects,
                events,
                slider.Index,
                options))
            .ToArray();
        SliderDirectionChangePatternOccurrence[] qualified = occurrences
            .Where(occurrence => occurrence.Qualified)
            .ToArray();

        var involvedObjects = new HashSet<int>();
        var significantAngles = new List<double>();
        foreach (SliderDirectionChangePatternOccurrence occurrence in qualified)
        {
            involvedObjects.Add(occurrence.SliderIndex);
            if (occurrence.EntryDirectionChange && occurrence.PreviousObjectIndex is { } previousIndex)
            {
                involvedObjects.Add(previousIndex);
                significantAngles.Add(occurrence.IncomingToSliderAngle!.Value);
            }
            if (occurrence.ExitDirectionChange && occurrence.NextObjectIndex is { } nextIndex)
            {
                involvedObjects.Add(nextIndex);
                significantAngles.Add(occurrence.SliderToOutgoingAngle!.Value);
            }
        }

        int count = qualified.Length;
        int totalSliders = occurrences.Length;
        double coverage = beatmap.HitObjects.Count == 0
            ? 0
            : (double)involvedObjects.Count / beatmap.HitObjects.Count;
        double sliderCoverage = totalSliders == 0 ? 0 : (double)count / totalSliders;
        double averageAngle = significantAngles.Count == 0 ? 0 : significantAngles.Average();
        double maximumAngle = significantAngles.Count == 0 ? 0 : significantAngles.Max();
        double averageScore = count == 0 ? 0 : qualified.Average(occurrence => occurrence.DirectionChangeScore);
        double countSignal = count == 0 ? 0 : (double)count / (count + 1);
        double confidence = count == 0
            ? 0
            : Math.Clamp((countSignal + coverage + sliderCoverage + averageScore) / 4, 0, 1);

        var result = new SliderDirectionChangePatternAnalysis(
            beatmap.HitObjects.Count,
            totalSliders,
            count,
            count,
            involvedObjects.Count,
            qualified.Count(occurrence => occurrence.EntryDirectionChange),
            qualified.Count(occurrence => occurrence.ExitDirectionChange),
            qualified.Count(occurrence => occurrence.EntryDirectionChange && occurrence.ExitDirectionChange),
            averageAngle,
            maximumAngle,
            averageScore,
            confidence,
            occurrences);

        GameplayDebug.SliderDirectionChangePattern(
            result,
            GameplayDebug.SliderDirectionChangePatternEnabled,
            GameplayDebug.SliderDirectionChangePatternDetailedEnabled);
        return result;
    }

    private static SliderDirectionChangePatternOccurrence CreateDirectionChangeOccurrence(
        IReadOnlyList<HitObject> objects,
        IReadOnlyList<GameplayEvent> events,
        int sliderIndex,
        SliderDirectionChangePatternOptions options)
    {
        HitObject sliderObject = objects[sliderIndex];
        GameplayEvent sliderEvent = events[sliderIndex];
        GameplayEvent? previousEvent = sliderIndex > 0 ? events[sliderIndex - 1] : null;
        GameplayEvent? nextEvent = sliderIndex + 1 < events.Count ? events[sliderIndex + 1] : null;

        Vector2 sliderStart = sliderEvent.StartPosition;
        Vector2 basePathEnd = sliderObject.SliderControlPoints.Count == 0
            ? sliderStart
            : new Vector2(
                sliderObject.SliderControlPoints[^1].X,
                sliderObject.SliderControlPoints[^1].Y);
        Vector2 sliderFinal = sliderEvent.EffectiveEndPosition;

        Vector2? intrinsicSliderDirection = sliderEvent.EndpointReliable
            ? ReliableDirection(sliderStart, basePathEnd, options.MinimumTransitionDistance)
            : null;
        Vector2? incomingDirection = previousEvent is { IsSpinner: false, EndpointReliable: true }
            ? ReliableDirection(
                previousEvent.EffectiveEndPosition,
                sliderStart,
                options.MinimumTransitionDistance)
            : null;
        Vector2? outgoingDirection = nextEvent is { IsSpinner: false } && sliderEvent.EndpointReliable
            ? ReliableDirection(
                sliderFinal,
                nextEvent.StartPosition,
                options.MinimumTransitionDistance)
            : null;

        Vector2? terminalSliderDirection = intrinsicSliderDirection;
        if (terminalSliderDirection.HasValue && sliderEvent.SliderRepeats % 2 == 1)
            terminalSliderDirection = -terminalSliderDirection.Value;

        double? entryAngle = AbsoluteAngleDegrees(incomingDirection, intrinsicSliderDirection);
        double? exitAngle = AbsoluteAngleDegrees(terminalSliderDirection, outgoingDirection);
        double? throughAngle = AbsoluteAngleDegrees(incomingDirection, outgoingDirection);
        bool entryChange = entryAngle >= options.MinimumDirectionChangeAngle;
        bool exitChange = exitAngle >= options.MinimumDirectionChangeAngle;
        int directionChangeCount = (entryChange ? 1 : 0) + (exitChange ? 1 : 0);
        double[] availableAngles = new double?[] { entryAngle, exitAngle }
            .Where(angle => angle.HasValue)
            .Select(angle => angle!.Value)
            .ToArray();
        double maximumAngle = availableAngles.Length == 0 ? 0 : availableAngles.Max();
        double averageAngle = availableAngles.Length == 0 ? 0 : availableAngles.Average();
        double score = availableAngles.Length == 0
            ? 0
            : Math.Clamp(availableAngles.Average(angle => angle / 180), 0, 1);
        bool qualified = directionChangeCount > 0;
        string reason = availableAngles.Length == 0
            ? "NoReliableTransition"
            : qualified
                ? "Qualified"
                : "BelowDirectionThreshold";

        return new SliderDirectionChangePatternOccurrence(
            sliderIndex,
            previousEvent?.Index,
            nextEvent?.Index,
            sliderObject.Time,
            sliderEvent.SliderDuration.HasValue ? sliderEvent.EffectiveEndTime : null,
            previousEvent is null ? null : Position(previousEvent.EffectiveEndPosition),
            Position(sliderStart),
            Position(basePathEnd),
            Position(sliderFinal),
            nextEvent is null ? null : Position(nextEvent.StartPosition),
            entryAngle,
            exitAngle,
            throughAngle,
            entryChange,
            exitChange,
            directionChangeCount,
            maximumAngle,
            averageAngle,
            score,
            qualified,
            reason);
    }

    private static Vector2? ReliableDirection(Vector2 from, Vector2 to, double minimumDistance)
    {
        Vector2 displacement = to - from;
        return displacement.Length() < minimumDistance
            ? null
            : GameplayEventBuilder.Unit(displacement);
    }

    private static double? AbsoluteAngleDegrees(Vector2? first, Vector2? second)
    {
        double? angle = LocalPatternContext.Angle(first, second);
        return angle.HasValue ? Math.Abs(angle.Value) * 180 / Math.PI : null;
    }

    private static SliderPatternPosition Position(Vector2 position) =>
        new(position.X, position.Y);

    private static void Validate(SliderDirectionChangePatternOptions options)
    {
        if (!double.IsFinite(options.MinimumDirectionChangeAngle) ||
            options.MinimumDirectionChangeAngle < 0 ||
            options.MinimumDirectionChangeAngle > 180)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumDirectionChangeAngle must be in [0,180].");
        if (!double.IsFinite(options.MinimumTransitionDistance) ||
            options.MinimumTransitionDistance <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumTransitionDistance must be greater than 0.");
    }
}
