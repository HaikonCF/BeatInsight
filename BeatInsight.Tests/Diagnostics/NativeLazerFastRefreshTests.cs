using System.Buffers.Binary;
using System.IO;
using System.Text;
using BeatInsight.Diagnostics.NativeLazer;

namespace BeatInsight.Tests.Diagnostics;

public sealed class NativeLazerFastRefreshTests
{
    private static readonly DateTime start = new(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Cache_SamePidAndStartIdentity_IsHit()
    {
        Fixture fixture = Fixture.Create();
        NativeLazerRootCacheLookup lookup = fixture.Cache.Lookup(fixture.Generation, processExists: true);
        Assert.True(lookup.CacheHit);
        Assert.Equal(NativeLazerCacheMissReason.None, lookup.CacheMissReason);
    }

    [Fact]
    public void Cache_DifferentPid_IsRejected()
    {
        Fixture fixture = Fixture.Create();
        NativeLazerRootCacheLookup lookup = fixture.Cache.Lookup(new(43, start), processExists: true);
        Assert.False(lookup.CacheHit);
        Assert.Equal(NativeLazerCacheMissReason.PidChanged, lookup.CacheMissReason);
        Assert.Null(fixture.Cache.Current);
    }

    [Fact]
    public void Cache_SamePidDifferentStartIdentity_IsRejected()
    {
        Fixture fixture = Fixture.Create();
        NativeLazerRootCacheLookup lookup = fixture.Cache.Lookup(new(42, start.AddSeconds(1)), processExists: true);
        Assert.False(lookup.CacheHit);
        Assert.Equal(NativeLazerCacheMissReason.ProcessStartIdentityChanged, lookup.CacheMissReason);
    }

    [Fact]
    public void Cache_ProcessExited_IsRejected()
    {
        Fixture fixture = Fixture.Create();
        NativeLazerRootCacheLookup lookup = fixture.Cache.Lookup(fixture.Generation, processExists: false);
        Assert.False(lookup.CacheHit);
        Assert.Equal(NativeLazerCacheMissReason.ProcessExited, lookup.CacheMissReason);
        Assert.Null(fixture.Cache.Current);
    }

    [Fact]
    public void RootValidation_ValidCachedRoot_ReturnsAvailable()
    {
        Fixture fixture = Fixture.Create();
        NativeLazerFastRefreshResult result = fixture.Refresh();
        Assert.Equal(NativeLazerFastOutcome.Available, result.Outcome);
        Assert.True(result.RootValid);
        Assert.Equal(2431320, result.BeatmapOnlineId);
        Assert.Equal(123456, result.BeatmapSetOnlineId);
        Assert.Equal(Fixture.Hash, result.BeatmapHash);
        Assert.True(result.BlobExists);
    }

    [Fact]
    public void RootValidation_UnreadableRoot_InvalidatesCache()
    {
        Fixture fixture = Fixture.Create();
        fixture.Memory.RemoveRange(Fixture.Root, 8);
        NativeLazerFastRefreshResult result = fixture.Refresh();
        AssertUnknownWithoutCurrentMetadata(result);
        Assert.False(result.RootValid);
        Assert.Equal(NativeLazerCacheMissReason.RootValidationFailed, result.CacheMissReason);
        Assert.Null(fixture.Cache.Current);
    }

    [Fact]
    public void RootValidation_BrokenPrimaryRelationship_InvalidatesCache()
    {
        Fixture fixture = Fixture.Create();
        fixture.Memory.SetPointer(Fixture.Root + fixture.Layout.Storage, 0);
        fixture.Memory.SetPointer(Fixture.Root + fixture.Layout.Api, 0);
        fixture.Memory.SetPointer(Fixture.Root + fixture.Layout.Ruleset, 0);
        fixture.Memory.SetPointer(Fixture.Root + fixture.Layout.Realm, 0);
        NativeLazerFastRefreshResult result = fixture.Refresh();
        AssertUnknownWithoutCurrentMetadata(result);
        Assert.Null(fixture.Cache.Current);
    }

    [Fact]
    public void RootValidation_InvalidBeatmapInfoRelationship_IsUnknown()
    {
        Fixture fixture = Fixture.Create();
        fixture.Memory.SetPointer(Fixture.Info + fixture.Layout.BeatmapInfoSet, Fixture.OtherSet);
        NativeLazerFastRefreshResult result = fixture.Refresh();
        AssertUnknownWithoutCurrentMetadata(result);
        Assert.Contains("relationship", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RootValidation_ImplausibleIdDomain_IsUnknown()
    {
        Fixture fixture = Fixture.Create();
        fixture.Memory.SetInt32(Fixture.Info + fixture.Layout.BeatmapInfoOnlineId, -2);
        NativeLazerFastRefreshResult result = fixture.Refresh();
        AssertUnknownWithoutCurrentMetadata(result);
        Assert.Contains("ID/hash", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void RootValidation_InvalidHashDomain_IsUnknown()
    {
        Fixture fixture = Fixture.Create();
        fixture.Memory.SetString(Fixture.HashAddress, "not-a-sha256", Fixture.StringMethodTable);
        NativeLazerFastRefreshResult result = fixture.Refresh();
        AssertUnknownWithoutCurrentMetadata(result);
    }

    [Fact]
    public void FastRefresh_CoherentNoBeatmap_ReturnsNoBeatmap()
    {
        Fixture fixture = Fixture.Create();
        fixture.Memory.SetPointer(Fixture.Bindable + fixture.Layout.BindableValue, 0);
        NativeLazerFastRefreshResult result = fixture.Refresh();
        Assert.Equal(NativeLazerFastOutcome.NoBeatmap, result.Outcome);
        Assert.True(result.RootValid);
        Assert.Null(result.BeatmapOnlineId);
        Assert.Null(result.BeatmapHash);
    }

    [Fact]
    public void HeavyDiscoveryValidation_NullWorkingBeatmapCandidateIsRejected()
    {
        Fixture fixture = Fixture.Create();
        fixture.Memory.SetPointer(Fixture.Bindable + fixture.Layout.BindableValue, 0);

        NativeLazerDiagnosticTrace trace = NativeLazerKnownRootTracer.Capture(
            "HeavyCleanRoomAnchorCandidate",
            fixture.Entry,
            fixture.Memory);

        Assert.False(trace.ValidationResult);
        NativeLazerDiagnosticStage working = Assert.Single(trace.Stages, stage => stage.Number == 6);
        Assert.True(working.IsNull);
        Assert.False(working.ValidationResult);
        Assert.Equal("WorkingBeatmap is null.", working.FailureReason);
    }

    [Fact]
    public void FastRefresh_PartialRead_ReturnsUnknown()
    {
        Fixture fixture = Fixture.Create();
        fixture.Memory.RemoveRange(Fixture.Info + fixture.Layout.BeatmapInfoOnlineId, 4);
        NativeLazerFastRefreshResult result = fixture.Refresh();
        AssertUnknownWithoutCurrentMetadata(result);
    }

    [Fact]
    public void FastRefresh_InconsistentBeginEndIdentity_ReturnsUnknown()
    {
        Fixture fixture = Fixture.Create();
        ulong valueAddress = Fixture.Bindable + fixture.Layout.BindableValue;
        fixture.Memory.BeforeRead = (address, count) =>
        {
            if (address == valueAddress && count == 2)
                fixture.Memory.SetPointer(valueAddress, Fixture.OtherWorking);
        };
        NativeLazerFastRefreshResult result = fixture.Refresh();
        AssertUnknownWithoutCurrentMetadata(result);
        Assert.Contains("Snapshot identity", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void FastRefresh_DoesNotFallBackToPreviousAvailable()
    {
        Fixture fixture = Fixture.Create();
        NativeLazerFastRefreshResult first = fixture.Refresh();
        Assert.Equal(NativeLazerFastOutcome.Available, first.Outcome);

        fixture.Memory.SetPointer(Fixture.Info + fixture.Layout.BeatmapInfoSet, Fixture.OtherSet);
        NativeLazerFastRefreshResult transition = fixture.Refresh();
        AssertUnknownWithoutCurrentMetadata(transition);
        Assert.False(transition.StaleAvailableDetected);
    }

    [Fact]
    public void FastRefresh_ProcessGenerationRace_InvalidatesOldCache()
    {
        Fixture fixture = Fixture.Create();
        int checks = 0;
        NativeLazerFastRefreshResult result = new NativeLazerFastRefresher().Refresh(
            fixture.Generation,
            fixture.Entry,
            fixture.Cache,
            fixture.Memory,
            _ => checks++ == 0,
            _ => true);
        AssertUnknownWithoutCurrentMetadata(result);
        Assert.False(result.RootValid);
        Assert.Equal(NativeLazerCacheMissReason.ProcessExited, result.CacheMissReason);
        Assert.Null(fixture.Cache.Current);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("nobeatmap")]
    [InlineData("direct")]
    public void Transitions_DoNotLeakPreviousMap(string sequence)
    {
        NativeLazerFastRefreshResult[] samples = sequence switch
        {
            "unknown" => [Result(1), Result(null, NativeLazerFastOutcome.Unknown), Result(2)],
            "nobeatmap" => [Result(1), Result(null, NativeLazerFastOutcome.NoBeatmap), Result(2)],
            _ => [Result(1), Result(2)]
        };
        var timed = samples.Select((result, index) => ((double)index, result)).ToArray();
        NativeLazerTransitionMetrics metrics = NativeLazerTransitionAnalyzer.Analyze(1, 2, timed);
        Assert.False(metrics.StaleAvailableDetected);
        Assert.NotNull(metrics.FirstNewMapAvailableMs);
    }

    [Fact]
    public void Transitions_RepeatedABADoesNotLeakAcrossEitherTransition()
    {
        NativeLazerTransitionMetrics aToB = NativeLazerTransitionAnalyzer.Analyze(
            1, 2, new[] { (0d, Result(1)), (1d, Result(null, NativeLazerFastOutcome.Unknown)), (2d, Result(2)) });
        NativeLazerTransitionMetrics bToA = NativeLazerTransitionAnalyzer.Analyze(
            2, 1, new[] { (0d, Result(2)), (1d, Result(null, NativeLazerFastOutcome.NoBeatmap)), (2d, Result(1)) });
        Assert.False(aToB.StaleAvailableDetected);
        Assert.False(bToA.StaleAvailableDetected);
    }

    [Fact]
    public void Restart_OldGenerationCannotPopulateNewGenerationCache()
    {
        Fixture fixture = Fixture.Create();
        NativeLazerGenerationIdentity newGeneration = new(99, start.AddMinutes(1));
        Assert.False(fixture.Cache.TryStore(fixture.Entry, newGeneration, processExists: true));
        Assert.Null(fixture.Cache.Current);
    }

    [Fact]
    public void Restart_NewDiscoveryRunsOnceThenNewGenerationHitsCache()
    {
        Fixture fixture = Fixture.Create(storeEntry: false);
        var coordinator = new NativeLazerFastRefreshCoordinator(fixture.Cache);
        int discoveryCount = 0;
        NativeLazerHeavyDiscoveryResult Discover(NativeLazerGenerationIdentity _, INativeLazerFastMemory __)
        {
            discoveryCount++;
            return new(fixture.Entry, 10, 1_000);
        }

        NativeLazerFastRefreshResult first = coordinator.Refresh(
            fixture.Generation, true, fixture.Memory, Discover, identity => identity == fixture.Generation, _ => true);
        NativeLazerFastRefreshResult second = coordinator.Refresh(
            fixture.Generation, true, fixture.Memory, Discover, identity => identity == fixture.Generation, _ => true);

        Assert.Equal(1, discoveryCount);
        Assert.True(first.HeavyDiscoveryTriggered);
        Assert.False(first.CacheHit);
        Assert.False(second.HeavyDiscoveryTriggered);
        Assert.True(second.CacheHit);
    }

    [Fact]
    public void PerformanceAccounting_FastBytesExcludeHeavyDiscoveryBytes()
    {
        Fixture fixture = Fixture.Create(storeEntry: false);
        var coordinator = new NativeLazerFastRefreshCoordinator(fixture.Cache);
        NativeLazerFastRefreshResult result = coordinator.Refresh(
            fixture.Generation,
            true,
            fixture.Memory,
            (_, _) => new(fixture.Entry, 123, 3_700_000_000),
            identity => identity == fixture.Generation,
            _ => true);
        Assert.Equal(3_700_000_000, result.HeavyDiscoveryBytesRead);
        Assert.InRange(result.Metrics.BytesRead, 1, 10_000);
        Assert.True(result.Metrics.ReadOperationCount > 0);
    }

    [Fact]
    public void PerformanceAccounting_AggregatesMinMedianP95MaxAndAverageBytes()
    {
        NativeLazerFastRefreshResult[] results =
        [
            Result(1, totalMs: 1, bytes: 10),
            Result(1, totalMs: 2, bytes: 20),
            Result(null, NativeLazerFastOutcome.Unknown, totalMs: 4, bytes: 30),
            Result(null, NativeLazerFastOutcome.NoBeatmap, totalMs: 8, bytes: 40)
        ];
        NativeLazerFastRefreshStatistics statistics = NativeLazerFastRefreshStatistics.From(results);
        Assert.Equal(1, statistics.MinRefreshMs);
        Assert.Equal(3, statistics.MedianRefreshMs);
        Assert.Equal(8, statistics.MaxRefreshMs);
        Assert.Equal(25, statistics.AverageBytesRead);
        Assert.InRange(statistics.P95RefreshMs, 7, 8);
    }

    [Fact]
    public void PersistentHint_Absent_IsRejected()
    {
        Fixture fixture = Fixture.Create();

        bool resolved = NativeLazerPersistentHintResolver.TryResolve(
            null,
            HintContext(fixture.Layout),
            fixture.Generation,
            fixture.Layout,
            fixture.Memory,
            out NativeLazerRootCacheEntry? entry,
            out _,
            out NativeLazerPersistentHintMissReason reason);

        Assert.False(resolved);
        Assert.Null(entry);
        Assert.Equal(NativeLazerPersistentHintMissReason.Absent, reason);
    }

    [Fact]
    public void PersistentHint_Valid_IsFullyValidatedForCurrentGeneration()
    {
        Fixture fixture = Fixture.Create();
        NativeLazerPersistentHintContext context = HintContext(fixture.Layout);
        NativeLazerPersistentRootHint hint =
            NativeLazerPersistentRootHint.Create(context, Fixture.Root);

        bool resolved = NativeLazerPersistentHintResolver.TryResolve(
            hint,
            context,
            fixture.Generation,
            fixture.Layout,
            fixture.Memory,
            out NativeLazerRootCacheEntry? entry,
            out NativeLazerDiagnosticTrace? trace,
            out NativeLazerPersistentHintMissReason reason,
            _ => ("synthetic-blob", true));

        Assert.True(resolved);
        Assert.Equal(NativeLazerPersistentHintMissReason.None, reason);
        Assert.Equal(fixture.Generation, entry!.GenerationIdentity);
        Assert.True(trace!.ValidationResult);
    }

    [Fact]
    public void PersistentHint_InvalidRoot_IsRejectedWithoutMetadata()
    {
        Fixture fixture = Fixture.Create();
        NativeLazerPersistentHintContext context = HintContext(fixture.Layout);
        NativeLazerPersistentRootHint hint =
            NativeLazerPersistentRootHint.Create(context, 0xDEADBEEF);

        bool resolved = NativeLazerPersistentHintResolver.TryResolve(
            hint,
            context,
            fixture.Generation,
            fixture.Layout,
            fixture.Memory,
            out NativeLazerRootCacheEntry? entry,
            out _,
            out NativeLazerPersistentHintMissReason reason);

        Assert.False(resolved);
        Assert.Null(entry);
        Assert.Equal(NativeLazerPersistentHintMissReason.RootValidationFailed, reason);
    }

    [Fact]
    public void PersistentHint_VersionMismatch_IsRejectedBeforeRootRead()
    {
        Fixture fixture = Fixture.Create();
        NativeLazerPersistentHintContext current = HintContext(fixture.Layout);
        NativeLazerPersistentRootHint hint = NativeLazerPersistentRootHint.Create(
            current with { ProductVersion = "older-build" },
            Fixture.Root);
        fixture.Memory.ResetAccounting();

        bool resolved = NativeLazerPersistentHintResolver.TryResolve(
            hint,
            current,
            fixture.Generation,
            fixture.Layout,
            fixture.Memory,
            out _,
            out _,
            out NativeLazerPersistentHintMissReason reason);

        Assert.False(resolved);
        Assert.Equal(NativeLazerPersistentHintMissReason.VersionMismatch, reason);
        Assert.Equal(0, fixture.Memory.ReadOperationCount);
    }

    [Fact]
    public void PersistentHint_StructureFingerprintMismatch_IsRejectedBeforeRootRead()
    {
        Fixture fixture = Fixture.Create();
        NativeLazerPersistentHintContext current = HintContext(fixture.Layout);
        NativeLazerPersistentRootHint hint = NativeLazerPersistentRootHint.Create(
            current with { StructureFingerprint = "different" },
            Fixture.Root);
        fixture.Memory.ResetAccounting();

        bool resolved = NativeLazerPersistentHintResolver.TryResolve(
            hint,
            current,
            fixture.Generation,
            fixture.Layout,
            fixture.Memory,
            out _,
            out _,
            out NativeLazerPersistentHintMissReason reason);

        Assert.False(resolved);
        Assert.Equal(
            NativeLazerPersistentHintMissReason.StructureFingerprintMismatch,
            reason);
        Assert.Equal(0, fixture.Memory.ReadOperationCount);
    }

    [Fact]
    public void PersistentHint_InvalidFastPath_FallsBackToHeavyDiscovery()
    {
        Fixture fixture = Fixture.Create(storeEntry: false);
        NativeLazerPersistentHintContext context = HintContext(fixture.Layout);
        NativeLazerPersistentRootHint invalidHint =
            NativeLazerPersistentRootHint.Create(context, 0xDEADBEEF);
        bool hintResolved = NativeLazerPersistentHintResolver.TryResolve(
            invalidHint,
            context,
            fixture.Generation,
            fixture.Layout,
            fixture.Memory,
            out _,
            out _,
            out _);
        int heavyDiscoveryCount = 0;
        NativeLazerFastRefreshResult result = new NativeLazerFastRefreshCoordinator(
            fixture.Cache).Refresh(
                fixture.Generation,
                true,
                fixture.Memory,
                (_, _) =>
                {
                    heavyDiscoveryCount++;
                    return new(fixture.Entry, 10, 1_000);
                },
                identity => identity == fixture.Generation,
                _ => true);

        Assert.False(hintResolved);
        Assert.Equal(1, heavyDiscoveryCount);
        Assert.True(result.HeavyDiscoveryTriggered);
        Assert.Equal(NativeLazerFastOutcome.Available, result.Outcome);
    }

    [Fact]
    public void PersistentHintStore_MissingThenRoundTripsValidatedData()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"beatinsight-lazer-hint-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "hint.json");
        var store = new NativeLazerPersistentRootHintStore(path);
        NativeLazerPersistentHintContext context = HintContext(Fixture.Create().Layout);
        NativeLazerPersistentRootHint expected =
            NativeLazerPersistentRootHint.Create(context, Fixture.Root);
        try
        {
            Assert.Null(store.Load(out NativeLazerPersistentHintMissReason absent));
            Assert.Equal(NativeLazerPersistentHintMissReason.Absent, absent);
            Assert.True(store.TrySave(expected));
            Assert.Equal(expected, store.Load(out NativeLazerPersistentHintMissReason loaded));
            Assert.Equal(NativeLazerPersistentHintMissReason.None, loaded);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static NativeLazerPersistentHintContext HintContext(
        NativeLazerFastLayout layout) => new(
            "current-build",
            "Amd64",
            "module-identity",
            NativeLazerPersistentHintIdentity.HashLayout(layout));

    private static void AssertUnknownWithoutCurrentMetadata(NativeLazerFastRefreshResult result)
    {
        Assert.Equal(NativeLazerFastOutcome.Unknown, result.Outcome);
        Assert.Null(result.BeatmapOnlineId);
        Assert.Null(result.BeatmapSetOnlineId);
        Assert.Null(result.BeatmapHash);
        Assert.Null(result.BlobExists);
    }

    private static NativeLazerFastRefreshResult Result(
        int? beatmapId,
        NativeLazerFastOutcome outcome = NativeLazerFastOutcome.Available,
        double totalMs = 1,
        long bytes = 1) => new()
    {
        ProcessIdentity = new(1, start),
        RootValid = outcome != NativeLazerFastOutcome.Unknown,
        Outcome = outcome,
        BeatmapOnlineId = outcome == NativeLazerFastOutcome.Available ? beatmapId : null,
        Metrics = new(0, 0, 0, 0, totalMs, bytes, 1)
    };

    private sealed class Fixture
    {
        internal const ulong Root = 0x10000;
        internal const ulong Bindable = 0x20000;
        internal const ulong Working = 0x30000;
        internal const ulong OtherWorking = 0x31000;
        internal const ulong Info = 0x40000;
        internal const ulong Set = 0x50000;
        internal const ulong OtherSet = 0x51000;
        internal const ulong VersionAddress = 0x60000;
        internal const ulong HashAddress = 0x61000;
        internal const ulong StringMethodTable = 0x90000;
        internal const string Hash = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

        private Fixture(
            NativeLazerFastLayout layout,
            FakeMemory memory,
            NativeLazerGenerationIdentity generation,
            NativeLazerRootCacheEntry entry,
            NativeLazerDiagnosticRootCache cache)
        {
            Layout = layout;
            Memory = memory;
            Generation = generation;
            Entry = entry;
            Cache = cache;
        }

        internal NativeLazerFastLayout Layout { get; }
        internal FakeMemory Memory { get; }
        internal NativeLazerGenerationIdentity Generation { get; }
        internal NativeLazerRootCacheEntry Entry { get; }
        internal NativeLazerDiagnosticRootCache Cache { get; }

        internal static Fixture Create(bool storeEntry = true)
        {
            var layout = new NativeLazerFastLayout(
                VersionHash: 0x10,
                BeatmapManager: 0x18,
                Api: 0x20,
                Storage: 0x28,
                Beatmap: 0x30,
                Ruleset: 0x38,
                SelectedMods: 0x40,
                Realm: 0x48,
                Dependencies: 0x50,
                BindableValue: 0x10,
                BindableDisabled: 0x18,
                WorkingBeatmapInfo: 0x10,
                WorkingBeatmapSetInfo: 0x18,
                BeatmapInfoId: 0x10,
                BeatmapInfoSet: 0x20,
                BeatmapInfoOnlineId: 0x28,
                BeatmapInfoHash: 0x30,
                BeatmapSetOnlineId: 0x10);
            var memory = new FakeMemory();
            memory.SetObject(Root, 0x91000);
            memory.SetObject(Bindable, 0x92000);
            memory.SetObject(Working, 0x93000);
            memory.SetObject(OtherWorking, 0x93000);
            memory.SetObject(Info, 0x94000);
            memory.SetObject(Set, 0x95000);
            memory.SetObject(OtherSet, 0x95000);
            memory.SetString(VersionAddress, "2026.804.2-lazer", StringMethodTable);
            memory.SetString(HashAddress, Hash, StringMethodTable);
            memory.SetPointer(Root + layout.VersionHash, VersionAddress);
            ulong dummy = 0x70000;
            foreach (ulong offset in layout.RootReferenceOffsets)
            {
                ulong address = offset == layout.Beatmap ? Bindable : dummy;
                memory.SetObject(address, 0x96000);
                memory.SetPointer(Root + offset, address);
                dummy += 0x1000;
            }
            memory.SetPointer(Root + layout.Beatmap, Bindable);
            memory.SetByte(Bindable + layout.BindableDisabled, 0);
            memory.SetPointer(Bindable + layout.BindableValue, Working);
            memory.SetPointer(Working + layout.WorkingBeatmapInfo, Info);
            memory.SetPointer(Working + layout.WorkingBeatmapSetInfo, Set);
            memory.SetPointer(Info + layout.BeatmapInfoSet, Set);
            memory.SetGuid(Info + layout.BeatmapInfoId, new("11111111-2222-3333-4444-555555555555"));
            memory.SetInt32(Info + layout.BeatmapInfoOnlineId, 2431320);
            memory.SetPointer(Info + layout.BeatmapInfoHash, HashAddress);
            memory.SetInt32(Set + layout.BeatmapSetOnlineId, 123456);

            Assert.True(NativeLazerRootFingerprintFactory.TryCreate(Root, layout, memory, out NativeLazerRootValidationFingerprint? fingerprint));
            var generation = new NativeLazerGenerationIdentity(42, start);
            var entry = new NativeLazerRootCacheEntry(generation, Root, fingerprint!, layout);
            var cache = new NativeLazerDiagnosticRootCache();
            if (storeEntry)
                Assert.True(cache.TryStore(entry, generation, processExists: true));
            memory.ResetAccounting();
            return new(layout, memory, generation, entry, cache);
        }

        internal NativeLazerFastRefreshResult Refresh() => new NativeLazerFastRefresher().Refresh(
            Generation, Entry, Cache, Memory, identity => identity == Generation, _ => true);
    }

    private sealed class FakeMemory : INativeLazerFastMemory
    {
        private readonly Dictionary<ulong, byte> bytes = [];
        private readonly Dictionary<ulong, int> addressReads = [];

        internal Action<ulong, int>? BeforeRead { get; set; }
        public long BytesRead { get; private set; }
        public int ReadOperationCount { get; private set; }

        public bool IsReadable(ulong address, int count) => Enumerable.Range(0, count)
            .All(offset => bytes.ContainsKey(address + checked((ulong)offset)));

        public byte? ReadByte(ulong address) => Read(address, 1) is { } data ? data[0] : null;
        public int? ReadInt32(ulong address) => Read(address, 4) is { } data ? BinaryPrimitives.ReadInt32LittleEndian(data) : null;
        public ulong? ReadPointer(ulong address) => Read(address, 8) is { } data ? BinaryPrimitives.ReadUInt64LittleEndian(data) : null;
        public Guid? ReadGuid(ulong address) => Read(address, 16) is { } data ? new Guid(data) : null;

        public NativeLazerRemoteText? ReadString(ulong address, int maximumCharacters)
        {
            ulong? methodTable = ReadPointer(address);
            int? length = ReadInt32(address + 8);
            if (!methodTable.HasValue || length is null || length < 0 || length > maximumCharacters)
                return null;
            byte[]? data = Read(address + 12, checked(length.Value * 2));
            return data is null ? null : new(Encoding.Unicode.GetString(data), methodTable.Value);
        }

        internal void SetObject(ulong address, ulong methodTable)
        {
            SetPointer(address, methodTable);
            SetPointer(methodTable, 0xA0000);
        }

        internal void SetByte(ulong address, byte value) => bytes[address] = value;
        internal void SetInt32(ulong address, int value) => Set(address, BitConverter.GetBytes(value));
        internal void SetPointer(ulong address, ulong value) => Set(address, BitConverter.GetBytes(value));
        internal void SetGuid(ulong address, Guid value) => Set(address, value.ToByteArray());

        internal void SetString(ulong address, string value, ulong methodTable)
        {
            SetPointer(address, methodTable);
            SetPointer(methodTable, 0xA0000);
            SetInt32(address + 8, value.Length);
            Set(address + 12, Encoding.Unicode.GetBytes(value));
        }

        internal void RemoveRange(ulong address, int count)
        {
            for (int index = 0; index < count; index++)
                bytes.Remove(address + checked((ulong)index));
        }

        internal void ResetAccounting()
        {
            BytesRead = 0;
            ReadOperationCount = 0;
            addressReads.Clear();
        }

        private byte[]? Read(ulong address, int count)
        {
            int occurrence = addressReads.GetValueOrDefault(address) + 1;
            addressReads[address] = occurrence;
            BeforeRead?.Invoke(address, occurrence);
            ReadOperationCount++;
            if (!IsReadable(address, count))
                return null;
            var result = new byte[count];
            for (int index = 0; index < count; index++)
                result[index] = bytes[address + checked((ulong)index)];
            BytesRead += count;
            return result;
        }

        private void Set(ulong address, IReadOnlyList<byte> value)
        {
            for (int index = 0; index < value.Count; index++)
                bytes[address + checked((ulong)index)] = value[index];
        }
    }
}
