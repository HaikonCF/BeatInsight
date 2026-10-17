using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using BeatInsight.CommunityOverlays.Poc.Adapter.Live;
using BeatInsight.Integration.CommunityOverlays.Pipe;

namespace BeatInsight.CommunityOverlays.Poc.Bridge;

/// <summary>
/// Raccorde le pipe precise au bridge tosu (V3.14-COM-KEY-04, opt-in <c>--precise-pipe</c>).
/// Chaque message reçu est traité dès son arrivée (thread du pipe) par <see cref="PreciseKeyChannel"/>,
/// puis posté sur le thread UI sans attendre le tick gameplay. v2 et precise passent tous deux par
/// le thread UI et le même canal WebView2, ce qui fixe leur ordre d'arrivée dans la page ; la
/// politique de reset s'appuie sur cet ordre (purge v2 postée avant toute réintroduction de touche).
/// </summary>
public sealed class PreciseBridgeConnector : IDisposable
{
    private readonly TosuCompatibleBridge _bridge;
    private readonly PrecisePipeClient _client;
    private readonly PreciseKeyChannel _channel;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _watchdog;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _statsGate = new();
    private readonly List<double> _pipeLatencyMs = [];
    private readonly List<double> _postLatencyMs = [];
    private long _lastReceiptTicks = -1;
    private long _postsQueued;
    private bool _disposed;

    // V3.14-COM-LAZER-KEY-03 (opt-in --precise-v2) : v2 prioritaire, v1 en repli, jamais les deux à la fois.
    private readonly PreciseV2PipeClient? _v2;
    private long _lastV2ReceiptTicks = -1;
    private long _v1Ignored;
    private long _v2Envelopes;
    private int _lazerGameplayEvidence = -1;
    private PreciseV2Envelope? _lastV2Envelope;

    public PreciseBridgeConnector(TosuCompatibleBridge bridge, PrecisePipeClient client, PreciseKeyChannel channel,
        PreciseV2PipeClient? v2 = null)
    {
        _v2 = v2;
        _bridge = bridge;
        _client = client;
        _channel = channel;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _bridge.PreciseFromSnapshot = false;
        _bridge.OverlayNavigationCompleted += OnNavigationCompleted;
        _bridge.V2Posted += _channel.OnV2Posted;
        _client.EnvelopeReceived += OnEnvelope;
        _client.ConnectionLost += OnLost;
        if (_v2 is not null)
        {
            _v2.EnvelopeReceived += OnV2Envelope;
            _v2.ConnectionLost += OnV2Lost;
        }
        _watchdog = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(250) };
        _watchdog.Tick += (_, _) => CheckSilence();
        _watchdog.Start();
        _client.Start();
        _v2?.Start();
    }

    /// <summary>
    /// v2 active : connectée et un message reçu depuis moins du délai de réception. Tant qu'elle l'est, le v1 est ignoré ;
    /// le repli sur le v1 ne se fait qu'après ce délai, et le changement de canal ouvre une nouvelle partie (préfixes).
    /// </summary>
    public bool V2Active
    {
        get
        {
            long last = Interlocked.Read(ref _lastV2ReceiptTicks);
            double? messageAgeMilliseconds = last >= 0
                ? (_clock.ElapsedTicks - last) * 1000.0 / Stopwatch.Frequency
                : null;
            return PreciseV2PriorityPolicy.IsV2Active(
                _v2 is { IsConnected: true },
                messageAgeMilliseconds,
                CommunityOverlayPreciseV2PipeProtocol.MaxMessageAge.TotalMilliseconds);
        }
    }

    private void OnV2Envelope(PreciseV2Envelope envelope, DateTimeOffset receivedAt)
    {
        Interlocked.Exchange(ref _lastV2ReceiptTicks, _clock.ElapsedTicks);
        Interlocked.Increment(ref _v2Envelopes);
        _lastV2Envelope = envelope;
        QueueLazerGameplayEvidence(PreciseV2Mapping.ProvesLazerGameplay(envelope));
        Handle(PreciseV2Mapping.ToChannel(envelope), receivedAt);
    }

    private void OnV2Lost()
    {
        Interlocked.Exchange(ref _lastV2ReceiptTicks, -1);
        QueueLazerGameplayEvidence(false);
        OnLost();
    }

    private void QueueLazerGameplayEvidence(bool active)
    {
        int value = active ? 1 : 0;
        if (Interlocked.Exchange(ref _lazerGameplayEvidence, value) == value) return;
        _dispatcher.BeginInvoke(DispatcherPriority.Send, () =>
        {
            if (!_disposed) _bridge.SetPreciseLazerGameplay(active);
        });
    }

    public PrecisePipeClient Client => _client;
    public PreciseKeyChannel Channel => _channel;

    private void OnEnvelope(PrecisePipeEnvelope envelope, DateTimeOffset receivedAt)
    {
        if (V2Active)
        {
            Interlocked.Increment(ref _v1Ignored);
            return;
        }
        Handle(envelope, receivedAt);
    }

    private void Handle(PrecisePipeEnvelope envelope, DateTimeOffset receivedAt)
    {
        Interlocked.Exchange(ref _lastReceiptTicks, _clock.ElapsedTicks);
        double pipeLatency = (receivedAt - envelope.ObservedAtUtc).TotalMilliseconds;
        string? payload = _channel.OnEnvelope(envelope);
        lock (_statsGate) Bounded(_pipeLatencyMs, pipeLatency);
        if (_channel.ConsumePageReset())
        {
            // COM-KEY-07 : nouvelle partie avec des compteurs déjà affichés → page vierge.
            // Même file que les posts (thread UI, ordre conservé) : le reset précède tout payload suivant.
            _dispatcher.BeginInvoke(DispatcherPriority.Send, () =>
            {
                if (_disposed) return;
                _bridge.ReloadOverlayPage();
            });
        }
        if (payload is null) return;
        Post(payload, envelope.ObservedAtUtc);
    }

    /// <summary>Après un (re)chargement, la page reçoit immédiatement le dernier état precise connu.</summary>
    private void OnNavigationCompleted()
    {
        if (_disposed) return;
        _bridge.PublishPrecise(_bridge.LastPrecise);
    }

    private void OnLost()
    {
        string? payload = _channel.OnSourceLost();
        if (payload is not null) Post(payload, null);
    }

    /// <summary>Source muette au-delà du délai de réception : relâchement (sans changement de compteur).</summary>
    private void CheckSilence()
    {
        if (_v2 is not null && !V2Active)
            QueueLazerGameplayEvidence(false);
        long last = Interlocked.Read(ref _lastReceiptTicks);
        if (last < 0) return;
        double silentMs = (_clock.ElapsedTicks - last) * 1000.0 / Stopwatch.Frequency;
        if (silentMs < CommunityOverlayPrecisePipeProtocol.ReceiveTimeout.TotalMilliseconds) return;
        string? payload = _channel.OnSourceLost();
        if (payload is not null) _bridge.PublishPrecise(payload);
    }

    private void Post(string payload, DateTimeOffset? observedAt)
    {
        Interlocked.Increment(ref _postsQueued);
        _dispatcher.BeginInvoke(DispatcherPriority.Send, () =>
        {
            if (_disposed) return;
            _bridge.PublishPrecise(payload);
            if (observedAt is DateTimeOffset at)
                lock (_statsGate) Bounded(_postLatencyMs, (DateTimeOffset.UtcNow - at).TotalMilliseconds);
        });
    }

    private static void Bounded(List<double> list, double value)
    {
        if (list.Count >= 20_000) list.RemoveRange(0, 10_000);
        list.Add(value);
    }

    public JsonObject Stats()
    {
        lock (_statsGate)
        {
            return new JsonObject
            {
                ["client"] = new JsonObject
                {
                    ["connected"] = _client.IsConnected,
                    ["connections"] = _client.Connections,
                    ["received"] = _client.Received,
                    ["rejected"] = _client.Rejected,
                    ["stale"] = _client.Stale,
                    ["outOfOrder"] = _client.OutOfOrder,
                    ["bytes"] = _client.BytesReceived,
                    ["lastRejection"] = _client.LastRejection,
                },
                ["channel"] = new JsonObject
                {
                    ["valid"] = _channel.ValidEnvelopes,
                    ["unknown"] = _channel.UnknownEnvelopes,
                    ["newPlays"] = _channel.NewPlays,
                    ["lastTransition"] = _channel.LastTransition.ToString(),
                    ["releases"] = _channel.Releases,
                    ["payloads"] = _channel.PayloadsEmitted,
                    ["withheldKeyUpdates"] = _channel.WithheldKeyUpdates,
                    ["purgeTimeouts"] = _channel.PurgeTimeouts,
                    ["pageResets"] = _channel.PageResets,
                    ["emittedCounts"] = new JsonArray(_channel.EmittedCounts.Select(c => (JsonNode)c).ToArray()),
                },
                ["v2"] = _v2 is null ? null : new JsonObject
                {
                    ["active"] = V2Active,
                    ["connected"] = _v2.IsConnected,
                    ["received"] = _v2.Received,
                    ["rejected"] = _v2.Rejected,
                    ["stale"] = _v2.Stale,
                    ["outOfOrder"] = _v2.OutOfOrder,
                    ["lastRejection"] = _v2.LastRejection,
                    ["envelopes"] = Interlocked.Read(ref _v2Envelopes),
                    ["v1Ignored"] = Interlocked.Read(ref _v1Ignored),
                    ["lastEnvelope"] = V2EnvelopeDiagnostics(_lastV2Envelope),
                },
                ["postsQueued"] = Interlocked.Read(ref _postsQueued),
                ["precisePosted"] = _bridge.PrecisePublished,
                ["overlayReloads"] = _bridge.OverlayReloads,
                ["latencySourceToPipeMs"] = Summary(_pipeLatencyMs),
                ["latencySourceToPostMs"] = Summary(_postLatencyMs),
            };
        }
    }

    private static JsonObject? V2EnvelopeDiagnostics(PreciseV2Envelope? e) => e is null ? null : new JsonObject
    {
        ["source"] = e.Source.ToString(),
        ["validity"] = e.Validity.ToString(),
        ["reason"] = e.Reason,
        ["processIdentity"] = e.ProcessIdentity,
        ["beatmapIdentity"] = e.BeatmapIdentity,
        ["playIdentity"] = e.PlayIdentity,
        ["transition"] = e.Transition.ToString(),
        ["k1"] = e.Keys?.K1.Count,
        ["k2"] = e.Keys?.K2.Count,
        ["m1Availability"] = e.Keys?.M1.Availability.ToString(),
        ["m2Availability"] = e.Keys?.M2.Availability.ToString(),
    };

    private static JsonObject Summary(List<double> values)
    {
        if (values.Count == 0) return new JsonObject { ["n"] = 0 };
        double[] sorted = values.OrderBy(v => v).ToArray();
        return new JsonObject
        {
            ["n"] = sorted.Length,
            ["mean"] = Math.Round(values.Average(), 3),
            ["p50"] = Math.Round(sorted[sorted.Length / 2], 3),
            ["p95"] = Math.Round(sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * 0.95) - 1)], 3),
            ["max"] = Math.Round(sorted[^1], 3),
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watchdog.Stop();
        _client.EnvelopeReceived -= OnEnvelope;
        _client.ConnectionLost -= OnLost;
        _bridge.V2Posted -= _channel.OnV2Posted;
        _bridge.OverlayNavigationCompleted -= OnNavigationCompleted;
        _client.Dispose();
        if (_v2 is not null)
        {
            _v2.EnvelopeReceived -= OnV2Envelope;
            _v2.ConnectionLost -= OnV2Lost;
            _v2.Dispose();
        }
    }
}
