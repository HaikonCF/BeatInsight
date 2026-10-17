using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeatInsight.Integration.CommunityOverlays.Pipe;

// Contrat du Named Pipe precise BeatInsight → compagnon (V3.14-COM-KEY-04).
// Canal distinct du pipe gameplay : touches réelles d'osu!stable (KeyCounter), rien d'autre.
// Comme CommunityOverlayPipeProtocol, ce fichier n'a aucune dépendance vers le reste de BeatInsight :
// il est lié tel quel dans le compagnon (Pipe/*.cs). Le cadrage (longueur + JSON) est celui de
// CommunityOverlayPipeProtocol.WriteFrameAsync / ReadFrameAsync.

/// <summary>Constantes du protocole precise.</summary>
public static class CommunityOverlayPrecisePipeProtocol
{
    public const string Name = "beatinsight.community-overlays.precise";

    public const int Version = 1;

    /// <summary>Nom du pipe local. Accès restreint à l'utilisateur courant (CurrentUserOnly).</summary>
    public const string PipeName = "BeatInsight.CommunityOverlays.precise.v1";

    /// <summary>Un message precise est minuscule ; toute trame plus grande est rejetée.</summary>
    public const int MaxMessageBytes = 4 * 1024;

    /// <summary>Battement de cœur : le dernier état est republié au moins à cette cadence.</summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Au-delà, le compagnon considère la source perdue (aucun message frais).</summary>
    public static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Âge maximal d'un message (horloge de l'émetteur) pour être considéré comme actuel.</summary>
    public static readonly TimeSpan MaxMessageAge = TimeSpan.FromSeconds(1);

    public const int MaxIdentityLength = 256;
    public const int MaxReasonLength = 64;
    public const int MaxCount = 1_000_000;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    public static byte[] Serialize(PrecisePipeEnvelope envelope)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        if (bytes.Length > MaxMessageBytes)
            throw new InvalidOperationException("Message precise trop grand.");
        return bytes;
    }

    /// <summary>Désérialise et valide. Null + raison si le message doit être ignoré.</summary>
    public static PrecisePipeEnvelope? TryDeserialize(ReadOnlySpan<byte> json, out string? rejection)
    {
        if (json.Length == 0 || json.Length > MaxMessageBytes)
        {
            rejection = "taille";
            return null;
        }
        PrecisePipeEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<PrecisePipeEnvelope>(json, JsonOptions);
        }
        catch (JsonException)
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
    public static string? Validate(PrecisePipeEnvelope e)
    {
        if (e.Protocol != Name) return "protocole";
        if (e.Version != Version) return $"version {e.Version}";
        if (e.Sequence < 1) return "séquence";
        if (!Text(e.ProducerSession, required: true)) return "session";
        if (!Text(e.ProcessIdentity, required: false) || !Text(e.BeatmapIdentity, required: false) ||
            !Text(e.PlayIdentity, required: false))
            return "identité";
        if (e.Reason is null || e.Reason.Length > MaxReasonLength) return "raison";
        if (e.PlayTimeMs is int t && (t < -60_000 || t > 24 * 3_600_000)) return "temps";
        if (!Enum.IsDefined(e.Validity) || !Enum.IsDefined(e.Transition)) return "énumération";
        if (e.Validity == PreciseValidity.Valid)
        {
            if (e.Keys is null || e.PlayIdentity is null) return "touches absentes";
            if (!Key(e.Keys.K1) || !Key(e.Keys.K2) || !Key(e.Keys.M1) || !Key(e.Keys.M2)) return "touches";
        }
        else if (e.Keys is not null)
        {
            return "touches sur un échantillon Unknown";
        }
        return null;

        static bool Text(string? value, bool required) =>
            value is null ? !required : value.Length is > 0 and <= MaxIdentityLength;
        static bool Key(PreciseKey? k) => k is not null && k.Count is >= 0 and <= MaxCount;
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<PreciseValidity>))]
public enum PreciseValidity
{
    Valid,
    Unknown,
}

/// <summary>
/// Comment la partie courante (<c>PlayIdentity</c>) a commencé, selon le producteur. Répétée sur
/// tous les messages de la partie : le pipe ne garde que le dernier message, une valeur portée par
/// un seul message pourrait être remplacée avant lecture. <c>None</c> hors partie.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PreciseTransition>))]
public enum PreciseTransition
{
    None,
    /// <summary>Première partie observée par ce producteur.</summary>
    FirstPlay,
    /// <summary>Retour en gameplay après un autre écran, même beatmap.</summary>
    NewGameplay,
    /// <summary>Recul du temps sans sortie du gameplay.</summary>
    Retry,
    /// <summary>Nouvelle partie sur une autre beatmap.</summary>
    BeatmapChanged,
    /// <summary>Nouveau processus osu! (redémarrage).</summary>
    ProcessChanged,
}

public sealed record PreciseKey(int Count, bool IsPressed);

public sealed record PreciseKeys(PreciseKey K1, PreciseKey K2, PreciseKey M1, PreciseKey M2);

/// <summary>
/// Message precise. Un message porte toujours l'état complet (pas de delta) : un message perdu
/// ou remplacé n'invente ni ne retire jamais d'appui. <see cref="Sequence"/> croît strictement
/// pour un même <see cref="ProducerSession"/>. <see cref="Keys"/> est nul dès que
/// <see cref="Validity"/> vaut Unknown.
/// </summary>
public sealed record PrecisePipeEnvelope(
    string Protocol,
    int Version,
    string ProducerSession,
    long Sequence,
    DateTimeOffset SentAtUtc,
    bool Heartbeat,
    PreciseValidity Validity,
    string Reason,
    string? ProcessIdentity,
    string? BeatmapIdentity,
    string? PlayIdentity,
    PreciseTransition Transition,
    int? PlayTimeMs,
    DateTimeOffset ObservedAtUtc,
    PreciseKeys? Keys);
