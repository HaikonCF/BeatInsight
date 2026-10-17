using System.Collections.Immutable;

namespace BeatInsight.Integration.Game;

public sealed record LiveGameplayMod(string Acronym);

public sealed record LiveGameplayStatsSnapshot(
    string ProcessIdentity,
    string? BeatmapId,
    string? BeatmapSetId,
    TimeSpan CurrentTime,
    int CurrentCombo,
    int MaxComboReached,
    int Count300,
    int Count100,
    int Count50,
    int CountMiss,
    double Accuracy,
    ImmutableArray<LiveGameplayMod> Mods,
    DateTimeOffset ObservedAtUtc,
    int? Score = null,
    int CountLargeTickMiss = 0,
    int CountSliderTailHit = 0);

public interface ILiveGameplayStatsProvider
{
    LiveGameplayStatsSnapshot? ReadLiveGameplayStats();
}

public enum LiveGameplayStatsFailureReason
{
    None,
    GameUnavailable,
    ProviderUnavailable,
    ProcessOpenFailed,
    ProcessIdentityMissing,
    ProcessIdentityMismatch,
    RouteNotGameplay,
    CurrentTimeUnavailable,
    DiscoveryNotReady,
    PointerChainUnavailable,
    ScoreReadIncomplete,
    AccuracyReadIncomplete,
    ModsReadIncomplete,
    AccuracyNotFinite,
    AccuracyOutOfRange,
    HitCountOutOfRange,
    ComboOutOfRange,
    ScoreNegative,
    ModsNegative,
    ModsUnsupportedForStandard,
    AccuracyMismatch,
    ProviderReturnedNull,
    GameStateChangedDuringRead,
    BeatmapIdMismatch,
    BeatmapSetIdMismatch,
    SnapshotValuesInvalid,
    ProviderException
}

public sealed record LiveGameplayStatsDiscoveryCandidateDiagnostics(
    string RootAddress,
    string Source,
    string SourceRegionAddress,
    string SourceOffset,
    string GameplayBaseAddress,
    string ScoreBaseAddress,
    string AccuracyBaseAddress,
    string ModsBaseAddress,
    bool PointerChainStructurallyValid,
    bool ProbeReadsComplete,
    string RejectionReason,
    bool SemanticReadSucceeded,
    string SemanticFailureReason,
    string SemanticSample,
    string RawSample,
    bool PlausibleCandidate,
    int TemporalObservationCount,
    int CoherentProgressionCount,
    string TemporalEvidence,
    bool TemporallyActive,
    bool ActiveGameplayEvidence);

public sealed record LiveGameplayStatsDiagnostics(
    LiveGameplayStatsFailureReason FailureReason,
    string DiscoveryState,
    bool ReaderResolved,
    int DiscoveryAttempts,
    int SuccessfulDiscoveryCount,
    string LastDiscoveryFailure,
    string DiscoveryOutcome,
    int RawCandidateCount,
    int StructuralCandidateCount,
    int ValidatedCandidateCount,
    int PlausibleCandidateCount,
    int ActiveCandidateCount,
    IReadOnlyList<LiveGameplayStatsDiscoveryCandidateDiagnostics> DiscoveryCandidates,
    TimeSpan? NextDiscoveryRetry,
    TimeSpan? ProcessAttachToDiscoveryReady,
    TimeSpan? LastDiscoveryDuration,
    string ContainerAddress,
    string ActiveRulesetAddress,
    string GameplayBaseAddress,
    string ScoreBaseAddress)
{
    public static LiveGameplayStatsDiagnostics Unavailable(
        LiveGameplayStatsFailureReason reason) => new(
            reason,
            "Unavailable",
            false,
            0,
            0,
            "None",
            "NotAttempted",
            0,
            0,
            0,
            0,
            0,
            [],
            null,
            null,
            null,
            "<null>",
            "<null>",
            "<null>",
            "<null>");
}

public sealed record LiveGameplayStatsReadResult(
    LiveGameplayStatsSnapshot? Snapshot,
    LiveGameplayStatsDiagnostics Diagnostics);

public interface ILiveGameplayStatsDiagnosticsProvider
{
    LiveGameplayStatsReadResult ReadLiveGameplayStatsWithDiagnostics();
}
