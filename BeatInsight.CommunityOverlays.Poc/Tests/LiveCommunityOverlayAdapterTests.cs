using BeatInsight.CommunityOverlays.Poc.Adapter;
using BeatInsight.CommunityOverlays.Poc.Adapter.Live;
using BeatInsight.CommunityOverlays.Poc.Bridge;
using Xunit;
using static BeatInsight.CommunityOverlays.Poc.Tests.LiveAdapterFixtures;

namespace BeatInsight.CommunityOverlays.Poc.Tests;

/// <summary>V3.14-COM-09 : transitions et fail-closed de l'adaptateur live, sur séquences synthétiques.</summary>
public class LiveCommunityOverlayAdapterTests
{
    private static DateTimeOffset At(double seconds) => T0.AddSeconds(seconds);

    private static readonly FieldState[] NoValue = [FieldState.Unknown, FieldState.NotAvailableOnScreen];

    // ------------------------------------------------------------ zéro réel / inconnu

    [Fact]
    public void Real_zero_is_distinct_from_unknown()
    {
        var seq = new Sequence();
        seq.Step(ScreenTick(At(0), SourceRoute.SongSelect), At(0));
        var gameplay = seq.Step(GameplayTick(At(0.5), 2_000, c300: 3), At(0.5));

        Assert.Equal(FieldState.ObservedZero, gameplay.F("hits.0"));
        Assert.Equal(0, gameplay.Snapshot.Hits.Miss.Value);
        // V3.14-COM-SB-01 : slider breaks désormais estimés (logique tosu) — zéro observé, provenance Estimated.
        Assert.Equal(FieldState.ObservedZero, gameplay.F("hits.sliderBreaks"));
        Assert.Equal(FieldProvenance.Estimated, gameplay.Snapshot.Hits.SliderBreaks.Provenance);
        Assert.Equal(FieldState.Unknown, gameplay.F("hits.geki"));
        Assert.Throws<InvalidOperationException>(() => gameplay.Snapshot.Hits.Geki.Value);

        var songSelect = seq.Step(ScreenTick(At(1), SourceRoute.SongSelect), At(1));
        Assert.Equal(FieldState.NotAvailableOnScreen, songSelect.F("hits.0"));
    }

    [Fact]
    public void Stats_before_first_judgement_keep_accuracy_and_grade_unknown()
    {
        var seq = new Sequence();
        var r = seq.Step(GameplayTick(At(0), 100, c300: 0), At(0));

        Assert.Equal(FieldState.ObservedZero, r.F("combo"));
        Assert.Equal(FieldState.Unknown, r.F("accuracy"));
        Assert.Equal(FieldState.Unknown, r.F("grade"));
        Assert.Equal(FieldState.Unknown, r.F("pp.current")); // le calculateur ne fournit rien (NoPassedObjects)
    }

    // ------------------------------------------------------------ péremption et identité

    [Fact]
    public void Stale_stats_become_unknown()
    {
        var seq = new Sequence();
        var input = GameplayTick(At(10), 5_000, c300: 4) with
        {
            Stats = Stats(At(8), 5_000, c300: 4), // lues 2 s avant
        };

        var r = seq.Step(input, At(10));

        Assert.True(r.Has(LiveAdapterEvent.StatsStale));
        Assert.Equal(FieldState.Unknown, r.F("combo"));
        Assert.Equal(FieldState.Unknown, r.F("hits.300"));
        Assert.Equal(FieldState.Unknown, r.F("pp.current"));
        Assert.Equal(FieldState.Known, r.F("time")); // la position, elle, est fraîche
    }

    [Fact]
    public void Data_at_exactly_max_age_is_kept_and_beyond_is_dropped()
    {
        var at = At(0);
        var fresh = LiveCommunityOverlayAdapter.Adapt(GameplayTick(at, 1_000, c300: 1), LiveAdapterState.Initial, at + LiveCommunityOverlayAdapter.MaxAge);
        var stale = LiveCommunityOverlayAdapter.Adapt(GameplayTick(at, 1_000, c300: 1), LiveAdapterState.Initial, at + LiveCommunityOverlayAdapter.MaxAge + TimeSpan.FromMilliseconds(1));

        Assert.Equal(FieldState.Known, fresh.F("hits.300"));
        Assert.Equal(CommunityOverlayRoute.Unknown, stale.Snapshot.Route);
        Assert.True(stale.Has(LiveAdapterEvent.GameStateStale));
    }

    [Fact]
    public void Stale_game_state_produces_an_empty_snapshot()
    {
        var r = LiveCommunityOverlayAdapter.Adapt(GameplayTick(At(0), 1_000, c300: 2), LiveAdapterState.Initial, At(5));

        Assert.True(r.Has(LiveAdapterEvent.GameStateStale));
        Assert.Equal(CommunityOverlayRoute.Unknown, r.Snapshot.Route);
        Assert.Null(r.Snapshot.Beatmap);
        Assert.All(r.Diagnostics.Fields.Values, s => Assert.Equal(FieldState.Unknown, s));
    }

    [Fact]
    public void Stats_from_another_process_or_beatmap_are_rejected()
    {
        var otherProcess = GameplayTick(At(0), 1_000, c300: 2) with { Stats = Stats(At(0), 1_000, 2, process: OtherProcess) };
        var otherMap = GameplayTick(At(0), 1_000, c300: 2) with { Stats = Stats(At(0), 1_000, 2, id: "9999") };

        var a = LiveCommunityOverlayAdapter.Adapt(otherProcess, LiveAdapterState.Initial, At(0));
        var b = LiveCommunityOverlayAdapter.Adapt(otherMap, LiveAdapterState.Initial, At(0));

        Assert.True(a.Has(LiveAdapterEvent.StatsIdentityMismatch));
        Assert.True(b.Has(LiveAdapterEvent.StatsIdentityMismatch));
        Assert.Equal(FieldState.Unknown, a.F("hits.300"));
        Assert.Equal(FieldState.Unknown, b.F("hits.300"));
    }

    [Fact]
    public void Route_from_another_process_is_unknown()
    {
        var input = ScreenTick(At(0), SourceRoute.Gameplay) with { Route = Route(SourceRoute.Gameplay, At(0), OtherProcess) };

        var r = LiveCommunityOverlayAdapter.Adapt(input, LiveAdapterState.Initial, At(0));

        Assert.True(r.Has(LiveAdapterEvent.RouteStale));
        Assert.Equal(CommunityOverlayRoute.Unknown, r.Snapshot.Route);
    }

    [Fact]
    public void Process_change_resets_everything()
    {
        var seq = new Sequence();
        seq.Step(GameplayTick(At(0), 5_000, c300: 6, pp: 60), At(0));
        long firstPlay = seq.State.Play!.PlayId;

        // osu! redémarré : nouveau process, déjà en gameplay, stats pas encore découvertes.
        var restarted = new BeatInsightLiveInput(
            Game(At(0.5), OtherProcess), Route(SourceRoute.Gameplay, At(0.5), OtherProcess), Metadata(),
            Live(At(0.5), 300, OtherProcess), Stats: null);
        var r = seq.Step(restarted, At(0.5));

        Assert.True(r.Has(LiveAdapterEvent.ProcessChanged));
        Assert.True(r.Has(LiveAdapterEvent.NewPlay));
        Assert.NotEqual(firstPlay, seq.State.Play!.PlayId);
        Assert.Equal(OtherProcess, r.Snapshot.Identity.ProcessIdentity);
        Assert.Equal(FieldState.Unknown, r.F("hits.300"));
        Assert.Equal(FieldState.Unknown, r.F("pp.maxAchieved"));
    }

    [Fact]
    public void Beatmap_change_resets_play_and_results()
    {
        var seq = new Sequence();
        seq.Step(GameplayTick(At(0), 29_000, c300: TotalObjects, pp: 120), At(0));
        seq.Step(ScreenTick(At(0.5), SourceRoute.Results), At(0.5));
        Assert.NotNull(seq.State.Results);

        var r = seq.Step(ScreenTick(At(1), SourceRoute.Results, MapB, "2002"), At(1));

        Assert.True(r.Has(LiveAdapterEvent.BeatmapChanged));
        Assert.Null(seq.State.Results);
        Assert.Equal(FieldState.Unknown, r.F("hits.300"));
        Assert.Equal(FieldState.Unknown, r.F("pp.current"));
    }

    [Fact]
    public void Metadata_of_another_beatmap_is_never_shown()
    {
        var input = new BeatInsightLiveInput(Game(At(0), path: MapB, id: "2002"), Route(SourceRoute.SongSelect, At(0)), Metadata(MapA));

        var r = LiveCommunityOverlayAdapter.Adapt(input, LiveAdapterState.Initial, At(0));

        Assert.True(r.Has(LiveAdapterEvent.MetadataUnavailable));
        Assert.Equal(FieldState.Unknown, r.F("beatmap.title"));
        Assert.Equal(2002, r.Snapshot.Beatmap!.BeatmapId.Value); // id lu dans le GameStateSnapshot
        Assert.Equal(FieldProvenance.GameState, r.Snapshot.Beatmap.BeatmapId.Provenance);
    }

    // ------------------------------------------------------------ transitions

    [Fact]
    public void SongSelect_to_gameplay_starts_a_new_play_without_old_data()
    {
        var seq = new Sequence();
        var select = seq.Step(ScreenTick(At(0), SourceRoute.SongSelect), At(0));
        Assert.Null(select.Snapshot.Identity.PlayId);

        var play = seq.Step(GameplayTick(At(0.5), 1_500, c300: 2, c100: 1), At(0.5));

        Assert.True(play.Has(LiveAdapterEvent.NewPlay));
        Assert.Equal(1, play.Snapshot.Identity.PlayId);
        Assert.Equal(CommunityOverlayRoute.Gameplay, play.Snapshot.Route);
        Assert.Equal(1, play.Snapshot.Hits.Ok.Value);
        Assert.Equal(77.78, play.Snapshot.Accuracy.Value);
        Assert.Equal("C", play.Snapshot.Grade.Value);
    }

    [Fact]
    public void Pause_keeps_the_same_play_and_values()
    {
        var seq = new Sequence();
        seq.Step(GameplayTick(At(0), 8_000, c300: 5), At(0));
        long playId = seq.State.Play!.PlayId;

        // Pause : la route reste Gameplay, le temps est figé, les compteurs aussi.
        for (int i = 1; i <= 6; i++)
        {
            var r = seq.Step(GameplayTick(At(i * 0.5), 8_000, c300: 5), At(i * 0.5));
            Assert.False(r.Has(LiveAdapterEvent.Retry));
            Assert.False(r.Has(LiveAdapterEvent.NewPlay));
            Assert.Equal(playId, r.Snapshot.Identity.PlayId);
            Assert.Equal(8_000, r.Snapshot.CurrentTimeMs.Value);
        }

        var resumed = seq.Step(GameplayTick(At(3.5), 8_500, c300: 6), At(3.5));
        Assert.Equal(playId, resumed.Snapshot.Identity.PlayId);
        Assert.Equal(6, resumed.Snapshot.Hits.Great.Value);
    }

    [Fact]
    public void Retry_starts_a_new_play_and_resets_max_pp()
    {
        var seq = new Sequence();
        seq.Step(GameplayTick(At(0), 10_000, c300: 7, pp: 150), At(0));
        long firstPlay = seq.State.Play!.PlayId;

        var retry = seq.Step(GameplayTick(At(0.5), 400, c300: 1, pp: 5), At(0.5));

        Assert.True(retry.Has(LiveAdapterEvent.Retry));
        Assert.Equal(firstPlay + 1, retry.Snapshot.Identity.PlayId);
        Assert.Equal(5, retry.Snapshot.Pp.MaxAchieved.Value);
    }

    [Fact]
    public void Retry_is_detected_from_hit_counts_when_time_is_unavailable()
    {
        var seq = new Sequence();
        seq.Step(GameplayTick(At(0), 10_000, c300: 7) with { LiveGameplay = null }, At(0));
        var input = GameplayTick(At(0.5), 10_000, c300: 2) with { LiveGameplay = null };

        var r = seq.Step(input, At(0.5));

        Assert.True(r.Has(LiveAdapterEvent.Retry));
    }

    [Fact]
    public void Max_pp_is_the_running_maximum_of_valid_current_pp()
    {
        var seq = new Sequence();
        seq.Step(GameplayTick(At(0), 1_000, c300: 2, pp: 40), At(0));
        seq.Step(GameplayTick(At(0.5), 2_000, c300: 3, pp: 90), At(0.5));
        var r = seq.Step(GameplayTick(At(1), 3_000, c300: 3, miss: 1, pp: 60), At(1));

        Assert.Equal(60, r.Snapshot.Pp.Current.Value);
        Assert.Equal(90, r.Snapshot.Pp.MaxAchieved.Value);
        Assert.Equal(FieldProvenance.Derived, r.Snapshot.Pp.MaxAchieved.Provenance);
    }

    [Fact]
    public void Abandon_leaves_no_old_statistics_in_song_select()
    {
        var seq = new Sequence();
        seq.Step(GameplayTick(At(0), 9_000, c300: 6, miss: 2, pp: 80), At(0));

        var r = seq.Step(ScreenTick(At(0.5), SourceRoute.SongSelect), At(0.5));

        Assert.True(r.Has(LiveAdapterEvent.PlayEnded));
        Assert.Null(seq.State.Play);
        Assert.Equal(FieldState.NotAvailableOnScreen, r.F("hits.0"));
        Assert.Equal(FieldState.NotAvailableOnScreen, r.F("pp.current"));
        Assert.Equal(FieldState.Unknown, r.F("mods"));
        var v1 = TosuPayloadBuilder.BuildV1(r.Snapshot);
        Assert.Equal(0, v1["gameplay"]!["hits"]!["0"]!.GetValue<int>());
        Assert.Equal(0, v1["gameplay"]!["pp"]!["current"]!.GetValue<double>());
    }

    [Fact]
    public void Incomplete_results_emit_neutral_values()
    {
        var seq = new Sequence();
        seq.Step(GameplayTick(At(0), 27_000, c300: 8, pp: 100), At(0)); // 8 objets sur 10

        var r = seq.Step(ScreenTick(At(0.5), SourceRoute.Results), At(0.5));

        Assert.True(r.Has(LiveAdapterEvent.ResultsIncomplete));
        Assert.Equal(CommunityOverlayRoute.Results, r.Snapshot.Route);
        Assert.Equal(FieldState.Unknown, r.F("hits.300"));
        Assert.Equal(FieldState.Unknown, r.F("pp.current"));
        var v2 = TosuPayloadBuilder.BuildV2(r.Snapshot);
        Assert.Equal(0, v2["resultsScreen"]!["hits"]!["300"]!.GetValue<int>());
        Assert.Equal("", v2["resultsScreen"]!["rank"]!.GetValue<string>());
    }

    [Fact]
    public void Complete_results_keep_last_values_as_last_known()
    {
        var seq = new Sequence();
        seq.Step(GameplayTick(At(0), 20_000, c300: 6, pp: 70, mods: ["HD"]), At(0));
        seq.Step(GameplayTick(At(0.5), 29_500, c300: TotalObjects, pp: 140, mods: ["HD"]), At(0.5));
        long playId = seq.State.Play!.PlayId;

        var first = seq.Step(ScreenTick(At(1), SourceRoute.Results), At(1));
        var later = seq.Step(ScreenTick(At(9), SourceRoute.Results), At(9)); // bien après 1,5 s

        foreach (var r in new[] { first, later })
        {
            Assert.Equal(playId, r.Snapshot.Identity.PlayId);
            Assert.Equal(FieldState.LastKnown, r.F("hits.300"));
            Assert.Equal(TotalObjects, r.Snapshot.Hits.Great.Value);
            Assert.Equal(FieldState.LastKnown, r.F("pp.current"));
            Assert.Equal(140, r.Snapshot.Pp.Current.Value);
            Assert.Equal(FieldState.LastKnown, r.F("mods"));
            // V3.14-COM-SB-01 : estimation de la partie conservée en résultats, comme les autres compteurs.
            Assert.Equal(FieldState.LastKnown, r.F("hits.sliderBreaks"));
            Assert.Equal(FieldProvenance.Estimated, r.Snapshot.Hits.SliderBreaks.Provenance);
            Assert.Equal("SS", r.Snapshot.Grade.Value);
        }
        Assert.True(first.Has(LiveAdapterEvent.ResultsLatched));
        Assert.Equal("XH", TosuPayloadBuilder.BuildV1(later.Snapshot)["gameplay"]!["hits"]!["grade"]!["current"]!.GetValue<string>());

        var back = seq.Step(ScreenTick(At(10), SourceRoute.SongSelect), At(10));
        Assert.Equal(FieldState.NotAvailableOnScreen, back.F("hits.300"));
        Assert.Null(seq.State.Results);
    }

    [Fact]
    public void Results_without_a_tracked_play_are_neutral()
    {
        var seq = new Sequence();
        seq.Step(ScreenTick(At(0), SourceRoute.SongSelect), At(0));

        var r = seq.Step(ScreenTick(At(0.5), SourceRoute.Results), At(0.5));

        Assert.True(r.Has(LiveAdapterEvent.ResultsWithoutPlay));
        Assert.Equal(FieldState.Unknown, r.F("hits.300"));
    }

    [Fact]
    public void Results_after_a_suspension_gap_are_not_latched()
    {
        var seq = new Sequence();
        seq.Step(GameplayTick(At(0), 29_500, c300: TotalObjects, pp: 150), At(0));
        seq.Step(GameplayTick(At(0.5), 29_500, c300: TotalObjects) with { Suspension = SourceSuspension.FastLabeling }, At(0.5));

        var r = seq.Step(ScreenTick(At(1), SourceRoute.Results), At(1));

        Assert.Equal(FieldState.Unknown, r.F("hits.300"));
    }

    [Fact]
    public void Osu_closed_produces_an_empty_snapshot_and_resets()
    {
        var seq = new Sequence();
        seq.Step(GameplayTick(At(0), 4_000, c300: 3), At(0));

        var closed = new BeatInsightLiveInput(Game(At(0.5), availability: SourceAvailability.NoProcess), Route: null);
        var r = seq.Step(closed, At(0.5));

        Assert.True(r.Has(LiveAdapterEvent.GameUnavailable));
        Assert.Equal(CommunityOverlayIdentity.None, r.Snapshot.Identity);
        Assert.Null(r.Snapshot.Beatmap);
        Assert.Null(seq.State.Play);
        Assert.Null(seq.State.ProcessIdentity);
        Assert.Equal(1, seq.State.LastPlayId); // le compteur de parties n'est jamais réutilisé
    }

    [Fact]
    public void Memory_discovery_not_ready_then_ready_keeps_the_same_play()
    {
        var seq = new Sequence();
        var notReady = new BeatInsightLiveInput(Game(At(0)), Route(SourceRoute.Gameplay, At(0)), Metadata(), Live(At(0), 900), Stats: null);
        var first = seq.Step(notReady, At(0));

        Assert.True(first.Has(LiveAdapterEvent.StatsUnavailable));
        Assert.Equal(FieldState.Known, first.F("time"));
        Assert.Equal(FieldState.Unknown, first.F("combo"));
        Assert.Equal(FieldState.Unknown, first.F("mods"));

        var ready = seq.Step(GameplayTick(At(0.5), 1_400, c300: 1), At(0.5));
        Assert.Equal(first.Snapshot.Identity.PlayId, ready.Snapshot.Identity.PlayId);
        Assert.Equal(FieldState.Known, ready.F("combo"));
    }

    [Theory]
    [InlineData(SourceSuspension.FastLabeling)]
    [InlineData(SourceSuspension.OverlayLayoutEdit)]
    [InlineData(SourceSuspension.BackgroundLibraryWork)]
    public void Suspended_source_never_presents_frozen_data_as_live(SourceSuspension suspension)
    {
        var seq = new Sequence();
        seq.Step(GameplayTick(At(0), 4_000, c300: 3, pp: 30), At(0));

        var r = seq.Step(GameplayTick(At(0.5), 4_500, c300: 4, pp: 35) with { Suspension = suspension }, At(0.5));

        Assert.True(r.Has(LiveAdapterEvent.SourceSuspended));
        Assert.Equal(CommunityOverlayRoute.Unknown, r.Snapshot.Route);
        Assert.Null(r.Snapshot.Beatmap);
        Assert.All(r.Diagnostics.Fields.Values, s => Assert.Equal(FieldState.Unknown, s));

        // À la reprise, la continuité n'est pas prouvée : nouvelle partie.
        var resumed = seq.Step(GameplayTick(At(1), 5_000, c300: 5), At(1));
        Assert.True(resumed.Has(LiveAdapterEvent.NewPlay));
    }

    [Fact]
    public void Frozen_overlay_snapshot_without_suspension_flag_still_expires()
    {
        // OverlayEngine.CurrentSnapshot figé (édition du layout non signalée) : les horodatages vieillissent.
        var seq = new Sequence();
        var frozen = GameplayTick(At(0), 4_000, c300: 3, pp: 30);
        seq.Step(frozen, At(0));

        var r = seq.Step(frozen, At(2));

        Assert.True(r.Has(LiveAdapterEvent.GameStateStale));
        Assert.All(r.Diagnostics.Fields.Values, s => Assert.Equal(FieldState.Unknown, s));
    }

    // ------------------------------------------------------------ Lazer, PP

    [Fact]
    public void Lazer_without_stats_emits_time_and_neutral_values()
    {
        var input = new BeatInsightLiveInput(
            Game(At(0), provider: "LazerGameProvider", client: "osu!lazer"),
            Route(SourceRoute.Gameplay, At(0)), Metadata(), Live(At(0), 12_345), Stats: null);

        var r = LiveCommunityOverlayAdapter.Adapt(input, LiveAdapterState.Initial, At(0));

        Assert.Equal("lazer", r.Snapshot.Client.Value);
        Assert.Equal(12_345, r.Snapshot.CurrentTimeMs.Value);
        Assert.Equal(FieldState.Unknown, r.F("combo"));
        Assert.Equal(FieldState.Unknown, r.F("accuracy"));
        Assert.Equal(FieldState.Unknown, r.F("pp.current"));
        var v2 = TosuPayloadBuilder.BuildV2(r.Snapshot);
        Assert.Equal("lazer", v2["client"]!.GetValue<string>());
        Assert.Equal(0, v2["play"]!["combo"]!["current"]!.GetValue<int>());
        Assert.Equal(12_345, v2["beatmap"]!["time"]!["live"]!.GetValue<double>());
    }

    [Fact]
    public void Missing_performance_is_unknown_never_zero_or_recomputed()
    {
        var input = GameplayTick(At(0), 5_000, c300: 4) with { Performance = null };

        var r = LiveCommunityOverlayAdapter.Adapt(input, LiveAdapterState.Initial, At(0));

        Assert.True(r.Has(LiveAdapterEvent.PerformanceUnavailable));
        Assert.Equal(FieldState.Unknown, r.F("pp.current"));
        Assert.Equal(FieldState.Unknown, r.F("pp.fc"));
        Assert.Equal(FieldState.Unknown, r.F("pp.ss"));
        Assert.Equal(FieldState.Known, r.F("hits.300"));
    }

    [Fact]
    public void Performance_inconsistent_with_stats_is_rejected()
    {
        var input = GameplayTick(At(0), 5_000, c300: 4) with { Performance = Performance(At(0), passed: 3, current: 44) };

        var r = LiveCommunityOverlayAdapter.Adapt(input, LiveAdapterState.Initial, At(0));

        Assert.True(r.Has(LiveAdapterEvent.PerformanceInconsistent));
        Assert.Equal(FieldState.Unknown, r.F("pp.current"));
    }

    [Fact]
    public void Performance_maps_ss_and_95_without_inventing_96_to_99()
    {
        var r = LiveCommunityOverlayAdapter.Adapt(GameplayTick(At(0), 5_000, c300: 4, pp: 44), LiveAdapterState.Initial, At(0));

        Assert.Equal(44, r.Snapshot.Pp.Current.Value);
        Assert.Equal(300, r.Snapshot.Pp.ByAccuracy[100].Value);
        Assert.Equal(220, r.Snapshot.Pp.ByAccuracy[95].Value);
        Assert.False(r.Snapshot.Pp.ByAccuracy.ContainsKey(98));
    }

    // ------------------------------------------------------------ invariants live

    [Fact]
    public void Live_snapshots_never_carry_mock_values()
    {
        var seq = new Sequence();
        var inputs = new (BeatInsightLiveInput Input, double At)[]
        {
            (ScreenTick(At(0), SourceRoute.SongSelect), 0),
            (GameplayTick(At(0.5), 1_000, c300: 2), 0.5),
            (GameplayTick(At(1), 29_000, c300: TotalObjects), 1),
            (ScreenTick(At(1.5), SourceRoute.Results), 1.5),
            (new BeatInsightLiveInput(null, null), 2),
        };

        foreach (var (input, at) in inputs)
        {
            var r = seq.Step(input, At(at));
            Assert.Equal(CommunityOverlaySourceKind.Live, r.Snapshot.Source);
            Assert.DoesNotContain(FieldProvenance.Mock, Provenances(r.Snapshot));
            Assert.Contains(r.F("playerName"), NoValue);
            Assert.Contains(r.F("healthBar"), NoValue);
            Assert.Contains(r.F("keys"), NoValue);
            Assert.Contains(r.F("gradeMaxThisPlay"), NoValue);
            Assert.Contains(r.F("beatmap.bpm.realtime"), NoValue);
        }
    }

    [Fact]
    public void Adapter_is_deterministic()
    {
        var input = GameplayTick(At(0), 5_000, c300: 4, c100: 1, pp: 44, mods: ["HD", "DT"]);

        var a = LiveCommunityOverlayAdapter.Adapt(input, LiveAdapterState.Initial, At(0.2));
        var b = LiveCommunityOverlayAdapter.Adapt(input, LiveAdapterState.Initial, At(0.2));

        Assert.Equal(TosuPayloadBuilder.BuildV2(a.Snapshot).ToJsonString(), TosuPayloadBuilder.BuildV2(b.Snapshot).ToJsonString());
        Assert.Equal(a.State, b.State);
    }

    // ------------------------------------------------------------ beatmap et mods

    [Fact]
    public void Difficulty_is_converted_only_when_mods_are_known()
    {
        var withMods = LiveCommunityOverlayAdapter.Adapt(GameplayTick(At(0), 5_000, c300: 4, mods: ["HR", "DT"]), LiveAdapterState.Initial, At(0));
        var songSelect = LiveCommunityOverlayAdapter.Adapt(ScreenTick(At(0), SourceRoute.SongSelect), LiveAdapterState.Initial, At(0));

        var map = withMods.Snapshot.Beatmap!;
        Assert.Equal(9, map.Ar.Original.Value);
        Assert.Equal(FieldProvenance.Derived, map.Ar.Converted.Provenance);
        Assert.Equal(11, map.Ar.Converted.Value);   // HR 9 → 10 (plafond), DT → 11
        Assert.Equal(270, map.BpmCommon.Value);     // 180 × 1,5
        Assert.Equal(FieldState.Unknown, map.Stars.State); // étoiles moddées non fournies par BeatInsight

        var nomod = songSelect.Snapshot.Beatmap!;
        Assert.Equal(9, nomod.Ar.Converted.Value);  // mods inconnus : valeur de base
        Assert.Equal(5.25, nomod.Stars.Value);
        Assert.Equal(FieldProvenance.Analysis, nomod.Stars.Provenance);
    }

    [Theory]
    [InlineData(9.0, "DT", 10.33)]
    [InlineData(9.0, "HR", 10.0)]
    [InlineData(9.0, "EZ", 4.5)]
    [InlineData(8.0, "HT", 6.33)]
    public void Approach_rate_conversion_follows_osu_formulas(double ar, string mod, double expected)
    {
        double rate = mod == "DT" ? 1.5 : mod == "HT" ? 0.75 : 1.0;
        Assert.Equal(expected, Math.Round(DifficultyMath.ApproachRate(ar, [mod], rate), 2));
    }

    [Theory]
    [InlineData(10, 0, 0, 0, "SS")]
    [InlineData(95, 5, 0, 0, "S")]
    [InlineData(94, 4, 2, 0, "A")]   // 50 > 1 %
    [InlineData(95, 4, 0, 1, "A")]   // miss
    [InlineData(85, 15, 0, 0, "A")]
    [InlineData(75, 25, 0, 0, "B")]
    [InlineData(65, 35, 0, 0, "C")]
    [InlineData(50, 50, 0, 0, "D")]
    public void Grade_follows_stable_rules(int c300, int c100, int c50, int miss, string expected)
    {
        Assert.Equal(expected, LiveCommunityOverlayAdapter.GradeFor(Stats(T0, 0, c300, c100, c50, miss)));
    }

    [Fact]
    public void Files_are_relative_and_never_escape_songs()
    {
        var files = LiveCommunityOverlayAdapter.BuildFiles(Game(T0))!;
        Assert.Equal("100 Artist - Song", files.Folder);
        Assert.Equal("Artist - Song (Mapper) [Hard].osu", files.OsuFile);
        Assert.Equal("bg.jpg", files.Background);
        Assert.Null(files.Audio);

        var outsideBackground = Game(T0) with { CurrentBackgroundPath = @"C:\Users\someone\Pictures\bg.jpg" };
        Assert.Null(LiveCommunityOverlayAdapter.BuildFiles(outsideBackground)!.Background);

        var outsideSongs = Game(T0, path: @"D:\Elsewhere\map\x.osu");
        Assert.Null(LiveCommunityOverlayAdapter.BuildFiles(outsideSongs));

        var relative = Game(T0) with { SongsDirectory = @"Songs" };
        Assert.Null(LiveCommunityOverlayAdapter.BuildFiles(relative));
    }
}
