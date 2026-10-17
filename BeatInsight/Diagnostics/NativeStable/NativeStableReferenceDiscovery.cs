namespace BeatInsight.Diagnostics.NativeStable;

/// <summary>
/// EXPERIMENTAL — V3 PROOF OF CONCEPT — NOT PRODUCTION INTEGRATION.
/// Performs bounded reverse-reference discovery for already-observed candidates.
/// </summary>
internal static class NativeStableReferenceDiscovery
{
    private const int ChunkBytes = 1024 * 1024;
    private const nuint NeighborhoodRadius = 0x100;

    internal static NativeStableCandidateTargetSet BuildTargetSet(
        IReadOnlyList<NativeStableCorrelationCandidate> candidates,
        IReadOnlyList<NativeStableMemoryRegion> regions)
    {
        var targets = new List<NativeStableCandidateTarget>();
        var neighborhoods = new List<NativeStableCandidateNeighborhood>();

        foreach (IGrouping<string, NativeStableCorrelationCandidate> mapGroup in candidates.GroupBy(
                     candidate => candidate.MapLabel,
                     StringComparer.OrdinalIgnoreCase))
        {
            int candidateIndex = 0;
            foreach (NativeStableCorrelationCandidate candidate in mapGroup)
            {
                AddTarget(targets, regions, candidate, candidateIndex,
                    candidate.ChecksumAddress, NativeStableCandidateTargetKind.ChecksumAddress);
                AddTarget(targets, regions, candidate, candidateIndex,
                    candidate.FilenameAddress, NativeStableCandidateTargetKind.FilenameAddress);

                if (candidate.ChecksumAddress >= 0x08)
                {
                    AddTarget(targets, regions, candidate, candidateIndex,
                        candidate.ChecksumAddress - 0x08,
                        NativeStableCandidateTargetKind.CandidateAddressMinus0x08);
                }

                if (candidate.ChecksumAddress <= nuint.MaxValue - 0x48)
                {
                    AddTarget(targets, regions, candidate, candidateIndex,
                        candidate.ChecksumAddress + 0x48,
                        NativeStableCandidateTargetKind.CandidateAddressPlus0x48);
                }

                nuint lowerAnchor = Math.Min(candidate.ChecksumAddress, candidate.FilenameAddress);
                nuint upperAnchor = Math.Max(candidate.ChecksumAddress, candidate.FilenameAddress);
                nuint start = lowerAnchor >= NeighborhoodRadius
                    ? lowerAnchor - NeighborhoodRadius
                    : 0;
                nuint end = upperAnchor <= nuint.MaxValue - NeighborhoodRadius
                    ? upperAnchor + NeighborhoodRadius
                    : nuint.MaxValue;
                neighborhoods.Add(new(
                    candidate.MapLabel,
                    candidateIndex,
                    candidate.ChecksumAddress,
                    candidate.FilenameAddress,
                    start,
                    end));
                candidateIndex++;
            }
        }

        return new(targets, neighborhoods);
    }

    internal static NativeStableReferenceDiscoveryResult Search(
        INativeStableMemoryReader memory,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        NativeStableCandidateTargetSet targetSet,
        IReadOnlyList<NativeStableProbeSignal> knownSignals,
        CancellationToken cancellationToken = default)
    {
        int pointerSize = memory.Is32Bit ? sizeof(uint) : sizeof(ulong);
        var observations = new List<NativeStableReferenceObservation>();
        int filteredPayloadValues = 0;

        foreach (NativeStableMemoryRegion region in regions)
        {
            nuint offset = 0;
            while (offset < region.Size)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = region.Size - offset > ChunkBytes
                    ? ChunkBytes
                    : checked((int)(region.Size - offset));
                NativeStableMemoryReadResult read = memory.ReadBytes(
                    region.BaseAddress + offset,
                    count);
                if (read.BytesRead == 0)
                {
                    offset += checked((nuint)count);
                    continue;
                }

                NativeStableBufferReferenceResult bufferResult = ScanBuffer(
                    read.Data,
                    region.BaseAddress + offset,
                    region,
                    pointerSize,
                    targetSet,
                    knownSignals);
                observations.AddRange(bufferResult.Observations);
                filteredPayloadValues += bufferResult.FilteredStringPayloadPointerLikeValues;
                offset += checked((nuint)count);
            }
        }

        NativeStableReferenceObservation[] normalized = Normalize(observations);
        return new(
            normalized.Where(reference =>
                reference.Kind == NativeStableReferenceKind.DirectReference).ToArray(),
            normalized.Where(reference =>
                reference.Kind == NativeStableReferenceKind.CandidateNeighborhoodReference).ToArray(),
            [],
            filteredPayloadValues);
    }

    internal static NativeStableReferenceDiscoveryResult SearchOneIntermediateLevel(
        INativeStableMemoryReader memory,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        NativeStableReferenceDiscoveryResult directResult,
        IReadOnlyList<NativeStableProbeSignal> knownSignals,
        CancellationToken cancellationToken = default)
    {
        NativeStableReferenceObservation[] firstLevel = directResult.DirectReferences
            .Concat(directResult.NeighborhoodReferences)
            .ToArray();
        if (firstLevel.Length == 0)
        {
            return directResult;
        }

        int pointerSize = memory.Is32Bit ? sizeof(uint) : sizeof(ulong);
        var intermediate = new List<NativeStableIntermediateReference>();
        var byIntermediateAddress = firstLevel
            .GroupBy(reference => reference.SourceAddress)
            .ToDictionary(group => group.Key, group => group.ToArray());

        foreach (NativeStableMemoryRegion region in regions)
        {
            nuint offset = 0;
            while (offset < region.Size)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = region.Size - offset > ChunkBytes
                    ? ChunkBytes
                    : checked((int)(region.Size - offset));
                NativeStableMemoryReadResult read = memory.ReadBytes(
                    region.BaseAddress + offset,
                    count);
                if (read.BytesRead == 0)
                {
                    offset += checked((nuint)count);
                    continue;
                }

                foreach ((nuint sourceAddress, nuint value) in EnumeratePointerValues(
                             read.Data,
                             region.BaseAddress + offset,
                             pointerSize))
                {
                    if (!byIntermediateAddress.TryGetValue(
                            value,
                            out NativeStableReferenceObservation[]? firstLevelTargets) ||
                        SourceOverlapsKnownPayload(sourceAddress, pointerSize, knownSignals))
                    {
                        continue;
                    }

                    foreach (NativeStableReferenceObservation firstLevelTarget in firstLevelTargets)
                    {
                        intermediate.Add(new(
                            sourceAddress,
                            value,
                            firstLevelTarget.TargetAddress,
                            firstLevelTarget.TargetMapLabel,
                            firstLevelTarget.TargetKind,
                            firstLevelTarget.CandidateIndex,
                            region,
                            sourceAddress - region.BaseAddress,
                            checked((int)(sourceAddress % checked((nuint)pointerSize))),
                            Depth: 2));
                    }
                }

                offset += checked((nuint)count);
            }
        }

        return directResult with
        {
            IntermediateReferences = intermediate
                .Distinct()
                .OrderBy(reference => reference.SourceAddress)
                .ThenBy(reference => reference.IntermediateAddress)
                .ToArray()
        };
    }

    internal static NativeStableBufferReferenceResult ScanBuffer(
        byte[] buffer,
        nuint bufferAddress,
        NativeStableMemoryRegion sourceRegion,
        int pointerSize,
        NativeStableCandidateTargetSet targetSet,
        IReadOnlyList<NativeStableProbeSignal> knownSignals)
    {
        if (pointerSize is not (sizeof(uint) or sizeof(ulong)))
        {
            throw new ArgumentOutOfRangeException(nameof(pointerSize));
        }

        var targetsByAddress = targetSet.Targets
            .GroupBy(target => target.Address)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var observations = new List<NativeStableReferenceObservation>();
        int filteredPayloadValues = 0;

        foreach ((nuint sourceAddress, nuint value) in EnumeratePointerValues(
                     buffer,
                     bufferAddress,
                     pointerSize))
        {
            NativeStableCandidateTarget[] directTargets =
                targetsByAddress.GetValueOrDefault(value) ?? [];
            NativeStableCandidateNeighborhood[] neighborhoods = directTargets.Length == 0
                ? targetSet.Neighborhoods.Where(neighborhood => neighborhood.Contains(value)).ToArray()
                : [];

            if (directTargets.Length == 0 && neighborhoods.Length == 0)
            {
                continue;
            }

            if (SourceOverlapsKnownPayload(sourceAddress, pointerSize, knownSignals))
            {
                filteredPayloadValues++;
                continue;
            }

            foreach (NativeStableCandidateTarget target in directTargets)
            {
                observations.Add(CreateObservation(
                    NativeStableReferenceKind.DirectReference,
                    sourceAddress,
                    value,
                    target.MapLabel,
                    target.Kind,
                    target.CandidateIndex,
                    target.ChecksumAddress,
                    target.FilenameAddress,
                    sourceRegion,
                    pointerSize));
            }

            foreach (NativeStableCandidateNeighborhood neighborhood in neighborhoods)
            {
                observations.Add(CreateObservation(
                    NativeStableReferenceKind.CandidateNeighborhoodReference,
                    sourceAddress,
                    value,
                    neighborhood.MapLabel,
                    NativeStableCandidateTargetKind.CandidateNeighborhoodMatch,
                    neighborhood.CandidateIndex,
                    neighborhood.ChecksumAddress,
                    neighborhood.FilenameAddress,
                    sourceRegion,
                    pointerSize));
            }
        }

        return new(observations, filteredPayloadValues);
    }

    private static NativeStableReferenceObservation CreateObservation(
        NativeStableReferenceKind kind,
        nuint sourceAddress,
        nuint targetAddress,
        string mapLabel,
        NativeStableCandidateTargetKind targetKind,
        int candidateIndex,
        nuint checksumAddress,
        nuint filenameAddress,
        NativeStableMemoryRegion sourceRegion,
        int pointerSize)
    {
        NativeStableCandidateCorrelationAnalyzer.TrySignedDelta(
            checksumAddress, targetAddress, out long checksumDelta);
        NativeStableCandidateCorrelationAnalyzer.TrySignedDelta(
            filenameAddress, targetAddress, out long filenameDelta);
        return new(
            kind,
            sourceAddress,
            targetAddress,
            mapLabel,
            targetKind,
            candidateIndex,
            sourceRegion,
            sourceAddress - sourceRegion.BaseAddress,
            checked((int)(sourceAddress % checked((nuint)pointerSize))),
            checksumDelta,
            filenameDelta,
            OccurrenceCount: 1);
    }

    private static NativeStableReferenceObservation[] Normalize(
        IReadOnlyList<NativeStableReferenceObservation> observations) =>
        observations
            .GroupBy(reference => new
            {
                reference.Kind,
                reference.SourceAddress,
                reference.TargetAddress,
                reference.TargetMapLabel,
                reference.TargetKind,
                reference.CandidateIndex
            })
            .Select(group => group.First() with { OccurrenceCount = group.Count() })
            .OrderBy(reference => reference.SourceAddress)
            .ThenBy(reference => reference.TargetMapLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.TargetAddress)
            .ToArray();

    private static IEnumerable<(nuint SourceAddress, nuint Value)> EnumeratePointerValues(
        byte[] buffer,
        nuint bufferAddress,
        int pointerSize)
    {
        nuint remainder = bufferAddress % checked((nuint)pointerSize);
        int firstOffset = remainder == 0
            ? 0
            : checked(pointerSize - (int)remainder);

        for (int offset = firstOffset; offset <= buffer.Length - pointerSize; offset += pointerSize)
        {
            nuint value = pointerSize == sizeof(uint)
                ? BitConverter.ToUInt32(buffer, offset)
                : checked((nuint)BitConverter.ToUInt64(buffer, offset));
            yield return (bufferAddress + checked((nuint)offset), value);
        }
    }

    private static bool SourceOverlapsKnownPayload(
        nuint sourceAddress,
        int pointerSize,
        IReadOnlyList<NativeStableProbeSignal> knownSignals) =>
        knownSignals.Any(signal => signal.Addresses.Any(address =>
            RangesOverlap(sourceAddress, pointerSize, address, signal.KnownBytes.Length)));

    private static bool RangesOverlap(
        nuint firstStart,
        int firstLength,
        nuint secondStart,
        int secondLength) =>
        firstStart <= secondStart
            ? secondStart - firstStart < checked((nuint)firstLength)
            : firstStart - secondStart < checked((nuint)secondLength);

    private static void AddTarget(
        ICollection<NativeStableCandidateTarget> targets,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        NativeStableCorrelationCandidate candidate,
        int candidateIndex,
        nuint address,
        NativeStableCandidateTargetKind kind)
    {
        if (!regions.Any(region => region.Contains(address)))
        {
            return;
        }

        targets.Add(new(
            candidate.MapLabel,
            candidateIndex,
            kind,
            address,
            candidate.ChecksumAddress,
            candidate.FilenameAddress));
    }
}

internal static class NativeStableReferenceSnapshotComparer
{
    internal static IReadOnlyList<NativeStableReferenceTransition> Compare(
        NativeStableReferenceSnapshot first,
        NativeStableReferenceSnapshot second,
        NativeStableReferenceSnapshot third)
    {
        nuint[] sources = first.References.Select(reference => reference.SourceAddress)
            .Concat(second.References.Select(reference => reference.SourceAddress))
            .Concat(third.References.Select(reference => reference.SourceAddress))
            .Distinct()
            .OrderBy(address => address)
            .ToArray();
        var transitions = new List<NativeStableReferenceTransition>();

        foreach (nuint source in sources)
        {
            string[] firstTargets = Targets(first, source);
            string[] secondTargets = Targets(second, source);
            string[] thirdTargets = Targets(third, source);
            NativeStableReferenceTransitionKind kind;

            if (firstTargets.Length > 1 || secondTargets.Length > 1 || thirdTargets.Length > 1)
            {
                kind = NativeStableReferenceTransitionKind.MultipleTargetReference;
            }
            else if (firstTargets.Length == 0 || secondTargets.Length == 0 || thirdTargets.Length == 0)
            {
                kind = NativeStableReferenceTransitionKind.DisappearingReference;
            }
            else if (firstTargets[0].Equals(thirdTargets[0], StringComparison.OrdinalIgnoreCase) &&
                     !firstTargets[0].Equals(secondTargets[0], StringComparison.OrdinalIgnoreCase))
            {
                kind = NativeStableReferenceTransitionKind.SwitchingReference;
            }
            else if (firstTargets[0].Equals("A", StringComparison.OrdinalIgnoreCase) &&
                     secondTargets[0].Equals("A", StringComparison.OrdinalIgnoreCase) &&
                     thirdTargets[0].Equals("A", StringComparison.OrdinalIgnoreCase))
            {
                kind = NativeStableReferenceTransitionKind.StaticAReference;
            }
            else if (firstTargets[0].Equals("B", StringComparison.OrdinalIgnoreCase) &&
                     secondTargets[0].Equals("B", StringComparison.OrdinalIgnoreCase) &&
                     thirdTargets[0].Equals("B", StringComparison.OrdinalIgnoreCase))
            {
                kind = NativeStableReferenceTransitionKind.StaticBReference;
            }
            else
            {
                kind = NativeStableReferenceTransitionKind.AmbiguousReference;
            }

            transitions.Add(new(source, kind, firstTargets, secondTargets, thirdTargets));
        }

        return transitions;
    }

    private static string[] Targets(NativeStableReferenceSnapshot snapshot, nuint source) =>
        snapshot.References
            .Where(reference => reference.SourceAddress == source)
            .Select(reference => reference.TargetMapLabel)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

internal enum NativeStableCandidateTargetKind
{
    ChecksumAddress,
    FilenameAddress,
    CandidateAddressMinus0x08,
    CandidateAddressPlus0x48,
    CandidateNeighborhoodMatch
}

internal enum NativeStableReferenceKind
{
    DirectReference,
    CandidateNeighborhoodReference
}

internal enum NativeStableReferenceTransitionKind
{
    StaticAReference,
    StaticBReference,
    SwitchingReference,
    DisappearingReference,
    MultipleTargetReference,
    AmbiguousReference
}

internal sealed record NativeStableCandidateTarget(
    string MapLabel,
    int CandidateIndex,
    NativeStableCandidateTargetKind Kind,
    nuint Address,
    nuint ChecksumAddress,
    nuint FilenameAddress);

internal sealed record NativeStableCandidateNeighborhood(
    string MapLabel,
    int CandidateIndex,
    nuint ChecksumAddress,
    nuint FilenameAddress,
    nuint StartAddress,
    nuint EndAddress)
{
    internal bool Contains(nuint address) =>
        address >= StartAddress && address <= EndAddress;
}

internal sealed record NativeStableCandidateTargetSet(
    IReadOnlyList<NativeStableCandidateTarget> Targets,
    IReadOnlyList<NativeStableCandidateNeighborhood> Neighborhoods);

internal sealed record NativeStableReferenceObservation(
    NativeStableReferenceKind Kind,
    nuint SourceAddress,
    nuint TargetAddress,
    string TargetMapLabel,
    NativeStableCandidateTargetKind TargetKind,
    int CandidateIndex,
    NativeStableMemoryRegion SourceRegion,
    nuint SourceOffsetWithinRegion,
    int SourceAlignment,
    long TargetDeltaFromChecksum,
    long TargetDeltaFromFilename,
    int OccurrenceCount);

internal sealed record NativeStableIntermediateReference(
    nuint SourceAddress,
    nuint IntermediateAddress,
    nuint CandidateTargetAddress,
    string TargetMapLabel,
    NativeStableCandidateTargetKind TargetKind,
    int CandidateIndex,
    NativeStableMemoryRegion SourceRegion,
    nuint SourceOffsetWithinRegion,
    int SourceAlignment,
    int Depth);

internal sealed record NativeStableReferenceDiscoveryResult(
    IReadOnlyList<NativeStableReferenceObservation> DirectReferences,
    IReadOnlyList<NativeStableReferenceObservation> NeighborhoodReferences,
    IReadOnlyList<NativeStableIntermediateReference> IntermediateReferences,
    int FilteredStringPayloadPointerLikeValues)
{
    internal static NativeStableReferenceDiscoveryResult Empty { get; } =
        new([], [], [], 0);
}

internal sealed record NativeStableBufferReferenceResult(
    IReadOnlyList<NativeStableReferenceObservation> Observations,
    int FilteredStringPayloadPointerLikeValues);

internal sealed record NativeStableReferenceSnapshot(
    string CaptureId,
    string? ExpectedSelectedMapLabel,
    IReadOnlyList<NativeStableReferenceObservation> References);

internal sealed record NativeStableReferenceTransition(
    nuint SourceAddress,
    NativeStableReferenceTransitionKind Kind,
    IReadOnlyList<string> FirstTargets,
    IReadOnlyList<string> SecondTargets,
    IReadOnlyList<string> ThirdTargets);
