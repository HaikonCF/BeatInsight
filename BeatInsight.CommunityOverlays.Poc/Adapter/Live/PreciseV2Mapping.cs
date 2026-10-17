using BeatInsight.Integration.CommunityOverlays.Pipe;

namespace BeatInsight.CommunityOverlays.Poc.Adapter.Live;

/// <summary>
/// V3.14-COM-LAZER-KEY-03 : message precise v2 → entrée du <see cref="PreciseKeyChannel"/> existant (politique de reset
/// COM-KEY-07 inchangée). Règles :
/// <list type="bullet">
/// <item>Compteur <c>Native</c> : recopié tel quel.</item>
/// <item>Compteur <c>Unavailable</c> (Lazer M1/M2) : valeur interne constante 0 pour toute la partie. Le canal n'affiche une
/// touche qu'après un incrément réel observé : une touche indisponible n'est donc jamais affichée, n'anime rien et ne
/// produit aucun BPM. Le contrat, lui, garde le compteur null.</item>
/// <item>État pressé inconnu (null) : relâché.</item>
/// <item>Identités préfixées par la source (Stable / Lazer) et la session par « v2: » : un changement de source ou de canal
/// est toujours une nouvelle partie pour le canal, jamais une continuation de compteurs.</item>
/// </list>
/// </summary>
public static class PreciseV2Mapping
{
    /// <summary>
    /// Un échantillon Lazer valide est émis uniquement quand le lecteur natif a résolu une
    /// partie active. Il peut donc réconcilier l'état v2 général sans déduire de gameplay
    /// depuis les compteurs et sans accepter un échantillon Unknown/NotGameplay.
    /// </summary>
    public static bool ProvesLazerGameplay(PreciseV2Envelope e) =>
        e.Source == PreciseSource.Lazer
        && e.Validity == PreciseValidity.Valid
        && e.PlayIdentity is not null
        && e.Keys is not null;

    public static PrecisePipeEnvelope ToChannel(PreciseV2Envelope e)
    {
        PreciseKeys? keys = e.Validity == PreciseValidity.Valid && e.Keys is { } k
            ? new PreciseKeys(Key(k.K1), Key(k.K2), Key(k.M1), Key(k.M2))
            : null;
        return new PrecisePipeEnvelope(
            CommunityOverlayPrecisePipeProtocol.Name,
            CommunityOverlayPrecisePipeProtocol.Version,
            "v2:" + e.ProducerSession,
            e.Sequence,
            e.SentAtUtc,
            e.Heartbeat,
            keys is null ? PreciseValidity.Unknown : PreciseValidity.Valid,
            e.Reason,
            Prefix(e.Source, e.ProcessIdentity),
            e.BeatmapIdentity,
            keys is null ? null : Prefix(e.Source, e.PlayIdentity),
            keys is null ? PreciseTransition.None : e.Transition,
            e.PlayTimeMs,
            e.ObservedAtUtc,
            keys);
    }

    private static PreciseKey Key(PreciseV2Key key) => new(
        key.Availability == PreciseAvailability.Native && key.Count is int count ? count : 0,
        key.IsPressed == true && key.Availability == PreciseAvailability.Native);

    private static string? Prefix(PreciseSource source, string? identity) => identity is null ? null : $"{source}|{identity}";
}
