using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;

namespace BeatInsight.Diagnostics.NativeLazer;

internal sealed class NativeLazerCountingMemory(NativeLazerProcessMemory memory)
    : INativeLazerFastMemory, INativeLazerGameplayClockMemory
{
    public long BytesRead { get; private set; }
    public int ReadOperationCount { get; private set; }

    public bool IsReadable(ulong address, int count) => ReadBytes(address, count) is not null;
    public byte? ReadByte(ulong address) => ReadBytes(address, 1) is { } data ? data[0] : null;
    public int? ReadInt32(ulong address) => ReadBytes(address, 4) is { } data ? BinaryPrimitives.ReadInt32LittleEndian(data) : null;
    public long? ReadInt64(ulong address) => ReadBytes(address, 8) is { } data ? BinaryPrimitives.ReadInt64LittleEndian(data) : null;
    public double? ReadDouble(ulong address) => ReadBytes(address, 8) is { } data
        ? BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(data))
        : null;
    public ulong? ReadPointer(ulong address) => ReadBytes(address, 8) is { } data ? BinaryPrimitives.ReadUInt64LittleEndian(data) : null;
    public Guid? ReadGuid(ulong address) => ReadBytes(address, 16) is { } data ? new Guid(data) : null;

    public NativeLazerRemoteText? ReadString(ulong address, int maximumCharacters)
    {
        ulong? methodTable = ReadPointer(address);
        int? length = ReadInt32(address + 8);
        if (methodTable is not >= 0x10000 || length is null || length < 0 || length > maximumCharacters)
            return null;
        byte[]? characters = length == 0 ? [] : ReadBytes(address + 12, checked(length.Value * 2));
        return characters is null ? null : new(Encoding.Unicode.GetString(characters), methodTable.Value);
    }

    internal byte[]? ReadBytes(ulong address, int count)
    {
        ReadOperationCount++;
        NativeLazerMemoryRead read = memory.Read(address, count);
        BytesRead += read.Data.Length;
        return read.Complete ? read.Data : null;
    }
}

internal sealed class NativeLazerFastLiveSession
{
    private const long maximumScanBytes = 4L * 1024 * 1024 * 1024;
    private readonly NativeLazerFastRefreshCoordinator coordinator = new();
    private readonly NativeLazerCurrentTimeReader currentTimeReader = new();
    private readonly NativeLazerGameplayStatsReader gameplayStatsReader = new();
    private readonly NativeLazerGameRouteReader gameRouteReader = new();
    private readonly NativeLazerGameRouteMethodTableState gameRouteMethodTables = new();
    private readonly INativeLazerGameRouteMethodTableResolver? gameRouteResolver;
    private readonly INativeLazerGameplayStatsMethodTableResolver? gameplayStatsResolver;
    private readonly bool emitAudit;
    private NativeLazerProcessCandidate? validatedProcess;
    private NativeLazerGenerationIdentity gameplayLayoutGeneration;
    private NativeLazerGameplayClockLayout? gameplayLayout;
    private NativeLazerGameplayStatsLayout? gameplayStatsLayout;
    private NativeLazerGameplayStatsMethodTables? gameplayStatsMethodTables;
    private string? gameplayPositionFailureReason;

    internal NativeLazerFastLiveSession(
        INativeLazerGameRouteMethodTableResolver? gameRouteResolver = null,
        INativeLazerGameplayStatsMethodTableResolver? gameplayStatsResolver = null,
        bool emitAudit = true)
    {
        this.gameRouteResolver = gameRouteResolver;
        this.gameplayStatsResolver = gameplayStatsResolver;
        this.emitAudit = emitAudit;
    }

    internal NativeLazerFastRefreshResult Refresh()
    {
        long processDiscoveryStarted = Stopwatch.GetTimestamp();
        NativeLazerDiscovery discovery = NativeLazerProcessDiscovery.Discover();
        if (emitAudit)
        {
            Console.WriteLine(
                $"DiscoveryAudit.Phase=ProcessDiscovery; " +
                $"ElapsedMs={Stopwatch.GetElapsedTime(processDiscoveryStarted).TotalMilliseconds:F3}; " +
                $"Result={discovery.Status}");
        }
        NativeLazerProcessCandidate? process = discovery.UniqueCandidate;
        if (process is null)
        {
            ClearGameplayPositionState();
            NativeLazerGenerationIdentity identity = coordinator.Cache.Current?.GenerationIdentity ?? default;
            coordinator.Cache.Lookup(identity, processExists: false);
            return Unknown(identity, NativeLazerCacheMissReason.ProcessExited,
                $"Process discovery was {discovery.Status}.");
        }

        var generation = new NativeLazerGenerationIdentity(process.ProcessId, process.StartTimeUtc);
        (NativeLazerProcessMemory? attached, NativeLazerAttachStatus status) = NativeLazerProcessMemory.TryAttach(process);
        if (attached is null)
        {
            coordinator.Cache.Lookup(generation, processExists: false);
            return Unknown(generation, NativeLazerCacheMissReason.ProcessExited, $"Read-only attach failed: {status}.");
        }

        using (attached)
        {
            var memory = new NativeLazerCountingMemory(attached);
            long acquisitionStarted = Stopwatch.GetTimestamp();
            NativeLazerFastRefreshResult result = coordinator.Refresh(
                generation,
                processExists: true,
                memory,
                (_, countedMemory) => DiscoverRoot(process, attached, (NativeLazerCountingMemory)countedMemory, emitAudit),
                IsCurrentGeneration,
                BlobExists);
            if (emitAudit)
            {
                Console.WriteLine(
                    $"DiscoveryAudit.Phase=RootAcquisitionAndRefresh; " +
                    $"ElapsedMs={Stopwatch.GetElapsedTime(acquisitionStarted).TotalMilliseconds:F3}; " +
                    $"BytesRead={memory.BytesRead}; ReadOperationCount={memory.ReadOperationCount}; " +
                    $"Result={result.Outcome}");
            }
            NativeLazerRootCacheEntry? cached = coordinator.Cache.Current;
            if (cached is not null && result.RootValid && cached.GenerationIdentity == generation)
                EstablishGameplayPositionState(process, generation);
            else if (cached is null || cached.GenerationIdentity != generation)
                ClearGameplayPositionState();
            if (!emitAudit || cached is null || result.CachedRoot != cached.RootAddress)
                return result;

            NativeLazerDiagnosticTrace fastTrace = NativeLazerKnownRootTracer.Capture(
                "FastPath",
                cached,
                memory);
            return result with
            {
                FastPathTrace = fastTrace,
                TraceComparison = result.CleanRoomTrace is null
                    ? []
                    : NativeLazerKnownRootTracer.Compare(result.CleanRoomTrace, fastTrace)
            };
        }
    }

    internal NativeLazerGameplayPositionResponse ReadGameplayPosition()
    {
        NativeLazerRootCacheEntry? root = coordinator.Cache.Current;
        NativeLazerProcessCandidate? process = validatedProcess;
        NativeLazerGameplayClockLayout? layout = gameplayLayout;
        NativeLazerGenerationIdentity generation = root?.GenerationIdentity ?? default;
        if (root is null || process is null || layout is null ||
            gameplayLayoutGeneration != generation)
        {
            ClearGameplayPositionState();
            return NativeLazerGameplayPositionResponse.Unavailable(
                generation,
                "No validated NativeLazer generation/root/layout is available. Normal refresh must establish it first.");
        }

        (NativeLazerProcessMemory? attached, NativeLazerAttachStatus status) =
            NativeLazerProcessMemory.TryAttach(process);
        if (attached is null)
        {
            ClearGameplayPositionState();
            return NativeLazerGameplayPositionResponse.Unavailable(
                generation,
                $"Read-only attach failed: {status}.");
        }

        using (attached)
        {
            var memory = new NativeLazerCountingMemory(attached);
            NativeLazerGameplayClockResult result = currentTimeReader.TryReadCurrentTime(
                generation,
                root.GenerationIdentity,
                root.RootAddress,
                layout,
                memory,
                attached.IsCurrentGeneration);
            if (!attached.IsCurrentGeneration(generation))
                ClearGameplayPositionState();
            return NativeLazerGameplayPositionResponse.From(result);
        }
    }

    internal NativeLazerGameRouteResponse ReadGameRoute()
    {
        NativeLazerRootCacheEntry? root = coordinator.Cache.Current;
        NativeLazerProcessCandidate? process = validatedProcess;
        NativeLazerGameplayClockLayout? layout = gameplayLayout;
        NativeLazerGameRouteResolution? resolution = gameRouteMethodTables.Current;
        NativeLazerGenerationIdentity generation = root?.GenerationIdentity ?? default;
        if (root is null || process is null || layout is null ||
            gameplayLayoutGeneration != generation ||
            resolution is null ||
            resolution.Generation != generation)
        {
            return NativeLazerGameRouteResponse.Unavailable(
                generation,
                resolution?.FailureReason ??
                    gameplayPositionFailureReason ??
                    "No validated NativeLazer generation/root/layout/route table is available.",
                resolution);
        }

        (NativeLazerProcessMemory? attached, NativeLazerAttachStatus status) =
            NativeLazerProcessMemory.TryAttach(process);
        if (attached is null)
        {
            ClearGameplayPositionState();
            return NativeLazerGameRouteResponse.Unavailable(
                generation,
                $"Read-only attach failed: {status}.",
                resolution);
        }

        using (attached)
        {
            var memory = new NativeLazerCountingMemory(attached);
            NativeLazerGameRouteResult result = gameRouteReader.Read(
                generation,
                root.GenerationIdentity,
                root.RootAddress,
                layout,
                resolution.MethodTables,
                memory,
                attached.IsCurrentGeneration);
            if (result.Route == NativeLazerGameRoute.Unknown &&
                result.TopMethodTable is ulong unknownTop &&
                gameRouteResolver is not null &&
                gameRouteMethodTables.TryEnrich(
                    generation,
                    unknownTop,
                    () => gameRouteResolver.Resolve(process, generation)))
            {
                resolution = gameRouteMethodTables.Current;
                if (resolution is not null)
                {
                    result = gameRouteReader.Read(
                        generation,
                        root.GenerationIdentity,
                        root.RootAddress,
                        layout,
                        resolution.MethodTables,
                        memory,
                        attached.IsCurrentGeneration);
                }
            }
            else if (result.Route != NativeLazerGameRoute.Unknown)
            {
                gameRouteMethodTables.ObserveKnownTop(result.TopMethodTable);
            }
            if (!attached.IsCurrentGeneration(generation))
                ClearGameplayPositionState();
            return NativeLazerGameRouteResponse.From(result, resolution);
        }
    }

    internal NativeLazerGameplayStatsResponse ReadGameplayStats()
    {
        NativeLazerRootCacheEntry? root = coordinator.Cache.Current;
        NativeLazerProcessCandidate? process = validatedProcess;
        NativeLazerGameplayClockLayout? clockLayout = gameplayLayout;
        NativeLazerGameplayStatsLayout? statsLayout = gameplayStatsLayout;
        NativeLazerGameplayStatsMethodTables? tables = gameplayStatsMethodTables;
        NativeLazerGenerationIdentity generation = root?.GenerationIdentity ?? default;
        if (root is null || process is null || clockLayout is null || statsLayout is null ||
            tables is null || gameplayLayoutGeneration != generation || tables.Generation != generation)
        {
            return NativeLazerGameplayStatsResponse.Unavailable(
                generation,
                gameplayPositionFailureReason ??
                    "No validated NativeLazer generation/root/stats layout is available.");
        }

        (NativeLazerProcessMemory? attached, NativeLazerAttachStatus status) =
            NativeLazerProcessMemory.TryAttach(process);
        if (attached is null)
        {
            ClearGameplayPositionState();
            return NativeLazerGameplayStatsResponse.Unavailable(
                generation, $"Read-only attach failed: {status}.");
        }

        using (attached)
        {
            var memory = new NativeLazerCountingMemory(attached);
            NativeLazerGameplayClockResult clock = currentTimeReader.TryReadCurrentTime(
                generation, root.GenerationIdentity, root.RootAddress, clockLayout,
                memory, attached.IsCurrentGeneration);
            if (clock.Outcome != NativeLazerGameplayClockOutcome.Unique ||
                clock.PlayerAddress is not { } player ||
                clock.CurrentTimeMilliseconds is not { } currentTime)
            {
                return NativeLazerGameplayStatsResponse.Unavailable(
                    generation, clock.FailureReason ?? "Gameplay clock/player is unavailable.");
            }

            NativeLazerGameplayStatsResult result = gameplayStatsReader.Read(
                generation, root.GenerationIdentity, player, currentTime,
                statsLayout, tables, memory, attached.IsCurrentGeneration);
            if (!attached.IsCurrentGeneration(generation))
                ClearGameplayPositionState();
            return NativeLazerGameplayStatsResponse.From(result);
        }
    }

    private void EstablishGameplayPositionState(
        NativeLazerProcessCandidate process,
        NativeLazerGenerationIdentity generation)
    {
        if (gameplayLayout is not null && gameplayLayoutGeneration == generation)
        {
            validatedProcess = process;
            return;
        }

        try
        {
            string? installDirectory = Path.GetDirectoryName(process.ExecutablePath);
            gameplayLayout = string.IsNullOrWhiteSpace(installDirectory)
                ? null
                : NativeLazerGameplayClockAssemblyInspector.Inspect(installDirectory).Layout;
            if (gameplayLayout is null)
            {
                ClearGameplayPositionState();
                gameplayPositionFailureReason = "Gameplay clock layout inspection returned no layout.";
                return;
            }

            gameplayStatsLayout = NativeLazerGameplayStatsAssemblyInspector.Inspect(installDirectory!);
            gameplayStatsMethodTables = gameplayStatsResolver?.Resolve(
                process, generation, gameplayStatsLayout);
            if (gameplayStatsMethodTables is null)
            {
                ClearGameplayPositionState();
                gameplayPositionFailureReason = "Gameplay stats MethodTable resolution returned no result.";
                return;
            }

            gameplayLayoutGeneration = generation;
            validatedProcess = process;
            gameplayPositionFailureReason = null;
            gameRouteMethodTables.Initialize(gameRouteResolver?.Resolve(process, generation) ??
                new(
                    generation,
                    null,
                    0,
                    "No MethodTable resolver is configured.")
                {
                    InvocationCount = gameRouteResolver is null ? 0 : 1
                });
        }
        catch (Exception exception) when (exception is IOException or BadImageFormatException or
                                           FileLoadException or TypeLoadException or MissingMemberException or
                                           InvalidOperationException or MemberAccessException)
        {
            ClearGameplayPositionState();
            gameplayPositionFailureReason =
                $"Gameplay route/stats initialization failed: {exception.GetType().Name}: {exception.Message}";
        }
    }

    private void ClearGameplayPositionState()
    {
        validatedProcess = null;
        gameplayLayoutGeneration = default;
        gameplayLayout = null;
        gameplayStatsLayout = null;
        gameplayStatsMethodTables = null;
        gameplayPositionFailureReason = null;
        gameRouteMethodTables.Clear();
    }

    private static NativeLazerHeavyDiscoveryResult? DiscoverRoot(
        NativeLazerProcessCandidate process,
        NativeLazerProcessMemory attached,
        NativeLazerCountingMemory memory,
        bool emitAudit)
    {
        long started = Stopwatch.GetTimestamp();
        long bytesAtStart = memory.BytesRead;
        int operationsAtStart = memory.ReadOperationCount;
        string? installDirectory = Path.GetDirectoryName(process.ExecutablePath);
        if (string.IsNullOrWhiteSpace(installDirectory))
            return null;

        NativeLazerFastLayout layout;
        string[] anchors;
        long phaseStarted = Stopwatch.GetTimestamp();
        long phaseBytes = memory.BytesRead;
        int phaseOperations = memory.ReadOperationCount;
        try
        {
            (layout, anchors) = NativeLazerFastLayoutDiscovery.Derive(installDirectory, process.ProductVersion);
        }
        catch (Exception exception) when (exception is IOException or BadImageFormatException or FileNotFoundException or
                                          FileLoadException or TypeLoadException or MissingFieldException or InvalidOperationException)
        {
            return null;
        }
        WritePhase("LayoutDerivation", phaseStarted, phaseBytes, phaseOperations,
            $"AnchorCount={anchors.Length}");

        var hintStore = new NativeLazerPersistentRootHintStore();
        NativeLazerPersistentHintContext? hintContext = null;
        NativeLazerPersistentRootHint? hint = null;
        NativeLazerPersistentHintMissReason hintMissReason;
        phaseStarted = Stopwatch.GetTimestamp();
        phaseBytes = memory.BytesRead;
        phaseOperations = memory.ReadOperationCount;
        try
        {
            hintContext = NativeLazerPersistentHintIdentity.Create(
                process.ProductVersion ?? string.Empty,
                process.ExecutablePath,
                installDirectory,
                layout);
            hint = hintStore.Load(out hintMissReason);
        }
        catch (Exception exception) when (exception is IOException or BadImageFormatException or InvalidOperationException)
        {
            hintMissReason = NativeLazerPersistentHintMissReason.IoFailure;
        }

        var generation = new NativeLazerGenerationIdentity(
            process.ProcessId,
            process.StartTimeUtc);
        NativeLazerRootCacheEntry? hintedEntry = null;
        NativeLazerDiagnosticTrace? hintTrace = null;
        NativeLazerPersistentHintMissReason resolvedMissReason =
            NativeLazerPersistentHintMissReason.Absent;
        bool hintAccepted = hintContext is not null &&
            NativeLazerPersistentHintResolver.TryResolve(
                hint,
                hintContext,
                generation,
                layout,
                memory,
                out hintedEntry,
                out hintTrace,
                out resolvedMissReason);
        if (hintContext is not null)
        {
            hintMissReason = hintAccepted
                ? NativeLazerPersistentHintMissReason.None
                : hint is null && hintMissReason != NativeLazerPersistentHintMissReason.None
                    ? hintMissReason
                    : resolvedMissReason;
        }
        WritePhase("PersistentHintValidation", phaseStarted, phaseBytes, phaseOperations,
            $"HintPresent={hint is not null}; HintAccepted={hintAccepted}; MissReason={hintMissReason}");
        if (hintAccepted && hintedEntry is not null && hintTrace is not null)
        {
            return new(
                hintedEntry,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                memory.BytesRead - bytesAtStart,
                hintTrace,
                [],
                memory.ReadOperationCount - operationsAtStart,
                HeavyScanTriggered: false,
                PersistentHintAttempted: true,
                PersistentHintAccepted: true,
                PersistentHintMissReason: NativeLazerPersistentHintMissReason.None.ToString());
        }

        if (hint is not null)
            hintStore.Clear();

        phaseStarted = Stopwatch.GetTimestamp();
        phaseBytes = memory.BytesRead;
        phaseOperations = memory.ReadOperationCount;
        NativeLazerMemoryRegion[] regions = attached.EnumerateReadableRegions()
            .Where(region => region.IsPrivate && (region.Protection & 0xff) == 0x04)
            .OrderBy(region => region.BaseAddress)
            .ToArray();
        WritePhase("RegionEnumeration", phaseStarted, phaseBytes, phaseOperations,
            $"RegionsInspected={regions.Length}");
        if (regions.Length == 0 || anchors.Length == 0)
            return null;

        var scanner = new NativeLazerFastRootScanner(memory, regions, maximumScanBytes);
        phaseStarted = Stopwatch.GetTimestamp();
        phaseBytes = memory.BytesRead;
        phaseOperations = memory.ReadOperationCount;
        IReadOnlyList<ulong> roots = scanner.FindRoots(layout, anchors);
        WritePhase("AnchorDiscovery", phaseStarted, phaseBytes, phaseOperations,
            $"CandidateCount={roots.Count}; ScannedBytes={scanner.ScannedBytes}");
        var candidates = new List<(NativeLazerRootCacheEntry Entry, NativeLazerDiagnosticTrace Trace)>();
        var rejected = new List<NativeLazerDiagnosticTrace>();
        var evaluated = new HashSet<ulong>();
        phaseStarted = Stopwatch.GetTimestamp();
        phaseBytes = memory.BytesRead;
        phaseOperations = memory.ReadOperationCount;
        Evaluate(roots, "HeavyCleanRoomAnchorCandidate");
        WritePhase("AnchorCandidateValidation", phaseStarted, phaseBytes, phaseOperations,
            $"CandidateCount={roots.Count}; ValidatedCandidates={candidates.Count}; RejectedCandidates={rejected.Count}");
        if (emitAudit)
            WriteDiscoveryAudit("Anchor", roots.Count, candidates, rejected, scanner.Truncated, scanner.TruncationReason, scanner.ScannedBytes);
        if (!scanner.Truncated && candidates.Count == 0)
        {
            phaseStarted = Stopwatch.GetTimestamp();
            phaseBytes = memory.BytesRead;
            phaseOperations = memory.ReadOperationCount;
            IReadOnlyList<ulong> structuralRoots = scanner.FindStructuralRoots(layout);
            WritePhase("StructuralDiscovery", phaseStarted, phaseBytes, phaseOperations,
                $"CandidateCount={structuralRoots.Count}; ScannedBytes={scanner.ScannedBytes}");
            phaseStarted = Stopwatch.GetTimestamp();
            phaseBytes = memory.BytesRead;
            phaseOperations = memory.ReadOperationCount;
            Evaluate(structuralRoots, "HeavyCleanRoomStructuralCandidate");
            WritePhase("StructuralCandidateValidation", phaseStarted, phaseBytes, phaseOperations,
                $"CandidateCount={structuralRoots.Count}; ValidatedCandidates={candidates.Count}; RejectedCandidates={rejected.Count}");
            if (emitAudit)
                WriteDiscoveryAudit("Structural", structuralRoots.Count, candidates, rejected, scanner.Truncated, scanner.TruncationReason, scanner.ScannedBytes);
        }

        if (scanner.Truncated || candidates.Count != 1)
            return null;

        (NativeLazerRootCacheEntry entry, NativeLazerDiagnosticTrace trace) = candidates.Single();
        if (hintContext is not null)
        {
            hintStore.TrySave(NativeLazerPersistentRootHint.Create(
                hintContext,
                entry.RootAddress));
        }
        return new(
            entry,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            memory.BytesRead - bytesAtStart,
            trace with { Path = "HeavyCleanRoomValidatedPath" },
            rejected,
            memory.ReadOperationCount - operationsAtStart,
            HeavyScanTriggered: true,
            PersistentHintAttempted: hint is not null,
            PersistentHintAccepted: false,
            PersistentHintMissReason: hintMissReason.ToString());

        void WritePhase(
            string phase,
            long phaseStart,
            long bytesBefore,
            int operationsBefore,
            string details)
        {
            if (!emitAudit)
                return;
            Console.WriteLine(
                $"DiscoveryAudit.Phase={phase}; " +
                $"ElapsedMs={Stopwatch.GetElapsedTime(phaseStart).TotalMilliseconds:F3}; " +
                $"BytesRead={memory.BytesRead - bytesBefore}; " +
                $"ReadOperationCount={memory.ReadOperationCount - operationsBefore}; {details}");
        }

        void Evaluate(IEnumerable<ulong> candidateRoots, string path)
        {
            foreach (ulong root in candidateRoots.Order())
            {
                if (!evaluated.Add(root) ||
                    !NativeLazerRootFingerprintFactory.TryCreate(root, layout, memory, out NativeLazerRootValidationFingerprint? fingerprint))
                    continue;
                var entry = new NativeLazerRootCacheEntry(generation, root, fingerprint!, layout);
                NativeLazerDiagnosticTrace trace = NativeLazerKnownRootTracer.Capture(path, entry, memory);
                if (trace.ValidationResult)
                    candidates.Add((entry, trace));
                else
                    rejected.Add(trace);
            }
        }

        static void WriteDiscoveryAudit(
            string phase,
            int rawCount,
            IReadOnlyList<(NativeLazerRootCacheEntry Entry, NativeLazerDiagnosticTrace Trace)> valid,
            IReadOnlyList<NativeLazerDiagnosticTrace> rejectedTraces,
            bool truncated,
            string? truncationReason,
            long scannedBytes)
        {
            Console.WriteLine($"DiscoveryAudit.Phase={phase}; RawCandidates={rawCount}; " +
                              $"ValidatedCandidates={valid.Count}; RejectedCandidates={rejectedTraces.Count}; Truncated={truncated}; " +
                              $"TruncationReason={truncationReason ?? "none"}; ScannedBytes={scannedBytes}");
            foreach (NativeLazerDiagnosticTrace trace in rejectedTraces)
            {
                Console.WriteLine($"DiscoveryAudit.RejectedPath={trace.Path}; Root={trace.Stages[0].TargetAddressOrValue}; " +
                                  $"FailureReason={trace.FailureReason ?? "none"}");
                foreach (NativeLazerDiagnosticStage stage in trace.Stages)
                {
                    Console.WriteLine($"DiscoveryAudit.Stage={stage.Number}:{stage.Name}; " +
                                      $"SourceAddress={(stage.SourceAddress.HasValue ? $"0x{stage.SourceAddress.Value:X}" : "unavailable")}; " +
                                      $"OffsetUsed={(stage.OffsetUsed.HasValue ? $"0x{stage.OffsetUsed.Value:X}" : "n/a")}; " +
                                      $"TargetAddressOrValue={stage.TargetAddressOrValue}; Readable={stage.Readable}; Null={stage.IsNull}; " +
                                      $"ValidationResult={stage.ValidationResult}; FailureReason={stage.FailureReason ?? "none"}");
                }
            }
            foreach ((NativeLazerRootCacheEntry entry, NativeLazerDiagnosticTrace trace) in valid)
                Console.WriteLine($"DiscoveryAudit.ValidatedRoot=0x{entry.RootAddress:X}; Path={trace.Path}");
        }
    }

    private static bool IsCurrentGeneration(NativeLazerGenerationIdentity expected)
    {
        NativeLazerDiscovery discovery = NativeLazerProcessDiscovery.Discover();
        NativeLazerProcessCandidate? candidate = discovery.UniqueCandidate;
        return candidate is not null &&
               candidate.ProcessId == expected.ProcessId &&
               candidate.StartTimeUtc == expected.ProcessStartIdentity;
    }

    private static bool BlobExists(string hash)
        => NativeLazerBlobPathResolver.Resolve(hash).Exists;

    private static NativeLazerFastRefreshResult Unknown(
        NativeLazerGenerationIdentity identity,
        NativeLazerCacheMissReason missReason,
        string failure) => new()
    {
        ProcessIdentity = identity,
        CacheHit = false,
        CacheMissReason = missReason,
        RootValid = false,
        Outcome = NativeLazerFastOutcome.Unknown,
        FailureReason = failure,
        Metrics = new(0, 0, 0, 0, 0, 0, 0)
    };
}

internal static class NativeLazerKnownRootTracer
{
    internal static NativeLazerDiagnosticTrace Capture(
        string path,
        NativeLazerRootCacheEntry entry,
        INativeLazerFastMemory memory,
        Func<string?, (string? Path, bool Exists)>? blobResolver = null)
    {
        var stages = new List<NativeLazerDiagnosticStage>(13);
        NativeLazerFastLayout layout = entry.Layout;
        ulong root = entry.RootAddress;

        bool rootReadable = memory.IsReadable(root, 8);
        bool rootObject = NativeLazerRootFingerprintFactory.TryReadObjectMethodTable(root, memory, out _);
        Add(1, "RootAddress", null, null, Hex(root), rootReadable, root == 0, rootObject,
            rootObject ? null : "Root address is not a readable object.");

        bool fingerprintCreated = NativeLazerRootFingerprintFactory.TryCreate(
            root, layout, memory, out NativeLazerRootValidationFingerprint? actualFingerprint);
        bool fingerprintMatches = fingerprintCreated && actualFingerprint == entry.RootValidationFingerprint;
        Add(2, "RootStructuralValidation", root, null,
            actualFingerprint is null ? "unavailable" : Fingerprint(actualFingerprint),
            rootReadable, actualFingerprint is null, fingerprintMatches,
            fingerprintMatches ? null : "Structural fingerprint is unavailable or differs from the cached fingerprint.");

        ulong beatmapField = root + layout.Beatmap;
        bool beatmapFieldReadable = memory.IsReadable(beatmapField, 8);
        Add(3, "BeatmapBackingFieldAddress", root, layout.Beatmap, Hex(beatmapField),
            beatmapFieldReadable, false, beatmapFieldReadable,
            beatmapFieldReadable ? null : "Beatmap backing field is unreadable.");

        ulong? bindable = beatmapFieldReadable ? memory.ReadPointer(beatmapField) : null;
        bool bindableObject = NativeLazerRootFingerprintFactory.TryReadObjectMethodTable(bindable, memory, out ulong bindableMethodTable);
        bool bindableValid = bindableObject && bindableMethodTable == entry.RootValidationFingerprint.BeatmapBindableMethodTable;
        Add(4, "BindableAddress", beatmapField, 0, Format(bindable), beatmapFieldReadable,
            bindable == 0, bindableValid,
            bindableValid ? null : "Bindable pointer is null, unreadable, or has the wrong method table.");

        ulong? currentValueField = bindable.HasValue ? bindable.Value + layout.BindableValue : null;
        bool currentValueFieldReadable = currentValueField.HasValue && memory.IsReadable(currentValueField.Value, 8);
        Add(5, "BindableCurrentValueFieldAddress", bindable, layout.BindableValue, Format(currentValueField),
            currentValueFieldReadable, !currentValueField.HasValue, currentValueFieldReadable,
            currentValueFieldReadable ? null : "Bindable current-value field is unavailable or unreadable.");

        ulong? working = currentValueFieldReadable ? memory.ReadPointer(currentValueField!.Value) : null;
        bool workingObject = NativeLazerRootFingerprintFactory.TryReadObjectMethodTable(working, memory, out _);
        Add(6, "WorkingBeatmapAddress", currentValueField, 0, Format(working), currentValueFieldReadable,
            working == 0, workingObject,
            workingObject ? null : working == 0 ? "WorkingBeatmap is null." : "WorkingBeatmap pointer is not a readable object.");
        Add(7, "WorkingBeatmapStructuralValidation", working, null,
            workingObject ? "ReadableObject" : "InvalidObject", working.HasValue && memory.IsReadable(working.Value, 8),
            working == 0, workingObject, workingObject ? null : "WorkingBeatmap structural validation failed.");

        ulong? infoField = working.HasValue && working.Value != 0 ? working.Value + layout.WorkingBeatmapInfo : null;
        ulong? info = infoField.HasValue && memory.IsReadable(infoField.Value, 8) ? memory.ReadPointer(infoField.Value) : null;
        bool infoObject = NativeLazerRootFingerprintFactory.TryReadObjectMethodTable(info, memory, out _);
        Add(8, "BeatmapInfoAddress", working, layout.WorkingBeatmapInfo, Format(info),
            infoField.HasValue && memory.IsReadable(infoField.Value, 8), info == 0, infoObject,
            infoObject ? null : "BeatmapInfo pointer is null, unavailable, or not a readable object.");

        ulong? setField = working.HasValue && working.Value != 0 ? working.Value + layout.WorkingBeatmapSetInfo : null;
        ulong? set = setField.HasValue && memory.IsReadable(setField.Value, 8) ? memory.ReadPointer(setField.Value) : null;
        bool setObject = NativeLazerRootFingerprintFactory.TryReadObjectMethodTable(set, memory, out _);
        ulong? infoSet = infoObject ? memory.ReadPointer(info!.Value + layout.BeatmapInfoSet) : null;
        bool setRelation = setObject && infoSet == set;
        Add(9, "BeatmapSetInfoAddress", working, layout.WorkingBeatmapSetInfo, Format(set),
            setField.HasValue && memory.IsReadable(setField.Value, 8), set == 0, setRelation,
            setRelation ? null : "BeatmapSetInfo is invalid or differs from BeatmapInfo.BeatmapSet.");

        ulong? onlineField = infoObject ? info!.Value + layout.BeatmapInfoOnlineId : null;
        int? onlineId = onlineField.HasValue ? memory.ReadInt32(onlineField.Value) : null;
        bool onlineValid = onlineId is >= -1;
        Add(10, "OnlineID", info, layout.BeatmapInfoOnlineId, onlineId?.ToString() ?? "unavailable",
            onlineField.HasValue && memory.IsReadable(onlineField.Value, 4), !onlineId.HasValue, onlineValid,
            onlineValid ? null : "Beatmap OnlineID is unavailable or outside the valid domain.");

        ulong? setOnlineField = setObject ? set!.Value + layout.BeatmapSetOnlineId : null;
        int? setOnlineId = setOnlineField.HasValue ? memory.ReadInt32(setOnlineField.Value) : null;
        bool setOnlineValid = setOnlineId is >= -1;
        Add(11, "SetOnlineID", set, layout.BeatmapSetOnlineId, setOnlineId?.ToString() ?? "unavailable",
            setOnlineField.HasValue && memory.IsReadable(setOnlineField.Value, 4), !setOnlineId.HasValue, setOnlineValid,
            setOnlineValid ? null : "BeatmapSet OnlineID is unavailable or outside the valid domain.");

        ulong? hashField = infoObject ? info!.Value + layout.BeatmapInfoHash : null;
        ulong? hashAddress = hashField.HasValue ? memory.ReadPointer(hashField.Value) : null;
        NativeLazerRemoteText? hash = hashAddress.HasValue ? memory.ReadString(hashAddress.Value, 128) : null;
        bool hashValid = hash is not null && NativeLazerRootFingerprintFactory.IsSha256(hash.Value);
        Add(12, "Hash", info, layout.BeatmapInfoHash,
            hash is null ? $"address={Format(hashAddress)}; value=unavailable" : $"address={Format(hashAddress)}; value={hash.Value}",
            hashField.HasValue && memory.IsReadable(hashField.Value, 8), hashAddress == 0, hashValid,
            hashValid ? null : "Hash pointer/string is unavailable or outside the SHA-256 domain.");

        (string? blobPath, bool blobExists) =
            (blobResolver ?? NativeLazerBlobPathResolver.Resolve)(hash?.Value);
        Add(13, "BlobPath", hashAddress, 0, blobPath ?? "unavailable", blobPath is not null,
            blobPath is null, blobExists, blobExists ? null : "Blob path is unavailable or the blob does not exist.");

        string? failure = stages.FirstOrDefault(stage => !stage.ValidationResult)?.FailureReason;
        return new(path, stages.All(stage => stage.ValidationResult), failure, stages);

        void Add(int number, string name, ulong? source, ulong? offset, string target, bool readable,
            bool isNull, bool valid, string? reason) =>
            stages.Add(new(number, name, source, offset, target, readable, isNull, valid, reason));
    }

    internal static IReadOnlyList<NativeLazerDiagnosticStageComparison> Compare(
        NativeLazerDiagnosticTrace cleanRoom,
        NativeLazerDiagnosticTrace fastPath)
    {
        var comparisons = new List<NativeLazerDiagnosticStageComparison>(13);
        foreach (NativeLazerDiagnosticStage clean in cleanRoom.Stages)
        {
            NativeLazerDiagnosticStage? fast = fastPath.Stages.FirstOrDefault(stage => stage.Number == clean.Number);
            bool match = fast is not null && clean.SourceAddress == fast.SourceAddress &&
                         clean.OffsetUsed == fast.OffsetUsed &&
                         clean.TargetAddressOrValue == fast.TargetAddressOrValue &&
                         clean.ValidationResult == fast.ValidationResult;
            comparisons.Add(new(clean.Number, clean.Name, match, match ? null :
                $"clean(source={Format(clean.SourceAddress)},offset={Format(clean.OffsetUsed)},target={clean.TargetAddressOrValue},valid={clean.ValidationResult}) " +
                $"fast(source={Format(fast?.SourceAddress)},offset={Format(fast?.OffsetUsed)},target={fast?.TargetAddressOrValue ?? "missing"},valid={fast?.ValidationResult})"));
        }
        return comparisons;
    }

    private static string Fingerprint(NativeLazerRootValidationFingerprint value) =>
        $"rootMT={Hex(value.RootMethodTable)}; bindableMT={Hex(value.BeatmapBindableMethodTable)}; " +
        $"stringMT={Hex(value.VersionStringMethodTable)}; minRefs={value.MinimumCoherentRootReferences}";
    private static string Format(ulong? value) => value.HasValue ? Hex(value.Value) : "unavailable";
    private static string Hex(ulong value) => $"0x{value:X}";
}

internal static class NativeLazerBlobPathResolver
{
    internal static (string? Path, bool Exists) Resolve(string? hash)
    {
        if (hash is null || !NativeLazerRootFingerprintFactory.IsSha256(hash))
            return (null, false);

        string? storage = Environment.GetEnvironmentVariable("BEATINSIGHT_LAZER_STORAGE_PATH");
        if (string.IsNullOrWhiteSpace(storage))
        {
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(roaming))
                return (null, false);
            storage = Path.Combine(roaming, "osu");
        }

        string relative = Path.Combine(hash[..1], hash[..2], hash);
        string absolute = Path.GetFullPath(Path.Combine(storage, "files", relative));
        return (absolute, File.Exists(absolute));
    }
}

internal sealed class NativeLazerFastRootScanner(
    NativeLazerCountingMemory memory,
    IReadOnlyList<NativeLazerMemoryRegion> sourceRegions,
    long maximumBytes)
{
    private const int chunkSize = 4 * 1024 * 1024;
    private readonly NativeLazerMemoryRegion[] regions = sourceRegions.OrderBy(region => region.BaseAddress).ToArray();
    private long scannedBytes;

    internal bool Truncated { get; private set; }
    internal string? TruncationReason { get; private set; }
    internal long ScannedBytes => scannedBytes;

    internal IReadOnlyList<ulong> FindRoots(NativeLazerFastLayout layout, IReadOnlyList<string> anchors)
    {
        HashSet<ulong> stringObjects = FindStringObjects(anchors);
        if (Truncated && TruncationReason == "VersionAnchorObjectLimit")
        {
            Truncated = false;
            TruncationReason = "VersionAnchorObjectLimitFallbackRequired";
            return [];
        }
        if (Truncated || stringObjects.Count == 0)
            return [];

        var roots = new HashSet<ulong>();
        foreach (NativeLazerMemoryRegion region in regions)
        {
            for (ulong offset = 0; offset < region.Size; offset += chunkSize)
            {
                int count = checked((int)Math.Min((ulong)chunkSize, region.Size - offset));
                byte[]? block = ReadScanBlock(region.BaseAddress + offset, count);
                if (block is null)
                    continue;
                ulong blockAddress = region.BaseAddress + offset;
                int alignment = checked((int)((8 - (blockAddress & 7)) & 7));
                for (int index = alignment; index <= block.Length - 8; index += 8)
                {
                    ulong target = BinaryPrimitives.ReadUInt64LittleEndian(block.AsSpan(index, 8));
                    if (!stringObjects.Contains(target))
                        continue;
                    ulong slot = blockAddress + checked((ulong)index);
                    if (slot < layout.VersionHash)
                        continue;
                    ulong root = slot - layout.VersionHash;
                    if (NativeLazerRootFingerprintFactory.TryCreate(root, layout, memory, out _))
                        roots.Add(root);
                    if (roots.Count > 64)
                    {
                        Truncated = true;
                        TruncationReason = "AnchorRootCandidateLimit";
                        return roots.ToArray();
                    }
                }
            }
        }
        return roots.ToArray();
    }

    internal IReadOnlyList<ulong> FindStructuralRoots(NativeLazerFastLayout layout)
    {
        ulong[] offsets = layout.RootReferenceOffsets;
        int requiredBytes = checked((int)(offsets.Max() + 8));
        int overlap = requiredBytes - 1;
        var roots = new HashSet<ulong>();
        foreach (NativeLazerMemoryRegion region in regions)
        {
            for (ulong offset = 0; offset < region.Size;)
            {
                int count = checked((int)Math.Min((ulong)chunkSize, region.Size - offset));
                byte[]? block = ReadScanBlock(region.BaseAddress + offset, count);
                if (Truncated)
                    return roots.ToArray();
                if (block is not null)
                {
                    ulong blockAddress = region.BaseAddress + offset;
                    int alignment = checked((int)((8 - (blockAddress & 7)) & 7));
                    for (int index = alignment; index <= block.Length - requiredBytes; index += 8)
                    {
                        ulong methodTable = BinaryPrimitives.ReadUInt64LittleEndian(block.AsSpan(index, 8));
                        if (methodTable < 0x10000)
                            continue;
                        int coherentReferences = 0;
                        foreach (ulong fieldOffset in offsets)
                        {
                            ulong pointer = BinaryPrimitives.ReadUInt64LittleEndian(
                                block.AsSpan(index + checked((int)fieldOffset), 8));
                            if (pointer >= 0x10000 && (pointer & 7) == 0)
                                coherentReferences++;
                        }
                        if (coherentReferences < 5)
                            continue;
                        ulong root = blockAddress + checked((ulong)index);
                        if (!IsFullChainPlausible(root, layout))
                            continue;
                        roots.Add(root);
                        if (roots.Count > 64)
                        {
                            Truncated = true;
                            TruncationReason = "StructuralRootCandidateLimit";
                            return roots.ToArray();
                        }
                    }
                }
                if (count <= overlap)
                    break;
                offset += checked((ulong)(count - overlap));
            }
        }
        return roots.ToArray();
    }

    private HashSet<ulong> FindStringObjects(IReadOnlyList<string> anchors)
    {
        (string Value, byte[] Bytes)[] patterns = anchors
            .Distinct(StringComparer.Ordinal)
            .Select(anchor => (anchor, Encoding.Unicode.GetBytes(anchor)))
            .ToArray();
        int overlap = patterns.Max(pattern => pattern.Bytes.Length) - 1;
        var results = new HashSet<ulong>();
        foreach (NativeLazerMemoryRegion region in regions)
        {
            for (ulong offset = 0; offset < region.Size;)
            {
                int count = checked((int)Math.Min((ulong)chunkSize, region.Size - offset));
                byte[]? block = ReadScanBlock(region.BaseAddress + offset, count);
                if (block is not null)
                {
                    foreach ((string value, byte[] pattern) in patterns)
                    {
                        int start = 0;
                        while (start <= block.Length - pattern.Length)
                        {
                            int relative = block.AsSpan(start).IndexOf(pattern);
                            if (relative < 0)
                                break;
                            int index = start + relative;
                            ulong characters = region.BaseAddress + offset + checked((ulong)index);
                            if (characters >= 12)
                            {
                                ulong objectAddress = characters - 12;
                                if (memory.ReadString(objectAddress, 256)?.Value == value)
                                    results.Add(objectAddress);
                            }
                            if (results.Count >= 64)
                            {
                                Truncated = true;
                                TruncationReason = "VersionAnchorObjectLimit";
                                return results;
                            }
                            start = index + 1;
                        }
                    }
                }
                if (count <= overlap)
                    break;
                offset += checked((ulong)(count - overlap));
            }
        }
        return results;
    }

    private byte[]? ReadScanBlock(ulong address, int count)
    {
        if (scannedBytes > maximumBytes - count)
        {
            Truncated = true;
            TruncationReason = "MaximumScanBytes";
            return null;
        }
        byte[]? block = memory.ReadBytes(address, count);
        scannedBytes += block?.Length ?? 0;
        return block;
    }

    private bool IsFullChainPlausible(ulong root, NativeLazerFastLayout layout)
    {
        if (!NativeLazerRootFingerprintFactory.TryCreate(root, layout, memory, out _))
            return false;
        ulong? bindable = memory.ReadPointer(root + layout.Beatmap);
        ulong? working = bindable.HasValue ? memory.ReadPointer(bindable.Value + layout.BindableValue) : null;
        if (!NativeLazerRootFingerprintFactory.TryReadObjectMethodTable(working, memory, out _))
            return false;
        ulong? info = memory.ReadPointer(working!.Value + layout.WorkingBeatmapInfo);
        ulong? set = memory.ReadPointer(working.Value + layout.WorkingBeatmapSetInfo);
        return NativeLazerRootFingerprintFactory.TryReadObjectMethodTable(info, memory, out _) &&
               NativeLazerRootFingerprintFactory.TryReadObjectMethodTable(set, memory, out _) &&
               memory.ReadPointer(info!.Value + layout.BeatmapInfoSet) == set;
    }
}

internal static class NativeLazerFastLayoutDiscovery
{
    internal static (NativeLazerFastLayout Layout, string[] VersionAnchors) Derive(
        string installDirectory,
        string? productVersion)
    {
        var context = new LazerLoadContext(installDirectory);
        try
        {
            Assembly game = context.LoadFromAssemblyPath(Path.Combine(installDirectory, "osu.Game.dll"));
            Assembly framework = context.LoadFromAssemblyPath(Path.Combine(installDirectory, "osu.Framework.dll"));
            Type osuGame = RequireType(game, "osu.Game.OsuGame");
            Type workingBase = RequireType(game, "osu.Game.Beatmaps.WorkingBeatmap");
            Type concreteWorking = game.GetTypes().Where(type => !type.IsAbstract && workingBase.IsAssignableFrom(type))
                .OrderBy(type => type.FullName, StringComparer.Ordinal).FirstOrDefault()
                ?? throw new TypeLoadException("No concrete WorkingBeatmap type was found.");
            Type beatmapInfo = RequireType(game, "osu.Game.Beatmaps.BeatmapInfo");
            Type beatmapSetInfo = RequireType(game, "osu.Game.Beatmaps.BeatmapSetInfo");
            Type bindable = RequireType(framework, "osu.Framework.Bindables.Bindable`1").MakeGenericType(workingBase);
            string[] anchors = BuildVersionAnchors(productVersion);
            if (anchors.Length == 0)
                throw new InvalidOperationException("No precise local version anchor was available.");

            return (new(
                MeasureField(osuGame, "<VersionHash>k__BackingField"),
                MeasureField(osuGame, "<BeatmapManager>k__BackingField"),
                MeasureField(osuGame, "<API>k__BackingField"),
                MeasureField(osuGame, "<Storage>k__BackingField"),
                MeasureField(osuGame, "<Beatmap>k__BackingField"),
                MeasureField(osuGame, "Ruleset"),
                MeasureField(osuGame, "SelectedMods"),
                MeasureField(osuGame, "realm"),
                MeasureField(osuGame, "dependencies"),
                MeasureField(bindable, "value"),
                MeasureField(bindable, "disabled"),
                MeasureField(concreteWorking, "BeatmapInfo"),
                MeasureField(concreteWorking, "BeatmapSetInfo"),
                MeasureField(beatmapInfo, "<ID>k__BackingField"),
                MeasureField(beatmapInfo, "<BeatmapSet>k__BackingField"),
                MeasureField(beatmapInfo, "<OnlineID>k__BackingField"),
                MeasureField(beatmapInfo, "<Hash>k__BackingField"),
                MeasureField(beatmapSetInfo, "<OnlineID>k__BackingField")), anchors);
        }
        finally
        {
            context.Unload();
        }
    }

    private static string[] BuildVersionAnchors(string? productVersion)
    {
        var anchors = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(productVersion) && productVersion.Length >= 20)
        {
            int plus = productVersion.LastIndexOf('+');
            if (plus >= 0 && plus + 1 < productVersion.Length)
            {
                string hash = productVersion[(plus + 1)..];
                if (hash.Length >= 20 && hash.All(Uri.IsHexDigit))
                {
                    anchors.Add(hash);
                    anchors.Add(hash[..7]);
                }
            }
            int lazer = productVersion.IndexOf("-lazer", StringComparison.OrdinalIgnoreCase);
            if (lazer > 0)
            {
                anchors.Add(productVersion[..lazer]);
                anchors.Add(productVersion[..(lazer + "-lazer".Length)]);
            }
            anchors.Add(productVersion);
        }
        return anchors.OrderByDescending(value => value.Length).ToArray();
    }

    private static ulong MeasureField(Type concreteType, string name)
    {
        object instance = RuntimeHelpers.GetUninitializedObject(concreteType);
        FieldInfo field = FindField(concreteType, name);
        Func<object, nint> accessor = CreateFieldAddressAccessor(field);
        bool noGc = GC.TryStartNoGCRegion(1024 * 1024);
        try
        {
            nint objectAddress = Unsafe.As<object, nint>(ref instance);
            long offset = checked((long)(accessor(instance) - objectAddress));
            if (offset < 8 || offset > 0x4000)
                throw new InvalidOperationException($"Implausible field offset for {concreteType.FullName}.{name}.");
            return checked((ulong)offset);
        }
        finally
        {
            GC.KeepAlive(instance);
            if (noGc)
                GC.EndNoGCRegion();
        }
    }

    private static Type RequireType(Assembly assembly, string name) =>
        assembly.GetType(name, throwOnError: true, ignoreCase: false)!;

    private static FieldInfo FindField(Type type, string name)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            FieldInfo? field = current.GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field is not null)
                return field;
        }
        throw new MissingFieldException(type.FullName, name);
    }

    private static Func<object, nint> CreateFieldAddressAccessor(FieldInfo field)
    {
        var method = new DynamicMethod($"address_{field.MetadataToken:X8}", typeof(nint), [typeof(object)],
            typeof(NativeLazerFastLayoutDiscovery).Module, skipVisibility: true);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, field.DeclaringType!);
        il.Emit(OpCodes.Ldflda, field);
        il.Emit(OpCodes.Conv_I);
        il.Emit(OpCodes.Ret);
        return method.CreateDelegate<Func<object, nint>>();
    }

    private sealed class LazerLoadContext(string installDirectory) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string candidate = Path.Combine(installDirectory, assemblyName.Name + ".dll");
            return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
        }
    }
}
