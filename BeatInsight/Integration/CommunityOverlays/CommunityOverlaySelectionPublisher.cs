using System.Diagnostics;
using System.IO;
using System.Globalization;
using BeatInsight.Integration.CommunityOverlays.Pipe;
using BeatInsight.Integration.Game;
using BeatInsight.Integration.SongSelect;

namespace BeatInsight.Integration.CommunityOverlays;

/// <summary>
/// V3.14-COM-PP-03 : publie sur le pipe v3 « selection » le PP SS de la beatmap sélectionnée avec les mods actifs.
/// Un tick (minuteur du pool de threads, jamais le Dispatcher) : une observation des mods (deux lectures de
/// 4 octets), une lecture de <c>GameStateService.Current</c>, une consultation du service SS (aucun calcul ici),
/// puis remplacement du dernier message. Un tick encore en cours fait sauter le suivant.
/// </summary>
internal sealed class CommunityOverlaySelectionPublisher : IDisposable
{
    private readonly Func<SongSelectModsSnapshot> observeMods;
    private readonly Func<GameStateSnapshot> currentGame;
    private readonly SongSelectPerformanceService service;
    private readonly Action<byte[]> publish;
    private readonly Func<DateTimeOffset> clock;
    private readonly Action<string>? log;
    private readonly string producerSession = Guid.NewGuid().ToString("N");
    private Timer? timer;
    private int busy;
    private int disposed;
    private long sequence;

    // Diagnostic : temps de réaction par génération (changement de sélection → résultat publié).
    private long trackedGeneration = -1;
    private long generationStartTimestamp;
    private bool generationReported;
    private SelectionValidity? lastValidity;
    private string? lastReason;

    public CommunityOverlaySelectionPublisher(
        Func<SongSelectModsSnapshot> observeMods,
        Func<GameStateSnapshot> currentGame,
        SongSelectPerformanceService service,
        Action<byte[]> publish,
        Func<DateTimeOffset>? clock = null,
        Action<string>? log = null)
    {
        this.observeMods = observeMods ?? throw new ArgumentNullException(nameof(observeMods));
        this.currentGame = currentGame ?? throw new ArgumentNullException(nameof(currentGame));
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.publish = publish ?? throw new ArgumentNullException(nameof(publish));
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        this.log = log;
    }

    public long Published => Interlocked.Read(ref sequence);

    public long SkippedTicks { get; private set; }

    public long ValidMessages { get; private set; }

    public double TickMaxMs { get; private set; }

    public double TickTotalMs { get; private set; }

    public List<double> ReactionTimesMs { get; } = [];

    public SelectionPipeEnvelope? LastEnvelope { get; private set; }

    public void Start() =>
        timer ??= new Timer(_ => Tick(), null, TimeSpan.Zero, CommunityOverlaySelectionPipeProtocol.PublishInterval);

    /// <summary>Un tick. Public pour les tests.</summary>
    public void Tick()
    {
        if (Volatile.Read(ref disposed) != 0) return;
        if (Interlocked.Exchange(ref busy, 1) != 0)
        {
            SkippedTicks++;
            return;
        }
        long started = Stopwatch.GetTimestamp();
        try
        {
            SongSelectModsSnapshot mods = observeMods();
            GameStateSnapshot game = currentGame();
            (SongSelectSelection? selection, SelectionValidity validity, string reason) = Resolve(mods, game);
            SongSelectPerformanceState state = service.Update(selection);
            SelectionPipeEnvelope envelope = Build(mods, selection, validity, reason, state, Interlocked.Increment(ref sequence), clock());

            if (CommunityOverlaySelectionPipeProtocol.Validate(envelope) is { } rejection)
            {
                log?.Invoke($"SONGSELECT PP | message retiré : {rejection}");
                envelope = envelope with
                {
                    Validity = SelectionValidity.Unknown,
                    Reason = "message hors contrat",
                    Performance = null,
                    Mods = null,
                    ModsKey = null,
                    Beatmap = null,
                };
                if (CommunityOverlaySelectionPipeProtocol.Validate(envelope) is not null)
                    return;
            }
            publish(CommunityOverlaySelectionPipeProtocol.Serialize(envelope));
            LastEnvelope = envelope;
            if (envelope.Validity == SelectionValidity.Valid) ValidMessages++;
            Report(envelope, state);
        }
        catch (Exception ex)
        {
            // Accessoire : une erreur ici ne doit jamais remonter dans BeatInsight.
            log?.Invoke($"SONGSELECT PP | tick ignoré : {ex.GetType().Name}");
        }
        finally
        {
            double ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            TickMaxMs = Math.Max(TickMaxMs, ms);
            TickTotalMs += ms;
            Volatile.Write(ref busy, 0);
        }
    }

    /// <summary>Sélection publiable : mods valides en sélection des maps, même processus que l'état de jeu, beatmap connue.</summary>
    internal static (SongSelectSelection? Selection, SelectionValidity Validity, string Reason) Resolve(
        SongSelectModsSnapshot mods, GameStateSnapshot game)
    {
        if (mods.Validity == SongSelectModsValidity.NotSongSelect)
            return (null, SelectionValidity.NotSongSelect, "autre écran");
        if (!mods.IsValid || mods.ModsKey is null || mods.ProcessIdentity is null)
            return (null, SelectionValidity.Unknown,
                mods.Validity == SongSelectModsValidity.UnknownBits ? "mods hors table" : $"mods inconnus ({mods.FailureReason})");
        if (!string.Equals(game.ProcessIdentity, mods.ProcessIdentity, StringComparison.Ordinal))
            return (null, SelectionValidity.Unknown, "processus différent");
        if (!game.IsAvailable || string.IsNullOrWhiteSpace(game.CurrentBeatmapPath))
            return (null, SelectionValidity.Unknown, "beatmap inconnue");
        return (new SongSelectSelection(mods.ProcessIdentity, game.CurrentBeatmapPath, mods.NormalizedMods, mods.ModsKey),
            SelectionValidity.Valid, "ok");
    }

    internal SelectionPipeEnvelope Build(
        SongSelectModsSnapshot mods, SongSelectSelection? selection, SelectionValidity validity, string reason,
        SongSelectPerformanceState state, long seq, DateTimeOffset now)
    {
        SelectionPerformance? performance = null;
        if (selection is not null)
        {
            (validity, reason) = state.Status switch
            {
                SongSelectPerformanceStatus.Ready => (SelectionValidity.Valid, "ok"),
                SongSelectPerformanceStatus.Pending => (SelectionValidity.Pending, state.Reason),
                SongSelectPerformanceStatus.Failed => (SelectionValidity.Unsupported, state.Reason),
                _ => (SelectionValidity.Unknown, state.Reason),
            };
            if (validity == SelectionValidity.Valid && state.Value is { } value && state.ComputedAtUtc is { } at)
                performance = new SelectionPerformance(value.SsPp, value.Accuracy95FcPp, state.Key!.CalculatorVersion, at);
            else if (validity == SelectionValidity.Valid)
                (validity, reason) = (SelectionValidity.Unknown, "résultat absent");
        }

        SongSelectPerformanceKey? key = state.Key;
        return new SelectionPipeEnvelope(
            CommunityOverlaySelectionPipeProtocol.Name,
            CommunityOverlaySelectionPipeProtocol.Version,
            producerSession,
            seq,
            now,
            state.Generation,
            validity,
            reason.Length > CommunityOverlaySelectionPipeProtocol.MaxReasonLength ? reason[..CommunityOverlaySelectionPipeProtocol.MaxReasonLength] : reason,
            mods.ProcessIdentity,
            selection is not null && key is not null
                ? new SelectionBeatmap(key.BeatmapPath, key.FileLength, new DateTimeOffset(key.LastWriteTicks, TimeSpan.Zero))
                : null,
            selection?.ModsKey,
            selection?.Mods.ToArray(),
            mods.RawModsMask,
            mods.ObservedAtUtc,
            performance);
    }

    private void Report(SelectionPipeEnvelope envelope, SongSelectPerformanceState state)
    {
        if (state.Generation != trackedGeneration)
        {
            trackedGeneration = state.Generation;
            generationStartTimestamp = Stopwatch.GetTimestamp();
            generationReported = false;
        }
        if (envelope.Validity == SelectionValidity.Valid && !generationReported)
        {
            generationReported = true;
            double reaction = Stopwatch.GetElapsedTime(generationStartTimestamp).TotalMilliseconds;
            if (ReactionTimesMs.Count < 10_000) ReactionTimesMs.Add(reaction);
            log?.Invoke(string.Create(CultureInfo.InvariantCulture,
                $"SONGSELECT PP | gén. {state.Generation} : SS {envelope.Performance!.SsPp:0.00} pp, 95 % {envelope.Performance.Accuracy95Pp:0.00} pp, {envelope.ModsKey}, publié en {reaction:0} ms ({Path.GetFileName(envelope.Beatmap!.Path)})"));
        }
        if (envelope.Validity != lastValidity || (envelope.Validity != SelectionValidity.Valid && envelope.Reason != lastReason))
        {
            if (envelope.Validity != SelectionValidity.Valid)
                log?.Invoke($"SONGSELECT PP | {envelope.Validity} : {envelope.Reason}");
            lastValidity = envelope.Validity;
            lastReason = envelope.Reason;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        timer?.Dispose();
        // Attend la fin d'un tick en cours (borné) avant que l'appelant ne ferme le reader et le service.
        var wait = Stopwatch.StartNew();
        while (Volatile.Read(ref busy) != 0 && wait.ElapsedMilliseconds < 2000)
            Thread.Sleep(5);
    }
}
