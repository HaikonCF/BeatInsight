namespace BeatInsight.Models.TechPatterns;

/// <summary>Configurable V1 thresholds for intrinsic slider complexity.</summary>
public sealed record ComplexSliderPatternOptions(
    int MinimumComplexControlPoints = 3,
    double MinimumComplexPathToChordRatio = 1.20,
    double MaximumPathToChordRatio = 10.0,
    double MinimumInternalTurnAngle = 35,
    double MinimumSliderComplexityScore = 0.50);

public sealed record SliderPatternPosition(double X, double Y);

/// <summary>
/// Descriptive facts for one slider. Path geometry is based on the parsed control polygon:
/// BeatInsight does not currently retain a flattened playable curve.
/// </summary>
public sealed record ComplexSliderPatternOccurrence(
    int ObjectIndex,
    int StartTime,
    double? EndTime,
    double? Duration,
    SliderPatternPosition StartPosition,
    SliderPatternPosition EndPosition,
    SliderPatternPosition BasePathEndPosition,
    string CurveType,
    int ControlPointCount,
    int EffectiveControlPointCount,
    int RepeatCount,
    double? PathLength,
    double ChordLength,
    double? PathToChordRatio,
    int InternalTurnCount,
    double AverageInternalTurnAngle,
    double MaximumInternalTurnAngle,
    double ControlPointSignal,
    double RepeatSignal,
    double? PathSignal,
    double? DirectionSignal,
    double SliderComplexityScore,
    bool Qualified,
    string Reason);

/// <summary>
/// Descriptive Complex Slider result, independent from Slider Chain, historical Tech scoring
/// and TechV2. Aggregate averages use qualified sliders only.
/// </summary>
public sealed record ComplexSliderPatternAnalysis(
    int TotalObjects,
    int TotalSliders,
    int ComplexSliderCount,
    double AverageComplexityScore,
    double MaximumComplexityScore,
    double AverageControlPointCount,
    double AverageRepeatCount,
    double AveragePathToChordRatio,
    double Confidence,
    IReadOnlyList<ComplexSliderPatternOccurrence> Sliders)
{
    public IReadOnlyList<ComplexSliderPatternOccurrence> ComplexSliders =>
        Sliders.Where(slider => slider.Qualified).ToArray();

    public int ComplexSliderObjectCount => ComplexSliderCount;
    public double Coverage => TotalObjects == 0 ? 0 : (double)ComplexSliderObjectCount / TotalObjects;
    public double SliderCoverage => TotalSliders == 0 ? 0 : (double)ComplexSliderCount / TotalSliders;
}
