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
/// V3.14-COM-11 : durée audio réelle jusqu'aux champs tosu (<c>menu.bm.time.mp3</c>,
/// <c>beatmap.time.mp3Length</c>), et compatibilité entre versions du pipe.
/// </summary>
public class AudioDurationCompatibilityTests
{
    private const string MapA = @"C:\Games\osu!\Songs\100 Artist - Song\Artist - Song (Mapper) [Hard].osu";
    private const string MapB = @"C:\Games\osu!\Songs\200 Other - Tune\Other - Tune (Mapper) [Insane].osu";

    private static string UniqueName() => $"BeatInsight.CommunityOverlays.Test.{Guid.NewGuid():N}";

    private static PipeAudioFile Audio(string path = MapA, string file = "audio.mp3", PipeAudioStatus status = PipeAudioStatus.Known,
        double? duration = 95_250) =>
        new(path, file, status, PipeAudioFormat.Mp3, status == PipeAudioStatus.Known ? duration : null, 3_000_000, DateTimeOffset.UtcNow);

    private static PipeEnvelope WithAudio(DateTimeOffset at, PipeAudioFile? audio, string path = MapA, PipeRoute route = PipeRoute.Gameplay)
    {
        var e = PipeTransportTests.Envelope(1, at, path, route: route);
        return e with { Snapshot = e.Snapshot with { Audio = audio } };
    }

    private static (System.Text.Json.Nodes.JsonObject V1, System.Text.Json.Nodes.JsonObject V2, LiveAdapterResult R) Emit(PipeEnvelope e, DateTimeOffset now)
    {
        var r = LiveCommunityOverlayAdapter.Adapt(PipeLiveCommunityOverlayDataSource.ToInput(e.Snapshot), LiveAdapterState.Initial, now);
        return (TosuPayloadBuilder.BuildV1(r.Snapshot), TosuPayloadBuilder.BuildV2(r.Snapshot), r);
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

    // ------------------------------------------------------------ champs tosu

    [Fact]
    public void Known_audio_duration_reaches_both_tosu_formats()
    {
        var now = DateTimeOffset.UtcNow;
        var (v1, v2, r) = Emit(WithAudio(now, Audio()), now);

        Assert.Equal(95_250, v1["menu"]!["bm"]!["time"]!["mp3"]!.GetValue<double>());
        Assert.Equal(95_250, v2["beatmap"]!["time"]!["mp3Length"]!.GetValue<double>());
        Assert.Equal(FieldState.Known, r.Diagnostics.Fields["beatmap.audioLength"]);
        Assert.Equal(FieldProvenance.AudioFile, r.Snapshot.Beatmap!.AudioLengthMs.Provenance);
    }

    [Fact]
    public void Simplistic_progress_formula_gives_a_real_percentage()
    {
        // index.js de Simplistic : (menu.bm.time.current / menu.bm.time.mp3 * 100).toFixed(2)
        var now = DateTimeOffset.UtcNow;
        var (v1, _, _) = Emit(WithAudio(now, Audio(duration: 20_000)), now);
        double current = v1["menu"]!["bm"]!["time"]!["current"]!.GetValue<double>();
        double mp3 = v1["menu"]!["bm"]!["time"]!["mp3"]!.GetValue<double>();

        Assert.Equal(5_000, current);
        Assert.Equal("25.00", (current / mp3 * 100).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Audio_duration_is_known_in_song_select_and_results_too()
    {
        var now = DateTimeOffset.UtcNow;
        var (songSelect, _, _) = Emit(WithAudio(now, Audio(), route: PipeRoute.SongSelect), now);

        Assert.Equal(95_250, songSelect["menu"]!["bm"]!["time"]!["mp3"]!.GetValue<double>());
    }

    [Fact]
    public void Speed_mods_do_not_change_the_audio_duration()
    {
        // tosu : mp3Length = longueur de la piste, temps de jeu dans le même repère, aucune division par le rate.
        var now = DateTimeOffset.UtcNow;
        var e = WithAudio(now, Audio());
        e = e with { Snapshot = e.Snapshot with { Stats = e.Snapshot.Stats! with { Mods = ["DT", "HD"] } } };

        var (v1, v2, _) = Emit(e, now);

        Assert.Equal(95_250, v1["menu"]!["bm"]!["time"]!["mp3"]!.GetValue<double>());
        Assert.Equal(95_250, v2["beatmap"]!["time"]!["mp3Length"]!.GetValue<double>());
        Assert.Equal(1.5, v2["play"]!["mods"]!["rate"]!.GetValue<double>());
    }

    [Theory]
    [InlineData(PipeAudioStatus.Pending)]
    [InlineData(PipeAudioStatus.Missing)]
    [InlineData(PipeAudioStatus.Unreadable)]
    [InlineData(PipeAudioStatus.Invalid)]
    [InlineData(PipeAudioStatus.Unsupported)]
    public void Unknown_audio_duration_stays_neutral(PipeAudioStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        var (v1, v2, r) = Emit(WithAudio(now, Audio(status: status)), now);

        Assert.Equal(0, v1["menu"]!["bm"]!["time"]!["mp3"]!.GetValue<double>());
        Assert.Equal(0, v2["beatmap"]!["time"]!["mp3Length"]!.GetValue<double>());
        Assert.Equal(FieldState.Unknown, r.Diagnostics.Fields["beatmap.audioLength"]);
    }

    [Fact]
    public void Audio_of_another_beatmap_or_another_file_is_ignored()
    {
        var now = DateTimeOffset.UtcNow;

        var (otherMap, _, _) = Emit(WithAudio(now, Audio(path: MapB)), now);
        var (otherFile, _, _) = Emit(WithAudio(now, Audio(file: "other.ogg")), now);

        Assert.Equal(0, otherMap["menu"]!["bm"]!["time"]!["mp3"]!.GetValue<double>());
        Assert.Equal(0, otherFile["menu"]!["bm"]!["time"]!["mp3"]!.GetValue<double>());
    }

    [Fact]
    public void Beatmap_change_never_keeps_the_previous_duration()
    {
        var now = DateTimeOffset.UtcNow;
        var state = LiveAdapterState.Initial;
        var a = LiveCommunityOverlayAdapter.Adapt(PipeLiveCommunityOverlayDataSource.ToInput(WithAudio(now, Audio()).Snapshot), state, now);
        // Nouvelle beatmap, durée encore en cours de lecture côté BeatInsight.
        var b = LiveCommunityOverlayAdapter.Adapt(PipeLiveCommunityOverlayDataSource.ToInput(
            WithAudio(now, Audio(path: MapB, status: PipeAudioStatus.Pending), MapB).Snapshot), a.State, now);

        Assert.Equal(95_250, TosuPayloadBuilder.BuildV1(a.Snapshot)["menu"]!["bm"]!["time"]!["mp3"]!.GetValue<double>());
        Assert.Equal(0, TosuPayloadBuilder.BuildV1(b.Snapshot)["menu"]!["bm"]!["time"]!["mp3"]!.GetValue<double>());
    }

    // ------------------------------------------------------------ compatibilité entre versions

    [Fact]
    public void Version1_message_from_beatinsight_com10_is_still_accepted()
    {
        // JSON exact d'un message COM-10 (aucun bloc audio).
        var e = PipeTransportTests.Envelope(1, DateTimeOffset.UtcNow, version: 1);
        byte[] json = CommunityOverlayPipeProtocol.Serialize(e);

        var back = CommunityOverlayPipeProtocol.TryDeserialize(json, out string? rejection);

        Assert.Null(rejection);
        Assert.Equal(1, back!.Version);
        Assert.Null(back.Snapshot.Audio);
        Assert.DoesNotContain("\"audio\"", Encoding.UTF8.GetString(json));
    }

    [Fact]
    public void Version1_message_carrying_audio_is_refused()
    {
        var e = WithAudio(DateTimeOffset.UtcNow, Audio()) with { Version = 1 };

        Assert.StartsWith("bloc audio", CommunityOverlayPipeProtocol.Validate(e));
    }

    [Fact]
    public void Audio_null_is_omitted_and_present_audio_round_trips()
    {
        var without = CommunityOverlayPipeProtocol.Serialize(WithAudio(DateTimeOffset.UtcNow, null));
        var with = CommunityOverlayPipeProtocol.Serialize(WithAudio(DateTimeOffset.UtcNow, Audio()));

        Assert.DoesNotContain("\"audio\"", Encoding.UTF8.GetString(without));
        var back = CommunityOverlayPipeProtocol.TryDeserialize(with, out string? rejection);
        Assert.Null(rejection);
        Assert.Equal(95_250, back!.Snapshot.Audio!.DurationMs);
        Assert.Contains("\"status\":\"Known\"", Encoding.UTF8.GetString(with));
    }

    [Fact]
    public async Task Companion_falls_back_to_the_v1_pipe_when_v2_does_not_exist()
    {
        string v2 = UniqueName(), v1 = UniqueName();
        using var legacy = new CommunityOverlayPipeServer(v1); // BeatInsight COM-10 : pipe v1 seulement
        Assert.True(legacy.Start());
        using var client = new CommunityOverlayPipeClient(v2, fallbackPipeName: v1);
        client.Start();

        legacy.Publish(CommunityOverlayPipeProtocol.Serialize(PipeTransportTests.Envelope(1, DateTimeOffset.UtcNow, version: 1)));

        Assert.True(await WaitUntil(() => client.Latest is not null));
        Assert.Equal(v1, client.ConnectedPipeName);
        Assert.Equal(PipeClientState.Connected, client.State);
        Assert.Equal(1, client.Latest!.Envelope.Version);
    }

    [Fact]
    public async Task Companion_prefers_the_v2_pipe_when_both_exist()
    {
        string v2 = UniqueName(), v1 = UniqueName();
        using var current = new CommunityOverlayPipeServer(v2);
        using var legacy = new CommunityOverlayPipeServer(v1);
        Assert.True(current.Start());
        Assert.True(legacy.Start());
        using var client = new CommunityOverlayPipeClient(v2, fallbackPipeName: v1);
        client.Start();

        current.Publish(CommunityOverlayPipeProtocol.Serialize(WithAudio(DateTimeOffset.UtcNow, Audio())));
        legacy.Publish(CommunityOverlayPipeProtocol.Serialize(PipeTransportTests.Envelope(1, DateTimeOffset.UtcNow, version: 1)));

        Assert.True(await WaitUntil(() => client.Latest is not null));
        Assert.Equal(v2, client.ConnectedPipeName);
        Assert.Equal(95_250, client.Latest!.Envelope.Snapshot.Audio!.DurationMs);
    }

    [Fact]
    public async Task Companion_without_fallback_never_connects_elsewhere()
    {
        string v2 = UniqueName(), v1 = UniqueName();
        using var legacy = new CommunityOverlayPipeServer(v1);
        Assert.True(legacy.Start());
        using var client = new CommunityOverlayPipeClient(v2);
        client.Start();
        legacy.Publish(CommunityOverlayPipeProtocol.Serialize(PipeTransportTests.Envelope(1, DateTimeOffset.UtcNow, version: 1)));

        await Task.Delay(1_500);

        Assert.Null(client.Latest);
        Assert.Null(client.ConnectedPipeName);
        Assert.Equal(0, client.Connections);
    }

    [Fact]
    public void Default_live_source_uses_v2_with_v1_fallback()
    {
        Assert.Equal("BeatInsight.CommunityOverlays.v2", CommunityOverlayPipeProtocol.PipeName);
        Assert.Equal("BeatInsight.CommunityOverlays.v1", CommunityOverlayPipeProtocol.LegacyPipeNameV1);
        Assert.Equal(2, CommunityOverlayPipeProtocol.Version);
        Assert.Equal(1, CommunityOverlayPipeProtocol.MinimumVersion);
    }
}
