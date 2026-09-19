namespace BeatInsight.Models.SkillPressure;

public enum SkillPressureDecision
{
    Freeze,
    Investigate,
    Partial,
    Unavailable,
    StructuralIssue
}

public sealed record SkillPressureMetric(
    double? Score,
    double Confidence,
    bool Available,
    bool Reliable,
    IReadOnlyDictionary<string, double> Signals,
    string Limitation)
{
    public static SkillPressureMetric Unavailable(string limitation) =>
        new(null, 0, false, false, new Dictionary<string, double>(), limitation);
}

/// <summary>
/// Read-only Skill Pressure V2 snapshot. It is not structural Identity, is not persisted,
/// and is not consumed by Rating, UI, ML, cache, or Fusion V1.
/// </summary>
public sealed record SkillPressureProfile(
    SkillPressureMetric Stamina,
    SkillPressureMetric Endurance,
    SkillPressureMetric FingerControl,
    SkillPressureMetric BurstControl,
    SkillPressureMetric AimControl,
    SkillPressureMetric Precision,
    SkillPressureMetric ReadingPressure,
    SkillPressureMetric RhythmComplexity,
    double Confidence,
    int AvailableSkillCount,
    IReadOnlyList<string> Diagnostics);

/// <summary>
/// Normalized synthetic input. Every signal is [0,1]; DurationSeconds and ObjectCount are
/// raw quantities normalized exactly once by the analyzer.
/// </summary>
public sealed record SkillPressureInput
{
    public int ObjectCount { get; init; }
    public double DurationSeconds { get; init; }

    public double BaseAim { get; init; }
    public double BaseSpeed { get; init; }
    public double BaseReading { get; init; }

    public double StaminaCoverage { get; init; }
    public double StaminaRepetition { get; init; }
    public double StaminaActiveRatio { get; init; }
    public double StaminaRecoveryPressure { get; init; }
    public double RapidObjectVolume { get; init; }
    public double LongRapidSupport { get; init; }

    public double GlobalPressureCoverage { get; init; }
    public double PressureSectionSpread { get; init; }
    public double SustainedPressure { get; init; }

    public double IrregularStrength { get; init; }
    public double AlternatingStrength { get; init; }
    public double RhythmTransitionStrength { get; init; }
    public double TemporalBreadth { get; init; }

    public double BurstCoverage { get; init; }
    public double BurstRepetition { get; init; }
    public double BurstIntensity { get; init; }
    public double BurstLength { get; init; }
    public double BurstVariation { get; init; }

    public double AimAngleControl { get; init; }
    public double JumpControlPatternStrength { get; init; }
    public double SpatialAwkwardStrength { get; init; }
    public double SliderDirectionStrength { get; init; }

    public double ReadingDensity { get; init; }
    public double ReadingClutter { get; init; }
    public double ReadingAmbiguity { get; init; }
    public double ReadingNovelty { get; init; }
    public double ReadingPatternStrength { get; init; }

    public bool PrecisionGeometryAvailable { get; init; }
    public double PrecisionGeometryStrength { get; init; }
    public double PrecisionDemandCoverage { get; init; }
}
