using System.Collections.Immutable;

namespace BeatInsight.Integration.Game;

/// <summary>
/// Provider-neutral input for osu!standard performance calculation.
/// PassedObjects is derived from the fresh top-level judgement counts by the calculator.
/// CurrentTime remains available for timeline diagnostics and other live overlays.
/// </summary>
public sealed record LivePerformanceInput(
    string BeatmapPath,
    ImmutableArray<LiveGameplayMod> Mods,
    int CurrentCombo,
    int MaxComboReached,
    int Count300,
    int Count100,
    int Count50,
    int CountMiss,
    double Accuracy,
    TimeSpan CurrentTime,
    int? LegacyScore = null,
    LivePerformanceRulesetProfile Profile = LivePerformanceRulesetProfile.StableClassic,
    int CountLargeTickMiss = 0,
    int CountSliderTailHit = 0);

public enum LivePerformanceRulesetProfile
{
    StableClassic,
    Lazer
}

public sealed record LivePerformanceSnapshot(
    double CurrentPp,
    double FcPp,
    double Accuracy95FcPp,
    double SsPp,
    int PassedObjects,
    DateTimeOffset ObservedAtUtc);

public enum LivePerformanceFailureReason
{
    None,
    InputUnavailable,
    BeatmapUnavailable,
    UnsupportedMods,
    InvalidStats,
    HitCountMismatch,
    NoPassedObjects,
    CalculationFailed
}

public sealed record LivePerformanceCalculationResult(
    LivePerformanceSnapshot? Snapshot,
    LivePerformanceFailureReason FailureReason);

public sealed record LivePerformanceSynchronizationDiagnostics(
    double CurrentTimeMs,
    int PassedObjectsFromTime,
    int JudgedObjects,
    int Delta,
    double? LastPassedObjectTimeMs,
    double? NextObjectTimeMs,
    string? BoundaryObjectType,
    double? BoundaryObjectStartTimeMs,
    double? BoundaryObjectEndTimeMs,
    double? TimeSinceBoundaryStartMs,
    double? TimeUntilBoundaryEndMs);

public sealed record LivePerformanceCacheDiagnostics(
    int BeatmapParseCount,
    int DifficultyCalculationCount,
    int CacheHitCount,
    int NonFiniteCurrentNormalizationCount,
    TimeSpan? LastPreparationDuration,
    TimeSpan? LastCurrentCalculationDuration,
    TimeSpan? LastFcCalculationDuration,
    TimeSpan? LastStaticCalculationDuration);
