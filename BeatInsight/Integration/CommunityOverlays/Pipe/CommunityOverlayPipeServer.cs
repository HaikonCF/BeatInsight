using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace BeatInsight.Integration.CommunityOverlays.Pipe;

/// <summary>
/// Serveur Named Pipe côté BeatInsight (V3.14-COM-10). Données à sens unique :
/// le pipe est ouvert en <see cref="PipeDirection.Out"/>, aucun octet n'est jamais lu
/// depuis un client, donc aucun overlay ne peut envoyer de commande à BeatInsight.
/// <list type="bullet">
/// <item>Accès restreint à l'utilisateur Windows courant (<see cref="PipeOptions.CurrentUserOnly"/>).</item>
/// <item>Première instance créée avec <see cref="PipeOptions.FirstPipeInstance"/> : si un autre
/// process possède déjà ce nom, le serveur ne démarre pas (pas de partage avec un imposteur).</item>
/// <item><see cref="Publish"/> ne bloque jamais : il remplace le dernier message (le plus récent
/// gagne) et réveille les connexions, qui écrivent sur leurs propres tâches.</item>
/// <item>Un client lent ou bloqué est déconnecté après <see cref="WriteTimeout"/>.</item>
/// </list>
/// </summary>
public sealed class CommunityOverlayPipeServer : IDisposable
{
    public static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(2);

    private readonly string _pipeName;
    private readonly int _maxClients;
    private readonly Action<string>? _log;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly List<SemaphoreSlim> _signals = [];
    private byte[]? _latest;
    private int _started;
    private int _connected;
    private long _messagesWritten;
    private long _clientsAccepted;
    private long _clientsDropped;

    /// <summary>Clients simultanés par défaut (FEAT-003 : plafond des overlays gérés, compagnons manuels compris).</summary>
    public const int DefaultMaxClients = 4;

    public CommunityOverlayPipeServer(string pipeName = CommunityOverlayPipeProtocol.PipeName, int maxClients = DefaultMaxClients, Action<string>? log = null)
    {
        if (maxClients is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(maxClients));
        _pipeName = pipeName;
        _maxClients = maxClients;
        _log = log;
    }

    /// <summary>Faux si le nom du pipe était déjà pris par un autre process au démarrage.</summary>
    public bool IsListening { get; private set; }

    public int ConnectedClients => Volatile.Read(ref _connected);

    public long MessagesWritten => Interlocked.Read(ref _messagesWritten);

    public long ClientsAccepted => Interlocked.Read(ref _clientsAccepted);

    public long ClientsDropped => Interlocked.Read(ref _clientsDropped);

    /// <summary>Crée la première instance et lance les boucles d'acceptation. Ne bloque pas.</summary>
    public bool Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return IsListening;

        NamedPipeServerStream first;
        try
        {
            first = Create(firstInstance: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"COMMUNITY OVERLAY PIPE | nom déjà utilisé ou refusé : {ex.GetType().Name}");
            return IsListening = false;
        }

        IsListening = true;
        for (int i = 0; i < _maxClients; i++)
        {
            NamedPipeServerStream? initial = i == 0 ? first : null;
            _ = Task.Run(() => AcceptLoopAsync(initial));
        }
        return true;
    }

    /// <summary>Remplace le dernier message et réveille les connexions. Ne bloque jamais.</summary>
    public void Publish(byte[] message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Length > CommunityOverlayPipeProtocol.MaxMessageBytes)
            throw new ArgumentException("Message trop grand.", nameof(message));
        Volatile.Write(ref _latest, message);
        lock (_gate)
        {
            foreach (var signal in _signals)
                if (signal.CurrentCount == 0) signal.Release();
        }
    }

    private NamedPipeServerStream Create(bool firstInstance) => new(
        _pipeName,
        PipeDirection.Out,
        _maxClients,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | (firstInstance ? PipeOptions.FirstPipeInstance : PipeOptions.None));

    private async Task AcceptLoopAsync(NamedPipeServerStream? initial)
    {
        var token = _stop.Token;
        NamedPipeServerStream? server = initial;
        while (!token.IsCancellationRequested)
        {
            try
            {
                server ??= Create(firstInstance: false);
                await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                Interlocked.Increment(ref _clientsAccepted);
                await ServeAsync(server, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException or InvalidOperationException)
            {
                _log?.Invoke($"COMMUNITY OVERLAY PIPE | instance réinitialisée : {ex.GetType().Name}");
                try { await Task.Delay(500, token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
            }
            finally
            {
                server?.Dispose();
                server = null;
            }
        }
    }

    private async Task ServeAsync(NamedPipeServerStream stream, CancellationToken token)
    {
        var signal = new SemaphoreSlim(0, 1);
        lock (_gate) _signals.Add(signal);
        Interlocked.Increment(ref _connected);
        try
        {
            byte[]? lastSent = null;
            while (!token.IsCancellationRequested && stream.IsConnected)
            {
                byte[]? message = Volatile.Read(ref _latest);
                if (message is not null && !ReferenceEquals(message, lastSent))
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(WriteTimeout);
                    try
                    {
                        await CommunityOverlayPipeProtocol.WriteFrameAsync(stream, message, timeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        Interlocked.Increment(ref _clientsDropped);
                        _log?.Invoke("COMMUNITY OVERLAY PIPE | client trop lent, déconnecté");
                        return;
                    }
                    lastSent = message;
                    Interlocked.Increment(ref _messagesWritten);
                }

                // Réveil par Publish, ou au plus tard après un intervalle pour détecter une déconnexion.
                await signal.WaitAsync(CommunityOverlayPipeProtocol.PublishInterval * 4, token).ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
            // Client parti : fin normale de la connexion.
        }
        finally
        {
            Interlocked.Decrement(ref _connected);
            lock (_gate) _signals.Remove(signal);
            signal.Dispose();
        }
    }

    public void Dispose()
    {
        if (_stop.IsCancellationRequested) return;
        _stop.Cancel();
        _stop.Dispose();
    }
}
