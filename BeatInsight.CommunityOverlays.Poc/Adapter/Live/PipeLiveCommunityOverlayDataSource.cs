using System.Windows.Threading;
using BeatInsight.Integration.CommunityOverlays.Pipe;
using IOPath = System.IO.Path;

namespace BeatInsight.CommunityOverlays.Poc.Adapter.Live;

/// <summary>
/// Source live réelle du compagnon (V3.14-COM-10, option <c>--source live-pipe</c>) :
/// dernier message du Named Pipe → <see cref="LiveCommunityOverlayAdapter"/> → bridge tosu.
/// Connexion absente, perdue ou silencieuse depuis plus de 1,5 s : snapshot vide (Unknown).
/// Jamais de repli vers le mock.
/// </summary>
public sealed class PipeLiveCommunityOverlayDataSource : ICommunityOverlayDataSource
{
    private readonly CommunityOverlayPipeClient _client;
    private readonly PublishTicker _ticker;
    private readonly SelectionPipeClient? _selection;
    private LiveAdapterState _state = LiveAdapterState.Initial;
    private string? _songsRoot;

    public PipeLiveCommunityOverlayDataSource(
        string pipeName = CommunityOverlayPipeProtocol.PipeName,
        PublishTimerMode timerMode = PublishTimerMode.Compensated,
        bool selectionPipe = false)
    {
        // V3.14-COM-PP-03 (opt-in) : PP SS de la sélection via le pipe v3 ; sans lui, comportement inchangé.
        _selection = selectionPipe ? new SelectionPipeClient() : null;
        // V3.14-COM-11 : repli sur le pipe v1 quand BeatInsight est en version COM-10.
        _client = new CommunityOverlayPipeClient(pipeName,
            fallbackPipeName: pipeName == CommunityOverlayPipeProtocol.PipeName ? CommunityOverlayPipeProtocol.LegacyPipeNameV1 : null);
        _ticker = new PublishTicker(timerMode, TimeSpan.FromMilliseconds(100), DispatcherPriority.Normal, Publish);
        Current = Step(DateTimeOffset.UtcNow);
    }

    public PublishTicker Ticker => _ticker;

    /// <summary>État courant de l'adaptateur (diagnostic, ex. slider breaks estimés COM-SB-01).</summary>
    public LiveAdapterState State => _state;

    public CommunityOverlayPipeClient Client => _client;

    /// <summary>Client du pipe selection (COM-PP-03), null si l'option est désactivée.</summary>
    public SelectionPipeClient? Selection => _selection;

    /// <summary>Résultat de la dernière application du PP de sélection (diagnostic).</summary>
    public string? LastSelectionStatus { get; private set; }

    public CommunityOverlaySnapshot Current { get; private set; }

    public IReadOnlyList<LiveAdapterEvent> LastEvents { get; private set; } = [];

    /// <summary>
    /// Racine Songs du dernier message valide, pour <c>/files/beatmap/*</c>. Null si inconnue
    /// ou déconnecté : le fournisseur de fichiers répond alors 404.
    /// </summary>
    public string? SongsRoot => Volatile.Read(ref _songsRoot);

    public event EventHandler<CommunityOverlaySnapshot>? SnapshotChanged;

    public void Start()
    {
        _client.Start();
        _selection?.Start();
        Publish();
        _ticker.Start();
    }

    public void Dispose()
    {
        _ticker.Dispose();
        _client.Dispose();
        _selection?.Dispose();
    }

    private void Publish()
    {
        Current = Step(DateTimeOffset.UtcNow);
        SnapshotChanged?.Invoke(this, Current);
    }

    private CommunityOverlaySnapshot Step(DateTimeOffset now)
    {
        var reception = _client.State == PipeClientState.Connected ? _client.Latest : null;
        var input = Resolve(reception, now);
        var result = LiveCommunityOverlayAdapter.Adapt(input, _state, now);
        _state = result.State;
        LastEvents = result.Diagnostics.Events;
        string? root = input.Game?.SongsDirectory;
        Volatile.Write(ref _songsRoot, root is not null && IOPath.IsPathFullyQualified(root) && result.Snapshot.Beatmap?.Files is not null ? root : null);
        if (_selection is null)
            return result.Snapshot;
        var snapshot = SongSelectPerformanceOverlay.Apply(result.Snapshot, input, _selection.Latest, now, out string status);
        LastSelectionStatus = status;
        return snapshot;
    }

    /// <summary>Message reçu → entrées de l'adaptateur. Sans message récent : aucune donnée.</summary>
    public static BeatInsightLiveInput Resolve(PipeReception? reception, DateTimeOffset now)
    {
        if (reception is null || now - reception.ReceivedAtUtc > CommunityOverlayPipeProtocol.ReceiveTimeout)
            return new BeatInsightLiveInput(Game: null, Route: null);
        return ToInput(reception.Envelope.Snapshot);
    }

    /// <summary>Recopie champ à champ : aucune valeur ajoutée, aucun horodatage modifié.</summary>
    public static BeatInsightLiveInput ToInput(PipeSnapshot s) => new(
        Game: s.Game is { } g
            ? new SourceGameState(g.ProviderName, g.ClientName, g.ProcessIdentity, g.CurrentBeatmapPath, g.CurrentBeatmapId,
                g.CurrentBeatmapSetId, g.LastUpdateUtc, (SourceAvailability)(int)g.Availability, g.SongsDirectory, g.CurrentBackgroundPath)
            : null,
        Route: s.Route is { } r ? new SourceGameRoute((SourceRoute)(int)r.Route, r.ObservedAtUtc, r.ProcessIdentity) : null,
        Metadata: s.Metadata is { } m
            ? new SourceBeatmapMetadata(m.BeatmapPath, m.BeatmapId, m.BeatmapSetId, m.Artist, m.Title, m.Version, m.Creator,
                m.LengthTicks, m.Bpm, m.MaxCombo, m.ApproachRate, m.OverallDifficulty, m.CircleSize, m.HpDrain,
                m.CircleCount, m.SliderCount, m.SpinnerCount, m.OsuStarRating, m.PrimaryBpm, m.MinBpm, m.MaxBpm, m.OsuMaxCombo)
            : null,
        LiveGameplay: s.LiveGameplay is { } l
            ? new SourceLiveGameplay(l.IsGameplay, TimeSpan.FromMilliseconds(l.CurrentTimeMs), TimeSpan.FromMilliseconds(l.BeatmapDurationMs),
                l.BeatmapId, l.BeatmapSetId, l.ProcessIdentity, l.ObservedAtUtc)
            : null,
        Stats: s.Stats is { } st
            ? new SourceLiveStats(st.ProcessIdentity, st.BeatmapId, st.BeatmapSetId, TimeSpan.FromMilliseconds(st.CurrentTimeMs),
                st.CurrentCombo, st.MaxComboReached, st.Count300, st.Count100, st.Count50, st.CountMiss, st.Accuracy,
                st.Mods.Select(mod => mod.ToUpperInvariant()).ToArray(), st.ObservedAtUtc, st.Score)
            : null,
        Performance: s.Performance is { } p
            ? new SourcePerformance(p.CurrentPp, p.FcPp, p.Accuracy95FcPp, p.SsPp, p.PassedObjects, p.ObservedAtUtc)
            : null,
        Suspension: (SourceSuspension)(int)s.Suspension,
        OsuFile: s.OsuFile is { } f
            ? new SourceOsuFile(f.BeatmapPath, f.Md5, f.AudioFilename, f.TitleUnicode, f.ArtistUnicode)
            : null,
        AudioFile: s.Audio is { } a
            ? new SourceAudioFile(a.BeatmapPath, a.AudioFilename, a.Status == PipeAudioStatus.Known ? a.DurationMs : null, a.Status.ToString())
            : null);
}
