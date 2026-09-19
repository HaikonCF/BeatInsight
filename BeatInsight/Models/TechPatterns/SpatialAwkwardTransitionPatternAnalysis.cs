namespace BeatInsight.Models.TechPatterns;

/// <summary>Configurable V1 thresholds for local awkward spatial transitions.</summary>
public sealed record SpatialAwkwardTransitionPatternOptions(
    int SpatialWindowSize = 1,
    double MinimumRelevantDistance = 60,
    double MinimumAwkwardTurnAngle = 90,
    double MinimumSpatialAwkwardScore = 0.50,
    double MaximumSpatialTransitionGap = 1000);

/// <summary>One local pivot evaluating spatial awkwardness.</summary>
public sealed record SpatialAwkwardTransitionPatternOccurrence(
    int PivotIndex,
    int StartIndex,
    int EndIndex,
    int StartTime,
    int EndTime,
    double IncomingDistance,
    double OutgoingDistance,
    double AverageDistance,
    double TurnAngle,
    double DistanceRatio,
    double Asymmetry,
    double? DistanceSignal,
    double? TurnSignal,
    double? AsymmetrySignal,
    double SpatialAwkwardScore,
    bool Qualified,
    string Reason);

/// <summary>
/// Descriptive local spatial awkward transitions. Uses three-object windows
/// (A->B->C) around pivots to evaluate distance, turn angle, and asymmetry.
/// </summary>
public sealed record SpatialAwkwardTransitionPatternAnalysis(
    int TotalObjects,
    int SpatialAwkwardTransitionCount,
    int SpatialAwkwardObjectCount,
    double AverageIncomingDistance,
    double AverageOutgoingDistance,
    double AverageTurnAngle,
    double AverageAsymmetry,
    double AverageSpatialAwkwardScore,
    double MaximumSpatialAwkwardScore,
    double Confidence,
    IReadOnlyList<SpatialAwkwardTransitionPatternOccurrence> Occurrences)
{
    public IReadOnlyList<SpatialAwkwardTransitionPatternOccurrence> Transitions =>
        Occurrences.Where(occurrence => occurrence.Qualified).ToArray();

    public double Coverage => TotalObjects == 0
        ? 0
        : (double)SpatialAwkwardObjectCount / TotalObjects;
}
