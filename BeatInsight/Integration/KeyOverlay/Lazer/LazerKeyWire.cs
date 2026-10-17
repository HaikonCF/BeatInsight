using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeatInsight.Integration.KeyOverlay.Lazer;

// V3.14-COM-LAZER-KEY-03 : ligne JSON émise sur sa sortie standard par le processus lecteur Lazer
// (tools/LazerKeyOverlayRunner, .NET 10) et lue par BeatInsight. Fichier sans dépendance, lié dans le lecteur.
// Une ligne = un état complet ; jamais de compteur M1/M2 (indisponibles dans Lazer).

public static class LazerKeyWire
{
    public const int Version = 1;
    public const int MaxLineChars = 2048;
    public const int MaxCount = 1_000_000;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
        MaxDepth = 4,
    };

    public static string Serialize(LazerKeyWireSample sample) => JsonSerializer.Serialize(sample, JsonOptions);

    /// <summary>Analyse et valide une ligne ; null si elle doit être ignorée. Ne lève jamais.</summary>
    public static LazerKeyWireSample? TryParse(string? line, out string? rejection)
    {
        if (string.IsNullOrEmpty(line) || line.Length > MaxLineChars)
        {
            rejection = "taille";
            return null;
        }
        LazerKeyWireSample? sample;
        try
        {
            sample = JsonSerializer.Deserialize<LazerKeyWireSample>(line, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            rejection = "json";
            return null;
        }
        if (sample is null)
        {
            rejection = "json";
            return null;
        }
        rejection = Validate(sample);
        return rejection is null ? sample : null;
    }

    public static string? Validate(LazerKeyWireSample s)
    {
        if (s.Version != Version) return $"version {s.Version}";
        if (s.Sequence < 1) return "séquence";
        if (s.Validity is not ("Valid" or "NotGameplay" or "Unsupported" or "Unknown")) return "validité";
        if (!Text(s.Failure, 64, required: true) || !Text(s.ProcessIdentity, 256) || !Text(s.PlayIdentity, 300) ||
            !Text(s.Transition, 32, required: true) || !Text(s.Session, 64, required: true))
            return "texte";
        bool valid = s.Validity == "Valid";
        if (valid)
        {
            if (s.ProcessIdentity is null || s.PlayIdentity is null) return "identité";
            if (s.LeftCount is not (>= 0 and <= MaxCount) || s.RightCount is not (>= 0 and <= MaxCount)) return "compteurs";
            if (s.LeftActive is null || s.RightActive is null) return "états d'action";
        }
        else if (s.LeftCount is not null || s.RightCount is not null || s.PlayIdentity is not null)
        {
            return "compteurs sur un échantillon non valide";
        }
        if (s.RestartCount is < 0) return "retry";
        return null;

        static bool Text(string? v, int max, bool required = false) =>
            v is null ? !required : v.Length <= max && (!required || v.Length > 0) && !v.Any(char.IsControl);
    }
}

/// <summary>
/// Échantillon du lecteur Lazer (option A). <see cref="LeftCount"/> et <see cref="RightCount"/> sont les compteurs natifs
/// des actions gauche et droite (clavier, souris et stylet confondus). Les états K1..M2 sont les entrées physiques
/// enfoncées, classées par les liaisons réelles ; null = inconnu.
/// </summary>
public sealed record LazerKeyWireSample(
    int Version,
    string Session,
    long Sequence,
    DateTimeOffset ObservedAtUtc,
    string Validity,
    string Failure,
    string? ProcessIdentity,
    string? PlayIdentity,
    string Transition,
    int? RestartCount,
    int? LeftCount,
    bool? LeftActive,
    int? RightCount,
    bool? RightActive,
    bool? Counting,
    bool? K1Pressed,
    bool? K2Pressed,
    bool? M1Pressed,
    bool? M2Pressed,
    double ReadMs,
    int Reads);
