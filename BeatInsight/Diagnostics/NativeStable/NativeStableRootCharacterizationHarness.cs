using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeatInsight.Diagnostics.NativeStable;

internal enum NativeStableQPhase { CaptureA, CaptureB, CaptureA2, Finalize }

internal sealed class NativeStableQCharacterizationSession
{
    public int SchemaVersion { get; set; }
    public int ProcessId { get; set; }
    public DateTimeOffset ProcessStartIdentity { get; set; }
    public ulong QAddress { get; set; }
    public NativeStablePersistedMap MapA { get; set; } = null!;
    public NativeStablePersistedMap MapB { get; set; } = null!;
    public ulong[] ValidatedRootReferences { get; set; } = [];
    public NativeStableQCharacterizationCapture[] Captures { get; set; } = [];
    public NativeStableRootStructuralProfile[] Profiles { get; set; } = [];
    public NativeStableQPhase NextPhase { get; set; }

    internal NativeStablePersistedMap ExpectedMap(NativeStableQPhase phase) => phase switch
    {
        NativeStableQPhase.CaptureA or NativeStableQPhase.CaptureA2 => MapA,
        NativeStableQPhase.CaptureB => MapB,
        _ => throw new InvalidDataException($"Phase {phase} has no expected map.")
    };
}

internal static class NativeStableQCharacterizationHarness
{
    internal const int CurrentSchemaVersion = 1;
    internal const string SessionEnvironmentVariable =
        "BEATINSIGHT_NATIVE_STABLE_Q_SESSION";
    internal const string ProfileEnvironmentVariable =
        "BEATINSIGHT_NATIVE_STABLE_Q_PROFILE";
    internal static string DefaultSessionPath => Path.Combine(
        Path.GetTempPath(), "BeatInsight", "NativeStable",
        "native-stable-q-characterization-session.json");
    internal static string DefaultProfilePath => Path.Combine(
        Path.GetTempPath(), "BeatInsight", "NativeStable",
        "native-stable-q-structural-profile.json");

    private static readonly NativeStableQPhase[] CapturePhases =
    [NativeStableQPhase.CaptureA, NativeStableQPhase.CaptureB, NativeStableQPhase.CaptureA2];

    internal static NativeStableQCharacterizationSession CreateFromCompletedC3(
        NativeStableRootDiagnosticSession c3)
    {
        NativeStableRootPhaseHarness.Validate(c3);
        if (c3.NextPhase != NativeStableRootPhase.Finalize || c3.PhaseCaptures.Length != 5)
        {
            throw new InvalidDataException("C.3 session is not complete.");
        }
        NativeStablePersistedRootCapture last = c3.PhaseCaptures.Single(capture =>
            capture.Phase == NativeStableRootPhase.A3);
        ulong[] qValues = last.Candidates
            .Where(candidate => candidate.ExpectedMapMatches && candidate.CurrentQAddress is not null)
            .Select(candidate => candidate.CurrentQAddress!.Value)
            .Distinct()
            .ToArray();
        if (qValues.Length != 1 || c3.SurvivingCandidateIndexes.Length == 0)
        {
            throw new InvalidDataException(
                "C.3 survivors do not converge on exactly one dynamic Q.");
        }
        ulong[] roots = c3.SurvivingCandidateIndexes
            .Select(index => c3.InitialCandidates[index].RootSlotAddress)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        return new()
        {
            SchemaVersion = CurrentSchemaVersion,
            ProcessId = c3.ProcessId,
            ProcessStartIdentity = c3.ProcessStartIdentity,
            QAddress = qValues[0],
            MapA = c3.MapA,
            MapB = c3.MapB,
            ValidatedRootReferences = roots,
            Captures = [],
            Profiles = [],
            NextPhase = NativeStableQPhase.CaptureA
        };
    }

    internal static void ApplyCapture(
        NativeStableQCharacterizationSession session,
        NativeStableQPhase phase,
        int processId,
        DateTimeOffset? processStartIdentity,
        NativeStableQCharacterizationCapture capture)
    {
        EnsureCanRun(session, phase);
        NativeStableProcessGenerationGuard.EnsureSame(
            session.ProcessId, session.ProcessStartIdentity, processId, processStartIdentity);
        if (capture.Phase != phase.ToString() || capture.QAddress != session.QAddress)
        {
            throw new InvalidDataException("Capture phase or Q differs from the persisted session.");
        }
        if (!capture.ExpectedMapMatches)
        {
            throw new NativeStableExpectedMapMismatchException(phase.ToString());
        }
        session.Captures = session.Captures.Append(capture).ToArray();
        session.Profiles = session.Profiles
            .Append(NativeStableRootStructuralProfile.FromCapture(capture)).ToArray();
        session.NextPhase = phase switch
        {
            NativeStableQPhase.CaptureA => NativeStableQPhase.CaptureB,
            NativeStableQPhase.CaptureB => NativeStableQPhase.CaptureA2,
            NativeStableQPhase.CaptureA2 => NativeStableQPhase.Finalize,
            _ => throw new InvalidDataException($"Phase {phase} is not a capture phase.")
        };
    }

    internal static NativeStableQCharacterizationSummary Finalize(
        NativeStableQCharacterizationSession session)
    {
        EnsureCanRun(session, NativeStableQPhase.Finalize);
        NativeStableQCharacterizationCapture first = session.Captures[0];
        bool exactQ = session.Captures.All(capture => capture.QAddress == first.QAddress);
        bool exactRegion = session.Captures.All(capture =>
            capture.QMetadata.RegionBase == first.QMetadata.RegionBase &&
            capture.QMetadata.AllocationBase == first.QMetadata.AllocationBase &&
            capture.QMetadata.OffsetFromRegionBase == first.QMetadata.OffsetFromRegionBase &&
            capture.QMetadata.OffsetFromAllocationBase == first.QMetadata.OffsetFromAllocationBase);
        bool exactReferences = session.Captures.All(capture =>
            capture.ReferencesToQ.Select(reference => reference.AbsoluteAddress)
                .SequenceEqual(first.ReferencesToQ.Select(reference => reference.AbsoluteAddress)));
        string firstProfile = JsonSerializer.Serialize(session.Profiles[0]);
        bool structural = session.Profiles.All(profile =>
            JsonSerializer.Serialize(profile) == firstProfile);
        return new(exactQ, exactRegion, exactReferences, structural,
            session.Profiles[0], session.Captures);
    }

    internal static void EnsureCanRun(
        NativeStableQCharacterizationSession session,
        NativeStableQPhase phase)
    {
        Validate(session);
        if (phase != session.NextPhase)
        {
            throw new InvalidDataException(
                $"Phase order mismatch: session requires {session.NextPhase}, requested {phase}.");
        }
    }

    internal static void Validate(NativeStableQCharacterizationSession session)
    {
        if (session.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported Q characterization schema {session.SchemaVersion}.");
        }
        if (session.ProcessId <= 0 || session.ProcessStartIdentity == default ||
            session.QAddress == 0 || session.MapA is null || session.MapB is null ||
            session.ValidatedRootReferences is null || session.Captures is null ||
            session.Profiles is null)
        {
            throw new InvalidDataException("Q characterization session is incomplete.");
        }
        int completed = session.NextPhase == NativeStableQPhase.Finalize
            ? CapturePhases.Length
            : Array.IndexOf(CapturePhases, session.NextPhase);
        if (completed < 0 || session.Captures.Length != completed ||
            session.Profiles.Length != completed ||
            session.Captures.Where((capture, index) =>
                capture.Phase != CapturePhases[index].ToString() ||
                capture.QAddress != session.QAddress || !capture.ExpectedMapMatches).Any())
        {
            throw new InvalidDataException("Q characterization phase history is malformed.");
        }
    }
}

internal static class NativeStableQLiveCapture
{
    internal static NativeStableQCharacterizationCapture Capture(
        NativeStableQCharacterizationSession session,
        NativeStableQPhase phase,
        out int processId,
        out DateTimeOffset? processStartIdentity)
    {
        NativeStableQCharacterizationHarness.EnsureCanRun(session, phase);
        NativeStableProcessCandidate? process = NativeStableProcessDiscovery.Discover().UniqueCandidate;
        if (process is null)
        {
            throw new InvalidOperationException("A unique osu!stable process is required.");
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
        return NativeStableRootCharacterizer.Capture(
            memory,
            memory.EnumerateReadableRegions(),
            NativeStableModuleCatalog.Capture(processId),
            checked((nuint)session.QAddress),
            session.ExpectedMap(phase).ToRuntime(),
            phase.ToString(),
            session.ValidatedRootReferences.Select(value => checked((nuint)value)).ToArray());
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

internal sealed class NativeStableQCharacterizationStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    internal NativeStableQCharacterizationSession Load(string path) => LoadValidated(
        path,
        json => JsonSerializer.Deserialize<NativeStableQCharacterizationSession>(json, Options),
        NativeStableQCharacterizationHarness.Validate,
        "Q characterization session");

    internal void Save(string path, NativeStableQCharacterizationSession session)
    {
        NativeStableQCharacterizationHarness.Validate(session);
        SaveJson(path, JsonSerializer.Serialize(session, Options));
    }

    internal NativeStableRootStructuralProfile LoadProfile(string path) => LoadValidated(
        path,
        json => JsonSerializer.Deserialize<NativeStableRootStructuralProfile>(json, Options),
        profile => profile.Validate(),
        "Q structural profile");

    internal void SaveProfile(string path, NativeStableRootStructuralProfile profile)
    {
        profile.Validate();
        SaveJson(path, JsonSerializer.Serialize(profile, Options));
    }

    private static T LoadValidated<T>(
        string path, Func<string, T?> deserialize, Action<T> validate, string description)
        where T : class
    {
        try
        {
            if (!File.Exists(path))
            {
                throw new InvalidDataException($"{description} is missing: {path}");
            }
            T value = deserialize(File.ReadAllText(path)) ??
                throw new InvalidDataException($"{description} is empty.");
            validate(value);
            return value;
        }
        catch (InvalidDataException) { throw; }
        catch (Exception exception) when (exception is JsonException or IOException or
            UnauthorizedAccessException or NotSupportedException)
        {
            throw new InvalidDataException($"{description} could not be loaded.", exception);
        }
    }

    private static void SaveJson(string path, string json)
    {
        string full = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(full) ??
            throw new InvalidDataException("Diagnostic path has no directory.");
        Directory.CreateDirectory(directory);
        string temporary = full + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, full, overwrite: true);
    }
}

internal sealed record NativeStableRootStructuralProfile(
    int SchemaVersion,
    NativeStableAddressShape Q,
    NativeStableStructuralFingerprint QNeighborhood,
    NativeStableReferenceShape[] References,
    NativeStableClusterShape[] Clusters,
    NativeStableThirdLevelShape[] ThirdLevel)
{
    internal static NativeStableRootStructuralProfile FromCapture(
        NativeStableQCharacterizationCapture capture) => new(
            1,
            NativeStableAddressShape.FromObserved(capture.QMetadata),
            capture.QNeighborhood,
            capture.ReferencesToQ.Select(reference =>
                NativeStableReferenceShape.FromObserved(reference)).ToArray(),
            capture.ReferenceClusters.Select(NativeStableClusterShape.FromObserved).ToArray(),
            capture.ThirdLevelReferences.Select(reference =>
                new NativeStableThirdLevelShape(
                    NativeStableAddressShape.FromObserved(reference.Address),
                    capture.ReferencesToQ
                        .Where(candidate => candidate.AbsoluteAddress == reference.ReferencedRAddress)
                        .Select(NativeStableReferenceShape.FromObserved)
                        .Single())).ToArray());

    internal void Validate()
    {
        if (SchemaVersion != 1 || Q is null || QNeighborhood is null ||
            References is null || Clusters is null || ThirdLevel is null)
        {
            throw new InvalidDataException("Q structural profile is malformed.");
        }
    }
}

internal sealed record NativeStableAddressShape(
    ulong? OffsetFromAllocationBase,
    ulong OffsetFromRegionBase,
    ulong RegionSize,
    uint State,
    uint Protection,
    uint Type,
    int Alignment,
    NativeStableRegionBacking Backing,
    string? ModuleName,
    ulong? ModuleSize,
    ulong? OffsetFromModuleBase)
{
    internal static NativeStableAddressShape FromObserved(NativeStableObservedAddress value) =>
        new(value.OffsetFromAllocationBase, value.OffsetFromRegionBase, value.RegionSize,
            value.State, value.Protection, value.Type, value.Alignment, value.Backing,
            value.Module?.Name, value.Module?.Size, value.Module?.OffsetFromModuleBase);
}

internal sealed record NativeStableReferenceShape(
    NativeStableAddressShape Address,
    NativeStableStructuralFingerprint Neighborhood)
{
    internal static NativeStableReferenceShape FromObserved(NativeStableQReference value) =>
        new(NativeStableAddressShape.FromObserved(value.Address), value.Neighborhood);
}

internal sealed record NativeStableClusterShape(
    int Count, ulong? FirstOffsetFromAllocation, ulong? LastOffsetFromAllocation,
    ulong RegionSize, uint Protection, uint Type, NativeStableRegionBacking Backing,
    string? ModuleName, ulong? ModuleSize)
{
    internal static NativeStableClusterShape FromObserved(NativeStableReferenceCluster value) =>
        new(value.Count, value.FirstOffsetFromAllocation, value.LastOffsetFromAllocation,
            value.RegionSize, value.Protection, value.Type, value.Backing,
            value.Module?.Name, value.Module?.Size);
}

internal sealed record NativeStableThirdLevelShape(
    NativeStableAddressShape T,
    NativeStableReferenceShape R);

internal sealed record NativeStableQCharacterizationSummary(
    bool ExactQAddressInvariant,
    bool ExactQRegionIdentityInvariant,
    bool ExactReferenceSetInvariant,
    bool StructuralProfileInvariant,
    NativeStableRootStructuralProfile RestartProfile,
    IReadOnlyList<NativeStableQCharacterizationCapture> Captures);
