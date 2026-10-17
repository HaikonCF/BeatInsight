using System.Text.Json.Nodes;
using BeatInsight.CommunityOverlays.Poc.Adapter;
using BeatInsight.CommunityOverlays.Poc.Bridge;
using Xunit;

namespace BeatInsight.CommunityOverlays.Poc.Tests;

public class TosuPayloadBuilderTests
{
    private static CommunityOverlaySnapshot Gameplay(string grade, params string[] mods) =>
        MockCommunityOverlayDataSource.BuildAt(20_000) with { Grade = MockField.Of(grade), Mods = MockField.Of<IReadOnlyList<string>>(mods) };

    [Theory]
    [InlineData("SS", new string[0], "X")]
    [InlineData("SS", new[] { "HD" }, "XH")]
    [InlineData("SS", new[] { "FL" }, "XH")]
    [InlineData("S", new string[0], "S")]
    [InlineData("S", new[] { "HD", "DT" }, "SH")]
    [InlineData("A", new[] { "HD" }, "A")]
    [InlineData("D", new string[0], "D")]
    public void Grade_uses_tosu_letters(string neutral, string[] mods, string expected)
    {
        Assert.Equal(expected, TosuPayloadBuilder.TosuGrade(neutral, mods));
    }

    [Fact]
    public void V2_and_v1_expose_the_tosu_grade()
    {
        var snapshot = Gameplay("S", "HD");

        Assert.Equal("SH", TosuPayloadBuilder.BuildV2(snapshot)["play"]!["rank"]!["current"]!.GetValue<string>());
        Assert.Equal("SH", TosuPayloadBuilder.BuildV1(snapshot)["gameplay"]!["hits"]!["grade"]!["current"]!.GetValue<string>());
    }

    /// <summary>Régression KeyOverlay : data.settings.keybinds.osu.k1 lu sans garde.</summary>
    [Fact]
    public void V2_contains_settings_keybinds_osu()
    {
        JsonObject v2 = TosuPayloadBuilder.BuildV2(Gameplay("A"));

        var osu = v2["settings"]!["keybinds"]!["osu"]!;
        Assert.NotNull(osu["k1"]);
        Assert.NotNull(osu["k2"]);
        Assert.Equal("", osu["k1"]!.GetValue<string>()); // inconnu : jamais inventé
    }

    [Fact]
    public void V2_top_level_keys_are_a_subset_of_tosu()
    {
        // packages/tosu/src/api/types/v2.ts — interface TosuAPi.
        string[] tosuKeys =
        [
            "game", "client", "server", "state", "session", "settings", "profile", "beatmap", "play",
            "leaderboard", "performance", "resultsScreen", "folders", "files", "directPath",
            "tourney", "rankedPlay", "room",
        ];

        foreach (var (key, _) in TosuPayloadBuilder.BuildV2(Gameplay("A")))
            Assert.Contains(key, tosuKeys);
    }

    /// <summary>Même forme que tosu buildResultV2Precise : keys, hitErrors, tourney (pas de temps).</summary>
    [Fact]
    public void Precise_matches_tosu_shape()
    {
        var snapshot = Gameplay("A") with
        {
            Keys = MockField.Of(new CommunityOverlayKeys(
                new CommunityOverlayKey(true, 12), new CommunityOverlayKey(false, 11),
                CommunityOverlayKey.Idle, CommunityOverlayKey.Idle)),
        };

        JsonObject precise = TosuPayloadBuilder.BuildPrecise(snapshot);

        Assert.Equal(["keys", "hitErrors", "tourney"], precise.Select(kv => kv.Key).ToArray());
        Assert.True(precise["keys"]!["k1"]!["isPressed"]!.GetValue<bool>());
        Assert.Equal(12, precise["keys"]!["k1"]!["count"]!.GetValue<int>());
        Assert.Equal(11, precise["keys"]!["k2"]!["count"]!.GetValue<int>());
        Assert.Equal(0, precise["keys"]!["m1"]!["count"]!.GetValue<int>());
    }

    /// <summary>Même construction que tosu buildResultV2 / buildResult (path.join → "\" sous Windows).</summary>
    [Fact]
    public void File_paths_are_relative_like_tosu_and_never_absolute()
    {
        var snapshot = Gameplay("A");
        var files = snapshot.Beatmap!.Files!;

        JsonObject v2 = TosuPayloadBuilder.BuildV2(snapshot);
        JsonObject v1 = TosuPayloadBuilder.BuildV1(snapshot);

        Assert.Equal(files.Folder, v2["folders"]!["beatmap"]!.GetValue<string>());
        Assert.Equal(files.Background, v2["files"]!["background"]!.GetValue<string>());
        Assert.Equal($"{files.Folder}\\{files.Background}", v2["directPath"]!["beatmapBackground"]!.GetValue<string>());
        Assert.Equal($"{files.Folder}\\{files.Audio}", v2["directPath"]!["beatmapAudio"]!.GetValue<string>());
        Assert.Equal($"{files.Folder}\\{files.Background}", v1["menu"]!["bm"]!["path"]!["full"]!.GetValue<string>());
        Assert.Equal("", v2["folders"]!["songs"]!.GetValue<string>());
        Assert.Equal("", v2["folders"]!["game"]!.GetValue<string>());
    }

    [Fact]
    public void Mock_alternates_beatmaps_between_cycles()
    {
        var first = MockCommunityOverlayDataSource.BuildAt(1_000, cycle: 0).Beatmap!;
        var second = MockCommunityOverlayDataSource.BuildAt(1_000, cycle: 1).Beatmap!;

        Assert.NotEqual(first.Checksum, second.Checksum);
        Assert.NotEqual(first.Files!.Background, second.Files!.Background);
    }

    [Theory]
    [InlineData(CommunityOverlayRoute.SongSelect, 5)]
    [InlineData(CommunityOverlayRoute.Gameplay, 2)]
    [InlineData(CommunityOverlayRoute.Results, 7)]
    public void State_numbers_match_osu_stable(CommunityOverlayRoute route, int expected)
    {
        var snapshot = Gameplay("A") with { Route = route };

        Assert.Equal(expected, TosuPayloadBuilder.BuildV2(snapshot)["state"]!["number"]!.GetValue<int>());
        Assert.Equal(expected, TosuPayloadBuilder.BuildV1(snapshot)["menu"]!["state"]!.GetValue<int>());
    }
}
