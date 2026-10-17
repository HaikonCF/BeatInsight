using System.Diagnostics;
using System.IO;

namespace BeatInsight.Diagnostics.NativeStable;

/// <summary>
/// Read-only C.4 diagnostic. It characterizes a validated dynamic Q without
/// signatures, anchors, fixed addresses, or semantic claims.
/// </summary>
internal static class NativeStableRootCharacterizer
{
    internal const int NeighborhoodRadius = 0x80;
    internal const int MaximumThirdLevelRoots = 8;
    private const ulong ClusterGap = 0x100;

    internal static NativeStableQCharacterizationCapture Capture(
        INativeStableMemoryReader memory,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        IReadOnlyList<NativeStableModuleRange> modules,
        nuint q,
        NativeStableKnownBeatmapObjectMetadata expectedMap,
        string phase,
        IReadOnlyCollection<nuint> expectedReferences)
    {
        var total = Stopwatch.StartNew();
        if (!TryReadUInt32(memory, q, out nuint x))
        {
            throw new InvalidDataException("Q is unreadable or no longer contains X.");
        }
        NativeStableBeatmapObjectCandidate? validation =
            NativeStableBeatmapObjectDiscovery.ValidateCandidate(memory, regions, expectedMap, x);
        bool idsMatch = validation is not null &&
            (validation.AvailableOptionalIdCount == 0 ||
             validation.MatchedOptionalIdCount == validation.AvailableOptionalIdCount);
        if (validation is null || validation.MatchedCoreFieldCount != 3 || !idsMatch)
        {
            throw new NativeStableExpectedMapMismatchException(phase);
        }

        NativeStableObservedAddress qMetadata = Describe(q, regions, modules);
        NativeStableStructuralFingerprint qFingerprint =
            Fingerprint(memory, regions, modules, q);

        NativeStableQReference[] references =
            DiscoverReferences(memory, regions, modules, q);
        IReadOnlyList<nuint> referenceAddresses = references
            .Select(reference => checked((nuint)reference.AbsoluteAddress)).ToArray();
        ulong[] missingExpected = expectedReferences
            .Except(referenceAddresses)
            .Select(value => checked((ulong)value))
            .OrderBy(value => value)
            .ToArray();

        NativeStableReferenceCluster[] clusters = GroupReferences(references);
        NativeStableThirdLevelReference[] third = DiscoverThirdLevel(
            memory, regions, modules, references);

        return new(
            phase,
            checked((ulong)q),
            checked((ulong)x),
            qMetadata,
            qFingerprint,
            references,
            clusters,
            third,
            expectedReferences.Count,
            missingExpected,
            validation.MatchedCoreFieldCount,
            validation.MatchedOptionalIdCount,
            validation.AvailableOptionalIdCount,
            ExpectedMapMatches: true,
            total.Elapsed.Ticks);
    }

    internal static NativeStableQReference[] DiscoverReferences(
        INativeStableMemoryReader memory,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        IReadOnlyList<NativeStableModuleRange> modules,
        nuint q) => NativeStableRootReverseDiscovery
        .FindAlignedReferences(memory, regions, [q])
        .Select(address => new NativeStableQReference(
            checked((ulong)address),
            Describe(address, regions, modules),
            Fingerprint(memory, regions, modules, address)))
        .ToArray();

    internal static NativeStableThirdLevelReference[] DiscoverThirdLevel(
        INativeStableMemoryReader memory,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        IReadOnlyList<NativeStableModuleRange> modules,
        IReadOnlyList<NativeStableQReference> references)
    {
        NativeStableQReference[] selected = references
            .OrderByDescending(reference => reference.Address.Module is not null)
            .ThenByDescending(reference => IsExecutable(reference.Address.Protection))
            .ThenByDescending(reference =>
                reference.Address.Backing == NativeStableRegionBacking.ModuleBacked)
            .ThenBy(reference => reference.AbsoluteAddress)
            .Take(MaximumThirdLevelRoots)
            .ToArray();
        HashSet<ulong> selectedAddresses = selected.Select(item => item.AbsoluteAddress).ToHashSet();
        IReadOnlyList<NativeStableStoredReference> thirdLevel =
            NativeStableRootReverseDiscovery.FindAlignedReferencesWithValues(
                memory,
                regions,
                selected.Select(item => checked((nuint)item.AbsoluteAddress)).ToArray(),
                CancellationToken.None);
        return thirdLevel
            .Where(item => selectedAddresses.Contains(checked((ulong)item.Value)))
            .Select(item => new NativeStableThirdLevelReference(
                checked((ulong)item.Address),
                checked((ulong)item.Value),
                Describe(item.Address, regions, modules)))
            .ToArray();
    }

    internal static NativeStableObservedAddress Describe(
        nuint address,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        IReadOnlyList<NativeStableModuleRange> modules)
    {
        NativeStableMemoryRegion? region = FindRegion(regions, address);
        NativeStableModuleRange? module = modules.FirstOrDefault(item => item.Contains(address));
        if (region is null)
        {
            return new(checked((ulong)address), null, null, null, 0, 0, 0, 0, 0,
                checked((int)(address % sizeof(uint))), NativeStableRegionBacking.Unknown,
                module is null ? null : NativeStableObservedModule.FromRuntime(module, address));
        }
        nuint allocationBase = region.Value.AllocationBase;
        return new(
            checked((ulong)address),
            checked((ulong)region.Value.BaseAddress),
            allocationBase == 0 ? null : checked((ulong)allocationBase),
            allocationBase == 0 ? null : checked((ulong)(address - allocationBase)),
            checked((ulong)(address - region.Value.BaseAddress)),
            checked((ulong)region.Value.Size),
            region.Value.State,
            region.Value.Protect,
            region.Value.Type,
            checked((int)(address % sizeof(uint))),
            Backing(region.Value.Type),
            module is null ? null : NativeStableObservedModule.FromRuntime(module, address));
    }

    internal static NativeStableStructuralFingerprint Fingerprint(
        INativeStableMemoryReader memory,
        IReadOnlyList<NativeStableMemoryRegion> regions,
        IReadOnlyList<NativeStableModuleRange> modules,
        nuint origin)
    {
        var cells = new List<NativeStableStructuralCell>();
        for (int offset = -NeighborhoodRadius; offset <= NeighborhoodRadius; offset += sizeof(uint))
        {
            long signed = checked((long)origin + offset);
            if (signed < 0)
            {
                cells.Add(new(offset, NativeStableStructuralValueKind.UnknownValue, null));
                continue;
            }
            nuint cellAddress = checked((nuint)signed);
            NativeStableMemoryReadResult read = memory.ReadBytes(cellAddress, sizeof(uint));
            if (!read.Complete)
            {
                cells.Add(new(offset, NativeStableStructuralValueKind.UnknownValue, null));
                continue;
            }
            uint value = BitConverter.ToUInt32(read.Data, 0);
            if (value == 0)
            {
                cells.Add(new(offset, NativeStableStructuralValueKind.Zero, null));
                continue;
            }
            if (value <= 0xffff)
            {
                cells.Add(new(offset, NativeStableStructuralValueKind.SmallInteger, null));
                continue;
            }
            NativeStableMemoryRegion? target = FindRegion(regions, value);
            if (target is null || memory.ReadBytes(value, 1).BytesRead != 1)
            {
                cells.Add(new(offset, NativeStableStructuralValueKind.UnknownValue, null));
                continue;
            }
            NativeStableObservedAddress description = Describe(value, regions, modules);
            NativeStableStructuralValueKind kind = description.Module is not null
                ? NativeStableStructuralValueKind.ModulePointerCandidate
                : IsExecutable(description.Protection)
                    ? NativeStableStructuralValueKind.ExecutablePointerCandidate
                    : NativeStableStructuralValueKind.ReadablePointerCandidate;
            cells.Add(new(offset, kind, NativeStablePointerTargetShape.FromObserved(description)));
        }
        return new(NeighborhoodRadius, cells.ToArray());
    }

    internal static NativeStableReferenceCluster[] GroupReferences(
        IReadOnlyList<NativeStableQReference> references)
    {
        var result = new List<NativeStableReferenceCluster>();
        foreach (IGrouping<string, NativeStableQReference> allocation in references.GroupBy(item =>
                     $"{item.Address.AllocationBase:x}:{item.Address.RegionBase:x}:" +
                     $"{item.Address.Protection:x}:{item.Address.Type:x}:" +
                     $"{item.Address.Module?.Path}"))
        {
            NativeStableQReference[] ordered = allocation.OrderBy(item => item.AbsoluteAddress).ToArray();
            var current = new List<NativeStableQReference>();
            foreach (NativeStableQReference reference in ordered)
            {
                if (current.Count > 0 &&
                    reference.AbsoluteAddress - current[^1].AbsoluteAddress > ClusterGap)
                {
                    result.Add(CreateCluster(current));
                    current.Clear();
                }
                current.Add(reference);
            }
            if (current.Count > 0)
            {
                result.Add(CreateCluster(current));
            }
        }
        return result.OrderBy(item => item.FirstAbsoluteAddress).ToArray();
    }

    internal static bool IsExecutable(uint protection) =>
        (protection & 0xff) is NativeStableInterop.PageExecute or
            NativeStableInterop.PageExecuteRead or
            NativeStableInterop.PageExecuteReadWrite or
            NativeStableInterop.PageExecuteWriteCopy;

    private static NativeStableReferenceCluster CreateCluster(
        IReadOnlyList<NativeStableQReference> references) => new(
            references[0].AbsoluteAddress,
            references[^1].AbsoluteAddress,
            references.Count,
            references[0].Address.OffsetFromAllocationBase,
            references[^1].Address.OffsetFromAllocationBase,
            references[0].Address.RegionSize,
            references[0].Address.Protection,
            references[0].Address.Type,
            references[0].Address.Backing,
            references[0].Address.Module);

    private static bool TryReadUInt32(
        INativeStableMemoryReader memory,
        nuint address,
        out nuint value)
    {
        NativeStableMemoryReadResult read = memory.ReadBytes(address, sizeof(uint));
        if (!read.Complete)
        {
            value = 0;
            return false;
        }
        value = BitConverter.ToUInt32(read.Data, 0);
        return value != 0;
    }

    private static NativeStableMemoryRegion? FindRegion(
        IReadOnlyList<NativeStableMemoryRegion> regions,
        nuint address) => regions
        .Where(region => region.Contains(address))
        .Select(region => (NativeStableMemoryRegion?)region)
        .FirstOrDefault();

    private static NativeStableRegionBacking Backing(uint type) => type switch
    {
        NativeStableInterop.MemoryImage => NativeStableRegionBacking.ModuleBacked,
        NativeStableInterop.MemoryPrivate => NativeStableRegionBacking.Private,
        NativeStableInterop.MemoryMapped => NativeStableRegionBacking.Mapped,
        _ => NativeStableRegionBacking.Unknown
    };
}

internal static class NativeStableModuleCatalog
{
    internal static IReadOnlyList<NativeStableModuleRange> Capture(int processId)
    {
        var modules = new List<NativeStableModuleRange>();
        try
        {
            using Process process = Process.GetProcessById(processId);
            foreach (ProcessModule module in process.Modules)
            {
                try
                {
                    modules.Add(new(module.ModuleName, module.FileName,
                        checked((nuint)module.BaseAddress.ToInt64()),
                        checked((nuint)module.ModuleMemorySize)));
                }
                catch (Exception exception) when (exception is InvalidOperationException or
                    System.ComponentModel.Win32Exception)
                {
                    // Module changed during enumeration; omit that unverifiable range.
                }
            }
        }
        catch (Exception exception) when (exception is ArgumentException or
            InvalidOperationException or System.ComponentModel.Win32Exception or
            NotSupportedException)
        {
            return [];
        }
        return modules.OrderBy(module => module.BaseAddress).ToArray();
    }
}

internal sealed record NativeStableModuleRange(
    string Name, string Path, nuint BaseAddress, nuint Size)
{
    internal bool Contains(nuint address) =>
        address >= BaseAddress && address - BaseAddress < Size;
}

internal sealed record NativeStableObservedModule(
    string Name, string Path, ulong BaseAddress, ulong Size, ulong OffsetFromModuleBase)
{
    internal static NativeStableObservedModule FromRuntime(
        NativeStableModuleRange module, nuint address) =>
        new(module.Name, module.Path, checked((ulong)module.BaseAddress),
            checked((ulong)module.Size), checked((ulong)(address - module.BaseAddress)));
}

internal sealed record NativeStableObservedAddress(
    ulong AbsoluteAddress,
    ulong? RegionBase,
    ulong? AllocationBase,
    ulong? OffsetFromAllocationBase,
    ulong OffsetFromRegionBase,
    ulong RegionSize,
    uint State,
    uint Protection,
    uint Type,
    int Alignment,
    NativeStableRegionBacking Backing,
    NativeStableObservedModule? Module);

internal enum NativeStableStructuralValueKind
{
    Zero, SmallInteger, ReadablePointerCandidate, ExecutablePointerCandidate,
    ModulePointerCandidate, UnknownValue
}

internal sealed record NativeStablePointerTargetShape(
    ulong RegionSize, uint Protection, uint Type, NativeStableRegionBacking Backing,
    string? ModuleName, ulong? OffsetFromModuleBase)
{
    internal static NativeStablePointerTargetShape FromObserved(NativeStableObservedAddress value) =>
        new(value.RegionSize, value.Protection, value.Type, value.Backing,
            value.Module?.Name, value.Module?.OffsetFromModuleBase);
}

internal sealed record NativeStableStructuralCell(
    int RelativeOffset,
    NativeStableStructuralValueKind Kind,
    NativeStablePointerTargetShape? PointerTarget);

internal sealed record NativeStableStructuralFingerprint(
    int Radius,
    NativeStableStructuralCell[] Cells);

internal sealed record NativeStableQReference(
    ulong AbsoluteAddress,
    NativeStableObservedAddress Address,
    NativeStableStructuralFingerprint Neighborhood);

internal sealed record NativeStableReferenceCluster(
    ulong FirstAbsoluteAddress,
    ulong LastAbsoluteAddress,
    int Count,
    ulong? FirstOffsetFromAllocation,
    ulong? LastOffsetFromAllocation,
    ulong RegionSize,
    uint Protection,
    uint Type,
    NativeStableRegionBacking Backing,
    NativeStableObservedModule? Module);

internal sealed record NativeStableThirdLevelReference(
    ulong AbsoluteAddress,
    ulong ReferencedRAddress,
    NativeStableObservedAddress Address);

internal sealed record NativeStableQCharacterizationCapture(
    string Phase,
    ulong QAddress,
    ulong XAddress,
    NativeStableObservedAddress QMetadata,
    NativeStableStructuralFingerprint QNeighborhood,
    NativeStableQReference[] ReferencesToQ,
    NativeStableReferenceCluster[] ReferenceClusters,
    NativeStableThirdLevelReference[] ThirdLevelReferences,
    int ExpectedReferenceCount,
    ulong[] MissingExpectedReferences,
    int MatchedCoreFields,
    int MatchedOptionalIds,
    int AvailableOptionalIds,
    bool ExpectedMapMatches,
    long TotalTicks);

internal sealed class NativeStableExpectedMapMismatchException : InvalidOperationException
{
    internal NativeStableExpectedMapMismatchException(string phase)
        : base($"Q did not resolve to the expected map during {phase}.") { }
}
