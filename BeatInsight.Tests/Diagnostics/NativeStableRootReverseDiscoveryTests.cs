using System.Text;
using BeatInsight.Diagnostics.NativeStable;

namespace BeatInsight.Tests.Diagnostics;

public sealed class NativeStableRootReverseDiscoveryTests
{
    private const uint Base = 0x1000;
    private const uint X1 = 0x8000;
    private const uint X2 = 0x9000;
    private const uint Q1 = 0x3000;
    private const uint Q2 = 0x4000;
    private const uint S1 = 0x1800;
    private const uint S2 = 0x1900;

    [Fact]
    public void XWithOneQAndOneS_ProducesOneChain()
    {
        SyntheticMemory memory = Memory((Q1, X1), (S1, Q1));

        NativeStableRootCalibration result = Calibrate(memory, X1);

        Assert.Equal(1, result.FirstLevelReferenceCount);
        NativeStableRootChainCandidate chain = Assert.Single(result.Chains);
        Assert.Equal((nuint)S1, chain.RootSlotAddress);
        Assert.Equal((nuint)Q1, chain.QCandidateAddress);
    }

    [Fact]
    public void XWithMultipleQReferences_RetainsAllReferences()
    {
        SyntheticMemory memory = Memory((Q1, X1), (Q2, X1));

        NativeStableRootCalibration result = Calibrate(memory, X1);

        Assert.Equal(2, result.FirstLevelReferenceCount);
        Assert.Empty(result.Chains);
    }

    [Fact]
    public void MultipleSReferencesToOneQ_RetainsAllChains()
    {
        SyntheticMemory memory = Memory((Q1, X1), (S1, Q1), (S2, Q1));

        NativeStableRootCalibration result = Calibrate(memory, X1);

        Assert.Equal(2, result.SecondLevelRootSlotCount);
        Assert.Equal(2, result.InitialChainCount);
    }

    [Fact]
    public void MultipleCompleteChains_AreNotSilentlyReduced()
    {
        SyntheticMemory memory = Memory(
            (Q1, X1), (Q2, X1), (S1, Q1), (S2, Q2));

        NativeStableRootCalibration result = Calibrate(memory, X1);

        Assert.Equal(2, result.QCandidateAddresses.Count);
        Assert.Equal(2, result.Chains.Count);
    }

    [Fact]
    public void NoFirstLevelReferences_ReturnsEmptyCalibration()
    {
        NativeStableRootCalibration result = Calibrate(Memory(), X1);

        Assert.Equal(0, result.FirstLevelReferenceCount);
        Assert.Equal(0, result.InitialChainCount);
    }

    [Fact]
    public void FirstLevelWithoutSecondLevel_ReturnsNoChains()
    {
        NativeStableRootCalibration result = Calibrate(Memory((Q1, X1)), X1);

        Assert.Single(result.QCandidateAddresses);
        Assert.Empty(result.Chains);
    }

    [Fact]
    public void InvalidUnreadableQ_IsReportedWithoutObjectValidation()
    {
        SyntheticMemory memory = Memory((S1, Q1));
        memory.MarkUnreadable(Q1);
        NativeStableRootChainCandidate candidate = Candidate(S1, Q1, X1);

        NativeStableRootCandidateTransition transition = Assert.Single(
            Capture(memory, [candidate], MetadataA(), "A1").Candidates);

        Assert.True(transition.PointerReadable);
        Assert.False(transition.FinalObjectReadable);
        Assert.False(transition.ExpectedMapMatches);
    }

    [Fact]
    public void InvalidUnreadableS_IsRejected()
    {
        SyntheticMemory memory = Memory();
        memory.MarkUnreadable(S1);
        NativeStableRootCandidateTransition transition = Assert.Single(
            Capture(memory, [Candidate(S1, Q1, X1)], MetadataA(), "A1").Candidates);

        Assert.False(transition.PointerReadable);
        Assert.False(transition.FinalObjectReadable);
        Assert.False(transition.ExpectedMapMatches);
    }

    [Fact]
    public void SSurvivesA_B_A_AndCompletesFivePhaseSequence()
    {
        SyntheticMemory memory = Memory((S1, Q1));
        NativeStableRootChainCandidate candidate = Candidate(S1, Q1, X1);
        List<NativeStableRootTransitionCapture> captures = RunSequence(
            memory, [candidate], qChanges: false, failB: false);

        NativeStableRootTransitionSummary summary =
            NativeStableRootTransitionValidator.Summarize([candidate], captures);

        Assert.Equal(NativeStableRootSurvival.SurvivesAllTransitions,
            Assert.Single(summary.Candidates).Classification);
    }

    [Fact]
    public void SFailsDuringB_IsPartial()
    {
        SyntheticMemory memory = Memory((S1, Q1));
        NativeStableRootChainCandidate candidate = Candidate(S1, Q1, X1);
        List<NativeStableRootTransitionCapture> captures = RunSequence(
            memory, [candidate], qChanges: false, failB: true);

        NativeStableRootTransitionSummary summary =
            NativeStableRootTransitionValidator.Summarize([candidate], captures);

        Assert.Equal(NativeStableRootSurvival.Partial,
            Assert.Single(summary.Candidates).Classification);
        Assert.Equal(NativeStableRootSlotDiscovery.None, summary.RootSlotDiscovery);
    }

    [Fact]
    public void QMayChangeWhileSRemainsStable()
    {
        SyntheticMemory memory = Memory((S1, Q1));
        NativeStableRootChainCandidate candidate = Candidate(S1, Q1, X1);
        List<NativeStableRootTransitionCapture> captures = RunSequence(
            memory, [candidate], qChanges: true, failB: false);

        Assert.Contains(captures.SelectMany(capture => capture.Candidates), item =>
            item.Phase == "B1" && item.CurrentQAddress == Q2 && item.ExpectedMapMatches);
        Assert.Equal(NativeStableRootSurvival.SurvivesAllTransitions,
            NativeStableRootTransitionValidator.Summarize([candidate], captures)
                .Candidates.Single().Classification);
    }

    [Fact]
    public void XMayChangeWhileSRemainsStable()
    {
        SyntheticMemory memory = Memory((S1, Q1));
        NativeStableRootChainCandidate candidate = Candidate(S1, Q1, X1);
        List<NativeStableRootTransitionCapture> captures = RunSequence(
            memory, [candidate], qChanges: false, failB: false);

        Assert.Contains(captures.SelectMany(capture => capture.Candidates), item =>
            item.Phase == "A1" && item.CurrentXAddress == X1);
        Assert.Contains(captures.SelectMany(capture => capture.Candidates), item =>
            item.Phase == "B1" && item.CurrentXAddress == X2);
    }

    [Fact]
    public void MultipleSSurvive_IsAmbiguous()
    {
        SyntheticMemory memory = Memory((S1, Q1), (S2, Q2));
        NativeStableRootChainCandidate[] candidates =
            [Candidate(S1, Q1, X1), Candidate(S2, Q2, X1)];
        var captures = new List<NativeStableRootTransitionCapture>();
        foreach ((string phase, bool mapA) in Phases())
        {
            SetExpectedObject(memory, X1, mapA ? MetadataA() : MetadataB());
            memory.Set(Q1, X1);
            memory.Set(Q2, X1);
            captures.Add(Capture(memory, candidates,
                mapA ? MetadataA() : MetadataB(), phase));
        }

        NativeStableRootTransitionSummary summary =
            NativeStableRootTransitionValidator.Summarize(candidates, captures);

        Assert.Equal(2, summary.SurvivingRootSlots);
        Assert.Equal(NativeStableRootSlotDiscovery.Ambiguous, summary.RootSlotDiscovery);
    }

    [Fact]
    public void ExactlyOneSSurvives_IsUnique()
    {
        SyntheticMemory memory = Memory((S1, Q1), (S2, Q2));
        NativeStableRootChainCandidate[] candidates =
            [Candidate(S1, Q1, X1), Candidate(S2, Q2, X1)];
        var captures = new List<NativeStableRootTransitionCapture>();
        foreach ((string phase, bool mapA) in Phases())
        {
            SetExpectedObject(memory, X1, mapA ? MetadataA() : MetadataB());
            memory.Set(Q1, X1);
            memory.Set(Q2, mapA ? X1 : 0);
            captures.Add(Capture(memory, candidates,
                mapA ? MetadataA() : MetadataB(), phase));
        }

        NativeStableRootTransitionSummary summary =
            NativeStableRootTransitionValidator.Summarize(candidates, captures);

        Assert.Equal(1, summary.SurvivingRootSlots);
        Assert.Equal(NativeStableRootSlotDiscovery.Unique, summary.RootSlotDiscovery);
    }

    [Fact]
    public void ProcessGenerationChange_Aborts()
    {
        DateTimeOffset start = DateTimeOffset.Parse("2026-09-27T19:18:54Z");

        Assert.Throws<NativeStableProcessGenerationChangedException>(() =>
            NativeStableProcessGenerationGuard.EnsureSame(10, start, 11, start));
        Assert.Throws<NativeStableProcessGenerationChangedException>(() =>
            NativeStableProcessGenerationGuard.EnsureSame(10, start, 10, start.AddSeconds(1)));
    }

    [Fact]
    public void CandidateOutputBounding_DoesNotAffectInternalFiltering()
    {
        SyntheticMemory memory = Memory(
            (Q1, X1), (Q2, X1), (S1, Q1), (S2, Q2));

        NativeStableRootCalibration result = NativeStableRootReverseDiscovery.Calibrate(
            memory, memory.Regions, X1, presentedCandidateLimit: 1);

        Assert.Single(result.PresentedCandidates);
        Assert.Equal(2, result.Chains.Count);
        Assert.Equal(2, result.InitialChainCount);
    }

    private static NativeStableRootCalibration Calibrate(
        SyntheticMemory memory,
        uint x) => NativeStableRootReverseDiscovery.Calibrate(memory, memory.Regions, x);

    private static NativeStableRootTransitionCapture Capture(
        SyntheticMemory memory,
        IReadOnlyList<NativeStableRootChainCandidate> candidates,
        NativeStableKnownBeatmapObjectMetadata metadata,
        string phase) => NativeStableRootTransitionValidator.Capture(
            memory, memory.Regions, candidates, metadata, phase);

    private static List<NativeStableRootTransitionCapture> RunSequence(
        SyntheticMemory memory,
        IReadOnlyList<NativeStableRootChainCandidate> candidates,
        bool qChanges,
        bool failB)
    {
        var captures = new List<NativeStableRootTransitionCapture>();
        foreach ((string phase, bool mapA) in Phases())
        {
            NativeStableKnownBeatmapObjectMetadata metadata = mapA ? MetadataA() : MetadataB();
            uint x = mapA ? X1 : X2;
            uint q = qChanges && !mapA ? Q2 : Q1;
            memory.Set(S1, q);
            memory.Set(q, failB && !mapA ? 0u : x);
            SetExpectedObject(memory, x, metadata);
            captures.Add(Capture(memory, candidates, metadata, phase));
        }
        return captures;
    }

    private static IEnumerable<(string Phase, bool MapA)> Phases()
    {
        yield return ("A1", true);
        yield return ("B1", false);
        yield return ("A2", true);
        yield return ("B2", false);
        yield return ("A3", true);
    }

    private static void SetExpectedObject(
        SyntheticMemory memory,
        uint x,
        NativeStableKnownBeatmapObjectMetadata metadata)
    {
        uint checksum = x + 0x400;
        uint folder = x + 0x500;
        uint filename = x + 0x600;
        memory.SetManagedString(checksum, metadata.Checksum);
        memory.SetManagedString(folder, metadata.Folder);
        memory.SetManagedString(filename, metadata.Filename);
        memory.Set(x + 0x6c, checksum);
        memory.Set(x + 0x78, folder);
        memory.Set(x + 0x90, filename);
        memory.Set(x + 0xc8, checked((uint)metadata.BeatmapId!.Value));
        memory.Set(x + 0xcc, checked((uint)metadata.BeatmapSetId!.Value));
    }

    private static NativeStableKnownBeatmapObjectMetadata MetadataA() =>
        new("A.osu", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "A folder", "A.osu", 1, 10);

    private static NativeStableKnownBeatmapObjectMetadata MetadataB() =>
        new("B.osu", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "B folder", "B.osu", 2, 20);

    private static NativeStableRootChainCandidate Candidate(uint s, uint q, uint x)
    {
        NativeStableAddressMetadata metadata = new(
            s, Base, Base, 0x10000, NativeStableInterop.MemoryCommit,
            0x04, NativeStableInterop.MemoryPrivate, s - Base, 0,
            NativeStableRegionBacking.Private);
        return new(s, q, x, metadata, metadata with { Address = q },
            metadata with { Address = x }, true, true,
            NativeStableNeighborhoodFingerprint.Unavailable);
    }

    private static SyntheticMemory Memory(params (uint Address, uint Value)[] values)
    {
        var memory = new SyntheticMemory();
        foreach ((uint address, uint value) in values)
        {
            memory.Set(address, value);
        }
        return memory;
    }

    private sealed class SyntheticMemory : INativeStableMemoryReader
    {
        private readonly byte[] data = new byte[0x10000];
        private readonly HashSet<uint> unreadable = [];

        internal IReadOnlyList<NativeStableMemoryRegion> Regions { get; } =
        [new(Base, 0x10000, NativeStableInterop.MemoryCommit, 0x04,
            NativeStableInterop.MemoryPrivate, Base)];

        public bool Is32Bit => true;

        internal void Set(uint address, uint value) =>
            BitConverter.GetBytes(value).CopyTo(data, checked((int)(address - Base)));

        internal void MarkUnreadable(uint address) => unreadable.Add(address);

        internal void SetManagedString(uint address, string value)
        {
            Set(address, 0x12345678);
            BitConverter.GetBytes(value.Length).CopyTo(data,
                checked((int)(address - Base + sizeof(uint))));
            Encoding.Unicode.GetBytes(value).CopyTo(data,
                checked((int)(address - Base + sizeof(uint) + sizeof(int))));
        }

        public NativeStableMemoryReadResult ReadBytes(nuint address, int count)
        {
            if (address < Base || address + checked((nuint)count) >
                checked((nuint)Base) + checked((nuint)data.Length) ||
                unreadable.Contains(checked((uint)address)))
            {
                return NativeStableMemoryReadResult.Failed;
            }
            byte[] result = data.AsSpan(checked((int)(address - Base)), count).ToArray();
            return new(result, Complete: true, result.Length);
        }

        public bool TryReadPointer(nuint address, out nuint value)
        {
            NativeStableMemoryReadResult read = ReadBytes(address, sizeof(uint));
            value = read.Complete ? BitConverter.ToUInt32(read.Data, 0) : 0;
            return read.Complete && value != 0;
        }
    }
}
