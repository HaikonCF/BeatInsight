using BeatInsight.Diagnostics.NativeStable;
using BeatInsight.Integration.Game;
using System.IO;

namespace BeatInsight.Tests.Integration.Game;

public sealed class NativeStableGameProviderTests
{
    [Fact]
    public void ResolvedBeatmapPublishesFileBackedBeatmapAndSetIds()
    {
        using var scope = new TempBeatmap(
            includeBackground: false,
            beatmapId: 3724444,
            beatmapSetId: 128931);
        var provider = new NativeStableGameProvider(
            new FakeRuntime(
                Context(41, DateTimeOffset.Parse("2026-09-28T10:00:00Z")),
                scope.BeatmapPath),
            new FakeLocator(Unique()),
            new FakeLiveStateReader(),
            Profile());

        GameStateSnapshot result = provider.Refresh();

        Assert.True(result.IsAvailable);
        Assert.Equal("3724444", result.CurrentBeatmapId);
        Assert.Equal("128931", result.CurrentBeatmapSetId);
        Assert.Equal(scope.BeatmapPath, result.CurrentBeatmapPath);
    }

    [Fact]
    public void SameMapAndGenerationReadIdentityHeaderOnlyOnce()
    {
        DateTimeOffset start = DateTimeOffset.Parse("2026-09-28T10:00:00Z");
        var identityReader = new FakeBeatmapIdentityReader(
            new NativeStableBeatmapIdentity(3724444, 128931));
        var provider = new NativeStableGameProvider(
            new FakeRuntime(
                Context(41, start), @"D:\osu!\Songs\set\map.osu",
                Context(41, start)),
            new FakeLocator(Unique(), Unique()),
            new FakeLiveStateReader(),
            identityReader,
            Profile());

        GameStateSnapshot first = provider.Refresh();
        GameStateSnapshot second = provider.Refresh();

        Assert.Equal("128931", first.CurrentBeatmapSetId);
        Assert.Equal("128931", second.CurrentBeatmapSetId);
        Assert.Equal(1, identityReader.ReadCount);
    }

    [Fact]
    public void MapChangePublishesNewSetIdWithoutKeepingPreviousValue()
    {
        DateTimeOffset start = DateTimeOffset.Parse("2026-09-28T10:00:00Z");
        var identityReader = new FakeBeatmapIdentityReader(
            new NativeStableBeatmapIdentity(3724444, 128931),
            new NativeStableBeatmapIdentity(99, 456789));
        var provider = new NativeStableGameProvider(
            new ChangingMapRuntime(
                [Context(41, start), Context(41, start)],
                [@"D:\osu!\Songs\set-a\a.osu", @"D:\osu!\Songs\set-b\b.osu"]),
            new FakeLocator(Unique(), Unique() with { BeatmapId = 99 }),
            new FakeLiveStateReader(),
            identityReader,
            Profile());

        GameStateSnapshot first = provider.Refresh();
        GameStateSnapshot second = provider.Refresh();

        Assert.Equal("128931", first.CurrentBeatmapSetId);
        Assert.Equal("456789", second.CurrentBeatmapSetId);
        Assert.Equal(2, identityReader.ReadCount);
    }

    [Fact]
    public void ProcessRestartRereadsIdentityAndDoesNotKeepPreviousSetId()
    {
        DateTimeOffset start = DateTimeOffset.Parse("2026-09-28T10:00:00Z");
        var identityReader = new FakeBeatmapIdentityReader(
            new NativeStableBeatmapIdentity(3724444, 128931),
            new NativeStableBeatmapIdentity(3724444, null));
        var provider = new NativeStableGameProvider(
            new FakeRuntime(
                Context(41, start), @"D:\osu!\Songs\set\map.osu",
                Context(52, start.AddMinutes(5))),
            new FakeLocator(Unique(), Unique()),
            new FakeLiveStateReader(),
            identityReader,
            Profile());

        GameStateSnapshot first = provider.Refresh();
        GameStateSnapshot second = provider.Refresh();

        Assert.Equal("128931", first.CurrentBeatmapSetId);
        Assert.Null(second.CurrentBeatmapSetId);
        Assert.Equal(2, identityReader.ReadCount);
    }

    [Fact]
    public void UniqueLocatorResult_ExposesImmutableAvailableState()
    {
        DateTimeOffset start = DateTimeOffset.Parse("2026-09-28T10:00:00Z");
        var runtime = new FakeRuntime(Context(41, start), @"D:\osu!\Songs\set\map.osu");
        var locator = new FakeLocator(Unique());
        var provider = new NativeStableGameProvider(
            runtime, locator, new FakeLiveStateReader(), Profile());

        provider.Refresh();

        Assert.True(provider.IsAvailable);
        Assert.Equal("41:2026-09-28T10:00:00.0000000Z", provider.ProcessIdentity);
        Assert.Equal(@"D:\osu!\Songs\set\map.osu", provider.CurrentBeatmapPath);
        Assert.Equal("3724444", provider.CurrentBeatmapId);
        Assert.NotNull(provider.LastUpdate);
    }

    [Fact]
    public void ProcessRestart_UsesNewIdentityAndDoesNotReuseProviderState()
    {
        DateTimeOffset first = DateTimeOffset.Parse("2026-09-28T10:00:00Z");
        DateTimeOffset second = first.AddMinutes(5);
        var runtime = new FakeRuntime(
            Context(41, first), @"D:\osu!\Songs\set\map.osu",
            Context(52, second));
        var locator = new FakeLocator(Unique(), Unique() with { BeatmapId = 99 });
        var provider = new NativeStableGameProvider(
            runtime, locator, new FakeLiveStateReader(), Profile());

        provider.Refresh();
        provider.Refresh();

        Assert.Equal([first, second], locator.ProcessStarts);
        Assert.Equal("52:2026-09-28T10:05:00.0000000Z", provider.ProcessIdentity);
        Assert.Equal("99", provider.CurrentBeatmapId);
    }

    [Fact]
    public void AmbiguousCandidate_IsUnavailable()
    {
        var provider = Provider(new(
            NativeStableAutonomousQDiscovery.Ambiguous, false, null, null, null));

        provider.Refresh();

        Assert.False(provider.IsAvailable);
        Assert.Null(provider.CurrentBeatmapPath);
        Assert.Null(provider.CurrentBeatmapId);
    }

    [Fact]
    public void InvalidBeatmapObject_IsUnavailable()
    {
        var provider = Provider(Unique() with { ObjectValid = false });

        provider.Refresh();

        Assert.False(provider.IsAvailable);
    }

    [Fact]
    public void ProviderSource_DoesNotUseExpectedMapPath()
    {
        string source = ProviderSource();

        Assert.DoesNotContain("ExpectedMapPath", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expectedMapPath", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProviderSource_ContainsNoMemoryWriteOrInjectionApi()
    {
        string source = ProviderSource();

        Assert.DoesNotContain("WriteProcessMemory", source, StringComparison.Ordinal);
        Assert.DoesNotContain("VirtualAllocEx", source, StringComparison.Ordinal);
        Assert.DoesNotContain("VirtualProtectEx", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateRemoteThread", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingOsuProcess_StartsUnavailableWithoutThrowing()
    {
        var provider = new NativeStableGameProvider(
            new FakeRuntime(null, null), new FakeLocator(Unique()),
            new FakeLiveStateReader(), Profile());

        Exception? exception = Record.Exception(provider.Refresh);

        Assert.Null(exception);
        Assert.False(provider.IsAvailable);
        Assert.Null(provider.ProcessIdentity);
    }

    [Fact]
    public void ClosedProcess_ClearsPreviouslyAvailableState()
    {
        DateTimeOffset start = DateTimeOffset.Parse("2026-09-28T10:00:00Z");
        var runtime = new FakeRuntime(
            Context(41, start), @"D:\osu!\Songs\set\map.osu",
            new NativeStableGameProcessContext?[] { null });
        var live = new FakeLiveStateReader();
        var provider = new NativeStableGameProvider(
            runtime, new FakeLocator(Unique()), live, Profile());

        provider.Refresh();
        Assert.True(provider.IsAvailable);

        provider.Refresh();
        Assert.False(provider.IsAvailable);
        Assert.Null(provider.ProcessIdentity);
        Assert.Null(provider.CurrentBeatmapPath);
        Assert.Equal(1, live.InvalidateCount);
    }

    [Fact]
    public void ClosedProcessDoesNotRetryGameplayStatsDiscovery()
    {
        DateTimeOffset start = DateTimeOffset.Parse("2026-09-28T10:00:00Z");
        using var scope = new TempBeatmap(
            includeBackground: false,
            beatmapId: 3724444,
            beatmapSetId: 128931);
        var stats = new CountingGameplayStatsReader();
        var provider = new NativeStableGameProvider(
            new FakeRuntime(
                Context(41, start), scope.BeatmapPath,
                new NativeStableGameProcessContext?[] { null }),
            new FakeLocator(Unique()),
            new FakeLiveStateReader(status: 2),
            new FakeBeatmapIdentityReader(
                new NativeStableBeatmapIdentity(3724444, 128931)),
            stats,
            Profile());

        provider.Refresh();
        provider.Refresh();

        Assert.Equal(1, stats.ReadCount);
        Assert.Equal(1, stats.InvalidateCount);
    }

    [Fact]
    public void NoCandidate_IsUnavailable()
    {
        var provider = Provider(new(
            NativeStableAutonomousQDiscovery.None, false, null, null, null));

        provider.Refresh();

        Assert.False(provider.IsAvailable);
    }

    [Fact]
    public void SameProcessRefresh_AlwaysCallsLocatorForRevalidation()
    {
        DateTimeOffset start = DateTimeOffset.Parse("2026-09-28T10:00:00Z");
        var runtime = new FakeRuntime(
            Context(41, start), @"D:\osu!\Songs\set\map.osu", Context(41, start));
        var locator = new FakeLocator(
            Unique(),
            new(NativeStableAutonomousQDiscovery.None, false, null, null, null));
        var provider = new NativeStableGameProvider(
            runtime, locator, new FakeLiveStateReader(), Profile());

        provider.Refresh();
        provider.Refresh();

        Assert.Equal(2, locator.ProcessStarts.Count);
        Assert.False(provider.IsAvailable);
        Assert.Null(provider.CurrentBeatmapPath);
    }

    [Fact]
    public void NativeProvider_RemainsAvailableWithoutBackground()
    {
        using var scope = new TempBeatmap(includeBackground: false);
        var provider = new NativeStableGameProvider(
            new FakeRuntime(Context(41, DateTimeOffset.Parse("2026-09-28T10:00:00Z")),
                scope.BeatmapPath),
            new FakeLocator(Unique()),
            new FakeLiveStateReader(),
            Profile());

        GameStateSnapshot result = provider.Refresh();

        Assert.True(result.IsAvailable);
        Assert.Equal(GameStateAvailability.Available, result.Availability);
        Assert.Null(result.CurrentBackgroundPath);
    }

    [Fact]
    public void NativeProvider_ExposesResolvedBackground()
    {
        using var scope = new TempBeatmap(includeBackground: true);
        var provider = new NativeStableGameProvider(
            new FakeRuntime(Context(41, DateTimeOffset.Parse("2026-09-28T10:00:00Z")),
                scope.BeatmapPath),
            new FakeLocator(Unique()),
            new FakeLiveStateReader(),
            Profile());

        GameStateSnapshot result = provider.Refresh();

        Assert.True(result.IsAvailable);
        Assert.Equal(scope.BackgroundPath, result.CurrentBackgroundPath);
    }

    [Theory]
    [InlineData(5, GameRoute.SongSelect)]
    [InlineData(2, GameRoute.Gameplay)]
    [InlineData(7, GameRoute.Results)]
    [InlineData(0, GameRoute.Unknown)]
    [InlineData(4, GameRoute.Unknown)]
    [InlineData(13, GameRoute.Unknown)]
    public void StatusMapsOnlyValidatedRoutes(int status, GameRoute expected)
    {
        DateTimeOffset start = DateTimeOffset.Parse("2026-10-08T10:00:00Z");
        var provider = LiveProvider(start, new FakeLiveStateReader(status: status));
        provider.Refresh();

        GameRouteSnapshot result = provider.ReadGameRoute();

        Assert.Equal(expected, result.Route);
        Assert.Equal(provider.ProcessIdentity, result.ProcessIdentity);
    }

    [Fact]
    public void StatusReadFailure_ReturnsUnknown()
    {
        DateTimeOffset start = DateTimeOffset.Parse("2026-10-08T10:00:00Z");
        var provider = LiveProvider(start, new FakeLiveStateReader(status: null));
        provider.Refresh();

        GameRouteSnapshot result = provider.ReadGameRoute();

        Assert.Equal(GameRoute.Unknown, result.Route);
    }

    [Theory]
    [InlineData(12345, 12345)]
    [InlineData(0, 0)]
    [InlineData(-1, null)]
    public void PlayTimeFollowsExistingPositionContract(
        int playTime,
        int? expectedMilliseconds)
    {
        DateTimeOffset start = DateTimeOffset.Parse("2026-10-08T10:00:00Z");
        var provider = LiveProvider(start, new FakeLiveStateReader(playTime: playTime));
        provider.Refresh();

        GameplayPositionSnapshot result = provider.ReadGameplayPosition();

        Assert.Equal(expectedMilliseconds, (int?)result.CurrentTime?.TotalMilliseconds);
        Assert.Equal(provider.ProcessIdentity, result.ProcessIdentity);
    }

    [Fact]
    public void PlayTimeReadFailure_ReturnsUnavailable()
    {
        DateTimeOffset start = DateTimeOffset.Parse("2026-10-08T10:00:00Z");
        var provider = LiveProvider(start, new FakeLiveStateReader(playTime: null));
        provider.Refresh();

        GameplayPositionSnapshot result = provider.ReadGameplayPosition();

        Assert.Null(result.CurrentTime);
    }

    [Fact]
    public void ProcessIdentityMismatch_FailsClosedAndInvalidatesReaders()
    {
        DateTimeOffset first = DateTimeOffset.Parse("2026-10-08T10:00:00Z");
        var live = new FakeLiveStateReader(status: 5, playTime: 1000);
        var provider = new NativeStableGameProvider(
            new FakeRuntime(
                Context(41, first), @"D:\osu!\Songs\set\map.osu",
                Context(52, first.AddMinutes(1))),
            new FakeLocator(Unique()),
            live,
            Profile());
        provider.Refresh();
        int statusReadsBeforeMismatch = live.StatusReadCount;

        GameRouteSnapshot result = provider.ReadGameRoute();

        Assert.Equal(GameRoute.Unknown, result.Route);
        Assert.Equal(1, live.InvalidateCount);
        Assert.Equal(statusReadsBeforeMismatch, live.StatusReadCount);
    }

    [Fact]
    public void GameStateServiceAutomaticallyUsesStableLiveInterfaces()
    {
        DateTimeOffset start = DateTimeOffset.Parse("2026-10-08T10:00:00Z");
        var provider = new NativeStableGameProvider(
            new FakeRuntime(
                Context(41, start), @"D:\osu!\Songs\set\map.osu",
                Context(41, start), Context(41, start)),
            new FakeLocator(Unique()),
            new FakeLiveStateReader(status: 2, playTime: 4321),
            Profile());
        var service = new GameStateService(provider);
        service.Refresh();

        GameRouteSnapshot route = service.ReadGameRoute();
        GameplayPositionSnapshot position = service.ReadGameplayPosition();

        Assert.Equal(GameRoute.Gameplay, route.Route);
        Assert.Equal(TimeSpan.FromMilliseconds(4321), position.CurrentTime);
    }

    [Fact]
    public void LiveReader_DiscoversOnlyOnceForSameGeneration()
    {
        DateTimeOffset start = DateTimeOffset.Parse("2026-10-08T10:00:00Z");
        var discovery = new FakePatternDiscovery();
        var reader = new NativeStableLiveStateReader(discovery);
        using NativeStableGameProcessContext first = Context(41, start);
        using NativeStableGameProcessContext second = Context(41, start);

        Assert.True(reader.EnsureDiscovered(first));
        Assert.True(reader.EnsureDiscovered(second));

        Assert.Equal(1, discovery.Count);
    }

    [Fact]
    public void LiveReader_FailedDiscoveryRetriesAfterBackoffAndRecoversSameGeneration()
    {
        DateTimeOffset start = DateTimeOffset.Parse("2026-10-08T10:00:00Z");
        var discovery = new FakePatternDiscovery(succeeds: false);
        long timestamp = 0;
        var reader = new NativeStableLiveStateReader(
            discovery,
            () => timestamp,
            timestampFrequency: 1000);
        using NativeStableGameProcessContext first = Context(41, start);
        using NativeStableGameProcessContext second = Context(41, start);

        Assert.False(reader.EnsureDiscovered(first));
        Assert.False(reader.EnsureDiscovered(second));
        Assert.Equal(1, discovery.Count);

        discovery.Succeeds = true;
        timestamp = 1999;
        Assert.False(reader.EnsureDiscovered(second));
        Assert.Equal(1, discovery.Count);

        timestamp = 2000;
        Assert.True(reader.EnsureDiscovered(second));
        Assert.True(reader.EnsureDiscovered(first));
        Assert.Equal(2, discovery.Count);
    }

    [Fact]
    public void LiveReader_GenerationChangeRediscoversAndRejectsOldContext()
    {
        DateTimeOffset start = DateTimeOffset.Parse("2026-10-08T10:00:00Z");
        var discovery = new FakePatternDiscovery();
        var reader = new NativeStableLiveStateReader(discovery);
        using NativeStableGameProcessContext first = Context(41, start);
        using NativeStableGameProcessContext second = Context(52, start.AddMinutes(1));

        Assert.True(reader.EnsureDiscovered(first));
        Assert.True(reader.EnsureDiscovered(second));

        Assert.Equal(2, discovery.Count);
        Assert.Null(reader.ReadStatus(first));
    }

    [Fact]
    public void FailedLiveDiscovery_MakesProviderUnavailable()
    {
        var provider = new NativeStableGameProvider(
            new FakeRuntime(
                Context(41, DateTimeOffset.Parse("2026-10-08T10:00:00Z")),
                @"D:\osu!\Songs\set\map.osu"),
            new FakeLocator(Unique()),
            new FakeLiveStateReader(discoverySucceeds: false),
            Profile());

        GameStateSnapshot result = provider.Refresh();

        Assert.False(result.IsAvailable);
        Assert.Contains("signatures", result.DiagnosticReason, StringComparison.Ordinal);
    }

    [Fact]
    public void InitiallyUnavailableLiveStateCanRecoverWithoutProviderRestart()
    {
        DateTimeOffset start = DateTimeOffset.Parse("2026-10-08T10:00:00Z");
        var live = new FakeLiveStateReader(discoverySucceeds: false);
        var provider = new NativeStableGameProvider(
            new FakeRuntime(
                Context(41, start),
                @"D:\osu!\Songs\set\map.osu",
                Context(41, start)),
            new FakeLocator(Unique()),
            live,
            Profile());

        GameStateSnapshot unavailable = provider.Refresh();
        live.DiscoverySucceeds = true;
        GameStateSnapshot recovered = provider.Refresh();

        Assert.False(unavailable.IsAvailable);
        Assert.True(recovered.IsAvailable);
        Assert.Equal("41:2026-10-08T10:00:00.0000000Z", recovered.ProcessIdentity);
    }

    private static NativeStableGameProvider LiveProvider(
        DateTimeOffset start,
        INativeStableLiveStateReader live) => new(
        new FakeRuntime(
            Context(41, start), @"D:\osu!\Songs\set\map.osu",
            Context(41, start)),
        new FakeLocator(Unique()),
        live,
        Profile());

    private static NativeStableGameProvider Provider(
        NativeStableGameLocatorOutcome outcome) => new(
        new FakeRuntime(Context(41, DateTimeOffset.Parse("2026-09-28T10:00:00Z")),
            @"D:\osu!\Songs\set\map.osu"),
        new FakeLocator(outcome),
        new FakeLiveStateReader(),
        Profile());

    private static NativeStableGameLocatorOutcome Unique() => new(
        NativeStableAutonomousQDiscovery.Unique,
        true,
        "set",
        "map.osu",
        3724444);

    private static NativeStableAutonomousQProfile Profile() => new(
        1,
        NativeStableRegionBacking.Private,
        0x04,
        NativeStableInterop.MemoryPrivate,
        4,
        0x1000,
        [0x100],
        [0x100],
        new(0, []),
        0.80);

    private static NativeStableGameProcessContext Context(
        int processId,
        DateTimeOffset start) => new(
        processId,
        start,
        @"D:\osu!\osu!.exe",
        new EmptyMemory(),
        [],
        []);

    private static string ProviderSource()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine(root, "BeatInsight", "Integration", "Game",
            "NativeStableGameProvider.cs"));
    }

    private sealed class FakeRuntime : INativeStableGameRuntime
    {
        private readonly Queue<NativeStableGameProcessContext?> contexts;
        private readonly string? resolvedPath;

        internal FakeRuntime(
            NativeStableGameProcessContext? first,
            string? resolvedPath,
            params NativeStableGameProcessContext?[] remaining)
        {
            contexts = new([first, .. remaining]);
            this.resolvedPath = resolvedPath;
        }

        public NativeStableGameProcessContext? TryOpen() =>
            contexts.Count == 0 ? null : contexts.Dequeue();

        public string? TryResolveBeatmapPath(
            NativeStableGameProcessContext context,
            string folder,
            string filename) => resolvedPath;
    }

    private sealed class TempBeatmap : IDisposable
    {
        internal TempBeatmap(
            bool includeBackground,
            int? beatmapId = null,
            int? beatmapSetId = null)
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                $"BeatInsight-NativeBackground-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
            BeatmapPath = Path.Combine(DirectoryPath, "map.osu");
            BackgroundPath = Path.Combine(DirectoryPath, "native background.jpeg");
            if (includeBackground)
            {
                File.WriteAllText(BackgroundPath, "image");
            }

            string backgroundEvent = includeBackground
                ? "0,0,\"native background.jpeg\",0,0"
                : "// no background";
            File.WriteAllText(
                BeatmapPath,
                $"osu file format v14{Environment.NewLine}" +
                $"[Metadata]{Environment.NewLine}" +
                (beatmapId is null ? string.Empty : $"BeatmapID:{beatmapId}{Environment.NewLine}") +
                (beatmapSetId is null ? string.Empty : $"BeatmapSetID:{beatmapSetId}{Environment.NewLine}") +
                $"[Events]{Environment.NewLine}{backgroundEvent}{Environment.NewLine}");
        }

        private string DirectoryPath { get; }

        internal string BeatmapPath { get; }

        internal string BackgroundPath { get; }

        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }

    private sealed class ChangingMapRuntime(
        IEnumerable<NativeStableGameProcessContext> contexts,
        IEnumerable<string> paths) : INativeStableGameRuntime
    {
        private readonly Queue<NativeStableGameProcessContext> contexts = new(contexts);
        private readonly Queue<string> paths = new(paths);

        public NativeStableGameProcessContext? TryOpen() =>
            contexts.Count == 0 ? null : contexts.Dequeue();

        public string? TryResolveBeatmapPath(
            NativeStableGameProcessContext context,
            string folder,
            string filename) => paths.Count == 0 ? null : paths.Dequeue();
    }

    private sealed class FakeBeatmapIdentityReader(
        params NativeStableBeatmapIdentity[] identities)
        : INativeStableBeatmapIdentityReader
    {
        private readonly Queue<NativeStableBeatmapIdentity> identities = new(identities);

        internal int ReadCount { get; private set; }

        public NativeStableBeatmapIdentity Read(string beatmapPath)
        {
            ReadCount++;
            return identities.Dequeue();
        }
    }

    private sealed class FakeLocator(params NativeStableGameLocatorOutcome[] outcomes)
        : INativeStableGameLocator
    {
        private readonly Queue<NativeStableGameLocatorOutcome> outcomes = new(outcomes);

        internal List<DateTimeOffset> ProcessStarts { get; } = [];

        public NativeStableGameLocatorOutcome Locate(
            NativeStableGameProcessContext context,
            NativeStableAutonomousQProfile profile)
        {
            ProcessStarts.Add(context.ProcessStartIdentity);
            return outcomes.Dequeue();
        }
    }

    private sealed class EmptyMemory : INativeStableMemoryReader
    {
        public bool Is32Bit => true;

        public NativeStableMemoryReadResult ReadBytes(nuint address, int count) =>
            new([], false, 0);

        public bool TryReadPointer(nuint address, out nuint value)
        {
            value = 0;
            return false;
        }
    }

    private sealed class FakeLiveStateReader(
        int? status = 5,
        int? playTime = 0,
        bool discoverySucceeds = true) : INativeStableLiveStateReader
    {
        internal bool DiscoverySucceeds { get; set; } = discoverySucceeds;

        internal int InvalidateCount { get; private set; }

        internal int StatusReadCount { get; private set; }

        public bool EnsureDiscovered(NativeStableGameProcessContext context) =>
            DiscoverySucceeds;

        public int? ReadStatus(NativeStableGameProcessContext context)
        {
            StatusReadCount++;
            return status;
        }

        public int? ReadPlayTime(NativeStableGameProcessContext context) => playTime;

        public void Invalidate() => InvalidateCount++;
    }

    private sealed class FakePatternDiscovery(bool succeeds = true)
        : INativeStableLivePatternDiscovery
    {
        internal bool Succeeds { get; set; } = succeeds;

        internal int Count { get; private set; }

        public NativeStableLivePatternAddresses? Discover(
            NativeStableGameProcessContext context)
        {
            Count++;
            return Succeeds ? new(0x100, 0x200, 0x300) : null;
        }
    }

    private sealed class CountingGameplayStatsReader : INativeStableGameplayStatsReader
    {
        internal int ReadCount { get; private set; }
        internal int InvalidateCount { get; private set; }

        public NativeStableGameplayStatsReaderDiagnostics Diagnostics => new(
            null,
            NativeStableGameplayStatsDiscoveryState.NotAttempted,
            false,
            NativeStableGameplayStatsFailureReason.DiscoveryNotReady,
            ReadCount,
            0,
            NativeStableGameplayStatsDiscoveryFailure.None,
            NativeStableGameplayStatsDiscoveryReport.NotAttempted,
            null,
            null,
            null);

        public void ObserveProcessIdentity(string processIdentity)
        {
        }

        public NativeStableGameplayStats? Read(
            NativeStableGameProcessContext context,
            string processIdentity)
        {
            ReadCount++;
            return null;
        }

        public NativeStableGameplayStats? ReadCached(
            NativeStableGameProcessContext context,
            string processIdentity,
            out NativeStableGameplayStatsFailureReason failureReason)
        {
            failureReason = NativeStableGameplayStatsFailureReason.DiscoveryNotReady;
            return null;
        }

        public void Invalidate() => InvalidateCount++;
    }
}
