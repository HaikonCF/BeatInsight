using BeatInsight.Models;
using BeatInsight.Models.TechPatterns;

namespace BeatInsight.Analysis.TechPatterns;

/// <summary>
/// Entry point for deterministic descriptive Tech patterns. Results are independent from
/// historical Tech scoring, TechV2, gameplay identity and rating.
/// </summary>
public static partial class TechPatternAnalyzer
{
    public static SliderChainPatternAnalysis AnalyzeSliderChain(
        IReadOnlyList<HitObject> objects,
        SliderChainPatternOptions? options = null) =>
        AnalyzeSliderChainCore(objects, options ?? new SliderChainPatternOptions());

    public static ComplexSliderPatternAnalysis AnalyzeComplexSlider(
        IReadOnlyList<HitObject> objects,
        ComplexSliderPatternOptions? options = null) =>
        AnalyzeComplexSliderCore(objects, options ?? new ComplexSliderPatternOptions(), null);

    public static ComplexSliderPatternAnalysis AnalyzeComplexSlider(
        Beatmap beatmap,
        ComplexSliderPatternOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(beatmap);
        IReadOnlyDictionary<int, SliderTiming> timings = GameplayEventBuilder.Build(beatmap)
            .Where(gameplayEvent => gameplayEvent.IsSlider && gameplayEvent.SliderDuration.HasValue)
            .ToDictionary(
                gameplayEvent => gameplayEvent.Index,
                gameplayEvent => new SliderTiming(
                    gameplayEvent.EffectiveEndTime,
                    gameplayEvent.SliderDuration!.Value));
        return AnalyzeComplexSliderCore(
            beatmap.HitObjects,
            options ?? new ComplexSliderPatternOptions(),
            timings);
    }

    public static SliderDirectionChangePatternAnalysis AnalyzeSliderDirectionChange(
        IReadOnlyList<HitObject> objects,
        SliderDirectionChangePatternOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(objects);
        var beatmap = new Beatmap { HitObjects = objects.ToList() };
        return AnalyzeSliderDirectionChangeCore(
            beatmap,
            options ?? new SliderDirectionChangePatternOptions());
    }

    public static SliderDirectionChangePatternAnalysis AnalyzeSliderDirectionChange(
        Beatmap beatmap,
        SliderDirectionChangePatternOptions? options = null) =>
        AnalyzeSliderDirectionChangeCore(
            beatmap,
            options ?? new SliderDirectionChangePatternOptions());

    public static CircleSliderTransitionPatternAnalysis AnalyzeCircleSliderTransition(
        IReadOnlyList<HitObject> objects,
        CircleSliderTransitionPatternOptions? options = null) =>
        AnalyzeCircleSliderTransitionCore(
            objects,
            options ?? new CircleSliderTransitionPatternOptions());

    public static RhythmTransitionPatternAnalysis AnalyzeRhythmTransition(
        IReadOnlyList<HitObject> objects,
        RhythmTransitionPatternOptions? options = null) =>
        AnalyzeRhythmTransitionCore(
            objects,
            options ?? new RhythmTransitionPatternOptions());

    public static SpatialAwkwardTransitionPatternAnalysis AnalyzeSpatialAwkwardTransition(
        IReadOnlyList<HitObject> objects,
        SpatialAwkwardTransitionPatternOptions? options = null) =>
        AnalyzeSpatialAwkwardTransitionCore(
            objects,
            options ?? new SpatialAwkwardTransitionPatternOptions());

    public static OverlapPatternAnalysis AnalyzeOverlap(
        IReadOnlyList<HitObject> objects,
        OverlapPatternOptions? options = null) =>
        AnalyzeOverlapCore(
            objects,
            options ?? new OverlapPatternOptions());
}
