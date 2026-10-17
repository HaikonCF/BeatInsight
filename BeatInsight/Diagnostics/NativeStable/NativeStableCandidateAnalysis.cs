namespace BeatInsight.Diagnostics.NativeStable;

/// <summary>
/// EXPERIMENTAL — V3 PROOF OF CONCEPT — NOT PRODUCTION INTEGRATION.
/// Correlates only caller-supplied known strings. It does not assign runtime semantics.
/// </summary>
internal static class NativeStableCandidateCorrelationAnalyzer
{
    internal const long DefaultRelationshipWindow = 0x1000;

    internal static IReadOnlyList<NativeStableCorrelationCandidate> Analyze(
        IReadOnlyList<NativeStableProbeSignal> signals,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        long relationshipWindow = DefaultRelationshipWindow)
    {
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(regions);

        if (relationshipWindow < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(relationshipWindow));
        }

        var candidates = new List<NativeStableCorrelationCandidate>();

        foreach (IGrouping<string, NativeStableProbeSignal> mapSignals in signals.GroupBy(
                     signal => signal.MapLabel,
                     StringComparer.OrdinalIgnoreCase))
        {
            NativeStableProbeSignal[] sameMapSignals = mapSignals.ToArray();
            var checksumOccurrences = Occurrences(
                sameMapSignals,
                NativeStableKnownSignalKind.Checksum);
            var filenameOccurrences = Occurrences(
                sameMapSignals,
                NativeStableKnownSignalKind.Filename);
            var folderOccurrences = Occurrences(
                sameMapSignals,
                NativeStableKnownSignalKind.Folder);

            foreach (NativeStableKnownOccurrence checksum in checksumOccurrences)
            {
                foreach (NativeStableKnownOccurrence filename in filenameOccurrences)
                {
                    if (!TrySignedDelta(checksum.Address, filename.Address, out long delta) ||
                        Absolute(delta) > relationshipWindow)
                    {
                        continue;
                    }

                    NativeStableMemoryRegion? checksumRegion = FindRegion(regions, checksum.Address);
                    NativeStableMemoryRegion? filenameRegion = FindRegion(regions, filename.Address);
                    var nearbyFolders = new List<NativeStableFolderRelationship>();

                    foreach (NativeStableKnownOccurrence folder in folderOccurrences)
                    {
                        if (!TrySignedDelta(folder.Address, checksum.Address, out long checksumDelta) ||
                            !TrySignedDelta(folder.Address, filename.Address, out long filenameDelta) ||
                            Math.Min(Absolute(checksumDelta), Absolute(filenameDelta)) > relationshipWindow)
                        {
                            continue;
                        }

                        nearbyFolders.Add(new(
                            folder.Address,
                            checksumDelta,
                            filenameDelta,
                            folder.Encoding));
                    }

                    candidates.Add(new(
                        mapSignals.Key,
                        checksum.Address,
                        filename.Address,
                        delta,
                        checksum.Encoding,
                        filename.Encoding,
                        checksumRegion,
                        filenameRegion,
                        nearbyFolders
                            .OrderBy(folder => Math.Min(
                                Absolute(folder.ChecksumDelta),
                                Absolute(folder.FilenameDelta)))
                            .ThenBy(folder => folder.Address)
                            .ToArray()));
                }
            }
        }

        return candidates
            .OrderBy(candidate => candidate.MapLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.ChecksumAddress)
            .ThenBy(candidate => candidate.FilenameAddress)
            .ToArray();
    }

    internal static bool TrySignedDelta(nuint from, nuint to, out long delta)
    {
        if (to >= from)
        {
            nuint distance = to - from;
            if (distance > long.MaxValue)
            {
                delta = 0;
                return false;
            }

            delta = checked((long)distance);
            return true;
        }

        nuint reverseDistance = from - to;
        if (reverseDistance > long.MaxValue)
        {
            delta = 0;
            return false;
        }

        delta = -checked((long)reverseDistance);
        return true;
    }

    private static IReadOnlyList<NativeStableKnownOccurrence> Occurrences(
        IReadOnlyList<NativeStableProbeSignal> signals,
        NativeStableKnownSignalKind kind) =>
        signals
            .Where(signal => signal.Kind == kind)
            .SelectMany(signal => signal.Addresses.Select(address => new NativeStableKnownOccurrence(
                address,
                signal.Encoding)))
            .ToArray();

    private static NativeStableMemoryRegion? FindRegion(
        IReadOnlyList<NativeStableMemoryRegion> regions,
        nuint address)
    {
        foreach (NativeStableMemoryRegion region in regions)
        {
            if (region.Contains(address))
            {
                return region;
            }
        }

        return null;
    }

    private static long Absolute(long value) => value < 0 ? -value : value;

    private readonly record struct NativeStableKnownOccurrence(
        nuint Address,
        NativeStableTextEncoding Encoding);
}

/// <summary>
/// EXPERIMENTAL — V3 PROOF OF CONCEPT — NOT PRODUCTION INTEGRATION.
/// Performs one bounded, non-recursive pointer inspection around known candidates.
/// </summary>
internal static class NativeStableCandidateInspector
{
    private const nuint InspectionRadius = 0x80;
    private const int MaximumReportedRelationships = 32;

    internal static NativeStableCandidateInspectionResult Inspect(
        INativeStableMemoryReader memory,
        NativeStableCorrelationCandidate candidate,
        IReadOnlyList<NativeStableProbeSignal> signals,
        IReadOnlyList<NativeStableMemoryRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(regions);

        int pointerSize = memory.Is32Bit ? sizeof(uint) : sizeof(ulong);
        var knownTargets = signals
            .SelectMany(signal => signal.Addresses.Select(address => (address, signal)))
            .GroupBy(item => item.address)
            .ToDictionary(group => group.Key, group => group.First().signal);
        var observations = new List<NativeStablePointerRelationship>();
        int filteredStringPayloadPointerLikeValues = 0;

        IEnumerable<nuint> sourceAddresses = EnumerateAlignedWindow(
                candidate.ChecksumAddress,
                pointerSize)
            .Concat(EnumerateAlignedWindow(candidate.FilenameAddress, pointerSize))
            .Distinct();

        foreach (nuint sourceAddress in sourceAddresses)
        {
            if (!memory.TryReadPointer(sourceAddress, out nuint targetAddress) ||
                !regions.Any(region => region.Contains(targetAddress)) ||
                !NativeStableCandidateCorrelationAnalyzer.TrySignedDelta(
                    candidate.ChecksumAddress,
                    sourceAddress,
                    out long sourceDelta))
            {
                continue;
            }

            if (signals.Any(signal => signal.Addresses.Any(address =>
                    RangesOverlap(
                        sourceAddress,
                        pointerSize,
                        address,
                        signal.KnownBytes.Length))))
            {
                filteredStringPayloadPointerLikeValues++;
                continue;
            }

            knownTargets.TryGetValue(targetAddress, out NativeStableProbeSignal? targetSignal);
            if (targetSignal is not null)
            {
                NativeStableMemoryReadResult knownStringRead = memory.ReadBytes(
                    targetAddress,
                    targetSignal.KnownBytes.Length);
                if (!knownStringRead.Complete ||
                    !knownStringRead.Data.AsSpan().SequenceEqual(targetSignal.KnownBytes))
                {
                    targetSignal = null;
                }
            }

            observations.Add(new(
                sourceAddress,
                targetAddress,
                sourceDelta,
                targetSignal is null
                    ? NativeStablePointerClassification.ReadablePointerCandidate
                    : NativeStablePointerClassification.KnownStringTarget,
                targetSignal?.Kind,
                targetSignal?.Encoding,
                targetSignal?.MapLabel,
                ReferenceCount: 1,
                Alignment: checked((int)(sourceAddress % checked((nuint)pointerSize)))));
        }

        IReadOnlyDictionary<nuint, int> referenceCounts = observations
            .GroupBy(observation => observation.TargetAddress)
            .ToDictionary(group => group.Key, group => group.Count());

        NativeStablePointerRelationship[] reported = observations
            .Select(observation => observation with
            {
                ReferenceCount = referenceCounts[observation.TargetAddress]
            })
            .OrderBy(observation =>
                observation.Classification == NativeStablePointerClassification.KnownStringTarget
                    ? 0
                    : 1)
            .ThenByDescending(observation => observation.ReferenceCount)
            .ThenBy(observation => Math.Abs(observation.SourceDeltaFromChecksum))
            .Take(MaximumReportedRelationships)
            .ToArray();

        return new(reported, filteredStringPayloadPointerLikeValues);
    }

    private static IEnumerable<nuint> EnumerateAlignedWindow(
        nuint center,
        int pointerSize)
    {
        nuint start = center > InspectionRadius ? center - InspectionRadius : 0;
        nuint remainder = start % checked((nuint)pointerSize);
        if (remainder != 0)
        {
            start += checked((nuint)pointerSize) - remainder;
        }

        nuint end = center > nuint.MaxValue - InspectionRadius
            ? nuint.MaxValue
            : center + InspectionRadius;

        for (nuint address = start;
             address <= end && address <= nuint.MaxValue - checked((nuint)pointerSize);
             address += checked((nuint)pointerSize))
        {
            yield return address;
        }
    }

    private static bool RangesOverlap(
        nuint firstStart,
        int firstLength,
        nuint secondStart,
        int secondLength)
    {
        if (firstStart <= secondStart)
        {
            return secondStart - firstStart < checked((nuint)firstLength);
        }

        return firstStart - secondStart < checked((nuint)secondLength);
    }
}

internal enum NativeStableKnownSignalKind
{
    Checksum,
    Filename,
    Folder
}

internal enum NativeStableTextEncoding
{
    Ascii,
    Utf8,
    Utf16
}

internal enum NativeStablePointerClassification
{
    ReadablePointerCandidate,
    KnownStringTarget
}

internal sealed record NativeStableCorrelationCandidate(
    string MapLabel,
    nuint ChecksumAddress,
    nuint FilenameAddress,
    long Delta,
    NativeStableTextEncoding ChecksumEncoding,
    NativeStableTextEncoding FilenameEncoding,
    NativeStableMemoryRegion? ChecksumRegion,
    NativeStableMemoryRegion? FilenameRegion,
    IReadOnlyList<NativeStableFolderRelationship> NearbyKnownFolders)
{
    internal bool IsPlus50 => Delta == 0x50;

    internal bool SameRegion =>
        ChecksumRegion is not null &&
        FilenameRegion is not null &&
        ChecksumRegion.Value.BaseAddress == FilenameRegion.Value.BaseAddress;

    internal IReadOnlyList<NativeStablePointerRelationship> PointerRelationships { get; init; } = [];

    internal int FilteredStringPayloadPointerLikeValueCount { get; init; }
}

internal sealed record NativeStableFolderRelationship(
    nuint Address,
    long ChecksumDelta,
    long FilenameDelta,
    NativeStableTextEncoding Encoding);

internal sealed record NativeStablePointerRelationship(
    nuint SourceAddress,
    nuint TargetAddress,
    long SourceDeltaFromChecksum,
    NativeStablePointerClassification Classification,
    NativeStableKnownSignalKind? TargetKind,
    NativeStableTextEncoding? TargetEncoding,
    string? TargetMapLabel,
    int ReferenceCount,
    int Alignment);

internal sealed record NativeStableCandidateInspectionResult(
    IReadOnlyList<NativeStablePointerRelationship> Relationships,
    int FilteredStringPayloadPointerLikeValueCount);
