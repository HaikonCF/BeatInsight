namespace BeatInsight.Models.StreamPatterns;

/// <summary>Configurable V1 thresholds for coherent spatial curvature inside rapid runs.</summary>
public sealed record FlowStreamPatternOptions(
    int MinimumFlowStreamObjects = 6,
    double MinimumFlowTurnAngle = 15,
    double MaximumFlowTurnAngle = 110,
    double MinimumFlowLinkDistance = 18,
    double MinimumFlowPivotRatio = 0.70,
    double MinimumTurnDirectionConsistency = 0.75);

/// <summary>One internal pivot of a maximal rapid run, using 0 degrees for straight travel.</summary>
public sealed record FlowStreamPivot(
    int ObjectIndex,
    double TurnAngle,
    int TurnSign,
    double PreviousDistance,
    double NextDistance,
    bool SpatiallyValid,
    bool IsFlowTurn);

/// <summary>One complete maximal rapid run evaluated for coherent spatial curvature.</summary>
public sealed record FlowStreamPatternSequence(
    int StartIndex,
    int EndIndex,
    int StartTime,
    int EndTime,
    IReadOnlyList<double> Intervals,
    IReadOnlyList<double> Distances,
    IReadOnlyList<FlowStreamPivot> Pivots,
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
    public int FlowPivotCount => Pivots.Count(pivot => pivot.IsFlowTurn);
    public double FlowPivotRatio => PivotCount == 0 ? 0 : (double)FlowPivotCount / PivotCount;
    public int DominantTurnDirection
    {
        get
        {
            int positive = Pivots.Count(pivot => pivot.IsFlowTurn && pivot.TurnSign > 0);
            int negative = Pivots.Count(pivot => pivot.IsFlowTurn && pivot.TurnSign < 0);
            return positive == 0 && negative == 0 ? 0 : positive >= negative ? 1 : -1;
        }
    }
    public int DominantTurnCount => Pivots.Count(
        pivot => pivot.IsFlowTurn && pivot.TurnSign == DominantTurnDirection);
    public double TurnDirectionConsistency => FlowPivotCount == 0
        ? 0
        : (double)DominantTurnCount / FlowPivotCount;

    // Angle summaries intentionally use Flow pivots only; straight/reversal noise is represented by FlowPivotRatio.
    private IReadOnlyList<double> FlowTurnAngles => Pivots
        .Where(pivot => pivot.IsFlowTurn)
        .Select(pivot => pivot.TurnAngle)
        .ToArray();
    public double AverageTurnAngle => FlowTurnAngles.Count == 0 ? 0 : FlowTurnAngles.Average();
    public double MinimumTurnAngle => FlowTurnAngles.Count == 0 ? 0 : FlowTurnAngles.Min();
    public double MaximumTurnAngle => FlowTurnAngles.Count == 0 ? 0 : FlowTurnAngles.Max();
    public double CurvatureConsistency => MaximumTurnAngle <= 0 ? 0 : MinimumTurnAngle / MaximumTurnAngle;
    public double FlowScore => Math.Clamp(
        (FlowPivotRatio + TurnDirectionConsistency + CurvatureConsistency) / 3,
        0,
        1);
}

/// <summary>
/// Descriptive spatial-flow result, independent from Jump analysis and gameplay identity.
/// Coverage counts unique objects belonging to qualified maximal rapid runs.
/// </summary>
public sealed record FlowStreamPatternAnalysis(
    int TotalObjects,
    int RapidRunCount,
    int FlowStreamObjectCount,
    int LongestFlowStream,
    double AverageFlowStreamLength,
    double AverageFlowPivotRatio,
    double AverageTurnDirectionConsistency,
    double AverageTurnAngle,
    double AverageSpacingConsistency,
    double AverageFlowScore,
    double Confidence,
    IReadOnlyList<FlowStreamPatternSequence> Runs)
{
    public IReadOnlyList<FlowStreamPatternSequence> Sequences =>
        Runs.Where(run => run.Qualified).ToArray();

    public int FlowStreamSequenceCount => Sequences.Count;
    public double Coverage => TotalObjects == 0 ? 0 : (double)FlowStreamObjectCount / TotalObjects;
}
