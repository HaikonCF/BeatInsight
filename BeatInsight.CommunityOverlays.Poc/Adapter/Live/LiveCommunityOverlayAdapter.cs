using IOPath = System.IO.Path;

namespace BeatInsight.CommunityOverlays.Poc.Adapter.Live;

/// <summary>État d'une partie suivie par l'adaptateur.</summary>
/// <param name="LastStats">Dernières stats valides de la partie (identité et âge vérifiés).</param>
/// <param name="LastPerformance">Derniers PP cohérents avec <paramref name="LastStats"/>.</param>
/// <param name="MaxPp">Maximum des PP courants valides observés pendant la partie.</param>
/// <param name="LastPosition">Dernière position de jeu réellement observée et valide (V3.14-COM-12).</param>
public sealed record LivePlayState(
    long PlayId,
    double? LastTimeMs,
    int LastJudged,
    SourceLiveStats? LastStats,
    SourcePerformance? LastPerformance,
    double? MaxPp,
    DateTimeOffset? MaxPpAtUtc,
    LivePositionObservation? LastPosition = null,
    SliderBreakState? SliderBreaks = null);

/// <summary>Position de jeu (ms de piste) telle que lue, avec son horodatage et sa provenance d'origine.</summary>
public sealed record LivePositionObservation(double TimeMs, DateTimeOffset ObservedAtUtc, FieldProvenance Provenance);

/// <summary>
/// Position conservée en résultats (V3.14-COM-12) : dernière position observée de la partie terminée,
/// rattachée au process, à la beatmap et à la partie. Indépendante du verrou des statistiques.
/// </summary>
public sealed record LiveResultsPosition(
    long PlayId,
    string ProcessIdentity,
    string? BeatmapIdentity,
    double TimeMs,
    DateTimeOffset ObservedAtUtc,
    FieldProvenance Provenance);

/// <summary>Résultats autorisés : dernière lecture complète d'une partie terminée directement en résultats.</summary>
public sealed record LiveResultsLatch(
    long PlayId,
    SourceLiveStats Stats,
    SourcePerformance? Performance,
    double? MaxPp,
    DateTimeOffset? MaxPpAtUtc,
    SliderBreakState? SliderBreaks = null);

/// <summary>État interne de l'adaptateur, entièrement explicite (aucun état caché).</summary>
public sealed record LiveAdapterState(
    string? ProcessIdentity,
    string? BeatmapIdentity,
    SourceRoute LastRoute,
    long LastPlayId,
    LivePlayState? Play,
    LiveResultsLatch? Results,
    LiveResultsPosition? ResultsPosition = null)
{
    public static LiveAdapterState Initial { get; } = new(null, null, SourceRoute.Unknown, 0, null, null);
}

/// <summary>Événements détectés pendant un tick (diagnostic et tests).</summary>
public enum LiveAdapterEvent
{
    GameUnavailable,
    GameStateStale,
    SourceSuspended,
    ProcessChanged,
    BeatmapChanged,
    RouteUnknown,
    RouteStale,
    MetadataUnavailable,
    NewPlay,
    Retry,
    PlayEnded,
    StatsUnavailable,
    StatsStale,
    StatsIdentityMismatch,
    PerformanceUnavailable,
    PerformanceInconsistent,
    ResultsLatched,
    ResultsIncomplete,
    ResultsWithoutPlay,

    /// <summary>V3.14-COM-12 : dernière position de la partie conservée pour l'écran de résultats.</summary>
    ResultsPositionKept,

    /// <summary>V3.14-COM-12 : aucune position récente et valide à conserver (temps inconnu en résultats).</summary>
    ResultsPositionUnavailable,
}

/// <summary>Diagnostic de disponibilité : état de chaque champ émis et événements du tick.</summary>
public sealed record LiveAdapterDiagnostics(
    IReadOnlyDictionary<string, FieldState> Fields,
    IReadOnlyList<LiveAdapterEvent> Events);

public sealed record LiveAdapterResult(
    CommunityOverlaySnapshot Snapshot,
    LiveAdapterState State,
    LiveAdapterDiagnostics Diagnostics);

/// <summary>
/// Adaptateur pur BeatInsight → contrat overlay v2 (V3.14-COM-09).
/// <c>(entrées BeatInsight, état précédent, maintenant) → (snapshot, nouvel état, diagnostic)</c>.
/// Aucun accès au process osu!, à la mémoire, au système de fichiers, à WebView2,
/// aux readers natifs ni au calculateur PP. Déterministe : même entrée, même sortie.
/// </summary>
public static class LiveCommunityOverlayAdapter
{
    /// <summary>Âge maximal des stats, PP, temps, route et état de jeu (3 ticks BeatInsight de 500 ms).</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(1.5);

    /// <summary>Recul du temps de jeu au-delà duquel on considère un retry.</summary>
    public const double RetryRewindMs = 1_000;

    private static readonly string[] DifficultyMods = ["EZ", "HR", "DT", "NC", "HT", "FL"];

    public static LiveAdapterResult Adapt(BeatInsightLiveInput input, LiveAdapterState previous, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(previous);
        var events = new List<LiveAdapterEvent>();

        // 1. Jeu disponible ? Sinon (osu! fermé, aucun process) : tout est inconnu.
        var game = input.Game;
        if (game is null || game.Availability != SourceAvailability.Available || string.IsNullOrEmpty(game.ProcessIdentity))
        {
            events.Add(LiveAdapterEvent.GameUnavailable);
            if (previous.Play is not null) events.Add(LiveAdapterEvent.PlayEnded);
            return Empty(previous, now, events, processIdentity: null);
        }

        // 2. Changement de process (redémarrage d'osu!) : remise à zéro complète.
        var state = previous;
        if (state.ProcessIdentity is not null && !string.Equals(state.ProcessIdentity, game.ProcessIdentity, StringComparison.Ordinal))
        {
            events.Add(LiveAdapterEvent.ProcessChanged);
            state = Reset(state);
        }

        // 3. Source figée (édition du layout, Fast Labeling, travail de bibliothèque) ou périmée.
        if (input.Suspension != SourceSuspension.None)
        {
            events.Add(LiveAdapterEvent.SourceSuspended);
            return Empty(state, now, events, game.ProcessIdentity);
        }
        if (now - game.LastUpdateUtc > MaxAge)
        {
            events.Add(LiveAdapterEvent.GameStateStale);
            return Empty(state, now, events, game.ProcessIdentity);
        }

        // 4. Changement de beatmap : fin de toute partie et de tout résultat.
        string? beatmapIdentity = NormalizePath(game.CurrentBeatmapPath);
        if (state.BeatmapIdentity is not null && !string.Equals(state.BeatmapIdentity, beatmapIdentity, StringComparison.Ordinal))
        {
            events.Add(LiveAdapterEvent.BeatmapChanged);
            if (state.Play is not null) events.Add(LiveAdapterEvent.PlayEnded);
            state = state with { Play = null, Results = null, ResultsPosition = null, LastRoute = SourceRoute.Unknown };
        }
        state = state with { ProcessIdentity = game.ProcessIdentity, BeatmapIdentity = beatmapIdentity };

        // 5. Route : même process et âge valide, sinon inconnue.
        SourceRoute route = SourceRoute.Unknown;
        if (input.Route is null || input.Route.Route == SourceRoute.Unknown)
            events.Add(LiveAdapterEvent.RouteUnknown);
        else if (!string.Equals(input.Route.ProcessIdentity, game.ProcessIdentity, StringComparison.Ordinal) ||
                 now - input.Route.ObservedAtUtc > MaxAge)
            events.Add(LiveAdapterEvent.RouteStale);
        else
            route = input.Route.Route;

        // 6. Métadonnées : seulement celles de la beatmap sélectionnée.
        var metadata = input.Metadata;
        if (metadata is null ||
            !string.Equals(NormalizePath(metadata.BeatmapPath), beatmapIdentity, StringComparison.Ordinal) ||
            !IdsCompatible(metadata.BeatmapId?.ToString(System.Globalization.CultureInfo.InvariantCulture), game.CurrentBeatmapId))
        {
            if (metadata is not null || beatmapIdentity is not null) events.Add(LiveAdapterEvent.MetadataUnavailable);
            metadata = null;
        }

        var ctx = new Context(input, game, metadata, now, events);
        return route switch
        {
            SourceRoute.Gameplay => Gameplay(ctx, state),
            SourceRoute.Results => Results(ctx, state),
            SourceRoute.SongSelect => SongSelect(ctx, state),
            _ => UnknownRoute(ctx, state),
        };
    }

    private sealed record Context(
        BeatInsightLiveInput Input,
        SourceGameState Game,
        SourceBeatmapMetadata? Metadata,
        DateTimeOffset Now,
        List<LiveAdapterEvent> Events);

    // ---------------------------------------------------------------- écrans

    private static LiveAdapterResult Gameplay(Context ctx, LiveAdapterState state)
    {
        var stats = ValidStats(ctx);
        var live = ValidLive(ctx);
        double? timeMs = live?.CurrentTime.TotalMilliseconds ?? stats?.CurrentTime.TotalMilliseconds;
        int? judged = stats?.JudgedObjects;

        // Nouvelle partie : arrivée en gameplay, ou retry (temps qui recule, compteurs qui baissent).
        var play = state.Play;
        bool newPlay = state.LastRoute != SourceRoute.Gameplay || play is null;
        bool retry = !newPlay && (
            (timeMs is { } t && play!.LastTimeMs is { } last && t < last - RetryRewindMs) ||
            (judged is { } j && j < play!.LastJudged));
        long lastPlayId = state.LastPlayId;
        if (newPlay || retry)
        {
            ctx.Events.Add(newPlay ? LiveAdapterEvent.NewPlay : LiveAdapterEvent.Retry);
            lastPlayId++;
            play = new LivePlayState(lastPlayId, null, 0, null, null, null, null);
        }

        var performance = ValidPerformance(ctx, stats);
        double? maxPp = play!.MaxPp;
        DateTimeOffset? maxPpAt = play.MaxPpAtUtc;
        if (performance is not null && (maxPp is null || performance.CurrentPp >= maxPp))
        {
            maxPp = performance.CurrentPp;
            maxPpAt = performance.ObservedAtUtc;
        }
        play = play with
        {
            LastTimeMs = timeMs ?? play.LastTimeMs,
            LastJudged = judged ?? play.LastJudged,
            LastStats = stats ?? play.LastStats,
            LastPerformance = stats is null ? play.LastPerformance : performance,
            MaxPp = maxPp,
            MaxPpAtUtc = maxPpAt,
        };

        // V3.14-COM-SB-01 : slider breaks estimés (logique tosu), état propre à la partie (remis à zéro avec
        // elle : nouvelle partie, retry, changement de map ou de process). Uniquement sur des stats valides.
        if (stats is not null)
        {
            play = play with
            {
                SliderBreaks = SliderBreakEstimator.Step(
                    play.SliderBreaks ?? SliderBreakState.Initial,
                    stats.CurrentCombo, stats.MaxComboReached, stats.CountMiss, stats.ObservedAtUtc),
            };
        }

        var time = live is not null
            ? FieldValue<double>.Observed(live.CurrentTime.TotalMilliseconds, live.ObservedAtUtc, FieldProvenance.GameplayPosition)
            : stats is not null
                ? FieldValue<double>.Observed(stats.CurrentTime.TotalMilliseconds, stats.ObservedAtUtc, FieldProvenance.LiveStats)
                : FieldValue<double>.Unknown;

        // V3.14-COM-12 : mémorise la dernière position réellement lue (jamais une valeur déduite).
        if (time.HasValue && double.IsFinite(time.Value) && time.Value >= 0 && time.ObservedAtUtc is { } timeAt)
            play = play with { LastPosition = new LivePositionObservation(time.Value, timeAt, time.Provenance) };

        var mods = stats is null
            ? FieldValue<IReadOnlyList<string>>.Unknown
            : FieldValue<IReadOnlyList<string>>.Observed(stats.Mods, stats.ObservedAtUtc, FieldProvenance.LiveStats);

        var snapshot = Build(ctx, CommunityOverlayRoute.Gameplay, play.PlayId, mods) with
        {
            CurrentTimeMs = time,
        };
        snapshot = WithStats(snapshot, stats, lastKnown: false);
        if (stats is not null)
            snapshot = WithSliderBreaks(snapshot, play.SliderBreaks, lastKnown: false);
        snapshot = WithPerformance(snapshot, performance, maxPp, maxPpAt, lastKnown: false);

        var next = state with { LastRoute = SourceRoute.Gameplay, LastPlayId = lastPlayId, Play = play, Results = null, ResultsPosition = null };
        return Result(snapshot, next, ctx.Events);
    }

    private static LiveAdapterResult Results(Context ctx, LiveAdapterState state)
    {
        LiveResultsLatch? latch = state.Results;
        LiveResultsPosition? position = state.ResultsPosition;
        if (state.LastRoute == SourceRoute.Gameplay)
        {
            latch = TryLatch(state.Play, ctx.Metadata);
            ctx.Events.Add(latch is null ? LiveAdapterEvent.ResultsIncomplete : LiveAdapterEvent.ResultsLatched);
            position = TryKeepPosition(ctx, state);
            ctx.Events.Add(position is null ? LiveAdapterEvent.ResultsPositionUnavailable : LiveAdapterEvent.ResultsPositionKept);
        }
        else if (state.LastRoute != SourceRoute.Results)
        {
            // Résultats ouverts sans partie suivie (classement local, replay depuis le song select…).
            latch = null;
            position = null;
            ctx.Events.Add(LiveAdapterEvent.ResultsWithoutPlay);
        }

        // Défense en profondeur : la position n'appartient qu'au process et à la beatmap de sa partie.
        if (position is not null &&
            (!string.Equals(position.ProcessIdentity, ctx.Game.ProcessIdentity, StringComparison.Ordinal) ||
             !string.Equals(position.BeatmapIdentity, state.BeatmapIdentity, StringComparison.Ordinal)))
            position = null;

        // V3.14-COM-12 : dernière position réellement observée, figée (aucune progression, aucune extrapolation).
        var time = position is not null
            ? FieldValue<double>.LastKnown(position.TimeMs, position.ObservedAtUtc, position.Provenance)
            : FieldValue<double>.NotAvailableOnScreen;

        CommunityOverlaySnapshot snapshot;
        if (latch is null)
        {
            snapshot = Build(ctx, CommunityOverlayRoute.Results, position?.PlayId, FieldValue<IReadOnlyList<string>>.Unknown) with
            {
                CurrentTimeMs = time,
            };
        }
        else
        {
            var mods = FieldValue<IReadOnlyList<string>>.LastKnown(latch.Stats.Mods, latch.Stats.ObservedAtUtc, FieldProvenance.LiveStats);
            snapshot = Build(ctx, CommunityOverlayRoute.Results, latch.PlayId, mods) with
            {
                CurrentTimeMs = position?.PlayId == latch.PlayId ? time : FieldValue<double>.NotAvailableOnScreen,
            };
            snapshot = WithStats(snapshot, latch.Stats, lastKnown: true);
            snapshot = WithSliderBreaks(snapshot, latch.SliderBreaks, lastKnown: true);
            snapshot = WithPerformance(snapshot, latch.Performance, latch.MaxPp, latch.MaxPpAtUtc, lastKnown: true);
        }

        var next = state with { LastRoute = SourceRoute.Results, Play = null, Results = latch, ResultsPosition = position };
        return Result(snapshot, next, ctx.Events);
    }

    private static LiveAdapterResult SongSelect(Context ctx, LiveAdapterState state)
    {
        if (state.Play is not null) ctx.Events.Add(LiveAdapterEvent.PlayEnded);

        // Aucune donnée de partie en song select. Les mods existent mais ne sont pas lus :
        // inconnus. Les PP song select (SS moddé) ne sont pas calculés par BeatInsight : inconnus.
        var snapshot = Build(ctx, CommunityOverlayRoute.SongSelect, null, FieldValue<IReadOnlyList<string>>.Unknown) with
        {
            CurrentTimeMs = FieldValue<double>.NotAvailableOnScreen,
            Score = FieldValue<long>.NotAvailableOnScreen,
            Combo = FieldValue<int>.NotAvailableOnScreen,
            MaxCombo = FieldValue<int>.NotAvailableOnScreen,
            Accuracy = FieldValue<double>.NotAvailableOnScreen,
            Grade = FieldValue<string>.NotAvailableOnScreen,
            Hits = CommunityOverlayHits.NotAvailableOnScreen,
            HealthBar = FieldValue<double>.NotAvailableOnScreen,
            Pp = CommunityOverlayPerformance.Unknown with
            {
                Current = FieldValue<double>.NotAvailableOnScreen,
                IfFullCombo = FieldValue<double>.NotAvailableOnScreen,
                MaxAchieved = FieldValue<double>.NotAvailableOnScreen,
            },
        };

        var next = state with { LastRoute = SourceRoute.SongSelect, Play = null, Results = null, ResultsPosition = null };
        return Result(snapshot, next, ctx.Events);
    }

    private static LiveAdapterResult UnknownRoute(Context ctx, LiveAdapterState state)
    {
        if (state.Play is not null) ctx.Events.Add(LiveAdapterEvent.PlayEnded);
        var snapshot = Build(ctx, CommunityOverlayRoute.Unknown, null, FieldValue<IReadOnlyList<string>>.Unknown);
        var next = state with { LastRoute = SourceRoute.Unknown, Play = null, Results = null, ResultsPosition = null };
        return Result(snapshot, next, ctx.Events);
    }

    // ---------------------------------------------------------------- validations

    private static SourceLiveStats? ValidStats(Context ctx)
    {
        var stats = ctx.Input.Stats;
        if (stats is null)
        {
            ctx.Events.Add(LiveAdapterEvent.StatsUnavailable);
            return null;
        }
        if (!string.Equals(stats.ProcessIdentity, ctx.Game.ProcessIdentity, StringComparison.Ordinal) ||
            !IdsCompatible(stats.BeatmapId, ctx.Game.CurrentBeatmapId) ||
            !IdsCompatible(stats.BeatmapSetId, ctx.Game.CurrentBeatmapSetId))
        {
            ctx.Events.Add(LiveAdapterEvent.StatsIdentityMismatch);
            return null;
        }
        if (ctx.Now - stats.ObservedAtUtc > MaxAge)
        {
            ctx.Events.Add(LiveAdapterEvent.StatsStale);
            return null;
        }
        return stats;
    }

    private static SourceLiveGameplay? ValidLive(Context ctx)
    {
        var live = ctx.Input.LiveGameplay;
        if (live is null || !live.IsGameplay) return null;
        if (!string.Equals(live.ProcessIdentity, ctx.Game.ProcessIdentity, StringComparison.Ordinal) ||
            !IdsCompatible(live.BeatmapId, ctx.Game.CurrentBeatmapId) ||
            ctx.Now - live.ObservedAtUtc > MaxAge ||
            live.CurrentTime < TimeSpan.Zero)
            return null;
        return live;
    }

    /// <summary>
    /// PP utilisables seulement s'ils correspondent aux stats du même tick :
    /// même nombre d'objets jugés (le calculateur indexe par jugements) et lectures rapprochées.
    /// </summary>
    private static SourcePerformance? ValidPerformance(Context ctx, SourceLiveStats? stats)
    {
        var performance = ctx.Input.Performance;
        if (performance is null)
        {
            ctx.Events.Add(LiveAdapterEvent.PerformanceUnavailable);
            return null;
        }
        if (stats is null ||
            ctx.Now - performance.ObservedAtUtc > MaxAge ||
            (performance.ObservedAtUtc - stats.ObservedAtUtc).Duration() > MaxAge ||
            performance.PassedObjects != stats.JudgedObjects ||
            !double.IsFinite(performance.CurrentPp) || !double.IsFinite(performance.FcPp))
        {
            ctx.Events.Add(LiveAdapterEvent.PerformanceInconsistent);
            return null;
        }
        return performance;
    }

    /// <summary>
    /// Résultats autorisés uniquement si la dernière lecture de la partie couvre tous les objets
    /// de la beatmap : sinon la lecture à 2 Hz peut précéder les derniers jugements.
    /// </summary>
    private static LiveResultsLatch? TryLatch(LivePlayState? play, SourceBeatmapMetadata? metadata)
    {
        if (play?.LastStats is not { } stats || metadata is null || metadata.TotalObjects <= 0)
            return null;
        if (stats.JudgedObjects != metadata.TotalObjects)
            return null;
        var performance = play.LastPerformance is { } p && p.PassedObjects == stats.JudgedObjects ? p : null;
        return new LiveResultsLatch(play.PlayId, stats, performance, play.MaxPp, play.MaxPpAtUtc, play.SliderBreaks);
    }

    /// <summary>
    /// V3.14-COM-12 : position à conserver au passage direct gameplay → résultats. Seulement la dernière
    /// position réellement lue de la partie qui se termine, positive, finie et encore récente (âge ≤
    /// <see cref="MaxAge"/> au moment de la transition). Jamais la durée totale, jamais une valeur corrigée.
    /// </summary>
    private static LiveResultsPosition? TryKeepPosition(Context ctx, LiveAdapterState state)
    {
        if (state.Play?.LastPosition is not { } p || string.IsNullOrEmpty(ctx.Game.ProcessIdentity))
            return null;
        if (!double.IsFinite(p.TimeMs) || p.TimeMs < 0 || ctx.Now - p.ObservedAtUtc > MaxAge || p.ObservedAtUtc > ctx.Now + MaxAge)
            return null;
        return new LiveResultsPosition(state.Play.PlayId, ctx.Game.ProcessIdentity, state.BeatmapIdentity, p.TimeMs, p.ObservedAtUtc, p.Provenance);
    }

    // ---------------------------------------------------------------- construction

    private static CommunityOverlaySnapshot Build(
        Context ctx, CommunityOverlayRoute route, long? playId, FieldValue<IReadOnlyList<string>> mods)
    {
        var game = ctx.Game;
        return new CommunityOverlaySnapshot(
            Source: CommunityOverlaySourceKind.Live,
            Route: route,
            SnapshotAtUtc: ctx.Now,
            Identity: new CommunityOverlayIdentity(game.ProcessIdentity, NormalizePath(game.CurrentBeatmapPath), playId),
            Client: Client(game),
            PlayerName: FieldValue<string>.Unknown,
            Beatmap: BuildBeatmap(game, ctx.Metadata, ValidOsuFile(ctx), mods, ValidAudioLength(ctx)),
            CurrentTimeMs: FieldValue<double>.Unknown,
            Score: FieldValue<long>.Unknown,
            Combo: FieldValue<int>.Unknown,
            MaxCombo: FieldValue<int>.Unknown,
            Accuracy: FieldValue<double>.Unknown,
            Grade: FieldValue<string>.Unknown,
            GradeMaxThisPlay: FieldValue<string>.Unknown,
            Hits: CommunityOverlayHits.Unknown,
            Mods: mods,
            HealthBar: FieldValue<double>.Unknown,
            Pp: CommunityOverlayPerformance.Unknown);
    }

    private static CommunityOverlaySnapshot WithStats(CommunityOverlaySnapshot s, SourceLiveStats? stats, bool lastKnown)
    {
        if (stats is null) return s;
        var at = stats.ObservedAtUtc;
        FieldValue<T> F<T>(T value) => lastKnown
            ? FieldValue<T>.LastKnown(value, at, FieldProvenance.LiveStats)
            : FieldValue<T>.Observed(value, at, FieldProvenance.LiveStats);
        FieldValue<string> G(string value) => lastKnown
            ? FieldValue<string>.LastKnown(value, at, FieldProvenance.Derived)
            : FieldValue<string>.Observed(value, at, FieldProvenance.Derived);

        int judged = stats.JudgedObjects;
        return s with
        {
            Score = stats.Score is { } score ? F((long)score) : FieldValue<long>.Unknown,
            Combo = F(stats.CurrentCombo),
            MaxCombo = F(stats.MaxComboReached),
            // Avant le premier objet jugé, la précision n'a pas de sens (tosu affiche 100) : inconnue.
            Accuracy = judged > 0 ? F(Math.Round(stats.Accuracy * 100, 2)) : FieldValue<double>.Unknown,
            Grade = judged > 0 ? G(GradeFor(stats)) : FieldValue<string>.Unknown,
            Hits = new CommunityOverlayHits(
                F(stats.Count300), F(stats.Count100), F(stats.Count50), F(stats.CountMiss),
                Geki: FieldValue<int>.Unknown, Katu: FieldValue<int>.Unknown, SliderBreaks: FieldValue<int>.Unknown),
        };
    }

    /// <summary>
    /// Slider breaks estimés : provenance <see cref="FieldProvenance.Estimated"/>, jamais présentés comme une
    /// lecture native. Sans observation valide dans la partie, le champ reste Unknown (valeur neutre tosu : 0).
    /// </summary>
    private static CommunityOverlaySnapshot WithSliderBreaks(CommunityOverlaySnapshot s, SliderBreakState? state, bool lastKnown)
    {
        if (state?.LastObservedAtUtc is not { } at)
            return s;
        var value = lastKnown
            ? FieldValue<int>.LastKnown(state.Count, at, FieldProvenance.Estimated)
            : FieldValue<int>.Observed(state.Count, at, FieldProvenance.Estimated);
        return s with { Hits = s.Hits with { SliderBreaks = value } };
    }

    private static CommunityOverlaySnapshot WithPerformance(
        CommunityOverlaySnapshot s, SourcePerformance? performance, double? maxPp, DateTimeOffset? maxPpAt, bool lastKnown)
    {
        FieldValue<double> F(double value, DateTimeOffset? at, FieldProvenance provenance) => lastKnown
            ? FieldValue<double>.LastKnown(value, at, provenance)
            : FieldValue<double>.Observed(value, at, provenance);

        var max = maxPp is { } m ? F(m, maxPpAt, FieldProvenance.Derived) : FieldValue<double>.Unknown;
        if (performance is null)
            return s with { Pp = CommunityOverlayPerformance.Unknown with { MaxAchieved = max } };

        var at = performance.ObservedAtUtc;
        return s with
        {
            Pp = new CommunityOverlayPerformance(
                F(performance.CurrentPp, at, FieldProvenance.LivePerformance),
                F(performance.FcPp, at, FieldProvenance.LivePerformance),
                max,
                new Dictionary<int, FieldValue<double>>
                {
                    [95] = F(performance.Accuracy95FcPp, at, FieldProvenance.LivePerformance),
                    [100] = F(performance.SsPp, at, FieldProvenance.LivePerformance),
                }),
        };
    }

    /// <summary>Fichier .osu de la beatmap sélectionnée uniquement (même chemin normalisé).</summary>
    private static SourceOsuFile? ValidOsuFile(Context ctx)
    {
        var osu = ctx.Input.OsuFile;
        return osu is not null &&
               string.Equals(NormalizePath(osu.BeatmapPath), NormalizePath(ctx.Game.CurrentBeatmapPath), StringComparison.Ordinal)
            ? osu
            : null;
    }

    /// <summary>
    /// Durée audio (V3.14-COM-11) : seulement celle du fichier <c>AudioFilename</c> du .osu de la beatmap
    /// sélectionnée, lue dans le fichier. Durée de piste en ms, indépendante des mods de vitesse
    /// (même repère que le temps de jeu, comme <c>mp3Length</c> de tosu). Sinon inconnue.
    /// </summary>
    private static FieldValue<double> ValidAudioLength(Context ctx)
    {
        var osu = ValidOsuFile(ctx);
        var audio = ctx.Input.AudioFile;
        if (osu is null || audio is null || audio.DurationMs is not { } duration ||
            !double.IsFinite(duration) || duration <= 0 ||
            !string.Equals(NormalizePath(audio.BeatmapPath), NormalizePath(osu.BeatmapPath), StringComparison.Ordinal) ||
            !string.Equals(audio.AudioFilename, osu.AudioFilename, StringComparison.Ordinal))
            return FieldValue<double>.Unknown;
        return FieldValue<double>.Observed(duration, null, FieldProvenance.AudioFile);
    }

    private static CommunityOverlayBeatmap? BuildBeatmap(
        SourceGameState game, SourceBeatmapMetadata? metadata, SourceOsuFile? osu, FieldValue<IReadOnlyList<string>> mods,
        FieldValue<double> audioLength)
    {
        if (string.IsNullOrEmpty(game.CurrentBeatmapPath))
            return null;

        var at = game.LastUpdateUtc;
        var files = BuildFiles(game, osu?.AudioFilename);
        FieldValue<string> O(string? value) => string.IsNullOrEmpty(value)
            ? FieldValue<string>.Unknown
            : FieldValue<string>.Observed(value, null, FieldProvenance.OsuFile);
        if (metadata is null)
        {
            // Beatmap sélectionnée connue, analyse pas encore publiée : seuls ids et fichiers.
            return Unknown() with
            {
                BeatmapId = ParseId(game.CurrentBeatmapId, at),
                BeatmapSetId = ParseId(game.CurrentBeatmapSetId, at),
                Checksum = O(osu?.Md5),
                ArtistUnicode = O(osu?.ArtistUnicode),
                TitleUnicode = O(osu?.TitleUnicode),
                AudioLengthMs = audioLength,
                Files = files,
            };
        }

        FieldValue<T> A<T>(T value) => FieldValue<T>.Observed(value, null, FieldProvenance.Analysis);
        FieldValue<int> OptionalId(int? id) => id is { } v && v > 0 ? A(v) : FieldValue<int>.Unknown;

        bool modsKnown = mods.HasValue;
        IReadOnlyList<string> modList = modsKnown ? mods.Value : [];
        bool difficultyMods = modList.Any(m => DifficultyMods.Contains(m, StringComparer.OrdinalIgnoreCase));
        double rate = modList.Contains("DT") || modList.Contains("NC") ? 1.5 : modList.Contains("HT") ? 0.75 : 1.0;

        // Étoiles NoMod : valides si aucun mod de difficulté n'est connu. Avec DT/HR… actifs,
        // la valeur NoMod est fausse et BeatInsight n'expose pas les étoiles moddées : inconnue.
        var stars = modsKnown && difficultyMods ? FieldValue<double>.Unknown : A(metadata.OsuStarRating);

        CommunityOverlayDifficulty Difficulty(double original, Func<double, IReadOnlyList<string>, double, double> convert) =>
            new(A(original), modsKnown
                ? FieldValue<double>.Observed(Math.Round(convert(original, modList, rate), 2), mods.ObservedAtUtc, FieldProvenance.Derived)
                : A(original));

        double primaryBpm = metadata.PrimaryBpm ?? metadata.Bpm;
        FieldValue<double> Bpm(double value) => modsKnown && rate != 1.0
            ? FieldValue<double>.Observed(Math.Round(value * rate, 2), mods.ObservedAtUtc, FieldProvenance.Derived)
            : A(value);

        return new CommunityOverlayBeatmap(
            BeatmapId: OptionalId(metadata.BeatmapId),
            BeatmapSetId: OptionalId(metadata.BeatmapSetId),
            Checksum: O(osu?.Md5),
            Artist: A(metadata.Artist),
            ArtistUnicode: O(osu?.ArtistUnicode),
            Title: A(metadata.Title),
            TitleUnicode: O(osu?.TitleUnicode),
            Mapper: A(metadata.Creator),
            Version: A(metadata.Version),
            Stars: stars,
            Ar: Difficulty(metadata.ApproachRate, DifficultyMath.ApproachRate),
            Cs: Difficulty(metadata.CircleSize, DifficultyMath.CircleSize),
            Od: Difficulty(metadata.OverallDifficulty, DifficultyMath.OverallDifficulty),
            Hp: Difficulty(metadata.HpDrain, DifficultyMath.HpDrain),
            BpmCommon: Bpm(primaryBpm),
            BpmMin: Bpm(metadata.MinBpm ?? primaryBpm),
            BpmMax: Bpm(metadata.MaxBpm ?? primaryBpm),
            BpmRealtime: FieldValue<double>.Unknown,
            MaxCombo: A(metadata.OsuMaxCombo ?? metadata.MaxCombo),
            Circles: A(metadata.CircleCount),
            Sliders: A(metadata.SliderCount),
            Spinners: A(metadata.SpinnerCount),
            FirstObjectMs: FieldValue<double>.Unknown,
            LastObjectMs: FieldValue<double>.Unknown,
            AudioLengthMs: audioLength)
        {
            Files = files,
        };

        static CommunityOverlayBeatmap Unknown() => new(
            FieldValue<int>.Unknown, FieldValue<int>.Unknown, FieldValue<string>.Unknown,
            FieldValue<string>.Unknown, FieldValue<string>.Unknown, FieldValue<string>.Unknown,
            FieldValue<string>.Unknown, FieldValue<string>.Unknown, FieldValue<string>.Unknown,
            FieldValue<double>.Unknown, CommunityOverlayDifficulty.Unknown, CommunityOverlayDifficulty.Unknown,
            CommunityOverlayDifficulty.Unknown, CommunityOverlayDifficulty.Unknown,
            FieldValue<double>.Unknown, FieldValue<double>.Unknown, FieldValue<double>.Unknown,
            FieldValue<double>.Unknown, FieldValue<int>.Unknown, FieldValue<int>.Unknown,
            FieldValue<int>.Unknown, FieldValue<int>.Unknown, FieldValue<double>.Unknown,
            FieldValue<double>.Unknown, FieldValue<double>.Unknown);
    }

    /// <summary>
    /// Chemins relatifs, calculés par manipulation de chaînes uniquement (aucun accès disque).
    /// Toute sortie du dossier Songs ou du dossier de la beatmap est refusée.
    /// </summary>
    public static CommunityOverlayBeatmapFiles? BuildFiles(SourceGameState game, string? audioFilename = null)
    {
        if (string.IsNullOrEmpty(game.SongsDirectory) || string.IsNullOrEmpty(game.CurrentBeatmapPath))
            return null;
        if (!IOPath.IsPathFullyQualified(game.SongsDirectory) || !IOPath.IsPathFullyQualified(game.CurrentBeatmapPath))
            return null;

        string? beatmapFolder = IOPath.GetDirectoryName(game.CurrentBeatmapPath);
        if (beatmapFolder is null) return null;
        string? folder = SafeRelative(game.SongsDirectory, beatmapFolder);
        if (folder is null || folder == ".") return null;

        string? background = null;
        if (!string.IsNullOrEmpty(game.CurrentBackgroundPath) && IOPath.IsPathFullyQualified(game.CurrentBackgroundPath))
        {
            string? relative = SafeRelative(beatmapFolder, game.CurrentBackgroundPath!);
            if (relative is not null && relative != ".") background = relative;
        }

        // Nom audio lu dans le .osu courant : simple nom de fichier du dossier de la beatmap, sinon refusé.
        string? audio = audioFilename is { Length: > 0 and <= 255 } name &&
                        name.IndexOfAny(['/', '\\', ':']) < 0 && name is not "." and not ".." &&
                        SafeRelative(beatmapFolder, IOPath.Combine(beatmapFolder, name)) == name
            ? name
            : null;
        return new CommunityOverlayBeatmapFiles(folder, IOPath.GetFileName(game.CurrentBeatmapPath), background, audio);
    }

    private static string? SafeRelative(string root, string path)
    {
        string relative = IOPath.GetRelativePath(root, path);
        if (IOPath.IsPathRooted(relative) ||
            relative == ".." ||
            relative.StartsWith(".." + IOPath.DirectorySeparatorChar, StringComparison.Ordinal) ||
            relative.StartsWith("../", StringComparison.Ordinal))
            return null;
        return relative;
    }

    // ---------------------------------------------------------------- utilitaires

    private static FieldValue<string> Client(SourceGameState game)
    {
        string name = $"{game.ClientName} {game.ProviderName}";
        if (name.Contains("lazer", StringComparison.OrdinalIgnoreCase))
            return FieldValue<string>.Observed("lazer", game.LastUpdateUtc, FieldProvenance.GameState);
        if (name.Contains("stable", StringComparison.OrdinalIgnoreCase))
            return FieldValue<string>.Observed("stable", game.LastUpdateUtc, FieldProvenance.GameState);
        return FieldValue<string>.Unknown;
    }

    /// <summary>Note osu!standard (règles stable), au format neutre SS/S/A/B/C/D.</summary>
    public static string GradeFor(SourceLiveStats stats)
    {
        double total = stats.JudgedObjects;
        if (total <= 0) return "";
        double r300 = stats.Count300 / total;
        double r50 = stats.Count50 / total;
        bool noMiss = stats.CountMiss == 0;
        if (r300 >= 1) return "SS";
        if (r300 > 0.9 && r50 <= 0.01 && noMiss) return "S";
        if ((r300 > 0.8 && noMiss) || r300 > 0.9) return "A";
        if ((r300 > 0.7 && noMiss) || r300 > 0.8) return "B";
        if (r300 > 0.6) return "C";
        return "D";
    }

    private static FieldValue<int> ParseId(string? id, DateTimeOffset at) =>
        int.TryParse(id, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int value) && value > 0
            ? FieldValue<int>.Observed(value, at, FieldProvenance.GameState)
            : FieldValue<int>.Unknown;

    private static bool IdsCompatible(string? a, string? b) =>
        string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || string.Equals(a, b, StringComparison.Ordinal);

    internal static string? NormalizePath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : path.Replace('/', '\\').ToUpperInvariant();

    private static LiveAdapterState Reset(LiveAdapterState state) =>
        LiveAdapterState.Initial with { LastPlayId = state.LastPlayId };

    /// <summary>Snapshot sans aucune donnée : jeu absent, source figée ou périmée.</summary>
    private static LiveAdapterResult Empty(
        LiveAdapterState state, DateTimeOffset now, List<LiveAdapterEvent> events, string? processIdentity)
    {
        var snapshot = new CommunityOverlaySnapshot(
            Source: CommunityOverlaySourceKind.Live,
            Route: CommunityOverlayRoute.Unknown,
            SnapshotAtUtc: now,
            Identity: new CommunityOverlayIdentity(processIdentity, null, null),
            Client: FieldValue<string>.Unknown,
            PlayerName: FieldValue<string>.Unknown,
            Beatmap: null,
            CurrentTimeMs: FieldValue<double>.Unknown,
            Score: FieldValue<long>.Unknown,
            Combo: FieldValue<int>.Unknown,
            MaxCombo: FieldValue<int>.Unknown,
            Accuracy: FieldValue<double>.Unknown,
            Grade: FieldValue<string>.Unknown,
            GradeMaxThisPlay: FieldValue<string>.Unknown,
            Hits: CommunityOverlayHits.Unknown,
            Mods: FieldValue<IReadOnlyList<string>>.Unknown,
            HealthBar: FieldValue<double>.Unknown,
            Pp: CommunityOverlayPerformance.Unknown);

        // La partie en cours est abandonnée : une source figée ne prouve pas la continuité.
        var next = processIdentity is null
            ? Reset(state)
            : state with { ProcessIdentity = processIdentity, LastRoute = SourceRoute.Unknown, Play = null, Results = null, ResultsPosition = null };
        return Result(snapshot, next, events);
    }

    private static LiveAdapterResult Result(CommunityOverlaySnapshot snapshot, LiveAdapterState state, List<LiveAdapterEvent> events) =>
        new(snapshot, state, new LiveAdapterDiagnostics(FieldStates(snapshot), events.Distinct().ToArray()));

    /// <summary>État de chaque champ émis, sous des noms stables (utilisés par les tests et le debug).</summary>
    public static IReadOnlyDictionary<string, FieldState> FieldStates(CommunityOverlaySnapshot s)
    {
        var map = s.Beatmap;
        var fields = new SortedDictionary<string, FieldState>(StringComparer.Ordinal)
        {
            ["client"] = s.Client.State,
            ["playerName"] = s.PlayerName.State,
            ["time"] = s.CurrentTimeMs.State,
            ["score"] = s.Score.State,
            ["combo"] = s.Combo.State,
            ["maxCombo"] = s.MaxCombo.State,
            ["accuracy"] = s.Accuracy.State,
            ["grade"] = s.Grade.State,
            ["gradeMaxThisPlay"] = s.GradeMaxThisPlay.State,
            ["mods"] = s.Mods.State,
            ["healthBar"] = s.HealthBar.State,
            ["keys"] = s.Keys.State,
            ["hits.300"] = s.Hits.Great.State,
            ["hits.100"] = s.Hits.Ok.State,
            ["hits.50"] = s.Hits.Meh.State,
            ["hits.0"] = s.Hits.Miss.State,
            ["hits.geki"] = s.Hits.Geki.State,
            ["hits.katu"] = s.Hits.Katu.State,
            ["hits.sliderBreaks"] = s.Hits.SliderBreaks.State,
            ["pp.current"] = s.Pp.Current.State,
            ["pp.fc"] = s.Pp.IfFullCombo.State,
            ["pp.maxAchieved"] = s.Pp.MaxAchieved.State,
            ["pp.ss"] = s.Pp.ByAccuracy.TryGetValue(100, out var ss) ? ss.State : FieldState.Unknown,
            ["pp.95"] = s.Pp.ByAccuracy.TryGetValue(95, out var p95) ? p95.State : FieldState.Unknown,
            ["beatmap.id"] = map?.BeatmapId.State ?? FieldState.Unknown,
            ["beatmap.title"] = map?.Title.State ?? FieldState.Unknown,
            ["beatmap.stars"] = map?.Stars.State ?? FieldState.Unknown,
            ["beatmap.ar.converted"] = map?.Ar.Converted.State ?? FieldState.Unknown,
            ["beatmap.bpm.common"] = map?.BpmCommon.State ?? FieldState.Unknown,
            ["beatmap.bpm.realtime"] = map?.BpmRealtime.State ?? FieldState.Unknown,
            ["beatmap.checksum"] = map?.Checksum.State ?? FieldState.Unknown,
            ["beatmap.audioLength"] = map?.AudioLengthMs.State ?? FieldState.Unknown,
        };
        return fields;
    }
}

/// <summary>Conversions osu! stable des paramètres de difficulté selon les mods (formules publiques).</summary>
public static class DifficultyMath
{
    private static double Scale(double value, IReadOnlyList<string> mods, double hardRock)
    {
        if (mods.Contains("EZ")) value *= 0.5;
        if (mods.Contains("HR")) value = Math.Min(10, value * hardRock);
        return value;
    }

    public static double CircleSize(double cs, IReadOnlyList<string> mods, double rate) => Scale(cs, mods, 1.3);

    public static double HpDrain(double hp, IReadOnlyList<string> mods, double rate) => Scale(hp, mods, 1.4);

    public static double ApproachRate(double ar, IReadOnlyList<string> mods, double rate)
    {
        ar = Scale(ar, mods, 1.4);
        if (rate == 1.0) return ar;
        double ms = ar <= 5 ? 1800 - 120 * ar : 1200 - 150 * (ar - 5);
        ms /= rate;
        return ms > 1200 ? (1800 - ms) / 120 : 5 + (1200 - ms) / 150;
    }

    public static double OverallDifficulty(double od, IReadOnlyList<string> mods, double rate)
    {
        od = Scale(od, mods, 1.4);
        if (rate == 1.0) return od;
        double ms = (80 - 6 * od) / rate;
        return (80 - ms) / 6;
    }
}
