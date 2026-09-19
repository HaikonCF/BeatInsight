using BeatInsight.Models.Identity;
using BeatInsight.Models.Rating;
using BeatInsight.Models.Sections;
using BeatInsight.Models.SkillPressure;
using BeatInsight.Models.Speed;

namespace BeatInsight.Models.AnalysisCache;

public enum AnalysisCacheMissReason
{
    None,
    NotFound,
    HashMismatch,
    AnalyzerVersionMismatch,
    SchemaVersionMismatch,
    Corrupted,
    MissingRequiredData,
    Disabled
}

public enum AnalysisCacheResultKind
{
    Hit,
    Miss,
    Invalid
}

public sealed record BeatmapAnalysisSourceSnapshot(
    string? OriginalPath,
    string FileName,
    long FileSize,
    DateTime LastWriteTimeUtc,
    string ContentHash);

public sealed record BeatmapMetadataSnapshot(
    int? BeatmapId,
    int? BeatmapSetId,
    string Artist,
    string Title,
    string Version,
    string Creator,
    long LengthTicks,
    int Bpm,
    int MaxCombo,
    double ApproachRate,
    double OverallDifficulty,
    double CircleSize,
    double HpDrain,
    int CircleCount,
    int SliderCount,
    int SpinnerCount,
    double OsuStarRating,
    double HistoricalBeatInsightRating);

public sealed record GameplayProfileSnapshot(
    double AimScore,
    double SpeedScore,
    double ReadingScore,
    double TechScore,
    double StreamRatio,
    double JumpRatio,
    double BurstRatio,
    double TechPresence,
    double TechV2Presence,
    double ReadingCoverage,
    double SpeedCoverage,
    string HistoricalIdentityPrimary,
    string HistoricalIdentitySecondary,
    string HistoricalIdentityPattern,
    double HistoricalIdentityConfidence,
    IReadOnlyList<string> HistoricalTraits);

public sealed record BeatmapAnalysisSnapshot(
    BeatmapMetadataSnapshot Metadata,
    GameplayProfileSnapshot GameplayProfile,
    GameplayIdentityV3Profile IdentityV3,
    SkillPressureProfile SkillPressureV2,
    SectionAnalysisResult SectionAnalysisV2,
    BeatInsightRatingV2 RatingV2,
    GlobalTripletSpeedAnalysis GlobalTripletSpeed,
    IReadOnlyList<GameplayIdentityV3Pattern> PatternSummaries);

public sealed record BeatmapAnalysisCacheEntry(
    int SchemaVersion,
    string AnalyzerVersion,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    BeatmapAnalysisSourceSnapshot Source,
    BeatmapAnalysisSnapshot Analysis);

public sealed record AnalysisCacheLookupResult(
    AnalysisCacheResultKind Result,
    AnalysisCacheMissReason Reason,
    string CacheKey,
    string ContentHash,
    string CachePath,
    BeatmapAnalysisCacheEntry? Entry,
    TimeSpan LoadTime,
    long EntrySizeBytes);

public sealed record AnalysisCacheStats(
    int EntryCount,
    long TotalBytes,
    DateTime? OldestEntryUtc,
    DateTime? NewestEntryUtc);

public sealed record CachedBeatmapAnalysisResult(
    BeatmapAnalysisSnapshot Snapshot,
    bool WasCacheHit,
    AnalysisCacheMissReason InitialMissReason,
    string CacheKey,
    string ContentHash,
    string CachePath,
    TimeSpan LoadTime,
    TimeSpan AnalysisTime,
    TimeSpan SaveTime,
    long EntrySizeBytes);
