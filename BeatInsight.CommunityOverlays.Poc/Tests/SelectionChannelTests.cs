using System.Text.Json.Nodes;
using BeatInsight.CommunityOverlays.Poc.Adapter;
using BeatInsight.CommunityOverlays.Poc.Adapter.Live;
using BeatInsight.CommunityOverlays.Poc.Bridge;
using BeatInsight.Integration.CommunityOverlays.Pipe;
using Xunit;
using static BeatInsight.CommunityOverlays.Poc.Tests.LiveAdapterFixtures;

namespace BeatInsight.CommunityOverlays.Poc.Tests;

/// <summary>V3.14-COM-PP-03 : PP SS de la sélection (pipe v3) côté compagnon.</summary>
public class SelectionChannelTests
{
    private static DateTimeOffset At(double seconds) => T0.AddSeconds(seconds);

    private static SelectionPipeEnvelope Envelope(
        DateTimeOffset at, long seq = 1, SelectionValidity validity = SelectionValidity.Valid, string process = Process,
        string path = MapA, double ss = 412.5, double pp95 = 301.25, string[]? mods = null, string session = "s1")
    {
        mods ??= ["HD", "DT"];
        bool valid = validity == SelectionValidity.Valid;
        return new SelectionPipeEnvelope(
            CommunityOverlaySelectionPipeProtocol.Name, CommunityOverlaySelectionPipeProtocol.Version, session, seq, at,
            SelectionGeneration: 7, validity, valid ? "ok" : "calcul en attente", process,
            new SelectionBeatmap(path, 1234, at.AddDays(-1)), CommunityOverlaySelectionPipeProtocol.KeyOf(mods), mods, 72, at,
            valid ? new SelectionPerformance(ss, pp95, "ppy/test", at) : null);
    }

    private static CommunityOverlaySnapshot SongSelect(DateTimeOffset at, string path = MapA, string id = "1001") =>
        LiveCommunityOverlayAdapter.Adapt(ScreenTick(at, SourceRoute.SongSelect, path, id), LiveAdapterState.Initial, at).Snapshot;

    [Fact]
    public void Valid_matching_selection_fills_simplistic_and_triangles_fields()
    {
        var snapshot = SongSelect(At(0));
        var applied = SongSelectPerformanceOverlay.Apply(snapshot, ScreenTick(At(0), SourceRoute.SongSelect),
            new SelectionReception(Envelope(At(0)), At(0)), At(0.2), out string status);

        Assert.Equal("publié", status);
        Assert.Equal(412.5, applied.Pp.ByAccuracy[100].Value);
        Assert.Equal(301.25, applied.Pp.ByAccuracy[95].Value);
        Assert.Equal(FieldProvenance.SelectionPerformance, applied.Pp.ByAccuracy[100].Provenance);
        Assert.False(applied.Pp.ByAccuracy.ContainsKey(98)); // aucune précision intermédiaire inventée

        JsonObject v1 = TosuPayloadBuilder.BuildV1(applied);
        JsonObject v2 = TosuPayloadBuilder.BuildV2(applied);
        Assert.Equal(412.5, v1["menu"]!["pp"]!["100"]!.GetValue<double>());          // Simplistic
        Assert.Equal(412.5, v2["performance"]!["accuracy"]!["100"]!.GetValue<double>()); // Triangles
        Assert.Equal(301.25, v1["menu"]!["pp"]!["95"]!.GetValue<double>());
        Assert.Equal(0, v1["menu"]!["pp"]!["98"]!.GetValue<double>());               // neutre
    }

    [Theory]
    [InlineData(SelectionValidity.Pending)]
    [InlineData(SelectionValidity.Unknown)]
    [InlineData(SelectionValidity.NotSongSelect)]
    [InlineData(SelectionValidity.Unsupported)]
    public void Non_valid_selection_stays_neutral(SelectionValidity validity)
    {
        var snapshot = SongSelect(At(0));
        var applied = SongSelectPerformanceOverlay.Apply(snapshot, ScreenTick(At(0), SourceRoute.SongSelect),
            new SelectionReception(Envelope(At(0), validity: validity), At(0)), At(0.1), out _);
        Assert.Same(snapshot, applied);
        Assert.Equal(0, TosuPayloadBuilder.BuildV1(applied)["menu"]!["pp"]!["100"]!.GetValue<double>());
    }

    [Fact]
    public void Other_beatmap_other_process_or_stale_message_is_never_shown()
    {
        var snapshot = SongSelect(At(0));
        var input = ScreenTick(At(0), SourceRoute.SongSelect);

        // La sélection a changé de beatmap côté gameplay pipe : le résultat de l'ancienne n'est jamais affiché.
        Assert.Same(snapshot, SongSelectPerformanceOverlay.Apply(snapshot, input,
            new SelectionReception(Envelope(At(0), path: MapB), At(0)), At(0.1), out string s1));
        Assert.Equal("beatmap différente", s1);
        Assert.Same(snapshot, SongSelectPerformanceOverlay.Apply(snapshot, input,
            new SelectionReception(Envelope(At(0), process: OtherProcess), At(0)), At(0.1), out string s2));
        Assert.Equal("processus différent", s2);
        Assert.Same(snapshot, SongSelectPerformanceOverlay.Apply(snapshot, input,
            new SelectionReception(Envelope(At(0)), At(0)), At(1.5), out string s3));
        Assert.Equal("message périmé", s3);
        Assert.Same(snapshot, SongSelectPerformanceOverlay.Apply(snapshot, input, null, At(0.1), out _));
    }

    [Fact]
    public void Gameplay_pp_is_untouched_outside_song_select()
    {
        var gameplay = LiveCommunityOverlayAdapter.Adapt(ScreenTick(At(0), SourceRoute.Gameplay), LiveAdapterState.Initial, At(0)).Snapshot;
        var applied = SongSelectPerformanceOverlay.Apply(gameplay, ScreenTick(At(0), SourceRoute.Gameplay),
            new SelectionReception(Envelope(At(0)), At(0)), At(0.1), out string status);
        Assert.Same(gameplay, applied);
        Assert.Equal("autre écran", status);
    }

    [Fact]
    public void Path_comparison_ignores_case_and_separators_like_the_adapter()
    {
        var snapshot = SongSelect(At(0));
        var applied = SongSelectPerformanceOverlay.Apply(snapshot, ScreenTick(At(0), SourceRoute.SongSelect),
            new SelectionReception(Envelope(At(0), path: MapA.ToLowerInvariant().Replace('\\', '/')), At(0)), At(0.1), out _);
        Assert.True(applied.Pp.ByAccuracy.ContainsKey(100));
    }

    [Fact]
    public void Client_accepts_in_order_and_rejects_replays_stale_or_other_versions()
    {
        using var client = new SelectionPipeClient("unused-selection-test");
        byte[] Frame(SelectionPipeEnvelope e) => CommunityOverlaySelectionPipeProtocol.Serialize(e);

        Assert.True(client.Accept(Frame(Envelope(At(0), seq: 1)), At(0.1), out _, out _));
        Assert.False(client.Accept(Frame(Envelope(At(0), seq: 1)), At(0.1), out _, out _));                  // rejoué
        Assert.False(client.Accept(Frame(Envelope(At(0), seq: 2, session: "other")), At(0.1), out _, out _)); // autre producteur
        Assert.False(client.Accept(Frame(Envelope(At(0), seq: 3)), At(2), out _, out _));                     // périmé
        Assert.Equal(1, client.Stale);

        byte[] v2 = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(Envelope(At(0), seq: 4) with { Version = 2 },
            CommunityOverlaySelectionPipeProtocol.JsonOptions);
        Assert.False(client.Accept(v2, At(0.1), out _, out bool mismatch));
        Assert.True(mismatch);
    }

    [Fact]
    public void Contract_is_strict()
    {
        var ok = Envelope(At(0));
        Assert.Null(CommunityOverlaySelectionPipeProtocol.Validate(ok));
        Assert.NotNull(CommunityOverlaySelectionPipeProtocol.Validate(ok with { Performance = null }));
        Assert.NotNull(CommunityOverlaySelectionPipeProtocol.Validate(Envelope(At(0), validity: SelectionValidity.Pending) with
        {
            Performance = new SelectionPerformance(1, 1, "v", At(0)),
        }));
        Assert.NotNull(CommunityOverlaySelectionPipeProtocol.Validate(ok with { ModsKey = "DT+HD+HR" }));          // incohérente
        Assert.NotNull(CommunityOverlaySelectionPipeProtocol.Validate(ok with { Mods = ["HD", "?0x8000"] }));
        Assert.NotNull(CommunityOverlaySelectionPipeProtocol.Validate(ok with { Performance = ok.Performance! with { SsPp = double.NaN } }));
        Assert.NotNull(CommunityOverlaySelectionPipeProtocol.Validate(ok with { Performance = ok.Performance! with { SsPp = -1 } }));
        Assert.NotNull(CommunityOverlaySelectionPipeProtocol.Validate(ok with { Beatmap = null }));
        Assert.NotNull(CommunityOverlaySelectionPipeProtocol.Validate(ok with { Sequence = 0 }));

        string json = System.Text.Encoding.UTF8.GetString(CommunityOverlaySelectionPipeProtocol.Serialize(ok));
        byte[] extra = System.Text.Encoding.UTF8.GetBytes(json.Insert(1, "\"unexpected\":1,"));
        Assert.Null(CommunityOverlaySelectionPipeProtocol.TryDeserialize(extra, out string? rejection));
        Assert.Equal("json", rejection);
        Assert.NotNull(CommunityOverlaySelectionPipeProtocol.TryDeserialize(CommunityOverlaySelectionPipeProtocol.Serialize(ok), out _));
    }

    [Fact]
    public void Selection_pipe_is_separate_and_existing_contracts_are_unchanged()
    {
        Assert.Equal("BeatInsight.CommunityOverlays.selection.v3", CommunityOverlaySelectionPipeProtocol.PipeName);
        Assert.Equal(3, CommunityOverlaySelectionPipeProtocol.Version);
        Assert.NotEqual(CommunityOverlayPipeProtocol.PipeName, CommunityOverlaySelectionPipeProtocol.PipeName);
        Assert.NotEqual(CommunityOverlayPipeProtocol.LegacyPipeNameV1, CommunityOverlaySelectionPipeProtocol.PipeName);
        Assert.NotEqual(CommunityOverlayPrecisePipeProtocol.PipeName, CommunityOverlaySelectionPipeProtocol.PipeName);
        Assert.Equal(2, CommunityOverlayPipeProtocol.Version);
        Assert.Equal(1, CommunityOverlayPipeProtocol.MinimumVersion);
    }

    [Fact]
    public void Option_is_opt_in()
    {
        Assert.False(PocOptions.Parse([]).SelectionPipe);
        Assert.True(PocOptions.Parse(["--selection-pipe"]).SelectionPipe);
    }
}
