namespace BeatInsight.Models.Speed;

/// <summary>
/// Pressure created by the strongest contiguous passages of fast object-start
/// triplets. This metric is complementary to map-wide Global Triplet Speed.
/// </summary>
public sealed record LocalSpeedPressureAnalysis(
    double PressurePercent,
    int QualifyingTripletCount,
    int QualifyingSectionCount,
    IReadOnlyList<LocalSpeedPressureSection> Sections)
{
    public static LocalSpeedPressureAnalysis Empty { get; } = new(
        0,
        0,
        0,
        Array.Empty<LocalSpeedPressureSection>());
}

/// <summary>A chronological contiguous run of qualifying triplet starts.</summary>
public sealed record LocalSpeedPressureSection(
    int StartObjectIndex,
    int EndObjectIndex,
    double StartTime,
    double EndTime,
    int TripletCount,
    double Intensity,
    double Persistence,
    double SectionPressure)
{
    public int ParticipatingObjectCount => EndObjectIndex - StartObjectIndex + 1;
    public double Duration => EndTime - StartTime;
}
