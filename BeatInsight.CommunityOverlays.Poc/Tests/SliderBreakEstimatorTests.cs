using System.Text.Json.Nodes;
using BeatInsight.CommunityOverlays.Poc.Adapter;
using BeatInsight.CommunityOverlays.Poc.Adapter.Live;
using BeatInsight.CommunityOverlays.Poc.Bridge;
using Xunit;
using static BeatInsight.CommunityOverlays.Poc.Tests.LiveAdapterFixtures;

namespace BeatInsight.CommunityOverlays.Poc.Tests;

/// <summary>V3.14-COM-SB-01 : slider breaks estimés avec la logique de tosu.</summary>
public class SliderBreakEstimatorTests
{
    private static readonly DateTimeOffset T = new(2026, 10, 15, 15, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(double seconds) => T.AddSeconds(seconds);

    /// <summary>Copie littérale de tosu (a4da331, states/gameplay.ts:244-254), état initial de Gameplay.init.</summary>
    private sealed class TosuReference
    {
        public int HitSB, ComboPrev, HitMissPrev;

        public void Update(int combo, int maxCombo, int miss)
        {
            if (ComboPrev > maxCombo) ComboPrev = 0;
            if (combo < ComboPrev && miss == HitMissPrev) HitSB += 1;
            HitMissPrev = miss;
            ComboPrev = combo;
        }
    }

    private static SliderBreakState Run(params (int combo, int max, int miss)[] observations)
    {
        var s = SliderBreakState.Initial;
        for (int i = 0; i < observations.Length; i++)
            s = SliderBreakEstimator.Step(s, observations[i].combo, observations[i].max, observations[i].miss, T.AddMilliseconds(100 * (i + 1)));
        return s;
    }

    // ---------------------------------------------------------------- algorithme

    [Fact]
    public void Combo_drop_without_miss_counts_one_break()
    {
        Assert.Equal(1, Run((10, 10, 0), (20, 20, 0), (0, 20, 0)).Count);
    }

    [Fact]
    public void Combo_drop_with_a_new_miss_is_a_miss_not_a_break()
    {
        Assert.Equal(0, Run((10, 10, 0), (20, 20, 0), (0, 20, 1)).Count);
    }

    [Fact]
    public void Same_observation_published_twice_is_counted_once()
    {
        var s = SliderBreakState.Initial;
        s = SliderBreakEstimator.Step(s, 20, 20, 0, T.AddMilliseconds(100));
        s = SliderBreakEstimator.Step(s, 0, 20, 0, T.AddMilliseconds(200));
        s = SliderBreakEstimator.Step(s, 0, 20, 0, T.AddMilliseconds(200)); // republication 250 ms
        s = SliderBreakEstimator.Step(s, 0, 20, 0, T.AddMilliseconds(200)); // tick suivant à 100 ms
        Assert.Equal(1, s.Count);
        Assert.Equal(2, s.Observations);
    }

    [Fact]
    public void Older_observation_is_ignored()
    {
        var s = SliderBreakState.Initial;
        s = SliderBreakEstimator.Step(s, 20, 20, 0, T.AddMilliseconds(200));
        s = SliderBreakEstimator.Step(s, 0, 20, 0, T.AddMilliseconds(100)); // périmée / hors ordre
        Assert.Equal(0, s.Count);
        Assert.Equal(20, s.PreviousCombo);
    }

    [Fact]
    public void Two_breaks_between_two_observations_count_once_like_tosu()
    {
        // 30 → (rupture → 5 → rupture → 2) : une seule baisse visible entre deux observations.
        Assert.Equal(1, Run((30, 30, 0), (2, 30, 0)).Count);
    }

    [Fact]
    public void Combo_that_drops_and_climbs_back_between_observations_is_invisible_like_tosu()
    {
        // 30 → rupture → remonte à 35 avant l'observation suivante : aucune baisse observée.
        Assert.Equal(0, Run((30, 30, 0), (35, 35, 0)).Count);
    }

    [Fact]
    public void Previous_combo_above_max_combo_is_reset_like_tosu()
    {
        // Partie non détectée comme nouvelle par un autre moyen : maxCombo retombe, comboPrev remis à 0.
        Assert.Equal(0, Run((50, 50, 0), (3, 3, 0)).Count);
    }

    [Fact]
    public void Matches_tosu_on_ten_thousand_random_sequences()
    {
        var random = new Random(20261015);
        for (int sequence = 0; sequence < 10_000; sequence++)
        {
            var tosu = new TosuReference();
            var ours = SliderBreakState.Initial;
            int combo = 0, max = 0, miss = 0;
            for (int i = 0; i < 60; i++)
            {
                switch (random.Next(6))
                {
                    case 0: combo = 0; break;                                 // rupture
                    case 1: combo = 0; miss++; break;                         // miss
                    case 2: miss += random.Next(2); combo = random.Next(combo + 1); break; // mélange
                    case 3: combo = 0; max = 0; miss = 0; break;              // retry non détecté
                    default: combo += random.Next(1, 8); break;               // progression
                }
                max = Math.Max(max, combo);
                tosu.Update(combo, max, miss);
                ours = SliderBreakEstimator.Step(ours, combo, max, miss, T.AddMilliseconds(10 * (i + 1)));
            }
            Assert.Equal(tosu.HitSB, ours.Count);
        }
    }

    // ---------------------------------------------------------------- intégration adaptateur et JSON tosu

    private static BeatInsightLiveInput Tick(DateTimeOffset at, double timeMs, int c300, int miss, int combo, int maxCombo,
        string process = Process, string path = MapA, string id = "1001")
    {
        var stats = Stats(at, timeMs, c300, miss: miss, combo: combo, maxCombo: maxCombo, process: process, id: id);
        return new BeatInsightLiveInput(
            Game(at, process, path, id), Route(SourceRoute.Gameplay, at, process), Metadata(path, int.Parse(id)),
            Live(at, timeMs, process, id), stats, Performance(at, stats.JudgedObjects, 10));
    }

    private static int V2(LiveAdapterResult r) => TosuPayloadBuilder.BuildV2(r.Snapshot)["play"]!["hits"]!["sliderBreaks"]!.GetValue<int>();
    private static int V1(LiveAdapterResult r) => TosuPayloadBuilder.BuildV1(r.Snapshot)["gameplay"]!["hits"]!["sliderBreaks"]!.GetValue<int>();

    [Fact]
    public void Break_is_published_as_an_estimated_integer_on_v2_and_legacy()
    {
        var seq = new Sequence();
        seq.Step(ScreenTick(At(0), SourceRoute.SongSelect), At(0));
        seq.Step(Tick(At(1), 5_000, 20, 0, 20, 20), At(1));
        var r = seq.Step(Tick(At(1.25), 5_250, 21, 0, 0, 20), At(1.25));

        Assert.Equal(1, r.Snapshot.Hits.SliderBreaks.Value);
        Assert.Equal(FieldProvenance.Estimated, r.Snapshot.Hits.SliderBreaks.Provenance);
        Assert.Equal(1, V2(r));
        Assert.Equal(1, V1(r));
    }

    [Fact]
    public void Republication_of_the_same_snapshot_never_increments()
    {
        var seq = new Sequence();
        seq.Step(ScreenTick(At(0), SourceRoute.SongSelect), At(0));
        seq.Step(Tick(At(1), 5_000, 20, 0, 20, 20), At(1));
        BeatInsightLiveInput broken = Tick(At(1.25), 5_250, 21, 0, 0, 20);
        LiveAdapterResult r = null!;
        for (int i = 0; i < 8; i++) // ticks à 100 ms sur le même message du pipe
            r = seq.Step(broken, At(1.25 + 0.1 * i));
        Assert.Equal(1, V2(r));
    }

    [Fact]
    public void Retry_resets_and_its_combo_drop_is_not_a_break()
    {
        var seq = new Sequence();
        seq.Step(ScreenTick(At(0), SourceRoute.SongSelect), At(0));
        seq.Step(Tick(At(1), 20_000, 50, 0, 50, 50), At(1));
        seq.Step(Tick(At(1.25), 20_250, 51, 0, 0, 50), At(1.25)); // 1 slider break
        var retry = seq.Step(Tick(At(1.5), 0, 0, 0, 0, 0), At(1.5)); // retry : temps et compteurs reviennent à 0

        Assert.True(retry.Has(LiveAdapterEvent.Retry));
        Assert.Equal(0, V2(retry));
    }

    [Fact]
    public void Pause_with_unchanged_combo_counts_nothing()
    {
        var seq = new Sequence();
        seq.Step(ScreenTick(At(0), SourceRoute.SongSelect), At(0));
        LiveAdapterResult r = null!;
        for (int i = 0; i < 20; i++) // observations nouvelles mais combo figé (pause)
            r = seq.Step(Tick(At(1 + 0.25 * i), 7_000, 30, 0, 30, 30), At(1 + 0.25 * i));
        Assert.Equal(0, V2(r));
    }

    [Fact]
    public void Abandon_then_new_gameplay_starts_from_zero()
    {
        var seq = new Sequence();
        seq.Step(ScreenTick(At(0), SourceRoute.SongSelect), At(0));
        seq.Step(Tick(At(1), 9_000, 40, 0, 40, 40), At(1));
        seq.Step(Tick(At(1.25), 9_250, 41, 0, 0, 40), At(1.25));
        seq.Step(ScreenTick(At(2), SourceRoute.SongSelect), At(2)); // abandon
        var again = seq.Step(Tick(At(3), 1_000, 1, 0, 1, 1), At(3));
        Assert.True(again.Has(LiveAdapterEvent.NewPlay));
        Assert.Equal(0, V2(again));
    }

    [Fact]
    public void Beatmap_change_starts_from_zero()
    {
        var seq = new Sequence();
        seq.Step(ScreenTick(At(0), SourceRoute.SongSelect), At(0));
        seq.Step(Tick(At(1), 9_000, 40, 0, 40, 40), At(1));
        seq.Step(Tick(At(1.25), 9_250, 41, 0, 0, 40), At(1.25));
        seq.Step(ScreenTick(At(2), SourceRoute.SongSelect, MapB, "2002"), At(2));
        var other = seq.Step(Tick(At(3), 1_000, 1, 0, 1, 1, path: MapB, id: "2002"), At(3));
        Assert.Equal(0, V2(other));
    }

    [Fact]
    public void Process_restart_starts_from_zero()
    {
        const string other = "osu!.exe:5555:638640000000000001";
        var seq = new Sequence();
        seq.Step(ScreenTick(At(0), SourceRoute.SongSelect), At(0));
        seq.Step(Tick(At(1), 9_000, 40, 0, 40, 40), At(1));
        seq.Step(Tick(At(1.25), 9_250, 41, 0, 0, 40), At(1.25));
        var restarted = seq.Step(Tick(At(2), 9_500, 41, 0, 0, 40, process: other), At(2));
        Assert.Equal(0, V2(restarted));
    }

    [Fact]
    public void Stale_statistics_are_not_consumed()
    {
        var seq = new Sequence();
        seq.Step(ScreenTick(At(0), SourceRoute.SongSelect), At(0));
        seq.Step(Tick(At(1), 9_000, 40, 0, 40, 40), At(1));
        // Stats observées à 1,25 s mais traitées 3 s plus tard (> MaxAge) : ignorées, rien n'est compté.
        BeatInsightLiveInput fresh = Tick(At(4.25), 9_250, 41, 0, 0, 40);
        var stale = seq.Step(fresh with { Stats = Stats(At(1.25), 9_250, 41, combo: 0, maxCombo: 40) }, At(4.25));
        Assert.True(stale.Has(LiveAdapterEvent.StatsStale));
        Assert.Equal(FieldState.Unknown, stale.F("hits.sliderBreaks"));
        Assert.Equal(1, seq.State.Play!.SliderBreaks!.Observations);
    }

    [Fact]
    public void Initially_unavailable_statistics_stay_unknown_and_neutral()
    {
        var seq = new Sequence();
        seq.Step(ScreenTick(At(0), SourceRoute.SongSelect), At(0));
        var noStats = seq.Step(Tick(At(1), 1_000, 1, 0, 1, 1) with { Stats = null }, At(1));
        Assert.Equal(FieldState.Unknown, noStats.F("hits.sliderBreaks"));
        Assert.Equal(0, V2(noStats)); // valeur neutre du contrat tosu
    }
}
