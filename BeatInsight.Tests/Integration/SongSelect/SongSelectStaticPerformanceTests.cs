using System.Collections.Immutable;
using System.IO;
using BeatInsight.Integration.Game;
using BeatInsight.Services;

namespace BeatInsight.Tests.Integration.SongSelect;

/// <summary>
/// V3.14-COM-PP-03 : le SS de la sélection est exactement celui du calculateur gameplay (même préparation),
/// et l'ajout de <see cref="OsuLivePerformanceCalculator.CalculateStatic"/> ne change aucun résultat gameplay.
/// </summary>
public sealed class SongSelectStaticPerformanceTests
{
    private const string MapOne = "Tower Of Heaven [Extra].osu";
    private const string MapTwo = "Snow Goose [Extra].osu";

    private static string PathFor(string filename) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps", filename);

    private static ImmutableArray<LiveGameplayMod> Mods(string mods) =>
        mods.Length == 0 ? [] : mods.Split('+').Select(acronym => new LiveGameplayMod(acronym)).ToImmutableArray();

    /// <summary>Une partie d'un seul objet parfait : SsPp et Accuracy95FcPp viennent de la préparation complète.</summary>
    private static LivePerformanceInput OneObject(string path, ImmutableArray<LiveGameplayMod> mods) =>
        new(path, mods, 1, 1, 1, 0, 0, 0, 1, TimeSpan.FromDays(1));

    [Theory]
    [InlineData(MapOne, "")]
    [InlineData(MapOne, "HD")]
    [InlineData(MapOne, "HR")]
    [InlineData(MapOne, "DT")]
    [InlineData(MapOne, "NC")]
    [InlineData(MapOne, "HD+HR")]
    [InlineData(MapOne, "HD+DT")]
    [InlineData(MapTwo, "")]
    [InlineData(MapTwo, "EZ")]
    [InlineData(MapTwo, "HD+HR+DT")]
    public void Static_ss_equals_gameplay_ss_exactly(string map, string mods)
    {
        string path = PathFor(map);
        LivePerformanceSnapshot gameplay = Assert.IsType<LivePerformanceSnapshot>(
            new OsuLivePerformanceCalculator().Calculate(OneObject(path, Mods(mods))).Snapshot);
        LiveStaticPerformanceResult selection = new OsuLivePerformanceCalculator().CalculateStatic(path, Mods(mods));

        Assert.Equal(LivePerformanceFailureReason.None, selection.FailureReason);
        LiveStaticPerformance value = Assert.IsType<LiveStaticPerformance>(selection.Value);
        Assert.Equal(gameplay.SsPp, value.SsPp);
        Assert.Equal(gameplay.Accuracy95FcPp, value.Accuracy95FcPp);
        Assert.True(value.SsPp > 0);
    }

    [Fact]
    public void Nightcore_and_double_time_give_the_same_ss()
    {
        var calculator = new OsuLivePerformanceCalculator();
        double dt = calculator.CalculateStatic(PathFor(MapOne), Mods("DT")).Value!.SsPp;
        double nc = calculator.CalculateStatic(PathFor(MapOne), Mods("NC")).Value!.SsPp;
        Assert.Equal(dt, nc, 6);
    }

    [Fact]
    public void Static_calculation_does_not_change_later_gameplay_results_on_the_same_instance()
    {
        string path = PathFor(MapOne);
        var shared = new OsuLivePerformanceCalculator();
        _ = shared.CalculateStatic(path, Mods("HD+DT"));
        _ = shared.CalculateStatic(path, Mods(""));
        LivePerformanceSnapshot after = shared.Calculate(OneObject(path, Mods("HD+DT"))).Snapshot!;
        LivePerformanceSnapshot fresh = new OsuLivePerformanceCalculator().Calculate(OneObject(path, Mods("HD+DT"))).Snapshot!;

        Assert.Equal(fresh.CurrentPp, after.CurrentPp);
        Assert.Equal(fresh.FcPp, after.FcPp);
        Assert.Equal(fresh.SsPp, after.SsPp);
        Assert.Equal(fresh.Accuracy95FcPp, after.Accuracy95FcPp);
    }

    [Fact]
    public void Mods_key_uses_the_calculator_convention()
    {
        Assert.Equal("DT+HD", new OsuLivePerformanceCalculator().CalculateStatic(PathFor(MapOne), Mods("HD+DT")).Value!.ModsKey);
        Assert.Equal("NM", new OsuLivePerformanceCalculator().CalculateStatic(PathFor(MapOne), Mods("")).Value!.ModsKey);
    }

    [Fact]
    public void Unsupported_mod_or_missing_file_is_reported_without_a_value()
    {
        var calculator = new OsuLivePerformanceCalculator();
        Assert.Equal(LivePerformanceFailureReason.UnsupportedMods, calculator.CalculateStatic(PathFor(MapOne), Mods("RX")).FailureReason);
        Assert.Equal(LivePerformanceFailureReason.BeatmapUnavailable,
            calculator.CalculateStatic(Path.Combine(AppContext.BaseDirectory, "missing.osu"), Mods("")).FailureReason);
    }

    [Fact]
    public void Cancellation_is_cooperative()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            new OsuLivePerformanceCalculator().CalculateStatic(PathFor(MapTwo), Mods("DT"), cts.Token));
    }

    [Fact]
    public void Calculator_version_identifies_the_engine()
    {
        Assert.StartsWith("ppy.osu.Game.Rulesets.Osu/", OsuLivePerformanceCalculator.StaticPerformanceVersion);
        Assert.EndsWith("/classic-nsha/timed", OsuLivePerformanceCalculator.StaticPerformanceVersion);
    }
}
