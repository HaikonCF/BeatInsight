using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using BeatInsight.Diagnostics.NativeStable;

namespace BeatInsight.Integration.Game;

internal sealed record NativeStableGameplayStats(
    int CurrentCombo,
    int MaxComboReached,
    int Count300,
    int Count100,
    int Count50,
    int CountMiss,
    double Accuracy,
    ImmutableArray<LiveGameplayMod> Mods,
    int Score);

internal interface INativeStableGameplayStatsReader
{
    void ObserveProcessIdentity(string processIdentity);

    NativeStableGameplayStats? Read(
        NativeStableGameProcessContext context,
        string processIdentity);

    NativeStableGameplayStats? ReadCached(
        NativeStableGameProcessContext context,
        string processIdentity,
        out NativeStableGameplayStatsFailureReason failureReason);

    NativeStableGameplayStatsReaderDiagnostics Diagnostics { get; }

    void Invalidate();
}

internal enum NativeStableGameplayStatsDiscoveryState
{
    NotAttempted,
    ObservingCandidates,
    PendingRetry,
    Resolved
}

internal enum NativeStableGameplayStatsDiscoveryFailure
{
    None,
    SignatureAbsent,
    MultipleSignatureMatches,
    ContainerOperandUnreadable,
    ContainerInvalid,
    ZeroValidRoots,
    ZeroActiveRoots,
    MultipleValidRoots,
    MultipleActiveRoots
}

internal enum NativeStableGameplayStatsDiscoveryOutcome
{
    NotAttempted,
    SignatureAbsent,
    MultipleSignatureMatches,
    ContainerOperandUnreadable,
    ContainerInvalid,
    ContainerResolved,
    ObservingCandidates,
    ZeroValidRoots,
    ZeroActiveRoots,
    OneCandidateResolved,
    MultipleValidRoots,
    MultipleActiveRoots
}

internal enum NativeStableGameplayStatsCandidateRejectionReason
{
    None,
    GameplayBasePointerUnreadable,
    GameplayBaseNull,
    ScoreBasePointerUnreadable,
    ScoreBaseNull,
    AccuracyBasePointerUnreadable,
    AccuracyBaseNull,
    ModsBasePointerUnreadable,
    ModsBaseNull,
    ScoreProbeIncomplete,
    AccuracyProbeIncomplete,
    ModsProbeIncomplete
}

internal sealed record NativeStableGameplayStatsCandidateTrace(
    nuint RootAddress,
    nuint SourceRegionBase,
    nuint SourceOffset,
    nuint GameplayBase,
    nuint ScoreBase,
    nuint AccuracyBase,
    nuint ModsBase,
    bool PointerChainStructurallyValid,
    bool ProbeReadsComplete,
    NativeStableGameplayStatsCandidateRejectionReason RejectionReason,
    bool SemanticReadSucceeded,
    NativeStableGameplayStatsFailureReason SemanticFailureReason,
    NativeStableGameplayStats? SemanticSample,
    string RawSample,
    bool PlausibleCandidate,
    int TemporalObservationCount,
    int CoherentProgressionCount,
    string TemporalEvidence,
    bool TemporallyActive,
    bool ActiveGameplayEvidence);

internal sealed record NativeStableGameplayStatsDiscoveryReport(
    NativeStableGameplayStatsDiscoveryOutcome Outcome,
    int RawCandidateCount,
    int StructuralCandidateCount,
    int ValidatedCandidateCount,
    int PlausibleCandidateCount,
    int ActiveCandidateCount,
    ImmutableArray<NativeStableGameplayStatsCandidateTrace> Candidates,
    nuint ResolvedRoot)
{
    internal static NativeStableGameplayStatsDiscoveryReport NotAttempted { get; } = new(
        NativeStableGameplayStatsDiscoveryOutcome.NotAttempted,
        0,
        0,
        0,
        0,
        0,
        [],
        0);
}

internal enum NativeStableGameplayStatsFailureReason
{
    None,
    ProcessIdentityMismatch,
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
    AccuracyMismatch
}

internal sealed record NativeStableGameplayStatsReaderDiagnostics(
    string? ProcessIdentity,
    NativeStableGameplayStatsDiscoveryState DiscoveryState,
    bool ReaderResolved,
    NativeStableGameplayStatsFailureReason FailureReason,
    int DiscoveryAttempts,
    int SuccessfulDiscoveryCount,
    NativeStableGameplayStatsDiscoveryFailure LastDiscoveryFailure,
    NativeStableGameplayStatsDiscoveryReport LastDiscoveryReport,
    TimeSpan? NextDiscoveryRetry,
    TimeSpan? ProcessAttachToDiscoveryReady,
    TimeSpan? LastDiscoveryDuration,
    nuint ContainerAddress = 0,
    nuint ActiveRulesetAddress = 0,
    nuint GameplayBaseAddress = 0,
    nuint ScoreBaseAddress = 0);

internal sealed class NativeStableGameplayStatsReader : INativeStableGameplayStatsReader
{
    private const string ActiveRulesetContainerPattern = "7D 15 A1 ?? ?? ?? ?? 85 C0";
    private const int ChunkBytes = 1024 * 1024;
    private const int RootProbeBytes = 0x68;
    private const int ScoreProbeBytes = 0x98;
    private const uint MemoryPrivate = 0x20000;
    private const int StandardGameplayModsMask =
        (1 << 0) |  // NoFail
        (1 << 1) |  // Easy
        (1 << 2) |  // TouchDevice
        (1 << 3) |  // Hidden
        (1 << 4) |  // HardRock
        (1 << 5) |  // SuddenDeath
        (1 << 6) |  // DoubleTime
        (1 << 8) |  // HalfTime
        (1 << 9) |  // Nightcore
        (1 << 10) | // Flashlight
        (1 << 12) | // SpunOut
        (1 << 14) | // Perfect
        (1 << 29);  // ScoreV2
    private static readonly TimeSpan ObservationInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ObservationWindow = TimeSpan.FromSeconds(1);
    private static readonly BytePattern ContainerPattern =
        BytePattern.Parse(ActiveRulesetContainerPattern);
    private readonly object gate = new();
    private readonly Func<long> getTimestamp;
    private readonly long timestampFrequency;
    private string? cachedProcessIdentity;
    private nuint cachedContainer;
    private nuint cachedRuleset;
    private nuint lastGameplayBase;
    private nuint lastScoreBase;
    private long processObservedTimestamp;
    private long nextDiscoveryRetryTimestamp;
    private long nextObservationTimestamp;
    private long observationDeadlineTimestamp;
    private int discoveryAttempts;
    private int successfulDiscoveryCount;
    private TimeSpan? processAttachToDiscoveryReady;
    private TimeSpan? lastDiscoveryDuration;
    private NativeStableGameplayStatsDiscoveryState discoveryState;
    private NativeStableGameplayStatsDiscoveryFailure lastDiscoveryFailure;
    private NativeStableGameplayStatsDiscoveryReport lastDiscoveryReport =
        NativeStableGameplayStatsDiscoveryReport.NotAttempted;
    private NativeStableGameplayStatsFailureReason lastFailureReason;

    internal NativeStableGameplayStatsReader(
        Func<long>? getTimestamp = null,
        long? timestampFrequency = null)
    {
        this.getTimestamp = getTimestamp ?? Stopwatch.GetTimestamp;
        this.timestampFrequency = timestampFrequency ?? Stopwatch.Frequency;
        if (this.timestampFrequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
    }

    public NativeStableGameplayStatsReaderDiagnostics Diagnostics
    {
        get
        {
            lock (gate)
            {
                return new(
                    cachedProcessIdentity,
                    discoveryState,
                    cachedContainer != 0,
                    lastFailureReason,
                    discoveryAttempts,
                    successfulDiscoveryCount,
                    lastDiscoveryFailure,
                    lastDiscoveryReport,
                    RemainingRetryDelay(getTimestamp()),
                    processAttachToDiscoveryReady,
                    lastDiscoveryDuration,
                    cachedContainer,
                    cachedRuleset,
                    lastGameplayBase,
                    lastScoreBase);
            }
        }
    }

    public void ObserveProcessIdentity(string processIdentity)
    {
        lock (gate)
        {
            EnsureProcessIdentity(processIdentity);
        }
    }

    public NativeStableGameplayStats? Read(
        NativeStableGameProcessContext context,
        string processIdentity)
    {
        lock (gate)
        {
            EnsureProcessIdentity(processIdentity);
            if (cachedContainer != 0)
                return TryReadAtContainer(context, cachedContainer, out lastFailureReason);

            long now = getTimestamp();
            if (discoveryState == NativeStableGameplayStatsDiscoveryState.PendingRetry &&
                now < nextDiscoveryRetryTimestamp)
            {
                lastFailureReason = NativeStableGameplayStatsFailureReason.DiscoveryNotReady;
                return null;
            }

            long discoveryStarted = now;
            discoveryAttempts++;
            bool resolved = TryDiscoverContainer(
                context,
                out nuint container,
                out NativeStableGameplayStatsDiscoveryFailure discoveryFailure,
                out NativeStableGameplayStatsDiscoveryReport report);
            long discoveryCompleted = getTimestamp();
            lastDiscoveryDuration = Elapsed(discoveryStarted, discoveryCompleted);
            lastDiscoveryReport = report;
            if (!resolved)
            {
                ScheduleContainerRetry(discoveryCompleted, discoveryFailure);
                lastFailureReason = NativeStableGameplayStatsFailureReason.DiscoveryNotReady;
                return null;
            }

            cachedContainer = container;
            discoveryState = NativeStableGameplayStatsDiscoveryState.Resolved;
            successfulDiscoveryCount++;
            lastDiscoveryFailure = NativeStableGameplayStatsDiscoveryFailure.None;
            nextDiscoveryRetryTimestamp = 0;
            processAttachToDiscoveryReady = Elapsed(
                processObservedTimestamp,
                discoveryCompleted);
            return TryReadAtContainer(context, cachedContainer, out lastFailureReason);
        }
    }

    public NativeStableGameplayStats? ReadCached(
        NativeStableGameProcessContext context,
        string processIdentity,
        out NativeStableGameplayStatsFailureReason failureReason)
    {
        lock (gate)
        {
            if (!string.Equals(cachedProcessIdentity, processIdentity, StringComparison.Ordinal))
            {
                failureReason = NativeStableGameplayStatsFailureReason.ProcessIdentityMismatch;
                lastFailureReason = failureReason;
                return null;
            }
            if (cachedContainer == 0)
            {
                failureReason = NativeStableGameplayStatsFailureReason.DiscoveryNotReady;
                lastFailureReason = failureReason;
                return null;
            }

            NativeStableGameplayStats? stats = TryReadAtContainer(
                context,
                cachedContainer,
                out failureReason);
            lastFailureReason = failureReason;
            return stats;
        }
    }

    public void Invalidate()
    {
        lock (gate)
        {
            cachedProcessIdentity = null;
            cachedContainer = 0;
            cachedRuleset = 0;
            lastGameplayBase = 0;
            lastScoreBase = 0;
            processObservedTimestamp = 0;
            nextDiscoveryRetryTimestamp = 0;
            nextObservationTimestamp = 0;
            observationDeadlineTimestamp = 0;
            discoveryAttempts = 0;
            successfulDiscoveryCount = 0;
            processAttachToDiscoveryReady = null;
            lastDiscoveryDuration = null;
            discoveryState = NativeStableGameplayStatsDiscoveryState.NotAttempted;
            lastDiscoveryFailure = NativeStableGameplayStatsDiscoveryFailure.None;
            lastDiscoveryReport = NativeStableGameplayStatsDiscoveryReport.NotAttempted;
            lastFailureReason = NativeStableGameplayStatsFailureReason.DiscoveryNotReady;
        }
    }

    private void EnsureProcessIdentity(string processIdentity)
    {
        if (string.Equals(cachedProcessIdentity, processIdentity, StringComparison.Ordinal))
            return;

        cachedProcessIdentity = processIdentity;
        cachedContainer = 0;
        cachedRuleset = 0;
        lastGameplayBase = 0;
        lastScoreBase = 0;
        processObservedTimestamp = getTimestamp();
        nextDiscoveryRetryTimestamp = 0;
        nextObservationTimestamp = 0;
        observationDeadlineTimestamp = 0;
        discoveryAttempts = 0;
        successfulDiscoveryCount = 0;
        processAttachToDiscoveryReady = null;
        lastDiscoveryDuration = null;
        discoveryState = NativeStableGameplayStatsDiscoveryState.NotAttempted;
        lastDiscoveryFailure = NativeStableGameplayStatsDiscoveryFailure.None;
        lastDiscoveryReport = NativeStableGameplayStatsDiscoveryReport.NotAttempted;
        lastFailureReason = NativeStableGameplayStatsFailureReason.DiscoveryNotReady;
    }

    private static bool TryDiscoverContainer(
        NativeStableGameProcessContext context,
        out nuint container,
        out NativeStableGameplayStatsDiscoveryFailure failure,
        out NativeStableGameplayStatsDiscoveryReport report)
    {
        container = 0;
        NativeStableMemoryRegion[] executableRegions = context.Regions
            .Where(region => IsExecutable(region.Protect))
            .ToArray();
        IReadOnlyList<nuint> matches = NativeStableProcessPatternSearch.FindAll(
            context.Memory,
            executableRegions,
            ContainerPattern,
            maximumMatches: 2);

        if (matches.Count == 0)
        {
            failure = NativeStableGameplayStatsDiscoveryFailure.SignatureAbsent;
            report = DiscoveryReport(
                NativeStableGameplayStatsDiscoveryOutcome.SignatureAbsent,
                0);
            return false;
        }
        if (matches.Count > 1)
        {
            failure = NativeStableGameplayStatsDiscoveryFailure.MultipleSignatureMatches;
            report = DiscoveryReport(
                NativeStableGameplayStatsDiscoveryOutcome.MultipleSignatureMatches,
                matches.Count);
            return false;
        }

        nuint match = matches[0];
        if (match < 0xb ||
            !context.Memory.TryReadPointer(match - 0xb, out container) ||
            container == 0)
        {
            failure = NativeStableGameplayStatsDiscoveryFailure.ContainerOperandUnreadable;
            report = DiscoveryReport(
                NativeStableGameplayStatsDiscoveryOutcome.ContainerOperandUnreadable,
                1);
            container = 0;
            return false;
        }

        int pointerBytes = context.Memory.Is32Bit ? sizeof(uint) : sizeof(ulong);
        if (!IsReadableAlignedPointer(context.Regions, container, 0x4 + pointerBytes) ||
            !context.Memory.ReadBytes(container + 0x4, pointerBytes).Complete)
        {
            failure = NativeStableGameplayStatsDiscoveryFailure.ContainerInvalid;
            report = DiscoveryReport(
                NativeStableGameplayStatsDiscoveryOutcome.ContainerInvalid,
                1);
            container = 0;
            return false;
        }

        failure = NativeStableGameplayStatsDiscoveryFailure.None;
        report = new(
            NativeStableGameplayStatsDiscoveryOutcome.ContainerResolved,
            1,
            1,
            1,
            1,
            0,
            [],
            container);
        return true;

        static NativeStableGameplayStatsDiscoveryReport DiscoveryReport(
            NativeStableGameplayStatsDiscoveryOutcome outcome,
            int matchCount) => new(
                outcome,
                matchCount,
                0,
                0,
                0,
                0,
                [],
                0);
    }

    private NativeStableGameplayStats? TryReadAtContainer(
        NativeStableGameProcessContext context,
        nuint container,
        out NativeStableGameplayStatsFailureReason failureReason)
    {
        // osu!stable updates the score counters and its separately stored
        // accuracy on different writes. Allow one 60 Hz frame for those writes
        // to converge, always using a fresh chain/sample. A mismatch that lasts
        // beyond this bounded window still fails closed.
        const int maxCoherenceAttempts = 20;
        for (int attempt = 1; attempt <= maxCoherenceAttempts; attempt++)
        {
            NativeStableGameplayStats? sample = TryReadAtContainerOnce(
                context,
                container,
                out failureReason,
                out cachedRuleset,
                out lastGameplayBase,
                out lastScoreBase);
            if (sample is not null ||
                failureReason != NativeStableGameplayStatsFailureReason.AccuracyMismatch ||
                attempt == maxCoherenceAttempts)
            {
                return sample;
            }

            // Score counters and accuracy are updated independently. Every retry
            // starts again at the process-global container; no child address from
            // a previous attempt is reused.
            Thread.Sleep(1);
        }

        throw new UnreachableException();
    }

    private static NativeStableGameplayStats? TryReadAtContainerOnce(
        NativeStableGameProcessContext context,
        nuint container,
        out NativeStableGameplayStatsFailureReason failureReason,
        out nuint ruleset,
        out nuint gameplayBase,
        out nuint scoreBase)
    {
        gameplayBase = 0;
        scoreBase = 0;
        if (!context.Memory.TryReadPointer(container + 0x4, out ruleset) ||
            !IsReadableAlignedPointer(context.Regions, ruleset, RootProbeBytes))
        {
            failureReason = NativeStableGameplayStatsFailureReason.PointerChainUnavailable;
            return null;
        }

        return TryReadAtRootOnce(
            context.Memory,
            ruleset,
            out failureReason,
            out gameplayBase,
            out scoreBase);
    }

    private void ScheduleContainerRetry(
        long now,
        NativeStableGameplayStatsDiscoveryFailure failure)
    {
        discoveryState = NativeStableGameplayStatsDiscoveryState.PendingRetry;
        lastDiscoveryFailure = failure;
        nextDiscoveryRetryTimestamp = AddDelay(
            now,
            RetryDelayAfterFailure(discoveryAttempts));
    }

    private static bool IsReadableAlignedPointer(
        IReadOnlyList<NativeStableMemoryRegion> regions,
        nuint address,
        int requiredBytes)
    {
        if (address < 0x10000 || (address & 3) != 0)
            return false;

        foreach (NativeStableMemoryRegion region in regions)
        {
            if (region.Contains(address) &&
                region.Size - (address - region.BaseAddress) >= checked((nuint)requiredBytes))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExecutable(uint protection) =>
        (protection & 0xff) is 0x10 or 0x20 or 0x40 or 0x80;

    private static TimeSpan RetryDelayAfterFailure(int attempts) => attempts switch
    {
        1 => TimeSpan.FromSeconds(1),
        2 => TimeSpan.FromSeconds(2),
        _ => TimeSpan.FromSeconds(5)
    };

    private long AddDelay(long timestamp, TimeSpan delay) => checked(
        timestamp + (long)Math.Ceiling(delay.TotalSeconds * timestampFrequency));

    private TimeSpan Elapsed(long start, long end) => TimeSpan.FromSeconds(
        Math.Max(0, end - start) / (double)timestampFrequency);

    private TimeSpan? RemainingRetryDelay(long now)
    {
        if (discoveryState != NativeStableGameplayStatsDiscoveryState.PendingRetry)
            return null;

        return TimeSpan.FromSeconds(
            Math.Max(0, nextDiscoveryRetryTimestamp - now) /
            (double)timestampFrequency);
    }

    private void BeginCandidateObservation(
        INativeStableMemoryReader memory,
        long now,
        bool refreshExistingCandidates)
    {
        if (refreshExistingCandidates)
        {
            lastDiscoveryReport = lastDiscoveryReport with
            {
                Candidates = lastDiscoveryReport.Candidates
                    .Select(candidate => TraceCandidate(
                        memory,
                        candidate.RootAddress,
                        candidate.SourceRegionBase,
                        candidate.SourceOffset))
                    .ToImmutableArray()
            };
            lastDiscoveryReport = Recount(lastDiscoveryReport);
        }

        if (lastDiscoveryReport.PlausibleCandidateCount == 0)
        {
            ScheduleRetry(
                now,
                NativeStableGameplayStatsDiscoveryFailure.ZeroValidRoots,
                NativeStableGameplayStatsDiscoveryOutcome.ZeroValidRoots);
            return;
        }

        discoveryState = NativeStableGameplayStatsDiscoveryState.ObservingCandidates;
        lastDiscoveryFailure = NativeStableGameplayStatsDiscoveryFailure.None;
        nextDiscoveryRetryTimestamp = 0;
        nextObservationTimestamp = AddDelay(now, ObservationInterval);
        observationDeadlineTimestamp = AddDelay(now, ObservationWindow);
        lastDiscoveryReport = lastDiscoveryReport with
        {
            Outcome = NativeStableGameplayStatsDiscoveryOutcome.ObservingCandidates,
            ActiveCandidateCount = 0,
            ResolvedRoot = 0
        };
    }

    private void AdvanceCandidateObservation(INativeStableMemoryReader memory, long now)
    {
        if (now < nextObservationTimestamp)
            return;

        ImmutableArray<NativeStableGameplayStatsCandidateTrace> observed =
            lastDiscoveryReport.Candidates
                .Select(previous => ObserveCandidate(
                    previous,
                    TraceCandidate(
                        memory,
                        previous.RootAddress,
                        previous.SourceRegionBase,
                        previous.SourceOffset)))
                .ToImmutableArray();
        lastDiscoveryReport = Recount(lastDiscoveryReport with { Candidates = observed });

        NativeStableGameplayStatsCandidateTrace[] active = observed
            .Where(candidate => candidate.TemporallyActive)
            .ToArray();
        if (now < observationDeadlineTimestamp)
        {
            nextObservationTimestamp = Math.Min(
                AddDelay(now, ObservationInterval),
                observationDeadlineTimestamp);
            return;
        }

        if (active.Length == 1)
        {
            cachedRuleset = active[0].RootAddress;
            discoveryState = NativeStableGameplayStatsDiscoveryState.Resolved;
            successfulDiscoveryCount++;
            lastDiscoveryFailure = NativeStableGameplayStatsDiscoveryFailure.None;
            lastDiscoveryReport = lastDiscoveryReport with
            {
                Outcome = NativeStableGameplayStatsDiscoveryOutcome.OneCandidateResolved,
                ActiveCandidateCount = 1,
                ResolvedRoot = cachedRuleset
            };
            nextDiscoveryRetryTimestamp = 0;
            nextObservationTimestamp = 0;
            observationDeadlineTimestamp = 0;
            processAttachToDiscoveryReady = Elapsed(processObservedTimestamp, now);
            lastFailureReason = NativeStableGameplayStatsFailureReason.None;
            return;
        }

        if (active.Length > 1)
        {
            ScheduleRetry(
                now,
                NativeStableGameplayStatsDiscoveryFailure.MultipleActiveRoots,
                NativeStableGameplayStatsDiscoveryOutcome.MultipleActiveRoots);
            return;
        }

        ScheduleRetry(
            now,
            NativeStableGameplayStatsDiscoveryFailure.ZeroActiveRoots,
            NativeStableGameplayStatsDiscoveryOutcome.ZeroActiveRoots);
    }

    private void ScheduleRetry(
        long now,
        NativeStableGameplayStatsDiscoveryFailure failure,
        NativeStableGameplayStatsDiscoveryOutcome outcome)
    {
        discoveryState = NativeStableGameplayStatsDiscoveryState.PendingRetry;
        lastDiscoveryFailure = failure;
        lastDiscoveryReport = lastDiscoveryReport with
        {
            Outcome = outcome,
            ActiveCandidateCount = lastDiscoveryReport.Candidates.Count(
                candidate => candidate.TemporallyActive),
            ResolvedRoot = 0
        };
        nextDiscoveryRetryTimestamp = AddDelay(now, RetryDelayAfterFailure(discoveryAttempts));
        nextObservationTimestamp = 0;
        observationDeadlineTimestamp = 0;
    }

    private static NativeStableGameplayStatsDiscoveryReport Recount(
        NativeStableGameplayStatsDiscoveryReport report) => report with
    {
        StructuralCandidateCount = report.Candidates.Count(
            candidate => candidate.PointerChainStructurallyValid),
        ValidatedCandidateCount = report.Candidates.Count(
            candidate => candidate.ProbeReadsComplete),
        PlausibleCandidateCount = report.Candidates.Count(
            candidate => candidate.PlausibleCandidate),
        ActiveCandidateCount = report.Candidates.Count(
            candidate => candidate.TemporallyActive)
    };

    private static NativeStableGameplayStatsCandidateTrace ObserveCandidate(
        NativeStableGameplayStatsCandidateTrace previous,
        NativeStableGameplayStatsCandidateTrace current)
    {
        int observationCount = previous.TemporalObservationCount + 1;
        if (!previous.PlausibleCandidate || !current.PlausibleCandidate ||
            previous.SemanticSample is null || current.SemanticSample is null)
        {
            return current with
            {
                TemporalObservationCount = observationCount,
                TemporalEvidence = current.PlausibleCandidate
                    ? "BaselineResetAfterInvalidSample"
                    : current.SemanticFailureReason.ToString()
            };
        }

        if (previous.GameplayBase != current.GameplayBase ||
            previous.ScoreBase != current.ScoreBase ||
            previous.AccuracyBase != current.AccuracyBase ||
            previous.ModsBase != current.ModsBase)
        {
            return current with
            {
                TemporalObservationCount = observationCount,
                TemporalEvidence = "ChildChainChangedBaselineReset"
            };
        }

        NativeStableGameplayStats before = previous.SemanticSample;
        NativeStableGameplayStats after = current.SemanticSample;
        bool modsStable = before.Mods.Select(mod => mod.Acronym)
            .SequenceEqual(after.Mods.Select(mod => mod.Acronym), StringComparer.Ordinal);
        bool monotonic = after.Score >= before.Score &&
            after.MaxComboReached >= before.MaxComboReached &&
            after.Count300 >= before.Count300 &&
            after.Count100 >= before.Count100 &&
            after.Count50 >= before.Count50 &&
            after.CountMiss >= before.CountMiss;
        bool progressed = after.Score > before.Score ||
            after.MaxComboReached > before.MaxComboReached ||
            after.Count300 > before.Count300 ||
            after.Count100 > before.Count100 ||
            after.Count50 > before.Count50 ||
            after.CountMiss > before.CountMiss ||
            after.CurrentCombo != before.CurrentCombo;

        if (!modsStable || !monotonic)
        {
            return current with
            {
                TemporalObservationCount = observationCount,
                TemporalEvidence = !modsStable
                    ? "ModsChangedBaselineReset"
                    : "CountersRegressedBaselineReset"
            };
        }

        int progressions = previous.CoherentProgressionCount + (progressed ? 1 : 0);
        bool active = progressions > 0;
        return current with
        {
            TemporalObservationCount = observationCount,
            CoherentProgressionCount = progressions,
            TemporalEvidence = progressed
                ? DescribeProgress(before, after)
                : previous.TemporalEvidence == "InitialPlausibleSample"
                    ? "PlausibleButFrozen"
                    : previous.TemporalEvidence,
            TemporallyActive = active,
            ActiveGameplayEvidence = active
        };
    }

    private static string DescribeProgress(
        NativeStableGameplayStats before,
        NativeStableGameplayStats after)
    {
        var changes = new List<string>();
        Add("Score", before.Score, after.Score);
        Add("Combo", before.CurrentCombo, after.CurrentCombo);
        Add("MaxCombo", before.MaxComboReached, after.MaxComboReached);
        Add("300", before.Count300, after.Count300);
        Add("100", before.Count100, after.Count100);
        Add("50", before.Count50, after.Count50);
        Add("Miss", before.CountMiss, after.CountMiss);
        return "CoherentProgress:" + string.Join(',', changes);

        void Add(string name, int oldValue, int newValue)
        {
            if (oldValue != newValue)
                changes.Add($"{name}={oldValue}->{newValue}");
        }
    }

    private static NativeStableGameplayStatsDiscoveryReport DiscoverRuleset(
        NativeStableGameProcessContext context)
    {
        var traces = ImmutableArray.CreateBuilder<NativeStableGameplayStatsCandidateTrace>();
        int structuralCandidateCount = 0;
        int validatedCandidateCount = 0;
        int plausibleCandidateCount = 0;
        foreach (NativeStableMemoryRegion region in context.Regions.Where(IsWritablePrivate))
        {
            nuint offset = 0;
            while (offset < region.Size)
            {
                nuint remaining = region.Size - offset;
                int requested = remaining > checked((nuint)(ChunkBytes + RootProbeBytes))
                    ? ChunkBytes + RootProbeBytes
                    : checked((int)remaining);
                NativeStableMemoryReadResult read = context.Memory.ReadBytes(
                    region.BaseAddress + offset, requested);
                if (read.BytesRead >= RootProbeBytes)
                {
                    int scanBytes = Math.Min(ChunkBytes, read.BytesRead - RootProbeBytes + 1);
                    int alignment = checked((int)((4 - ((region.BaseAddress + offset) & 3)) & 3));
                    for (int index = alignment; index < scanBytes; index += sizeof(uint))
                    {
                        if (!HasRulesetShape(read.Data, index))
                            continue;
                        nuint root = region.BaseAddress + offset + checked((nuint)index);
                        NativeStableGameplayStatsCandidateTrace trace = TraceCandidate(
                            context.Memory,
                            root,
                            region.BaseAddress,
                            root - region.BaseAddress);
                        traces.Add(trace);
                        if (trace.PointerChainStructurallyValid)
                            structuralCandidateCount++;
                        if (trace.ProbeReadsComplete)
                            validatedCandidateCount++;
                        if (trace.PlausibleCandidate)
                            plausibleCandidateCount++;
                    }
                }

                if (remaining <= ChunkBytes)
                    break;
                offset += ChunkBytes;
            }
        }
        NativeStableGameplayStatsDiscoveryOutcome outcome = plausibleCandidateCount == 0
            ? NativeStableGameplayStatsDiscoveryOutcome.ZeroValidRoots
            : NativeStableGameplayStatsDiscoveryOutcome.ObservingCandidates;
        return new(
            outcome,
            traces.Count,
            structuralCandidateCount,
            validatedCandidateCount,
            plausibleCandidateCount,
            0,
            traces.ToImmutable(),
            0);
    }

    private static bool HasRulesetShape(byte[] data, int index)
    {
        uint first = BitConverter.ToUInt32(data, index);
        if (first < 0x10000 || (first & 3) != 0)
            return false;
        for (int cell = 1; cell < 8; cell++)
        {
            if (BitConverter.ToUInt32(data, index + (cell * sizeof(uint))) !=
                first + (uint)(cell * 0x0c))
                return false;
        }
        return BitConverter.ToUInt32(data, index + 0x64) >= 0x10000;
    }

    private static NativeStableGameplayStats? TryReadAtRoot(
        INativeStableMemoryReader memory,
        nuint ruleset,
        out NativeStableGameplayStatsFailureReason failureReason)
    {
        const int maxCoherenceAttempts = 4;
        for (int attempt = 1; attempt <= maxCoherenceAttempts; attempt++)
        {
            NativeStableGameplayStats? sample = TryReadAtRootOnce(
                memory,
                ruleset,
                out failureReason);
            if (sample is not null ||
                failureReason != NativeStableGameplayStatsFailureReason.AccuracyMismatch ||
                attempt == maxCoherenceAttempts)
            {
                return sample;
            }

            // Score counters and the separately stored accuracy are updated
            // independently. Give the writer a bounded window to finish, then
            // take an entirely fresh sample. A persistent mismatch still fails
            // closed; no previous snapshot is reused.
            Thread.Sleep(1);
        }

        throw new UnreachableException();
    }

    private static NativeStableGameplayStats? TryReadAtRootOnce(
        INativeStableMemoryReader memory,
        nuint ruleset,
        out NativeStableGameplayStatsFailureReason failureReason)
    {
        return TryReadAtRootOnce(
            memory,
            ruleset,
            out failureReason,
            out _,
            out _);
    }

    private static NativeStableGameplayStats? TryReadAtRootOnce(
        INativeStableMemoryReader memory,
        nuint ruleset,
        out NativeStableGameplayStatsFailureReason failureReason,
        out nuint gameplayBase,
        out nuint scoreBase)
    {
        gameplayBase = 0;
        scoreBase = 0;
        if (!memory.TryReadPointer(ruleset + 0x64, out gameplayBase) ||
            !memory.TryReadPointer(gameplayBase + 0x38, out scoreBase) ||
            !memory.TryReadPointer(gameplayBase + 0x48, out nuint accuracyBase) ||
            !memory.TryReadPointer(scoreBase + 0x1c, out nuint modsBase))
        {
            failureReason = NativeStableGameplayStatsFailureReason.PointerChainUnavailable;
            return null;
        }

        NativeStableMemoryReadResult score = memory.ReadBytes(scoreBase, ScoreProbeBytes);
        NativeStableMemoryReadResult accuracy = memory.ReadBytes(accuracyBase + 0x0c, sizeof(double));
        NativeStableMemoryReadResult mods = memory.ReadBytes(modsBase + 0x8, sizeof(int) * 2);
        if (!score.Complete)
        {
            failureReason = NativeStableGameplayStatsFailureReason.ScoreReadIncomplete;
            return null;
        }
        if (!accuracy.Complete)
        {
            failureReason = NativeStableGameplayStatsFailureReason.AccuracyReadIncomplete;
            return null;
        }
        if (!mods.Complete)
        {
            failureReason = NativeStableGameplayStatsFailureReason.ModsReadIncomplete;
            return null;
        }

        int maxCombo = BitConverter.ToUInt16(score.Data, 0x68);
        int scoreValue = BitConverter.ToInt32(score.Data, 0x78);
        int count100 = BitConverter.ToUInt16(score.Data, 0x88);
        int count300 = BitConverter.ToUInt16(score.Data, 0x8a);
        int count50 = BitConverter.ToUInt16(score.Data, 0x8c);
        int countMiss = BitConverter.ToUInt16(score.Data, 0x92);
        int combo = BitConverter.ToUInt16(score.Data, 0x94);
        double rawAccuracy = BitConverter.ToDouble(accuracy.Data, 0);
        double normalizedAccuracy = rawAccuracy > 1.000001 && rawAccuracy <= 100.000001
            ? rawAccuracy / 100d
            : rawAccuracy;
        int rawMods = BitConverter.ToInt32(mods.Data, 0) ^
            BitConverter.ToInt32(mods.Data, sizeof(int));
        long totalHits = (long)count300 + count100 + count50 + countMiss;
        double derivedAccuracy = totalHits == 0 ? 0 :
            ((300d * count300) + (100d * count100) + (50d * count50)) /
            (300d * totalHits);

        if (!double.IsFinite(normalizedAccuracy))
        {
            failureReason = NativeStableGameplayStatsFailureReason.AccuracyNotFinite;
            return null;
        }
        if (normalizedAccuracy is < 0 or > 1.000001)
        {
            failureReason = NativeStableGameplayStatsFailureReason.AccuracyOutOfRange;
            return null;
        }
        if (totalHits is < 0 or > 100_000)
        {
            failureReason = NativeStableGameplayStatsFailureReason.HitCountOutOfRange;
            return null;
        }
        if (combo < 0 || maxCombo < 0 || combo > maxCombo)
        {
            failureReason = NativeStableGameplayStatsFailureReason.ComboOutOfRange;
            return null;
        }
        if (scoreValue < 0)
        {
            failureReason = NativeStableGameplayStatsFailureReason.ScoreNegative;
            return null;
        }
        if (rawMods < 0)
        {
            failureReason = NativeStableGameplayStatsFailureReason.ModsNegative;
            return null;
        }
        if ((rawMods & ~StandardGameplayModsMask) != 0 ||
            HasMutuallyExclusiveStandardMods(rawMods))
        {
            failureReason = NativeStableGameplayStatsFailureReason.ModsUnsupportedForStandard;
            return null;
        }
        if (totalHits > 0 && Math.Abs(normalizedAccuracy - derivedAccuracy) > 0.005)
        {
            failureReason = NativeStableGameplayStatsFailureReason.AccuracyMismatch;
            return null;
        }

        failureReason = NativeStableGameplayStatsFailureReason.None;
        return new(combo, maxCombo, count300, count100, count50, countMiss,
            normalizedAccuracy, DecodeMods(rawMods), scoreValue);
    }

    private static NativeStableGameplayStatsCandidateTrace TraceCandidate(
        INativeStableMemoryReader memory,
        nuint ruleset,
        nuint sourceRegionBase,
        nuint sourceOffset)
    {
        nuint gameplayBase = 0;
        nuint scoreBase = 0;
        nuint accuracyBase = 0;
        nuint modsBase = 0;

        if (!memory.TryReadPointer(ruleset + 0x64, out gameplayBase))
            return Rejected(PointerFailure(
                memory,
                ruleset + 0x64,
                NativeStableGameplayStatsCandidateRejectionReason.GameplayBasePointerUnreadable,
                NativeStableGameplayStatsCandidateRejectionReason.GameplayBaseNull));
        if (!memory.TryReadPointer(gameplayBase + 0x38, out scoreBase))
            return Rejected(PointerFailure(
                memory,
                gameplayBase + 0x38,
                NativeStableGameplayStatsCandidateRejectionReason.ScoreBasePointerUnreadable,
                NativeStableGameplayStatsCandidateRejectionReason.ScoreBaseNull));
        if (!memory.TryReadPointer(gameplayBase + 0x48, out accuracyBase))
            return Rejected(PointerFailure(
                memory,
                gameplayBase + 0x48,
                NativeStableGameplayStatsCandidateRejectionReason.AccuracyBasePointerUnreadable,
                NativeStableGameplayStatsCandidateRejectionReason.AccuracyBaseNull));
        if (!memory.TryReadPointer(scoreBase + 0x1c, out modsBase))
            return Rejected(PointerFailure(
                memory,
                scoreBase + 0x1c,
                NativeStableGameplayStatsCandidateRejectionReason.ModsBasePointerUnreadable,
                NativeStableGameplayStatsCandidateRejectionReason.ModsBaseNull));

        NativeStableMemoryReadResult score = memory.ReadBytes(scoreBase, ScoreProbeBytes);
        if (!score.Complete)
            return Rejected(
                NativeStableGameplayStatsCandidateRejectionReason.ScoreProbeIncomplete,
                pointerChainValid: true);
        NativeStableMemoryReadResult accuracy = memory.ReadBytes(
            accuracyBase + 0x0c,
            sizeof(double));
        if (!accuracy.Complete)
            return Rejected(
                NativeStableGameplayStatsCandidateRejectionReason.AccuracyProbeIncomplete,
                pointerChainValid: true);
        NativeStableMemoryReadResult mods = memory.ReadBytes(
            modsBase + 0x8,
            sizeof(int) * 2);
        if (!mods.Complete)
            return Rejected(
                NativeStableGameplayStatsCandidateRejectionReason.ModsProbeIncomplete,
                pointerChainValid: true);

        string rawSample = FormatRawSample(score.Data, accuracy.Data, mods.Data);

        NativeStableGameplayStats? semanticSample = TryReadAtRootOnce(
            memory,
            ruleset,
            out NativeStableGameplayStatsFailureReason semanticFailureReason);
        bool plausible = semanticSample is not null;
        return new(
            ruleset,
            sourceRegionBase,
            sourceOffset,
            gameplayBase,
            scoreBase,
            accuracyBase,
            modsBase,
            true,
            true,
            NativeStableGameplayStatsCandidateRejectionReason.None,
            semanticSample is not null,
            semanticFailureReason,
            semanticSample,
            rawSample,
            plausible,
            1,
            0,
            plausible ? "InitialPlausibleSample" : semanticFailureReason.ToString(),
            false,
            false);

        NativeStableGameplayStatsCandidateTrace Rejected(
            NativeStableGameplayStatsCandidateRejectionReason reason,
            bool pointerChainValid = false) => new(
                ruleset,
                sourceRegionBase,
                sourceOffset,
                gameplayBase,
                scoreBase,
                accuracyBase,
                modsBase,
                pointerChainValid,
                false,
                reason,
                false,
                NativeStableGameplayStatsFailureReason.DiscoveryNotReady,
                null,
                "<unavailable>",
                false,
                1,
                0,
                reason.ToString(),
                false,
                false);
    }

    private static bool HasMutuallyExclusiveStandardMods(int value)
    {
        bool hasHalfTime = (value & (1 << 8)) != 0;
        bool hasSpeedUp = (value & ((1 << 6) | (1 << 9))) != 0;
        bool hasEasy = (value & (1 << 1)) != 0;
        bool hasHardRock = (value & (1 << 4)) != 0;
        return (hasHalfTime && hasSpeedUp) || (hasEasy && hasHardRock);
    }

    private static string FormatRawSample(byte[] score, byte[] accuracy, byte[] mods)
    {
        int maxCombo = BitConverter.ToUInt16(score, 0x68);
        int scoreValue = BitConverter.ToInt32(score, 0x78);
        int count100 = BitConverter.ToUInt16(score, 0x88);
        int count300 = BitConverter.ToUInt16(score, 0x8a);
        int count50 = BitConverter.ToUInt16(score, 0x8c);
        int countMiss = BitConverter.ToUInt16(score, 0x92);
        int combo = BitConverter.ToUInt16(score, 0x94);
        double rawAccuracy = BitConverter.ToDouble(accuracy, 0);
        int rawMods = BitConverter.ToInt32(mods, 0) ^ BitConverter.ToInt32(mods, sizeof(int));
        return FormattableString.Invariant(
            $"Combo={combo}/{maxCombo};300={count300};100={count100};50={count50};Miss={countMiss};RawAccuracy={rawAccuracy:R};Score={scoreValue};RawMods={rawMods}");
    }

    private static NativeStableGameplayStatsCandidateRejectionReason PointerFailure(
        INativeStableMemoryReader memory,
        nuint address,
        NativeStableGameplayStatsCandidateRejectionReason unreadable,
        NativeStableGameplayStatsCandidateRejectionReason nullPointer)
    {
        int pointerBytes = memory.Is32Bit ? sizeof(uint) : sizeof(ulong);
        NativeStableMemoryReadResult read = memory.ReadBytes(address, pointerBytes);
        if (!read.Complete)
            return unreadable;
        bool isNull = memory.Is32Bit
            ? BitConverter.ToUInt32(read.Data, 0) == 0
            : BitConverter.ToUInt64(read.Data, 0) == 0;
        return isNull ? nullPointer : unreadable;
    }

    private static ImmutableArray<LiveGameplayMod> DecodeMods(int value)
    {
        var result = ImmutableArray.CreateBuilder<LiveGameplayMod>();
        Add(1 << 0, "NF"); Add(1 << 1, "EZ"); Add(1 << 3, "HD");
        Add(1 << 4, "HR"); Add(1 << 5, "SD"); Add(1 << 6, "DT");
        Add(1 << 8, "HT"); Add(1 << 9, "NC"); Add(1 << 10, "FL");
        Add(1 << 12, "SO"); Add(1 << 14, "PF"); Add(1 << 29, "V2");
        return result.ToImmutable();

        void Add(int flag, string acronym)
        {
            if ((value & flag) != 0)
                result.Add(new(acronym));
        }
    }

    private static bool IsWritablePrivate(NativeStableMemoryRegion region)
    {
        uint protection = region.Protect & 0xff;
        return region.Type == MemoryPrivate && protection is 0x04 or 0x08 or 0x40 or 0x80;
    }
}
