using System.Diagnostics;

namespace BeatInsight.Diagnostics.NativeStable;

/// <summary>
/// EXPERIMENTAL — V3 PROOF OF CONCEPT — NOT PRODUCTION INTEGRATION.
/// Reads one previously discovered, region-relative selection-slot hypothesis.
/// </summary>
internal sealed class NativeStableSelectionSlotReader
{
    internal const nuint SlotOffset = 0x5388;

    private readonly INativeStableMemoryReader memory;
    private readonly NativeStableCandidateTargetSet targets;
    private readonly Func<IReadOnlyList<NativeStableMemoryRegion>> enumerateRegions;
    private NativeStableMemoryRegion region;
    private bool reacquisitionAttempted;

    internal NativeStableSelectionSlotReader(
        INativeStableMemoryReader memory,
        NativeStableMemoryRegion region,
        NativeStableCandidateTargetSet targets,
        Func<IReadOnlyList<NativeStableMemoryRegion>>? enumerateRegions = null)
    {
        this.memory = memory;
        this.region = region;
        this.targets = targets;
        this.enumerateRegions = enumerateRegions ?? (() => []);
    }

    internal NativeStableSelectionSlotSnapshot Capture(
        int processId,
        string state,
        string expectedSelectedMap)
    {
        string captureId = Guid.NewGuid().ToString("N");
        DateTimeOffset observedAtUtc = DateTimeOffset.UtcNow;
        var total = Stopwatch.StartNew();
        var rapidRead = Stopwatch.StartNew();
        NativeStableMemoryRegion capturedRegion = region;
        nuint slotAddress = capturedRegion.BaseAddress + SlotOffset;
        bool slotReadable = TryReadValue(memory, slotAddress, out nuint rawValue);
        bool regionInvalidated = !slotReadable;
        bool reacquisitionAttemptedNow = false;
        bool regionReacquired = false;
        nuint? oldRegionBase = null;
        nuint? newRegionBase = null;

        if (!slotReadable && !reacquisitionAttempted)
        {
            reacquisitionAttempted = true;
            reacquisitionAttemptedNow = true;
            oldRegionBase = capturedRegion.BaseAddress;
            NativeStableMemoryRegion? replacement = FindReplacementRegion(capturedRegion);
            if (replacement is not null)
            {
                region = replacement.Value;
                capturedRegion = region;
                newRegionBase = region.BaseAddress;
                slotAddress = region.BaseAddress + SlotOffset;
                slotReadable = TryReadValue(memory, slotAddress, out rawValue);
                regionReacquired = slotReadable;
            }
        }

        TimeSpan rapidReadElapsed = rapidRead.Elapsed;
        var resolution = Stopwatch.StartNew();
        nuint? level1 = null;
        nuint? level2 = null;
        NativeStableSelectionResolution resolved = NativeStableSelectionResolution.None;

        if (slotReadable)
        {
            resolved = Resolve(rawValue);
            if (!resolved.IsKnown && rawValue != 0 &&
                TryReadValue(memory, rawValue, out nuint first))
            {
                level1 = first;
                resolved = Resolve(first);
                if (!resolved.IsKnown && first != 0 &&
                    TryReadValue(memory, first, out nuint second))
                {
                    level2 = second;
                    resolved = Resolve(second);
                }
            }

            if (!resolved.IsKnown)
            {
                resolved = rawValue == 0
                    ? NativeStableSelectionResolution.None
                    : NativeStableSelectionResolution.Unknown;
            }
        }

        TimeSpan resolutionElapsed = resolution.Elapsed;
        return new(
            captureId,
            state,
            processId,
            observedAtUtc,
            expectedSelectedMap,
            capturedRegion,
            SlotOffset,
            slotAddress,
            slotReadable,
            slotReadable ? rawValue : null,
            level1,
            level2,
            resolved.MapLabel,
            resolved.Kind,
            regionInvalidated,
            reacquisitionAttemptedNow,
            regionReacquired,
            oldRegionBase,
            newRegionBase,
            rapidReadElapsed,
            resolutionElapsed,
            total.Elapsed);
    }

    private NativeStableMemoryRegion? FindReplacementRegion(
        NativeStableMemoryRegion invalidatedRegion)
    {
        foreach (NativeStableMemoryRegion candidate in enumerateRegions())
        {
            if (candidate.Size != invalidatedRegion.Size ||
                candidate.State != invalidatedRegion.State ||
                candidate.Protect != invalidatedRegion.Protect ||
                candidate.Type != invalidatedRegion.Type ||
                candidate.Size <= SlotOffset)
            {
                continue;
            }

            nuint slot = candidate.BaseAddress + SlotOffset;
            if (!TryReadValue(memory, slot, out nuint raw))
            {
                continue;
            }

            if (ResolveChain(raw).IsKnown)
            {
                return candidate;
            }
        }

        return null;
    }

    private NativeStableSelectionResolution ResolveChain(nuint raw)
    {
        NativeStableSelectionResolution resolution = Resolve(raw);
        if (resolution.IsKnown || raw == 0 || !TryReadValue(memory, raw, out nuint first))
        {
            return resolution;
        }

        resolution = Resolve(first);
        if (resolution.IsKnown || first == 0 || !TryReadValue(memory, first, out nuint second))
        {
            return resolution;
        }

        return Resolve(second);
    }

    private NativeStableSelectionResolution Resolve(nuint value)
    {
        NativeStableCandidateTarget[] exact = targets.Targets
            .Where(target => target.Address == value)
            .ToArray();
        if (exact.Length > 0)
        {
            string[] maps = exact.Select(target => target.MapLabel)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (maps.Length == 1)
            {
                NativeStableCandidateTargetKind[] kinds = exact
                    .Where(target => target.MapLabel.Equals(maps[0], StringComparison.OrdinalIgnoreCase))
                    .Select(target => target.Kind).Distinct().ToArray();
                if (kinds.Length == 1)
                {
                    return new(maps[0], ToSelectionKind(kinds[0]));
                }

                return new(maps[0], NativeStableSelectionTargetKind.Unknown);
            }

            return NativeStableSelectionResolution.Unknown;
        }

        NativeStableCandidateNeighborhood[] neighborhoods = targets.Neighborhoods
            .Where(neighborhood => neighborhood.Contains(value))
            .ToArray();
        string[] neighborhoodMaps = neighborhoods.Select(item => item.MapLabel)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return neighborhoodMaps.Length == 1
            ? new(neighborhoodMaps[0], NativeStableSelectionTargetKind.CandidateNeighborhoodMatch)
            : NativeStableSelectionResolution.Unknown;
    }

    private static NativeStableSelectionTargetKind ToSelectionKind(
        NativeStableCandidateTargetKind kind) => kind switch
        {
            NativeStableCandidateTargetKind.ChecksumAddress =>
                NativeStableSelectionTargetKind.ChecksumAddress,
            NativeStableCandidateTargetKind.FilenameAddress =>
                NativeStableSelectionTargetKind.FilenameAddress,
            NativeStableCandidateTargetKind.CandidateAddressMinus0x08 =>
                NativeStableSelectionTargetKind.CandidateAddressMinus0x08,
            NativeStableCandidateTargetKind.CandidateAddressPlus0x48 =>
                NativeStableSelectionTargetKind.CandidateAddressPlus0x48,
            _ => NativeStableSelectionTargetKind.CandidateNeighborhoodMatch
        };

    private static bool TryReadValue(
        INativeStableMemoryReader memory,
        nuint address,
        out nuint value)
    {
        int pointerSize = memory.Is32Bit ? sizeof(uint) : sizeof(ulong);
        NativeStableMemoryReadResult read = memory.ReadBytes(address, pointerSize);
        if (!read.Complete || read.Data.Length != pointerSize)
        {
            value = 0;
            return false;
        }

        value = memory.Is32Bit
            ? BitConverter.ToUInt32(read.Data, 0)
            : checked((nuint)BitConverter.ToUInt64(read.Data, 0));
        return true;
    }
}

internal static class NativeStableSelectionSlotSequenceAnalyzer
{
    private static readonly string[] Expected = ["A", "B", "A", "B", "A"];

    internal static NativeStableSelectionSlotSequenceSummary Analyze(
        IReadOnlyList<NativeStableSelectionSlotSnapshot> snapshots)
    {
        string[] observed = snapshots.Select(snapshot => snapshot.ResolvedTargetMap).ToArray();
        bool sameProcess = snapshots.Count > 0 && snapshots
            .All(snapshot => snapshot.ProcessId == snapshots[0].ProcessId);
        bool sameRegion = snapshots.Count > 0 && snapshots
            .All(snapshot => snapshot.SourceRegion.BaseAddress == snapshots[0].SourceRegion.BaseAddress);
        bool sameSlot = snapshots.Count > 0 && snapshots
            .All(snapshot => snapshot.SlotAddress == snapshots[0].SlotAddress);
        bool exact = snapshots.Count == Expected.Length && sameProcess && sameRegion && sameSlot &&
            observed.SequenceEqual(Expected, StringComparer.OrdinalIgnoreCase);
        return new(observed, sameRegion, sameSlot, exact);
    }
}

internal static class NativeStableSelectionSlotRestartComparer
{
    internal static bool HasSameNormalizedShape(
        NativeStableSelectionSlotSnapshot beforeRestart,
        NativeStableSelectionSlotSnapshot afterRestart) =>
        beforeRestart.ProcessId != afterRestart.ProcessId &&
        beforeRestart.SourceOffsetWithinRegion == afterRestart.SourceOffsetWithinRegion &&
        beforeRestart.SourceRegion.Size == afterRestart.SourceRegion.Size &&
        beforeRestart.SourceRegion.State == afterRestart.SourceRegion.State &&
        beforeRestart.SourceRegion.Protect == afterRestart.SourceRegion.Protect &&
        beforeRestart.SourceRegion.Type == afterRestart.SourceRegion.Type &&
        beforeRestart.ResolvedTargetKind == afterRestart.ResolvedTargetKind;
}

internal static class NativeStableSelectionSlotCandidateBuilder
{
    internal const string NoCandidatesFailure =
        "No C.3 source with regional offset 0x5388 was observed.";

    internal static IReadOnlyList<NativeStableSelectionSlotCandidate> Build(
        NativeStableReferenceDiscoveryResult discovery)
    {
        IEnumerable<NativeStableSelectionSlotCandidate> direct = discovery.DirectReferences
            .Where(reference => IsStructurallyValid(
                reference.SourceRegion,
                reference.SourceAddress,
                reference.SourceOffsetWithinRegion))
            .Select(reference => new NativeStableSelectionSlotCandidate(
                reference.SourceRegion,
                reference.SourceAddress,
                reference.SourceOffsetWithinRegion,
                NativeStableSelectionSlotAcquisitionReferenceKind.DirectReference,
                reference.TargetMapLabel,
                ConvertKind(reference.TargetKind)));

        IEnumerable<NativeStableSelectionSlotCandidate> intermediate =
            discovery.IntermediateReferences
                .Where(reference => IsStructurallyValid(
                    reference.SourceRegion,
                    reference.SourceAddress,
                    reference.SourceOffsetWithinRegion))
                .Select(reference => new NativeStableSelectionSlotCandidate(
                    reference.SourceRegion,
                    reference.SourceAddress,
                    reference.SourceOffsetWithinRegion,
                    NativeStableSelectionSlotAcquisitionReferenceKind.IntermediateReadableReference,
                    reference.TargetMapLabel,
                    ConvertKind(reference.TargetKind)));

        return direct.Concat(intermediate)
            .GroupBy(candidate => new
            {
                candidate.SourceRegion,
                candidate.SlotAddress,
                candidate.SlotOffset,
                candidate.AcquisitionReferenceKind
            })
            .Select(group =>
            {
                string[] maps = group.Select(candidate => candidate.InitialResolvedTargetMap)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                NativeStableSelectionTargetKind[] kinds = group
                    .Select(candidate => candidate.InitialResolvedTargetKind)
                    .Distinct().ToArray();
                return new NativeStableSelectionSlotCandidate(
                    group.Key.SourceRegion,
                    group.Key.SlotAddress,
                    group.Key.SlotOffset,
                    group.Key.AcquisitionReferenceKind,
                    maps.Length == 1 ? maps[0] : "Unknown",
                    kinds.Length == 1 ? kinds[0] : NativeStableSelectionTargetKind.Unknown);
            })
            .OrderBy(candidate => candidate.SourceRegion.BaseAddress)
            .ThenBy(candidate => candidate.SlotAddress)
            .ThenBy(candidate => candidate.AcquisitionReferenceKind)
            .ThenBy(candidate => candidate.InitialResolvedTargetMap,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static string? GetFailure(
        IReadOnlyList<NativeStableSelectionSlotCandidate> candidates) =>
        candidates.Count == 0 ? NoCandidatesFailure : null;

    private static bool IsStructurallyValid(
        NativeStableMemoryRegion region,
        nuint sourceAddress,
        nuint sourceOffset) =>
        sourceOffset == NativeStableSelectionSlotReader.SlotOffset &&
        region.Size > sourceOffset &&
        region.Contains(sourceAddress) &&
        sourceAddress - region.BaseAddress == sourceOffset;

    private static NativeStableSelectionTargetKind ConvertKind(
        NativeStableCandidateTargetKind kind) => kind switch
        {
            NativeStableCandidateTargetKind.ChecksumAddress =>
                NativeStableSelectionTargetKind.ChecksumAddress,
            NativeStableCandidateTargetKind.FilenameAddress =>
                NativeStableSelectionTargetKind.FilenameAddress,
            NativeStableCandidateTargetKind.CandidateAddressMinus0x08 =>
                NativeStableSelectionTargetKind.CandidateAddressMinus0x08,
            NativeStableCandidateTargetKind.CandidateAddressPlus0x48 =>
                NativeStableSelectionTargetKind.CandidateAddressPlus0x48,
            _ => NativeStableSelectionTargetKind.CandidateNeighborhoodMatch
        };
}

internal sealed class NativeStableSelectionSlotCandidateSetReader
{
    private readonly IReadOnlyList<(NativeStableSelectionSlotCandidate Candidate,
        NativeStableSelectionSlotReader Reader)> readers;

    internal NativeStableSelectionSlotCandidateSetReader(
        INativeStableMemoryReader memory,
        IReadOnlyList<NativeStableSelectionSlotCandidate> candidates,
        NativeStableCandidateTargetSet targets,
        Func<IReadOnlyList<NativeStableMemoryRegion>>? enumerateRegions = null)
    {
        readers = candidates.Select(candidate => (
            candidate,
            new NativeStableSelectionSlotReader(
                memory, candidate.SourceRegion, targets, enumerateRegions)))
            .ToArray();
    }

    internal IReadOnlyList<NativeStableSelectionSlotCandidateCapture> Capture(
        int processId,
        string state,
        string expectedMap) =>
        readers.Select((entry, index) => new NativeStableSelectionSlotCandidateCapture(
            index + 1,
            entry.Candidate,
            entry.Reader.Capture(processId, state, expectedMap)))
        .ToArray();
}

internal sealed class NativeStableSelectionSlotSession : IDisposable
{
    private readonly NativeStableProcessMemory memory;
    private readonly NativeStableSelectionSlotCandidateSetReader readers;

    private NativeStableSelectionSlotSession(
        NativeStableProcessMemory memory,
        NativeStableSelectionSlotCandidateSetReader readers,
        NativeStableSelectionSlotAcquisition acquisition)
    {
        this.memory = memory;
        this.readers = readers;
        Acquisition = acquisition;
    }

    internal NativeStableSelectionSlotAcquisition Acquisition { get; }

    internal static NativeStableSelectionSlotSession? TryAcquire(
        IReadOnlyList<NativeStableKnownMapInput> knownMaps,
        string expectedSelectedMap,
        out NativeStableSelectionSlotAcquisition acquisition,
        CancellationToken cancellationToken = default)
    {
        var elapsed = Stopwatch.StartNew();
        NativeStableProbeResult discovery = NativeStablePoc.CaptureKnownMaps(
            knownMaps, expectedSelectedMap, cancellationToken);
        IReadOnlyList<NativeStableSelectionSlotCandidate> candidates =
            NativeStableSelectionSlotCandidateBuilder.Build(discovery.ReferenceDiscovery);

        if (discovery.ProcessId is null || discovery.ExecutablePath is null ||
            discovery.AttachmentStatus != NativeStableAttachmentStatus.AttachedReadOnly)
        {
            acquisition = new(discovery, candidates, elapsed.Elapsed,
                $"C.3 acquisition was unavailable: {discovery.AttachmentStatus}.");
            return null;
        }

        if (candidates.Count == 0)
        {
            acquisition = new(discovery, candidates, elapsed.Elapsed,
                NativeStableSelectionSlotCandidateBuilder.GetFailure(candidates));
            return null;
        }

        var candidate = new NativeStableProcessCandidate(
            discovery.ProcessId.Value,
            discovery.ExecutablePath,
            IsCandidateStable: true,
            NativeStableAttachmentStatus.Discovered);
        NativeStableAttachResult attachment = NativeStableProcessMemory.TryAttach(candidate);
        if (attachment.Memory is null)
        {
            acquisition = new(discovery, candidates, elapsed.Elapsed,
                $"Rapid read-only attachment failed: {attachment.Status}.");
            return null;
        }

        NativeStableProcessMemory memory = attachment.Memory;
        IReadOnlyList<NativeStableMemoryRegion> regions = memory.EnumerateReadableRegions();
        NativeStableCandidateTargetSet targets = NativeStableReferenceDiscovery.BuildTargetSet(
            discovery.CorrelationCandidates, regions);
        var slotReaders = new NativeStableSelectionSlotCandidateSetReader(
            memory, candidates, targets, memory.EnumerateReadableRegions);
        acquisition = new(discovery, candidates, elapsed.Elapsed, null);
        return new(memory, slotReaders, acquisition);
    }

    internal IReadOnlyList<NativeStableSelectionSlotCandidateCapture> Capture(
        string state,
        string expectedMap) =>
        readers.Capture(memory.ProcessId, state, expectedMap);

    public void Dispose() => memory.Dispose();
}

internal enum NativeStableSelectionTargetKind
{
    ChecksumAddress,
    FilenameAddress,
    CandidateAddressMinus0x08,
    CandidateAddressPlus0x48,
    CandidateNeighborhoodMatch,
    Unknown
}

internal readonly record struct NativeStableSelectionResolution(
    string MapLabel,
    NativeStableSelectionTargetKind Kind)
{
    internal static NativeStableSelectionResolution None { get; } =
        new("None", NativeStableSelectionTargetKind.Unknown);

    internal static NativeStableSelectionResolution Unknown { get; } =
        new("Unknown", NativeStableSelectionTargetKind.Unknown);

    internal bool IsKnown =>
        !MapLabel.Equals("None", StringComparison.Ordinal) &&
        !MapLabel.Equals("Unknown", StringComparison.Ordinal);
}

internal sealed record NativeStableSelectionSlotSnapshot(
    string CaptureId,
    string State,
    int ProcessId,
    DateTimeOffset ObservedAtUtc,
    string ExpectedSelectedMap,
    NativeStableMemoryRegion SourceRegion,
    nuint SourceOffsetWithinRegion,
    nuint SlotAddress,
    bool SlotReadable,
    nuint? RawSlotValue,
    nuint? DereferencedValueLevel1,
    nuint? DereferencedValueLevel2,
    string ResolvedTargetMap,
    NativeStableSelectionTargetKind ResolvedTargetKind,
    bool RegionInvalidated,
    bool ReacquisitionAttempted,
    bool RegionReacquired,
    nuint? OldRegionBase,
    nuint? NewRegionBase,
    TimeSpan RapidRead,
    TimeSpan Resolution,
    TimeSpan Total);

internal sealed record NativeStableSelectionSlotSequenceSummary(
    IReadOnlyList<string> ObservedSequence,
    bool SameRegionBaseAcrossSequence,
    bool SameSlotAddressAcrossSequence,
    bool ExactSelectionTrackingPattern);

internal enum NativeStableSelectionSlotAcquisitionReferenceKind
{
    DirectReference,
    IntermediateReadableReference
}

internal sealed record NativeStableSelectionSlotCandidate(
    NativeStableMemoryRegion SourceRegion,
    nuint SlotAddress,
    nuint SlotOffset,
    NativeStableSelectionSlotAcquisitionReferenceKind AcquisitionReferenceKind,
    string InitialResolvedTargetMap,
    NativeStableSelectionTargetKind InitialResolvedTargetKind);

internal sealed record NativeStableSelectionSlotCandidateCapture(
    int CandidateNumber,
    NativeStableSelectionSlotCandidate Candidate,
    NativeStableSelectionSlotSnapshot Snapshot);

internal sealed record NativeStableSelectionSlotAcquisition(
    NativeStableProbeResult Discovery,
    IReadOnlyList<NativeStableSelectionSlotCandidate> Candidates,
    TimeSpan InitialAcquisition,
    string? FailureReason);
