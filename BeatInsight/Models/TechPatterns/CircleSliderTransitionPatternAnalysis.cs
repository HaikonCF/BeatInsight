namespace BeatInsight.Models.TechPatterns;

/// <summary>Configurable V1 thresholds for local Circle/Slider type alternation.</summary>
public sealed record CircleSliderTransitionPatternOptions(
    int MinimumTransitionSequenceObjects = 4,
    int MinimumTransitionCount = 3,
    double MinimumTransitionRatio = 0.60,
    double MaximumTransitionObjectGap = 1000,
    int MaximumConsecutiveSameType = 2);

/// <summary>One maximal Circle/Slider-only candidate after structural segmentation.</summary>
public sealed record CircleSliderTransitionPatternSequence(
    int StartIndex,
    int EndIndex,
    int StartTime,
    int EndTime,
    IReadOnlyList<int> ObjectTypes,
    IReadOnlyList<double> ObjectGaps,
    int CircleCount,
    int SliderCount,
    int TransitionCount,
    int CircleToSliderCount,
    int SliderToCircleCount,
    int LongestSameTypeRun,
    double? TransitionDirectionBalance,
    double TransitionScore,
    bool Qualified,
    string Reason)
{
    public int ObjectCount => EndIndex - StartIndex + 1;
    public int PossibleTransitions => Math.Max(0, ObjectCount - 1);
    public double TransitionRatio => PossibleTransitions == 0
        ? 0
        : (double)TransitionCount / PossibleTransitions;
    public double Duration => EndTime - StartTime;
    public double AverageObjectGap => ObjectGaps.Count == 0 ? 0 : ObjectGaps.Average();
    public double MinimumObjectGap => ObjectGaps.Count == 0 ? 0 : ObjectGaps.Min();
    public double MaximumObjectGapObserved => ObjectGaps.Count == 0 ? 0 : ObjectGaps.Max();
}

/// <summary>
/// Descriptive Circle/Slider Transition result, independent from geometry, speed and the
/// other Tech Pattern analyzers. Aggregate metrics use qualified maximal sequences only.
/// </summary>
public sealed record CircleSliderTransitionPatternAnalysis(
    int TotalObjects,
    int CandidateSequenceCount,
    int TransitionSequenceCount,
    int TransitionObjectCount,
    int CircleToSliderCount,
    int SliderToCircleCount,
    double AverageTransitionRatio,
    double AverageTransitionCount,
    int LongestTransitionSequence,
    double AverageSequenceLength,
    double AverageTransitionScore,
    double Confidence,
    IReadOnlyList<CircleSliderTransitionPatternSequence> Runs)
{
    public IReadOnlyList<CircleSliderTransitionPatternSequence> Sequences =>
        Runs.Where(run => run.Qualified).ToArray();

    public double Coverage => TotalObjects == 0
        ? 0
        : (double)TransitionObjectCount / TotalObjects;
}
