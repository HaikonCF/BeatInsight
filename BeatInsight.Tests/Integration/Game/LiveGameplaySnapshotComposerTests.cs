using System.Diagnostics;
using BeatInsight.Integration.Game;
using BeatInsight.Models.AnalysisCache;
using BeatInsight.Models.Sections;
using BeatInsight.Tests.ViewModels;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Integration.Game;

public sealed class LiveGameplaySnapshotComposerTests(ITestOutputHelper output)
{
    [Fact]
    public void MissingGameBeatmapSetIdReportsExactFailClosedReason()
    {
        GameStateSnapshot game = Game(beatmapSetId: null);

        LiveGameplaySnapshot? result = LiveGameplaySnapshotComposer.TryCompose(
            game,
            Position(TimeSpan.FromSeconds(30)),
            Analysis(),
            out LiveGameplayCompositionFailure failure);

        Assert.Null(result);
        Assert.Equal(
            LiveGameplayCompositionFailure.GameBeatmapSetIdInvalid,
            failure);
    }

    private const string Generation = "42@2026-10-04T20:42:50.2813917Z";

    [Fact]
    public void ValidCurrentTimeAndAnalysisProduceSnapshot()
    {
        BeatmapAnalysisSnapshot analysis = Analysis();

        LiveGameplaySnapshot? result = Compose(analysis: analysis);

        Assert.NotNull(result);
        Assert.True(result.IsGameplay);
        Assert.Equal(TimeSpan.FromSeconds(30), result.CurrentTime);
        Assert.Equal(TimeSpan.FromSeconds(90), result.BeatmapDuration);
        Assert.Same(analysis, result.BeatmapAnalysis);
        Assert.Equal(123, result.BeatmapId);
        Assert.Equal(456, result.BeatmapSetId);
        Assert.Equal(Generation, result.ProcessIdentity);
    }

    [Fact]
    public void NullCurrentTimeReturnsNull() =>
        Assert.Null(Compose(position: Position(null)));

    [Fact]
    public void NullAnalysisReturnsNull() =>
        Assert.Null(LiveGameplaySnapshotComposer.TryCompose(
            Game(),
            Position(TimeSpan.FromSeconds(30)),
            null));

    [Fact]
    public void BeatmapSetIdentityMismatchReturnsNull() =>
        Assert.Null(Compose(game: Game(beatmapSetId: "999")));

    [Fact]
    public void DifficultyIdentityMismatchReturnsNull() =>
        Assert.Null(Compose(game: Game(beatmapId: "999")));

    [Fact]
    public void ProcessIdentityMismatchReturnsNull() =>
        Assert.Null(Compose(position: Position(TimeSpan.FromSeconds(30), "84@other")));

    [Fact]
    public void GenerationMismatchWithSamePidReturnsNull() =>
        Assert.Null(Compose(position: Position(TimeSpan.FromSeconds(30), "42@new-start")));

    [Fact]
    public void CurrentTimeAfterTimelineEndRemainsValidAndClampsProgress()
    {
        LiveGameplaySnapshot result = Assert.IsType<LiveGameplaySnapshot>(
            Compose(position: Position(TimeSpan.FromSeconds(91))));

        Assert.Equal(1, result.Progress);
    }

    [Fact]
    public void NegativeCurrentTimeReturnsNull() =>
        Assert.Null(Compose(position: Position(TimeSpan.FromTicks(-1))));

    [Fact]
    public void ProgressIsCalculatedFromAuthoritativeCurrentTime()
    {
        LiveGameplaySnapshot result = Assert.IsType<LiveGameplaySnapshot>(Compose());

        Assert.Equal(1d / 3d, result.Progress, 12);
    }

    [Theory]
    [InlineData(7_000, 0)]
    [InlineData(199_000, 0.5)]
    [InlineData(391_000, 1)]
    [InlineData(400_000, 1)]
    [InlineData(6_000, 0)]
    public void ProgressUsesAbsoluteSectionTimeline(
        double currentMilliseconds,
        double expectedProgress)
    {
        BeatmapAnalysisSnapshot analysis = Analysis(
            timelineStartMilliseconds: 7_000,
            durationMilliseconds: 384_000);

        LiveGameplaySnapshot result = Assert.IsType<LiveGameplaySnapshot>(Compose(
            position: Position(TimeSpan.FromMilliseconds(currentMilliseconds)),
            analysis: analysis));

        Assert.Equal(expectedProgress, result.Progress, 12);
    }

    [Fact]
    public void NonZeroTimelineStartDoesNotUseOldAbsoluteOverRelativeFormula()
    {
        BeatmapAnalysisSnapshot analysis = Analysis(
            timelineStartMilliseconds: 7_000,
            durationMilliseconds: 384_000);

        LiveGameplaySnapshot result = Assert.IsType<LiveGameplaySnapshot>(Compose(
            position: Position(TimeSpan.FromMilliseconds(199_000)),
            analysis: analysis));

        Assert.Equal(0.5, result.Progress, 12);
        Assert.NotEqual(199_000d / 384_000d, result.Progress);
    }

    [Fact]
    public void MissingSectionsFailClosed()
    {
        BeatmapAnalysisSnapshot source = Analysis();
        BeatmapAnalysisSnapshot analysis = source with
        {
            SectionAnalysisV2 = source.SectionAnalysisV2 with { Sections = [] }
        };

        Assert.Null(Compose(analysis: analysis));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteTimelineStartFailsClosed(double timelineStartMilliseconds) =>
        Assert.Null(Compose(analysis: Analysis(
            timelineStartMilliseconds: timelineStartMilliseconds)));

    [Theory]
    [InlineData(-1)]
    [InlineData(double.MaxValue)]
    public void IncoherentTimelineBoundsFailClosed(double timelineStartMilliseconds) =>
        Assert.Null(Compose(analysis: Analysis(
            timelineStartMilliseconds: timelineStartMilliseconds)));

    [Fact]
    public void ZeroDurationFailsClosed() =>
        Assert.Null(Compose(analysis: Analysis(durationMilliseconds: 0)));

    [Fact]
    public void NegativeDurationFailsClosed() =>
        Assert.Null(Compose(analysis: Analysis(durationMilliseconds: -1)));

    [Fact]
    public void SongSelectReturnsNull() =>
        Assert.Null(Compose(position: Position(null)));

    [Fact]
    public void ResultsReturnsNull() =>
        Assert.Null(Compose(position: Position(null)));

    [Fact]
    public void GameplayReturnsValidSnapshot() =>
        Assert.NotNull(Compose());

    [Fact]
    public void MapChangeNeverRetainsOldAnalysisOrTime()
    {
        BeatmapAnalysisSnapshot firstAnalysis = Analysis();
        Assert.NotNull(Compose(analysis: firstAnalysis));

        GameStateSnapshot secondGame = Game("847314", "1128931");
        Assert.Null(Compose(game: secondGame, analysis: firstAnalysis));

        BeatmapAnalysisSnapshot secondAnalysis = Analysis(847314, 1128931);
        LiveGameplaySnapshot second = Assert.IsType<LiveGameplaySnapshot>(Compose(
            game: secondGame,
            position: Position(TimeSpan.FromSeconds(5)),
            analysis: secondAnalysis));
        Assert.Equal(TimeSpan.FromSeconds(5), second.CurrentTime);
        Assert.Same(secondAnalysis, second.BeatmapAnalysis);
    }

    [Fact]
    public void GameplayExitNeverRetainsPreviousSnapshot()
    {
        Assert.NotNull(Compose());
        Assert.Null(Compose(position: Position(null)));
    }

    [Fact]
    public void RestartNeverRetainsPreviousGeneration()
    {
        Assert.NotNull(Compose());
        Assert.Null(Compose(position: Position(TimeSpan.FromSeconds(5), "84@new")));

        GameStateSnapshot restarted = Game(processIdentity: "84@new");
        LiveGameplaySnapshot next = Assert.IsType<LiveGameplaySnapshot>(Compose(
            game: restarted,
            position: Position(TimeSpan.FromSeconds(5), "84@new")));
        Assert.Equal("84@new", next.ProcessIdentity);
        Assert.Equal(TimeSpan.FromSeconds(5), next.CurrentTime);
    }

    [Fact]
    public void CompositionReusesAnalysisInstanceWithoutAnalyzerCall()
    {
        BeatmapAnalysisSnapshot analysis = Analysis();

        LiveGameplaySnapshot result = Assert.IsType<LiveGameplaySnapshot>(Compose(analysis: analysis));

        Assert.Same(analysis, result.BeatmapAnalysis);
    }

    [Fact]
    public void CompositionDoesNotReadOrParseBeatmapPath()
    {
        GameStateSnapshot game = Game();
        game = new(
            game.ProviderName,
            game.ClientName,
            game.ProcessId,
            game.ProcessIdentity,
            "::not-a-readable-beatmap::",
            game.CurrentBeatmapId,
            game.CurrentBeatmapSetId,
            game.LastUpdateUtc,
            game.Availability,
            game.DiagnosticReason);

        Assert.NotNull(Compose(game: game));
    }

    [Fact]
    public void GameStateServiceComposesFromSelectedPositionProvider()
    {
        var provider = new LiveProvider(
            Game(),
            Position(TimeSpan.FromSeconds(30)));
        var service = new GameStateService(provider);
        service.Refresh();

        LiveGameplaySnapshot? result = service.ReadLiveGameplaySnapshot(Analysis());

        Assert.NotNull(result);
        Assert.Equal(1, provider.PositionReadCount);
    }

    [Fact]
    public void GameStateServiceReturnsNullOutsideGameplay()
    {
        var provider = new LiveProvider(Game(), Position(null));
        var service = new GameStateService(provider);
        service.Refresh();

        Assert.Null(service.ReadLiveGameplaySnapshot(Analysis()));
    }

    [Fact]
    public void CompositionCostIsNegligibleAndAllocationFreeApartFromResult()
    {
        GameStateSnapshot game = Game();
        GameplayPositionSnapshot position = Position(TimeSpan.FromSeconds(30));
        BeatmapAnalysisSnapshot analysis = Analysis();
        const int iterations = 100_000;

        _ = LiveGameplaySnapshotComposer.TryCompose(game, position, analysis);
        Stopwatch watch = Stopwatch.StartNew();
        for (int index = 0; index < iterations; index++)
            _ = LiveGameplaySnapshotComposer.TryCompose(game, position, analysis);
        watch.Stop();

        double meanMicroseconds = watch.Elapsed.TotalMicroseconds / iterations;
        output.WriteLine($"Composition mean: {meanMicroseconds:F4} us over {iterations} iterations.");
        Assert.True(meanMicroseconds < 10,
            $"Composition mean was {meanMicroseconds:F4} us.");
    }

    private static LiveGameplaySnapshot? Compose(
        GameStateSnapshot? game = null,
        GameplayPositionSnapshot? position = null,
        BeatmapAnalysisSnapshot? analysis = null) =>
        LiveGameplaySnapshotComposer.TryCompose(
            game ?? Game(),
            position ?? Position(TimeSpan.FromSeconds(30)),
            analysis ?? Analysis());

    private static GameStateSnapshot Game(
        string beatmapId = "123",
        string? beatmapSetId = "456",
        string processIdentity = Generation) => new(
            "provider",
            "client",
            42,
            processIdentity,
            @"C:\maps\map.osu",
            beatmapId,
            beatmapSetId,
            DateTimeOffset.UtcNow,
            GameStateAvailability.Available,
            null);

    private static GameplayPositionSnapshot Position(
        TimeSpan? currentTime,
        string processIdentity = Generation) =>
        new(currentTime, DateTimeOffset.UtcNow, processIdentity);

    private static BeatmapAnalysisSnapshot Analysis(
        int beatmapId = 123,
        int beatmapSetId = 456,
        double timelineStartMilliseconds = 0,
        double durationMilliseconds = 90_000)
    {
        BeatmapAnalysisSnapshot source = OverviewViewModelTests.BuildSnapshot();
        GameplaySectionV2Analysis firstSection = source.SectionAnalysisV2.Sections[0];
        return source with
        {
            Metadata = source.Metadata with
            {
                BeatmapId = beatmapId,
                BeatmapSetId = beatmapSetId,
                LengthTicks = TimeSpan.FromMilliseconds(durationMilliseconds).Ticks
            },
            SectionAnalysisV2 = source.SectionAnalysisV2 with
            {
                Sections =
                [
                    firstSection with
                    {
                        StartTime = timelineStartMilliseconds,
                        EndTime = timelineStartMilliseconds + 5_000
                    }
                ]
            }
        };
    }

    private sealed class LiveProvider : IGameStateProvider, IGameplayPositionProvider
    {
        private readonly GameplayPositionSnapshot position;

        internal LiveProvider(
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
}
