using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeatInsight.Integration.CommunityOverlays.Pipe;

// Contrat v3 « selection » BeatInsight → compagnon (V3.14-COM-PP-03) : PP SS de la beatmap sélectionnée dans
// osu!stable, avec les mods réellement actifs. Canal additionnel : les pipes v1, v2 et precise restent publiés
// à l'identique (leurs contrats et leurs tests ne changent pas). Comme les autres contrats, ce fichier n'a aucune
// dépendance vers le reste de BeatInsight : il est lié tel quel dans le compagnon (Pipe/*.cs). Le cadrage
// (longueur + JSON) est celui de CommunityOverlayPipeProtocol.WriteFrameAsync / ReadFrameAsync.

/// <summary>Constantes du protocole selection.</summary>
public static class CommunityOverlaySelectionPipeProtocol
{
    public const string Name = "beatinsight.community-overlays.selection";

    /// <summary>Troisième version du transport vers les overlays (v1, v2 : gameplay ; v3 : sélection).</summary>
    public const int Version = 3;

    /// <summary>Nom du pipe local. Accès restreint à l'utilisateur courant (CurrentUserOnly).</summary>
    public const string PipeName = "BeatInsight.CommunityOverlays.selection.v3";

    public const int MaxMessageBytes = 8 * 1024;

    /// <summary>Cadence de publication (sert aussi de battement de cœur).</summary>
    public static readonly TimeSpan PublishInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>Âge maximal d'un message (horloge de l'émetteur) pour être considéré comme actuel.</summary>
    public static readonly TimeSpan MaxMessageAge = TimeSpan.FromSeconds(1);

    /// <summary>Sans message pendant cette durée, le compagnon considère la source perdue.</summary>
    public static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(1);

    public const int MaxIdentityLength = 256;
    public const int MaxPathLength = 1024;
    public const int MaxReasonLength = 128;
    public const int MaxVersionLength = 128;
    public const int MaxMods = 16;
    public const double MaxPp = 100_000;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        MaxDepth = 8,
    };

    public static byte[] Serialize(SelectionPipeEnvelope envelope)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        if (bytes.Length > MaxMessageBytes)
            throw new InvalidOperationException("Message selection trop grand.");
        return bytes;
    }

    /// <summary>Désérialise et valide. Null + raison si le message doit être ignoré. Ne lève jamais.</summary>
    public static SelectionPipeEnvelope? TryDeserialize(ReadOnlySpan<byte> json, out string? rejection)
    {
        if (json.Length == 0 || json.Length > MaxMessageBytes)
        {
            rejection = "taille";
            return null;
        }
        SelectionPipeEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<SelectionPipeEnvelope>(json, JsonOptions);
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
    public static string? Validate(SelectionPipeEnvelope e)
    {
        if (!string.Equals(e.Protocol, Name, StringComparison.Ordinal)) return "protocole";
        if (e.Version != Version) return $"version {e.Version}";
        if (e.Sequence < 1) return "séquence";
        if (e.SelectionGeneration < 0) return "génération";
        if (!Text(e.ProducerSession, MaxIdentityLength, required: true)) return "session";
        if (!Text(e.ProcessIdentity, MaxIdentityLength, required: false)) return "identité";
        if (e.Reason is null || !Text(e.Reason, MaxReasonLength, required: false)) return "raison";
        if (!Enum.IsDefined(e.Validity)) return "énumération";
        if (e.RawModsMask is < 0) return "masque";
        if (e.Mods is { } mods && (mods.Count > MaxMods || mods.Any(mod => !Acronym(mod)))) return "mods";
        if (e.ModsKey is { } key && !ModsKey(key)) return "clé de mods";
        if (e.Beatmap is { } b &&
            (!Text(b.Path, MaxPathLength, required: true) || b.FileLength < 0))
            return "beatmap";

        if (e.Validity == SelectionValidity.Valid)
        {
            // Un PP n'est publié que pour une sélection entièrement identifiée.
            if (e.ProcessIdentity is null || e.Beatmap is null || e.ModsKey is null || e.Mods is null || e.Performance is null)
                return "sélection incomplète";
            if (!string.Equals(e.ModsKey, KeyOf(e.Mods), StringComparison.Ordinal)) return "clé de mods incohérente";
            SelectionPerformance p = e.Performance;
            if (!Finite(p.SsPp) || !Finite(p.Accuracy95Pp)) return "performance";
            if (!Text(p.CalculatorVersion, MaxVersionLength, required: true)) return "version du calculateur";
        }
        else if (e.Performance is not null)
        {
            return "performance sur une sélection non valide";
        }
        return null;

        static bool Text(string? value, int max, bool required) =>
            value is null ? !required : value.Length <= max && !value.Any(char.IsControl) && (!required || value.Length > 0);
        static bool Finite(double value) => double.IsFinite(value) && value is >= 0 and <= MaxPp;
        static bool Acronym(string? mod) => mod is { Length: >= 2 and <= 3 } && mod.All(c => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9'));
        static bool ModsKey(string key) => key == "NM" || (key.Length <= 64 && key.Split('+').All(Acronym));
    }

    /// <summary>Clé de mods canonique : acronymes triés (ordinal) joints par « + », « NM » sans mod.</summary>
    public static string KeyOf(IEnumerable<string> mods)
    {
        string[] sorted = mods.Where(mod => mod != "NM").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return sorted.Length == 0 ? "NM" : string.Join('+', sorted);
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<SelectionValidity>))]
public enum SelectionValidity
{
    /// <summary>Sélection confirmée, mods valides, résultat calculé pour exactement cette sélection.</summary>
    Valid,

    /// <summary>Sélection valide, calcul en attente ou en cours : valeur neutre côté overlay.</summary>
    Pending,

    /// <summary>osu! n'est pas dans la sélection des maps.</summary>
    NotSongSelect,

    /// <summary>Mods, beatmap ou processus inconnus.</summary>
    Unknown,

    /// <summary>Sélection identifiée mais sans SS osu!standard (mod non pris en charge, autre mode, échec du calcul).</summary>
    Unsupported,
}

/// <summary>Identité du fichier .osu sélectionné (chemin absolu local, jamais transmis aux overlays).</summary>
public sealed record SelectionBeatmap(string Path, long FileLength, DateTimeOffset LastWriteUtc);

/// <summary>
/// PP de la sélection, mêmes conventions que le PP gameplay de BeatInsight : <see cref="SsPp"/> = 100 %,
/// <see cref="Accuracy95Pp"/> = 95 % FC. Aucune autre précision n'est calculée ni inventée.
/// </summary>
public sealed record SelectionPerformance(double SsPp, double Accuracy95Pp, string CalculatorVersion, DateTimeOffset ComputedAtUtc);

/// <summary>
/// Message selection : toujours l'état complet. <see cref="Sequence"/> croît strictement pour un même
/// <see cref="ProducerSession"/> ; <see cref="SelectionGeneration"/> croît à chaque changement de sélection.
/// <see cref="Performance"/> n'existe que si <see cref="Validity"/> vaut <see cref="SelectionValidity.Valid"/>.
/// </summary>
public sealed record SelectionPipeEnvelope(
    string Protocol,
    int Version,
    string ProducerSession,
    long Sequence,
    DateTimeOffset SentAtUtc,
    long SelectionGeneration,
    SelectionValidity Validity,
    string Reason,
    string? ProcessIdentity,
    SelectionBeatmap? Beatmap,
    string? ModsKey,
    IReadOnlyList<string>? Mods,
    int? RawModsMask,
    DateTimeOffset ObservedAtUtc,
    SelectionPerformance? Performance);
