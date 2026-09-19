namespace BeatInsight.Models.TechPatterns;

/// <summary>Configurable V1 thresholds for local overlap transitions.</summary>
public sealed record OverlapPatternOptions(
    double MaximumOverlapDistance = 40,
    double MaximumOverlapGap = 1000,
    int MinimumOverlapSequenceLinks = 2);

public enum OverlapTransitionType
{
    CircleCircle,
    CircleSlider,
    SliderCircle,
    SliderSlider
}

/// <summary>One diagnostic transition between consecutive hit objects.</summary>
public sealed record OverlapPatternLink(
    int StartIndex,
    int EndIndex,
    int StartTime,
    int EndTime,
    double Gap,
    double? Distance,
    double? OverlapStrength,
    OverlapTransitionType? TransitionType,
    bool Qualified,
    string Reason);

/// <summary>One maximal sequence of adjacent qualified overlap links.</summary>
public sealed record OverlapPatternSequence(
    int StartIndex,
    int EndIndex,
    int StartTime,
    int EndTime,
    double MinimumDistance,
    double MaximumDistance,
    double AverageDistance,
    double AverageOverlapStrength,
    double MaximumOverlapStrength,
    int CircleCircleCount,
    int CircleSliderCount,
    int SliderCircleCount,
    int SliderSliderCount,
    double OverlapSequenceScore,
    bool Qualified,
    string Reason,
    IReadOnlyList<OverlapPatternLink> Links)
{
    public int ObjectCount => EndIndex - StartIndex + 1;
    public int LinkCount => Links.Count;
    public double Duration => EndTime - StartTime;
}

/// <summary>
/// Descriptive overlap result. Link metrics include every geometrically qualified link;
/// coverage and sequence metrics include qualified maximal sequences only.
/// </summary>
public sealed record OverlapPatternAnalysis(
    int TotalObjects,
    int OverlapLinkCount,
    int QualifiedOverlapSequenceCount,
    int QualifiedOverlapObjectCount,
    double AverageDistance,
    double MinimumDistance,
    double AverageOverlapStrength,
    double MaximumOverlapStrength,
    double AverageSequenceLength,
    double Confidence,
    IReadOnlyList<OverlapPatternLink> Links,
    IReadOnlyList<OverlapPatternSequence> Runs)
{
    public IReadOnlyList<OverlapPatternSequence> Sequences =>
        Runs.Where(run => run.Qualified).ToArray();

    public double Coverage => TotalObjects == 0
        ? 0
        : (double)QualifiedOverlapObjectCount / TotalObjects;
}
