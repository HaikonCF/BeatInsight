using System.Text.Json.Nodes;
using BeatInsight.CommunityOverlays.Poc.Adapter;
using BeatInsight.CommunityOverlays.Poc.Adapter.Live;
using BeatInsight.CommunityOverlays.Poc.Bridge;
using Xunit;
using static BeatInsight.CommunityOverlays.Poc.Tests.LiveAdapterFixtures;

namespace BeatInsight.CommunityOverlays.Poc.Tests;

/// <summary>
/// V3.14-COM-12 : conservation de la dernière position de jeu réellement observée sur l'écran de
/// résultats (Simplistic calcule sa barre avec <c>menu.bm.time.current / menu.bm.time.mp3</c>).
/// Séquences synthétiques, deux ticks BeatInsight par seconde.
/// </summary>
public class ResultsAudioPositionTests
{
    private const double AudioMs = 30_000;

    /// <summary>Ajoute le .osu courant et sa durée audio (COM-11) à une entrée.</summary>
    private static BeatInsightLiveInput WithAudio(BeatInsightLiveInput input, string path = MapA, double? audioMs = AudioMs) => input with
    {
        OsuFile = new SourceOsuFile(path, "0123456789abcdef0123456789abcdef", "audio.mp3", null, null),
        AudioFile = new SourceAudioFile(path, "audio.mp3", audioMs, audioMs is null ? "Pending" : "Known"),
    };

    private static BeatInsightLiveInput Play(DateTimeOffset at, double timeMs, int judged) =>
        WithAudio(GameplayTick(at, timeMs, judged));

    private static BeatInsightLiveInput Screen(DateTimeOffset at, SourceRoute route, string path = MapA, string id = "1001") =>
        WithAudio(ScreenTick(at, route, path, id), path);

    private static (double V1, double V2) Times(LiveAdapterResult r)
    {
        JsonObject v1 = TosuPayloadBuilder.BuildV1(r.Snapshot);
        JsonObject v2 = TosuPayloadBuilder.BuildV2(r.Snapshot);
        return (v1["menu"]!["bm"]!["time"]!["current"]!.GetValue<double>(), v2["beatmap"]!["time"]!["live"]!.GetValue<double>());
    }

    private static double Mp3(LiveAdapterResult r) =>
        TosuPayloadBuilder.BuildV1(r.Snapshot)["menu"]!["bm"]!["time"]!["mp3"]!.GetValue<double>();

    /// <summary>Partie complète (10 objets) jusqu'à 28 s, puis résultats 500 ms après le dernier tick.</summary>
    private static Sequence PlayToResults(out LiveAdapterResult results, int finalJudged = TotalObjects)
    {
        var seq = new Sequence();
        var t = T0;
        seq.Step(Play(t, 1_000, 0), t);
        t += TimeSpan.FromMilliseconds(500);
        seq.Step(Play(t, 15_000, 5), t);
        t += TimeSpan.FromMilliseconds(500);
        seq.Step(Play(t, 28_000, finalJudged), t);
        t += TimeSpan.FromMilliseconds(500);
        results = seq.Step(Screen(t, SourceRoute.Results), t);
        return seq;
    }

    // ------------------------------------------------------------ conservation

    [Fact]
    public void Gameplay_to_results_keeps_the_last_observed_position_as_last_known()
    {
        PlayToResults(out var results);

        Assert.Equal(CommunityOverlayRoute.Results, results.Snapshot.Route);
        Assert.Equal(FieldState.LastKnown, results.F("time"));
        Assert.Equal(28_000, results.Snapshot.CurrentTimeMs.Value);
        // Horodatage et provenance d'origine (lecture de la position, pas l'instant des résultats).
        Assert.Equal(T0 + TimeSpan.FromSeconds(1), results.Snapshot.CurrentTimeMs.ObservedAtUtc);
        Assert.Equal(FieldProvenance.GameplayPosition, results.Snapshot.CurrentTimeMs.Provenance);
        Assert.True(results.Has(LiveAdapterEvent.ResultsPositionKept));
        Assert.Equal((28_000, 28_000), Times(results));
        Assert.Equal(AudioMs, Mp3(results));
    }

    [Fact]
    public void Kept_position_is_not_extrapolated_to_the_audio_length()
    {
        // Dernière lecture à 28 s sur 30 s d'audio : la barre reste à 93,33 %, pas 100 %.
        PlayToResults(out var results);

        var (current, _) = Times(results);

        Assert.Equal("93.33", (current / Mp3(results) * 100).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Long_results_screen_keeps_the_same_frozen_position()
    {
        var seq = PlayToResults(out _);
        LiveAdapterResult r = null!;
        for (int i = 1; i <= 120; i++) // 60 s de résultats
        {
            var at = T0 + TimeSpan.FromSeconds(1.5) + TimeSpan.FromMilliseconds(500 * i);
            r = seq.Step(Screen(at, SourceRoute.Results), at);
        }

        Assert.Equal(FieldState.LastKnown, r.F("time"));
        Assert.Equal((28_000, 28_000), Times(r));
        Assert.Equal(T0 + TimeSpan.FromSeconds(1), r.Snapshot.CurrentTimeMs.ObservedAtUtc);
    }

    [Fact]
    public void Incomplete_results_still_keep_a_valid_position_independently_of_stats()
    {
        // 9 objets jugés sur 10 au dernier tick : les stats ne sont pas verrouillées, la position l'est.
        PlayToResults(out var results, finalJudged: 9);

        Assert.True(results.Has(LiveAdapterEvent.ResultsIncomplete));
        Assert.Equal(FieldState.Unknown, results.F("hits.300"));
        Assert.Equal(FieldState.LastKnown, results.F("time"));
        Assert.Equal((28_000, 28_000), Times(results));
        Assert.NotNull(results.Snapshot.Identity.PlayId);
    }

    [Fact]
    public void Complete_results_keep_stats_and_position_of_the_same_play()
    {
        PlayToResults(out var results);

        Assert.True(results.Has(LiveAdapterEvent.ResultsLatched));
        Assert.Equal(FieldState.LastKnown, results.F("hits.300"));
        Assert.Equal(FieldState.LastKnown, results.F("time"));
    }

    // ------------------------------------------------------------ refus

    [Fact]
    public void No_position_observed_during_the_play_gives_unknown_time()
    {
        var seq = new Sequence();
        // Gameplay sans position ni stats (découverte mémoire non prête), puis résultats.
        var noData = WithAudio(new BeatInsightLiveInput(Game(T0), Route(SourceRoute.Gameplay, T0), Metadata()));
        seq.Step(noData, T0);
        var at = T0 + TimeSpan.FromMilliseconds(500);
        var r = seq.Step(Screen(at, SourceRoute.Results), at);

        Assert.True(r.Has(LiveAdapterEvent.ResultsPositionUnavailable));
        Assert.False(r.Snapshot.CurrentTimeMs.HasValue);
        Assert.Equal((0, 0), Times(r));
    }

    [Fact]
    public void Stale_position_at_transition_is_not_kept()
    {
        var seq = new Sequence();
        seq.Step(Play(T0, 20_000, 5), T0);
        // Plusieurs ticks de gameplay sans position (lecture perdue), puis résultats 2 s après la dernière position.
        for (int i = 1; i <= 4; i++)
        {
            var at = T0 + TimeSpan.FromMilliseconds(500 * i);
            seq.Step(WithAudio(new BeatInsightLiveInput(Game(at), Route(SourceRoute.Gameplay, at), Metadata())), at);
        }
        var resultsAt = T0 + TimeSpan.FromMilliseconds(2_500);
        var r = seq.Step(Screen(resultsAt, SourceRoute.Results), resultsAt);

        Assert.True(r.Has(LiveAdapterEvent.ResultsPositionUnavailable));
        Assert.False(r.Snapshot.CurrentTimeMs.HasValue);
    }

    [Fact]
    public void Negative_position_is_never_kept()
    {
        var seq = new Sequence();
        var input = Play(T0, 5_000, 0);
        // Lead-in : position négative (refusée par la validation live), sans autre source de temps.
        seq.Step(input with { LiveGameplay = Live(T0, -1_500), Stats = null, Performance = null }, T0);
        var at = T0 + TimeSpan.FromMilliseconds(500);
        var r = seq.Step(Screen(at, SourceRoute.Results), at);

        Assert.False(r.Snapshot.CurrentTimeMs.HasValue);
    }

    [Fact]
    public void Results_without_a_tracked_play_never_show_a_position()
    {
        // Song select → résultats (classement local, replay) : aucune partie suivie.
        var seq = new Sequence();
        seq.Step(Screen(T0, SourceRoute.SongSelect), T0);
        var at = T0 + TimeSpan.FromMilliseconds(500);
        var r = seq.Step(Screen(at, SourceRoute.Results), at);

        Assert.True(r.Has(LiveAdapterEvent.ResultsWithoutPlay));
        Assert.Equal(FieldState.NotAvailableOnScreen, r.F("time"));
        Assert.Equal((0, 0), Times(r));
    }

    // ------------------------------------------------------------ réinitialisations

    [Fact]
    public void Results_to_song_select_clears_the_position()
    {
        var seq = PlayToResults(out _);
        var at = T0 + TimeSpan.FromSeconds(2);
        var songSelect = seq.Step(Screen(at, SourceRoute.SongSelect), at);
        var back = seq.Step(Screen(at + TimeSpan.FromMilliseconds(500), SourceRoute.Results), at + TimeSpan.FromMilliseconds(500));

        Assert.Equal((0, 0), Times(songSelect));
        Assert.Null(seq.State.ResultsPosition);
        Assert.False(back.Snapshot.CurrentTimeMs.HasValue);
    }

    [Fact]
    public void Retry_from_results_starts_a_new_play_without_the_old_position()
    {
        var seq = PlayToResults(out var results);
        long oldPlay = results.Snapshot.Identity.PlayId!.Value;
        var at = T0 + TimeSpan.FromSeconds(2);
        var retry = seq.Step(Play(at, 300, 0), at);
        var resultsAt = at + TimeSpan.FromMilliseconds(500);
        // Nouvelle partie interrompue sans position valide : rien de l'ancienne partie n'apparaît.
        var empty = WithAudio(new BeatInsightLiveInput(Game(resultsAt), Route(SourceRoute.Results, resultsAt), Metadata()));
        var r = seq.Step(empty with { LiveGameplay = null }, resultsAt);

        Assert.True(retry.Has(LiveAdapterEvent.NewPlay));
        Assert.NotEqual(oldPlay, retry.Snapshot.Identity.PlayId);
        Assert.Equal(300, Times(retry).V1);
        Assert.Equal(300, Times(r).V1); // position de la nouvelle partie, jamais 28 000
    }

    [Fact]
    public void In_play_retry_resets_the_position_of_the_previous_attempt()
    {
        var seq = new Sequence();
        seq.Step(Play(T0, 20_000, 6), T0);
        var at = T0 + TimeSpan.FromMilliseconds(500);
        var retry = seq.Step(Play(at, 200, 0), at); // temps qui recule : retry

        Assert.True(retry.Has(LiveAdapterEvent.Retry));
        Assert.Equal(200, seq.State.Play!.LastPosition!.TimeMs);
    }

    [Fact]
    public void Beatmap_change_during_results_clears_the_position()
    {
        var seq = PlayToResults(out _);
        var at = T0 + TimeSpan.FromSeconds(2);
        var r = seq.Step(Screen(at, SourceRoute.Results, MapB, "2002") with { Metadata = Metadata(MapB, 2002) }, at);

        Assert.True(r.Has(LiveAdapterEvent.BeatmapChanged));
        Assert.False(r.Snapshot.CurrentTimeMs.HasValue);
        Assert.Equal((0, 0), Times(r));
    }

    [Fact]
    public void Abandon_to_song_select_never_keeps_a_position()
    {
        var seq = new Sequence();
        seq.Step(Play(T0, 12_000, 4), T0);
        var at = T0 + TimeSpan.FromMilliseconds(500);
        var r = seq.Step(Screen(at, SourceRoute.SongSelect), at);

        Assert.Equal(FieldState.NotAvailableOnScreen, r.F("time"));
        Assert.Null(seq.State.ResultsPosition);
    }

    [Fact]
    public void Osu_restart_clears_the_position()
    {
        var seq = PlayToResults(out _);
        var at = T0 + TimeSpan.FromSeconds(2);
        var other = Screen(at, SourceRoute.Results) with
        {
            Game = Game(at, OtherProcess),
            Route = Route(SourceRoute.Results, at, OtherProcess),
        };

        var r = seq.Step(other, at);

        Assert.True(r.Has(LiveAdapterEvent.ProcessChanged));
        Assert.False(r.Snapshot.CurrentTimeMs.HasValue);
    }

    [Fact]
    public void Osu_closed_clears_the_position()
    {
        var seq = PlayToResults(out _);
        var at = T0 + TimeSpan.FromSeconds(2);
        seq.Step(new BeatInsightLiveInput(Game(at, availability: SourceAvailability.NoProcess), null), at);
        var r = seq.Step(Screen(at + TimeSpan.FromMilliseconds(500), SourceRoute.Results), at + TimeSpan.FromMilliseconds(500));

        Assert.Null(seq.State.ResultsPosition);
        Assert.False(r.Snapshot.CurrentTimeMs.HasValue);
    }

    [Fact]
    public void Pipe_disconnection_clears_the_position()
    {
        var seq = PlayToResults(out _);
        var at = T0 + TimeSpan.FromSeconds(2);
        // Source perdue : PipeLiveCommunityOverlayDataSource.Resolve donne une entrée vide.
        var lost = seq.Step(PipeLiveCommunityOverlayDataSource.Resolve(null, at), at);
        var reconnected = seq.Step(Screen(at + TimeSpan.FromMilliseconds(500), SourceRoute.Results), at + TimeSpan.FromMilliseconds(500));

        Assert.True(lost.Has(LiveAdapterEvent.GameUnavailable));
        Assert.Equal((0, 0), Times(lost));
        Assert.False(reconnected.Snapshot.CurrentTimeMs.HasValue);
    }

    [Theory]
    [InlineData(SourceSuspension.OverlayLayoutEdit)]
    [InlineData(SourceSuspension.FastLabeling)]
    [InlineData(SourceSuspension.BackgroundLibraryWork)]
    public void Suspended_source_clears_the_position(SourceSuspension suspension)
    {
        var seq = PlayToResults(out _);
        var at = T0 + TimeSpan.FromSeconds(2);
        var suspended = seq.Step(Screen(at, SourceRoute.Results) with { Suspension = suspension }, at);
        var resumed = seq.Step(Screen(at + TimeSpan.FromMilliseconds(500), SourceRoute.Results), at + TimeSpan.FromMilliseconds(500));

        Assert.False(suspended.Snapshot.CurrentTimeMs.HasValue);
        Assert.False(resumed.Snapshot.CurrentTimeMs.HasValue);
    }

    [Fact]
    public void Next_map_never_shows_the_previous_position()
    {
        var seq = PlayToResults(out _);
        var at = T0 + TimeSpan.FromSeconds(2);
        seq.Step(Screen(at, SourceRoute.SongSelect), at);
        var mapB = Screen(at + TimeSpan.FromMilliseconds(500), SourceRoute.SongSelect, MapB, "2002") with { Metadata = Metadata(MapB, 2002) };
        var r = seq.Step(mapB, at + TimeSpan.FromMilliseconds(500));

        Assert.Equal((0, 0), Times(r));
    }

    // ------------------------------------------------------------ durée audio inconnue

    [Fact]
    public void Unknown_audio_length_keeps_position_but_emits_no_duration()
    {
        var seq = new Sequence();
        seq.Step(WithAudio(GameplayTick(T0, 28_000, TotalObjects), audioMs: null), T0);
        var at = T0 + TimeSpan.FromMilliseconds(500);
        var r = seq.Step(WithAudio(ScreenTick(at, SourceRoute.Results), audioMs: null), at);

        Assert.Equal((28_000, 28_000), Times(r));
        Assert.Equal(0, Mp3(r)); // Simplistic : 28000 / 0 = Infinity, règle CSS ignorée (comportement COM-10)
        Assert.Equal(FieldState.Unknown, r.F("beatmap.audioLength"));
    }
}
