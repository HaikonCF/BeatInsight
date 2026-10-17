using BeatInsight.Integration.CommunityOverlays.Pipe;
using BeatInsight.Integration.Game;
using BeatInsight.Overlay;

namespace BeatInsight.Integration.CommunityOverlays;

/// <summary>
/// Ce que BeatInsight expose déjà à un instant donné, capturé sans aucune lecture native :
/// <c>OverlayEngine.CurrentSnapshot</c>, <c>GameStateService.Current</c> et les drapeaux
/// qui figent la boucle 500 ms (édition du layout, Fast Labeling, travail de bibliothèque).
/// </summary>
public sealed record CommunityOverlaySourceFrame(
    OverlaySnapshot? Overlay,
    GameStateSnapshot Game,
    bool OverlayEditMode,
    bool FastLabeling,
    bool BackgroundLibraryWork);

/// <summary>
/// Recopie pure des snapshots BeatInsight vers le DTO du pipe (V3.14-COM-10).
/// Aucune valeur n'est recalculée ni complétée : un bloc absent reste absent,
/// les horodatages et identités d'origine sont conservés tels quels.
/// </summary>
public static class CommunityOverlayPipeSnapshotMapper
{
    public static PipeEnvelope Map(CommunityOverlaySourceFrame frame, PipeOsuFile? osuFile, long sequence, DateTimeOffset sentAtUtc,
        PipeAudioFile? audio = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var game = frame.Game;
        var overlay = frame.Overlay;

        // Les éléments de l'OverlaySnapshot ne valent que pour le process qui les a produits.
        bool overlayMatchesProcess = overlay is not null &&
            !string.IsNullOrEmpty(game.ProcessIdentity) &&
            string.Equals(overlay.Game.ProcessIdentity, game.ProcessIdentity, StringComparison.Ordinal);

        PipeGameRoute? route = null;
        PipeBeatmapMetadata? metadata = null;
        PipeLiveGameplay? live = null;
        PipeLiveStats? stats = null;
        PipePerformance? performance = null;
        if (overlayMatchesProcess)
        {
            // GameMode est lu au même tick que overlay.Game (ReadGameRoute dans la boucle 500 ms) :
            // son horodatage est donc celui de ce GameStateSnapshot.
            route = new PipeGameRoute(Route(overlay!.GameMode), overlay.Game.LastUpdateUtc, overlay.Game.ProcessIdentity);
            metadata = Metadata(overlay);
            live = Live(overlay.LiveGameplay);
            stats = Stats(overlay.LiveStats);
            performance = Performance(overlay.Performance);
        }

        bool osuMatches = osuFile is not null &&
            string.Equals(osuFile.BeatmapPath, game.CurrentBeatmapPath, StringComparison.OrdinalIgnoreCase);
        var snapshot = new PipeSnapshot(
            Game(game),
            route,
            metadata,
            osuMatches ? osuFile : null,
            live,
            stats,
            performance,
            Suspension(frame),
            // Durée audio : seulement celle du fichier AudioFilename de ce même .osu.
            osuMatches && audio is not null &&
            string.Equals(audio.BeatmapPath, osuFile!.BeatmapPath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(audio.AudioFilename, osuFile.AudioFilename, StringComparison.Ordinal)
                ? audio
                : null);

        return new PipeEnvelope(CommunityOverlayPipeProtocol.Name, CommunityOverlayPipeProtocol.Version, sequence, sentAtUtc, snapshot);
    }

    /// <summary>
    /// Message v1 strict pour un compagnon COM-10 (V3.14-COM-11) : même contenu, sans le bloc audio.
    /// Le JSON produit est identique à celui de COM-10.
    /// </summary>
    public static PipeEnvelope ToVersion1(PipeEnvelope envelope) => envelope with
    {
        Version = 1,
        Snapshot = envelope.Snapshot with { Audio = null },
    };

    private static PipeSuspension Suspension(CommunityOverlaySourceFrame frame) =>
        frame.OverlayEditMode ? PipeSuspension.OverlayLayoutEdit
        : frame.FastLabeling ? PipeSuspension.FastLabeling
        : frame.BackgroundLibraryWork ? PipeSuspension.BackgroundLibraryWork
        : PipeSuspension.None;

    private static PipeGameState Game(GameStateSnapshot game) => new(
        game.ProviderName,
        game.ClientName,
        game.ProcessIdentity,
        game.CurrentBeatmapPath,
        game.CurrentBeatmapId,
        game.CurrentBeatmapSetId,
        game.LastUpdateUtc,
        game.Availability switch
        {
            GameStateAvailability.Available => PipeAvailability.Available,
            GameStateAvailability.NoProcess => PipeAvailability.NoProcess,
            GameStateAvailability.NoBeatmap => PipeAvailability.NoBeatmap,
            GameStateAvailability.Ambiguous => PipeAvailability.Ambiguous,
            GameStateAvailability.InvalidData => PipeAvailability.InvalidData,
            GameStateAvailability.ProviderUnavailable => PipeAvailability.ProviderUnavailable,
            _ => PipeAvailability.Error,
        },
        game.SongsDirectory,
        game.CurrentBackgroundPath);

    private static PipeRoute Route(OverlayGameState state) => state switch
    {
        OverlayGameState.SongSelect => PipeRoute.SongSelect,
        OverlayGameState.Gameplay => PipeRoute.Gameplay,
        OverlayGameState.Results => PipeRoute.Results,
        _ => PipeRoute.Unknown,
    };

    /// <summary>Métadonnées de l'analyse publiée, rattachées au chemin pour lequel elle a été publiée.</summary>
    private static PipeBeatmapMetadata? Metadata(OverlaySnapshot overlay)
    {
        if (overlay.Analysis?.Metadata is not { } m || string.IsNullOrWhiteSpace(overlay.Game.CurrentBeatmapPath))
            return null;
        return new PipeBeatmapMetadata(
            overlay.Game.CurrentBeatmapPath,
            m.BeatmapId,
            m.BeatmapSetId,
            m.Artist ?? "",
            m.Title ?? "",
            m.Version ?? "",
            m.Creator ?? "",
            m.LengthTicks,
            m.Bpm,
            m.MaxCombo,
            m.ApproachRate,
            m.OverallDifficulty,
            m.CircleSize,
            m.HpDrain,
            m.CircleCount,
            m.SliderCount,
            m.SpinnerCount,
            m.OsuStarRating,
            m.PrimaryBpm,
            m.MinBpm,
            m.MaxBpm,
            m.OsuMaxCombo);
    }

    private static PipeLiveGameplay? Live(LiveGameplaySnapshot? live) => live is null ? null : new(
        live.IsGameplay,
        live.CurrentTime.TotalMilliseconds,
        live.BeatmapDuration.TotalMilliseconds,
        live.BeatmapId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        live.BeatmapSetId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        live.ProcessIdentity,
        live.ObservedAtUtc);

    private static PipeLiveStats? Stats(LiveGameplayStatsSnapshot? stats) => stats is null ? null : new(
        stats.ProcessIdentity,
        stats.BeatmapId,
        stats.BeatmapSetId,
        stats.CurrentTime.TotalMilliseconds,
        stats.CurrentCombo,
        stats.MaxComboReached,
        stats.Count300,
        stats.Count100,
        stats.Count50,
        stats.CountMiss,
        stats.Accuracy,
        stats.Mods.IsDefaultOrEmpty ? [] : stats.Mods.Select(mod => mod.Acronym).ToArray(),
        stats.ObservedAtUtc,
        stats.Score);

    private static PipePerformance? Performance(LivePerformanceSnapshot? performance) => performance is null ? null : new(
        performance.CurrentPp,
        performance.FcPp,
        performance.Accuracy95FcPp,
        performance.SsPp,
        performance.PassedObjects,
        performance.ObservedAtUtc);
}
