using System.IO;
using System.IO.Pipes;
using BeatInsight.Integration.CommunityOverlays.Pipe;

namespace BeatInsight.CommunityOverlays.Poc.Adapter.Live;

public enum PipeClientState
{
    Connecting,
    Connected,
    Disconnected,
    VersionMismatch,
}

/// <summary>Dernier message valide reçu, avec l'heure locale de réception.</summary>
public sealed record PipeReception(PipeEnvelope Envelope, DateTimeOffset ReceivedAtUtc);

/// <summary>
/// Client Named Pipe du compagnon (V3.14-COM-10), en lecture seule (<see cref="PipeDirection.In"/>) :
/// il ne peut rien envoyer à BeatInsight. Se reconnecte en boucle (redémarrage de BeatInsight),
/// ne garde que le dernier message valide (le plus récent remplace le précédent), rejette les
/// messages malformés ou d'une autre version, et efface la donnée dès que la connexion tombe.
/// </summary>
public sealed class CommunityOverlayPipeClient : IDisposable
{
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

    private readonly string _pipeName;
    private readonly string? _fallbackPipeName;
    private readonly Func<DateTimeOffset> _clock;
    private string? _connectedPipeName;
    private readonly CancellationTokenSource _stop = new();
    private PipeReception? _latest;
    private int _state = (int)PipeClientState.Connecting;
    private Task? _loop;
    private long _received;
    private long _rejected;
    private long _connections;
    private long _lastSequence = -1;

    /// <param name="fallbackPipeName">
    /// V3.14-COM-11 : pipe essayé quand <paramref name="pipeName"/> n'existe pas (BeatInsight COM-10,
    /// qui ne publie que le pipe v1). Null : aucun repli.
    /// </param>
    public CommunityOverlayPipeClient(string pipeName = CommunityOverlayPipeProtocol.PipeName, Func<DateTimeOffset>? clock = null,
        string? fallbackPipeName = null)
    {
        _pipeName = pipeName;
        _fallbackPipeName = fallbackPipeName;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public PipeClientState State => (PipeClientState)Volatile.Read(ref _state);

    /// <summary>Pipe de la connexion en cours (principal ou repli) ; null si déconnecté.</summary>
    public string? ConnectedPipeName => Volatile.Read(ref _connectedPipeName);

    /// <summary>Dernier message valide de la connexion en cours ; null si déconnecté.</summary>
    public PipeReception? Latest => Volatile.Read(ref _latest);

    public long Received => Interlocked.Read(ref _received);

    public long Rejected => Interlocked.Read(ref _rejected);

    public long Connections => Interlocked.Read(ref _connections);

    public string? LastRejection { get; private set; }

    public void Start() => _loop ??= Task.Run(() => RunAsync(_stop.Token));

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            SetState(PipeClientState.Connecting);
            try
            {
                using var pipe = await ConnectAsync(token).ConfigureAwait(false);
                Interlocked.Increment(ref _connections);
                SetState(PipeClientState.Connected);
                Interlocked.Exchange(ref _lastSequence, -1);
                await ReadLoopAsync(pipe, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or InvalidDataException)
            {
                if (ex is InvalidDataException) Reject(ex.Message);
            }

            // Connexion perdue : plus aucune donnée n'est présentée comme actuelle.
            Volatile.Write(ref _latest, null);
            Volatile.Write(ref _connectedPipeName, null);
            if (State != PipeClientState.VersionMismatch) SetState(PipeClientState.Disconnected);
            try { await Task.Delay(RetryDelay, token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
        }
        Volatile.Write(ref _latest, null);
        Volatile.Write(ref _connectedPipeName, null);
        SetState(PipeClientState.Disconnected);
    }

    /// <summary>Pipe principal, puis repli s'il est configuré. Lève <see cref="TimeoutException"/> si aucun n'existe.</summary>
    private async Task<NamedPipeClientStream> ConnectAsync(CancellationToken token)
    {
        foreach (string? name in new[] { _pipeName, _fallbackPipeName })
        {
            if (name is null) continue;
            var pipe = new NamedPipeClientStream(".", name, PipeDirection.In, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.ConnectAsync((int)RetryDelay.TotalMilliseconds, token).ConfigureAwait(false);
                Volatile.Write(ref _connectedPipeName, name);
                return pipe;
            }
            catch (TimeoutException) when (name != _fallbackPipeName && _fallbackPipeName is not null)
            {
                pipe.Dispose();
            }
            catch
            {
                pipe.Dispose();
                throw;
            }
        }
        throw new TimeoutException("Aucun pipe BeatInsight disponible.");
    }

    private async Task ReadLoopAsync(Stream pipe, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            byte[]? frame = await CommunityOverlayPipeProtocol.ReadFrameAsync(pipe, token).ConfigureAwait(false);
            if (frame is null) return; // BeatInsight fermé ou serveur arrêté.

            var envelope = CommunityOverlayPipeProtocol.TryDeserialize(frame, out string? rejection);
            if (envelope is null)
            {
                Reject(rejection);
                if (rejection is not null && rejection.StartsWith("version", StringComparison.Ordinal))
                {
                    // Autre version du protocole : on n'interprète rien et on se déconnecte.
                    SetState(PipeClientState.VersionMismatch);
                    return;
                }
                continue;
            }
            // Ordre garanti par le flux ; une séquence qui recule signale un autre producteur : ignorée.
            if (envelope.Sequence <= Interlocked.Read(ref _lastSequence))
            {
                Reject("séquence non croissante");
                continue;
            }
            Interlocked.Exchange(ref _lastSequence, envelope.Sequence);
            Volatile.Write(ref _latest, new PipeReception(envelope, _clock()));
            Interlocked.Increment(ref _received);
            SetState(PipeClientState.Connected);
        }
    }

    private void Reject(string? reason)
    {
        Interlocked.Increment(ref _rejected);
        LastRejection = reason;
    }

    private void SetState(PipeClientState state) => Volatile.Write(ref _state, (int)state);

    public void Dispose()
    {
        if (_stop.IsCancellationRequested) return;
        _stop.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        _stop.Dispose();
        Volatile.Write(ref _latest, null);
    }
}
