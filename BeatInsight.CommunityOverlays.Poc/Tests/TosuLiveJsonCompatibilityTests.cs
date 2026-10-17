using System.Text.Json.Nodes;
using BeatInsight.CommunityOverlays.Poc.Adapter;
using BeatInsight.CommunityOverlays.Poc.Adapter.Live;
using BeatInsight.CommunityOverlays.Poc.Bridge;
using Xunit;
using static BeatInsight.CommunityOverlays.Poc.Tests.LiveAdapterFixtures;

namespace BeatInsight.CommunityOverlays.Poc.Tests;

/// <summary>
/// V3.14-COM-09 : JSON produit depuis l'adaptateur live, sur les chemins réellement lus
/// par Simplistic (<c>/ws</c>, index.js) et Triangles Lazer PP (<c>/websocket/v2</c>, index.js),
/// ainsi que les invariants du contrat v2 et la compatibilité du mock.
/// </summary>
public class TosuLiveJsonCompatibilityTests
{
    private static DateTimeOffset At(double seconds) => T0.AddSeconds(seconds);

    /// <summary>Une séquence complète : chaque écran et chaque cas dégradé.</summary>
    public static TheoryData<string> Scenarios() =>
    [
        "song-select", "gameplay", "gameplay-no-stats", "results-complete", "results-incomplete", "lazer", "osu-closed", "suspended",
    ];

    private static CommunityOverlaySnapshot Scenario(string name)
    {
        var seq = new Sequence();
        return name switch
        {
            "song-select" => seq.Step(ScreenTick(At(0), SourceRoute.SongSelect), At(0)).Snapshot,
            "gameplay" => seq.Step(GameplayTick(At(0), 5_000, c300: 5, c100: 1, miss: 1, pp: 77, mods: ["HD"]), At(0)).Snapshot,
            "gameplay-no-stats" => seq.Step(GameplayTick(At(0), 5_000, c300: 5) with { Stats = null, Performance = null }, At(0)).Snapshot,
            "results-complete" => Then(seq, GameplayTick(At(0), 29_000, c300: TotalObjects, pp: 150), ScreenTick(At(0.5), SourceRoute.Results)),
            "results-incomplete" => Then(seq, GameplayTick(At(0), 20_000, c300: 6, pp: 70), ScreenTick(At(0.5), SourceRoute.Results)),
            "lazer" => seq.Step(new BeatInsightLiveInput(
                Game(At(0), provider: "LazerGameProvider", client: "osu!lazer"), Route(SourceRoute.Gameplay, At(0)),
                Metadata(), Live(At(0), 8_000)), At(0)).Snapshot,
            "osu-closed" => seq.Step(new BeatInsightLiveInput(Game(At(0), availability: SourceAvailability.NoProcess), null), At(0)).Snapshot,
            "suspended" => seq.Step(GameplayTick(At(0), 5_000, c300: 5) with { Suspension = SourceSuspension.OverlayLayoutEdit }, At(0)).Snapshot,
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };

        static CommunityOverlaySnapshot Then(Sequence seq, BeatInsightLiveInput first, BeatInsightLiveInput second)
        {
            seq.Step(first, At(0));
            return seq.Step(second, At(0.5)).Snapshot;
        }
    }

    private static JsonNode Path(JsonNode root, string path)
    {
        // Relu depuis le texte, comme l'overlay qui fait JSON.parse(event.data).
        JsonNode? node = JsonNode.Parse(root.ToJsonString());
        foreach (string part in path.Split('.'))
        {
            node = node?[part];
            Assert.True(node is not null, $"chemin manquant : {path}");
        }
        return node!;
    }

    /// <summary>Simplistic : chemins lus dans index.js, tous présents et du bon type, sur tous les écrans.</summary>
    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Simplistic_paths_are_present_and_typed(string scenario)
    {
        JsonObject v1 = TosuPayloadBuilder.BuildV1(Scenario(scenario));

        string[] numbers =
        [
            "menu.state", "menu.bm.time.current", "menu.bm.time.mp3", "menu.pp.100",
            "gameplay.pp.current", "gameplay.pp.fc", "gameplay.hits.0", "gameplay.hits.100", "gameplay.hits.50",
            "gameplay.hits.sliderBreaks",
        ];
        foreach (string path in numbers)
            Assert.True(double.IsFinite(Path(v1, path).GetValue<double>()), path);
        Assert.NotNull(Path(v1, "gameplay.hits.grade.current").GetValue<string>());
    }

    /// <summary>Triangles Lazer PP : state.name, play.pp.current, performance.accuracy["100"].</summary>
    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Triangles_paths_are_present_and_typed(string scenario)
    {
        JsonObject v2 = TosuPayloadBuilder.BuildV2(Scenario(scenario));

        Assert.Contains(Path(v2, "state.name").GetValue<string>(), new[] { "menu", "play", "selectPlay", "resultScreen", "unknown" });
        Assert.True(double.IsFinite(Path(v2, "play.pp.current").GetValue<double>()));
        Assert.True(double.IsFinite(Path(v2, "performance.accuracy.100").GetValue<double>()));
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Live_json_contains_no_invented_value(string scenario)
    {
        var s = Scenario(scenario);
        JsonObject v2 = TosuPayloadBuilder.BuildV2(s);
        JsonObject v1 = TosuPayloadBuilder.BuildV1(s);

        Assert.Equal(0, Path(v2, "play.healthBar.normal").GetValue<double>());      // ex-100 codé en dur
        Assert.Equal(0, Path(v1, "gameplay.hp.normal").GetValue<double>());         // ex-200 codé en dur
        Assert.Equal("", Path(v2, "play.rank.maxThisPlay").GetValue<string>());     // ex-copie de current
        Assert.Equal(0, Path(v2, "beatmap.stats.bpm.realtime").GetValue<double>()); // ex-copie de common
        Assert.Equal("", Path(v2, "play.playerName").GetValue<string>());
        Assert.Equal(0, Path(v1, "gameplay.hits.sliderBreaks").GetValue<int>());
        Assert.Equal(0, Path(v1, "gameplay.hits.geki").GetValue<int>());
        Assert.Equal(0, Path(v1, "gameplay.hits.katu").GetValue<int>());
        Assert.Equal(0, Path(v2, "performance.accuracy.98").GetValue<double>());
        foreach (string key in new[] { "k1", "k2", "m1", "m2" })
        {
            Assert.False(Path(TosuPayloadBuilder.BuildPrecise(s), $"keys.{key}.isPressed").GetValue<bool>());
            Assert.Equal(0, Path(TosuPayloadBuilder.BuildPrecise(s), $"keys.{key}.count").GetValue<int>());
        }
    }

    [Fact]
    public void Live_gameplay_json_carries_the_real_values()
    {
        var s = Scenario("gameplay");
        JsonObject v1 = TosuPayloadBuilder.BuildV1(s);
        JsonObject v2 = TosuPayloadBuilder.BuildV2(s);

        Assert.Equal(2, Path(v1, "menu.state").GetValue<int>());
        Assert.Equal(5_000, Path(v1, "menu.bm.time.current").GetValue<double>());
        Assert.Equal(77, Path(v1, "gameplay.pp.current").GetValue<double>());
        Assert.Equal(1, Path(v1, "gameplay.hits.0").GetValue<int>());
        Assert.Equal(1, Path(v1, "gameplay.hits.100").GetValue<int>());
        Assert.Equal(300, Path(v2, "performance.accuracy.100").GetValue<double>());
        Assert.Equal("HD", Path(v2, "play.mods.name").GetValue<string>());
        Assert.Equal("stable", Path(v2, "client").GetValue<string>());
        Assert.Equal("Song", Path(v2, "beatmap.title").GetValue<string>());
        Assert.Equal("100 Artist - Song", Path(v2, "folders.beatmap").GetValue<string>());
        Assert.Equal(@"100 Artist - Song\bg.jpg", Path(v2, "directPath.beatmapBackground").GetValue<string>());
    }

    [Fact]
    public void Song_select_mods_are_unknown_not_nomod()
    {
        JsonObject v2 = TosuPayloadBuilder.BuildV2(Scenario("song-select"));
        JsonObject v1 = TosuPayloadBuilder.BuildV1(Scenario("song-select"));

        Assert.Equal("", Path(v2, "play.mods.name").GetValue<string>());
        Assert.Equal("", Path(v1, "menu.mods.str").GetValue<string>());
        Assert.Equal(0, Path(v1, "menu.pp.100").GetValue<double>()); // PP song select : non calculés par BeatInsight
        Assert.Equal(5, Path(v1, "menu.state").GetValue<int>());
    }

    [Fact]
    public void Known_empty_mods_are_nomod()
    {
        var s = Scenario("gameplay") with
        {
            Mods = FieldValue<IReadOnlyList<string>>.Observed(Array.Empty<string>(), T0, FieldProvenance.LiveStats),
        };

        Assert.Equal("NM", Path(TosuPayloadBuilder.BuildV2(s), "play.mods.name").GetValue<string>());
    }

    // ------------------------------------------------------------ invariants FieldValue

    [Fact]
    public void Observed_numeric_zero_is_observed_zero_and_never_known()
    {
        Assert.Equal(FieldState.ObservedZero, FieldValue<int>.Observed(0, T0, FieldProvenance.LiveStats).State);
        Assert.Equal(FieldState.ObservedZero, FieldValue<double>.Observed(0.0, T0, FieldProvenance.LiveStats).State);
        Assert.Equal(FieldState.Known, FieldValue<int>.Observed(3, T0, FieldProvenance.LiveStats).State);
        Assert.Equal(FieldState.Known, FieldValue<string>.Observed("", T0, FieldProvenance.Analysis).State);
    }

    [Fact]
    public void Unknown_has_no_value_and_cannot_be_read_as_zero()
    {
        var unknown = FieldValue<int>.Unknown;

        Assert.False(unknown.HasValue);
        Assert.Throws<InvalidOperationException>(() => unknown.Value);
        Assert.Equal(-1, unknown.Or(-1));
        Assert.NotEqual(FieldValue<int>.Observed(0, null, FieldProvenance.LiveStats), unknown);
        Assert.False(FieldValue<int>.NotAvailableOnScreen.HasValue);
    }

    [Fact]
    public void Values_require_a_provenance()
    {
        Assert.Throws<ArgumentException>(() => FieldValue<int>.Observed(1, T0, FieldProvenance.None));
        Assert.Throws<ArgumentException>(() => FieldValue<int>.LastKnown(1, T0, FieldProvenance.None));
    }

    [Fact]
    public void Last_known_keeps_value_timestamp_and_provenance()
    {
        var observed = FieldValue<int>.Observed(42, T0, FieldProvenance.LiveStats);

        var last = observed.AsLastKnown();

        Assert.Equal(FieldState.LastKnown, last.State);
        Assert.Equal(42, last.Value);
        Assert.Equal(T0, last.ObservedAtUtc);
        Assert.Equal(FieldProvenance.LiveStats, last.Provenance);
    }

    // ------------------------------------------------------------ compatibilité mock

    [Fact]
    public void Mock_keeps_its_v1_output()
    {
        var mock = MockCommunityOverlayDataSource.BuildAt(20_000);
        JsonObject v1 = TosuPayloadBuilder.BuildV1(mock);
        JsonObject v2 = TosuPayloadBuilder.BuildV2(mock);

        Assert.Equal(CommunityOverlaySourceKind.Mock, mock.Source);
        Assert.All(Provenances(mock).Where(p => p != FieldProvenance.None), p => Assert.Equal(FieldProvenance.Mock, p));
        Assert.Equal("MockPlayer", Path(v2, "play.playerName").GetValue<string>());
        Assert.Equal(100, Path(v2, "play.healthBar.normal").GetValue<double>());
        Assert.Equal(200, Path(v1, "gameplay.hp.normal").GetValue<double>());
        Assert.Equal("stable", Path(v2, "client").GetValue<string>());
        Assert.Equal(270, Path(v2, "beatmap.stats.bpm.realtime").GetValue<double>());
        Assert.Equal("HDDT", Path(v1, "menu.mods.str").GetValue<string>());
        Assert.Equal(31_500, Path(v1, "menu.bm.time.mp3").GetValue<double>());
        Assert.Equal(
            Path(v2, "play.rank.current").GetValue<string>(),
            Path(v2, "play.rank.maxThisPlay").GetValue<string>());
        Assert.True(Path(v1, "gameplay.hits.300").GetValue<int>() > 0);
    }
}
