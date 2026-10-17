using BeatInsight.Diagnostics.NativeLazer;
using BeatInsight.Integration.Game;
using BeatInsight.Models;
using BeatInsight.Parser;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace BeatInsight.Tests.Integration.Game;

public sealed class LazerGameProviderTests : IDisposable
{
    private static readonly NativeLazerProcessGeneration GenerationA =
        new(101, new DateTime(2026, 9, 30, 1, 2, 3, DateTimeKind.Utc));
    private static readonly NativeLazerProcessGeneration GenerationB =
        new(202, new DateTime(2026, 9, 30, 2, 3, 4, DateTimeKind.Utc));
    private readonly string blobPath;
    private readonly string backgroundAPath;
    private readonly string backgroundBPath;

    public LazerGameProviderTests()
    {
        blobPath = Path.Combine(Path.GetTempPath(), $"beatinsight-lazer-{Guid.NewGuid():N}");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps",
            "(can you) understand me [uhh].osu"), blobPath);
        backgroundAPath = blobPath + "-background-a";
        backgroundBPath = blobPath + "-background-b";
        File.WriteAllText(backgroundAPath, "background a");
        File.WriteAllText(backgroundBPath, "background b");
    }

    [Fact]
    public void Provider_ExposesLiveGameplayStatsContract()
    {
        var provider = new LazerGameProvider(
            new FakeRuntime(() => new(NativeLazerProcessProbeStatus.None)),
            new ControlledScheduler().Start);

        Assert.IsAssignableFrom<ILiveGameplayStatsProvider>(provider);
        Assert.IsAssignableFrom<ILiveGameplayStatsDiagnosticsProvider>(provider);
    }

    [Fact]
    public void ExistingParser_AcceptsExtensionlessBlobPath()
    {
        Beatmap beatmap = BeatmapParser.Load(blobPath);

        Assert.NotEmpty(beatmap.Title);
        Assert.NotEmpty(beatmap.HitObjects);
    }

    [Fact]
    public void NoProcess_ReturnsNoProcessWithoutScheduling()
    {
        var runtime = new FakeRuntime(() => new(NativeLazerProcessProbeStatus.None));
        var scheduler = new ControlledScheduler();
        var provider = new LazerGameProvider(runtime, scheduler.Start);

        GameStateSnapshot result = provider.Refresh();

        Assert.Equal(GameStateAvailability.NoProcess, result.Availability);
        Assert.Equal(0, scheduler.StartCount);
        AssertNoBeatmap(result);
    }

    [Fact]
    public void FirstRefresh_SchedulesOnceAndReturnsPromptly()
    {
        var runtime = new FakeRuntime(() => Unique(GenerationA));
        var scheduler = new ControlledScheduler();
        var provider = new LazerGameProvider(runtime, scheduler.Start);
        var timer = Stopwatch.StartNew();

        GameStateSnapshot first = provider.Refresh();
        GameStateSnapshot second = provider.Refresh();

        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(1));
        Assert.Equal(1, scheduler.StartCount);
        Assert.Equal(GameStateAvailability.ProviderUnavailable, first.Availability);
        Assert.Equal("NativeLazer discovery in progress.", first.DiagnosticReason);
        Assert.Equal(GameStateAvailability.ProviderUnavailable, second.Availability);
        Assert.Equal(0, runtime.RefreshCount);
    }

    [Fact]
    public void CompletedDiscovery_InstallsAvailableExtensionlessBlob()
    {
        var runtime = new FakeRuntime(() => Unique(GenerationA));
        var scheduler = new ControlledScheduler();
        var provider = new LazerGameProvider(runtime, scheduler.Start);
        provider.Refresh();
        scheduler.CompleteNext(Available(GenerationA, 2431320, 1049899));

        GameStateSnapshot result = provider.Refresh();

        Assert.Equal(GameStateAvailability.Available, result.Availability);
        Assert.Equal(LazerGameProvider.ProviderName, result.ProviderName);
        Assert.Equal("osu!lazer", result.ClientName);
        Assert.Equal("2431320", result.CurrentBeatmapId);
        Assert.Equal("1049899", result.CurrentBeatmapSetId);
        Assert.Equal(Path.GetFullPath(blobPath), result.CurrentBeatmapPath);
        Assert.Equal(GenerationA.Display, result.ProcessIdentity);
    }

    [Fact]
    public void ObsoleteDiscoveryResult_IsRejectedAndNewGenerationIsScheduled()
    {
        NativeLazerProcessGeneration current = GenerationA;
        var runtime = new FakeRuntime(() => Unique(current));
        var scheduler = new ControlledScheduler();
        var provider = new LazerGameProvider(runtime, scheduler.Start);
        provider.Refresh();
        scheduler.CompleteNext(Available(GenerationA, 2431320, 1049899));
        current = GenerationB;

        GameStateSnapshot result = provider.Refresh();

        Assert.Equal(GameStateAvailability.ProviderUnavailable, result.Availability);
        Assert.Equal(2, scheduler.StartCount);
        Assert.Equal(GenerationB.Display, result.ProcessIdentity);
        AssertNoBeatmap(result);
    }

    [Fact]
    public void FastRefresh_MapsAThenBWithoutRediscovery()
    {
        var runtime = new FakeRuntime(() => Unique(GenerationA));
        var scheduler = new ControlledScheduler();
        var provider = new LazerGameProvider(runtime, scheduler.Start);
        provider.Refresh();
        scheduler.CompleteNext(Available(GenerationA, 2431320, 1049899));
        Assert.Equal("2431320", provider.Refresh().CurrentBeatmapId);
        runtime.Enqueue(Available(GenerationA, 847314, 404938));

        GameStateSnapshot result = provider.Refresh();

        Assert.Equal(GameStateAvailability.Available, result.Availability);
        Assert.Equal("847314", result.CurrentBeatmapId);
        Assert.Equal(1, runtime.RefreshCount);
        Assert.Equal(1, scheduler.StartCount);
    }

    [Fact]
    public void AvailableMapping_ExposesResolvedBackground()
    {
        var runtime = new FakeRuntime(() => Unique(GenerationA));
        var scheduler = new ControlledScheduler();
        var backgrounds = new FakeBackgroundResolver(
            new Dictionary<int, string?> { [1049899] = backgroundAPath });
        var provider = InstallAvailable(runtime, scheduler, backgrounds);

        Assert.Equal(backgroundAPath, provider.Current.CurrentBackgroundPath);
        Assert.Equal(1, backgrounds.ResolveCount);
    }

    [Fact]
    public void BackgroundMissing_DoesNotAffectAvailability()
    {
        var runtime = new FakeRuntime(() => Unique(GenerationA));
        var scheduler = new ControlledScheduler();
        var provider = InstallAvailable(
            runtime,
            scheduler,
            new FakeBackgroundResolver(new Dictionary<int, string?>()));

        Assert.True(provider.Current.IsAvailable);
        Assert.Null(provider.Current.CurrentBackgroundPath);
    }

    [Fact]
    public void FastRefresh_ChangesBackgroundWithBeatmap()
    {
        var runtime = new FakeRuntime(() => Unique(GenerationA));
        var scheduler = new ControlledScheduler();
        var backgrounds = new FakeBackgroundResolver(new Dictionary<int, string?>
        {
            [1049899] = backgroundAPath,
            [404938] = backgroundBPath
        });
        var provider = InstallAvailable(runtime, scheduler, backgrounds);
        runtime.Enqueue(Available(GenerationA, 847314, 404938));

        GameStateSnapshot result = provider.Refresh();

        Assert.Equal("847314", result.CurrentBeatmapId);
        Assert.Equal(backgroundBPath, result.CurrentBackgroundPath);
        Assert.Equal(2, backgrounds.ResolveCount);
    }

    [Fact]
    public void NewBeatmapWithoutBackground_DoesNotRetainPreviousBackground()
    {
        var runtime = new FakeRuntime(() => Unique(GenerationA));
        var scheduler = new ControlledScheduler();
        var backgrounds = new FakeBackgroundResolver(
            new Dictionary<int, string?> { [1049899] = backgroundAPath });
        var provider = InstallAvailable(runtime, scheduler, backgrounds);
        runtime.Enqueue(Available(GenerationA, 847314, 404938));

        GameStateSnapshot result = provider.Refresh();

        Assert.True(result.IsAvailable);
        Assert.Equal("847314", result.CurrentBeatmapId);
        Assert.Null(result.CurrentBackgroundPath);
    }

    [Fact]
    public void NonAvailableSnapshot_ClearsBackground()
    {
        var runtime = new FakeRuntime(() => Unique(GenerationA));
        var scheduler = new ControlledScheduler();
        var backgrounds = new FakeBackgroundResolver(
            new Dictionary<int, string?> { [1049899] = backgroundAPath });
        var provider = InstallAvailable(runtime, scheduler, backgrounds);
        runtime.Enqueue(NoBeatmap(GenerationA));

        GameStateSnapshot result = provider.Refresh();

        Assert.Equal(GameStateAvailability.NoBeatmap, result.Availability);
        Assert.Null(result.CurrentBackgroundPath);
    }

    [Fact]
    public void NullWorkingBeatmap_ClearsPreviouslyAvailableMap()
    {
        var runtime = new FakeRuntime(() => Unique(GenerationA));
        var scheduler = new ControlledScheduler();
        var provider = InstallAvailable(runtime, scheduler);
        runtime.Enqueue(NoBeatmap(GenerationA));

        GameStateSnapshot result = provider.Refresh();

        Assert.Equal(GameStateAvailability.NoBeatmap, result.Availability);
        AssertNoBeatmap(result);
        Assert.Equal(1, scheduler.StartCount);
    }

    [Fact]
    public void RootInvalidation_ClearsMapAndStartsOneBackgroundRediscovery()
    {
        var runtime = new FakeRuntime(() => Unique(GenerationA));
        var scheduler = new ControlledScheduler();
        var provider = InstallAvailable(runtime, scheduler);
        runtime.Enqueue(Unknown(GenerationA, "Cached root failed structural revalidation."));

        GameStateSnapshot result = provider.Refresh();
        GameStateSnapshot whilePending = provider.Refresh();

        Assert.False(result.IsAvailable);
        AssertNoBeatmap(result);
        Assert.Equal(GameStateAvailability.ProviderUnavailable, whilePending.Availability);
        Assert.Equal(2, scheduler.StartCount);
    }

    [Fact]
    public void ExitAndRestart_InvalidateImmediatelyAndNeverPublishOldMap()
    {
        NativeLazerProcessProbe probe = Unique(GenerationA);
        var runtime = new FakeRuntime(() => probe);
        var scheduler = new ControlledScheduler();
        var provider = InstallAvailable(runtime, scheduler);
        probe = new(NativeLazerProcessProbeStatus.None);

        GameStateSnapshot exited = provider.Refresh();
        probe = Unique(GenerationB);
        GameStateSnapshot restarted = provider.Refresh();

        Assert.Equal(GameStateAvailability.NoProcess, exited.Availability);
        AssertNoBeatmap(exited);
        Assert.Equal(GameStateAvailability.ProviderUnavailable, restarted.Availability);
        Assert.Equal(GenerationB.Display, restarted.ProcessIdentity);
        AssertNoBeatmap(restarted);
        Assert.Equal(2, scheduler.StartCount);
    }

    [Fact]
    public async Task ConcurrentRefreshWhileDiscoveryPending_SchedulesOnlyOnce()
    {
        var runtime = new FakeRuntime(() => Unique(GenerationA));
        var scheduler = new ControlledScheduler();
        var provider = new LazerGameProvider(runtime, scheduler.Start);

        GameStateSnapshot[] results = await Task.WhenAll(
            Enumerable.Range(0, 32).Select(_ => Task.Run(provider.Refresh)));

        Assert.Equal(1, scheduler.StartCount);
        Assert.All(results, result =>
        {
            Assert.Equal(GameStateAvailability.ProviderUnavailable, result.Availability);
            AssertNoBeatmap(result);
        });
    }

    [Fact]
    public void GameplayPositionRequiresInstalledGeneration()
    {
        var runtime = new FakeRuntime(() => Unique(GenerationA));
        var provider = new LazerGameProvider(runtime);

        GameplayPositionSnapshot result = provider.ReadGameplayPosition();

        Assert.Null(result.CurrentTime);
        Assert.Equal(0, runtime.PositionReadCount);
        Assert.Equal(0, runtime.RefreshCount);
    }

    [Fact]
    public void GameRouteRequiresInstalledGeneration()
    {
        var runtime = new FakeRuntime(() => new(NativeLazerProcessProbeStatus.None));
        var provider = new LazerGameProvider(runtime);

        GameRouteSnapshot result = provider.ReadGameRoute();

        Assert.Equal(GameRoute.Unknown, result.Route);
        Assert.Null(result.ProcessIdentity);
        Assert.Equal(0, runtime.RouteReadCount);
    }

    [Theory]
    [InlineData("SongSelect", GameRoute.SongSelect)]
    [InlineData("Gameplay", GameRoute.Gameplay)]
    [InlineData("Results", GameRoute.Results)]
    [InlineData("Unexpected", GameRoute.Unknown)]
    public void GameRouteUsesDedicatedRuntimeOperation(
        string nativeRoute,
        GameRoute expected)
    {
        var runtime = new FakeRuntime(() => Unique(GenerationA));
        var scheduler = new ControlledScheduler();
        var provider = InstallAvailable(runtime, scheduler);
        runtime.EnqueueRoute(Route(GenerationA, nativeRoute));
        int refreshesBefore = runtime.RefreshCount;

        GameRouteSnapshot result = provider.ReadGameRoute();

        Assert.Equal(expected, result.Route);
        Assert.Equal(GenerationA.Display, result.ProcessIdentity);
        Assert.Equal(1, runtime.RouteReadCount);
        Assert.Equal(refreshesBefore, runtime.RefreshCount);
    }

    [Fact]
    public void UnknownRouteNeverRetainsPreviousRoute()
    {
        var runtime = new FakeRuntime(() => Unique(GenerationA));
        var scheduler = new ControlledScheduler();
        var provider = InstallAvailable(runtime, scheduler);
        runtime.EnqueueRoute(Route(GenerationA, "Gameplay"));
        runtime.EnqueueRoute(Route(GenerationA, "Unknown"));

        Assert.Equal(GameRoute.Gameplay, provider.ReadGameRoute().Route);
        Assert.Equal(GameRoute.Unknown, provider.ReadGameRoute().Route);
    }

    [Fact]
    public void RouteGenerationMismatchIsRejectedAndInvalidatesReader()
    {
        var runtime = new FakeRuntime(() => Unique(GenerationA));
        var scheduler = new ControlledScheduler();
        var provider = InstallAvailable(runtime, scheduler);
        runtime.EnqueueRoute(Route(GenerationB, "Results"));

        GameRouteSnapshot mismatch = provider.ReadGameRoute();
        GameRouteSnapshot afterInvalidation = provider.ReadGameRoute();

        Assert.Equal(GameRoute.Unknown, mismatch.Route);
        Assert.Equal(GameRoute.Unknown, afterInvalidation.Route);
        Assert.Equal(1, runtime.RouteReadCount);
    }

    [Fact]
    public void RestartAcceptsOnlyNewGenerationRouteAfterValidation()
    {
        NativeLazerProcessGeneration generation = GenerationA;
        var runtime = new FakeRuntime(() => Unique(generation));
        var scheduler = new ControlledScheduler();
        var provider = InstallAvailable(runtime, scheduler);
        runtime.EnqueueRoute(Route(GenerationA, "Gameplay"));
        Assert.Equal(GameRoute.Gameplay, provider.ReadGameRoute().Route);

        generation = GenerationB;
        Assert.False(provider.Refresh().IsAvailable);
        Assert.Equal(GameRoute.Unknown, provider.ReadGameRoute().Route);
        scheduler.CompleteNext(Available(GenerationB, 847314, 404938));
        Assert.True(provider.Refresh().IsAvailable);
        runtime.EnqueueRoute(Route(GenerationB, "SongSelect"));

        GameRouteSnapshot result = provider.ReadGameRoute();

        Assert.Equal(GameRoute.SongSelect, result.Route);
        Assert.Equal(GenerationB.Display, result.ProcessIdentity);
    }

    [Fact]
    public void GameplayPositionUsesDedicatedRuntimeOperationWithoutBeatmapRefresh()
    {
        var runtime = new FakeRuntime(() => Unique(GenerationA));
        var scheduler = new ControlledScheduler();
        var provider = InstallAvailable(runtime, scheduler);
        runtime.EnqueuePosition(Position(GenerationA, 42_000));
        int refreshesBefore = runtime.RefreshCount;

        GameplayPositionSnapshot result = provider.ReadGameplayPosition();

        Assert.Equal(TimeSpan.FromSeconds(42), result.CurrentTime);
        Assert.Equal(GenerationA.Display, result.ProcessIdentity);
        Assert.Equal(1, runtime.PositionReadCount);
        Assert.Equal(refreshesBefore, runtime.RefreshCount);
    }

    [Fact]
    public void NonGameplayAndInvalidValuesNeverReturnPreviousPosition()
    {
        var runtime = new FakeRuntime(() => Unique(GenerationA));
        var scheduler = new ControlledScheduler();
        var provider = InstallAvailable(runtime, scheduler);
        runtime.EnqueuePosition(Position(GenerationA, 42_000));
        runtime.EnqueuePosition(Position(GenerationA, null, "None"));
        runtime.EnqueuePosition(Position(GenerationA, double.NaN));

        Assert.Equal(TimeSpan.FromSeconds(42), provider.ReadGameplayPosition().CurrentTime);
        Assert.Null(provider.ReadGameplayPosition().CurrentTime);
        Assert.Null(provider.ReadGameplayPosition().CurrentTime);
    }

    [Fact]
    public void PositionGenerationMismatchIsRejectedAndInvalidatesReader()
    {
        var runtime = new FakeRuntime(() => Unique(GenerationA));
        var scheduler = new ControlledScheduler();
        var provider = InstallAvailable(runtime, scheduler);
        runtime.EnqueuePosition(Position(GenerationB, 3_000));

        GameplayPositionSnapshot mismatch = provider.ReadGameplayPosition();
        GameplayPositionSnapshot afterInvalidation = provider.ReadGameplayPosition();

        Assert.Null(mismatch.CurrentTime);
        Assert.Null(afterInvalidation.CurrentTime);
        Assert.Equal(1, runtime.PositionReadCount);
    }

    [Fact]
    public void NewGenerationReturnsOnlyItsOwnPositionAfterNormalValidation()
    {
        NativeLazerProcessGeneration generation = GenerationA;
        var runtime = new FakeRuntime(() => Unique(generation));
        var scheduler = new ControlledScheduler();
        var provider = InstallAvailable(runtime, scheduler);
        runtime.EnqueuePosition(Position(GenerationA, 42_000));
        Assert.Equal(TimeSpan.FromSeconds(42), provider.ReadGameplayPosition().CurrentTime);

        generation = GenerationB;
        Assert.False(provider.Refresh().IsAvailable);
        scheduler.CompleteNext(Available(GenerationB, 847314, 404938));
        Assert.True(provider.Refresh().IsAvailable);
        runtime.EnqueuePosition(Position(GenerationB, 3_000));

        GameplayPositionSnapshot result = provider.ReadGameplayPosition();

        Assert.Equal(TimeSpan.FromSeconds(3), result.CurrentTime);
        Assert.Equal(GenerationB.Display, result.ProcessIdentity);
    }

    [Fact]
    public void CompletedNoBeatmapDiscovery_InstallsRootForNextFastRefresh()
    {
        var runtime = new FakeRuntime(() => Unique(GenerationA));
        var scheduler = new ControlledScheduler();
        var provider = new LazerGameProvider(runtime, scheduler.Start);
        provider.Refresh();
        scheduler.CompleteNext(NoBeatmap(GenerationA));
        Assert.Equal(GameStateAvailability.NoBeatmap, provider.Refresh().Availability);
        runtime.Enqueue(Available(GenerationA, 2431320, 1049899));

        GameStateSnapshot result = provider.Refresh();

        Assert.True(result.IsAvailable);
        Assert.Equal(1, runtime.RefreshCount);
        Assert.Equal(1, scheduler.StartCount);
    }

    [Fact]
    public void ValidNativeGameplayStats_ArePublishedWithoutInventingValues()
    {
        var runtime = new FakeRuntime(() => Unique(GenerationA));
        var scheduler = new ControlledScheduler();
        var provider = InstallAvailable(runtime, scheduler);
        runtime.EnqueueRoute(Route(GenerationA, "Gameplay"));
        runtime.EnqueueStats(Stats(GenerationA));

        LiveGameplayStatsReadResult result = provider.ReadLiveGameplayStatsWithDiagnostics();

        LiveGameplayStatsSnapshot snapshot = Assert.IsType<LiveGameplayStatsSnapshot>(result.Snapshot);
        Assert.Equal(123, snapshot.CurrentCombo);
        Assert.Equal(150, snapshot.MaxComboReached);
        Assert.Equal(90, snapshot.Count300);
        Assert.Equal(6, snapshot.Count100);
        Assert.Equal(2, snapshot.Count50);
        Assert.Equal(1, snapshot.CountMiss);
        Assert.Equal(543_210, snapshot.Score);
        Assert.Equal(["HD"], snapshot.Mods.Select(mod => mod.Acronym));
        Assert.Equal("2431320", snapshot.BeatmapId);
        Assert.Equal(LiveGameplayStatsFailureReason.None, result.Diagnostics.FailureReason);
        Assert.True(result.Diagnostics.ReaderResolved);
    }

    [Fact]
    public void NativeStatsUnavailable_FailsClosedAndPreservesExactReason()
    {
        var runtime = new FakeRuntime(() => Unique(GenerationA));
        var scheduler = new ControlledScheduler();
        var provider = InstallAvailable(runtime, scheduler);
        runtime.EnqueueRoute(Route(GenerationA, "Gameplay"));
        runtime.EnqueueStats(new()
        {
            ProcessIdentity = GenerationA.Display,
            ProcessId = GenerationA.ProcessId,
            Outcome = "Unavailable",
            FailureReason = "ScoreProcessor changed during the read."
        });

        LiveGameplayStatsReadResult result = provider.ReadLiveGameplayStatsWithDiagnostics();

        Assert.Null(result.Snapshot);
        Assert.Equal(LiveGameplayStatsFailureReason.ProviderReturnedNull, result.Diagnostics.FailureReason);
        Assert.Equal("ScoreProcessor changed during the read.", result.Diagnostics.LastDiscoveryFailure);
    }

    [Fact]
    public void StatsRouteFromAnotherGeneration_InvalidatesAndNeverReadsStats()
    {
        var runtime = new FakeRuntime(() => Unique(GenerationA));
        var scheduler = new ControlledScheduler();
        var provider = InstallAvailable(runtime, scheduler);
        runtime.EnqueueRoute(Route(GenerationB, "Gameplay"));

        LiveGameplayStatsReadResult result = provider.ReadLiveGameplayStatsWithDiagnostics();
        LiveGameplayStatsReadResult afterInvalidation = provider.ReadLiveGameplayStatsWithDiagnostics();

        Assert.Null(result.Snapshot);
        Assert.Equal(LiveGameplayStatsFailureReason.ProcessIdentityMismatch, result.Diagnostics.FailureReason);
        Assert.Equal(LiveGameplayStatsFailureReason.GameUnavailable, afterInvalidation.Diagnostics.FailureReason);
        Assert.Equal(0, runtime.StatsReadCount);
    }

    [Fact]
    public void NonGameplayRoute_FailsClosedWithoutReadingStats()
    {
        var runtime = new FakeRuntime(() => Unique(GenerationA));
        var scheduler = new ControlledScheduler();
        var provider = InstallAvailable(runtime, scheduler);
        runtime.EnqueueRoute(Route(GenerationA, "Results"));

        LiveGameplayStatsReadResult result = provider.ReadLiveGameplayStatsWithDiagnostics();

        Assert.Null(result.Snapshot);
        Assert.Equal(LiveGameplayStatsFailureReason.RouteNotGameplay, result.Diagnostics.FailureReason);
        Assert.Equal(0, runtime.StatsReadCount);
    }

    public void Dispose()
    {
        File.Delete(blobPath);
        File.Delete(backgroundAPath);
        File.Delete(backgroundBPath);
    }

    private LazerGameProvider InstallAvailable(
        FakeRuntime runtime,
        ControlledScheduler scheduler,
        ILazerBackgroundResolver? backgroundResolver = null)
    {
        var provider = new LazerGameProvider(runtime, scheduler.Start, backgroundResolver);
        provider.Refresh();
        scheduler.CompleteNext(Available(GenerationA, 2431320, 1049899));
        Assert.True(provider.Refresh().IsAvailable);
        return provider;
    }

    private NativeLazerServerResponse Available(
        NativeLazerProcessGeneration generation,
        int beatmapId,
        int setId) => new()
    {
        ProcessIdentity = generation.Display,
        ProcessId = generation.ProcessId,
        Outcome = "Available",
        RootValid = true,
        BeatmapOnlineId = beatmapId,
        BeatmapSetOnlineId = setId,
        BeatmapHash = new string('A', 64),
        BeatmapBlobPath = blobPath,
        BlobExists = true
    };

    private static NativeLazerServerResponse NoBeatmap(NativeLazerProcessGeneration generation) => new()
    {
        ProcessIdentity = generation.Display,
        ProcessId = generation.ProcessId,
        Outcome = "NoBeatmap",
        RootValid = true
    };

    private static NativeLazerServerResponse Unknown(
        NativeLazerProcessGeneration generation,
        string reason) => new()
    {
        ProcessIdentity = generation.Display,
        ProcessId = generation.ProcessId,
        Outcome = "Unknown",
        RootValid = false,
        FailureReason = reason
    };

    private static NativeLazerProcessProbe Unique(NativeLazerProcessGeneration generation) =>
        new(NativeLazerProcessProbeStatus.Unique, generation);

    private static NativeLazerGameplayPositionResponse Position(
        NativeLazerProcessGeneration generation,
        double? milliseconds,
        string outcome = "Unique") => new()
    {
        ProcessIdentity = generation.Display,
        ProcessId = generation.ProcessId,
        Outcome = outcome,
        CurrentTimeMilliseconds = milliseconds
    };

    private static NativeLazerGameRouteResponse Route(
        NativeLazerProcessGeneration generation,
        string route) => new()
    {
        ProcessIdentity = generation.Display,
        ProcessId = generation.ProcessId,
        Route = route
    };

    private static NativeLazerGameplayStatsResponse Stats(
        NativeLazerProcessGeneration generation) => new()
    {
        ProcessIdentity = generation.Display,
        ProcessId = generation.ProcessId,
        Outcome = "Valid",
        CurrentTimeMilliseconds = 42_000,
        CurrentCombo = 123,
        HighestCombo = 150,
        CountGreat = 90,
        CountOk = 6,
        CountMeh = 2,
        CountMiss = 1,
        Accuracy = (90 * 300d + 6 * 100d + 2 * 50d) / (99 * 300d),
        Score = 543_210,
        Mods = ["HD"],
        ObservedAtUtc = DateTimeOffset.UtcNow
    };

    private static void AssertNoBeatmap(GameStateSnapshot snapshot)
    {
        Assert.Null(snapshot.CurrentBeatmapPath);
        Assert.Null(snapshot.CurrentBeatmapId);
        Assert.Null(snapshot.CurrentBeatmapSetId);
    }

    private sealed class ControlledScheduler
    {
        private readonly ConcurrentQueue<TaskCompletionSource<NativeLazerServerResponse>> pending = new();
        private int startCount;

        internal int StartCount => Volatile.Read(ref startCount);

        internal Task<NativeLazerServerResponse> Start(Func<NativeLazerServerResponse> _)
        {
            Interlocked.Increment(ref startCount);
            var source = new TaskCompletionSource<NativeLazerServerResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            pending.Enqueue(source);
            return source.Task;
        }

        internal void CompleteNext(NativeLazerServerResponse response)
        {
            Assert.True(pending.TryDequeue(out TaskCompletionSource<NativeLazerServerResponse>? source));
            source.SetResult(response);
        }
    }

    private sealed class FakeRuntime(Func<NativeLazerProcessProbe> probe) : INativeLazerGameRuntime
    {
        private readonly ConcurrentQueue<NativeLazerServerResponse> responses = new();
        private readonly ConcurrentQueue<NativeLazerGameplayPositionResponse> positions = new();
        private readonly ConcurrentQueue<NativeLazerGameRouteResponse> routes = new();
        private readonly ConcurrentQueue<NativeLazerGameplayStatsResponse> stats = new();
        private int refreshCount;
        private int positionReadCount;
        private int routeReadCount;
        private int statsReadCount;

        internal int RefreshCount => Volatile.Read(ref refreshCount);
        internal int PositionReadCount => Volatile.Read(ref positionReadCount);
        internal int RouteReadCount => Volatile.Read(ref routeReadCount);
        internal int StatsReadCount => Volatile.Read(ref statsReadCount);

        public NativeLazerProcessProbe Probe() => probe();

        public NativeLazerServerResponse Refresh()
        {
            Interlocked.Increment(ref refreshCount);
            return responses.TryDequeue(out NativeLazerServerResponse? response)
                ? response
                : throw new InvalidOperationException("No fake NativeLazer response was queued.");
        }

        internal void Enqueue(NativeLazerServerResponse response) => responses.Enqueue(response);

        public NativeLazerGameplayPositionResponse ReadGameplayPosition()
        {
            Interlocked.Increment(ref positionReadCount);
            return positions.TryDequeue(out NativeLazerGameplayPositionResponse? response)
                ? response
                : throw new InvalidOperationException("No fake gameplay-position response was queued.");
        }

        internal void EnqueuePosition(NativeLazerGameplayPositionResponse response) =>
            positions.Enqueue(response);

        public NativeLazerGameRouteResponse ReadGameRoute()
        {
            Interlocked.Increment(ref routeReadCount);
            return routes.TryDequeue(out NativeLazerGameRouteResponse? response)
                ? response
                : throw new InvalidOperationException("No fake game-route response was queued.");
        }

        internal void EnqueueRoute(NativeLazerGameRouteResponse response) =>
            routes.Enqueue(response);

        public NativeLazerGameplayStatsResponse ReadGameplayStats()
        {
            Interlocked.Increment(ref statsReadCount);
            return stats.TryDequeue(out NativeLazerGameplayStatsResponse? response)
                ? response
                : throw new InvalidOperationException("No fake gameplay-stats response was queued.");
        }

        internal void EnqueueStats(NativeLazerGameplayStatsResponse response) =>
            stats.Enqueue(response);
    }

    private sealed class FakeBackgroundResolver(
        IReadOnlyDictionary<int, string?> backgrounds) : ILazerBackgroundResolver
    {
        private int resolveCount;
        internal int ResolveCount => Volatile.Read(ref resolveCount);

        public string? Resolve(string beatmapBlobPath, int beatmapSetOnlineId)
        {
            Assert.True(File.Exists(beatmapBlobPath));
            Interlocked.Increment(ref resolveCount);
            return backgrounds.TryGetValue(beatmapSetOnlineId, out string? path) ? path : null;
        }
    }
}
