using BeatInsight.Models.Fusion;
using BeatInsight.Models.Sections;
using BeatInsight.Models.SkillPressure;

namespace BeatInsight.Models.Rating;

public enum RatingV2ContributorCategory
{
    BaseDifficulty,
    SectionDifficulty,
    SkillPressure,
    PatternComplexity,
    Endurance,
    Specialization
}

public enum RatingV2DiagnosticFlag
{
    SparseMap,
    SingleExtremePeak,
    SustainedHighDifficulty,
    RepeatedHardPassages,
    HighSpecialization,
    LowSignalConfidence,
    PatternHeavyLowBaseDifficulty,
    HighSkillPressureLowSectionDifficulty,
    HighSectionDifficultyLowGlobalSkills
}

public sealed record RatingV2Contributor(
    string Name,
    RatingV2ContributorCategory Category,
    double Value,
    double Contribution,
    double Confidence);

public sealed record RatingV2PatternEvidence(
    string Name,
    FusionPatternFamily Family,
    double Strength,
    double Confidence);

/// <summary>
/// Standalone, descriptive BeatInsight difficulty rating. It is not persisted and does
/// not replace historical BeatInsightRating or osu! Star Rating.
/// </summary>
public sealed record BeatInsightRatingV2(
    double Score,
    double Confidence,
    double BaseDifficulty,
    double PeakDifficulty,
    double SustainedDifficulty,
    double SkillPressureContribution,
    double PatternContribution,
    double EnduranceContribution,
    double SpecializationContribution,
    double MeanSectionDifficulty,
    double MaxSectionDifficulty,
    double HighDifficultySectionRatio,
    double RepeatedHardPassageSignal,
    int HardPassageCount,
    IReadOnlyList<RatingV2Contributor> TopContributors,
    IReadOnlyList<RatingV2DiagnosticFlag> Flags)
{
    public static BeatInsightRatingV2 Empty { get; } = new(
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        Array.Empty<RatingV2Contributor>(),
        [RatingV2DiagnosticFlag.SparseMap, RatingV2DiagnosticFlag.LowSignalConfidence]);
}

/// <summary>
/// Synthetic/read-only input. Star Rating, historical Rating, Identity labels,
/// Community tags, persistence and ML labels are deliberately absent.
/// </summary>
public sealed record BeatInsightRatingV2Input(
    int ObjectCount,
    double DurationSeconds,
    double AimScore,
    double SpeedScore,
    double ReadingScore,
    double TechScore,
    SkillPressureProfile SkillPressure,
    SectionAnalysisResult Sections,
    IReadOnlyList<RatingV2PatternEvidence> Patterns,
    double IdentityConfidence);
