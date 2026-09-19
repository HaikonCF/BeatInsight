namespace BeatInsight.Models.TechPatterns;

/// <summary>Configurable V1 thresholds for local slider-heavy sequences.</summary>
public sealed record SliderChainPatternOptions(
    int MinimumSliderChainSliders = 3,
    double MinimumSliderRatio = 0.75,
    double MaximumObjectGap = 1000,
    int MaximumConsecutiveNonSliders = 1);

/// <summary>One maximal local sequence anchored by sliders.</summary>
public sealed record SliderChainPatternSequence(
    int StartIndex,
    int EndIndex,
    int StartTime,
    int EndTime,
    IReadOnlyList<double> ObjectGaps,
    int SliderCount,
    int CircleCount,
    bool Qualified,
    string Reason)
{
    public int ObjectCount => EndIndex - StartIndex + 1;
    public double SliderRatio => ObjectCount == 0 ? 0 : (double)SliderCount / ObjectCount;
    public double Duration => EndTime - StartTime;
    public double AverageObjectGap => ObjectGaps.Count == 0 ? 0 : ObjectGaps.Average();
    public double MinimumObjectGap => ObjectGaps.Count == 0 ? 0 : ObjectGaps.Min();
    public double MaximumObjectGapObserved => ObjectGaps.Count == 0 ? 0 : ObjectGaps.Max();
}

/// <summary>
/// Descriptive Slider Chain result, independent from historical Tech scoring and TechV2.
/// Coverage counts unique objects belonging to qualified maximal local sequences.
/// </summary>
public sealed record SliderChainPatternAnalysis(
    int TotalObjects,
    int TotalSliders,
    int CandidateSequenceCount,
    int SliderChainObjectCount,
    int SliderChainSliderCount,
    int LongestSliderChain,
    double AverageChainLength,
    double AverageSliderCount,
    double AverageSliderRatio,
    double AverageDuration,
    double Confidence,
    IReadOnlyList<SliderChainPatternSequence> Runs)
{
    public IReadOnlyList<SliderChainPatternSequence> Sequences =>
        Runs.Where(run => run.Qualified).ToArray();

    public int SliderChainSequenceCount => Sequences.Count;
    public double Coverage => TotalObjects == 0 ? 0 : (double)SliderChainObjectCount / TotalObjects;
    public double SliderCoverage => TotalSliders == 0 ? 0 : (double)SliderChainSliderCount / TotalSliders;
}
