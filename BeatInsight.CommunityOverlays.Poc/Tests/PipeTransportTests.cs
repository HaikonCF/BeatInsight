using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using BeatInsight.CommunityOverlays.Poc.Adapter;
using BeatInsight.CommunityOverlays.Poc.Adapter.Live;
using BeatInsight.CommunityOverlays.Poc.Bridge;
using BeatInsight.Integration.CommunityOverlays.Pipe;
using Xunit;

namespace BeatInsight.CommunityOverlays.Poc.Tests;

/// <summary>
/// V3.14-COM-10 : transport Named Pipe de bout en bout, sans osu! ni BeatInsight.
/// Serveur réel (fichier partagé avec BeatInsight) + client réel du compagnon,
/// avec un producteur synthétique et un nom de pipe unique par test.
/// </summary>
public class PipeTransportTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private const string Process = "osu!.exe:4242:1";
    private const string MapA = @"C:\Games\osu!\Songs\100 Artist - Song\Artist - Song (Mapper) [Hard].osu";
    private const string MapB = @"C:\Games\osu!\Songs\200 Other - Tune\Other - Tune (Mapper) [Insane].osu";

    private static string UniqueName() => $"BeatInsight.CommunityOverlays.Test.{Guid.NewGuid():N}";

    internal static PipeEnvelope Envelope(long sequence, DateTimeOffset at, string path = MapA, string md5 = "0123456789abcdef0123456789abcdef",
        PipeRoute route = PipeRoute.Gameplay, int c300 = 5, int version = CommunityOverlayPipeProtocol.Version)
    {
        var game = new PipeGameState("NativeStableGameProvider", "osu! stable", Process, path, path == MapA ? "1001" : "2002", "100",
            at, PipeAvailability.Available, @"C:\Games\osu!\Songs", Path.Combine(Path.GetDirectoryName(path)!, "bg.jpg"));
        var metadata = new PipeBeatmapMetadata(path, path == MapA ? 1001 : 2002, 100, "Artist", "Song", "Hard", "Mapper",
            TimeSpan.FromSeconds(30).Ticks, 180, 14, 9, 8, 4, 5, 5, 3, 2, 5.25, 180, 160, 200, 14);
        var osu = new PipeOsuFile(path, 1234, at, md5, "audio.mp3", "ソング", "アーティスト");
        var stats = route == PipeRoute.Gameplay
            ? new PipeLiveStats(Process, path == MapA ? "1001" : "2002", "100", 5_000, c300, c300, c300, 0, 0, 0, 1.0, ["HD"], at, c300 * 300)
            : null;
        var perf = stats is null ? null : new PipePerformance(42, 100, 90, 120, c300, at);
        var live = route == PipeRoute.Gameplay
            ? new PipeLiveGameplay(true, 5_000, 30_000, path == MapA ? "1001" : "2002", "100", Process, at)
            : null;
        return new PipeEnvelope(CommunityOverlayPipeProtocol.Name, version, sequence, at,
            new PipeSnapshot(game, new PipeGameRoute(route, at, Process), metadata, osu, live, stats, perf, PipeSuspension.None));
    }

    private static async Task<bool> WaitUntil(Func<bool> condition, int timeoutMs = 5_000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return true;
            await Task.Delay(20);
        }
        return condition();
    }

    // ------------------------------------------------------------ protocole

    [Fact]
    public void Envelope_round_trips_and_validates()
    {
        var envelope = Envelope(1, T0);
        byte[] bytes = CommunityOverlayPipeProtocol.Serialize(envelope);

        var back = CommunityOverlayPipeProtocol.TryDeserialize(bytes, out string? rejection);

        Assert.Null(rejection);
        Assert.Equal(envelope.Snapshot.Stats!.Count300, back!.Snapshot.Stats!.Count300);
        Assert.Equal(envelope.Snapshot.OsuFile!.Md5, back.Snapshot.OsuFile!.Md5);
        Assert.Equal(T0, back.Snapshot.Game!.LastUpdateUtc);
        Assert.True(bytes.Length < 4_096, $"{bytes.Length} octets");
        Assert.Contains("\"route\":\"Gameplay\"", Encoding.UTF8.GetString(bytes));
    }

    public static TheoryData<string, string> MalformedMessages() => new()
    {
        { "{", "JSON invalide" },
        { "[]", "JSON invalide" },
        { "null", "message vide" },
        { """{"protocol":"other","version":1,"sequence":1,"sentAtUtc":"2026-10-14T12:00:00Z","snapshot":{"game":null,"route":null,"metadata":null,"osuFile":null,"liveGameplay":null,"stats":null,"performance":null,"suspension":"None"}}""", "protocole inconnu" },
        { """{"protocol":"beatinsight.community-overlays","version":3,"sequence":1,"sentAtUtc":"2026-10-14T12:00:00Z","snapshot":{"game":null,"route":null,"metadata":null,"osuFile":null,"liveGameplay":null,"stats":null,"performance":null,"suspension":"None"}}""", "version 3" },
        { """{"protocol":"beatinsight.community-overlays","version":1,"sequence":1,"sentAtUtc":"2026-10-14T12:00:00Z","snapshot":{"game":null,"route":null,"metadata":null,"osuFile":null,"liveGameplay":null,"stats":null,"performance":null,"suspension":"None"},"extra":1}""", "JSON invalide" },
        { """{"protocol":"beatinsight.community-overlays","version":1,"sequence":1,"sentAtUtc":"2026-10-14T12:00:00Z","snapshot":{"game":null,"route":null,"metadata":null,"osuFile":null,"liveGameplay":null,"stats":null,"performance":null,"suspension":"Hacked"}}""", "JSON invalide" },
    };

    [Theory]
    [MemberData(nameof(MalformedMessages))]
    public void Malformed_messages_are_rejected_without_throwing(string json, string expected)
    {
        var envelope = CommunityOverlayPipeProtocol.TryDeserialize(Encoding.UTF8.GetBytes(json), out string? rejection);

        Assert.Null(envelope);
        Assert.StartsWith(expected, rejection);
    }

    [Fact]
    public void Out_of_contract_values_are_rejected()
    {
        var e = Envelope(1, T0);
        PipeEnvelope With(PipeSnapshot s) => e with { Snapshot = s };

        Assert.NotNull(CommunityOverlayPipeProtocol.Validate(With(e.Snapshot with { Stats = e.Snapshot.Stats! with { Count300 = -1 } })));
        Assert.NotNull(CommunityOverlayPipeProtocol.Validate(With(e.Snapshot with { Stats = e.Snapshot.Stats! with { CurrentCombo = 99, MaxComboReached = 5 } })));
        Assert.NotNull(CommunityOverlayPipeProtocol.Validate(With(e.Snapshot with { Stats = e.Snapshot.Stats! with { Accuracy = 1.5 } })));
        Assert.NotNull(CommunityOverlayPipeProtocol.Validate(With(e.Snapshot with { Stats = e.Snapshot.Stats! with { Mods = ["HD; rm -rf"] } })));
        Assert.NotNull(CommunityOverlayPipeProtocol.Validate(With(e.Snapshot with { Performance = e.Snapshot.Performance! with { CurrentPp = double.NaN } })));
        Assert.NotNull(CommunityOverlayPipeProtocol.Validate(With(e.Snapshot with { OsuFile = e.Snapshot.OsuFile! with { Md5 = "xyz" } })));
        Assert.NotNull(CommunityOverlayPipeProtocol.Validate(With(e.Snapshot with { OsuFile = e.Snapshot.OsuFile! with { AudioFilename = @"..\..\secret.mp3" } })));
        Assert.NotNull(CommunityOverlayPipeProtocol.Validate(With(e.Snapshot with { Game = e.Snapshot.Game! with { ProviderName = "" } })));
        Assert.NotNull(CommunityOverlayPipeProtocol.Validate(e with { Sequence = -1 }));
        Assert.Null(CommunityOverlayPipeProtocol.Validate(e));
    }

    [Fact]
    public async Task Frames_with_invalid_length_or_truncated_body_are_rejected()
    {
        using var negative = new MemoryStream([0xFF, 0xFF, 0xFF, 0xFF]);
        using var tooBig = new MemoryStream(BitConverter.GetBytes(CommunityOverlayPipeProtocol.MaxMessageBytes + 1));
        using var truncated = new MemoryStream([10, 0, 0, 0, (byte)'{']);
        using var empty = new MemoryStream();

        await Assert.ThrowsAsync<InvalidDataException>(() => CommunityOverlayPipeProtocol.ReadFrameAsync(negative, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => CommunityOverlayPipeProtocol.ReadFrameAsync(tooBig, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => CommunityOverlayPipeProtocol.ReadFrameAsync(truncated, default));
        Assert.Null(await CommunityOverlayPipeProtocol.ReadFrameAsync(empty, default));
    }

    [Fact]
    public void Pipe_enums_match_adapter_enums()
    {
        Assert.Equal(Enum.GetNames<PipeRoute>(), Enum.GetNames<SourceRoute>());
        Assert.Equal(Enum.GetNames<PipeAvailability>(), Enum.GetNames<SourceAvailability>());
        Assert.Equal(Enum.GetNames<PipeSuspension>(), Enum.GetNames<SourceSuspension>());
    }

    // ------------------------------------------------------------ transport réel

    [Fact]
    public async Task Synthetic_producer_reaches_the_companion()
    {
        string name = UniqueName();
        using var server = new CommunityOverlayPipeServer(name);
        Assert.True(server.Start());
        using var client = new CommunityOverlayPipeClient(name);
        client.Start();

        Assert.True(await WaitUntil(() => client.State == PipeClientState.Connected));
        server.Publish(CommunityOverlayPipeProtocol.Serialize(Envelope(1, DateTimeOffset.UtcNow)));

        Assert.True(await WaitUntil(() => client.Latest is not null));
        Assert.Equal(5, client.Latest!.Envelope.Snapshot.Stats!.Count300);
        Assert.Equal(1, server.ConnectedClients);
    }

    [Fact]
    public async Task Latest_message_wins_without_backlog()
    {
        string name = UniqueName();
        using var server = new CommunityOverlayPipeServer(name);
        server.Start();
        using var client = new CommunityOverlayPipeClient(name);
        client.Start();
        Assert.True(await WaitUntil(() => client.State == PipeClientState.Connected && server.ConnectedClients == 1));

        for (int i = 1; i <= 500; i++)
            server.Publish(CommunityOverlayPipeProtocol.Serialize(Envelope(i, DateTimeOffset.UtcNow, c300: i % 10)));

        Assert.True(await WaitUntil(() => client.Latest?.Envelope.Sequence == 500));
        Assert.True(client.Received < 500, $"reçus {client.Received}"); // les messages intermédiaires sont remplacés
    }

    [Fact]
    public void Publish_never_blocks_even_without_reader()
    {
        string name = UniqueName();
        using var server = new CommunityOverlayPipeServer(name);
        server.Start();
        // Un client connecté qui ne lit jamais : le tampon du pipe se remplit côté serveur.
        using var stuck = new NamedPipeClientStream(".", name, PipeDirection.In, PipeOptions.CurrentUserOnly);
        stuck.Connect(2_000);
        byte[] big = CommunityOverlayPipeProtocol.Serialize(Envelope(1, DateTimeOffset.UtcNow) with
        {
            Snapshot = Envelope(1, DateTimeOffset.UtcNow).Snapshot with
            {
                Metadata = Envelope(1, DateTimeOffset.UtcNow).Snapshot.Metadata! with { Title = new string('x', 1_000) },
            },
        });

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 2_000; i++)
            server.Publish(big);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 500, $"{sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Producer_shutdown_clears_data_and_restart_reconnects()
    {
        string name = UniqueName();
        using var client = new CommunityOverlayPipeClient(name);
        client.Start();

        var first = new CommunityOverlayPipeServer(name);
        first.Start();
        Assert.True(await WaitUntil(() => client.State == PipeClientState.Connected));
        first.Publish(CommunityOverlayPipeProtocol.Serialize(Envelope(1, DateTimeOffset.UtcNow)));
        Assert.True(await WaitUntil(() => client.Latest is not null));

        // BeatInsight fermé.
        first.Dispose();
        Assert.True(await WaitUntil(() => client.Latest is null && client.State != PipeClientState.Connected));

        // BeatInsight relancé : la séquence repart de 1 et doit être acceptée.
        using var second = new CommunityOverlayPipeServer(name);
        Assert.True(await WaitUntil(() => second.Start()));
        Assert.True(await WaitUntil(() => client.State == PipeClientState.Connected && second.ConnectedClients == 1));
        second.Publish(CommunityOverlayPipeProtocol.Serialize(Envelope(1, DateTimeOffset.UtcNow, c300: 7)));

        Assert.True(await WaitUntil(() => client.Latest?.Envelope.Snapshot.Stats?.Count300 == 7));
        Assert.True(client.Connections >= 2);
    }

    [Fact]
    public async Task Second_server_cannot_take_an_existing_pipe_name()
    {
        string name = UniqueName();
        using var owner = new CommunityOverlayPipeServer(name);
        Assert.True(owner.Start());

        using var squatter = new CommunityOverlayPipeServer(name);
        Assert.False(squatter.Start());
        await Task.CompletedTask;
    }

    /// <summary>Faux producteur : écrit des trames brutes pour tester la robustesse du client.</summary>
    private static async Task<NamedPipeServerStream> RawServer(string name)
    {
        var raw = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await raw.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(5));
        return raw;
    }

    [Fact]
    public async Task Malformed_json_is_skipped_and_the_next_valid_message_is_used()
    {
        string name = UniqueName();
        using var client = new CommunityOverlayPipeClient(name);
        var serverTask = RawServer(name);
        client.Start();
        using var raw = await serverTask;

        await CommunityOverlayPipeProtocol.WriteFrameAsync(raw, Encoding.UTF8.GetBytes("{not json"), default);
        await CommunityOverlayPipeProtocol.WriteFrameAsync(raw, CommunityOverlayPipeProtocol.Serialize(Envelope(3, DateTimeOffset.UtcNow)), default);

        Assert.True(await WaitUntil(() => client.Latest?.Envelope.Sequence == 3));
        Assert.Equal(1, client.Rejected);
    }

    [Fact]
    public async Task Broken_framing_disconnects_and_clears_data()
    {
        string name = UniqueName();
        using var client = new CommunityOverlayPipeClient(name);
        var serverTask = RawServer(name);
        client.Start();
        using var raw = await serverTask;
        await CommunityOverlayPipeProtocol.WriteFrameAsync(raw, CommunityOverlayPipeProtocol.Serialize(Envelope(1, DateTimeOffset.UtcNow)), default);
        Assert.True(await WaitUntil(() => client.Latest is not null));

        await raw.WriteAsync(new byte[] { 0xFF, 0xFF, 0xFF, 0x7F });
        await raw.FlushAsync();

        Assert.True(await WaitUntil(() => client.Latest is null));
        Assert.True(client.Rejected >= 1);
    }

    [Fact]
    public async Task Other_protocol_version_is_refused()
    {
        string name = UniqueName();
        using var client = new CommunityOverlayPipeClient(name);
        var serverTask = RawServer(name);
        client.Start();
        using var raw = await serverTask;

        await CommunityOverlayPipeProtocol.WriteFrameAsync(raw,
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(Envelope(1, DateTimeOffset.UtcNow, version: CommunityOverlayPipeProtocol.Version + 1), CommunityOverlayPipeProtocol.JsonOptions), default);

        Assert.True(await WaitUntil(() => client.LastRejection?.StartsWith("version") == true));
        Assert.Null(client.Latest);
    }

    [Fact]
    public async Task Sequence_going_backwards_on_one_connection_is_ignored()
    {
        string name = UniqueName();
        using var client = new CommunityOverlayPipeClient(name);
        var serverTask = RawServer(name);
        client.Start();
        using var raw = await serverTask;

        await CommunityOverlayPipeProtocol.WriteFrameAsync(raw, CommunityOverlayPipeProtocol.Serialize(Envelope(5, DateTimeOffset.UtcNow, c300: 5)), default);
        await CommunityOverlayPipeProtocol.WriteFrameAsync(raw, CommunityOverlayPipeProtocol.Serialize(Envelope(4, DateTimeOffset.UtcNow, c300: 9)), default);
        await Task.Delay(200);

        Assert.Equal(5, client.Latest!.Envelope.Sequence);
        Assert.Equal(1, client.Rejected);
    }

    // ------------------------------------------------------------ compagnon

    [Fact]
    public void Silent_producer_becomes_unknown_after_receive_timeout()
    {
        var now = DateTimeOffset.UtcNow;
        var reception = new PipeReception(Envelope(1, now), now);

        var fresh = PipeLiveCommunityOverlayDataSource.Resolve(reception, now + TimeSpan.FromSeconds(1));
        var silent = PipeLiveCommunityOverlayDataSource.Resolve(reception, now + TimeSpan.FromSeconds(1.6));
        var none = PipeLiveCommunityOverlayDataSource.Resolve(null, now);

        Assert.NotNull(fresh.Game);
        Assert.Null(silent.Game);
        Assert.Null(none.Game);
        var r = LiveCommunityOverlayAdapter.Adapt(silent, LiveAdapterState.Initial, now);
        Assert.Equal(CommunityOverlaySourceKind.Live, r.Snapshot.Source);
        Assert.Equal(CommunityOverlayRoute.Unknown, r.Snapshot.Route);
        Assert.True(r.Diagnostics.Events.Contains(LiveAdapterEvent.GameUnavailable));
    }

    [Fact]
    public void Pipe_snapshot_reaches_tosu_json_with_real_checksum_and_audio()
    {
        var now = DateTimeOffset.UtcNow;
        var input = PipeLiveCommunityOverlayDataSource.ToInput(Envelope(1, now).Snapshot);
        var r = LiveCommunityOverlayAdapter.Adapt(input, LiveAdapterState.Initial, now);
        var v2 = TosuPayloadBuilder.BuildV2(r.Snapshot);
        var v1 = TosuPayloadBuilder.BuildV1(r.Snapshot);

        Assert.Equal("0123456789abcdef0123456789abcdef", v2["beatmap"]!["checksum"]!.GetValue<string>());
        Assert.Equal("0123456789abcdef0123456789abcdef", v1["menu"]!["bm"]!["md5"]!.GetValue<string>());
        Assert.Equal("audio.mp3", v2["files"]!["audio"]!.GetValue<string>());
        Assert.Equal(@"100 Artist - Song\audio.mp3", v2["directPath"]!["beatmapAudio"]!.GetValue<string>());
        Assert.Equal("ソング", v2["beatmap"]!["titleUnicode"]!.GetValue<string>());
        Assert.Equal(5, v1["gameplay"]!["hits"]!["300"]!.GetValue<int>());
        Assert.Equal(42, v1["gameplay"]!["pp"]!["current"]!.GetValue<double>());
        Assert.Equal(0, v1["menu"]!["bm"]!["time"]!["mp3"]!.GetValue<double>()); // durée audio jamais inventée
        Assert.DoesNotContain(@"C:\", v2.ToJsonString());
        Assert.DoesNotContain(@"C:\\", v2.ToJsonString());
    }

    [Fact]
    public void Beatmap_change_changes_the_checksum_seen_by_obs_background()
    {
        var now = DateTimeOffset.UtcNow;
        var state = LiveAdapterState.Initial;
        var a = LiveCommunityOverlayAdapter.Adapt(PipeLiveCommunityOverlayDataSource.ToInput(
            Envelope(1, now, MapA, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", PipeRoute.SongSelect).Snapshot), state, now);
        var b = LiveCommunityOverlayAdapter.Adapt(PipeLiveCommunityOverlayDataSource.ToInput(
            Envelope(2, now, MapB, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", PipeRoute.SongSelect).Snapshot), a.State, now);

        Assert.True(b.Diagnostics.Events.Contains(LiveAdapterEvent.BeatmapChanged));
        Assert.NotEqual(
            TosuPayloadBuilder.BuildV2(a.Snapshot)["beatmap"]!["checksum"]!.GetValue<string>(),
            TosuPayloadBuilder.BuildV2(b.Snapshot)["beatmap"]!["checksum"]!.GetValue<string>());
        Assert.Equal("200 Other - Tune", b.Snapshot.Beatmap!.Files!.Folder);
    }

    [Fact]
    public void Osu_file_of_another_beatmap_is_ignored()
    {
        var now = DateTimeOffset.UtcNow;
        var envelope = Envelope(1, now, MapB);
        var snapshot = envelope.Snapshot with { OsuFile = Envelope(1, now, MapA).Snapshot.OsuFile };

        var r = LiveCommunityOverlayAdapter.Adapt(PipeLiveCommunityOverlayDataSource.ToInput(snapshot), LiveAdapterState.Initial, now);

        Assert.Equal(FieldState.Unknown, r.Snapshot.Beatmap!.Checksum.State);
        Assert.Null(r.Snapshot.Beatmap.Files!.Audio);
    }

    [Fact]
    public void Live_asset_root_follows_the_announced_songs_folder()
    {
        string? root = null;
        var provider = new BeatmapAssetProvider(() => root);
        var files = new CommunityOverlayBeatmapFiles("900001 BeatInsight - Community Overlay POC", "x.osu", "bg.png", null);

        Assert.Equal(BeatmapAssetStatus.NotFound, provider.Resolve("/files/beatmap/background", files).Status);
        root = Path.Combine(AppContext.BaseDirectory, "TestAssets", "Songs");
        Assert.Equal(BeatmapAssetStatus.Found, provider.Resolve("/files/beatmap/background", files).Status);
        root = "relative\\Songs";
        Assert.Equal(BeatmapAssetStatus.NotFound, provider.Resolve("/files/beatmap/background", files).Status);
    }
}
