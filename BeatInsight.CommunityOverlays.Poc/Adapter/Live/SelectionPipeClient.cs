using System.IO;
using System.IO.Pipes;
using BeatInsight.Integration.CommunityOverlays.Pipe;

namespace BeatInsight.CommunityOverlays.Poc.Adapter.Live;

/// <summary>Dernier message selection valide, avec l'heure locale de réception.</summary>
public sealed record SelectionReception(SelectionPipeEnvelope Envelope, DateTimeOffset ReceivedAtUtc);

/// <summary>
/// Client du pipe v3 « selection » (V3.14-COM-PP-03), en lecture seule (<see cref="PipeDirection.In"/>).
/// Ne garde que le dernier message valide ; rejette les messages malformés, d'une autre version, hors séquence
/// ou périmés ; efface la donnée dès que la connexion tombe ; se reconnecte en boucle.
/// </summary>
public sealed class SelectionPipeClient : IDisposable
{
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

    private readonly string _pipeName;
    private readonly Func<DateTimeOffset> _clock;
    private readonly CancellationTokenSource _stop = new();
    private SelectionReception? _latest;
    private Task? _loop;
    private string? _session;
    private long _lastSequence;
    private long _received, _rejected, _stale, _outOfOrder, _connections;

    public SelectionPipeClient(string pipeName = CommunityOverlaySelectionPipeProtocol.PipeName, Func<DateTimeOffset>? clock = null)
    {
        _pipeName = pipeName;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Dernier message valide de la connexion en cours ; null si déconnecté.</summary>
    public SelectionReception? Latest => Volatile.Read(ref _latest);

    public bool IsConnected { get; private set; }
    public long Received => Interlocked.Read(ref _received);
    public long Rejected => Interlocked.Read(ref _rejected);
    public long Stale => Interlocked.Read(ref _stale);
    public long OutOfOrder => Interlocked.Read(ref _outOfOrder);
    public long Connections => Interlocked.Read(ref _connections);
    public string? LastRejection { get; private set; }

    public void Start() => _loop ??= Task.Run(() => RunAsync(_stop.Token));

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.In,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.ConnectAsync((int)RetryDelay.TotalMilliseconds, token).ConfigureAwait(false);
                Interlocked.Increment(ref _connections);
                _session = null;
                _lastSequence = 0;
                IsConnected = true;
                while (!token.IsCancellationRequested)
                {
                    byte[]? frame = await CommunityOverlayPipeProtocol.ReadFrameAsync(pipe, token).ConfigureAwait(false);
                    if (frame is null) break;
                    DateTimeOffset now = _clock();
                    if (Accept(frame, now, out SelectionPipeEnvelope? envelope, out bool versionMismatch))
                        Volatile.Write(ref _latest, new SelectionReception(envelope!, now));
                    else if (versionMismatch)
                        break; // autre version : rien n'est interprété
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or InvalidDataException)
            {
                if (ex is InvalidDataException) Reject(ex.Message);
            }
            IsConnected = false;
            Volatile.Write(ref _latest, null);
            try { await Task.Delay(RetryDelay, token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
        }
        IsConnected = false;
        Volatile.Write(ref _latest, null);
    }

    /// <summary>Validation d'une trame (testable sans pipe).</summary>
    public bool Accept(byte[] frame, DateTimeOffset now, out SelectionPipeEnvelope? envelope, out bool versionMismatch)
    {
        versionMismatch = false;
        envelope = CommunityOverlaySelectionPipeProtocol.TryDeserialize(frame, out string? rejection);
        if (envelope is null)
        {
            Reject(rejection);
            versionMismatch = rejection is not null && rejection.StartsWith("version", StringComparison.Ordinal);
            return false;
        }
        if ((_session is not null && envelope.ProducerSession != _session) || envelope.Sequence <= _lastSequence)
        {
            Interlocked.Increment(ref _outOfOrder);
            envelope = null;
            return false;
        }
        _session = envelope.ProducerSession;
        _lastSequence = envelope.Sequence;
        if (now - envelope.SentAtUtc > CommunityOverlaySelectionPipeProtocol.MaxMessageAge)
        {
            Interlocked.Increment(ref _stale);
            envelope = null;
            return false;
        }
        Interlocked.Increment(ref _received);
        return true;
    }

    private void Reject(string? reason)
    {
        Interlocked.Increment(ref _rejected);
        LastRejection = reason;
    }

    public void Dispose()
    {
        if (_stop.IsCancellationRequested) return;
        _stop.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(1)); } catch (AggregateException) { }
        _stop.Dispose();
    }
}

/// <summary>
/// V3.14-COM-PP-03 : application du PP de sélection au snapshot (fonction pure). Le PP n'est publié qu'en sélection
/// des maps, pour un message récent, valide, du même processus et de la même beatmap que l'état de jeu courant ;
/// dans tous les autres cas le snapshot reste inchangé (valeur neutre côté overlay). Seules les précisions
/// réellement calculées par BeatInsight (100 % et 95 %) sont renseignées.
/// </summary>
public static class SongSelectPerformanceOverlay
{
    public static CommunityOverlaySnapshot Apply(
        CommunityOverlaySnapshot snapshot, BeatInsightLiveInput input, SelectionReception? selection, DateTimeOffset now,
        out string status)
    {
        if (snapshot.Route != CommunityOverlayRoute.SongSelect)
        {
            status = "autre écran";
            return snapshot;
        }
        if (selection is null)
        {
            status = "aucun message";
            return snapshot;
        }
        SelectionPipeEnvelope e = selection.Envelope;
        if (now - selection.ReceivedAtUtc > CommunityOverlaySelectionPipeProtocol.ReceiveTimeout ||
            now - e.SentAtUtc > CommunityOverlaySelectionPipeProtocol.MaxMessageAge)
        {
            status = "message périmé";
            return snapshot;
        }
        if (e.Validity != SelectionValidity.Valid || e.Performance is null || e.Beatmap is null)
        {
            status = e.Validity.ToString();
            return snapshot;
        }
        SourceGameState? game = input.Game;
        if (game is null || !string.Equals(game.ProcessIdentity, e.ProcessIdentity, StringComparison.Ordinal))
        {
            status = "processus différent";
            return snapshot;
        }
        string? current = LiveCommunityOverlayAdapter.NormalizePath(game.CurrentBeatmapPath);
        if (current is null || !string.Equals(current, LiveCommunityOverlayAdapter.NormalizePath(e.Beatmap.Path), StringComparison.Ordinal))
        {
            status = "beatmap différente";
            return snapshot;
        }

        DateTimeOffset at = e.Performance.ComputedAtUtc;
        var byAccuracy = new Dictionary<int, FieldValue<double>>(snapshot.Pp.ByAccuracy)
        {
            [95] = FieldValue<double>.Observed(e.Performance.Accuracy95Pp, at, FieldProvenance.SelectionPerformance),
            [100] = FieldValue<double>.Observed(e.Performance.SsPp, at, FieldProvenance.SelectionPerformance),
        };
        status = "publié";
        return snapshot with { Pp = snapshot.Pp with { ByAccuracy = byAccuracy } };
    }
}
