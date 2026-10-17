using BeatInsight.Diagnostics.NativeStable;
using System.IO;

namespace BeatInsight.Tests.Diagnostics;

public sealed class NativeStableQRestartExperimentTests
{
    private static readonly DateTimeOffset GenerationAStart =
        new(2026, 9, 27, 19, 18, 54, TimeSpan.Zero);
    private static readonly DateTimeOffset GenerationBStart =
        GenerationAStart.AddHours(2);

    [Fact]
    public void GenerationBRequiresDifferentProcessStartIdentity()
    {
        NativeStableQRestartExperimentSession session = Session();
        NativeStableQRestartExperiment.ApplyGenerationBCalibration(
            session, Generation(84, GenerationBStart, 1));
        Assert.Equal(GenerationBStart, session.GenerationB!.ProcessStartIdentity);
    }

    [Fact]
    public void SamePidWithDifferentStartIdentity_IsValidNewGeneration()
    {
        NativeStableQRestartExperimentSession session = Session();
        NativeStableQRestartExperiment.ApplyGenerationBCalibration(
            session, Generation(42, GenerationBStart, 1));
        Assert.Equal(42, session.GenerationB!.ProcessId);
    }

    [Fact]
    public void SameProcessStartIdentity_IsRejected()
    {
        NativeStableQRestartExperimentSession session = Session();
        Assert.Throws<InvalidOperationException>(() =>
            NativeStableQRestartExperiment.ApplyGenerationBCalibration(
                session, Generation(84, GenerationAStart, 1)));
    }

    [Fact]
    public void GenerationBDiscovery_DoesNotUseHistoricalQRTAddresses()
    {
        var memory = new SyntheticMemory();
        memory.WriteUInt32(0x1100, 0x9000); // Historical decoy X.
        memory.WriteUInt32(0x2200, 0xa000); // Independently discovered GenerationB X.
        IReadOnlyList<nuint> result = NativeStableQRestartExperiment
            .DiscoverGenerationBQCandidates(memory, [Region(0x1000, 0x2000)], 0xa000);
        Assert.Equal(new nuint[] { 0x2200 }, result);
    }

    [Fact]
    public void AbsoluteQMayChangeWhileStructureRemainsEquivalent()
    {
        NativeStableRootStructuralProfile first = Profile(0x2000, 0x120);
        NativeStableRootStructuralProfile second = Profile(0x9000, 0x120);
        Assert.All(NativeStableStructuralComparator.Compare(first, second), item =>
            Assert.Equal(NativeStableInvariantStatus.ReproducedExactly, item.Status));
    }

    [Fact]
    public void AllocationOffsetChange_DoesNotInvalidateWholeComparison()
    {
        NativeStableInvariantComparison[] result = NativeStableStructuralComparator.Compare(
            Profile(0x2000, 0x120), Profile(0x9000, 0x220));
        Assert.Contains(result, item => item.Name == "Q.OffsetFromAllocation" &&
            item.Status == NativeStableInvariantStatus.Changed);
        Assert.Contains(result, item => item.Name == "Q.Backing" &&
            item.Status == NativeStableInvariantStatus.ReproducedExactly);
    }

    [Fact]
    public void MultipleSurvivingQCandidates_IsAmbiguous()
    {
        NativeStableQRestartExperimentSession session = Completed(2, [true, true]);
        Assert.Equal(NativeStableRestartDiscovery.Ambiguous,
            NativeStableQRestartExperiment.Finalize(session).Discovery);
    }

    [Fact]
    public void ZeroQCandidate_IsNone()
    {
        NativeStableQRestartExperimentSession session = Completed(0, []);
        Assert.Equal(NativeStableRestartDiscovery.None,
            NativeStableQRestartExperiment.Finalize(session).Discovery);
    }

    [Fact]
    public void AtoBtoA_EliminatesFalseQAndRetainsFollower()
    {
        NativeStableQRestartExperimentSession session = Session();
        NativeStableQRestartExperiment.ApplyGenerationBCalibration(
            session, Generation(84, GenerationBStart, 2));
        Apply(session, NativeStableQRestartPhase.RestartCaptureB, true, false);
        Apply(session, NativeStableQRestartPhase.RestartCaptureA2, true);
        Assert.Equal(new[] { 0 }, session.GenerationB!.SurvivorIndexes);
        Assert.Equal(NativeStableRestartDiscovery.Unique, session.GenerationB.Discovery);
    }

    [Fact]
    public void UnreadableQPointer_FailsClosed()
    {
        var memory = new SyntheticMemory { FailReads = true };
        NativeStableRestartCandidateTransition result = NativeStableQRestartLiveHarness.Validate(
            memory, [Region(0x1000, 0x1000)], 0x1100,
            new("a", "a", "a", "a.osu", 1, 2), 0);
        Assert.False(result.PointerReadable);
        Assert.False(result.ExpectedMapMatches);
    }

    [Fact]
    public void MalformedSessionJson_FailsClosed()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{bad");
        Assert.Throws<InvalidDataException>(() => new NativeStableQRestartStore().Load(path));
    }

    [Fact]
    public void OutOfOrderPhase_FailsClosed()
    {
        Assert.Throws<InvalidDataException>(() => NativeStableQRestartExperiment.EnsurePhase(
            Session(), NativeStableQRestartPhase.RestartCaptureB));
    }

    [Fact]
    public void Finalize_PerformsNoMemoryScan()
    {
        NativeStableQRestartComparison result =
            NativeStableQRestartExperiment.Finalize(Completed(1, [true]));
        Assert.False(result.MemoryScanPerformed);
    }

    [Fact]
    public void StructuralComparator_ReportsAllFourStatuses()
    {
        NativeStableStructuralFingerprint exact = Fingerprint(
            NativeStableStructuralValueKind.Zero,
            NativeStableStructuralValueKind.SmallInteger,
            NativeStableStructuralValueKind.ReadablePointerCandidate,
            NativeStableStructuralValueKind.UnknownValue,
            NativeStableStructuralValueKind.Zero);
        Assert.Equal(NativeStableInvariantStatus.ReproducedExactly,
            NativeStableStructuralComparator.CompareNeighborhood(exact, exact).Status);
        Assert.Equal(NativeStableInvariantStatus.ReproducedStructurally,
            NativeStableStructuralComparator.CompareNeighborhood(exact, Fingerprint(
                NativeStableStructuralValueKind.Zero,
                NativeStableStructuralValueKind.SmallInteger,
                NativeStableStructuralValueKind.ReadablePointerCandidate,
                NativeStableStructuralValueKind.UnknownValue,
                NativeStableStructuralValueKind.SmallInteger)).Status);
        Assert.Equal(NativeStableInvariantStatus.Changed,
            NativeStableStructuralComparator.CompareNeighborhood(exact, Fingerprint(
                NativeStableStructuralValueKind.SmallInteger,
                NativeStableStructuralValueKind.SmallInteger,
                NativeStableStructuralValueKind.SmallInteger,
                NativeStableStructuralValueKind.SmallInteger,
                NativeStableStructuralValueKind.SmallInteger)).Status);
        Assert.Equal(NativeStableInvariantStatus.UnknownInsufficientEvidence,
            NativeStableStructuralComparator.CompareNeighborhood(new(0x80, []), exact).Status);
    }

    [Fact]
    public void CompleteCandidateSetPersists_PresentationLimitOnlyBoundsDisplay()
    {
        NativeStableQRestartExperimentSession session = Session();
        NativeStableQRestartExperiment.ApplyGenerationBCalibration(
            session, Generation(84, GenerationBStart, 40));
        string directory = Path.Combine(Path.GetTempPath(), "BeatInsightRestartTests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "session.json");
        var store = new NativeStableQRestartStore();
        store.Save(path, session);
        NativeStableRestartGeneration loaded = store.Load(path).GenerationB!;
        Assert.Equal(40, loaded.InitialCandidates.Length);
        Assert.Equal(40, loaded.SurvivorIndexes.Length);
        Assert.Equal(32, loaded.PresentationLimit);
    }

    private static NativeStableQRestartExperimentSession Completed(
        int count, bool[] survivors)
    {
        NativeStableQRestartExperimentSession session = Session();
        NativeStableQRestartExperiment.ApplyGenerationBCalibration(
            session, Generation(84, GenerationBStart, count));
        Apply(session, NativeStableQRestartPhase.RestartCaptureB, survivors);
        Apply(session, NativeStableQRestartPhase.RestartCaptureA2,
            Enumerable.Repeat(true, survivors.Count(value => value)).ToArray());
        return session;
    }

    private static void Apply(
        NativeStableQRestartExperimentSession session,
        NativeStableQRestartPhase phase,
        params bool[] matches)
    {
        NativeStableRestartGeneration generation = session.GenerationB!;
        Assert.Equal(generation.SurvivorIndexes.Length, matches.Length);
        NativeStableRestartCandidateTransition[] transitions = generation.SurvivorIndexes
            .Select((index, position) => new NativeStableRestartCandidateTransition(
                index,
                generation.InitialCandidates[index].InitialQAddress,
                matches[position] ? checked((ulong)(0x9000 + index * 4)) : null,
                true,
                matches[position],
                matches[position] ? 3 : 0,
                matches[position] ? 2 : 0,
                2,
                matches[position]))
            .ToArray();
        NativeStableQCharacterizationCapture[] captures = transitions
            .Where(item => item.ExpectedMapMatches)
            .Select(item => Capture(item.QAddress, phase.ToString()))
            .ToArray();
        NativeStableQRestartExperiment.ApplyTransition(
            session, phase, generation.ProcessId, generation.ProcessStartIdentity,
            new(phase.ToString(), transitions, captures, 1, new()));
    }

    private static NativeStableQRestartExperimentSession Session() => new()
    {
        SchemaVersion = 1,
        GenerationA = Generation(42, GenerationAStart, 1),
        GenerationAProfile = Profile(0x2000, 0x120),
        GenerationB = null,
        NextPhase = NativeStableQRestartPhase.RestartCalibrateA
    };

    private static NativeStableRestartGeneration Generation(
        int processId, DateTimeOffset started, int candidateCount)
    {
        NativeStableRestartCandidate[] candidates = Enumerable.Range(0, candidateCount)
            .Select(index => new NativeStableRestartCandidate(
                checked((ulong)(0x2000 + index * 4)),
                Capture(checked((ulong)(0x2000 + index * 4)), "RestartCalibrateA")))
            .ToArray();
        return new()
        {
            ProcessId = processId,
            ProcessStartIdentity = started,
            MapA = new("a", "a", "a", "a.osu", 1, 2),
            MapB = new("b", "b", "b", "b.osu", 3, 4),
            XMapA = 0x9000,
            AllObservedQAddresses = candidates.Select(candidate => candidate.InitialQAddress).ToArray(),
            InitialCandidates = candidates,
            SurvivorIndexes = Enumerable.Range(0, candidateCount).ToArray(),
            TransitionPhases = [],
            Timings = new(),
            PresentationLimit = 32
        };
    }

    private static NativeStableRootStructuralProfile Profile(ulong q, ulong allocationOffset) =>
        NativeStableRootStructuralProfile.FromCapture(Capture(q, "CaptureA", allocationOffset));

    private static NativeStableQCharacterizationCapture Capture(
        ulong q, string phase, ulong allocationOffset = 0x120)
    {
        NativeStableObservedAddress address = new(
            q, q - 0x20, q - allocationOffset, allocationOffset, 0x20,
            0x1000, 0x1000, 0x04, 0x20000, 0,
            NativeStableRegionBacking.Private, null);
        return new(phase, q, q + 0x7000, address,
            Fingerprint(NativeStableStructuralValueKind.Zero), [], [], [],
            0, [], 3, 2, 2, true, 1);
    }

    private static NativeStableStructuralFingerprint Fingerprint(
        params NativeStableStructuralValueKind[] kinds) => new(
            0x80,
            kinds.Select((kind, index) =>
                new NativeStableStructuralCell(index * 4, kind, null)).ToArray());

    private static NativeStableMemoryRegion Region(nuint address, nuint size) =>
        new(address, size, 0x1000, 0x04, 0x20000, address);

    private sealed class SyntheticMemory : INativeStableMemoryReader
    {
        private readonly Dictionary<nuint, byte> bytes = [];
        internal bool FailReads { get; init; }
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
            if (FailReads)
            {
                return NativeStableMemoryReadResult.Failed;
            }
            byte[] result = new byte[count];
            for (int index = 0; index < count; index++)
            {
                bytes.TryGetValue(address + checked((nuint)index), out result[index]);
            }
            return new(result, true, count);
        }

        public bool TryReadPointer(nuint address, out nuint value)
        {
            NativeStableMemoryReadResult result = ReadBytes(address, sizeof(uint));
            value = result.Complete ? BitConverter.ToUInt32(result.Data, 0) : 0;
            return result.Complete;
        }
    }
}
