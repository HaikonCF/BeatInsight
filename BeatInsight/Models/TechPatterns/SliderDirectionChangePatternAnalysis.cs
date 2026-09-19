namespace BeatInsight.Models.TechPatterns;

/// <summary>Configurable V1 thresholds for local direction changes around sliders.</summary>
public sealed record SliderDirectionChangePatternOptions(
    double MinimumDirectionChangeAngle = 60,
    double MinimumTransitionDistance = 20);

/// <summary>
/// One slider-centred local observation. BasePathEndPosition describes the intrinsic
/// forward chord; SliderFinalPosition describes the post-repeat playable endpoint.
/// </summary>
public sealed record SliderDirectionChangePatternOccurrence(
    int SliderIndex,
    int? PreviousObjectIndex,
    int? NextObjectIndex,
    int StartTime,
    double? EndTime,
    SliderPatternPosition? PreviousPosition,
    SliderPatternPosition SliderStartPosition,
    SliderPatternPosition SliderBaseEndPosition,
    SliderPatternPosition SliderFinalPosition,
    SliderPatternPosition? NextPosition,
    double? IncomingToSliderAngle,
    double? SliderToOutgoingAngle,
    double? IncomingToOutgoingAngle,
    bool EntryDirectionChange,
    bool ExitDirectionChange,
    int DirectionChangeCount,
    double MaximumDirectionChangeAngle,
    double AverageDirectionChangeAngle,
    double DirectionChangeScore,
    bool Qualified,
    string Reason);

/// <summary>
/// Descriptive Slider Direction Change result. Each qualified slider-centred local
/// observation is one sequence; object coverage deduplicates its significant transitions.
/// </summary>
public sealed record SliderDirectionChangePatternAnalysis(
    int TotalObjects,
    int TotalSliders,
    int DirectionChangeSequenceCount,
    int DirectionChangeSliderCount,
    int DirectionChangeObjectCount,
    int EntryChangeCount,
    int ExitChangeCount,
    int BothChangeCount,
    double AverageDirectionChangeAngle,
    double MaximumDirectionChangeAngle,
    double AverageDirectionChangeScore,
    double Confidence,
    IReadOnlyList<SliderDirectionChangePatternOccurrence> Occurrences)
{
    public IReadOnlyList<SliderDirectionChangePatternOccurrence> QualifiedOccurrences =>
        Occurrences.Where(occurrence => occurrence.Qualified).ToArray();

    public double Coverage => TotalObjects == 0
        ? 0
        : (double)DirectionChangeObjectCount / TotalObjects;

    public double SliderCoverage => TotalSliders == 0
        ? 0
        : (double)DirectionChangeSliderCount / TotalSliders;
}
