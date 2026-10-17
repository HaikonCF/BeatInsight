using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeatInsight.Diagnostics.NativeStable;

/// <summary>Diagnostic-only state for the multi-invocation V3.1C.3 experiment.</summary>
internal enum NativeStableRootPhase { Calibrate, A1, B1, A2, B2, A3, Finalize }

internal sealed class NativeStableRootDiagnosticSession
{
    public int SchemaVersion { get; set; }
    public int ProcessId { get; set; }
    public DateTimeOffset ProcessStartIdentity { get; set; }
    public NativeStablePersistedMap MapA { get; set; } = null!;
    public NativeStablePersistedMap MapB { get; set; } = null!;
    public ulong GroundTruthX { get; set; }
    public ulong[] QCandidateAddresses { get; set; } = [];
    public NativeStablePersistedRootCandidate[] InitialCandidates { get; set; } = [];
    public int[] SurvivingCandidateIndexes { get; set; } = [];
    public NativeStablePersistedRootCapture[] PhaseCaptures { get; set; } = [];
    public NativeStableRootPhase NextPhase { get; set; }
    public int PresentedCandidateLimit { get; set; }
    public long GroundTruthObjectDiscoveryTicks { get; set; }
    public long FirstLevelReferenceSearchTicks { get; set; }
    public long SecondLevelReferenceSearchTicks { get; set; }
    public long CandidateMetadataTicks { get; set; }
    public long ReverseDiscoveryTotalTicks { get; set; }
    public long TotalCalibrationTicks { get; set; }

    internal NativeStablePersistedMap ExpectedMap(NativeStableRootPhase phase) => phase switch
    {
        NativeStableRootPhase.A1 or NativeStableRootPhase.A2 or NativeStableRootPhase.A3 => MapA,
        NativeStableRootPhase.B1 or NativeStableRootPhase.B2 => MapB,
        _ => throw new InvalidDataException($"Phase {phase} has no expected map.")
    };
}

internal static class NativeStableRootPhaseHarness
{
    internal const int CurrentSchemaVersion = 1;
    internal const string SessionEnvironmentVariable =
        "BEATINSIGHT_NATIVE_STABLE_ROOT_SESSION";
    internal static string DefaultSessionPath => Path.Combine(
        Path.GetTempPath(), "BeatInsight", "NativeStable", "native-stable-root-session.json");

    private static readonly NativeStableRootPhase[] LivePhases =
    [
        NativeStableRootPhase.A1, NativeStableRootPhase.B1, NativeStableRootPhase.A2,
        NativeStableRootPhase.B2, NativeStableRootPhase.A3
    ];

    internal static NativeStableRootDiagnosticSession Create(
        int processId,
        DateTimeOffset processStartIdentity,
        NativeStableKnownBeatmapObjectMetadata mapA,
        NativeStableKnownBeatmapObjectMetadata mapB,
        NativeStableRootCalibration calibration,
        TimeSpan groundTruthObjectDiscovery,
        TimeSpan totalCalibration)
    {
        NativeStablePersistedRootCandidate[] candidates = calibration.Chains
            .Select(NativeStablePersistedRootCandidate.FromRuntime).ToArray();
        return new()
        {
            SchemaVersion = CurrentSchemaVersion,
            ProcessId = processId,
            ProcessStartIdentity = processStartIdentity,
            MapA = NativeStablePersistedMap.FromRuntime(mapA),
            MapB = NativeStablePersistedMap.FromRuntime(mapB),
            GroundTruthX = checked((ulong)calibration.GroundTruthX),
            QCandidateAddresses = calibration.QCandidateAddresses
                .Select(value => checked((ulong)value)).ToArray(),
            InitialCandidates = candidates,
            SurvivingCandidateIndexes = Enumerable.Range(0, candidates.Length).ToArray(),
            PhaseCaptures = [],
            NextPhase = NativeStableRootPhase.A1,
            PresentedCandidateLimit = calibration.PresentedCandidateLimit,
            GroundTruthObjectDiscoveryTicks = groundTruthObjectDiscovery.Ticks,
            FirstLevelReferenceSearchTicks = calibration.Timings.FirstLevelReferenceSearch.Ticks,
            SecondLevelReferenceSearchTicks = calibration.Timings.SecondLevelReferenceSearch.Ticks,
            CandidateMetadataTicks = calibration.Timings.CandidateMetadata.Ticks,
            ReverseDiscoveryTotalTicks = calibration.Timings.TotalCalibration.Ticks,
            TotalCalibrationTicks = totalCalibration.Ticks
        };
    }

    internal static void EnsureCanRun(
        NativeStableRootDiagnosticSession session,
        NativeStableRootPhase phase)
    {
        Validate(session);
        if (phase is NativeStableRootPhase.Calibrate || phase != session.NextPhase)
        {
            throw new InvalidDataException(
                $"Phase order mismatch: session requires {session.NextPhase}, requested {phase}.");
        }
    }

    internal static void ApplyCapture(
        NativeStableRootDiagnosticSession session,
        NativeStableRootPhase phase,
        int currentProcessId,
        DateTimeOffset? currentProcessStartIdentity,
        NativeStableRootTransitionCapture capture)
    {
        EnsureCanRun(session, phase);
        NativeStableProcessGenerationGuard.EnsureSame(
            session.ProcessId, session.ProcessStartIdentity,
            currentProcessId, currentProcessStartIdentity);
        if (!string.Equals(capture.Phase, phase.ToString(), StringComparison.Ordinal))
        {
            throw new InvalidDataException("Capture phase does not match the requested phase.");
        }

        int[] entering = session.SurvivingCandidateIndexes.ToArray();
        if (capture.Candidates.Count != entering.Length)
        {
            throw new InvalidDataException(
                "Capture candidate count differs from the persisted survivor set.");
        }
        var survivors = new List<int>();
        for (int position = 0; position < entering.Length; position++)
        {
            int index = entering[position];
            NativeStablePersistedRootCandidate saved = session.InitialCandidates[index];
            NativeStableRootCandidateTransition observed = capture.Candidates[position];
            if (observed.RootSlotAddress != checked((nuint)saved.RootSlotAddress) ||
                observed.InitialQCandidateAddress != checked((nuint)saved.QCandidateAddress))
            {
                throw new InvalidDataException(
                    "Capture attempted to replace, reorder, or add a persisted root candidate.");
            }
            if (observed.ExpectedMapMatches)
            {
                survivors.Add(index);
            }
        }

        session.PhaseCaptures = session.PhaseCaptures
            .Append(NativeStablePersistedRootCapture.FromRuntime(capture)).ToArray();
        session.SurvivingCandidateIndexes = survivors.ToArray();
        session.NextPhase = Next(phase);
    }

    internal static NativeStableRootTransitionSummary Finalize(
        NativeStableRootDiagnosticSession session)
    {
        EnsureCanRun(session, NativeStableRootPhase.Finalize);
        return NativeStableRootTransitionValidator.Summarize(
            session.InitialCandidates.Select(candidate => candidate.ToRuntime()).ToArray(),
            session.PhaseCaptures.Select(capture => capture.ToRuntime()).ToArray());
    }

    internal static void Validate(NativeStableRootDiagnosticSession session)
    {
        if (session.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported root diagnostic session schema {session.SchemaVersion}.");
        }
        if (session.ProcessId <= 0 || session.ProcessStartIdentity == default ||
            session.MapA is null || session.MapB is null ||
            session.InitialCandidates is null || session.SurvivingCandidateIndexes is null ||
            session.PhaseCaptures is null || session.QCandidateAddresses is null)
        {
            throw new InvalidDataException("Root diagnostic session is incomplete.");
        }
        if (session.SurvivingCandidateIndexes.Distinct().Count() !=
                session.SurvivingCandidateIndexes.Length ||
            session.SurvivingCandidateIndexes.Any(index =>
                index < 0 || index >= session.InitialCandidates.Length))
        {
            throw new InvalidDataException("Root diagnostic survivor indexes are invalid.");
        }
        int completed = session.NextPhase == NativeStableRootPhase.Finalize
            ? LivePhases.Length
            : Array.IndexOf(LivePhases, session.NextPhase);
        if (completed < 0 || session.PhaseCaptures.Length != completed ||
            session.PhaseCaptures.Where((capture, index) =>
                capture.Phase != LivePhases[index]).Any())
        {
            throw new InvalidDataException("Root diagnostic phase history is malformed.");
        }
    }

    private static NativeStableRootPhase Next(NativeStableRootPhase phase) => phase switch
    {
        NativeStableRootPhase.A1 => NativeStableRootPhase.B1,
        NativeStableRootPhase.B1 => NativeStableRootPhase.A2,
        NativeStableRootPhase.A2 => NativeStableRootPhase.B2,
        NativeStableRootPhase.B2 => NativeStableRootPhase.A3,
        NativeStableRootPhase.A3 => NativeStableRootPhase.Finalize,
        _ => throw new InvalidDataException($"Phase {phase} is not a capture phase.")
    };
}

internal sealed class NativeStableRootDiagnosticSessionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    internal NativeStableRootDiagnosticSession Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                throw new InvalidDataException($"Root diagnostic session is missing: {path}");
            }
            NativeStableRootDiagnosticSession? session =
                JsonSerializer.Deserialize<NativeStableRootDiagnosticSession>(
                    File.ReadAllText(path), Options);
            if (session is null)
            {
                throw new InvalidDataException("Root diagnostic session is empty.");
            }
            NativeStableRootPhaseHarness.Validate(session);
            return session;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or IOException or
            UnauthorizedAccessException or NotSupportedException)
        {
            throw new InvalidDataException("Root diagnostic session could not be loaded.", exception);
        }
    }

    internal void Save(string path, NativeStableRootDiagnosticSession session)
    {
        NativeStableRootPhaseHarness.Validate(session);
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidDataException("Root diagnostic session path has no directory.");
        }
        Directory.CreateDirectory(directory);
        string temporary = fullPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(session, Options));
        File.Move(temporary, fullPath, overwrite: true);
    }
}

internal static class NativeStableRootLivePhaseCapture
{
    internal static NativeStableRootTransitionCapture Capture(
        NativeStableRootDiagnosticSession session,
        NativeStableRootPhase phase,
        out int processId,
        out DateTimeOffset? processStartIdentity)
    {
        NativeStableRootPhaseHarness.EnsureCanRun(session, phase);
        NativeStableProcessDiscoveryResult discovery = NativeStableProcessDiscovery.Discover();
        NativeStableProcessCandidate? process = discovery.UniqueCandidate;
        if (process is null)
        {
            throw new InvalidOperationException(
                $"A unique osu!stable process is required; discovery status was {discovery.Status}.");
        }
        processId = process.ProcessId;
        processStartIdentity = TryGetProcessStart(processId);
        NativeStableProcessGenerationGuard.EnsureSame(
            session.ProcessId, session.ProcessStartIdentity, processId, processStartIdentity);
        NativeStableAttachResult attachment = NativeStableProcessMemory.TryAttach(process);
        if (attachment.Memory is null)
        {
            throw new InvalidOperationException($"Read-only attachment failed: {attachment.Status}.");
        }
        using NativeStableProcessMemory memory = attachment.Memory;
        NativeStableRootChainCandidate[] candidates = session.SurvivingCandidateIndexes
            .Select(index => session.InitialCandidates[index].ToRuntime()).ToArray();
        return NativeStableRootTransitionValidator.Capture(
            memory, memory.EnumerateReadableRegions(), candidates,
            session.ExpectedMap(phase).ToRuntime(), phase.ToString());
    }

    private static DateTimeOffset? TryGetProcessStart(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.StartTime.ToUniversalTime();
        }
        catch (Exception exception) when (exception is ArgumentException or
            InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}

internal sealed record NativeStablePersistedMap(
    string KnownMapPath, string Checksum, string Folder, string Filename,
    int? BeatmapId, int? BeatmapSetId)
{
    internal static NativeStablePersistedMap FromRuntime(NativeStableKnownBeatmapObjectMetadata map) =>
        new(map.KnownMapPath, map.Checksum, map.Folder, map.Filename,
            map.BeatmapId, map.BeatmapSetId);
    internal NativeStableKnownBeatmapObjectMetadata ToRuntime() =>
        new(KnownMapPath, Checksum, Folder, Filename, BeatmapId, BeatmapSetId);
}

internal sealed record NativeStablePersistedRootCandidate(
    ulong RootSlotAddress, ulong QCandidateAddress, ulong GroundTruthX,
    NativeStablePersistedAddress RootSlotMetadata,
    NativeStablePersistedAddress QCandidateMetadata,
    NativeStablePersistedAddress XMetadata,
    bool RootSlotAndQShareRegion, bool QAndXShareRegion,
    NativeStablePersistedFingerprint NeighborhoodFingerprint)
{
    internal static NativeStablePersistedRootCandidate FromRuntime(NativeStableRootChainCandidate value) =>
        new(checked((ulong)value.RootSlotAddress), checked((ulong)value.QCandidateAddress),
            checked((ulong)value.GroundTruthX),
            NativeStablePersistedAddress.FromRuntime(value.RootSlotMetadata),
            NativeStablePersistedAddress.FromRuntime(value.QCandidateMetadata),
            NativeStablePersistedAddress.FromRuntime(value.XMetadata),
            value.RootSlotAndQShareRegion, value.QAndXShareRegion,
            NativeStablePersistedFingerprint.FromRuntime(value.NeighborhoodFingerprint));
    internal NativeStableRootChainCandidate ToRuntime() =>
        new(checked((nuint)RootSlotAddress), checked((nuint)QCandidateAddress),
            checked((nuint)GroundTruthX), RootSlotMetadata.ToRuntime(),
            QCandidateMetadata.ToRuntime(), XMetadata.ToRuntime(),
            RootSlotAndQShareRegion, QAndXShareRegion, NeighborhoodFingerprint.ToRuntime());
}

internal sealed record NativeStablePersistedAddress(
    ulong Address, ulong? RegionBase, ulong? AllocationBase, ulong RegionSize,
    uint State, uint Protection, uint Type, ulong OffsetFromRegionBase,
    int Alignment, NativeStableRegionBacking Backing)
{
    internal static NativeStablePersistedAddress FromRuntime(NativeStableAddressMetadata value) =>
        new(checked((ulong)value.Address), value.RegionBase is null ? null : checked((ulong)value.RegionBase.Value),
            value.AllocationBase is null ? null : checked((ulong)value.AllocationBase.Value),
            checked((ulong)value.RegionSize), value.State, value.Protection, value.Type,
            checked((ulong)value.OffsetFromRegionBase), value.Alignment, value.Backing);
    internal NativeStableAddressMetadata ToRuntime() =>
        new(checked((nuint)Address), RegionBase is null ? null : checked((nuint)RegionBase.Value),
            AllocationBase is null ? null : checked((nuint)AllocationBase.Value),
            checked((nuint)RegionSize), State, Protection, Type,
            checked((nuint)OffsetFromRegionBase), Alignment, Backing);
}

internal sealed record NativeStablePersistedFingerprint(
    ulong DistanceFromRegionStart, ulong DistanceToRegionEnd,
    NativeStableNeighborhoodObservation[] Observations)
{
    internal static NativeStablePersistedFingerprint FromRuntime(NativeStableNeighborhoodFingerprint value) =>
        new(checked((ulong)value.DistanceFromRegionStart), checked((ulong)value.DistanceToRegionEnd),
            value.Observations.ToArray());
    internal NativeStableNeighborhoodFingerprint ToRuntime() =>
        new(checked((nuint)DistanceFromRegionStart), checked((nuint)DistanceToRegionEnd), Observations);
}

internal sealed record NativeStablePersistedRootCapture(
    NativeStableRootPhase Phase, NativeStablePersistedRootTransition[] Candidates,
    long ValidationTimeTicks)
{
    internal static NativeStablePersistedRootCapture FromRuntime(NativeStableRootTransitionCapture value) =>
        new(Enum.Parse<NativeStableRootPhase>(value.Phase, ignoreCase: false),
            value.Candidates.Select(NativeStablePersistedRootTransition.FromRuntime).ToArray(),
            value.ValidationTime.Ticks);
    internal NativeStableRootTransitionCapture ToRuntime() =>
        new(Phase.ToString(), Candidates.Select(candidate => candidate.ToRuntime(Phase)).ToArray(),
            TimeSpan.FromTicks(ValidationTimeTicks));
}

internal sealed record NativeStablePersistedRootTransition(
    ulong RootSlotAddress, ulong InitialQCandidateAddress, ulong? CurrentQAddress,
    ulong? CurrentXAddress, bool PointerReadable, bool FinalObjectReadable,
    int MatchedCoreFields, int MatchedOptionalIds, int AvailableOptionalIds,
    bool ExpectedMapMatches)
{
    internal static NativeStablePersistedRootTransition FromRuntime(NativeStableRootCandidateTransition value) =>
        new(checked((ulong)value.RootSlotAddress), checked((ulong)value.InitialQCandidateAddress),
            value.CurrentQAddress is null ? null : checked((ulong)value.CurrentQAddress.Value),
            value.CurrentXAddress is null ? null : checked((ulong)value.CurrentXAddress.Value),
            value.PointerReadable, value.FinalObjectReadable, value.MatchedCoreFields,
            value.MatchedOptionalIds, value.AvailableOptionalIds, value.ExpectedMapMatches);
    internal NativeStableRootCandidateTransition ToRuntime(NativeStableRootPhase phase) =>
        new(phase.ToString(), checked((nuint)RootSlotAddress), checked((nuint)InitialQCandidateAddress),
            CurrentQAddress is null ? null : checked((nuint)CurrentQAddress.Value),
            CurrentXAddress is null ? null : checked((nuint)CurrentXAddress.Value),
            PointerReadable, FinalObjectReadable, MatchedCoreFields, MatchedOptionalIds,
            AvailableOptionalIds, ExpectedMapMatches);
}
