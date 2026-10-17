using BeatInsight.Integration.CommunityOverlays.Pipe;

namespace BeatInsight.Integration.CommunityOverlays;

/// <summary>
/// Publie périodiquement les snapshots BeatInsight sur le Named Pipe (V3.14-COM-10).
/// Tout se passe sur un minuteur du pool de threads : aucune tâche n'est ajoutée au thread UI,
/// aucune lecture native n'est faite (la capture ne lit que des objets déjà produits),
/// et aucun PP n'est calculé. Un tick encore en cours fait sauter le suivant.
/// V3.14-COM-11 : le message v2 (avec la durée audio) part sur le pipe v2 ; le même message,
/// réduit au contrat v1, part sur le pipe v1 pour les compagnons COM-10.
/// </summary>
public sealed class CommunityOverlayLiveBridge : IDisposable
{
    private readonly Func<CommunityOverlaySourceFrame> _capture;
    private readonly CommunityOverlayPipeServer _server;
    private readonly CommunityOverlayPipeServer? _legacyServer;
    private readonly OsuFileIdentityReader _osuFiles;
    private readonly AudioDurationReader _audio;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<string>? _log;
    private Timer? _timer;
    private int _busy;
    private int _disposed;
    private long _sequence;
    private bool _legacyListening;

    public CommunityOverlayLiveBridge(
        Func<CommunityOverlaySourceFrame> capture,
        CommunityOverlayPipeServer server,
        OsuFileIdentityReader? osuFiles = null,
        Func<DateTimeOffset>? clock = null,
        Action<string>? log = null,
        CommunityOverlayPipeServer? legacyServer = null,
        AudioDurationReader? audio = null)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _legacyServer = legacyServer;
        _osuFiles = osuFiles ?? new OsuFileIdentityReader();
        _audio = audio ?? new AudioDurationReader();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _log = log;
    }

    public long Published => Interlocked.Read(ref _sequence);

    public long SkippedTicks { get; private set; }

    public long RejectedFrames { get; private set; }

    public bool Start()
    {
        if (!_server.Start())
            return false;
        // Le pipe v1 est accessoire : s'il ne démarre pas, le pipe v2 fonctionne quand même.
        _legacyListening = _legacyServer?.Start() == true;
        if (_legacyServer is not null && !_legacyListening)
            _log?.Invoke("COMMUNITY OVERLAY PIPE | pipe v1 non démarré : nom indisponible");
        _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, CommunityOverlayPipeProtocol.PublishInterval);
        return true;
    }

    /// <summary>Un tick : capture, recopie, sérialisation, remplacement du dernier message. Public pour les tests.</summary>
    public void Tick()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (Interlocked.Exchange(ref _busy, 1) != 0)
        {
            SkippedTicks++;
            return;
        }

        try
        {
            CommunityOverlaySourceFrame frame = _capture();
            PipeOsuFile? osuFile = frame.Game.IsAvailable ? _osuFiles.Read(frame.Game.CurrentBeatmapPath) : null;
            // Lecture de la durée en tâche de fond : ce tick ne lit que le cache (ou Pending).
            PipeAudioFile? audio = osuFile is not null ? _audio.Get(osuFile.BeatmapPath, osuFile.AudioFilename) : null;
            long sequence = Interlocked.Increment(ref _sequence);
            PipeEnvelope envelope = CommunityOverlayPipeSnapshotMapper.Map(frame, osuFile, sequence, _clock(), audio);

            if (CommunityOverlayPipeProtocol.Validate(envelope) is { } rejection)
            {
                // Valeur hors contrat (ex. lecture native aberrante) : on retire les blocs live
                // plutôt que d'envoyer un message que le compagnon rejetterait en entier.
                RejectedFrames++;
                _log?.Invoke($"COMMUNITY OVERLAY PIPE | bloc live retiré : {rejection}");
                envelope = envelope with
                {
                    Snapshot = envelope.Snapshot with { LiveGameplay = null, Stats = null, Performance = null, Audio = null },
                };
                if (CommunityOverlayPipeProtocol.Validate(envelope) is not null)
                    return;
            }

            _server.Publish(CommunityOverlayPipeProtocol.Serialize(envelope));
            if (_legacyListening)
                _legacyServer!.Publish(CommunityOverlayPipeProtocol.Serialize(CommunityOverlayPipeSnapshotMapper.ToVersion1(envelope)));
        }
        catch (Exception ex)
        {
            // Le compagnon est accessoire : une erreur ici ne doit jamais remonter dans BeatInsight.
            _log?.Invoke($"COMMUNITY OVERLAY PIPE | tick ignoré : {ex.GetType().Name}");
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _timer?.Dispose();
        _server.Dispose();
        _legacyServer?.Dispose();
    }
}
