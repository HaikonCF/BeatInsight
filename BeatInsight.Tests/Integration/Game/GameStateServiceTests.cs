using BeatInsight.Integration.Game;
using System.Collections.Immutable;

namespace BeatInsight.Tests.Integration.Game;

public sealed class GameStateServiceTests
{
    [Fact]
    public void FirstAvailableProviderWins()
    {
        var first = new FakeProvider("first", Available("first", 1));
        var second = new FakeProvider("second", Available("second", 2));

        GameStateSnapshot result = new GameStateService([first, second]).Refresh();

        Assert.Equal("first", result.ProviderName);
        Assert.Equal(0, second.RefreshCount);
    }

    [Fact]
    public void FallsBackToSecondProvider()
    {
        var first = new FakeProvider("first", Unavailable("first", "not ready"));
        var second = new FakeProvider("second", Available("second", 2));

        GameStateSnapshot result = new GameStateService([first, second]).Refresh();

        Assert.Equal("second", result.ProviderName);
        Assert.Equal(2, result.ProviderAttempts.Length);
    }

    [Fact]
    public void AllUnavailable_ReturnsUnavailable()
    {
        var service = new GameStateService([
            new FakeProvider("first", Unavailable("first", "no process")),
            new FakeProvider("second", Unavailable("second", "offline"))]);

        GameStateSnapshot result = service.Refresh();

        Assert.False(result.IsAvailable);
        Assert.Equal(GameStateAvailability.ProviderUnavailable, result.Availability);
        Assert.Equal(nameof(GameStateService), result.ProviderName);
    }

    [Fact]
    public void ProviderException_DoesNotEscape()
    {
        var service = new GameStateService([new FakeProvider("broken", new InvalidOperationException())]);

        Exception? exception = Record.Exception(service.Refresh);

        Assert.Null(exception);
        Assert.Equal(GameStateAvailability.Error, service.Current.Availability);
    }

    [Fact]
    public void ProviderException_FallsBack()
    {
        var service = new GameStateService([
            new FakeProvider("broken", new InvalidOperationException()),
            new FakeProvider("fallback", Available("fallback", 9))]);

        GameStateSnapshot result = service.Refresh();

        Assert.True(result.IsAvailable);
        Assert.Equal("fallback", result.ProviderName);
        Assert.Equal(GameStateAvailability.Error, result.ProviderAttempts[0].Availability);
    }

    [Fact]
    public void CurrentUpdatesAfterRefresh()
    {
        var service = new GameStateService([new FakeProvider("provider", Available("provider", 7))]);

        GameStateSnapshot result = service.Refresh();

        Assert.Same(result, service.Current);
        Assert.Equal(7, service.Current.ProcessId);
    }

    [Fact]
    public void NativeProcessLoss_ClearsCurrentState()
    {
        var provider = new FakeProvider(
            NativeStableGameProvider.ProviderName,
            Available(NativeStableGameProvider.ProviderName, 7),
            Unavailable(NativeStableGameProvider.ProviderName, "process closed"));
        var service = new GameStateService([provider]);

        service.Refresh();
        GameStateSnapshot result = service.Refresh();

        Assert.False(result.IsAvailable);
        Assert.Null(result.ProcessId);
        Assert.Null(result.CurrentBeatmapPath);
    }

    [Fact]
    public void NativeRecovery_RestoresAvailableState()
    {
        var provider = new FakeProvider(
            NativeStableGameProvider.ProviderName,
            Unavailable(NativeStableGameProvider.ProviderName, "process closed"),
            Available(NativeStableGameProvider.ProviderName, 11));
        var service = new GameStateService([provider]);

        GameStateSnapshot unavailable = service.Refresh();
        GameStateSnapshot result = service.Refresh();

        Assert.False(unavailable.IsAvailable);
        Assert.True(result.IsAvailable);
        Assert.Equal(11, result.ProcessId);
        Assert.Equal("11:identity", result.ProcessIdentity);
    }

    [Fact]
    public void ProviderOrderIsRespected()
    {
        var service = new GameStateService([
            new FakeProvider("z-provider", Available("z-provider", 1)),
            new FakeProvider("a-provider", Available("a-provider", 2))]);

        GameStateSnapshot result = service.Refresh();

        Assert.Equal("z-provider", result.ProviderName);
    }

    [Fact]
    public void NoProviders_ReturnsProviderUnavailable()
    {
        GameStateSnapshot result = new GameStateService([]).Refresh();

        Assert.Equal(GameStateAvailability.ProviderUnavailable, result.Availability);
        Assert.Equal("No providers configured.", result.DiagnosticReason);
        Assert.Empty(result.ProviderAttempts);
    }

    [Fact]
    public async Task ConcurrentRefresh_DoesNotCorruptState()
    {
        var provider = new CountingProvider("counter");
        var service = new GameStateService([provider]);

        GameStateSnapshot[] results = await Task.WhenAll(
            Enumerable.Range(0, 32).Select(_ => Task.Run(service.Refresh)));

        Assert.Equal(32, provider.RefreshCount);
        Assert.All(results, result => Assert.True(result.IsAvailable));
        Assert.InRange(service.Current.ProcessId!.Value, 1, 32);
        Assert.Equal(32, results.Select(result => result.ProcessId).Distinct().Count());
    }

    [Fact]
    public void DiagnosticsPreserved()
    {
        var service = new GameStateService([
            new FakeProvider("first", Unavailable("first", "profile missing")),
            new FakeProvider("second", Available("second", 2, "fallback diagnostic"))]);

        GameStateSnapshot result = service.Refresh();

        Assert.Equal("fallback diagnostic", result.DiagnosticReason);
        Assert.Collection(
            result.ProviderAttempts,
            attempt => Assert.Equal("profile missing", attempt.DiagnosticReason),
            attempt => Assert.Equal("fallback diagnostic", attempt.DiagnosticReason));
    }

    [Fact]
    public void DefaultComposition_ContainsStableThenLazer()
    {
        IReadOnlyList<IGameStateProvider> providers =
            GameStateComposition.CreateDefaultProviders();

        Assert.Collection(
            providers,
            provider => Assert.IsType<NativeStableGameProvider>(provider),
            provider => Assert.IsType<LazerGameProvider>(provider));
    }

    [Fact]
    public void NativeUnavailable_ReturnsUnavailable()
    {
        var native = new FakeProvider(
            NativeStableGameProvider.ProviderName,
            Unavailable(NativeStableGameProvider.ProviderName, "no process"));
        GameStateService service = GameStateComposition.Create(native);

        GameStateSnapshot result = service.Refresh();

        Assert.False(result.IsAvailable);
        Assert.Equal(GameStateAvailability.ProviderUnavailable, result.Availability);
        Assert.Collection(
            result.ProviderAttempts,
            attempt =>
            {
                Assert.Equal(
                    NativeStableGameProvider.ProviderName,
                    attempt.ProviderName);
                Assert.Equal("test-client", attempt.ClientName);
            });
    }

    [Fact]
    public void SameBeatmapDoesNotTriggerDuplicateChange()
    {
        GameStateSnapshot first = AvailableAt(
            "NativeStable",
            @"C:\maps\set\map.osu",
            "42");
        GameStateSnapshot second = AvailableAt(
            "future-provider",
            @"C:\maps\set\.\map.osu",
            "42");

        Assert.True(GameStateBeatmapIdentity.AreSame(first, second));
    }

    [Fact]
    public void BeatmapChangeIsDetected()
    {
        GameStateSnapshot first = AvailableAt(
            "NativeStable",
            @"C:\maps\set-a\map.osu",
            "42");
        GameStateSnapshot second = AvailableAt(
            "future-provider",
            @"C:\maps\set-b\map.osu",
            "43");

        Assert.False(GameStateBeatmapIdentity.AreSame(first, second));
    }

    [Fact]
    public void GameplayPositionComesOnlyFromSelectedProvider()
    {
        var provider = new PositionProvider(
            Available("position", 7),
            new GameplayPositionSnapshot(TimeSpan.FromSeconds(12), DateTimeOffset.UtcNow, "7:identity"));
        var service = new GameStateService(provider);
        service.Refresh();

        GameplayPositionSnapshot result = service.ReadGameplayPosition();

        Assert.Equal(TimeSpan.FromSeconds(12), result.CurrentTime);
        Assert.Equal(1, provider.PositionReadCount);
    }

    [Fact]
    public void GameplayPositionGenerationMismatchFailsClosed()
    {
        var provider = new PositionProvider(
            Available("position", 7),
            new GameplayPositionSnapshot(TimeSpan.FromSeconds(12), DateTimeOffset.UtcNow, "8:identity"));
        var service = new GameStateService(provider);
        service.Refresh();

        GameplayPositionSnapshot result = service.ReadGameplayPosition();

        Assert.Null(result.CurrentTime);
        Assert.Equal("7:identity", result.ProcessIdentity);
    }

    [Fact]
    public void UnavailableGameDoesNotInvokeGameplayPositionProvider()
    {
        var provider = new PositionProvider(
            Unavailable("position", "offline"),
            new GameplayPositionSnapshot(TimeSpan.FromSeconds(12), DateTimeOffset.UtcNow, null));
        var service = new GameStateService(provider);
        service.Refresh();

        GameplayPositionSnapshot result = service.ReadGameplayPosition();

        Assert.Null(result.CurrentTime);
        Assert.Equal(0, provider.PositionReadCount);
    }

    [Fact]
    public void GameRouteComesOnlyFromSelectedProvider()
    {
        var provider = new RouteProvider(
            Available("route", 7),
            new GameRouteSnapshot(GameRoute.Results, DateTimeOffset.UtcNow, "7:identity"));
        var service = new GameStateService(provider);
        service.Refresh();

        GameRouteSnapshot result = service.ReadGameRoute();

        Assert.Equal(GameRoute.Results, result.Route);
        Assert.Equal(1, provider.RouteReadCount);
    }

    [Fact]
    public void GameRouteGenerationMismatchFailsClosed()
    {
        var provider = new RouteProvider(
            Available("route", 7),
            new GameRouteSnapshot(GameRoute.Gameplay, DateTimeOffset.UtcNow, "8:identity"));
        var service = new GameStateService(provider);
        service.Refresh();

        GameRouteSnapshot result = service.ReadGameRoute();

        Assert.Equal(GameRoute.Unknown, result.Route);
        Assert.Equal("7:identity", result.ProcessIdentity);
    }

    [Fact]
    public void UnavailableGameDoesNotInvokeGameRouteProvider()
    {
        var provider = new RouteProvider(
            Unavailable("route", "offline"),
            new GameRouteSnapshot(GameRoute.SongSelect, DateTimeOffset.UtcNow, null));
        var service = new GameStateService(provider);
        service.Refresh();

        GameRouteSnapshot result = service.ReadGameRoute();

        Assert.Equal(GameRoute.Unknown, result.Route);
        Assert.Equal(0, provider.RouteReadCount);
    }

    [Fact]
    public void LiveStatsComeOnlyFromSelectedProvider()
    {
        GameStateSnapshot game = Available("stats", 7);
        var provider = new StatsProvider(game, Stats(game));
        var service = new GameStateService(provider);
        service.Refresh();

        LiveGameplayStatsSnapshot result = Assert.IsType<LiveGameplayStatsSnapshot>(
            service.ReadLiveGameplayStats());

        Assert.Equal(12, result.CurrentCombo);
        Assert.Equal(1, provider.ReadCount);
    }

    [Fact]
    public void LiveStatsGenerationOrBeatmapMismatchFailsClosed()
    {
        GameStateSnapshot game = Available("stats", 7);
        var generation = new StatsProvider(game, Stats(game) with { ProcessIdentity = "8:identity" });
        var map = new StatsProvider(game, Stats(game) with { BeatmapId = "other" });
        var generationService = new GameStateService(generation);
        var mapService = new GameStateService(map);
        generationService.Refresh();
        mapService.Refresh();

        Assert.Null(generationService.ReadLiveGameplayStats(
            out LiveGameplayStatsDiagnostics generationDiagnostics));
        Assert.Equal(
            LiveGameplayStatsFailureReason.ProcessIdentityMismatch,
            generationDiagnostics.FailureReason);
        Assert.Null(mapService.ReadLiveGameplayStats(
            out LiveGameplayStatsDiagnostics mapDiagnostics));
        Assert.Equal(
            LiveGameplayStatsFailureReason.BeatmapIdMismatch,
            mapDiagnostics.FailureReason);
    }

    [Fact]
    public void InvalidLiveStatsFailClosed()
    {
        GameStateSnapshot game = Available("stats", 7);
        var provider = new StatsProvider(game, Stats(game) with { Accuracy = double.NaN });
        var service = new GameStateService(provider);
        service.Refresh();

        Assert.Null(service.ReadLiveGameplayStats());
    }

    private static LiveGameplayStatsSnapshot Stats(GameStateSnapshot game) => new(
        game.ProcessIdentity!, game.CurrentBeatmapId, game.CurrentBeatmapSetId,
        TimeSpan.FromSeconds(10), 12, 24, 100, 2, 1, 3, .95,
        ImmutableArray.Create(new LiveGameplayMod("HD")), DateTimeOffset.UtcNow);

    private static GameStateSnapshot Available(
        string provider,
        int processId,
        string? diagnostic = null) => new(
            provider,
            "test-client",
            processId,
            $"{processId}:identity",
            $@"C:\maps\{processId}.osu",
            processId.ToString(),
            null,
            DateTimeOffset.UtcNow,
            GameStateAvailability.Available,
            diagnostic);

    private static GameStateSnapshot AvailableAt(
        string provider,
        string path,
        string id) => new(
            provider,
            "test-client",
            null,
            null,
            path,
            id,
            null,
            DateTimeOffset.UtcNow,
            GameStateAvailability.Available,
            null);

    private static GameStateSnapshot Unavailable(
        string provider,
        string reason,
        GameStateAvailability availability = GameStateAvailability.ProviderUnavailable) => new(
        provider,
        "test-client",
        null,
        null,
        null,
        null,
        null,
        DateTimeOffset.UtcNow,
        availability,
        reason);

    private sealed class FakeProvider : IGameStateProvider
    {
        private readonly Queue<object> results;

        internal FakeProvider(string name, params object[] results)
        {
            Name = name;
            this.results = new(results);
            Current = Unavailable(name, "not refreshed");
        }

        public string Name { get; }

        public GameStateSnapshot Current { get; private set; }

        internal int RefreshCount { get; private set; }

        public GameStateSnapshot Refresh()
        {
            RefreshCount++;
            object next = results.Dequeue();
            if (next is Exception exception)
            {
                throw exception;
            }

            Current = (GameStateSnapshot)next;
            return Current;
        }
    }

    private sealed class CountingProvider(string name) : IGameStateProvider
    {
        private int refreshCount;

        public string Name { get; } = name;

        public GameStateSnapshot Current { get; private set; } = Unavailable(name, "not refreshed");

        internal int RefreshCount => Volatile.Read(ref refreshCount);

        public GameStateSnapshot Refresh()
        {
            int value = Interlocked.Increment(ref refreshCount);
            Current = Available(Name, value);
            return Current;
        }
    }

    private sealed class PositionProvider : IGameStateProvider, IGameplayPositionProvider
    {
        private readonly GameplayPositionSnapshot position;

        internal PositionProvider(
            GameStateSnapshot game,
            GameplayPositionSnapshot position)
        {
            Current = game;
            this.position = position;
        }

        public string Name => Current.ProviderName;
        public GameStateSnapshot Current { get; private set; }
        internal int PositionReadCount { get; private set; }

        public GameStateSnapshot Refresh() => Current;

        public GameplayPositionSnapshot ReadGameplayPosition()
        {
            PositionReadCount++;
            return position;
        }
    }

    private sealed class RouteProvider : IGameStateProvider, IGameRouteProvider
    {
        private readonly GameRouteSnapshot route;

        internal RouteProvider(GameStateSnapshot game, GameRouteSnapshot route)
        {
            Current = game;
            this.route = route;
        }

        public string Name => Current.ProviderName;
        public GameStateSnapshot Current { get; }
        internal int RouteReadCount { get; private set; }

        public GameStateSnapshot Refresh() => Current;

        public GameRouteSnapshot ReadGameRoute()
        {
            RouteReadCount++;
            return route;
        }
    }

    private sealed class StatsProvider : IGameStateProvider, ILiveGameplayStatsProvider
    {
        private readonly LiveGameplayStatsSnapshot? stats;
        internal StatsProvider(GameStateSnapshot game, LiveGameplayStatsSnapshot? stats)
        {
            Current = game;
            this.stats = stats;
        }
        public string Name => Current.ProviderName;
        public GameStateSnapshot Current { get; }
        internal int ReadCount { get; private set; }
        public GameStateSnapshot Refresh() => Current;
        public LiveGameplayStatsSnapshot? ReadLiveGameplayStats()
        {
            ReadCount++;
            return stats;
        }
    }
}
