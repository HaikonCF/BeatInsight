namespace BeatInsight.Models.Fusion;

/// <summary>Structural families remain separate from transversal skill pressure.</summary>
public enum GameplayStructureKind
{
    Jump,
    Stream,
    Tech,
    ClassicMixed
}

public enum FusionPatternFamily
{
    Jump,
    Stream,
    Tech
}

public enum GameplayFusionDiagnosticFlag
{
    HighTechPatternsLowTechSkill,
    HighStreamStructureLowSpeed,
    HighJumpStructureLowAim,
    HighReadingWithoutReadingPatterns,
    MixedStructure
}

/// <summary>
/// Normalized, immutable input from one existing pattern analyzer. Coverage and confidence
/// are in [0,1]. Intensity is null when that analyzer has no independent normalized
/// intensity metric. Mapping weights are zero unless the pattern has a justified secondary
/// relationship with Reading or Stamina.
/// </summary>
public sealed record GameplayFusionPatternEvidence(
    string Name,
    FusionPatternFamily Family,
    double Coverage,
    double Confidence,
    double? Intensity = null,
    double ReadingWeight = 0,
    double StaminaWeight = 0);

/// <summary>
/// Input contract for deterministic synthetic testing and future callers. Base skills are
/// already normalized to [0,100]; structure signals and pattern evidence use [0,1].
/// </summary>
public sealed record GameplayFusionInput(
    double AimScore,
    double SpeedScore,
    double ReadingScore,
    double TechScore,
    double? StaminaScore,
    double JumpStructureSignal,
    double StreamStructureSignal,
    double TechStructureSignal,
    IReadOnlyList<GameplayFusionPatternEvidence> PatternEvidence);

/// <summary>
/// Diagnostic Fusion V1 snapshot. It is not GameplayIdentity, does not replace the existing
/// rating, and is not persisted or consumed by the UI.
/// </summary>
public sealed record GameplayFusionProfile(
    double JumpStructureScore,
    double StreamStructureScore,
    double TechStructureScore,
    double MixedStructureScore,
    double AimScore,
    double SpeedScore,
    double ReadingScore,
    double TechScore,
    double? StaminaScore,
    double JumpPatternSupport,
    double StreamPatternSupport,
    double TechPatternSupport,
    double ReadingPatternSupport,
    double StaminaPatternSupport,
    double FinalAim,
    double FinalSpeed,
    double FinalReading,
    double FinalTech,
    double? FinalStamina,
    GameplayStructureKind DominantStructure,
    GameplayStructureKind? SecondaryStructure,
    double Confidence,
    IReadOnlyList<GameplayFusionDiagnosticFlag> Flags);
