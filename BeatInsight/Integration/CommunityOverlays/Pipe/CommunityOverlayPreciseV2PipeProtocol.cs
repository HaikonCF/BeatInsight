using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeatInsight.Integration.CommunityOverlays.Pipe;

// Contrat precise v2 BeatInsight → compagnon (V3.14-COM-LAZER-KEY-03).
// Pipe distinct du v1 : le v1 (BeatInsight.CommunityOverlays.precise.v1) reste publié à l'identique pour osu!stable.
// La v2 ajoute la source (Stable / Lazer) et, pour chaque touche, la disponibilité et la sémantique du compteur :
//   Stable : K1/K2/M1/M2 = Native / Key (compteurs natifs par touche, mêmes valeurs que la v1) ;
//   Lazer  : K1 = Native / LeftAction, K2 = Native / RightAction (clavier, souris et stylet confondus),
//            M1/M2 = Unavailable : compteur null, jamais un zéro.
// Comme les autres contrats, ce fichier n'a aucune dépendance vers le reste de BeatInsight (lié dans le compagnon).

public static class CommunityOverlayPreciseV2PipeProtocol
{
    public const string Name = "beatinsight.community-overlays.precise";

    public const int Version = 2;

    /// <summary>Nom du pipe local v2. Accès restreint à l'utilisateur courant (CurrentUserOnly).</summary>
    public const string PipeName = "BeatInsight.CommunityOverlays.precise.v2";

    public const int MaxMessageBytes = 4 * 1024;

    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMilliseconds(250);

    public static readonly TimeSpan MaxMessageAge = TimeSpan.FromSeconds(1);

    public const int MaxIdentityLength = 256;
    public const int MaxReasonLength = 64;
    public const int MaxCount = 1_000_000;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        MaxDepth = 8,
    };

    public static byte[] Serialize(PreciseV2Envelope envelope)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        if (bytes.Length > MaxMessageBytes)
            throw new InvalidOperationException("Message precise v2 trop grand.");
        return bytes;
    }

    /// <summary>Désérialise et valide. Null + raison si le message doit être ignoré. Ne lève jamais.</summary>
    public static PreciseV2Envelope? TryDeserialize(ReadOnlySpan<byte> json, out string? rejection)
    {
        if (json.Length == 0 || json.Length > MaxMessageBytes)
        {
            rejection = "taille";
            return null;
        }
        PreciseV2Envelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<PreciseV2Envelope>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            rejection = "json";
            return null;
        }
        if (envelope is null)
        {
            rejection = "json";
            return null;
        }
        rejection = Validate(envelope);
        return rejection is null ? envelope : null;
    }

    /// <summary>Null si le message est valide, sinon la raison du rejet.</summary>
    public static string? Validate(PreciseV2Envelope e)
    {
        if (!string.Equals(e.Protocol, Name, StringComparison.Ordinal)) return "protocole";
        if (e.Version != Version) return $"version {e.Version}";
        if (e.Sequence < 1) return "séquence";
        if (!Text(e.ProducerSession, required: true)) return "session";
        if (!Text(e.ProcessIdentity, required: false) || !Text(e.BeatmapIdentity, required: false) ||
            !Text(e.PlayIdentity, required: false))
            return "identité";
        if (e.Reason is null || e.Reason.Length > MaxReasonLength || e.Reason.Any(char.IsControl)) return "raison";
        if (e.PlayTimeMs is int t && (t < -60_000 || t > 24 * 3_600_000)) return "temps";
        if (!Enum.IsDefined(e.Source) || !Enum.IsDefined(e.Validity) || !Enum.IsDefined(e.Transition)) return "énumération";

        if (e.Validity == PreciseValidity.Valid)
        {
            if (e.Source == PreciseSource.Unknown) return "source inconnue";
            if (e.Keys is null || e.PlayIdentity is null || e.ProcessIdentity is null) return "touches absentes";
            PreciseV2Key[] keys = [e.Keys.K1, e.Keys.K2, e.Keys.M1, e.Keys.M2];
            if (keys.Any(k => k is null)) return "touches";
            foreach (PreciseV2Key k in keys)
                if (KeyRejection(k) is { } r) return r;
            // Sémantique imposée par la source : aucune combinaison ambiguë n'est acceptée.
            if (e.Source == PreciseSource.Stable &&
                keys.Any(k => k.Availability != PreciseAvailability.Native || k.Semantics != PreciseSemantics.Key))
                return "sémantique Stable";
            if (e.Source == PreciseSource.Lazer &&
                (e.Keys.K1.Availability != PreciseAvailability.Native || e.Keys.K1.Semantics != PreciseSemantics.LeftAction ||
                 e.Keys.K2.Availability != PreciseAvailability.Native || e.Keys.K2.Semantics != PreciseSemantics.RightAction ||
                 e.Keys.M1.Availability != PreciseAvailability.Unavailable || e.Keys.M1.Semantics != PreciseSemantics.None ||
                 e.Keys.M2.Availability != PreciseAvailability.Unavailable || e.Keys.M2.Semantics != PreciseSemantics.None))
                return "sémantique Lazer";
        }
        else if (e.Keys is not null)
        {
            return "touches sur un échantillon non valide";
        }
        return null;

        static bool Text(string? value, bool required) =>
            value is null ? !required : value.Length is > 0 and <= MaxIdentityLength && !value.Any(char.IsControl);
    }

    private static string? KeyRejection(PreciseV2Key k)
    {
        if (!Enum.IsDefined(k.Availability) || !Enum.IsDefined(k.Semantics)) return "énumération de touche";
        return k.Availability switch
        {
            // Un compteur natif a toujours une valeur bornée.
            PreciseAvailability.Native when k.Count is not (>= 0 and <= MaxCount) => "compteur natif",
            // Indisponible : jamais de valeur (un zéro serait faux).
            PreciseAvailability.Unavailable when k.Count is not null => "compteur indisponible avec valeur",
            PreciseAvailability.Unknown when k.Count is not null => "compteur inconnu avec valeur",
            _ => null,
        };
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<PreciseSource>))]
public enum PreciseSource
{
    Unknown,
    Stable,
    Lazer,
}

[JsonConverter(typeof(JsonStringEnumConverter<PreciseAvailability>))]
public enum PreciseAvailability
{
    /// <summary>Compteur tenu par le jeu, lu en mémoire.</summary>
    Native,

    /// <summary>Le jeu ne tient pas ce compteur (Lazer : M1/M2). Compteur toujours null.</summary>
    Unavailable,

    /// <summary>Compteur qui existe mais n'a pas pu être lu pour ce message. Compteur null.</summary>
    Unknown,
}

[JsonConverter(typeof(JsonStringEnumConverter<PreciseSemantics>))]
public enum PreciseSemantics
{
    None,

    /// <summary>Stable : compteur de la touche (K1, K2, M1 ou M2).</summary>
    Key,

    /// <summary>Lazer : appuis reconnus pour l'action gauche, toutes entrées confondues.</summary>
    LeftAction,

    /// <summary>Lazer : appuis reconnus pour l'action droite, toutes entrées confondues.</summary>
    RightAction,
}

/// <summary>Une touche : disponibilité et sens du compteur, compteur (null si non natif), état pressé (null = inconnu).</summary>
public sealed record PreciseV2Key(PreciseAvailability Availability, PreciseSemantics Semantics, int? Count, bool? IsPressed);

public sealed record PreciseV2Keys(PreciseV2Key K1, PreciseV2Key K2, PreciseV2Key M1, PreciseV2Key M2);

/// <summary>
/// Message precise v2 : état complet (jamais de delta). <see cref="Sequence"/> croît strictement pour un même
/// <see cref="ProducerSession"/>. <see cref="Keys"/> est nul dès que <see cref="Validity"/> n'est pas Valid.
/// </summary>
public sealed record PreciseV2Envelope(
    string Protocol,
    int Version,
    string ProducerSession,
    long Sequence,
    DateTimeOffset SentAtUtc,
    bool Heartbeat,
    PreciseSource Source,
    PreciseValidity Validity,
    string Reason,
    string? ProcessIdentity,
    string? BeatmapIdentity,
    string? PlayIdentity,
    PreciseTransition Transition,
    int? PlayTimeMs,
    DateTimeOffset ObservedAtUtc,
    PreciseV2Keys? Keys);
