using BeatInsight.Diagnostics.NativeStable;
using System.IO;
using System.Reflection;

namespace BeatInsight.Tests.Diagnostics;

public sealed class NativeStableAutonomousQLocatorTests
{
    private static readonly DateTimeOffset Started =
        new(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ExactOffsetFingerprintAndValidX_IsUnique()
    {
        TestSpace space = Space(0x1100);
        NativeStableAutonomousQLocatorResult result = Locate(space, Profile(space, 0x100));
        Assert.Equal(NativeStableAutonomousQDiscovery.Unique, result.Discovery);
        Assert.Equal(0x1100UL, result.SelectedQAddress);
    }

    [Fact]
    public void ExactOffsetWithWrongFingerprint_IsRejected()
    {
        TestSpace space = Space(0x1100);
        NativeStableAutonomousQProfile profile = Profile(space, 0x100) with
        {
            Neighborhood = new(0x80,
                Enumerable.Range(0, 65).Select(index => new NativeStableStructuralCell(
                    -0x80 + index * 4,
                    NativeStableStructuralValueKind.ModulePointerCandidate,
                    new(1, 0x20, 0x1000000, NativeStableRegionBacking.ModuleBacked,
                        "missing.dll", 1))).ToArray())
        };
        Assert.Equal(NativeStableAutonomousQDiscovery.None, Locate(space, profile).Discovery);
    }

    [Fact]
    public void ChangedOffsetWithFallbackFingerprint_IsAccepted()
    {
        TestSpace reference = Space(0x1100);
        TestSpace changed = Space(0x1200);
        NativeStableAutonomousQLocatorResult result = Locate(
            changed, Profile(reference, 0x100),
            new(FallbackRadius: 0x200));
        Assert.Equal(NativeStableAutonomousQDiscovery.Unique, result.Discovery);
        Assert.Equal(0x1200UL, result.SelectedQAddress);
        Assert.True(result.UsedFallback);
    }

    [Fact]
    public void MultipleSimilarValidCandidates_IsAmbiguous()
    {
        TestSpace space = Space(0x1100, 0x1200);
        NativeStableAutonomousQProfile profile = Profile(space, 0x100) with
        {
            ObservedAllocationOffsets = [0x100, 0x200]
        };
        Assert.Equal(NativeStableAutonomousQDiscovery.Ambiguous,
            Locate(space, profile).Discovery);
    }

    [Fact]
    public void ClearlyDominantCandidateByMargin_IsUnique()
    {
        TestSpace space = Space(0x1100, 0x1200);
        for (nuint offset = 4; offset <= 20; offset += 4)
        {
            space.Memory.WriteUInt32(0x1200 + offset, 7);
        }
        NativeStableAutonomousQProfile profile = Profile(space, 0x100) with
        {
            ObservedAllocationOffsets = [0x100, 0x200],
            MinimumNeighborhoodSimilarity = 0.50
        };
        NativeStableAutonomousQLocatorResult result = Locate(space, profile,
            new(UniqueMargin: 12, FallbackRadius: 0x200));
        Assert.Equal(NativeStableAutonomousQDiscovery.Unique, result.Discovery);
        Assert.Equal(0x1100UL, result.SelectedQAddress);
    }

    [Fact]
    public void NoCandidate_IsNone()
    {
        TestSpace reference = Space(0x1100);
        TestSpace empty = new(new SyntheticMemory(), reference.Regions, []);
        Assert.Equal(NativeStableAutonomousQDiscovery.None,
            Locate(empty, Profile(reference, 0x100), new(EnableFallback: false)).Discovery);
    }

    [Fact]
    public void UnreadableQ_IsRejected()
    {
        TestSpace reference = Space(0x1100);
        reference.Memory.Unreadable.Add(0x1100);
        Assert.Equal(NativeStableAutonomousQDiscovery.None,
            Locate(reference, Profile(Space(0x1100), 0x100)).Discovery);
    }

    [Fact]
    public void ReadableQWithInvalidX_IsRejected()
    {
        TestSpace reference = Space(0x1100);
        reference.Memory.WriteUInt32(0x1100, 0x7000);
        Assert.Equal(NativeStableAutonomousQDiscovery.None,
            Locate(reference, Profile(Space(0x1100), 0x100)).Discovery);
    }

    [Fact]
    public void MalformedBeatmapFields_AreRejected()
    {
        TestSpace reference = Space(0x1100);
        reference.Memory.WriteManagedString(0x6000, "not-a-checksum");
        Assert.False(NativeStableAutonomousBeatmapObjectValidator.Validate(
            reference.Memory, 0x5100).IsValid);
    }

    [Fact]
    public void ProcessGenerationChange_InvalidatesCache()
    {
        TestSpace space = Space(0x1100);
        var locator = new NativeStableAutonomousQLocator();
        NativeStableAutonomousQProfile profile = Profile(space, 0x100);
        _ = locator.Locate(space.Memory, space.Regions, [], 1, Started, profile);
        NativeStableAutonomousQLocatorResult result = locator.Locate(
            space.Memory, space.Regions, [], 2, Started.AddMinutes(1), profile);
        Assert.False(result.UsedCache);
    }

    [Fact]
    public void SameProcessIdentity_ReusesCacheSafely()
    {
        TestSpace space = Space(0x1100);
        var locator = new NativeStableAutonomousQLocator();
        NativeStableAutonomousQProfile profile = Profile(space, 0x100);
        _ = locator.Locate(space.Memory, space.Regions, [], 1, Started, profile);
        NativeStableAutonomousQLocatorResult result = locator.Locate(
            space.Memory, space.Regions, [], 1, Started, profile);
        Assert.True(result.UsedCache);
    }

    [Fact]
    public void CachedQ_IsRevalidatedBeforeUse()
    {
        TestSpace space = Space(0x1100);
        var locator = new NativeStableAutonomousQLocator();
        NativeStableAutonomousQProfile profile = Profile(space, 0x100);
        _ = locator.Locate(space.Memory, space.Regions, [], 1, Started, profile);
        NativeStableAutonomousQLocatorResult result = locator.Locate(
            space.Memory, space.Regions, [], 1, Started, profile);
        Assert.True(result.CacheRevalidated);
        Assert.True(result.Candidates.Single().ObjectEvidence.IsValid);
    }

    [Fact]
    public void StaleCachedQ_FallsBackToDiscovery()
    {
        TestSpace space = Space(0x1200);
        var locator = new NativeStableAutonomousQLocator();
        locator.SeedCacheForTesting(1, Started, 0x1100);
        NativeStableAutonomousQLocatorResult result = locator.Locate(
            space.Memory, space.Regions, [], 1, Started, Profile(space, 0x200));
        Assert.False(result.UsedCache);
        Assert.Equal(0x1200UL, result.SelectedQAddress);
    }

    [Fact]
    public void LocatorApi_RequiresNoExpectedMapPath()
    {
        MethodInfo method = typeof(NativeStableAutonomousQLocator)
            .GetMethod("Locate", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.DoesNotContain(method.GetParameters(), parameter =>
            parameter.Name!.Contains("map", StringComparison.OrdinalIgnoreCase) ||
            parameter.ParameterType == typeof(string));
    }

    [Fact]
    public void HarnessTruthValidation_IsIndependentFromLocatorDecision()
    {
        TestSpace space = Space(0x1100);
        NativeStableAutonomousQLocatorResult locator = Locate(space, Profile(space, 0x100));
        NativeStableAutonomousHarnessTruth truth = Truth(0x1100, true);
        NativeStableAutonomousQValidationSession session =
            NativeStableAutonomousQHarness.Create(Profile(space, 0x100));
        NativeStableAutonomousQHarness.ApplyStartup(session, "C", locator, [truth]);
        Assert.True(session.Generations[0].TruthA.Single().ExpectedMapMatches);
    }

    [Fact]
    public void HeavyRTScan_IsDisabledByDefault()
    {
        TestSpace space = Space(0x1100);
        NativeStableAutonomousQLocatorResult result = Locate(space, Profile(space, 0x100));
        Assert.False(result.HeavyTopologyScanPerformed);
        Assert.False(new NativeStableAutonomousQLocatorOptions().EnableHeavyTopologyDiagnostics);
    }

    [Fact]
    public void FinalizeAll_PerformsNoMemoryReads()
    {
        NativeStableAutonomousQValidationSession session = CompletedHarnessSession();
        NativeStableAutonomousQComparison result =
            NativeStableAutonomousQHarness.FinalizeAll(session);
        Assert.False(result.MemoryReadPerformed);
    }

    [Fact]
    public void HistoricalAbsoluteQAddresses_AreAbsentFromLocatorSource()
    {
        string source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "BeatInsight", "Diagnostics", "NativeStable",
            "NativeStableAutonomousQLocator.cs"));
        string text = File.ReadAllText(source);
        Assert.DoesNotContain("04854710", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("049E4710", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("049E4F38", text, StringComparison.OrdinalIgnoreCase);
    }

    private static NativeStableAutonomousQValidationSession CompletedHarnessSession()
    {
        TestSpace space = Space(0x1100);
        NativeStableAutonomousQProfile profile = Profile(space, 0x100);
        NativeStableAutonomousQValidationSession session =
            NativeStableAutonomousQHarness.Create(profile);
        foreach (string label in new[] { "C", "D", "E" })
        {
            int number = label[0] - 'A';
            NativeStableAutonomousQLocatorResult result = Locate(space, profile) with
            {
                ProcessId = number,
                ProcessStartIdentity = Started.AddHours(number)
            };
            NativeStableAutonomousQHarness.ApplyStartup(
                session, label, result, [Truth(0x1100, true)]);
            NativeStableAutonomousQHarness.ApplyTransition(
                session, label, "B", number, result.ProcessStartIdentity,
                [Truth(0x1100, true)]);
            NativeStableAutonomousQHarness.ApplyTransition(
                session, label, "A2", number, result.ProcessStartIdentity,
                [Truth(0x1100, true)]);
            _ = NativeStableAutonomousQHarness.FinalizeGeneration(session, label);
        }
        return session;
    }

    private static NativeStableAutonomousHarnessTruth Truth(ulong q, bool matches) =>
        new(q, matches ? q + 0x4000 : null, true, matches,
            matches ? 3 : 0, matches ? 2 : 0, 2, matches);

    private static NativeStableAutonomousQLocatorResult Locate(
        TestSpace space,
        NativeStableAutonomousQProfile profile,
        NativeStableAutonomousQLocatorOptions? options = null) =>
        new NativeStableAutonomousQLocator().Locate(
            space.Memory, space.Regions, [], 1, Started, profile, options);

    private static NativeStableAutonomousQProfile Profile(TestSpace space, ulong offset)
    {
        NativeStableMemoryRegion qRegion = space.Regions[0];
        nuint q = qRegion.AllocationBase + checked((nuint)offset);
        NativeStableStructuralFingerprint fingerprint =
            NativeStableRootCharacterizer.Fingerprint(
                space.Memory, space.Regions, [], q);
        return new(1, NativeStableRegionBacking.Private, 0x04, 0x20000, 0,
            checked((ulong)qRegion.Size), [offset], [offset], fingerprint, 0.80);
    }

    private static TestSpace Space(params nuint[] qAddresses)
    {
        var memory = new SyntheticMemory();
        var regions = new[]
        {
            new NativeStableMemoryRegion(0x1000, 0x1000, 0x1000, 0x04, 0x20000, 0x1000),
            new NativeStableMemoryRegion(0x5000, 0x3000, 0x1000, 0x04, 0x40000, 0x5000)
        };
        foreach (nuint q in qAddresses)
        {
            memory.WriteUInt32(q, 0x5100);
        }
        memory.WriteUInt32(0x5100 + NativeStableBeatmapObjectDiscovery.ChecksumOffset, 0x6000);
        memory.WriteUInt32(0x5100 + NativeStableBeatmapObjectDiscovery.FolderOffset, 0x6100);
        memory.WriteUInt32(0x5100 + NativeStableBeatmapObjectDiscovery.FilenameOffset, 0x6200);
        memory.WriteInt32(0x5100 + NativeStableBeatmapObjectDiscovery.BeatmapIdOffset, 123);
        memory.WriteInt32(0x5100 + NativeStableBeatmapObjectDiscovery.BeatmapSetIdOffset, 456);
        memory.WriteManagedString(0x6000, new string('a', 32));
        memory.WriteManagedString(0x6100, "123 Artist - Title");
        memory.WriteManagedString(0x6200, "Artist - Title (Mapper) [Difficulty].osu");
        return new(memory, regions, []);
    }

    private sealed record TestSpace(
        SyntheticMemory Memory,
        IReadOnlyList<NativeStableMemoryRegion> Regions,
        IReadOnlyList<NativeStableModuleRange> Modules);

    private sealed class SyntheticMemory : INativeStableMemoryReader
    {
        private readonly Dictionary<nuint, byte> bytes = [];
        internal HashSet<nuint> Unreadable { get; } = [];
        public bool Is32Bit => true;

        internal void WriteUInt32(nuint address, uint value) => Write(address, BitConverter.GetBytes(value));
        internal void WriteInt32(nuint address, int value) => Write(address, BitConverter.GetBytes(value));

        internal void WriteManagedString(nuint address, string value)
        {
            WriteUInt32(address, 0xdeadbeef);
            WriteInt32(address + 4, value.Length);
            Write(address + 8, System.Text.Encoding.Unicode.GetBytes(value));
        }

        private void Write(nuint address, byte[] data)
        {
            for (int index = 0; index < data.Length; index++)
            {
                bytes[address + checked((nuint)index)] = data[index];
            }
        }

        public NativeStableMemoryReadResult ReadBytes(nuint address, int count)
        {
            if (Unreadable.Any(blocked => address <= blocked &&
                    blocked < address + checked((nuint)count)))
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
            NativeStableMemoryReadResult read = ReadBytes(address, sizeof(uint));
            value = read.Complete ? BitConverter.ToUInt32(read.Data, 0) : 0;
            return read.Complete && value != 0;
        }
    }
}
