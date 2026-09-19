namespace BeatInsight.Models.StreamPatterns;

/// <summary>Configurable V1 thresholds for regularly alternating spatial turns in rapid runs.</summary>
public sealed record AlternatingStreamPatternOptions(
    int MinimumAlternatingStreamObjects = 6,
    double MinimumAlternatingTurnAngle = 20,
    double MaximumAlternatingTurnAngle = 160,
    double MinimumAlternatingLinkDistance = 20,
    double MinimumValidAlternatingPivotRatio = 0.70,
    double MinimumAlternationRatio = 0.75);

/// <summary>One internal pivot using the same geometry and sign convention as Flow Stream.</summary>
public sealed record AlternatingStreamPivot(
    int ObjectIndex,
    double TurnAngle,
    int TurnSign,
    double PreviousDistance,
    double NextDistance,
    bool SpatiallyValid,
    bool IsValidAlternatingPivot);

/// <summary>One complete maximal rapid run evaluated for regularly alternating turn signs.</summary>
public sealed record AlternatingStreamPatternSequence(
    int StartIndex,
    int EndIndex,
    int StartTime,
    int EndTime,
    IReadOnlyList<double> Intervals,
    IReadOnlyList<double> Distances,
    IReadOnlyList<AlternatingStreamPivot> Pivots,
    bool Qualified,
    string Reason)
{
    public int ObjectCount => EndIndex - StartIndex + 1;
    public int LinkCount => Intervals.Count;
    public double Duration => EndTime - StartTime;
    public double AverageInterval => LinkCount == 0 ? 0 : Intervals.Average();
    public double AverageDistance => Distances.Count == 0 ? 0 : Distances.Average();
    public double MinimumDistance => Distances.Count == 0 ? 0 : Distances.Min();
    public double MaximumDistance => Distances.Count == 0 ? 0 : Distances.Max();
    public double SpacingConsistency => MaximumDistance <= 0 ? 0 : MinimumDistance / MaximumDistance;
    public IReadOnlyList<double> TurnAngles => Pivots.Select(pivot => pivot.TurnAngle).ToArray();
    public IReadOnlyList<int> TurnSigns => Pivots.Select(pivot => pivot.TurnSign).ToArray();
    public int PivotCount => Pivots.Count;
    public int ValidAlternatingPivotCount => Pivots.Count(pivot => pivot.IsValidAlternatingPivot);
    public double ValidAlternatingPivotRatio => PivotCount == 0
        ? 0
        : (double)ValidAlternatingPivotCount / PivotCount;

    private IReadOnlyList<AlternatingStreamPivot> ValidPivots => Pivots
        .Where(pivot => pivot.IsValidAlternatingPivot)
        .ToArray();
    public int PossibleAlternations => Math.Max(0, ValidPivots.Count - 1);
    public int AlternationCount => Enumerable.Range(0, PossibleAlternations)
        .Count(index => ValidPivots[index].TurnSign != ValidPivots[index + 1].TurnSign);
    public double AlternationRatio => PossibleAlternations == 0
        ? 0
        : (double)AlternationCount / PossibleAlternations;
    public double AverageValidTurnAngle => ValidPivots.Count == 0
        ? 0
        : ValidPivots.Average(pivot => pivot.TurnAngle);
    public double MinimumValidTurnAngle => ValidPivots.Count == 0
        ? 0
        : ValidPivots.Min(pivot => pivot.TurnAngle);
    public double MaximumValidTurnAngle => ValidPivots.Count == 0
        ? 0
        : ValidPivots.Max(pivot => pivot.TurnAngle);
    public double AlternatingAngleConsistency => MaximumValidTurnAngle <= 0
        ? 0
        : MinimumValidTurnAngle / MaximumValidTurnAngle;
    public double AlternatingScore => Math.Clamp(
        (ValidAlternatingPivotRatio + AlternationRatio + AlternatingAngleConsistency) / 3,
        0,
        1);
}

/// <summary>
/// Descriptive spatial-alternation result, independent from Flow, Jump analysis and gameplay identity.
/// Coverage counts unique objects belonging to qualified maximal rapid runs.
/// </summary>
public sealed record AlternatingStreamPatternAnalysis(
    int TotalObjects,
    int RapidRunCount,
    int AlternatingStreamObjectCount,
    int LongestAlternatingStream,
    double AverageAlternatingStreamLength,
    double AverageValidPivotRatio,
    double AverageAlternationRatio,
    double AverageTurnAngle,
    double AverageSpacingConsistency,
    double AverageAlternatingScore,
    double Confidence,
    IReadOnlyList<AlternatingStreamPatternSequence> Runs)
{
    public IReadOnlyList<AlternatingStreamPatternSequence> Sequences =>
        Runs.Where(run => run.Qualified).ToArray();

    public int AlternatingStreamSequenceCount => Sequences.Count;
    public double Coverage => TotalObjects == 0 ? 0 : (double)AlternatingStreamObjectCount / TotalObjects;
}
