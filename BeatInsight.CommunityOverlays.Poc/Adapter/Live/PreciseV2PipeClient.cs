using System.IO;
using System.IO.Pipes;
using BeatInsight.Integration.CommunityOverlays.Pipe;

namespace BeatInsight.CommunityOverlays.Poc.Adapter.Live;

/// <summary>
/// Client du pipe precise v2 (V3.14-COM-LAZER-KEY-03, même politique que le client v1 COM-KEY-04), en lecture seule (<see cref="PipeDirection.In"/>) :
/// aucun canal de commande vers BeatInsight. Contrairement au client gameplay (interrogé au tick),
/// chaque message valide est signalé immédiatement par <see cref="EnvelopeReceived"/> (thread du pipe),
/// pour que la cadence des touches ne dépende ni du tick gameplay ni du FPS de WebView2.
/// Rejette les messages malformés, d'une autre version, hors séquence ou périmés ; se reconnecte en boucle.
/// </summary>
public sealed class PreciseV2PipeClient : IDisposable
{
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

    private readonly string _pipeName;
    private readonly Func<DateTimeOffset> _clock;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private string? _session;
    private long _lastSequence;
    private long _received, _rejected, _stale, _outOfOrder, _connections, _bytes;

    public PreciseV2PipeClient(string pipeName = CommunityOverlayPreciseV2PipeProtocol.PipeName, Func<DateTimeOffset>? clock = null)
    {
        _pipeName = pipeName;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Message valide et actuel (thread du pipe). Second argument : heure locale de réception.</summary>
    public event Action<PreciseV2Envelope, DateTimeOffset>? EnvelopeReceived;

    /// <summary>Connexion perdue (BeatInsight fermé, pipe coupé, version refusée).</summary>
    public event Action? ConnectionLost;

    public bool IsConnected { get; private set; }

    public long Received => Interlocked.Read(ref _received);
    public long Rejected => Interlocked.Read(ref _rejected);
    public long Stale => Interlocked.Read(ref _stale);
    public long OutOfOrder => Interlocked.Read(ref _outOfOrder);
    public long Connections => Interlocked.Read(ref _connections);
    public long BytesReceived => Interlocked.Read(ref _bytes);
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
                // Nouvelle connexion : aucune référence de séquence ou de session n'est reprise.
                _session = null;
                _lastSequence = 0;
                IsConnected = true;
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
            if (IsConnected)
            {
                IsConnected = false;
                ConnectionLost?.Invoke();
            }
            try { await Task.Delay(RetryDelay, token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
        }
        if (IsConnected)
        {
            IsConnected = false;
            ConnectionLost?.Invoke();
        }
    }

    private async Task ReadLoopAsync(Stream pipe, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            byte[]? frame = await CommunityOverlayPipeProtocol.ReadFrameAsync(pipe, token).ConfigureAwait(false);
            if (frame is null) return;
            Interlocked.Add(ref _bytes, frame.Length + 4);
            DateTimeOffset now = _clock();
            if (!Accept(frame, now, out PreciseV2Envelope? envelope, out bool versionMismatch))
            {
                if (versionMismatch) return; // autre version : rien n'est interprété
                continue;
            }
            EnvelopeReceived?.Invoke(envelope!, now);
        }
    }

    /// <summary>Validation d'une trame (testable sans pipe).</summary>
    public bool Accept(byte[] frame, DateTimeOffset now, out PreciseV2Envelope? envelope, out bool versionMismatch)
    {
        versionMismatch = false;
        envelope = CommunityOverlayPreciseV2PipeProtocol.TryDeserialize(frame, out string? rejection);
        if (envelope is null)
        {
            Reject(rejection);
            versionMismatch = rejection is not null && rejection.StartsWith("version", StringComparison.Ordinal);
            return false;
        }
        if (_session is not null && envelope.ProducerSession != _session)
        {
            // Autre producteur sur la même connexion : impossible en fonctionnement normal, on ignore.
            Interlocked.Increment(ref _outOfOrder);
            envelope = null;
            return false;
        }
        if (envelope.Sequence <= _lastSequence)
        {
            Interlocked.Increment(ref _outOfOrder);
            envelope = null;
            return false;
        }
        _session = envelope.ProducerSession;
        _lastSequence = envelope.Sequence;
        if (now - envelope.SentAtUtc > CommunityOverlayPreciseV2PipeProtocol.MaxMessageAge)
        {
            // Dernier message conservé par le serveur depuis trop longtemps : jamais présenté comme actuel.
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
