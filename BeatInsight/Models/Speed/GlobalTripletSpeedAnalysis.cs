namespace BeatInsight.Models.Speed;

/// <summary>
/// Descriptive map-wide timing speed, separate from historical gameplay Speed.
/// </summary>
public sealed record GlobalTripletSpeedAnalysis(
    double SpeedPercent,
    double AverageTripletIntervalMs,
    int HitObjectCount,
    int TotalTripletCount,
    int ValidTripletCount,
    int ExcludedTripletCount,
    double MinTripletIntervalMs,
    double MaxTripletIntervalMs,
    double MedianTripletIntervalMs,
    double Confidence,
    double EquivalentAverageBpm,
    double AverageConsistency,
    IReadOnlyList<GlobalTripletSpeedDiagnostic> Diagnostics)
{
    public static GlobalTripletSpeedAnalysis Empty { get; } = new(
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        Array.Empty<GlobalTripletSpeedDiagnostic>());
}

public sealed record GlobalTripletSpeedDiagnostic(
    int StartIndex,
    int MiddleIndex,
    int EndIndex,
    double Delta1Ms,
    double Delta2Ms,
    double TripletIntervalMs,
    double LocalSpeedPercent,
    double Consistency,
    bool Valid,
    string ExclusionReason);
