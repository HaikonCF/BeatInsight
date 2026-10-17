using System.Collections.Immutable;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using BeatInsight.Integration.CommunityOverlays;
using BeatInsight.Integration.CommunityOverlays.Pipe;
using BeatInsight.Integration.Game;
using BeatInsight.Models.AnalysisCache;
using BeatInsight.Overlay;
using BeatInsight.Tests.ViewModels;
using Xunit;

namespace BeatInsight.Tests.Integration.CommunityOverlays;

/// <summary>
/// V3.14-COM-10 : recopie BeatInsight → DTO du Named Pipe, lecteur .osu et publication.
/// Aucun osu!, aucune lecture native : uniquement des snapshots construits.
/// </summary>
public sealed class CommunityOverlayPipeIntegrationTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private const string Process = "osu!.exe:4242:1";
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "bi-com10-" + Guid.NewGuid().ToString("N"));

    public CommunityOverlayPipeIntegrationTests() => Directory.CreateDirectory(_sandbox);

    public void Dispose()
    {
        try { Directory.Delete(_sandbox, recursive: true); } catch (IOException) { }
    }

    private static GameStateSnapshot Game(string path, DateTimeOffset at, string process = Process) =>
        new GameStateSnapshot("NativeStableGameProvider", "osu! stable", 4242, process, path, "123", "456", at,
            GameStateAvailability.Available, null)
        {
            SongsDirectory = Path.GetDirectoryName(Path.GetDirectoryName(path)),
            CurrentBackgroundPath = Path.Combine(Path.GetDirectoryName(path)!, "bg.jpg"),
        };

    private static BeatmapAnalysisSnapshot Analysis()
    {
        BeatmapAnalysisSnapshot source = OverviewViewModelTests.BuildSnapshot();
        return source with { Metadata = source.Metadata with { BeatmapId = 123, BeatmapSetId = 456, Title = "Song" } };
    }

    private static OverlaySnapshot GameplayOverlay(GameStateSnapshot game, BeatmapAnalysisSnapshot analysis, DateTimeOffset at) => new(
        game,
        OverlayGameState.Gameplay,
        analysis,
        LiveGameplay: new LiveGameplaySnapshot(true, TimeSpan.FromSeconds(12), TimeSpan.FromMinutes(2), 0.1, analysis, 123, 456, Process, at),
        LiveStats: new LiveGameplayStatsSnapshot(Process, "123", "456", TimeSpan.FromSeconds(12), 40, 52, 50, 3, 1, 2, 0.9425,
            [new LiveGameplayMod("HD"), new LiveGameplayMod("DT")], at, 123_456),
        Performance: new LivePerformanceSnapshot(88.5, 140.25, 120.0, 160.75, 56, at));

    // ------------------------------------------------------------ recopie

    [Fact]
    public void Mapper_copies_live_snapshots_without_alteration()
    {
        string path = @"C:\Games\osu!\Songs\1 A - B\A - B (M) [X].osu";
        var at = T0.AddMilliseconds(-120);
        var game = Game(path, at);
        var analysis = Analysis();
        var frame = new CommunityOverlaySourceFrame(GameplayOverlay(game, analysis, at), game, false, false, false);

        var envelope = CommunityOverlayPipeSnapshotMapper.Map(frame, null, 7, T0);

        Assert.Null(CommunityOverlayPipeProtocol.Validate(envelope));
        var s = envelope.Snapshot;
        Assert.Equal(7, envelope.Sequence);
        Assert.Equal(PipeRoute.Gameplay, s.Route!.Route);
        Assert.Equal(at, s.Route.ObservedAtUtc);
        Assert.Equal(at, s.Game!.LastUpdateUtc);
        Assert.Equal(path, s.Metadata!.BeatmapPath);
        Assert.Equal(analysis.Metadata.ApproachRate, s.Metadata.ApproachRate);
        Assert.Equal(analysis.Metadata.CircleCount, s.Metadata.CircleCount);
        Assert.Equal(12_000, s.LiveGameplay!.CurrentTimeMs);
        Assert.Equal((50, 3, 1, 2, 40, 52), (s.Stats!.Count300, s.Stats.Count100, s.Stats.Count50, s.Stats.CountMiss, s.Stats.CurrentCombo, s.Stats.MaxComboReached));
        Assert.Equal(0.9425, s.Stats.Accuracy);
        Assert.Equal(["HD", "DT"], s.Stats.Mods);
        Assert.Equal(123_456, s.Stats.Score);
        Assert.Equal(at, s.Stats.ObservedAtUtc);
        Assert.Equal((88.5, 140.25, 120.0, 160.75, 56), (s.Performance!.CurrentPp, s.Performance.FcPp, s.Performance.Accuracy95FcPp, s.Performance.SsPp, s.Performance.PassedObjects));
        Assert.Equal(PipeSuspension.None, s.Suspension);
    }

    [Fact]
    public void Mapper_drops_overlay_data_of_another_process()
    {
        string path = @"C:\Games\osu!\Songs\1 A - B\A - B (M) [X].osu";
        var oldGame = Game(path, T0, "osu!.exe:1:old");
        var newGame = Game(path, T0, "osu!.exe:2:new");
        var frame = new CommunityOverlaySourceFrame(GameplayOverlay(oldGame, Analysis(), T0), newGame, false, false, false);

        var s = CommunityOverlayPipeSnapshotMapper.Map(frame, null, 1, T0).Snapshot;

        Assert.NotNull(s.Game);
        Assert.Null(s.Route);
        Assert.Null(s.Metadata);
        Assert.Null(s.Stats);
        Assert.Null(s.Performance);
    }

    [Fact]
    public void Mapper_without_overlay_snapshot_sends_only_the_game_state()
    {
        var game = Game(@"C:\Games\osu!\Songs\1 A - B\A - B (M) [X].osu", T0);

        var s = CommunityOverlayPipeSnapshotMapper.Map(new(null, game, false, false, false), null, 1, T0).Snapshot;

        Assert.NotNull(s.Game);
        Assert.Null(s.Route);
        Assert.Null(s.Stats);
    }

    [Theory]
    [InlineData(true, false, false, PipeSuspension.OverlayLayoutEdit)]
    [InlineData(false, true, false, PipeSuspension.FastLabeling)]
    [InlineData(false, false, true, PipeSuspension.BackgroundLibraryWork)]
    [InlineData(true, true, true, PipeSuspension.OverlayLayoutEdit)]
    public void Mapper_reports_why_the_source_is_frozen(bool edit, bool fastLabeling, bool library, PipeSuspension expected)
    {
        var game = Game(@"C:\Games\osu!\Songs\1 A - B\A - B (M) [X].osu", T0);

        var s = CommunityOverlayPipeSnapshotMapper.Map(new(null, game, edit, fastLabeling, library), null, 1, T0).Snapshot;

        Assert.Equal(expected, s.Suspension);
    }

    [Fact]
    public void Mapper_keeps_the_osu_file_only_for_the_current_beatmap()
    {
        var game = Game(@"C:\Games\osu!\Songs\1 A - B\A - B (M) [X].osu", T0);
        var other = new PipeOsuFile(@"C:\Games\osu!\Songs\2 C - D\C - D (M) [Y].osu", 10, T0, new string('a', 32), "a.mp3", null, null);
        var same = other with { BeatmapPath = game.CurrentBeatmapPath! };

        Assert.Null(CommunityOverlayPipeSnapshotMapper.Map(new(null, game, false, false, false), other, 1, T0).Snapshot.OsuFile);
        Assert.NotNull(CommunityOverlayPipeSnapshotMapper.Map(new(null, game, false, false, false), same, 1, T0).Snapshot.OsuFile);
    }

    // ------------------------------------------------------------ fichier .osu

    private string WriteOsu(string name, string content)
    {
        string folder = Path.Combine(_sandbox, "Songs", "1 A - B");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private const string OsuContent = """
        osu file format v14

        [General]
        AudioFilename: audio.mp3
        AudioLeadIn: 0

        [Metadata]
        Title:Song
        TitleUnicode:ソング
        Artist:Artist
        ArtistUnicode:アーティスト
        Version:Hard

        [HitObjects]
        256,192,1000,1,0,0:0:0:0:
        AudioFilename: not-this-one.mp3
        """;

    [Fact]
    public void Osu_reader_returns_the_real_md5_and_header_fields()
    {
        string path = WriteOsu("A - B (M) [X].osu", OsuContent);
        var reader = new OsuFileIdentityReader();

        var file = reader.Read(path)!;

        Assert.Equal(Convert.ToHexString(MD5.HashData(File.ReadAllBytes(path))).ToLowerInvariant(), file.Md5);
        Assert.Equal("audio.mp3", file.AudioFilename);
        Assert.Equal("ソング", file.TitleUnicode);
        Assert.Equal("アーティスト", file.ArtistUnicode);
        Assert.Equal(new FileInfo(path).Length, file.FileLength);
    }

    [Fact]
    public void Osu_reader_caches_by_file_identity()
    {
        string path = WriteOsu("A - B (M) [X].osu", OsuContent);
        var reader = new OsuFileIdentityReader();

        var first = reader.Read(path);
        var second = reader.Read(path);
        Assert.Same(first, second);
        Assert.Equal(1, reader.ReadCount);

        File.WriteAllText(path, OsuContent.Replace("audio.mp3", "other.ogg"), new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        var third = reader.Read(path)!;
        Assert.Equal("other.ogg", third.AudioFilename);
        Assert.NotEqual(first!.Md5, third.Md5);
        Assert.Equal(2, reader.ReadCount);
    }

    [Fact]
    public void Osu_reader_refuses_unsafe_or_missing_inputs()
    {
        var reader = new OsuFileIdentityReader();
        string unsafeAudio = WriteOsu("unsafe.osu", "[General]\nAudioFilename: ..\\..\\secret.mp3\n");
        string notOsu = WriteOsu("notes.txt", "[General]\nAudioFilename: a.mp3\n");

        Assert.Null(reader.Read(null));
        Assert.Null(reader.Read("relative\\map.osu"));
        Assert.Null(reader.Read(Path.Combine(_sandbox, "missing.osu")));
        Assert.Null(reader.Read(notOsu));
        Assert.Null(reader.Read(unsafeAudio)!.AudioFilename);
    }

    // ------------------------------------------------------------ publication

    [Fact]
    public void Bridge_tick_never_throws_into_beatinsight()
    {
        using var bridge = new CommunityOverlayLiveBridge(
            () => throw new InvalidOperationException("capture"), new CommunityOverlayPipeServer("BeatInsight.Test." + Guid.NewGuid().ToString("N")));

        bridge.Tick();

        Assert.Equal(0, bridge.Published);
    }

    [Fact]
    public void Bridge_removes_out_of_contract_live_blocks_instead_of_sending_them()
    {
        string path = @"C:\Games\osu!\Songs\1 A - B\A - B (M) [X].osu";
        var game = Game(path, T0);
        var overlay = GameplayOverlay(game, Analysis(), T0) with
        {
            Performance = new LivePerformanceSnapshot(double.PositiveInfinity, 1, 1, 1, 56, T0),
        };
        using var server = new CommunityOverlayPipeServer("BeatInsight.Test." + Guid.NewGuid().ToString("N"));
        using var bridge = new CommunityOverlayLiveBridge(() => new(overlay, game, false, false, false), server, clock: () => T0);

        bridge.Tick();

        Assert.Equal(1, bridge.RejectedFrames);
        Assert.Equal(1, bridge.Published);
    }

    [Fact]
    public async Task Bridge_publishes_from_the_thread_pool_to_a_connected_reader()
    {
        string name = "BeatInsight.Test." + Guid.NewGuid().ToString("N");
        string osu = WriteOsu("A - B (M) [X].osu", OsuContent);
        var game = Game(osu, DateTimeOffset.UtcNow);
        bool? captureOnPool = null;
        int uiThread = Environment.CurrentManagedThreadId;
        using var bridge = new CommunityOverlayLiveBridge(
            () =>
            {
                captureOnPool ??= Environment.CurrentManagedThreadId != uiThread && Thread.CurrentThread.IsThreadPoolThread;
                return new CommunityOverlaySourceFrame(null, game, false, false, false);
            },
            new CommunityOverlayPipeServer(name));
        Assert.True(bridge.Start());

        using var reader = new NamedPipeClientStream(".", name, PipeDirection.In, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await reader.ConnectAsync(5_000);
        byte[]? frame = await CommunityOverlayPipeProtocol.ReadFrameAsync(reader, new CancellationTokenSource(5_000).Token);

        var envelope = CommunityOverlayPipeProtocol.TryDeserialize(frame, out string? rejection);
        Assert.Null(rejection);
        Assert.Equal(osu, envelope!.Snapshot.Game!.CurrentBeatmapPath);
        Assert.Equal("audio.mp3", envelope.Snapshot.OsuFile!.AudioFilename);
        Assert.True(captureOnPool);
    }

    [Fact]
    public void Pipe_connection_is_disabled_by_default()
    {
        Assert.False(MainWindow.IsCommunityOverlaysPipeEnabled(["BeatInsight.exe"], null));
        Assert.False(MainWindow.IsCommunityOverlaysPipeEnabled(["BeatInsight.exe"], "0"));
        Assert.True(MainWindow.IsCommunityOverlaysPipeEnabled(["BeatInsight.exe", "--community-overlays-pipe"], null));
        Assert.True(MainWindow.IsCommunityOverlaysPipeEnabled(["BeatInsight.exe"], "1"));
    }
}
