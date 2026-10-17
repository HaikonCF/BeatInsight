using System.Diagnostics;

namespace BeatInsight.Diagnostics.NativeStable;

/// <summary>
/// EXPERIMENTAL — V3 PROOF OF CONCEPT — NOT PRODUCTION INTEGRATION.
/// Independently derives two-level reference chains from an already validated X.
/// It contains no external signature, anchor, or displacement data.
/// </summary>
internal static class NativeStableRootReverseDiscovery
{
    private const int ChunkBytes = 1024 * 1024;
    private const int FingerprintRadius = 0x20;

    internal static NativeStableRootCalibration Calibrate(
        INativeStableMemoryReader memory,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        nuint groundTruthX,
        int presentedCandidateLimit = 32,
        CancellationToken cancellationToken = default)
    {
        if (!memory.Is32Bit)
        {
            throw new NotSupportedException("The NativeStable root diagnostic supports x86 only.");
        }

        var total = Stopwatch.StartNew();
        var phase = Stopwatch.StartNew();
        IReadOnlyList<nuint> qCandidates = FindAlignedReferences(
            memory, regions, [groundTruthX], cancellationToken);
        TimeSpan firstLevel = phase.Elapsed;

        phase.Restart();
        IReadOnlyList<NativeStableStoredReference> secondLevel = FindAlignedReferencesWithValues(
            memory, regions, qCandidates, cancellationToken);
        TimeSpan secondLevelTime = phase.Elapsed;

        phase.Restart();
        NativeStableRootChainCandidate[] chains = secondLevel
            .Select(reference => CreateChain(
                memory, regions, reference.Address, reference.Value, groundTruthX))
            .OrderBy(candidate => candidate.RootSlotAddress)
            .ThenBy(candidate => candidate.QCandidateAddress)
            .ToArray();
        TimeSpan metadata = phase.Elapsed;

        return new(
            groundTruthX,
            qCandidates,
            chains,
            Math.Max(0, presentedCandidateLimit),
            new(firstLevel, secondLevelTime, metadata, total.Elapsed));
    }

    internal static IReadOnlyList<nuint> FindAlignedReferences(
        INativeStableMemoryReader memory,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        IReadOnlyCollection<nuint> targets,
        CancellationToken cancellationToken = default) =>
        FindAlignedReferencesWithValues(memory, regions, targets, cancellationToken)
            .Select(reference => reference.Address)
            .Distinct()
            .OrderBy(address => address)
            .ToArray();

    internal static IReadOnlyList<NativeStableStoredReference> FindAlignedReferencesWithValues(
        INativeStableMemoryReader memory,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        IReadOnlyCollection<nuint> targets,
        CancellationToken cancellationToken)
    {
        if (targets.Count == 0)
        {
            return [];
        }

        HashSet<uint> targetValues = targets
            .Where(value => value <= uint.MaxValue)
            .Select(value => checked((uint)value))
            .ToHashSet();
        var references = new List<NativeStableStoredReference>();

        foreach (NativeStableMemoryRegion region in regions)
        {
            nuint regionOffset = 0;
            while (regionOffset < region.Size)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int requested = region.Size - regionOffset > ChunkBytes
                    ? ChunkBytes
                    : checked((int)(region.Size - regionOffset));
                nuint bufferAddress = region.BaseAddress + regionOffset;
                NativeStableMemoryReadResult read = memory.ReadBytes(bufferAddress, requested);
                if (read.BytesRead > 0)
                {
                    int first = AlignmentOffset(bufferAddress);
                    for (int index = first; index <= read.Data.Length - sizeof(uint);
                         index += sizeof(uint))
                    {
                        uint value = BitConverter.ToUInt32(read.Data, index);
                        if (targetValues.Contains(value))
                        {
                            references.Add(new(
                                bufferAddress + checked((nuint)index), value));
                        }
                    }
                }

                regionOffset += checked((nuint)requested);
            }
        }

        return references
            .Distinct()
            .OrderBy(reference => reference.Address)
            .ThenBy(reference => reference.Value)
            .ToArray();
    }

    private static NativeStableRootChainCandidate CreateChain(
        INativeStableMemoryReader memory,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        nuint rootSlot,
        nuint qCandidate,
        nuint groundTruthX)
    {
        NativeStableMemoryRegion? sRegion = FindRegion(regions, rootSlot);
        NativeStableMemoryRegion? qRegion = FindRegion(regions, qCandidate);
        NativeStableMemoryRegion? xRegion = FindRegion(regions, groundTruthX);
        return new(
            rootSlot,
            qCandidate,
            groundTruthX,
            Describe(rootSlot, sRegion),
            Describe(qCandidate, qRegion),
            Describe(groundTruthX, xRegion),
            sRegion is not null && qRegion is not null &&
                sRegion.Value.BaseAddress == qRegion.Value.BaseAddress,
            qRegion is not null && xRegion is not null &&
                qRegion.Value.BaseAddress == xRegion.Value.BaseAddress,
            BuildFingerprint(memory, sRegion, rootSlot));
    }

    private static NativeStableAddressMetadata Describe(
        nuint address,
        NativeStableMemoryRegion? region) =>
        region is null
            ? new(address, null, null, 0, 0, 0, 0, 0,
                checked((int)(address % sizeof(uint))), NativeStableRegionBacking.Unknown)
            : new(
                address,
                region.Value.BaseAddress,
                region.Value.AllocationBase == 0 ? null : region.Value.AllocationBase,
                region.Value.Size,
                region.Value.State,
                region.Value.Protect,
                region.Value.Type,
                address - region.Value.BaseAddress,
                checked((int)(address % sizeof(uint))),
                RegionBacking(region.Value.Type));

    private static NativeStableNeighborhoodFingerprint BuildFingerprint(
        INativeStableMemoryReader memory,
        NativeStableMemoryRegion? region,
        nuint rootSlot)
    {
        if (region is null)
        {
            return NativeStableNeighborhoodFingerprint.Unavailable;
        }

        nuint before = Math.Min(checked((nuint)FingerprintRadius),
            rootSlot - region.Value.BaseAddress);
        nuint regionEnd = region.Value.BaseAddress + region.Value.Size;
        nuint after = Math.Min(checked((nuint)FingerprintRadius), regionEnd - rootSlot);
        nuint start = rootSlot - before;
        nuint end = rootSlot + after;
        var observations = new List<NativeStableNeighborhoodObservation>();

        nuint aligned = start + checked((nuint)AlignmentOffset(start));
        for (nuint address = aligned; address <= end && end - address >= sizeof(uint);
             address += checked((nuint)sizeof(uint)))
        {
            NativeStableMemoryReadResult valueRead = memory.ReadBytes(address, sizeof(uint));
            if (!valueRead.Complete)
            {
                observations.Add(new(SignedDelta(rootSlot, address),
                    NativeStableNeighborhoodValueKind.UnknownValue));
                continue;
            }

            uint value = BitConverter.ToUInt32(valueRead.Data, 0);
            NativeStableNeighborhoodValueKind kind = value switch
            {
                0 => NativeStableNeighborhoodValueKind.Zero,
                <= 0xffff => NativeStableNeighborhoodValueKind.SmallInteger,
                _ when memory.ReadBytes(value, 1).BytesRead == 1 =>
                    NativeStableNeighborhoodValueKind.ReadablePointerCandidate,
                _ => NativeStableNeighborhoodValueKind.UnknownValue
            };
            observations.Add(new(SignedDelta(rootSlot, address), kind));
        }

        return new(
            rootSlot - region.Value.BaseAddress,
            region.Value.Size - (rootSlot - region.Value.BaseAddress),
            observations);
    }

    private static long SignedDelta(nuint origin, nuint address) =>
        address >= origin
            ? checked((long)(address - origin))
            : -checked((long)(origin - address));

    private static int AlignmentOffset(nuint address)
    {
        nuint remainder = address % sizeof(uint);
        return remainder == 0 ? 0 : checked(sizeof(uint) - (int)remainder);
    }

    private static NativeStableMemoryRegion? FindRegion(
        IReadOnlyList<NativeStableMemoryRegion> regions,
        nuint address) => regions
        .Where(region => region.Contains(address))
        .Select(region => (NativeStableMemoryRegion?)region)
        .FirstOrDefault();

    private static NativeStableRegionBacking RegionBacking(uint type) => type switch
    {
        NativeStableInterop.MemoryImage => NativeStableRegionBacking.ModuleBacked,
        NativeStableInterop.MemoryPrivate => NativeStableRegionBacking.Private,
        NativeStableInterop.MemoryMapped => NativeStableRegionBacking.Mapped,
        _ => NativeStableRegionBacking.Unknown
    };
}

internal static class NativeStableRootTransitionValidator
{
    internal static NativeStableRootTransitionCapture Capture(
        INativeStableMemoryReader memory,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        IReadOnlyList<NativeStableRootChainCandidate> candidates,
        NativeStableKnownBeatmapObjectMetadata expectedMap,
        string phase)
    {
        var timer = Stopwatch.StartNew();
        NativeStableRootCandidateTransition[] transitions = candidates
            .Select(candidate => CaptureCandidate(memory, regions, candidate, expectedMap, phase))
            .ToArray();
        return new(phase, transitions, timer.Elapsed);
    }

    internal static NativeStableRootTransitionSummary Summarize(
        IReadOnlyList<NativeStableRootChainCandidate> initialCandidates,
        IReadOnlyList<NativeStableRootTransitionCapture> captures)
    {
        string[] required = ["A1", "B1", "A2", "B2", "A3"];
        var survivals = new List<NativeStableRootCandidateSurvival>();
        foreach (NativeStableRootChainCandidate candidate in initialCandidates)
        {
            NativeStableRootCandidateTransition[] observations = captures
                .SelectMany(capture => capture.Candidates)
                .Where(item => item.RootSlotAddress == candidate.RootSlotAddress &&
                    item.InitialQCandidateAddress == candidate.QCandidateAddress)
                .ToArray();
            int valid = observations.Count(item => item.ExpectedMapMatches);
            bool complete = required.All(label => observations.Any(item =>
                item.Phase.Equals(label, StringComparison.Ordinal) && item.ExpectedMapMatches));
            NativeStableRootSurvival classification = complete
                ? NativeStableRootSurvival.SurvivesAllTransitions
                : valid == 0
                    ? NativeStableRootSurvival.Rejected
                    : NativeStableRootSurvival.Partial;
            survivals.Add(new(candidate.RootSlotAddress, candidate.QCandidateAddress,
                classification, observations));
        }

        int survivors = survivals.Count(item =>
            item.Classification == NativeStableRootSurvival.SurvivesAllTransitions);
        return new(
            survivals,
            survivors,
            survivors switch
            {
                0 => NativeStableRootSlotDiscovery.None,
                1 => NativeStableRootSlotDiscovery.Unique,
                _ => NativeStableRootSlotDiscovery.Ambiguous
            },
            captures.Select((capture, index) => new NativeStableRemainingCount(
                capture.Phase,
                initialCandidates.Count(candidate => captures.Take(index + 1).All(prior =>
                    prior.Candidates.Any(item =>
                        item.RootSlotAddress == candidate.RootSlotAddress &&
                        item.InitialQCandidateAddress == candidate.QCandidateAddress &&
                        item.ExpectedMapMatches))))).ToArray());
    }

    private static NativeStableRootCandidateTransition CaptureCandidate(
        INativeStableMemoryReader memory,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        NativeStableRootChainCandidate candidate,
        NativeStableKnownBeatmapObjectMetadata expectedMap,
        string phase)
    {
        bool pointerReadable = TryReadUInt32(memory, candidate.RootSlotAddress, out nuint q) && q != 0;
        nuint x = 0;
        bool finalObjectReadable = pointerReadable && TryReadUInt32(memory, q, out x) && x != 0;
        NativeStableBeatmapObjectCandidate? validation = finalObjectReadable
            ? NativeStableBeatmapObjectDiscovery.ValidateCandidate(
                memory, regions, expectedMap, x)
            : null;
        bool idsMatch = validation is not null &&
            (validation.AvailableOptionalIdCount == 0 ||
             validation.MatchedOptionalIdCount == validation.AvailableOptionalIdCount);
        bool expected = validation is not null && validation.MatchedCoreFieldCount == 3 && idsMatch;
        return new(
            phase,
            candidate.RootSlotAddress,
            candidate.QCandidateAddress,
            pointerReadable ? q : null,
            finalObjectReadable ? x : null,
            pointerReadable,
            finalObjectReadable,
            validation?.MatchedCoreFieldCount ?? 0,
            validation?.MatchedOptionalIdCount ?? 0,
            validation?.AvailableOptionalIdCount ?? expectedMap.AvailableIdCount,
            expected);
    }

    private static bool TryReadUInt32(
        INativeStableMemoryReader memory,
        nuint address,
        out nuint value)
    {
        NativeStableMemoryReadResult read = memory.ReadBytes(address, sizeof(uint));
        if (!read.Complete || read.Data.Length != sizeof(uint))
        {
            value = 0;
            return false;
        }
        value = BitConverter.ToUInt32(read.Data, 0);
        return true;
    }
}

internal static class NativeStableProcessGenerationGuard
{
    internal static void EnsureSame(
        int expectedProcessId,
        DateTimeOffset? expectedStartIdentity,
        int currentProcessId,
        DateTimeOffset? currentStartIdentity)
    {
        if (expectedProcessId != currentProcessId ||
            expectedStartIdentity != currentStartIdentity)
        {
            throw new NativeStableProcessGenerationChangedException(
                expectedProcessId, expectedStartIdentity,
                currentProcessId, currentStartIdentity);
        }
    }
}

internal sealed class NativeStableProcessGenerationChangedException : InvalidOperationException
{
    internal NativeStableProcessGenerationChangedException(
        int expectedProcessId,
        DateTimeOffset? expectedStartIdentity,
        int currentProcessId,
        DateTimeOffset? currentStartIdentity)
        : base($"osu! process generation changed: expected {expectedProcessId}/" +
            $"{expectedStartIdentity?.ToString("O") ?? "unavailable"}, observed " +
            $"{currentProcessId}/{currentStartIdentity?.ToString("O") ?? "unavailable"}.")
    {
    }
}

internal sealed record NativeStableRootCalibration(
    nuint GroundTruthX,
    IReadOnlyList<nuint> QCandidateAddresses,
    IReadOnlyList<NativeStableRootChainCandidate> Chains,
    int PresentedCandidateLimit,
    NativeStableRootCalibrationTimings Timings)
{
    internal int FirstLevelReferenceCount => QCandidateAddresses.Count;
    internal int SecondLevelRootSlotCount => Chains
        .Select(candidate => candidate.RootSlotAddress).Distinct().Count();
    internal int InitialChainCount => Chains.Count;
    internal IReadOnlyList<NativeStableRootChainCandidate> PresentedCandidates =>
        Chains.Take(PresentedCandidateLimit).ToArray();
}

internal sealed record NativeStableStoredReference(nuint Address, nuint Value);

internal sealed record NativeStableRootChainCandidate(
    nuint RootSlotAddress,
    nuint QCandidateAddress,
    nuint GroundTruthX,
    NativeStableAddressMetadata RootSlotMetadata,
    NativeStableAddressMetadata QCandidateMetadata,
    NativeStableAddressMetadata XMetadata,
    bool RootSlotAndQShareRegion,
    bool QAndXShareRegion,
    NativeStableNeighborhoodFingerprint NeighborhoodFingerprint);

internal sealed record NativeStableAddressMetadata(
    nuint Address,
    nuint? RegionBase,
    nuint? AllocationBase,
    nuint RegionSize,
    uint State,
    uint Protection,
    uint Type,
    nuint OffsetFromRegionBase,
    int Alignment,
    NativeStableRegionBacking Backing);

internal enum NativeStableRegionBacking { Unknown, Private, Mapped, ModuleBacked }

internal sealed record NativeStableNeighborhoodFingerprint(
    nuint DistanceFromRegionStart,
    nuint DistanceToRegionEnd,
    IReadOnlyList<NativeStableNeighborhoodObservation> Observations)
{
    internal static NativeStableNeighborhoodFingerprint Unavailable { get; } = new(0, 0, []);
}

internal sealed record NativeStableNeighborhoodObservation(
    long RelativeOffset,
    NativeStableNeighborhoodValueKind Kind);

internal enum NativeStableNeighborhoodValueKind
{
    Zero,
    SmallInteger,
    ReadablePointerCandidate,
    UnknownValue
}

internal sealed record NativeStableRootCalibrationTimings(
    TimeSpan FirstLevelReferenceSearch,
    TimeSpan SecondLevelReferenceSearch,
    TimeSpan CandidateMetadata,
    TimeSpan TotalCalibration);

internal sealed record NativeStableRootTransitionCapture(
    string Phase,
    IReadOnlyList<NativeStableRootCandidateTransition> Candidates,
    TimeSpan ValidationTime);

internal sealed record NativeStableRootCandidateTransition(
    string Phase,
    nuint RootSlotAddress,
    nuint InitialQCandidateAddress,
    nuint? CurrentQAddress,
    nuint? CurrentXAddress,
    bool PointerReadable,
    bool FinalObjectReadable,
    int MatchedCoreFields,
    int MatchedOptionalIds,
    int AvailableOptionalIds,
    bool ExpectedMapMatches);

internal sealed record NativeStableRootCandidateSurvival(
    nuint RootSlotAddress,
    nuint InitialQCandidateAddress,
    NativeStableRootSurvival Classification,
    IReadOnlyList<NativeStableRootCandidateTransition> Transitions);

internal enum NativeStableRootSurvival { Rejected, Partial, SurvivesAllTransitions }
internal enum NativeStableRootSlotDiscovery { None, Unique, Ambiguous }

internal sealed record NativeStableRemainingCount(string Phase, int Remaining);

internal sealed record NativeStableRootTransitionSummary(
    IReadOnlyList<NativeStableRootCandidateSurvival> Candidates,
    int SurvivingRootSlots,
    NativeStableRootSlotDiscovery RootSlotDiscovery,
    IReadOnlyList<NativeStableRemainingCount> RemainingAfterEachPhase);
