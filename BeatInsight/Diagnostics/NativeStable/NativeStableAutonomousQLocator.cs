using System.Diagnostics;
using System.IO;

namespace BeatInsight.Diagnostics.NativeStable;

internal enum NativeStableAutonomousQDiscovery { None, Unique, Ambiguous, Unavailable }

internal sealed record NativeStableAutonomousQProfile(
    int SchemaVersion,
    NativeStableRegionBacking Backing,
    uint Protection,
    uint Type,
    int Alignment,
    ulong RegionSize,
    ulong[] ObservedAllocationOffsets,
    ulong[] ObservedRegionOffsets,
    NativeStableStructuralFingerprint Neighborhood,
    double MinimumNeighborhoodSimilarity)
{
    internal static NativeStableAutonomousQProfile FromCrossProcessEvidence(
        NativeStableRootStructuralProfile generationA,
        NativeStableQRestartExperimentSession restart,
        NativeStableQRestartComparison comparison)
    {
        generationA.Validate();
        NativeStableQRestartExperiment.Validate(restart);
        NativeStableRestartGeneration generationB = restart.GenerationB ??
            throw new InvalidDataException("Cross-process Q evidence is incomplete.");
        if (restart.NextPhase != NativeStableQRestartPhase.Finalize ||
            comparison.GenerationAProcessId != restart.GenerationA.ProcessId ||
            comparison.GenerationAStart != restart.GenerationA.ProcessStartIdentity ||
            comparison.GenerationBProcessId != generationB.ProcessId ||
            comparison.GenerationBStart != generationB.ProcessStartIdentity)
        {
            throw new InvalidDataException(
                "Cross-process Q session and comparison identities do not match.");
        }
        NativeStableRestartCandidateComparison? best = comparison.CandidateComparisons
            .OrderByDescending(candidate => candidate.StructuralInvariants.Count(item =>
                item.Status == NativeStableInvariantStatus.ReproducedExactly))
            .ThenBy(candidate => candidate.QAddress)
            .FirstOrDefault();
        ulong[] allocationOffsets = new[]
        {
            generationA.Q.OffsetFromAllocationBase,
            best?.CurrentCapture.QMetadata.OffsetFromAllocationBase
        }.Where(value => value is not null).Select(value => value!.Value).Distinct().ToArray();
        ulong[] regionOffsets = new[]
        {
            (ulong?)generationA.Q.OffsetFromRegionBase,
            best is null ? null : best.CurrentCapture.QMetadata.OffsetFromRegionBase
        }.Where(value => value is not null).Select(value => value!.Value).Distinct().ToArray();
        return new(
            1,
            generationA.Q.Backing,
            generationA.Q.Protection,
            generationA.Q.Type,
            generationA.Q.Alignment,
            generationA.Q.RegionSize,
            allocationOffsets,
            regionOffsets,
            generationA.QNeighborhood,
            0.80);
    }

    internal void Validate()
    {
        if (SchemaVersion != 1 || ObservedAllocationOffsets is null ||
            ObservedAllocationOffsets.Length == 0 || ObservedRegionOffsets is null ||
            Neighborhood is null || MinimumNeighborhoodSimilarity is < 0 or > 1)
        {
            throw new InvalidDataException("Autonomous Q profile is malformed.");
        }
    }
}

internal sealed record NativeStableAutonomousQLocatorOptions(
    double MinimumStructuralScore = 45,
    double MinimumFinalScore = 75,
    double UniqueMargin = 12,
    int FallbackRadius = 0x4000,
    bool EnableFallback = true,
    bool EnableHeavyTopologyDiagnostics = false);

internal sealed record NativeStableAutonomousQCandidate(
    ulong QAddress,
    ulong? XAddress,
    NativeStableObservedAddress Metadata,
    NativeStableStructuralFingerprint Neighborhood,
    double NeighborhoodSimilarity,
    bool ExactObservedAllocationOffset,
    bool ExactObservedRegionOffset,
    bool PointerReadable,
    bool XReadable,
    NativeStableAutonomousBeatmapObjectEvidence ObjectEvidence,
    double StructuralScore,
    double FinalScore,
    string DiscoveryPath);

internal sealed record NativeStableAutonomousBeatmapObjectEvidence(
    bool ChecksumValid,
    bool FolderValid,
    bool FilenameValid,
    string? Checksum,
    string? Folder,
    string? Filename,
    int? BeatmapId,
    int? BeatmapSetId,
    int ValidCoreFields,
    int AvailableIds,
    int PlausibleIds,
    bool IsValid);

internal sealed record NativeStableAutonomousQLocatorTimings(
    long ProcessAttachTicks,
    long CandidateRegionEnumerationTicks,
    long CandidateGenerationTicks,
    long NeighborhoodValidationTicks,
    long ObjectValidationTicks,
    long DecisionTicks,
    long TransitionValidationTicks,
    long TotalTicks);

internal sealed record NativeStableAutonomousQLocatorResult(
    int ProcessId,
    DateTimeOffset ProcessStartIdentity,
    NativeStableAutonomousQDiscovery Discovery,
    ulong? SelectedQAddress,
    NativeStableAutonomousQCandidate[] Candidates,
    bool UsedCache,
    bool CacheRevalidated,
    bool UsedFallback,
    bool HeavyTopologyScanPerformed,
    NativeStableAutonomousQLocatorTimings Timings);

internal static class NativeStableAutonomousBeatmapObjectValidator
{
    internal static NativeStableAutonomousBeatmapObjectEvidence Validate(
        INativeStableMemoryReader memory,
        nuint address)
    {
        bool checksumReadable = TryReadField(memory, address,
            NativeStableBeatmapObjectDiscovery.ChecksumOffset, out string? checksum);
        bool folderReadable = TryReadField(memory, address,
            NativeStableBeatmapObjectDiscovery.FolderOffset, out string? folder);
        bool filenameReadable = TryReadField(memory, address,
            NativeStableBeatmapObjectDiscovery.FilenameOffset, out string? filename);
        bool checksumValid = checksumReadable && checksum!.Length == 32 &&
            checksum.All(Uri.IsHexDigit);
        bool folderValid = folderReadable && !string.IsNullOrWhiteSpace(folder) &&
            folder.Length <= 255;
        bool filenameValid = filenameReadable && filename!.Length is > 4 and <= 255 &&
            filename.EndsWith(".osu", StringComparison.OrdinalIgnoreCase);
        int? beatmapId = TryReadInt32(
            memory, address + NativeStableBeatmapObjectDiscovery.BeatmapIdOffset);
        int? beatmapSetId = TryReadInt32(
            memory, address + NativeStableBeatmapObjectDiscovery.BeatmapSetIdOffset);
        int available = new[] { beatmapId, beatmapSetId }.Count(value => value is > 0);
        int plausible = new[] { beatmapId, beatmapSetId }.Count(value => value is > 0);
        bool idsConsistent = available is 0 or 2;
        int core = new[] { checksumValid, folderValid, filenameValid }.Count(value => value);
        return new(
            checksumValid, folderValid, filenameValid,
            checksumValid ? checksum : null,
            folderValid ? folder : null,
            filenameValid ? filename : null,
            beatmapId, beatmapSetId,
            core, available, plausible,
            core == 3 && idsConsistent && plausible == available);
    }

    private static bool TryReadField(
        INativeStableMemoryReader memory,
        nuint objectAddress,
        nuint offset,
        out string? value)
    {
        value = null;
        if (!memory.TryReadPointer(objectAddress + offset, out nuint pointer) || pointer == 0 ||
            !NativeStableManagedStringReader.TryRead(memory, pointer, out string text))
        {
            return false;
        }
        value = text;
        return true;
    }

    private static int? TryReadInt32(INativeStableMemoryReader memory, nuint address)
    {
        NativeStableMemoryReadResult read = memory.ReadBytes(address, sizeof(int));
        return read.Complete && read.Data.Length == sizeof(int)
            ? BitConverter.ToInt32(read.Data, 0)
            : null;
    }
}

internal sealed class NativeStableAutonomousQLocator
{
    private NativeStableAutonomousQCacheEntry? cache;

    internal NativeStableAutonomousQLocatorResult Locate(
        INativeStableMemoryReader memory,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        IReadOnlyList<NativeStableModuleRange> modules,
        int processId,
        DateTimeOffset processStartIdentity,
        NativeStableAutonomousQProfile profile,
        NativeStableAutonomousQLocatorOptions? options = null)
    {
        options ??= new();
        profile.Validate();
        var total = Stopwatch.StartNew();
        var phase = Stopwatch.StartNew();
        NativeStableMemoryRegion[] candidateRegions = regions.Where(region =>
            region.State == NativeStableInterop.MemoryCommit &&
            region.Type == profile.Type &&
            region.Protect == profile.Protection &&
            RegionBacking(region.Type) == profile.Backing).ToArray();
        long regionTicks = phase.Elapsed.Ticks;

        if (cache is not null && cache.ProcessId == processId &&
            cache.ProcessStartIdentity == processStartIdentity)
        {
            NativeStableAutonomousQCandidate? cached = Evaluate(
                memory, candidateRegions, modules, checked((nuint)cache.QAddress),
                profile, options, "Cache", out long neighborhoodTicks,
                out long objectTicks);
            if (cached is not null && cached.ObjectEvidence.IsValid &&
                cached.FinalScore >= options.MinimumFinalScore)
            {
                return new(processId, processStartIdentity,
                    NativeStableAutonomousQDiscovery.Unique, cached.QAddress,
                    [cached], true, true, false, false,
                    new(0, regionTicks, 0, neighborhoodTicks, objectTicks, 0, 0,
                        total.Elapsed.Ticks));
            }
            cache = null;
        }
        else if (cache is not null)
        {
            cache = null;
        }

        phase.Restart();
        nuint[] exactAddresses = CandidateAddresses(candidateRegions, profile);
        long generationTicks = phase.Elapsed.Ticks;
        var candidates = new List<NativeStableAutonomousQCandidate>();
        long neighborhoodTotal = 0;
        long objectTotal = 0;
        foreach (nuint address in exactAddresses)
        {
            NativeStableAutonomousQCandidate? candidate = Evaluate(
                memory, candidateRegions, modules, address, profile, options,
                "ObservedOffset", out long neighborhoodTicks, out long objectTicks);
            neighborhoodTotal += neighborhoodTicks;
            objectTotal += objectTicks;
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }
        }

        bool usedFallback = false;
        if (!candidates.Any(item => item.ObjectEvidence.IsValid) && options.EnableFallback)
        {
            usedFallback = true;
            foreach (nuint address in FallbackAddresses(candidateRegions, profile, options)
                         .Except(exactAddresses))
            {
                NativeStableAutonomousQCandidate? candidate = Evaluate(
                    memory, candidateRegions, modules, address, profile, options,
                    "BoundedFingerprintFallback", out long neighborhoodTicks,
                    out long objectTicks);
                neighborhoodTotal += neighborhoodTicks;
                objectTotal += objectTicks;
                if (candidate is not null)
                {
                    candidates.Add(candidate);
                }
            }
        }

        phase.Restart();
        NativeStableAutonomousQCandidate[] valid = candidates
            .Where(item => item.ObjectEvidence.IsValid &&
                item.FinalScore >= options.MinimumFinalScore)
            .OrderByDescending(item => item.FinalScore)
            .ThenBy(item => item.QAddress)
            .ToArray();
        NativeStableAutonomousQDiscovery discovery;
        ulong? selected = null;
        if (valid.Length == 0)
        {
            discovery = NativeStableAutonomousQDiscovery.None;
        }
        else if (valid.Length == 1 ||
            valid[0].FinalScore - valid[1].FinalScore >= options.UniqueMargin)
        {
            discovery = NativeStableAutonomousQDiscovery.Unique;
            selected = valid[0].QAddress;
            cache = new(processId, processStartIdentity, selected.Value);
        }
        else
        {
            discovery = NativeStableAutonomousQDiscovery.Ambiguous;
        }
        long decisionTicks = phase.Elapsed.Ticks;
        return new(
            processId, processStartIdentity, discovery, selected,
            candidates.OrderByDescending(item => item.FinalScore)
                .ThenBy(item => item.QAddress).ToArray(),
            false, false, usedFallback, false,
            new(0, regionTicks, generationTicks, neighborhoodTotal, objectTotal,
                decisionTicks, 0, total.Elapsed.Ticks));
    }

    internal void SeedCacheForTesting(
        int processId, DateTimeOffset processStartIdentity, ulong qAddress) =>
        cache = new(processId, processStartIdentity, qAddress);

    private static nuint[] CandidateAddresses(
        IReadOnlyList<NativeStableMemoryRegion> regions,
        NativeStableAutonomousQProfile profile) => regions
        .SelectMany(region => profile.ObservedAllocationOffsets.Select(offset =>
            region.AllocationBase + checked((nuint)offset)))
        .Where(address => regions.Any(region => region.Contains(address)))
        .Distinct().OrderBy(address => address).ToArray();

    private static IEnumerable<nuint> FallbackAddresses(
        IReadOnlyList<NativeStableMemoryRegion> regions,
        NativeStableAutonomousQProfile profile,
        NativeStableAutonomousQLocatorOptions options)
    {
        foreach (IGrouping<nuint, NativeStableMemoryRegion> allocation in regions
                     .GroupBy(region => region.AllocationBase))
        {
            foreach (ulong observed in profile.ObservedAllocationOffsets)
            {
                long center = checked((long)observed);
                for (long delta = -options.FallbackRadius;
                     delta <= options.FallbackRadius; delta += sizeof(uint))
                {
                    long offset = center + delta;
                    if (offset < 0)
                    {
                        continue;
                    }
                    nuint address = allocation.Key + checked((nuint)offset);
                    if (allocation.Any(region => region.Contains(address)))
                    {
                        yield return address;
                    }
                }
            }
        }
    }

    private static NativeStableAutonomousQCandidate? Evaluate(
        INativeStableMemoryReader memory,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        IReadOnlyList<NativeStableModuleRange> modules,
        nuint q,
        NativeStableAutonomousQProfile profile,
        NativeStableAutonomousQLocatorOptions options,
        string discoveryPath,
        out long neighborhoodTicks,
        out long objectTicks)
    {
        NativeStableObservedAddress metadata =
            NativeStableRootCharacterizer.Describe(q, regions, modules);
        bool exactAllocation = metadata.OffsetFromAllocationBase is not null &&
            profile.ObservedAllocationOffsets.Contains(metadata.OffsetFromAllocationBase.Value);
        bool exactRegion = profile.ObservedRegionOffsets.Contains(metadata.OffsetFromRegionBase);
        var timer = Stopwatch.StartNew();
        NativeStableStructuralFingerprint fingerprint =
            NativeStableRootCharacterizer.Fingerprint(memory, regions, modules, q);
        double similarity = NeighborhoodSimilarity(profile.Neighborhood, fingerprint);
        neighborhoodTicks = timer.Elapsed.Ticks;
        double structural = StructuralScore(
            metadata, profile, similarity, exactAllocation, exactRegion);
        if (structural < options.MinimumStructuralScore ||
            similarity < profile.MinimumNeighborhoodSimilarity)
        {
            objectTicks = 0;
            return null;
        }
        timer.Restart();
        bool pointerReadable = memory.TryReadPointer(q, out nuint x) && x != 0;
        bool xReadable = pointerReadable && memory.ReadBytes(x, 1).BytesRead == 1;
        NativeStableAutonomousBeatmapObjectEvidence evidence = xReadable
            ? NativeStableAutonomousBeatmapObjectValidator.Validate(memory, x)
            : new(false, false, false, null, null, null, null, null,
                0, 0, 0, false);
        objectTicks = timer.Elapsed.Ticks;
        double final = structural + (pointerReadable ? 5 : 0) +
            (xReadable ? 5 : 0) + (evidence.IsValid ? 30 : 0);
        return new(
            checked((ulong)q), pointerReadable ? checked((ulong)x) : null,
            metadata, fingerprint, similarity, exactAllocation, exactRegion,
            pointerReadable, xReadable, evidence, structural, final, discoveryPath);
    }

    internal static double NeighborhoodSimilarity(
        NativeStableStructuralFingerprint expected,
        NativeStableStructuralFingerprint observed)
    {
        Dictionary<int, NativeStableStructuralCell> byOffset = observed.Cells
            .ToDictionary(cell => cell.RelativeOffset);
        NativeStableStructuralCell[] comparable = expected.Cells
            .Where(cell => byOffset.ContainsKey(cell.RelativeOffset)).ToArray();
        return comparable.Length == 0 ? 0 : comparable.Count(cell =>
        {
            NativeStableStructuralCell other = byOffset[cell.RelativeOffset];
            if (cell.Kind != other.Kind)
            {
                return false;
            }
            if (cell.Kind != NativeStableStructuralValueKind.ModulePointerCandidate)
            {
                return true;
            }
            return cell.PointerTarget?.ModuleName == other.PointerTarget?.ModuleName &&
                cell.PointerTarget?.OffsetFromModuleBase ==
                    other.PointerTarget?.OffsetFromModuleBase;
        }) / (double)comparable.Length;
    }

    private static double StructuralScore(
        NativeStableObservedAddress metadata,
        NativeStableAutonomousQProfile profile,
        double similarity,
        bool exactAllocation,
        bool exactRegion)
    {
        double score = 0;
        if (metadata.Backing == profile.Backing) score += 10;
        if (metadata.Protection == profile.Protection) score += 10;
        if (metadata.Type == profile.Type) score += 10;
        if (metadata.Alignment == profile.Alignment) score += 5;
        if (metadata.RegionSize == profile.RegionSize) score += 5;
        if (exactAllocation) score += 20;
        if (exactRegion) score += 10;
        score += similarity * 30;
        return score;
    }

    private static NativeStableRegionBacking RegionBacking(uint type) => type switch
    {
        NativeStableInterop.MemoryPrivate => NativeStableRegionBacking.Private,
        NativeStableInterop.MemoryMapped => NativeStableRegionBacking.Mapped,
        NativeStableInterop.MemoryImage => NativeStableRegionBacking.ModuleBacked,
        _ => NativeStableRegionBacking.Unknown
    };
}

internal sealed record NativeStableAutonomousQCacheEntry(
    int ProcessId,
    DateTimeOffset ProcessStartIdentity,
    ulong QAddress);
