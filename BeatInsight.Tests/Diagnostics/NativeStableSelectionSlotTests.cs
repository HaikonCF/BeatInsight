using BeatInsight.Diagnostics.NativeStable;

namespace BeatInsight.Tests.Diagnostics;

public sealed class NativeStableSelectionSlotTests
{
    private const uint RegionBase = 0x500000;
    private const uint SlotAddress = RegionBase + 0x5388;

    [Fact]
    public void StableRegionAndNormalizedSlot_ResolveKnownTarget()
    {
        var memory = Memory((SlotAddress, 0x1100));
        var reader = Reader(memory);

        NativeStableSelectionSlotSnapshot snapshot = reader.Capture(42, "A1", "A");

        Assert.Equal((nuint)0x5388, snapshot.SourceOffsetWithinRegion);
        Assert.Equal((nuint)SlotAddress, snapshot.SlotAddress);
        Assert.True(snapshot.SlotReadable);
        Assert.Equal((nuint)0x1100, snapshot.RawSlotValue);
        Assert.Equal("A", snapshot.ResolvedTargetMap);
    }

    [Fact]
    public void Sequence_A_B_A_B_A_IsExactTrackingPattern()
    {
        IReadOnlyList<NativeStableSelectionSlotSnapshot> snapshots = Sequence(
            "A", "B", "A", "B", "A");

        NativeStableSelectionSlotSequenceSummary summary =
            NativeStableSelectionSlotSequenceAnalyzer.Analyze(snapshots);

        Assert.Equal(["A", "B", "A", "B", "A"], summary.ObservedSequence);
        Assert.True(summary.SameRegionBaseAcrossSequence);
        Assert.True(summary.SameSlotAddressAcrossSequence);
        Assert.True(summary.ExactSelectionTrackingPattern);
    }

    [Fact]
    public void Sequence_A_B_Unknown_B_A_IsNotExactTrackingPattern()
    {
        IReadOnlyList<NativeStableSelectionSlotSnapshot> snapshots = Sequence(
            "A", "B", "Unknown", "B", "A");

        NativeStableSelectionSlotSequenceSummary summary =
            NativeStableSelectionSlotSequenceAnalyzer.Analyze(snapshots);

        Assert.Equal(["A", "B", "Unknown", "B", "A"], summary.ObservedSequence);
        Assert.False(summary.ExactSelectionTrackingPattern);
    }

    [Fact]
    public void ReadableSlot_PreservesChangedRawValue()
    {
        var memory = Memory((SlotAddress, 0x1100));
        var reader = Reader(memory);
        NativeStableSelectionSlotSnapshot first = reader.Capture(42, "A1", "A");
        memory.Set(SlotAddress, 0x2100);

        NativeStableSelectionSlotSnapshot second = reader.Capture(42, "B1", "B");

        Assert.True(first.SlotReadable);
        Assert.True(second.SlotReadable);
        Assert.Equal((nuint)0x1100, first.RawSlotValue);
        Assert.Equal((nuint)0x2100, second.RawSlotValue);
    }

    [Fact]
    public void LevelOnePointer_ResolvesKnownTarget()
    {
        var memory = Memory((SlotAddress, 0x7000), (0x7000, 0x1100));

        NativeStableSelectionSlotSnapshot snapshot = Reader(memory)
            .Capture(42, "A1", "A");

        Assert.Equal((nuint)0x7000, snapshot.RawSlotValue);
        Assert.Equal((nuint)0x1100, snapshot.DereferencedValueLevel1);
        Assert.Null(snapshot.DereferencedValueLevel2);
        Assert.Equal("A", snapshot.ResolvedTargetMap);
    }

    [Fact]
    public void LevelTwoPointer_ResolvesKnownTarget()
    {
        var memory = Memory(
            (SlotAddress, 0x7000),
            (0x7000, 0x8000),
            (0x8000, 0x2100));

        NativeStableSelectionSlotSnapshot snapshot = Reader(memory)
            .Capture(42, "B1", "B");

        Assert.Equal((nuint)0x8000, snapshot.DereferencedValueLevel1);
        Assert.Equal((nuint)0x2100, snapshot.DereferencedValueLevel2);
        Assert.Equal("B", snapshot.ResolvedTargetMap);
    }

    [Fact]
    public void UnresolvedReadableTarget_IsReportedUnknown()
    {
        var memory = Memory((SlotAddress, 0x7777));

        NativeStableSelectionSlotSnapshot snapshot = Reader(memory)
            .Capture(42, "A1", "A");

        Assert.True(snapshot.SlotReadable);
        Assert.Equal((nuint)0x7777, snapshot.RawSlotValue);
        Assert.Equal("Unknown", snapshot.ResolvedTargetMap);
        Assert.Equal(NativeStableSelectionTargetKind.Unknown, snapshot.ResolvedTargetKind);
    }

    [Fact]
    public void InvalidRegion_IsReportedWithoutPretendingSlotDisappeared()
    {
        var memory = Memory();

        NativeStableSelectionSlotSnapshot snapshot = Reader(memory)
            .Capture(42, "A1", "A");

        Assert.True(snapshot.RegionInvalidated);
        Assert.False(snapshot.SlotReadable);
        Assert.Null(snapshot.RawSlotValue);
        Assert.Equal("None", snapshot.ResolvedTargetMap);
    }

    [Fact]
    public void InvalidRegion_AllowsExactlyOneBoundedReacquisition()
    {
        const uint replacementBase = 0x600000;
        var memory = Memory((replacementBase + 0x5388, 0x1100));
        int enumerations = 0;
        var reader = Reader(memory, () =>
        {
            enumerations++;
            return [Region(replacementBase)];
        });

        NativeStableSelectionSlotSnapshot first = reader.Capture(42, "A1", "A");
        memory.Remove(replacementBase + 0x5388);
        NativeStableSelectionSlotSnapshot second = reader.Capture(42, "B1", "B");

        Assert.True(first.RegionInvalidated);
        Assert.True(first.ReacquisitionAttempted);
        Assert.True(first.RegionReacquired);
        Assert.Equal((nuint)RegionBase, first.OldRegionBase);
        Assert.Equal((nuint)replacementBase, first.NewRegionBase);
        Assert.True(second.RegionInvalidated);
        Assert.False(second.ReacquisitionAttempted);
        Assert.Equal(1, enumerations);
    }

    [Fact]
    public void Reader_DoesNotDereferenceThirdLevel()
    {
        var memory = Memory(
            (SlotAddress, 0x7000),
            (0x7000, 0x8000),
            (0x8000, 0x9000),
            (0x9000, 0x1100));

        NativeStableSelectionSlotSnapshot snapshot = Reader(memory)
            .Capture(42, "A1", "A");

        Assert.Equal((nuint)0x9000, snapshot.DereferencedValueLevel2);
        Assert.Equal("Unknown", snapshot.ResolvedTargetMap);
        Assert.DoesNotContain((nuint)0x9000, memory.ReadAddresses.Skip(1));
    }

    [Fact]
    public void RestartComparison_UsesNormalizedOffsetAndRegionShape()
    {
        NativeStableSelectionSlotSnapshot before = Reader(
            Memory((SlotAddress, 0x1100))).Capture(42, "A3", "A");
        const uint restartedBase = 0x700000;
        NativeStableSelectionSlotSnapshot after = Reader(
            Memory((restartedBase + 0x5388, 0x1100)),
            region: Region(restartedBase)).Capture(84, "A-restart", "A");

        Assert.NotEqual(before.SourceRegion.BaseAddress, after.SourceRegion.BaseAddress);
        Assert.True(NativeStableSelectionSlotRestartComparer.HasSameNormalizedShape(
            before, after));
    }

    [Fact]
    public void ExactTrackingPattern_RequiresSameRegionIdentity()
    {
        List<NativeStableSelectionSlotSnapshot> snapshots = Sequence(
            "A", "B", "A", "B", "A").ToList();
        snapshots[2] = snapshots[2] with
        {
            SourceRegion = Region(0x600000),
            SlotAddress = 0x605388
        };

        NativeStableSelectionSlotSequenceSummary summary =
            NativeStableSelectionSlotSequenceAnalyzer.Analyze(snapshots);

        Assert.False(summary.SameRegionBaseAcrossSequence);
        Assert.False(summary.SameSlotAddressAcrossSequence);
        Assert.False(summary.ExactSelectionTrackingPattern);
    }

    [Fact]
    public void ExactTrackingPattern_RequiresAllFiveCaptures()
    {
        NativeStableSelectionSlotSequenceSummary summary =
            NativeStableSelectionSlotSequenceAnalyzer.Analyze(
                Sequence("A", "B", "A", "B"));

        Assert.False(summary.ExactSelectionTrackingPattern);
    }

    [Fact]
    public void Acquisition_AcceptsIntermediateReferenceAtNormalizedOffset()
    {
        NativeStableReferenceDiscoveryResult discovery = Discovery(
            intermediate: [Intermediate(RegionBase, "A")]);

        NativeStableSelectionSlotCandidate candidate = Assert.Single(
            NativeStableSelectionSlotCandidateBuilder.Build(discovery));

        Assert.Equal(NativeStableSelectionSlotAcquisitionReferenceKind.IntermediateReadableReference,
            candidate.AcquisitionReferenceKind);
        Assert.Equal((nuint)SlotAddress, candidate.SlotAddress);
    }

    [Fact]
    public void Acquisition_AcceptsDirectReferenceAtNormalizedOffset()
    {
        NativeStableReferenceDiscoveryResult discovery = Discovery(
            direct: [Direct(RegionBase, "A")]);

        NativeStableSelectionSlotCandidate candidate = Assert.Single(
            NativeStableSelectionSlotCandidateBuilder.Build(discovery));

        Assert.Equal(NativeStableSelectionSlotAcquisitionReferenceKind.DirectReference,
            candidate.AcquisitionReferenceKind);
        Assert.Equal("A", candidate.InitialResolvedTargetMap);
    }

    [Fact]
    public void Acquisition_AcceptsBothReferenceForms()
    {
        NativeStableReferenceDiscoveryResult discovery = Discovery(
            direct: [Direct(RegionBase, "A")],
            intermediate: [Intermediate(0x600000, "B")]);

        IReadOnlyList<NativeStableSelectionSlotCandidate> candidates =
            NativeStableSelectionSlotCandidateBuilder.Build(discovery);

        Assert.Equal(2, candidates.Count);
        Assert.Contains(candidates, candidate => candidate.AcquisitionReferenceKind ==
            NativeStableSelectionSlotAcquisitionReferenceKind.DirectReference);
        Assert.Contains(candidates, candidate => candidate.AcquisitionReferenceKind ==
            NativeStableSelectionSlotAcquisitionReferenceKind.IntermediateReadableReference);
    }

    [Fact]
    public void Acquisition_RetainsMultipleNormalizedCandidatesWithoutExpectedMapFiltering()
    {
        NativeStableReferenceDiscoveryResult discovery = Discovery(
            direct: [Direct(RegionBase, "B"), Direct(0x600000, "A")],
            intermediate: [Intermediate(0x700000, "B")]);

        IReadOnlyList<NativeStableSelectionSlotCandidate> candidates =
            NativeStableSelectionSlotCandidateBuilder.Build(discovery);

        Assert.Equal(3, candidates.Count);
        Assert.Equal(["B", "A", "B"],
            candidates.Select(candidate => candidate.InitialResolvedTargetMap));
    }

    [Fact]
    public void Acquisition_ZeroCandidatesReportsExactGracefulFailure()
    {
        IReadOnlyList<NativeStableSelectionSlotCandidate> candidates =
            NativeStableSelectionSlotCandidateBuilder.Build(Discovery());

        string? failure = NativeStableSelectionSlotCandidateBuilder.GetFailure(candidates);

        Assert.Empty(candidates);
        Assert.Equal(
            "No C.3 source with regional offset 0x5388 was observed.", failure);
    }

    [Fact]
    public void Acquisition_ZeroCandidatesDoesNotRequireNonNullSessionAssertion()
    {
        Exception? exception = Record.Exception(() =>
        {
            IReadOnlyList<NativeStableSelectionSlotCandidate> candidates =
                NativeStableSelectionSlotCandidateBuilder.Build(Discovery());
            if (NativeStableSelectionSlotCandidateBuilder.GetFailure(candidates) is not null)
            {
                return;
            }
        });

        Assert.Null(exception);
    }

    [Fact]
    public void MultipleCandidates_AreSampledIndependently()
    {
        const uint secondBase = 0x600000;
        var memory = Memory(
            (SlotAddress, 0x1100),
            (secondBase + 0x5388, 0x2100));
        var readers = new NativeStableSelectionSlotCandidateSetReader(
            memory,
            [Candidate(RegionBase), Candidate(secondBase)],
            Targets());

        IReadOnlyList<NativeStableSelectionSlotCandidateCapture> captures =
            readers.Capture(42, "A1", "A");

        Assert.Equal(2, captures.Count);
        Assert.Equal("A", captures[0].Snapshot.ResolvedTargetMap);
        Assert.Equal("B", captures[1].Snapshot.ResolvedTargetMap);
        Assert.NotEqual(captures[0].Snapshot.SlotAddress, captures[1].Snapshot.SlotAddress);
    }

    [Fact]
    public void MultipleCandidates_OnlyExactTransitionFollowerValidates()
    {
        const uint secondBase = 0x600000;
        var memory = Memory(
            (SlotAddress, 0x1100),
            (secondBase + 0x5388, 0x1100));
        var readers = new NativeStableSelectionSlotCandidateSetReader(
            memory,
            [Candidate(RegionBase), Candidate(secondBase)],
            Targets());
        var first = new List<NativeStableSelectionSlotSnapshot>();
        var second = new List<NativeStableSelectionSlotSnapshot>();
        string[] states = ["A", "B", "A", "B", "A"];

        for (int index = 0; index < states.Length; index++)
        {
            memory.Set(SlotAddress, states[index] == "A" ? 0x1100u : 0x2100u);
            IReadOnlyList<NativeStableSelectionSlotCandidateCapture> captures =
                readers.Capture(42, $"capture-{index}", states[index]);
            first.Add(captures[0].Snapshot);
            second.Add(captures[1].Snapshot);
        }

        Assert.True(NativeStableSelectionSlotSequenceAnalyzer.Analyze(first)
            .ExactSelectionTrackingPattern);
        Assert.False(NativeStableSelectionSlotSequenceAnalyzer.Analyze(second)
            .ExactSelectionTrackingPattern);
    }

    private static IReadOnlyList<NativeStableSelectionSlotSnapshot> Sequence(
        params string[] maps)
    {
        var snapshots = new List<NativeStableSelectionSlotSnapshot>();
        for (int index = 0; index < maps.Length; index++)
        {
            uint target = maps[index] switch
            {
                "A" => 0x1100,
                "B" => 0x2100,
                _ => 0x7777
            };
            snapshots.Add(Reader(Memory((SlotAddress, target))).Capture(
                42, $"capture-{index}", index % 2 == 0 ? "A" : "B"));
        }

        return snapshots;
    }

    private static NativeStableReferenceDiscoveryResult Discovery(
        IReadOnlyList<NativeStableReferenceObservation>? direct = null,
        IReadOnlyList<NativeStableIntermediateReference>? intermediate = null) =>
        new(direct ?? [], [], intermediate ?? [], 0);

    private static NativeStableReferenceObservation Direct(
        uint regionBase,
        string map) =>
        new(
            NativeStableReferenceKind.DirectReference,
            regionBase + 0x5388,
            map == "A" ? 0x1100u : 0x2100u,
            map,
            NativeStableCandidateTargetKind.CandidateAddressMinus0x08,
            0,
            Region(regionBase),
            0x5388,
            0,
            -8,
            -0x58,
            1);

    private static NativeStableIntermediateReference Intermediate(
        uint regionBase,
        string map) =>
        new(
            regionBase + 0x5388,
            0x8000,
            map == "A" ? 0x1100u : 0x2100u,
            map,
            NativeStableCandidateTargetKind.CandidateAddressMinus0x08,
            0,
            Region(regionBase),
            0x5388,
            0,
            2);

    private static NativeStableSelectionSlotCandidate Candidate(uint regionBase) =>
        new(
            Region(regionBase),
            regionBase + 0x5388,
            0x5388,
            NativeStableSelectionSlotAcquisitionReferenceKind.DirectReference,
            "A",
            NativeStableSelectionTargetKind.CandidateAddressMinus0x08);

    private static NativeStableSelectionSlotReader Reader(
        MutableMemoryReader memory,
        Func<IReadOnlyList<NativeStableMemoryRegion>>? enumerate = null,
        NativeStableMemoryRegion? region = null) =>
        new(memory, region ?? Region(RegionBase), Targets(), enumerate);

    private static MutableMemoryReader Memory(
        params (uint Address, uint Value)[] values) => new(values);

    private static NativeStableCandidateTargetSet Targets() => new(
    [
        new("A", 0, NativeStableCandidateTargetKind.CandidateAddressMinus0x08,
            0x1100, 0x1108, 0x1158),
        new("B", 0, NativeStableCandidateTargetKind.CandidateAddressMinus0x08,
            0x2100, 0x2108, 0x2158)
    ],
    []);

    private static NativeStableMemoryRegion Region(uint baseAddress) =>
        new(baseAddress, 0x6000, NativeStableInterop.MemoryCommit, 0x04, 0x20000);

    private sealed class MutableMemoryReader : INativeStableMemoryReader
    {
        private readonly Dictionary<nuint, nuint> values;

        internal MutableMemoryReader(IEnumerable<(uint Address, uint Value)> initial)
        {
            values = initial.ToDictionary(
                item => checked((nuint)item.Address),
                item => checked((nuint)item.Value));
        }

        internal List<nuint> ReadAddresses { get; } = [];

        public bool Is32Bit => true;

        internal void Set(uint address, uint value) => values[address] = value;

        internal void Remove(uint address) => values.Remove(address);

        public NativeStableMemoryReadResult ReadBytes(nuint address, int count)
        {
            ReadAddresses.Add(address);
            if (count != sizeof(uint) || !values.TryGetValue(address, out nuint value))
            {
                return NativeStableMemoryReadResult.Failed;
            }

            byte[] bytes = BitConverter.GetBytes(checked((uint)value));
            return new(bytes, Complete: true, bytes.Length);
        }

        public bool TryReadPointer(nuint address, out nuint value) =>
            values.TryGetValue(address, out value) && value != 0;
    }
}
