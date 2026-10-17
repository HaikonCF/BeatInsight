using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace BeatInsight.Diagnostics.NativeLazer;

internal enum NativeLazerFastOutcome
{
    Available,
    NoBeatmap,
    Unknown
}

internal enum NativeLazerCacheMissReason
{
    None,
    NoCache,
    PidChanged,
    ProcessStartIdentityChanged,
    RootValidationFailed,
    ProcessExited,
    Unknown
}

internal readonly record struct NativeLazerGenerationIdentity(int ProcessId, DateTime ProcessStartIdentity)
{
    internal string Display => ProcessId > 0
        ? $"{ProcessId}@{ProcessStartIdentity.ToUniversalTime():O}"
        : "unavailable";
}

internal sealed record NativeLazerRootValidationFingerprint(
    ulong RootMethodTable,
    ulong BeatmapBindableMethodTable,
    ulong VersionStringMethodTable,
    int MinimumCoherentRootReferences);

internal sealed record NativeLazerFastLayout(
    ulong VersionHash,
    ulong BeatmapManager,
    ulong Api,
    ulong Storage,
    ulong Beatmap,
    ulong Ruleset,
    ulong SelectedMods,
    ulong Realm,
    ulong Dependencies,
    ulong BindableValue,
    ulong BindableDisabled,
    ulong WorkingBeatmapInfo,
    ulong WorkingBeatmapSetInfo,
    ulong BeatmapInfoId,
    ulong BeatmapInfoSet,
    ulong BeatmapInfoOnlineId,
    ulong BeatmapInfoHash,
    ulong BeatmapSetOnlineId)
{
    internal ulong[] RootReferenceOffsets =>
    [
        BeatmapManager, Api, Storage, Beatmap, Ruleset, SelectedMods, Realm, Dependencies
    ];
}

internal sealed record NativeLazerRootCacheEntry(
    NativeLazerGenerationIdentity GenerationIdentity,
    ulong RootAddress,
    NativeLazerRootValidationFingerprint RootValidationFingerprint,
    NativeLazerFastLayout Layout);

internal sealed record NativeLazerRootCacheLookup(
    NativeLazerRootCacheEntry? Entry,
    bool CacheHit,
    NativeLazerCacheMissReason CacheMissReason);

internal sealed class NativeLazerDiagnosticRootCache
{
    private NativeLazerRootCacheEntry? entry;
    private NativeLazerCacheMissReason pendingMissReason = NativeLazerCacheMissReason.NoCache;

    internal NativeLazerRootCacheEntry? Current => entry;

    internal NativeLazerRootCacheLookup Lookup(NativeLazerGenerationIdentity generation, bool processExists)
    {
        if (!processExists)
        {
            entry = null;
            pendingMissReason = NativeLazerCacheMissReason.ProcessExited;
            return new(null, false, NativeLazerCacheMissReason.ProcessExited);
        }

        if (entry is null)
        {
            NativeLazerCacheMissReason reason = pendingMissReason;
            pendingMissReason = NativeLazerCacheMissReason.NoCache;
            return new(null, false, reason);
        }

        if (entry.GenerationIdentity.ProcessId != generation.ProcessId)
        {
            entry = null;
            pendingMissReason = NativeLazerCacheMissReason.PidChanged;
            return new(null, false, NativeLazerCacheMissReason.PidChanged);
        }

        if (entry.GenerationIdentity.ProcessStartIdentity != generation.ProcessStartIdentity)
        {
            entry = null;
            pendingMissReason = NativeLazerCacheMissReason.ProcessStartIdentityChanged;
            return new(null, false, NativeLazerCacheMissReason.ProcessStartIdentityChanged);
        }

        return new(entry, true, NativeLazerCacheMissReason.None);
    }

    internal bool TryStore(
        NativeLazerRootCacheEntry candidate,
        NativeLazerGenerationIdentity currentGeneration,
        bool processExists)
    {
        if (!processExists || candidate.GenerationIdentity != currentGeneration)
        {
            entry = null;
            pendingMissReason = processExists
                ? NativeLazerCacheMissReason.ProcessStartIdentityChanged
                : NativeLazerCacheMissReason.ProcessExited;
            return false;
        }

        entry = candidate;
        pendingMissReason = NativeLazerCacheMissReason.None;
        return true;
    }

    internal void Invalidate(NativeLazerCacheMissReason reason)
    {
        entry = null;
        pendingMissReason = reason;
    }
}

internal interface INativeLazerFastMemory
{
    long BytesRead { get; }
    int ReadOperationCount { get; }
    bool IsReadable(ulong address, int count);
    byte? ReadByte(ulong address);
    int? ReadInt32(ulong address);
    ulong? ReadPointer(ulong address);
    Guid? ReadGuid(ulong address);
    NativeLazerRemoteText? ReadString(ulong address, int maximumCharacters);
}

internal sealed record NativeLazerRemoteText(string Value, ulong MethodTable);

internal sealed record NativeLazerFastRefreshMetrics(
    double RootRevalidationMs,
    double BeatmapDereferenceMs,
    double MetadataReadMs,
    double BlobValidationMs,
    double TotalRefreshMs,
    long BytesRead,
    int ReadOperationCount);

internal sealed record NativeLazerDiagnosticStage(
    int Number,
    string Name,
    ulong? SourceAddress,
    ulong? OffsetUsed,
    string TargetAddressOrValue,
    bool Readable,
    bool IsNull,
    bool ValidationResult,
    string? FailureReason);

internal sealed record NativeLazerDiagnosticTrace(
    string Path,
    bool ValidationResult,
    string? FailureReason,
    IReadOnlyList<NativeLazerDiagnosticStage> Stages);

internal sealed record NativeLazerDiagnosticStageComparison(
    int Number,
    string Name,
    bool Match,
    string? Difference);

internal sealed record NativeLazerFastRefreshResult
{
    internal required NativeLazerGenerationIdentity ProcessIdentity { get; init; }
    internal ulong? CachedRoot { get; init; }
    internal bool CacheHit { get; init; }
    internal NativeLazerCacheMissReason CacheMissReason { get; init; }
    internal bool RootValid { get; init; }
    internal int? BeatmapOnlineId { get; init; }
    internal int? BeatmapSetOnlineId { get; init; }
    internal string? BeatmapHash { get; init; }
    internal bool? BlobExists { get; init; }
    internal NativeLazerFastOutcome Outcome { get; init; } = NativeLazerFastOutcome.Unknown;
    internal bool StaleAvailableDetected { get; init; }
    internal bool HeavyDiscoveryTriggered { get; init; }
    internal double HeavyDiscoveryMs { get; init; }
    internal long HeavyDiscoveryBytesRead { get; init; }
    internal int HeavyDiscoveryReadOperationCount { get; init; }
    internal double RootAcquisitionMs { get; init; }
    internal long RootAcquisitionBytesRead { get; init; }
    internal int RootAcquisitionReadOperationCount { get; init; }
    internal bool PersistentHintAttempted { get; init; }
    internal bool PersistentHintAccepted { get; init; }
    internal string? PersistentHintMissReason { get; init; }
    internal string? FailureReason { get; init; }
    internal NativeLazerDiagnosticTrace? FastPathTrace { get; init; }
    internal NativeLazerDiagnosticTrace? CleanRoomTrace { get; init; }
    internal IReadOnlyList<NativeLazerDiagnosticTrace> RejectedCandidateTraces { get; init; } = [];
    internal IReadOnlyList<NativeLazerDiagnosticStageComparison> TraceComparison { get; init; } = [];
    internal required NativeLazerFastRefreshMetrics Metrics { get; init; }
}

internal sealed record NativeLazerHeavyDiscoveryResult(
    NativeLazerRootCacheEntry Entry,
    double ElapsedMs,
    long BytesRead,
    NativeLazerDiagnosticTrace? CleanRoomTrace = null,
    IReadOnlyList<NativeLazerDiagnosticTrace>? RejectedCandidateTraces = null,
    int ReadOperationCount = 0,
    bool HeavyScanTriggered = true,
    bool PersistentHintAttempted = false,
    bool PersistentHintAccepted = false,
    string? PersistentHintMissReason = null);

internal sealed class NativeLazerFastRefreshCoordinator
{
    private readonly NativeLazerDiagnosticRootCache cache;
    private readonly NativeLazerFastRefresher refresher;

    internal NativeLazerFastRefreshCoordinator(
        NativeLazerDiagnosticRootCache? cache = null,
        NativeLazerFastRefresher? refresher = null)
    {
        this.cache = cache ?? new NativeLazerDiagnosticRootCache();
        this.refresher = refresher ?? new NativeLazerFastRefresher();
    }

    internal NativeLazerDiagnosticRootCache Cache => cache;

    internal NativeLazerFastRefreshResult Refresh(
        NativeLazerGenerationIdentity generation,
        bool processExists,
        INativeLazerFastMemory memory,
        Func<NativeLazerGenerationIdentity, INativeLazerFastMemory, NativeLazerHeavyDiscoveryResult?> heavyDiscovery,
        Func<NativeLazerGenerationIdentity, bool> generationStillCurrent,
        Func<string, bool> blobExists)
    {
        NativeLazerRootCacheLookup lookup = cache.Lookup(generation, processExists);
        if (!processExists)
            return Unknown(generation, lookup.CacheMissReason, "Process no longer exists.");

        NativeLazerRootCacheEntry? entry = lookup.Entry;
        NativeLazerHeavyDiscoveryResult? discovery = null;
        if (entry is null)
        {
            discovery = heavyDiscovery(generation, memory);
            if (discovery is null)
                return Unknown(generation, lookup.CacheMissReason, "Heavy root discovery did not produce one validated root.");

            bool stillExists = generationStillCurrent(generation);
            if (!cache.TryStore(discovery.Entry, generation, stillExists))
            {
                return Unknown(generation, stillExists
                    ? NativeLazerCacheMissReason.ProcessStartIdentityChanged
                    : NativeLazerCacheMissReason.ProcessExited,
                    "Process generation changed during heavy discovery.") with
                {
                    HeavyDiscoveryTriggered = discovery.HeavyScanTriggered,
                    HeavyDiscoveryMs = discovery.HeavyScanTriggered ? discovery.ElapsedMs : 0,
                    HeavyDiscoveryBytesRead = discovery.HeavyScanTriggered ? discovery.BytesRead : 0,
                    HeavyDiscoveryReadOperationCount = discovery.HeavyScanTriggered
                        ? discovery.ReadOperationCount
                        : 0,
                    RootAcquisitionMs = discovery.ElapsedMs,
                    RootAcquisitionBytesRead = discovery.BytesRead,
                    RootAcquisitionReadOperationCount = discovery.ReadOperationCount,
                    PersistentHintAttempted = discovery.PersistentHintAttempted,
                    PersistentHintAccepted = discovery.PersistentHintAccepted,
                    PersistentHintMissReason = discovery.PersistentHintMissReason
                };
            }

            entry = discovery.Entry;
        }

        NativeLazerFastRefreshResult result = refresher.Refresh(
            generation,
            entry,
            cache,
            memory,
            generationStillCurrent,
            blobExists);
        return result with
        {
            CacheHit = discovery is null && result.CacheHit,
            CacheMissReason = discovery is null ? result.CacheMissReason : lookup.CacheMissReason,
            HeavyDiscoveryTriggered = discovery?.HeavyScanTriggered == true,
            HeavyDiscoveryMs = discovery?.HeavyScanTriggered == true ? discovery.ElapsedMs : 0,
            HeavyDiscoveryBytesRead = discovery?.HeavyScanTriggered == true ? discovery.BytesRead : 0,
            HeavyDiscoveryReadOperationCount = discovery?.HeavyScanTriggered == true
                ? discovery.ReadOperationCount
                : 0,
            RootAcquisitionMs = discovery?.ElapsedMs ?? 0,
            RootAcquisitionBytesRead = discovery?.BytesRead ?? 0,
            RootAcquisitionReadOperationCount = discovery?.ReadOperationCount ?? 0,
            PersistentHintAttempted = discovery?.PersistentHintAttempted == true,
            PersistentHintAccepted = discovery?.PersistentHintAccepted == true,
            PersistentHintMissReason = discovery?.PersistentHintMissReason,
            CleanRoomTrace = discovery?.CleanRoomTrace,
            RejectedCandidateTraces = discovery?.RejectedCandidateTraces ?? []
        };
    }

    private static NativeLazerFastRefreshResult Unknown(
        NativeLazerGenerationIdentity generation,
        NativeLazerCacheMissReason missReason,
        string reason) => new()
    {
        ProcessIdentity = generation,
        CacheHit = false,
        CacheMissReason = missReason,
        RootValid = false,
        Outcome = NativeLazerFastOutcome.Unknown,
        FailureReason = reason,
        Metrics = new(0, 0, 0, 0, 0, 0, 0)
    };
}

internal static class NativeLazerRootFingerprintFactory
{
    internal static bool TryCreate(
        ulong rootAddress,
        NativeLazerFastLayout layout,
        INativeLazerFastMemory memory,
        out NativeLazerRootValidationFingerprint? fingerprint)
    {
        fingerprint = null;
        if (!TryReadObjectMethodTable(rootAddress, memory, out ulong rootMethodTable))
            return false;

        ulong? versionAddress = memory.ReadPointer(rootAddress + layout.VersionHash);
        NativeLazerRemoteText? version = versionAddress.HasValue
            ? memory.ReadString(versionAddress.Value, 256)
            : null;
        if (version is null || !IsPlausibleText(version.Value))
            return false;

        int coherentReferences = layout.RootReferenceOffsets
            .Select(offset => memory.ReadPointer(rootAddress + offset))
            .Count(pointer => TryReadObjectMethodTable(pointer, memory, out _));
        if (coherentReferences < 5)
            return false;

        ulong? bindableAddress = memory.ReadPointer(rootAddress + layout.Beatmap);
        if (!TryReadObjectMethodTable(bindableAddress, memory, out ulong bindableMethodTable))
            return false;

        byte? disabled = memory.ReadByte(bindableAddress!.Value + layout.BindableDisabled);
        if (disabled is not (0 or 1))
            return false;

        fingerprint = new(
            rootMethodTable,
            bindableMethodTable,
            version.MethodTable,
            MinimumCoherentRootReferences: 5);
        return true;
    }

    internal static bool TryReadObjectMethodTable(
        ulong? address,
        INativeLazerFastMemory memory,
        out ulong methodTable)
    {
        methodTable = 0;
        if (address is not >= 0x10000 || (address.Value & 7) != 0 || !memory.IsReadable(address.Value, 8))
            return false;

        ulong? candidate = memory.ReadPointer(address.Value);
        if (candidate is not >= 0x10000 || !memory.IsReadable(candidate.Value, 8))
            return false;

        methodTable = candidate.Value;
        return true;
    }

    internal static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool IsPlausibleText(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 256 &&
        value.All(character => character is >= ' ' or '\t');
}

internal sealed class NativeLazerFastRefresher
{
    internal NativeLazerFastRefreshResult Refresh(
        NativeLazerGenerationIdentity generation,
        NativeLazerRootCacheEntry entry,
        NativeLazerDiagnosticRootCache cache,
        INativeLazerFastMemory memory,
        Func<NativeLazerGenerationIdentity, bool> generationStillCurrent,
        Func<string, bool> blobExists)
    {
        long bytesAtStart = memory.BytesRead;
        int operationsAtStart = memory.ReadOperationCount;
        long totalStarted = Stopwatch.GetTimestamp();
        double rootMs = 0;
        double dereferenceMs = 0;
        double metadataMs = 0;
        double blobMs = 0;

        NativeLazerFastRefreshResult Finish(
            NativeLazerFastOutcome outcome,
            bool rootValid,
            string? reason = null,
            int? beatmapOnlineId = null,
            int? beatmapSetOnlineId = null,
            string? beatmapHash = null,
            bool? blob = null,
            bool cacheHit = true,
            NativeLazerCacheMissReason missReason = NativeLazerCacheMissReason.None)
        {
            return new()
            {
                ProcessIdentity = generation,
                CachedRoot = entry.RootAddress,
                CacheHit = cacheHit,
                CacheMissReason = missReason,
                RootValid = rootValid,
                BeatmapOnlineId = outcome == NativeLazerFastOutcome.Available ? beatmapOnlineId : null,
                BeatmapSetOnlineId = outcome == NativeLazerFastOutcome.Available ? beatmapSetOnlineId : null,
                BeatmapHash = outcome == NativeLazerFastOutcome.Available ? beatmapHash : null,
                BlobExists = outcome == NativeLazerFastOutcome.Available ? blob : null,
                Outcome = outcome,
                StaleAvailableDetected = false,
                FailureReason = reason,
                Metrics = new(
                    rootMs,
                    dereferenceMs,
                    metadataMs,
                    blobMs,
                    Stopwatch.GetElapsedTime(totalStarted).TotalMilliseconds,
                    memory.BytesRead - bytesAtStart,
                    memory.ReadOperationCount - operationsAtStart)
            };
        }

        if (entry.GenerationIdentity != generation || !generationStillCurrent(generation))
        {
            cache.Invalidate(NativeLazerCacheMissReason.ProcessExited);
            return Finish(
                NativeLazerFastOutcome.Unknown,
                rootValid: false,
                reason: "Process generation changed before refresh.",
                cacheHit: false,
                missReason: NativeLazerCacheMissReason.ProcessExited);
        }

        long phaseStarted = Stopwatch.GetTimestamp();
        if (!TryRevalidateRoot(entry, memory, out ulong bindableAddress))
        {
            rootMs = Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds;
            cache.Invalidate(NativeLazerCacheMissReason.RootValidationFailed);
            return Finish(
                NativeLazerFastOutcome.Unknown,
                rootValid: false,
                reason: "Cached root failed structural revalidation.",
                cacheHit: false,
                missReason: NativeLazerCacheMissReason.RootValidationFailed);
        }
        rootMs = Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds;

        phaseStarted = Stopwatch.GetTimestamp();
        byte? disabled = memory.ReadByte(bindableAddress + entry.Layout.BindableDisabled);
        ulong? workingAddress = memory.ReadPointer(bindableAddress + entry.Layout.BindableValue);
        if (disabled is not (0 or 1) || !workingAddress.HasValue)
        {
            dereferenceMs = Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds;
            cache.Invalidate(NativeLazerCacheMissReason.RootValidationFailed);
            return Finish(NativeLazerFastOutcome.Unknown, false, "Bindable state became unreadable.", cacheHit: false,
                missReason: NativeLazerCacheMissReason.RootValidationFailed);
        }

        if (workingAddress.Value == 0)
        {
            ulong? endingNoBeatmapWorking = memory.ReadPointer(bindableAddress + entry.Layout.BindableValue);
            dereferenceMs = Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds;
            if (endingNoBeatmapWorking == 0 && generationStillCurrent(generation))
                return Finish(NativeLazerFastOutcome.NoBeatmap, true);

            return Finish(NativeLazerFastOutcome.Unknown, true, "No-beatmap snapshot changed during verification.");
        }

        if (!NativeLazerRootFingerprintFactory.TryReadObjectMethodTable(workingAddress, memory, out _))
        {
            dereferenceMs = Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds;
            cache.Invalidate(NativeLazerCacheMissReason.RootValidationFailed);
            return Finish(NativeLazerFastOutcome.Unknown, false, "WorkingBeatmap was not a coherent object.", cacheHit: false,
                missReason: NativeLazerCacheMissReason.RootValidationFailed);
        }

        ulong? beatmapInfoAddress = memory.ReadPointer(workingAddress.Value + entry.Layout.WorkingBeatmapInfo);
        ulong? beatmapSetAddress = memory.ReadPointer(workingAddress.Value + entry.Layout.WorkingBeatmapSetInfo);
        if (!NativeLazerRootFingerprintFactory.TryReadObjectMethodTable(beatmapInfoAddress, memory, out _) ||
            !NativeLazerRootFingerprintFactory.TryReadObjectMethodTable(beatmapSetAddress, memory, out _) ||
            memory.ReadPointer(beatmapInfoAddress!.Value + entry.Layout.BeatmapInfoSet) != beatmapSetAddress)
        {
            dereferenceMs = Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds;
            cache.Invalidate(NativeLazerCacheMissReason.RootValidationFailed);
            return Finish(NativeLazerFastOutcome.Unknown, false, "BeatmapInfo/BeatmapSetInfo relationship was incoherent.",
                cacheHit: false, missReason: NativeLazerCacheMissReason.RootValidationFailed);
        }
        dereferenceMs = Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds;

        phaseStarted = Stopwatch.GetTimestamp();
        Guid? initialGuid = memory.ReadGuid(beatmapInfoAddress.Value + entry.Layout.BeatmapInfoId);
        int? beatmapOnlineId = memory.ReadInt32(beatmapInfoAddress.Value + entry.Layout.BeatmapInfoOnlineId);
        int? beatmapSetOnlineId = memory.ReadInt32(beatmapSetAddress!.Value + entry.Layout.BeatmapSetOnlineId);
        ulong? hashAddress = memory.ReadPointer(beatmapInfoAddress.Value + entry.Layout.BeatmapInfoHash);
        NativeLazerRemoteText? hash = hashAddress.HasValue
            ? memory.ReadString(hashAddress.Value, 128)
            : null;
        if (!initialGuid.HasValue || initialGuid.Value == Guid.Empty ||
            beatmapOnlineId is null or < -1 || beatmapSetOnlineId is null or < -1 ||
            hash is null || !NativeLazerRootFingerprintFactory.IsSha256(hash.Value) ||
            hash.MethodTable != entry.RootValidationFingerprint.VersionStringMethodTable)
        {
            metadataMs = Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds;
            cache.Invalidate(NativeLazerCacheMissReason.RootValidationFailed);
            return Finish(NativeLazerFastOutcome.Unknown, false, "Beatmap ID/hash domain was invalid.", cacheHit: false,
                missReason: NativeLazerCacheMissReason.RootValidationFailed);
        }
        metadataMs = Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds;

        phaseStarted = Stopwatch.GetTimestamp();
        bool blob;
        try
        {
            blob = blobExists(hash.Value);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            blobMs = Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds;
            return Finish(NativeLazerFastOutcome.Unknown, true, $"Blob validation failed: {exception.GetType().Name}.");
        }
        blobMs = Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds;
        if (!blob)
            return Finish(NativeLazerFastOutcome.Unknown, true, "Beatmap blob was not consistent with the hash domain.");

        ulong? endingWorking = memory.ReadPointer(bindableAddress + entry.Layout.BindableValue);
        ulong? endingInfo = memory.ReadPointer(workingAddress.Value + entry.Layout.WorkingBeatmapInfo);
        ulong? endingSet = memory.ReadPointer(workingAddress.Value + entry.Layout.WorkingBeatmapSetInfo);
        Guid? endingGuid = memory.ReadGuid(beatmapInfoAddress.Value + entry.Layout.BeatmapInfoId);
        if (!generationStillCurrent(generation))
        {
            cache.Invalidate(NativeLazerCacheMissReason.ProcessExited);
            return Finish(NativeLazerFastOutcome.Unknown, false, "Process generation changed during refresh.",
                cacheHit: false, missReason: NativeLazerCacheMissReason.ProcessExited);
        }
        if (endingWorking != workingAddress || endingInfo != beatmapInfoAddress || endingSet != beatmapSetAddress ||
            endingGuid != initialGuid)
        {
            return Finish(NativeLazerFastOutcome.Unknown, true, "Snapshot identity changed during refresh.");
        }

        return Finish(
            NativeLazerFastOutcome.Available,
            rootValid: true,
            beatmapOnlineId: beatmapOnlineId,
            beatmapSetOnlineId: beatmapSetOnlineId,
            beatmapHash: hash.Value.ToUpperInvariant(),
            blob: true);
    }

    private static bool TryRevalidateRoot(
        NativeLazerRootCacheEntry entry,
        INativeLazerFastMemory memory,
        out ulong bindableAddress)
    {
        bindableAddress = 0;
        NativeLazerRootValidationFingerprint fingerprint = entry.RootValidationFingerprint;
        NativeLazerFastLayout layout = entry.Layout;
        if (!NativeLazerRootFingerprintFactory.TryReadObjectMethodTable(entry.RootAddress, memory, out ulong rootMethodTable) ||
            rootMethodTable != fingerprint.RootMethodTable)
            return false;

        ulong? versionAddress = memory.ReadPointer(entry.RootAddress + layout.VersionHash);
        NativeLazerRemoteText? version = versionAddress.HasValue ? memory.ReadString(versionAddress.Value, 256) : null;
        if (version is null || version.MethodTable != fingerprint.VersionStringMethodTable)
            return false;

        int coherentReferences = layout.RootReferenceOffsets
            .Select(offset => memory.ReadPointer(entry.RootAddress + offset))
            .Count(pointer => NativeLazerRootFingerprintFactory.TryReadObjectMethodTable(pointer, memory, out _));
        if (coherentReferences < fingerprint.MinimumCoherentRootReferences)
            return false;

        ulong? candidateBindable = memory.ReadPointer(entry.RootAddress + layout.Beatmap);
        if (!NativeLazerRootFingerprintFactory.TryReadObjectMethodTable(candidateBindable, memory, out ulong bindableMethodTable) ||
            bindableMethodTable != fingerprint.BeatmapBindableMethodTable)
            return false;

        byte? disabled = memory.ReadByte(candidateBindable!.Value + layout.BindableDisabled);
        if (disabled is not (0 or 1))
            return false;

        bindableAddress = candidateBindable.Value;
        return true;
    }
}

internal sealed record NativeLazerFastRefreshStatistics(
    int AvailableCount,
    int UnknownCount,
    int NoBeatmapCount,
    double MinRefreshMs,
    double MedianRefreshMs,
    double P95RefreshMs,
    double MaxRefreshMs,
    double AverageBytesRead)
{
    internal static NativeLazerFastRefreshStatistics From(IReadOnlyList<NativeLazerFastRefreshResult> results)
    {
        if (results.Count == 0)
            return new(0, 0, 0, 0, 0, 0, 0, 0);

        double[] timings = results.Select(result => result.Metrics.TotalRefreshMs).Order().ToArray();
        return new(
            results.Count(result => result.Outcome == NativeLazerFastOutcome.Available),
            results.Count(result => result.Outcome == NativeLazerFastOutcome.Unknown),
            results.Count(result => result.Outcome == NativeLazerFastOutcome.NoBeatmap),
            timings[0],
            Percentile(timings, 0.50),
            Percentile(timings, 0.95),
            timings[^1],
            results.Average(result => (double)result.Metrics.BytesRead));
    }

    private static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 1)
            return sorted[0];
        double position = (sorted.Count - 1) * percentile;
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        if (lower == upper)
            return sorted[lower];
        double fraction = position - lower;
        return sorted[lower] + ((sorted[upper] - sorted[lower]) * fraction);
    }
}

internal sealed record NativeLazerTransitionMetrics(
    int PreviousBeatmapId,
    int TargetBeatmapId,
    double? FirstOldMapGoneMs,
    double? FirstNewMapAvailableMs,
    int TransientUnknownCount,
    int TransientNoBeatmapCount,
    bool StaleAvailableDetected);

internal static class NativeLazerTransitionAnalyzer
{
    internal static NativeLazerTransitionMetrics Analyze(
        int previousBeatmapId,
        int targetBeatmapId,
        IReadOnlyList<(double ElapsedMs, NativeLazerFastRefreshResult Result)> samples)
    {
        double? oldGone = samples
            .Where(sample => sample.Result.Outcome != NativeLazerFastOutcome.Available ||
                             sample.Result.BeatmapOnlineId != previousBeatmapId)
            .Select(sample => (double?)sample.ElapsedMs)
            .FirstOrDefault();

        double? newAvailable = samples
            .Where(sample => sample.Result.Outcome == NativeLazerFastOutcome.Available &&
                             sample.Result.BeatmapOnlineId == targetBeatmapId)
            .Select(sample => (double?)sample.ElapsedMs)
            .FirstOrDefault();

        bool oldWasGone = false;
        bool stale = false;
        foreach ((_, NativeLazerFastRefreshResult result) in samples)
        {
            if (result.Outcome != NativeLazerFastOutcome.Available || result.BeatmapOnlineId != previousBeatmapId)
                oldWasGone = true;
            else if (oldWasGone)
                stale = true;
        }

        return new(
            previousBeatmapId,
            targetBeatmapId,
            oldGone,
            newAvailable,
            samples.Count(sample => sample.Result.Outcome == NativeLazerFastOutcome.Unknown),
            samples.Count(sample => sample.Result.Outcome == NativeLazerFastOutcome.NoBeatmap),
            stale || samples.Any(sample => sample.Result.StaleAvailableDetected));
    }
}
