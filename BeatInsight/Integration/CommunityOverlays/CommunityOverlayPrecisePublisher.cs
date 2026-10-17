using System.Diagnostics;
using BeatInsight.Integration.CommunityOverlays.Pipe;
using BeatInsight.Integration.KeyOverlay;

namespace BeatInsight.Integration.CommunityOverlays;

/// <summary>Stratégie de publication IPC du canal precise (V3.14-COM-KEY-04).</summary>
public enum PrecisePublishMode
{
    /// <summary>Stratégie B (défaut) : publication à chaque changement d'état, plus un battement de cœur.</summary>
    OnChange,
    /// <summary>Stratégie A (mesure) : un message par échantillon lu (100 Hz).</summary>
    Continuous,
}

/// <summary>
/// Transforme les échantillons du lecteur KeyCounter en messages precise. Appelé uniquement par le
/// thread de la boucle de lecture (pas de concurrence). Ne bloque jamais : la publication
/// délègue à <see cref="CommunityOverlayPipeServer.Publish"/> (le plus récent gagne).
/// Qualifie aussi la transition de partie (premier gameplay, retry, nouvelle map, redémarrage).
/// </summary>
internal sealed class CommunityOverlayPrecisePublisher
{
    private readonly Action<byte[]> publish;
    private readonly Func<DateTimeOffset> clock;
    private readonly Func<long> timestamp;
    private readonly PrecisePublishMode mode;
    private readonly string producerSession;
    private readonly long heartbeatTicks;

    private long sequence;
    private long lastPublishTimestamp;
    private string? lastContentKey;
    private PrecisePipeEnvelope? lastEnvelope;

    private string? currentPlay;
    private string? currentPlayProcess;
    private string? currentPlayBeatmap;
    private string? previousPlayBeatmap;
    private bool anyPlaySeen;
    private bool leftGameplaySinceLastPlay;
    private PreciseTransition currentTransition;

    public CommunityOverlayPrecisePublisher(
        Action<byte[]> publish,
        PrecisePublishMode mode = PrecisePublishMode.OnChange,
        Func<DateTimeOffset>? clock = null,
        Func<long>? timestamp = null,
        string? producerSession = null)
    {
        this.publish = publish;
        this.mode = mode;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        this.timestamp = timestamp ?? Stopwatch.GetTimestamp;
        this.producerSession = producerSession ?? Guid.NewGuid().ToString("N");
        heartbeatTicks = (long)(CommunityOverlayPrecisePipeProtocol.HeartbeatInterval.TotalSeconds * Stopwatch.Frequency);
    }

    public long Samples { get; private set; }

    public long MessagesPublished { get; private set; }

    public long HeartbeatsPublished { get; private set; }

    public long BytesPublished { get; private set; }

    public PrecisePipeEnvelope? LastEnvelope => lastEnvelope;

    public void OnSample(KeyOverlaySample sample)
    {
        Samples++;
        long now = timestamp();
        PreciseTransition transition = Qualify(sample);

        PreciseKeys? keys = sample.IsValid && sample.Keys is { } k
            ? new PreciseKeys(Key(k.K1), Key(k.K2), Key(k.M1), Key(k.M2))
            : null;
        string reason = sample.Reason.ToString();
        string contentKey = string.Join('|',
            sample.Status, reason, sample.ProcessIdentity, sample.BeatmapIdentity, sample.PlayIdentity, transition,
            keys is null ? "-" : $"{keys.K1.Count},{keys.K1.IsPressed},{keys.K2.Count},{keys.K2.IsPressed}," +
                                 $"{keys.M1.Count},{keys.M1.IsPressed},{keys.M2.Count},{keys.M2.IsPressed}");

        bool changed = !string.Equals(contentKey, lastContentKey, StringComparison.Ordinal);
        bool heartbeatDue = lastPublishTimestamp == 0 || now - lastPublishTimestamp >= heartbeatTicks;
        if (mode == PrecisePublishMode.OnChange && !changed && !heartbeatDue)
            return;

        DateTimeOffset sentAt = clock();
        // ObservedTimestamp (horloge monotone) → horloge murale, pour mesurer la latence côté compagnon.
        DateTimeOffset observedAt = sentAt - TimeSpan.FromSeconds(
            Math.Max(0, now - sample.ObservedTimestamp) / (double)Stopwatch.Frequency);
        var envelope = new PrecisePipeEnvelope(
            CommunityOverlayPrecisePipeProtocol.Name,
            CommunityOverlayPrecisePipeProtocol.Version,
            producerSession,
            ++sequence,
            sentAt,
            Heartbeat: !changed,
            sample.IsValid ? PreciseValidity.Valid : PreciseValidity.Unknown,
            reason,
            Trim(sample.ProcessIdentity),
            Trim(sample.BeatmapIdentity),
            Trim(sample.PlayIdentity),
            sample.IsValid ? transition : PreciseTransition.None,
            sample.PlayTimeMs,
            observedAt,
            keys);
        byte[] bytes = CommunityOverlayPrecisePipeProtocol.Serialize(envelope);
        publish(bytes);
        lastEnvelope = envelope;
        lastContentKey = contentKey;
        lastPublishTimestamp = now;
        MessagesPublished++;
        BytesPublished += bytes.Length;
        if (!changed) HeartbeatsPublished++;
    }

    /// <summary>Comment la partie courante a commencé (identités séparées, jamais d'adresse mémoire).</summary>
    private PreciseTransition Qualify(KeyOverlaySample sample)
    {
        if (!sample.IsValid)
        {
            if (sample.Reason is KeyOverlayReadFailureReason.RouteNotGameplay)
                leftGameplaySinceLastPlay = true;
            return currentTransition;
        }

        if (!string.Equals(sample.PlayIdentity, currentPlay, StringComparison.Ordinal))
        {
            currentTransition =
                !anyPlaySeen ? PreciseTransition.FirstPlay :
                !string.Equals(sample.ProcessIdentity, currentPlayProcess, StringComparison.Ordinal) ? PreciseTransition.ProcessChanged :
                BeatmapDiffers(sample.BeatmapIdentity, currentPlayBeatmap) ? PreciseTransition.BeatmapChanged :
                leftGameplaySinceLastPlay ? PreciseTransition.NewGameplay :
                PreciseTransition.Retry;
            previousPlayBeatmap = currentPlayBeatmap;
            currentPlay = sample.PlayIdentity;
            currentPlayProcess = sample.ProcessIdentity;
            currentPlayBeatmap = sample.BeatmapIdentity;
            anyPlaySeen = true;
            leftGameplaySinceLastPlay = false;
        }
        else if (!string.Equals(sample.BeatmapIdentity, currentPlayBeatmap, StringComparison.Ordinal))
        {
            // Identité de beatmap arrivée avec un rafraîchissement de retard : la qualification est révisée.
            currentPlayBeatmap = sample.BeatmapIdentity;
            if (currentTransition == PreciseTransition.NewGameplay && BeatmapDiffers(currentPlayBeatmap, previousPlayBeatmap))
                currentTransition = PreciseTransition.BeatmapChanged;
        }
        return currentTransition;
    }

    private static bool BeatmapDiffers(string? a, string? b) =>
        a is not null && b is not null && !string.Equals(a, b, StringComparison.Ordinal);

    private static PreciseKey Key(KeyCounterState state) => new(state.Count, state.IsPressed);

    private static string? Trim(string? value) =>
        value is null ? null :
        value.Length <= CommunityOverlayPrecisePipeProtocol.MaxIdentityLength ? value :
        value[^CommunityOverlayPrecisePipeProtocol.MaxIdentityLength..];
}
