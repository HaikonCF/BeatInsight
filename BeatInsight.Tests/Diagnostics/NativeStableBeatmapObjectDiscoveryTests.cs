using BeatInsight.Diagnostics.NativeStable;
using System.Text;

namespace BeatInsight.Tests.Diagnostics;

public sealed class NativeStableBeatmapObjectDiscoveryTests
{
    private const uint RegionBase = 0x1000;
    private const uint CandidateBase = 0x2000;
    private const uint ChecksumObject = 0x5000;
    private const uint FolderObject = 0x5200;
    private const uint FilenameObject = 0x5400;
    private const string Checksum = "0123456789abcdef0123456789abcdef";
    private const string Folder = "123 Artist - Title";
    private const string Filename = "Artist - Title (Mapper) [Hard].osu";

    [Fact]
    public void ExactCandidate_MatchesAllThreeCoreFields()
    {
        SyntheticAddressSpace memory = StandardMemory(CandidateBase, coreFields: 3);

        NativeStableBeatmapObjectDiscoveryResult result = Discover(memory);

        NativeStableBeatmapObjectCandidate candidate = Assert.Single(result.StrongCandidates);
        Assert.Equal(NativeStableBeatmapObjectDiscoveryStatus.Unique, result.Status);
        Assert.Equal(3, candidate.MatchedCoreFieldCount);
        Assert.Equal(NativeStableBeatmapObjectCandidateLevel.StrongCandidate, candidate.Level);
    }

    [Fact]
    public void ChecksumOnly_IsWeakCandidate()
    {
        SyntheticAddressSpace memory = StandardMemory(CandidateBase, coreFields: 1);

        NativeStableBeatmapObjectCandidate candidate = Assert.Single(
            Discover(memory).Candidates);

        Assert.True(candidate.Checksum.Matches);
        Assert.False(candidate.Folder.Matches);
        Assert.False(candidate.Filename.Matches);
        Assert.Equal(NativeStableBeatmapObjectCandidateLevel.WeakCandidate, candidate.Level);
    }

    [Fact]
    public void ChecksumAndFilenameWithWrongFolder_IsPartialCandidate()
    {
        SyntheticAddressSpace memory = StandardMemory(CandidateBase, coreFields: 3);
        const uint wrongFolderObject = 0x5600;
        memory.WriteManagedString(wrongFolderObject, "wrong folder");
        memory.WriteUInt32(CandidateBase + 0x78, wrongFolderObject);

        NativeStableBeatmapObjectCandidate candidate = Assert.Single(
            Discover(memory).Candidates);

        Assert.Equal(2, candidate.MatchedCoreFieldCount);
        Assert.False(candidate.Folder.Matches);
        Assert.Equal(NativeStableBeatmapObjectCandidateLevel.PartialCandidate, candidate.Level);
    }

    [Fact]
    public void MultipleStrongCandidates_AreAmbiguous()
    {
        SyntheticAddressSpace memory = StandardMemory(CandidateBase, coreFields: 3);
        WriteCandidate(memory, 0x3000, coreFields: 3);

        NativeStableBeatmapObjectDiscoveryResult result = Discover(memory);

        Assert.Equal(NativeStableBeatmapObjectDiscoveryStatus.Ambiguous, result.Status);
        Assert.Equal(2, result.StrongCandidates.Count);
    }

    [Fact]
    public void NoReferences_ProducesNoCandidate()
    {
        SyntheticAddressSpace memory = StandardMemory(candidateAddress: null, coreFields: 0);

        NativeStableBeatmapObjectDiscoveryResult result = Discover(memory);

        Assert.Equal(NativeStableBeatmapObjectDiscoveryStatus.None, result.Status);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void MatchingIds_ProduceStrongCandidateWithIds()
    {
        SyntheticAddressSpace memory = StandardMemory(CandidateBase, coreFields: 3);
        memory.WriteInt32(CandidateBase + 0xc8, 1234);
        memory.WriteInt32(CandidateBase + 0xcc, 5678);

        NativeStableBeatmapObjectCandidate candidate = Assert.Single(
            Discover(memory, beatmapId: 1234, beatmapSetId: 5678).StrongCandidates);

        Assert.Equal(2, candidate.MatchedOptionalIdCount);
        Assert.Equal(NativeStableBeatmapObjectCandidateLevel.StrongCandidateWithIds,
            candidate.Level);
    }

    [Fact]
    public void WrongIds_RemainExplicitMismatches()
    {
        SyntheticAddressSpace memory = StandardMemory(CandidateBase, coreFields: 3);
        memory.WriteInt32(CandidateBase + 0xc8, 9999);
        memory.WriteInt32(CandidateBase + 0xcc, 8888);

        NativeStableBeatmapObjectCandidate candidate = Assert.Single(
            Discover(memory, beatmapId: 1234, beatmapSetId: 5678).StrongCandidates);

        Assert.False(candidate.BeatmapIdMatches);
        Assert.False(candidate.BeatmapSetIdMatches);
        Assert.Equal(0, candidate.MatchedOptionalIdCount);
        Assert.Equal(NativeStableBeatmapObjectCandidateLevel.StrongCandidate, candidate.Level);
    }

    [Fact]
    public void UnreadableManagedPointer_FailsClosed()
    {
        SyntheticAddressSpace memory = StandardMemory(CandidateBase, coreFields: 1);
        memory.WriteUInt32(CandidateBase + 0x6c, 0xdead0000);
        memory.WriteUInt32(CandidateBase + 0x90, FilenameObject);

        NativeStableBeatmapObjectCandidate candidate = Assert.Single(
            Discover(memory).Candidates);

        Assert.False(candidate.Checksum.Matches);
        Assert.True(candidate.Filename.Matches);
    }

    [Fact]
    public void AbsurdManagedStringLength_IsRejected()
    {
        var memory = new SyntheticAddressSpace(RegionBase, 0x8000, is32Bit: true);
        memory.WriteUInt32(ChecksumObject, 0xdeadbeef);
        memory.WriteInt32(ChecksumObject + 4,
            NativeStableManagedStringReader.MaximumCharacters + 1);

        bool success = NativeStableManagedStringReader.TryRead(
            memory, ChecksumObject, out _);

        Assert.False(success);
    }

    [Fact]
    public void PointerSize_IsExplicitAndUnsupportedDiscoveryIsRejected()
    {
        var memory = new SyntheticAddressSpace(RegionBase, 0x8000, is32Bit: false);
        memory.WriteManagedString64(ChecksumObject, Checksum);

        Assert.True(NativeStableManagedStringReader.TryRead(
            memory, ChecksumObject, out string value));
        Assert.Equal(Checksum, value);
        NativeStableBeatmapObjectDiscoveryResult result =
            NativeStableBeatmapObjectDiscovery.Discover(
                memory, [memory.Region], Metadata(), Signals(memory));
        Assert.False(result.ArchitectureSupported);
    }

    [Fact]
    public void CandidateNearRegionBoundary_FailsClosedWithoutBeingDiscarded()
    {
        var memory = new SyntheticAddressSpace(RegionBase, 0x8000, is32Bit: true);
        WriteStrings(memory);
        uint candidate = RegionBase + 0x8000 - 0x70;
        memory.WriteUInt32(candidate + 0x6c, ChecksumObject);

        NativeStableBeatmapObjectCandidate result = Assert.Single(
            Discover(memory).Candidates);

        Assert.Equal((nuint)candidate, result.CandidateAddress);
        Assert.Equal(1, result.MatchedCoreFieldCount);
    }

    [Fact]
    public void RawUtf16Address_DiffersFromManagedObjectReference()
    {
        SyntheticAddressSpace memory = StandardMemory(CandidateBase, coreFields: 3);

        NativeStableBeatmapObjectDiscoveryResult result = Discover(memory);
        NativeStableManagedStringTarget checksum = Assert.Single(
            result.ManagedStringTargets,
            target => target.Kind == NativeStableKnownSignalKind.Checksum);
        NativeStableBeatmapObjectCandidate candidate = Assert.Single(result.StrongCandidates);

        Assert.Equal((nuint)(ChecksumObject + 8), checksum.RawCharacterAddress);
        Assert.Equal((nuint)ChecksumObject, checksum.ObjectAddress);
        Assert.Equal((nuint)ChecksumObject, candidate.Checksum.Reference);
        Assert.NotEqual(checksum.RawCharacterAddress, candidate.Checksum.Reference);
        Assert.False(result.RawDeltaMatchesHypothesizedFieldSpacing);
    }

    [Fact]
    public void CandidateEvidence_IsNotTruncatedByOutputBounding()
    {
        SyntheticAddressSpace memory = StandardMemory(candidateAddress: null, coreFields: 0);
        const int count = 40;
        for (uint index = 0; index < count; index++)
        {
            WriteCandidate(memory, 0x2000 + index * 0x100, coreFields: 3);
        }

        NativeStableBeatmapObjectDiscoveryResult result = Discover(memory);

        Assert.Equal(count, result.StrongCandidates.Count);
        Assert.Equal(count, result.Statistics.SurvivingThreeFieldCount);
        Assert.Equal(NativeStableBeatmapObjectDiscoveryStatus.Ambiguous, result.Status);
    }

    private static NativeStableBeatmapObjectDiscoveryResult Discover(
        SyntheticAddressSpace memory,
        int? beatmapId = null,
        int? beatmapSetId = null) =>
        NativeStableBeatmapObjectDiscovery.Discover(
            memory,
            [memory.Region],
            Metadata(beatmapId, beatmapSetId),
            Signals(memory));

    private static SyntheticAddressSpace StandardMemory(
        uint? candidateAddress,
        int coreFields)
    {
        var memory = new SyntheticAddressSpace(RegionBase, 0x10000, is32Bit: true);
        WriteStrings(memory);
        if (candidateAddress is not null)
        {
            WriteCandidate(memory, candidateAddress.Value, coreFields);
        }
        return memory;
    }

    private static void WriteStrings(SyntheticAddressSpace memory)
    {
        memory.WriteManagedString(ChecksumObject, Checksum);
        memory.WriteManagedString(FolderObject, Folder);
        memory.WriteManagedString(FilenameObject, Filename);
    }

    private static void WriteCandidate(
        SyntheticAddressSpace memory,
        uint candidateAddress,
        int coreFields)
    {
        if (coreFields >= 1)
        {
            memory.WriteUInt32(candidateAddress + 0x6c, ChecksumObject);
        }
        if (coreFields >= 2)
        {
            memory.WriteUInt32(candidateAddress + 0x78, FolderObject);
        }
        if (coreFields >= 3)
        {
            memory.WriteUInt32(candidateAddress + 0x90, FilenameObject);
        }
    }

    private static NativeStableKnownBeatmapObjectMetadata Metadata(
        int? beatmapId = null,
        int? beatmapSetId = null) =>
        new("C:\\Songs\\123 Artist - Title\\map.osu", Checksum,
            Folder, Filename, beatmapId, beatmapSetId);

    private static IReadOnlyList<NativeStableProbeSignal> Signals(
        SyntheticAddressSpace memory) =>
    [
        Signal(NativeStableKnownSignalKind.Checksum, Checksum,
            ChecksumObject + checked((uint)NativeStableManagedStringReader.CharacterDataOffset(memory.Is32Bit))),
        Signal(NativeStableKnownSignalKind.Folder, Folder,
            FolderObject + checked((uint)NativeStableManagedStringReader.CharacterDataOffset(memory.Is32Bit))),
        Signal(NativeStableKnownSignalKind.Filename, Filename,
            FilenameObject + checked((uint)NativeStableManagedStringReader.CharacterDataOffset(memory.Is32Bit)))
    ];

    private static NativeStableProbeSignal Signal(
        NativeStableKnownSignalKind kind,
        string value,
        uint rawAddress) =>
        new("MAP", kind.ToString(), kind, NativeStableTextEncoding.Utf16,
            Encoding.Unicode.GetBytes(value), [rawAddress]);

    private sealed class SyntheticAddressSpace : INativeStableMemoryReader
    {
        private readonly byte[] data;

        internal SyntheticAddressSpace(uint baseAddress, int size, bool is32Bit)
        {
            BaseAddress = baseAddress;
            data = new byte[size];
            Is32Bit = is32Bit;
            Region = new(baseAddress, checked((nuint)size), NativeStableInterop.MemoryCommit,
                0x04, 0x20000);
        }

        internal uint BaseAddress { get; }
        internal NativeStableMemoryRegion Region { get; }
        public bool Is32Bit { get; }

        public NativeStableMemoryReadResult ReadBytes(nuint address, int count)
        {
            if (address < BaseAddress || count < 0 ||
                address - BaseAddress > checked((nuint)data.Length) ||
                checked((nuint)count) > checked((nuint)data.Length) - (address - BaseAddress))
            {
                return NativeStableMemoryReadResult.Failed;
            }
            int offset = checked((int)(address - BaseAddress));
            byte[] result = data.AsSpan(offset, count).ToArray();
            return new(result, Complete: true, result.Length);
        }

        public bool TryReadPointer(nuint address, out nuint value)
        {
            NativeStableMemoryReadResult read = ReadBytes(
                address, Is32Bit ? sizeof(uint) : sizeof(ulong));
            if (!read.Complete)
            {
                value = 0;
                return false;
            }
            value = Is32Bit
                ? BitConverter.ToUInt32(read.Data, 0)
                : checked((nuint)BitConverter.ToUInt64(read.Data, 0));
            return value != 0;
        }

        internal void WriteUInt32(uint address, uint value) =>
            Write(address, BitConverter.GetBytes(value));

        internal void WriteInt32(uint address, int value) =>
            Write(address, BitConverter.GetBytes(value));

        internal void WriteManagedString(uint address, string value)
        {
            WriteUInt32(address, 0xdeadbeef);
            WriteInt32(address + 4, value.Length);
            Write(address + 8, Encoding.Unicode.GetBytes(value));
        }

        internal void WriteManagedString64(uint address, string value)
        {
            Write(address, BitConverter.GetBytes(0xdeadbeefdeadbeefUL));
            WriteInt32(address + 8, value.Length);
            Write(address + 12, Encoding.Unicode.GetBytes(value));
        }

        private void Write(uint address, byte[] bytes)
        {
            int offset = checked((int)(address - BaseAddress));
            bytes.CopyTo(data, offset);
        }
    }
}
