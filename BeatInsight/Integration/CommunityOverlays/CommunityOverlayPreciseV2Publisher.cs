using System.Diagnostics;
using System.Globalization;
using BeatInsight.Integration.CommunityOverlays.Pipe;
using BeatInsight.Integration.Game;
using BeatInsight.Integration.KeyOverlay;
using BeatInsight.Integration.KeyOverlay.Lazer;

namespace BeatInsight.Integration.CommunityOverlays;

/// <summary>
/// V3.14-COM-LAZER-KEY-03 (expérimental, opt-in) : publication precise v2, Stable et Lazer, sur un pipe distinct du v1.
/// <list type="bullet">
/// <item><b>Stable</b> : les échantillons du canal precise existant passent par une instance dédiée de
/// <see cref="CommunityOverlayPrecisePublisher"/> (mêmes transitions et identités que le v1, sans modifier le v1), puis sont
/// recopiés en v2 avec K1/K2/M1/M2 = Native / Key.</item>
/// <item><b>Lazer</b> : échantillons du processus lecteur ; K1 = Native / LeftAction, K2 = Native / RightAction,
/// M1/M2 = Unavailable (compteur null).</item>
/// <item><b>Arbitrage</b> : une seule source par message. La source est le jeu de <c>GameStateService.Current</c> (le même
/// que pour tous les autres overlays) ; ses échantillons doivent être récents (&lt; 1 s) et, pour Lazer, du même processus.
/// Si Stable et Lazer sont tous deux en partie, ou si la source attendue est silencieuse : Unknown, sans touches.</item>
/// </list>
/// Thread-safe : appelé par la boucle precise (Stable), le thread du lecteur Lazer et le minuteur de battement.
/// </summary>
internal sealed class CommunityOverlayPreciseV2Publisher : IDisposable
{
    public const string StableClient = "osu!stable";
    public const string LazerClient = "osu!lazer";
    private static readonly TimeSpan SourceMaxAge = TimeSpan.FromSeconds(1);

    private readonly Action<byte[]> publish;
    private readonly Func<GameStateSnapshot> game;
    private readonly Func<DateTimeOffset> clock;
    private readonly string producerSession;
    private readonly CommunityOverlayPrecisePublisher stableShadow;
    private readonly object gate = new();
    private Timer? heartbeat;
    private long sequence;
    private long stableShadowSequence;
    private (PrecisePipeEnvelope Envelope, DateTimeOffset At)? stable;
    private (LazerKeyWireSample Sample, DateTimeOffset At)? lazer;
    private string? lastContentKey;
    private DateTimeOffset lastPublish;
    private bool disposed;

    public CommunityOverlayPreciseV2Publisher(
        Action<byte[]> publish,
        Func<GameStateSnapshot> game,
        Func<DateTimeOffset>? clock = null,
        string? producerSession = null)
    {
        this.publish = publish;
        this.game = game;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        this.producerSession = producerSession ?? Guid.NewGuid().ToString("N");
        // Instance dédiée : mêmes règles d'identité et de transition que le v1, aucune publication propre.
        stableShadow = new CommunityOverlayPrecisePublisher(static _ => { }, PrecisePublishMode.OnChange, this.clock);
    }

    public long MessagesPublished { get; private set; }
    public long HeartbeatsPublished { get; private set; }
    public long AmbiguousMessages { get; private set; }
    public long StaleSourceMessages { get; private set; }
    public PreciseV2Envelope? LastEnvelope { get; private set; }

    public void Start() =>
        heartbeat ??= new Timer(_ => Evaluate(), null, CommunityOverlayPreciseV2PipeProtocol.HeartbeatInterval,
            CommunityOverlayPreciseV2PipeProtocol.HeartbeatInterval);

    /// <summary>Échantillon Stable du canal precise (thread de la boucle 100 Hz).</summary>
    public void OnStableSample(KeyOverlaySample sample)
    {
        lock (gate)
        {
            if (disposed) return;
            stableShadow.OnSample(sample);
            if (stableShadow.LastEnvelope is { } env && env.Sequence != stableShadowSequence)
            {
                stableShadowSequence = env.Sequence;
                stable = (env, clock());
            }
        }
        Evaluate();
    }

    /// <summary>Échantillon Lazer validé (thread de lecture du processus lecteur).</summary>
    public void OnLazerSample(LazerKeyWireSample sample, DateTimeOffset receivedAt)
    {
        lock (gate)
        {
            if (disposed) return;
            lazer = (sample, receivedAt);
        }
        Evaluate();
    }

    public void Evaluate()
    {
        lock (gate)
        {
            if (disposed) return;
            DateTimeOffset now = clock();
            PreciseV2Envelope envelope = Build(game(), now);
            string contentKey = ContentKey(envelope);
            bool changed = contentKey != lastContentKey;
            if (!changed && now - lastPublish < CommunityOverlayPreciseV2PipeProtocol.HeartbeatInterval)
                return;
            envelope = envelope with { Sequence = ++sequence, Heartbeat = !changed };
            if (CommunityOverlayPreciseV2PipeProtocol.Validate(envelope) is not null)
                envelope = Unknown(envelope.Source, "message hors contrat", envelope.ProcessIdentity, now) with { Sequence = envelope.Sequence };
            byte[] bytes = CommunityOverlayPreciseV2PipeProtocol.Serialize(envelope);
            // Publication sous le verrou : les séquences partent dans l'ordre (le serveur est non bloquant, latest-wins).
            publish(bytes);
            LastEnvelope = envelope;
            lastContentKey = contentKey;
            lastPublish = now;
            MessagesPublished++;
            if (!changed) HeartbeatsPublished++;
        }
    }

    /// <summary>Choix de la source et construction du message (sans séquence). Public pour les tests.</summary>
    internal PreciseV2Envelope Build(GameStateSnapshot current, DateTimeOffset now)
    {
        var stableNow = stable is { } s && now - s.At <= SourceMaxAge ? s.Envelope : null;
        var lazerNow = lazer is { } l && now - l.At <= SourceMaxAge ? l.Sample : null;
        bool stableValid = stableNow?.Validity == PreciseValidity.Valid;
        bool lazerValid = lazerNow?.Validity == "Valid";

        if (stableValid && lazerValid)
        {
            AmbiguousMessages++;
            return Unknown(PreciseSource.Unknown, "SourcesAmbiguous", null, now);
        }

        if (string.Equals(current.ClientName, StableClient, StringComparison.Ordinal))
        {
            if (lazerValid)
            {
                AmbiguousMessages++;
                return Unknown(PreciseSource.Unknown, "LazerPlayingWhileStableSelected", null, now);
            }
            if (stableNow is null)
            {
                StaleSourceMessages++;
                return Unknown(PreciseSource.Stable, "StableSourceSilent", current.ProcessIdentity, now);
            }
            return FromStable(stableNow);
        }

        if (string.Equals(current.ClientName, LazerClient, StringComparison.Ordinal))
        {
            if (stableValid)
            {
                AmbiguousMessages++;
                return Unknown(PreciseSource.Unknown, "StablePlayingWhileLazerSelected", null, now);
            }
            if (lazerNow is null)
            {
                StaleSourceMessages++;
                return Unknown(PreciseSource.Lazer, "LazerSourceSilent", current.ProcessIdentity, now);
            }
            // Le lecteur et GameStateService doivent parler du même processus Lazer.
            if (lazerNow.ProcessIdentity is not null && current.ProcessId is int pid && ProcessIdOf(lazerNow.ProcessIdentity) != pid)
                return Unknown(PreciseSource.Lazer, "LazerProcessMismatch", lazerNow.ProcessIdentity, now);
            return FromLazer(lazerNow, current);
        }

        return Unknown(PreciseSource.Unknown, "NoGame", null, now);
    }

    private PreciseV2Envelope FromStable(PrecisePipeEnvelope v1)
    {
        PreciseV2Keys? keys = v1.Validity == PreciseValidity.Valid && v1.Keys is { } k
            ? new PreciseV2Keys(StableKey(k.K1), StableKey(k.K2), StableKey(k.M1), StableKey(k.M2))
            : null;
        return new PreciseV2Envelope(
            CommunityOverlayPreciseV2PipeProtocol.Name, CommunityOverlayPreciseV2PipeProtocol.Version, producerSession, 0,
            clock(), false, PreciseSource.Stable, keys is null ? PreciseValidity.Unknown : PreciseValidity.Valid, v1.Reason,
            v1.ProcessIdentity, v1.BeatmapIdentity, keys is null ? null : v1.PlayIdentity,
            keys is null ? PreciseTransition.None : v1.Transition, v1.PlayTimeMs, v1.ObservedAtUtc, keys);

        static PreciseV2Key StableKey(PreciseKey key) => new(PreciseAvailability.Native, PreciseSemantics.Key, key.Count, key.IsPressed);
    }

    private PreciseV2Envelope FromLazer(LazerKeyWireSample s, GameStateSnapshot current)
    {
        bool valid = s.Validity == "Valid" && s.LeftCount is not null && s.RightCount is not null && s.PlayIdentity is not null;
        PreciseV2Keys? keys = valid
            ? new PreciseV2Keys(
                // État pressé de K1/K2 = action active (même sémantique que le compteur : clavier, souris ou stylet).
                new(PreciseAvailability.Native, PreciseSemantics.LeftAction, s.LeftCount, s.LeftActive),
                new(PreciseAvailability.Native, PreciseSemantics.RightAction, s.RightCount, s.RightActive),
                // M1/M2 : aucun compteur ; état pressé physique du bouton de souris lié à l'action (informatif).
                new(PreciseAvailability.Unavailable, PreciseSemantics.None, null, s.M1Pressed),
                new(PreciseAvailability.Unavailable, PreciseSemantics.None, null, s.M2Pressed))
            : null;
        // Beatmap : celle de GameStateService, seulement pour le même processus (vérifié par l'appelant).
        string? beatmap = current.CurrentBeatmapPath ?? (current.CurrentBeatmapId is { } id ? $"id:{id}" : null);
        PreciseTransition transition = valid && Enum.TryParse(s.Transition, out PreciseTransition t) ? t : PreciseTransition.None;
        return new PreciseV2Envelope(
            CommunityOverlayPreciseV2PipeProtocol.Name, CommunityOverlayPreciseV2PipeProtocol.Version, producerSession, 0,
            clock(), false, PreciseSource.Lazer, valid ? PreciseValidity.Valid : PreciseValidity.Unknown,
            Clip(s.Validity == "Valid" ? "ok" : $"{s.Validity}/{s.Failure}"),
            Clip(s.ProcessIdentity, CommunityOverlayPreciseV2PipeProtocol.MaxIdentityLength),
            Clip(beatmap, CommunityOverlayPreciseV2PipeProtocol.MaxIdentityLength),
            valid ? Clip(s.PlayIdentity, CommunityOverlayPreciseV2PipeProtocol.MaxIdentityLength) : null,
            transition, null, s.ObservedAtUtc, keys);
    }

    private PreciseV2Envelope Unknown(PreciseSource source, string reason, string? process, DateTimeOffset now) => new(
        CommunityOverlayPreciseV2PipeProtocol.Name, CommunityOverlayPreciseV2PipeProtocol.Version, producerSession, 0,
        now, false, source, PreciseValidity.Unknown, reason,
        Clip(process, CommunityOverlayPreciseV2PipeProtocol.MaxIdentityLength), null, null, PreciseTransition.None, null, now, null);

    private static int? ProcessIdOf(string identity)
    {
        int end = identity.IndexOfAny(['@', ':']);
        return end > 0 && int.TryParse(identity.AsSpan(0, end), NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid) ? pid : null;
    }

    private static string ContentKey(PreciseV2Envelope e) =>
        string.Join('|', e.Source, e.Validity, e.Reason, e.ProcessIdentity, e.BeatmapIdentity, e.PlayIdentity, e.Transition,
            e.Keys is null ? "-" : string.Join(',', new[] { e.Keys.K1, e.Keys.K2, e.Keys.M1, e.Keys.M2 }.Select(k => $"{k.Availability}{k.Count}{k.IsPressed}")));

    private static string Clip(string text) =>
        text.Length > CommunityOverlayPreciseV2PipeProtocol.MaxReasonLength ? text[..CommunityOverlayPreciseV2PipeProtocol.MaxReasonLength] : text;

    private static string? Clip(string? text, int max) =>
        text is null || text.Length == 0 ? null : text.Length > max ? text[..max] : text;

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
        }
        heartbeat?.Dispose();
    }
}
