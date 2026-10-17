using BeatInsight.Diagnostics.NativeStable;
using System.IO;
using System.Text.Json;

namespace BeatInsight.Tests.Diagnostics;

public sealed class NativeStableRootCharacterizationTests
{
    private static readonly DateTimeOffset Started =
        new(2026, 9, 27, 19, 18, 54, TimeSpan.Zero);

    [Fact]
    public void QMetadata_UsesRegionAllocationAndActualModuleRange()
    {
        NativeStableMemoryRegion region = Region(0x2000, 0x1000, 0x1800);
        NativeStableObservedAddress result = NativeStableRootCharacterizer.Describe(
            0x2204, [region], [new("osu!.exe", "C:\\osu!\\osu!.exe", 0x2000, 0x800)]);

        Assert.Equal(0x204UL, result.OffsetFromRegionBase);
        Assert.Equal(0xa04UL, result.OffsetFromAllocationBase);
        Assert.Equal("osu!.exe", result.Module?.Name);
        Assert.Equal(0x204UL, result.Module?.OffsetFromModuleBase);
    }

    [Fact]
    public void QNeighborhood_ClassifiesStructuralCellKinds()
    {
        var memory = new SyntheticMemory();
        memory.WriteUInt32(0x20000, 7);
        memory.WriteUInt32(0x20004, 0x28000);
        memory.WriteUInt32(0x20008, 0x38000);
        memory.WriteUInt32(0x2000c, 0x48000);
        IReadOnlyList<NativeStableMemoryRegion> regions =
        [Region(0x18000, 0x18000), Region(0x38000, 0x400, protection: 0x20), Region(0x48000, 0x400, type: 0x1000000)];
        NativeStableStructuralFingerprint result = NativeStableRootCharacterizer.Fingerprint(
            memory, regions, [new("module.dll", "C:\\module.dll", 0x48000, 0x400)], 0x20000);

        Assert.Equal(NativeStableStructuralValueKind.SmallInteger, Cell(result, 0).Kind);
        Assert.Equal(NativeStableStructuralValueKind.ReadablePointerCandidate, Cell(result, 4).Kind);
        Assert.Equal(NativeStableStructuralValueKind.ExecutablePointerCandidate, Cell(result, 8).Kind);
        Assert.Equal(NativeStableStructuralValueKind.ModulePointerCandidate, Cell(result, 12).Kind);
    }

    [Fact]
    public void ReferenceDiscovery_FindsCompleteAlignedSet()
    {
        var memory = new SyntheticMemory();
        memory.WriteUInt32(0x1100, 0x2800);
        memory.WriteUInt32(0x1200, 0x2800);
        NativeStableQReference[] result = NativeStableRootCharacterizer.DiscoverReferences(
            memory, [Region(0x1000, 0x2000)], [], 0x2800);
        Assert.Equal(new ulong[] { 0x1100, 0x1200 }, result.Select(item => item.AbsoluteAddress));
    }

    [Fact]
    public void ReferenceDiscovery_RetainsMultipleReferences()
    {
        var memory = new SyntheticMemory();
        foreach (nuint address in new nuint[] { 0x1100, 0x1104, 0x1400 })
        {
            memory.WriteUInt32(address, 0x2800);
        }
        Assert.Equal(3, NativeStableRootCharacterizer.DiscoverReferences(
            memory, [Region(0x1000, 0x2000)], [], 0x2800).Length);
    }

    [Fact]
    public void ReferenceGrouping_SeparatesStructuralClusters()
    {
        NativeStableQReference[] references =
        [Reference(0x1100, 0x100), Reference(0x1104, 0x104), Reference(0x1400, 0x400)];
        NativeStableReferenceCluster[] result =
            NativeStableRootCharacterizer.GroupReferences(references);
        Assert.Equal(2, result.Length);
        Assert.Equal(2, result[0].Count);
        Assert.Equal(1, result[1].Count);
    }

    [Fact]
    public void ThirdLevelDiscovery_IsBoundedAndRetainsChains()
    {
        var memory = new SyntheticMemory();
        for (int index = 0; index < 10; index++)
        {
            memory.WriteUInt32(checked((nuint)(0x1800 + index * 4)), 0x2800);
        }
        memory.WriteUInt32(0x1c00, 0x1800);
        NativeStableMemoryRegion region = Region(0x1000, 0x2000);
        NativeStableQReference[] references = NativeStableRootCharacterizer.DiscoverReferences(
            memory, [region], [], 0x2800);
        NativeStableThirdLevelReference[] third = NativeStableRootCharacterizer.DiscoverThirdLevel(
            memory, [region], [], references);

        Assert.Contains(third, item => item.AbsoluteAddress == 0x1c00 && item.ReferencedRAddress == 0x1800);
        Assert.All(third, item => Assert.Contains(item.ReferencedRAddress,
            references.Take(NativeStableRootCharacterizer.MaximumThirdLevelRoots)
                .Select(reference => reference.AbsoluteAddress)));
    }

    [Fact]
    public void StructuralProfile_IgnoresAbsoluteAddresses()
    {
        NativeStableRootStructuralProfile first =
            NativeStableRootStructuralProfile.FromCapture(Capture(0x2000, 0x1100));
        NativeStableRootStructuralProfile second =
            NativeStableRootStructuralProfile.FromCapture(Capture(0x9000, 0x8100));
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
    }

    [Fact]
    public void StructuralProfile_PreservesMeaningfulRelativeMetadata()
    {
        NativeStableRootStructuralProfile profile =
            NativeStableRootStructuralProfile.FromCapture(Capture(0x2000, 0x1100));
        Assert.Equal(0x20UL, profile.Q.OffsetFromRegionBase);
        Assert.Equal(0x120UL, profile.Q.OffsetFromAllocationBase);
        Assert.Equal(0x100UL, profile.References[0].Address.OffsetFromAllocationBase);
    }

    [Fact]
    public void PhaseOrdering_FailsClosed()
    {
        NativeStableQCharacterizationSession session = Session();
        Assert.Throws<InvalidDataException>(() =>
            NativeStableQCharacterizationHarness.EnsureCanRun(session, NativeStableQPhase.CaptureB));
    }

    [Fact]
    public void ProcessGenerationMismatch_FailsClosedForPidAndStartIdentity()
    {
        NativeStableQCharacterizationSession session = Session();
        Assert.Throws<NativeStableProcessGenerationChangedException>(() =>
            NativeStableQCharacterizationHarness.ApplyCapture(
                session, NativeStableQPhase.CaptureA, 43, Started, Capture(0x2000, 0x1100)));
        Assert.Throws<NativeStableProcessGenerationChangedException>(() =>
            NativeStableQCharacterizationHarness.ApplyCapture(
                session, NativeStableQPhase.CaptureA, 42, Started.AddSeconds(1), Capture(0x2000, 0x1100)));
    }

    [Fact]
    public void ChangedQBetweenPhases_FailsClosed()
    {
        NativeStableQCharacterizationSession session = Session();
        Assert.Throws<InvalidDataException>(() => NativeStableQCharacterizationHarness.ApplyCapture(
            session, NativeStableQPhase.CaptureA, 42, Started, Capture(0x3000, 0x1100)));
    }

    [Fact]
    public void ExpectedMapMismatch_FailsClosed()
    {
        NativeStableQCharacterizationSession session = Session();
        NativeStableQCharacterizationCapture capture = Capture(0x2000, 0x1100) with
        {
            ExpectedMapMatches = false
        };
        Assert.Throws<NativeStableExpectedMapMismatchException>(() =>
            NativeStableQCharacterizationHarness.ApplyCapture(
                session, NativeStableQPhase.CaptureA, 42, Started, capture));
    }

    [Fact]
    public void CorruptedSessionAndProfile_FailClosed()
    {
        string directory = Path.Combine(Path.GetTempPath(), "BeatInsightC4Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string session = Path.Combine(directory, "session.json");
        string profile = Path.Combine(directory, "profile.json");
        File.WriteAllText(session, "{broken");
        File.WriteAllText(profile, "{}");
        var store = new NativeStableQCharacterizationStore();
        Assert.Throws<InvalidDataException>(() => store.Load(session));
        Assert.Throws<InvalidDataException>(() => store.LoadProfile(profile));
    }

    private static NativeStableQCharacterizationSession Session() => new()
    {
        SchemaVersion = 1,
        ProcessId = 42,
        ProcessStartIdentity = Started,
        QAddress = 0x2000,
        MapA = new("a", "a", "a", "a.osu", 1, 2),
        MapB = new("b", "b", "b", "b.osu", 3, 4),
        ValidatedRootReferences = [0x1100],
        Captures = [],
        Profiles = [],
        NextPhase = NativeStableQPhase.CaptureA
    };

    private static NativeStableQCharacterizationCapture Capture(ulong q, ulong reference)
    {
        NativeStableObservedAddress qAddress = Address(q, 0x20, 0x120);
        NativeStableQReference qReference = new(reference, Address(reference, 0x100, 0x100),
            new(0x80, [new(0, NativeStableStructuralValueKind.ReadablePointerCandidate, null)]));
        return new("CaptureA", q, q + 0x1000, qAddress,
            new(0x80, [new(0, NativeStableStructuralValueKind.ReadablePointerCandidate, null)]),
            [qReference],
            [new(reference, reference, 1, 0x100, 0x100, 0x1000, 0x04, 0x20000,
                NativeStableRegionBacking.Private, null)],
            [], 1, [], 3, 2, 2, true, 10);
    }

    private static NativeStableObservedAddress Address(
        ulong absolute, ulong regionOffset, ulong allocationOffset) =>
        new(absolute, absolute - regionOffset, absolute - allocationOffset,
            allocationOffset, regionOffset, 0x1000, 0x1000, 0x04, 0x20000, 0,
            NativeStableRegionBacking.Private, null);

    private static NativeStableQReference Reference(ulong absolute, ulong allocationOffset) =>
        new(absolute, Address(absolute, allocationOffset, allocationOffset), new(0x80, []));

    private static NativeStableStructuralCell Cell(
        NativeStableStructuralFingerprint fingerprint, int offset) =>
        fingerprint.Cells.Single(cell => cell.RelativeOffset == offset);

    private static NativeStableMemoryRegion Region(
        nuint address, nuint size, nuint allocationBase = 0,
        uint protection = 0x04, uint type = 0x20000) =>
        new(address, size, 0x1000, protection, type,
            allocationBase == 0 ? address : allocationBase);

    private sealed class SyntheticMemory : INativeStableMemoryReader
    {
        private readonly Dictionary<nuint, byte> bytes = [];
        public bool Is32Bit => true;

        internal void WriteUInt32(nuint address, uint value)
        {
            byte[] data = BitConverter.GetBytes(value);
            for (int index = 0; index < data.Length; index++)
            {
                bytes[address + checked((nuint)index)] = data[index];
            }
        }

        public NativeStableMemoryReadResult ReadBytes(nuint address, int count)
        {
            byte[] result = new byte[count];
            for (int index = 0; index < count; index++)
            {
                bytes.TryGetValue(address + checked((nuint)index), out result[index]);
            }
            return new(result, Complete: true, BytesRead: count);
        }

        public bool TryReadPointer(nuint address, out nuint value)
        {
            NativeStableMemoryReadResult result = ReadBytes(address, sizeof(uint));
            value = BitConverter.ToUInt32(result.Data, 0);
            return true;
        }
    }
}
