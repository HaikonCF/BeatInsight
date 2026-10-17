using BeatInsight.Models.Fusion;
using BeatInsight.Models.Sections;
using BeatInsight.Models.SkillPressure;

namespace BeatInsight.Models.Identity;

public enum GameplayIdentityV3EvidenceStatus
{
    Reliable,
    Partial
}

public enum GameplayIdentityV3DiagnosticFlag
{
    WeakPrimaryStructure,
    StrongSecondaryStructure,
    HighlyMixedStructure,
    HighSectionVariability,
    SkillStructureMismatch,
    PatternStructureMismatch,
    LowConfidence,
    SparseMap
}

public sealed record GameplayIdentityV3Skill(
    string Name,
    double Score,
    double Confidence,
    GameplayIdentityV3EvidenceStatus Status);

public sealed record GameplayIdentityV3Pattern(
    string Name,
    FusionPatternFamily Family,
    double Score,
    double Coverage,
    double Confidence,
    double? Intensity);

public sealed record GameplayIdentityV3SectionContext(
    double JumpSectionRatio,
    double StreamSectionRatio,
    double TechSectionRatio,
    double MixedSectionRatio,
    int RepresentedStructuralFamilyCount,
    double RealStructuralDiversity,
    int GameplayTransitionCount,
    int DifficultySpikeCount,
    IReadOnlyList<GameplayStructureKind> CompressedStructureTimeline);

/// <summary>
/// Analytical Identity V3. It remains parallel to the historical GameplayIdentity;
/// MainPatterns keeps its identity/rating semantics while AllPatterns is presentation data.
/// </summary>
public sealed record GameplayIdentityV3Profile(
    double JumpStructureScore,
    double StreamStructureScore,
    double TechStructureScore,
    double ClassicMixedScore,
    GameplayStructureKind PrimaryStructure,
    GameplayStructureKind? SecondaryStructure,
    IReadOnlyList<GameplayIdentityV3Skill> MainSkills,
    IReadOnlyList<GameplayIdentityV3Pattern> MainPatterns,
    GameplayIdentityV3SectionContext SectionContext,
    double StructureConfidence,
    double SkillConfidence,
    double PatternConfidence,
    double GlobalIdentityConfidence,
    IReadOnlyList<GameplayIdentityV3DiagnosticFlag> Flags)
{
    /// <summary>
    /// Complete positive pattern summaries produced for this analysis. This presentation
    /// dataset is intentionally separate from the filtered MainPatterns identity signal.
    /// </summary>
    public IReadOnlyList<GameplayIdentityV3Pattern> AllPatterns { get; init; }
        = Array.Empty<GameplayIdentityV3Pattern>();
}

public sealed record GameplayIdentityV3PatternInput(
    string Name,
    FusionPatternFamily Family,
    double Coverage,
    double Confidence,
    double? Intensity = null);

/// <summary>Synthetic/read-only adapter contract used by V3 and its tests.</summary>
public sealed record GameplayIdentityV3Input(
    int ObjectCount,
    double GlobalJumpStructure,
    double GlobalStreamStructure,
    double GlobalTechStructure,
    SkillPressureProfile SkillPressure,
    IReadOnlyList<GameplayIdentityV3PatternInput> Patterns,
    SectionAnalysisResult Sections);
