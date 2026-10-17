using BeatInsight.Diagnostics.NativeStable;
using System.IO;

namespace BeatInsight.Tests.Diagnostics;

public sealed class NativeStableRootPhaseHarnessTests
{
    private static readonly DateTimeOffset ProcessStart =
        new(2026, 9, 28, 10, 20, 30, TimeSpan.Zero);

    [Fact]
    public void Calibrate_PersistsCompleteInitialCandidateSet()
    {
        string directory = NewDirectory();
        string path = Path.Combine(directory, "session.json");
        NativeStableRootDiagnosticSession original = Session(3);
        var store = new NativeStableRootDiagnosticSessionStore();

        store.Save(path, original);
        NativeStableRootDiagnosticSession loaded = store.Load(path);

        Assert.Equal(3, loaded.InitialCandidates.Length);
        Assert.Equal(new ulong[] { 0x1000, 0x1004, 0x1008 },
            loaded.InitialCandidates.Select(candidate => candidate.RootSlotAddress));
        Assert.Equal(new[] { 0, 1, 2 }, loaded.SurvivingCandidateIndexes);
    }

    [Fact]
    public void A1_LoadsExistingCandidatesAndRejectsNewRootSlot()
    {
        NativeStableRootDiagnosticSession session = Session(1);
        NativeStableRootTransitionCapture capture = new("A1",
        [
            Transition("A1", session.InitialCandidates[0], true),
            new("A1", 0x9999, 0x8888, 0x7777, 0x6666,
                true, true, 3, 0, 0, true)
        ], TimeSpan.Zero);

        Assert.Throws<InvalidDataException>(() => NativeStableRootPhaseHarness.ApplyCapture(
            session, NativeStableRootPhase.A1, 42, ProcessStart, capture));
        Assert.Single(session.InitialCandidates);
        Assert.Equal(NativeStableRootPhase.A1, session.NextPhase);
    }

    [Fact]
    public void FailedCandidate_IsRemoved()
    {
        NativeStableRootDiagnosticSession session = Session(2);
        Apply(session, NativeStableRootPhase.A1, true, false);
        Assert.Equal(new[] { 0 }, session.SurvivingCandidateIndexes);
    }

    [Fact]
    public void SurvivingCandidate_IsRetained()
    {
        NativeStableRootDiagnosticSession session = Session(1);
        Apply(session, NativeStableRootPhase.A1, true);
        Assert.Equal(new[] { 0 }, session.SurvivingCandidateIndexes);
    }

    [Fact]
    public void B1_UsesPersistedMapB()
    {
        NativeStableRootDiagnosticSession session = Session(1);
        Assert.Equal("b.osu", session.ExpectedMap(NativeStableRootPhase.B1).Filename);
    }

    [Fact]
    public void A2_UsesPersistedMapA()
    {
        NativeStableRootDiagnosticSession session = Session(1);
        Assert.Equal("a.osu", session.ExpectedMap(NativeStableRootPhase.A2).Filename);
    }

    [Fact]
    public void OutOfOrderPhase_FailsClosed()
    {
        NativeStableRootDiagnosticSession session = Session(1);
        Assert.Throws<InvalidDataException>(() =>
            NativeStableRootPhaseHarness.EnsureCanRun(session, NativeStableRootPhase.B1));
    }

    [Fact]
    public void ProcessIdMismatch_FailsClosed()
    {
        NativeStableRootDiagnosticSession session = Session(1);
        Assert.Throws<NativeStableProcessGenerationChangedException>(() =>
            NativeStableRootPhaseHarness.ApplyCapture(session, NativeStableRootPhase.A1,
                43, ProcessStart, Capture(session, NativeStableRootPhase.A1, true)));
    }

    [Fact]
    public void ProcessStartIdentityMismatch_FailsClosed()
    {
        NativeStableRootDiagnosticSession session = Session(1);
        Assert.Throws<NativeStableProcessGenerationChangedException>(() =>
            NativeStableRootPhaseHarness.ApplyCapture(session, NativeStableRootPhase.A1,
                42, ProcessStart.AddSeconds(1), Capture(session, NativeStableRootPhase.A1, true)));
    }

    [Fact]
    public void MissingOrCorruptSession_FailsClosed()
    {
        string directory = NewDirectory();
        var store = new NativeStableRootDiagnosticSessionStore();
        Assert.Throws<InvalidDataException>(() => store.Load(Path.Combine(directory, "missing.json")));
        string corrupt = Path.Combine(directory, "corrupt.json");
        File.WriteAllText(corrupt, "{ not-json }");
        Assert.Throws<InvalidDataException>(() => store.Load(corrupt));
    }

    [Fact]
    public void ZeroSurvivors_RemainsValidThroughAllPhases()
    {
        NativeStableRootDiagnosticSession session = Session(1);
        Apply(session, NativeStableRootPhase.A1, false);
        Apply(session, NativeStableRootPhase.B1);
        Apply(session, NativeStableRootPhase.A2);
        Apply(session, NativeStableRootPhase.B2);
        Apply(session, NativeStableRootPhase.A3);

        NativeStableRootTransitionSummary summary = NativeStableRootPhaseHarness.Finalize(session);
        Assert.Empty(session.SurvivingCandidateIndexes);
        Assert.Equal(NativeStableRootSlotDiscovery.None, summary.RootSlotDiscovery);
    }

    [Fact]
    public void Finalize_ClassifiesNoneUniqueAndAmbiguous()
    {
        Assert.Equal(NativeStableRootSlotDiscovery.None, Complete(0).RootSlotDiscovery);
        Assert.Equal(NativeStableRootSlotDiscovery.Unique, Complete(1).RootSlotDiscovery);
        Assert.Equal(NativeStableRootSlotDiscovery.Ambiguous, Complete(2).RootSlotDiscovery);
    }

    [Fact]
    public void Finalize_DoesNotMutateOrRepopulateCandidates()
    {
        NativeStableRootDiagnosticSession session = Session(2);
        Apply(session, NativeStableRootPhase.A1, true, false);
        Apply(session, NativeStableRootPhase.B1, true);
        Apply(session, NativeStableRootPhase.A2, true);
        Apply(session, NativeStableRootPhase.B2, true);
        Apply(session, NativeStableRootPhase.A3, true);
        ulong[] initial = session.InitialCandidates.Select(candidate => candidate.RootSlotAddress).ToArray();
        int[] survivors = session.SurvivingCandidateIndexes.ToArray();

        _ = NativeStableRootPhaseHarness.Finalize(session);

        Assert.Equal(initial, session.InitialCandidates.Select(candidate => candidate.RootSlotAddress));
        Assert.Equal(survivors, session.SurvivingCandidateIndexes);
    }

    [Fact]
    public void AbsoluteRootSlots_AreWrittenOnlyToExplicitDiagnosticSessionFile()
    {
        string directory = NewDirectory();
        string path = Path.Combine(directory, "explicit-diagnostic-session.json");
        var store = new NativeStableRootDiagnosticSessionStore();
        store.Save(path, Session(2));

        Assert.Equal([path], Directory.GetFiles(directory));
        Assert.Contains("4096", File.ReadAllText(path), StringComparison.Ordinal);
    }

    private static NativeStableRootTransitionSummary Complete(int candidateCount)
    {
        NativeStableRootDiagnosticSession session = Session(candidateCount);
        Apply(session, NativeStableRootPhase.A1, Enumerable.Repeat(true, candidateCount).ToArray());
        Apply(session, NativeStableRootPhase.B1, Enumerable.Repeat(true, candidateCount).ToArray());
        Apply(session, NativeStableRootPhase.A2, Enumerable.Repeat(true, candidateCount).ToArray());
        Apply(session, NativeStableRootPhase.B2, Enumerable.Repeat(true, candidateCount).ToArray());
        Apply(session, NativeStableRootPhase.A3, Enumerable.Repeat(true, candidateCount).ToArray());
        return NativeStableRootPhaseHarness.Finalize(session);
    }

    private static void Apply(
        NativeStableRootDiagnosticSession session,
        NativeStableRootPhase phase,
        params bool[] matches) => NativeStableRootPhaseHarness.ApplyCapture(
            session, phase, 42, ProcessStart, Capture(session, phase, matches));

    private static NativeStableRootTransitionCapture Capture(
        NativeStableRootDiagnosticSession session,
        NativeStableRootPhase phase,
        params bool[] matches)
    {
        Assert.Equal(session.SurvivingCandidateIndexes.Length, matches.Length);
        NativeStableRootCandidateTransition[] transitions = session.SurvivingCandidateIndexes
            .Select((index, position) => Transition(
                phase.ToString(), session.InitialCandidates[index], matches[position]))
            .ToArray();
        return new(phase.ToString(), transitions, TimeSpan.FromMilliseconds(2));
    }

    private static NativeStableRootCandidateTransition Transition(
        string phase,
        NativeStablePersistedRootCandidate candidate,
        bool matches) => new(
            phase,
            checked((nuint)candidate.RootSlotAddress),
            checked((nuint)candidate.QCandidateAddress),
            checked((nuint)(candidate.QCandidateAddress + 0x100)),
            checked((nuint)(candidate.GroundTruthX + 0x100)),
            true, true, matches ? 3 : 2, 0, 0, matches);

    private static NativeStableRootDiagnosticSession Session(int candidateCount)
    {
        NativeStableKnownBeatmapObjectMetadata mapA =
            new("C:\\Songs\\A\\a.osu", "aaa", "A", "a.osu", 1, 10);
        NativeStableKnownBeatmapObjectMetadata mapB =
            new("C:\\Songs\\B\\b.osu", "bbb", "B", "b.osu", 2, 20);
        NativeStableRootChainCandidate[] candidates = Enumerable.Range(0, candidateCount)
            .Select(index => Candidate(
                checked((nuint)(0x1000 + index * 4)),
                checked((nuint)(0x2000 + index * 4)), 0x3000))
            .ToArray();
        NativeStableRootCalibration calibration = new(
            0x3000,
            candidates.Select(candidate => candidate.QCandidateAddress).ToArray(),
            candidates,
            32,
            new(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2),
                TimeSpan.FromMilliseconds(3), TimeSpan.FromMilliseconds(6)));
        return NativeStableRootPhaseHarness.Create(
            42, ProcessStart, mapA, mapB, calibration,
            TimeSpan.FromMilliseconds(4), TimeSpan.FromMilliseconds(10));
    }

    private static NativeStableRootChainCandidate Candidate(nuint s, nuint q, nuint x)
    {
        NativeStableAddressMetadata metadata = new(
            s, 0x1000, 0x1000, 0x1000, 0x1000, 0x04, 0x20000,
            s - 0x1000, 0, NativeStableRegionBacking.Private);
        return new(s, q, x, metadata,
            metadata with { Address = q, OffsetFromRegionBase = q - 0x1000 },
            metadata with { Address = x, OffsetFromRegionBase = x - 0x1000 },
            true, true,
            new(s - 0x1000, 0x1000 - (s - 0x1000),
                [new(0, NativeStableNeighborhoodValueKind.ReadablePointerCandidate)]));
    }

    private static string NewDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "BeatInsightHarnessTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
