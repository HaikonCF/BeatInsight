using BeatInsight.Integration.Game;
using BeatInsight.Overlay;
using BeatInsight.Overlay.GameplayHud;
using System.Collections.Immutable;

namespace BeatInsight.Tests.Overlay;

public sealed class ResultsPerformanceLatchTests
{
    [Theory]
    [InlineData("NativeStable")]
    [InlineData("NativeLazer")]
    public void GameplayToResultsKeepsAndFreezesLastValidPerformance(string provider)
    {
        var latch = new ResultsPerformanceLatch();
        GameStateSnapshot game = Game(provider);

        Assert.Equal(100, latch.Select(
            OverlayGameState.Gameplay, game, Stats(10, 10), TimeSpan.FromSeconds(10), Pp(100))!.CurrentPp);
        Assert.Equal(150, latch.Select(
            OverlayGameState.Gameplay, game, Stats(20, 20), TimeSpan.FromSeconds(20), Pp(150))!.CurrentPp);

        LivePerformanceSnapshot results = Assert.IsType<LivePerformanceSnapshot>(latch.Select(
            OverlayGameState.Results, game, null, null, null));
        LivePerformanceSnapshot frozen = Assert.IsType<LivePerformanceSnapshot>(latch.Select(
            OverlayGameState.Results, game, null, null, Pp(999)));

        Assert.Equal(150, results.CurrentPp);
        Assert.Same(results, frozen);
    }

    [Fact]
    public void ResultsWithoutValidGameplayPerformanceInventsNothing()
    {
        var latch = new ResultsPerformanceLatch();

        Assert.Null(latch.Select(
            OverlayGameState.Gameplay, Game(), Stats(0, 0), TimeSpan.Zero, null));
        Assert.Null(latch.Select(
            OverlayGameState.Results, Game(), null, null, null));
    }

    [Fact]
    public void ResultsToSongSelectClearsFinalPerformance()
    {
        var latch = Latched(out GameStateSnapshot game);

        Assert.NotNull(latch.Select(OverlayGameState.Results, game, null, null, null));
        Assert.Null(latch.Select(OverlayGameState.SongSelect, game, null, null, null));
        Assert.Null(latch.Select(OverlayGameState.Results, game, null, null, null));
    }

    [Fact]
    public void ResultsToNewGameplayClearsOldAttemptBeforeFreshPerformanceExists()
    {
        var latch = Latched(out GameStateSnapshot game);
        Assert.NotNull(latch.Select(OverlayGameState.Results, game, null, null, null));

        Assert.Null(latch.Select(
            OverlayGameState.Gameplay, game, null, TimeSpan.Zero, null));
        Assert.Null(latch.Select(
            OverlayGameState.Results, game, null, null, null));
    }

    [Fact]
    public void RetryWithoutRouteChangeClearsOldAttemptOnCounterOrClockRegression()
    {
        var latch = new ResultsPerformanceLatch();
        GameStateSnapshot game = Game();
        _ = latch.Select(
            OverlayGameState.Gameplay, game, Stats(100, 60), TimeSpan.FromSeconds(60), Pp(250));

        Assert.Null(latch.Select(
            OverlayGameState.Gameplay, game, Stats(1, 1), TimeSpan.FromSeconds(1), null));
        Assert.Null(latch.Select(
            OverlayGameState.Results, game, null, null, null));
    }

    [Fact]
    public void BeatmapOrProcessGenerationChangeCannotReuseFinalPerformance()
    {
        var latch = Latched(out GameStateSnapshot game);

        Assert.Null(latch.Select(
            OverlayGameState.Results,
            Game(path: "other.osu", beatmapId: "999"),
            null,
            null,
            null));

        _ = latch.Select(
            OverlayGameState.Gameplay, game, Stats(20, 20), TimeSpan.FromSeconds(20), Pp(200));
        Assert.Null(latch.Select(
            OverlayGameState.Results,
            Game(processIdentity: "42@next-generation"),
            null,
            null,
            null));
    }

    [Fact]
    public void MismatchedStatsAreNeverLatchedForResults()
    {
        var latch = new ResultsPerformanceLatch();
        GameStateSnapshot game = Game();
        LiveGameplayStatsSnapshot foreign = Stats(20, 20) with
        {
            ProcessIdentity = "99@foreign",
            BeatmapId = "999"
        };

        Assert.NotNull(latch.Select(
            OverlayGameState.Gameplay, game, foreign, TimeSpan.FromSeconds(20), Pp(200)));
        Assert.Null(latch.Select(
            OverlayGameState.Results, game, null, null, null));
    }

    [Fact]
    public void TransientUnknownIsFailClosedWithoutDestroyingMatchingFinalResult()
    {
        var latch = Latched(out GameStateSnapshot game);

        Assert.Null(latch.Select(
            OverlayGameState.Unknown, game, null, null, null));
        Assert.Equal(150, latch.Select(
            OverlayGameState.Results, game, null, null, null)!.CurrentPp);
    }

    private static ResultsPerformanceLatch Latched(out GameStateSnapshot game)
    {
        var latch = new ResultsPerformanceLatch();
        game = Game();
        _ = latch.Select(
            OverlayGameState.Gameplay,
            game,
            Stats(20, 20),
            TimeSpan.FromSeconds(20),
            Pp(150));
        return latch;
    }

    private static GameStateSnapshot Game(
        string provider = "NativeStable",
        string processIdentity = "42@generation",
        string path = "map.osu",
        string beatmapId = "123") => new(
            provider,
            provider == "NativeLazer" ? "osu!lazer" : "osu!stable",
            42,
            processIdentity,
            path,
            beatmapId,
            "456",
            DateTimeOffset.UtcNow,
            GameStateAvailability.Available,
            null);

    private static LiveGameplayStatsSnapshot Stats(int judged, int seconds) => new(
        "42@generation",
        "123",
        "456",
        TimeSpan.FromSeconds(seconds),
        judged,
        judged,
        judged,
        0,
        0,
        0,
        1,
        ImmutableArray<LiveGameplayMod>.Empty,
        DateTimeOffset.UtcNow,
        judged * 1_000);

    private static LivePerformanceSnapshot Pp(double current) => new(
        current,
        current + 20,
        current + 10,
        current + 30,
        1,
        DateTimeOffset.UtcNow);
}
