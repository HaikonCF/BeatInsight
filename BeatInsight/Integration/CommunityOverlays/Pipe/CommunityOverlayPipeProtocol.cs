using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace BeatInsight.Integration.CommunityOverlays.Pipe;

// Contrat du Named Pipe BeatInsight → compagnon CommunityOverlays (V3.14-COM-10).
// Ce fichier n'a aucune dépendance vers le reste de BeatInsight : il est compilé tel quel
// dans le projet principal et, par lien, dans le compagnon. Toute évolution du contenu
// impose d'incrémenter Version : un compagnon d'une autre version refuse les messages.
// V3.14-COM-11 : v2 sur un pipe dédié ; le pipe v1 reste publié à l'identique (voir LegacyPipeNameV1).

/// <summary>Constantes du protocole.</summary>
public static class CommunityOverlayPipeProtocol
{
    public const string Name = "beatinsight.community-overlays";

    /// <summary>
    /// Version courante. v2 (V3.14-COM-11) ajoute le bloc <c>audio</c> ; tout le reste est identique à v1.
    /// </summary>
    public const int Version = 2;

    /// <summary>Plus ancienne version acceptée par ce compagnon (BeatInsight COM-10).</summary>
    public const int MinimumVersion = 1;

    /// <summary>Nom du pipe local v2. L'accès est restreint à l'utilisateur courant (CurrentUserOnly).</summary>
    public const string PipeName = "BeatInsight.CommunityOverlays.v2";

    /// <summary>
    /// Nom du pipe v1 (COM-10). BeatInsight continue d'y publier des messages v1 stricts, sans bloc
    /// <c>audio</c>, pour qu'un compagnon COM-10 fonctionne sans changement. Le compagnon courant
    /// s'y replie quand le pipe v2 n'existe pas (BeatInsight COM-10).
    /// </summary>
    public const string LegacyPipeNameV1 = "BeatInsight.CommunityOverlays.v1";

    /// <summary>Taille maximale d'un message (hors préfixe de longueur).</summary>
    public const int MaxMessageBytes = 64 * 1024;

    /// <summary>Le producteur republie au moins à cette cadence (sert aussi de battement de cœur).</summary>
    public static readonly TimeSpan PublishInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Sans message pendant cette durée, le compagnon considère la source perdue.</summary>
    public static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(1.5);

    public const int MaxTextLength = 1024;
    public const int MaxPathLength = 1024;
    public const int MaxMods = 16;
    public const int MaxHitCount = 100_000;

    /// <summary>Durée audio maximale acceptée (3 h, en ms).</summary>
    public const double MaxAudioDurationMs = 3 * 60 * 60 * 1000.0;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        MaxDepth = 8,
    };

    public static byte[] Serialize(PipeEnvelope envelope)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        if (json.Length > MaxMessageBytes)
            throw new InvalidOperationException($"Message trop grand ({json.Length} octets).");
        return json;
    }

    /// <summary>Désérialise et valide un message. Ne lève jamais : renvoie la raison du rejet.</summary>
    public static PipeEnvelope? TryDeserialize(ReadOnlySpan<byte> json, out string? rejection)
    {
        if (json.Length == 0 || json.Length > MaxMessageBytes)
        {
            rejection = "taille invalide";
            return null;
        }

        PipeEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<PipeEnvelope>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            rejection = "JSON invalide : " + ex.Message;
            return null;
        }
        catch (NotSupportedException ex)
        {
            rejection = "JSON invalide : " + ex.Message;
            return null;
        }

        rejection = envelope is null ? "message vide" : Validate(envelope);
        return rejection is null ? envelope : null;
    }

    /// <summary>Validation stricte des champs. Renvoie null si le message est valide.</summary>
    public static string? Validate(PipeEnvelope e)
    {
        if (!string.Equals(e.Protocol, Name, StringComparison.Ordinal)) return "protocole inconnu";
        if (e.Version is < MinimumVersion or > Version)
            return $"version {e.Version} non supportée (attendue {MinimumVersion} à {Version})";
        if (e.Sequence < 0) return "séquence négative";
        if (e.Snapshot is null) return "snapshot absent";

        var s = e.Snapshot;
        if (!Enum.IsDefined(s.Suspension)) return "suspension inconnue";

        if (s.Game is { } g)
        {
            if (!Text(g.ProviderName, required: true) || !Text(g.ClientName) || !Text(g.ProcessIdentity) ||
                !Path(g.CurrentBeatmapPath) || !Text(g.CurrentBeatmapId) || !Text(g.CurrentBeatmapSetId) ||
                !Path(g.SongsDirectory) || !Path(g.CurrentBackgroundPath))
                return "état de jeu invalide";
            if (!Enum.IsDefined(g.Availability)) return "disponibilité inconnue";
        }
        if (s.Route is { } r)
        {
            if (!Enum.IsDefined(r.Route) || !Text(r.ProcessIdentity)) return "route invalide";
        }
        if (s.Metadata is { } m)
        {
            if (!Path(m.BeatmapPath, required: true) || !Text(m.Artist, required: true) || !Text(m.Title, required: true) ||
                !Text(m.Version, required: true) || !Text(m.Creator, required: true) ||
                !Finite(m.ApproachRate, 0, 20) || !Finite(m.OverallDifficulty, 0, 20) || !Finite(m.CircleSize, 0, 20) ||
                !Finite(m.HpDrain, 0, 20) || !Finite(m.OsuStarRating, 0, 1000) ||
                !Optional(m.PrimaryBpm, 0, 100_000) || !Optional(m.MinBpm, 0, 100_000) || !Optional(m.MaxBpm, 0, 100_000) ||
                m.Bpm < 0 || m.MaxCombo < 0 || m.LengthTicks < 0 ||
                !Count(m.CircleCount) || !Count(m.SliderCount) || !Count(m.SpinnerCount) ||
                m.OsuMaxCombo is < 0 || m.BeatmapId is < 0 || m.BeatmapSetId is < -1)
                return "métadonnées invalides";
        }
        if (s.OsuFile is { } f)
        {
            if (!Path(f.BeatmapPath, required: true) || f.Md5 is null || f.Md5.Length != 32 ||
                !f.Md5.All(Uri.IsHexDigit) || f.FileLength < 0 ||
                !FileName(f.AudioFilename) || !Text(f.TitleUnicode) || !Text(f.ArtistUnicode))
                return "fichier .osu invalide";
        }
        if (s.LiveGameplay is { } l)
        {
            if (!Text(l.ProcessIdentity, required: true) || !Text(l.BeatmapId) || !Text(l.BeatmapSetId) ||
                !Finite(l.CurrentTimeMs, -1_000_000, 100_000_000) || !Finite(l.BeatmapDurationMs, 0, 100_000_000))
                return "position de jeu invalide";
        }
        if (s.Stats is { } st)
        {
            if (!Text(st.ProcessIdentity, required: true) || !Text(st.BeatmapId) || !Text(st.BeatmapSetId) ||
                !Finite(st.CurrentTimeMs, -1_000_000, 100_000_000) || !Finite(st.Accuracy, 0, 1.000001) ||
                !Count(st.Count300) || !Count(st.Count100) || !Count(st.Count50) || !Count(st.CountMiss) ||
                st.CurrentCombo < 0 || st.MaxComboReached < 0 || st.CurrentCombo > st.MaxComboReached ||
                st.Score is < 0 || st.Mods is null || st.Mods.Count > MaxMods ||
                st.Mods.Any(mod => mod is null || mod.Length is < 2 or > 3 || !mod.All(char.IsAsciiLetterOrDigit)))
                return "stats invalides";
        }
        if (s.Performance is { } p)
        {
            if (!Finite(p.CurrentPp, 0, 100_000) || !Finite(p.FcPp, 0, 100_000) || !Finite(p.Accuracy95FcPp, 0, 100_000) ||
                !Finite(p.SsPp, 0, 100_000) || p.PassedObjects is < 0 or > MaxHitCount)
                return "performance invalide";
        }
        if (s.Audio is { } a)
        {
            if (e.Version < 2) return "bloc audio absent de la v1";
            if (!Path(a.BeatmapPath, required: true) || a.AudioFilename is null || !FileName(a.AudioFilename) ||
                !Enum.IsDefined(a.Status) || !Enum.IsDefined(a.Format) || a.FileLength < 0)
                return "audio invalide";
            // Une durée n'existe que si elle a réellement été lue ; sinon elle est absente.
            if (a.Status == PipeAudioStatus.Known
                    ? a.DurationMs is not { } d || !Finite(d, 1, MaxAudioDurationMs)
                    : a.DurationMs is not null)
                return "audio invalide";
        }
        return null;

        static bool Text(string? value, bool required = false) =>
            value is null ? !required : value.Length <= MaxTextLength && !value.Any(char.IsControl) && (!required || value.Length > 0);
        static bool Path(string? value, bool required = false) =>
            value is null ? !required : value.Length is > 0 and <= MaxPathLength && !value.Any(char.IsControl);
        static bool FileName(string? value) =>
            value is null || (value.Length is > 0 and <= 255 && !value.Any(char.IsControl) &&
                              value.IndexOfAny(['/', '\\', ':']) < 0 && value != "." && value != "..");
        static bool Finite(double value, double min, double max) => double.IsFinite(value) && value >= min && value <= max;
        static bool Optional(double? value, double min, double max) => value is null || Finite(value.Value, min, max);
        static bool Count(int value) => value is >= 0 and <= MaxHitCount;
    }

    /// <summary>Écrit un message : longueur sur 4 octets (little-endian) puis le JSON UTF-8.</summary>
    public static async Task WriteFrameAsync(Stream stream, byte[] message, CancellationToken cancellationToken)
    {
        if (message.Length > MaxMessageBytes)
            throw new ArgumentException("Message trop grand.", nameof(message));
        byte[] frame = new byte[4 + message.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, message.Length);
        message.CopyTo(frame, 4);
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lit un message. Renvoie null en fin de flux (déconnexion propre).
    /// Lève <see cref="InvalidDataException"/> si la longueur annoncée est hors limites.
    /// </summary>
    public static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] header = new byte[4];
        if (!await ReadExactlyOrEndAsync(stream, header, cancellationToken).ConfigureAwait(false))
            return null;
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaxMessageBytes)
            throw new InvalidDataException($"Longueur de message invalide ({length}).");
        byte[] body = new byte[length];
        if (!await ReadExactlyOrEndAsync(stream, body, cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("Message tronqué.");
        return body;
    }

    private static async Task<bool> ReadExactlyOrEndAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (n == 0)
            {
                if (read == 0) return false;
                throw new InvalidDataException("Message tronqué.");
            }
            read += n;
        }
        return true;
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<PipeRoute>))]
public enum PipeRoute
{
    Unknown,
    SongSelect,
    Gameplay,
    Results,
}

[JsonConverter(typeof(JsonStringEnumConverter<PipeAvailability>))]
public enum PipeAvailability
{
    Available,
    NoProcess,
    NoBeatmap,
    Ambiguous,
    InvalidData,
    ProviderUnavailable,
    Error,
}

[JsonConverter(typeof(JsonStringEnumConverter<PipeSuspension>))]
public enum PipeSuspension
{
    None,
    OverlayLayoutEdit,
    FastLabeling,
    BackgroundLibraryWork,
}

/// <summary>Message complet. <see cref="Sequence"/> croît strictement chez le producteur.</summary>
public sealed record PipeEnvelope(
    string Protocol,
    int Version,
    long Sequence,
    DateTimeOffset SentAtUtc,
    PipeSnapshot Snapshot);

/// <summary>
/// Recopie des snapshots BeatInsight d'un tick. Chaque bloc peut être absent : le compagnon
/// ne le remplace jamais par autre chose. Les chemins sont absolus sur le pipe (local, réservé
/// à l'utilisateur) et ne sortent jamais du compagnon : seuls des chemins relatifs vont aux overlays.
/// </summary>
public sealed record PipeSnapshot(
    PipeGameState? Game,
    PipeGameRoute? Route,
    PipeBeatmapMetadata? Metadata,
    PipeOsuFile? OsuFile,
    PipeLiveGameplay? LiveGameplay,
    PipeLiveStats? Stats,
    PipePerformance? Performance,
    PipeSuspension Suspension,
    // v2 uniquement. Absent du JSON quand il est nul : un message v1 reste strictement identique à COM-10.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PipeAudioFile? Audio = null);

[JsonConverter(typeof(JsonStringEnumConverter<PipeAudioStatus>))]
public enum PipeAudioStatus
{
    /// <summary>Durée lue dans le fichier.</summary>
    Known,

    /// <summary>Lecture en cours (nouvelle beatmap) : durée pas encore disponible.</summary>
    Pending,

    /// <summary>Fichier audio absent du dossier de la beatmap.</summary>
    Missing,

    /// <summary>Fichier verrouillé, supprimé pendant la lecture ou accès refusé.</summary>
    Unreadable,

    /// <summary>Structure incohérente (fichier corrompu ou tronqué).</summary>
    Invalid,

    /// <summary>Format dont la durée exacte ne se lit pas sans décodage : aucune estimation n'est faite.</summary>
    Unsupported,
}

[JsonConverter(typeof(JsonStringEnumConverter<PipeAudioFormat>))]
public enum PipeAudioFormat
{
    Unknown,
    Mp3,
    Ogg,
    Wav,
}

/// <summary>
/// Durée réelle du fichier <c>AudioFilename</c> de la beatmap <see cref="BeatmapPath"/> (V3.14-COM-11),
/// en millisecondes de piste audio (indépendante des mods de vitesse, comme <c>mp3Length</c> de tosu).
/// <see cref="DurationMs"/> n'est renseignée que si <see cref="Status"/> vaut <see cref="PipeAudioStatus.Known"/>.
/// </summary>
public sealed record PipeAudioFile(
    string BeatmapPath,
    string AudioFilename,
    PipeAudioStatus Status,
    PipeAudioFormat Format,
    double? DurationMs,
    long FileLength,
    DateTimeOffset? LastWriteUtc);

public sealed record PipeGameState(
    string ProviderName,
    string? ClientName,
    string? ProcessIdentity,
    string? CurrentBeatmapPath,
    string? CurrentBeatmapId,
    string? CurrentBeatmapSetId,
    DateTimeOffset LastUpdateUtc,
    PipeAvailability Availability,
    string? SongsDirectory,
    string? CurrentBackgroundPath);

public sealed record PipeGameRoute(PipeRoute Route, DateTimeOffset ObservedAtUtc, string? ProcessIdentity);

public sealed record PipeBeatmapMetadata(
    string BeatmapPath,
    int? BeatmapId,
    int? BeatmapSetId,
    string Artist,
    string Title,
    string Version,
    string Creator,
    long LengthTicks,
    int Bpm,
    int MaxCombo,
    double ApproachRate,
    double OverallDifficulty,
    double CircleSize,
    double HpDrain,
    int CircleCount,
    int SliderCount,
    int SpinnerCount,
    double OsuStarRating,
    double? PrimaryBpm,
    double? MinBpm,
    double? MaxBpm,
    int? OsuMaxCombo);

/// <summary>Lecture du seul fichier .osu courant : MD5 réel et champs d'en-tête.</summary>
public sealed record PipeOsuFile(
    string BeatmapPath,
    long FileLength,
    DateTimeOffset LastWriteUtc,
    string Md5,
    string? AudioFilename,
    string? TitleUnicode,
    string? ArtistUnicode);

public sealed record PipeLiveGameplay(
    bool IsGameplay,
    double CurrentTimeMs,
    double BeatmapDurationMs,
    string? BeatmapId,
    string? BeatmapSetId,
    string ProcessIdentity,
    DateTimeOffset ObservedAtUtc);

public sealed record PipeLiveStats(
    string ProcessIdentity,
    string? BeatmapId,
    string? BeatmapSetId,
    double CurrentTimeMs,
    int CurrentCombo,
    int MaxComboReached,
    int Count300,
    int Count100,
    int Count50,
    int CountMiss,
    double Accuracy,
    IReadOnlyList<string> Mods,
    DateTimeOffset ObservedAtUtc,
    int? Score);

public sealed record PipePerformance(
    double CurrentPp,
    double FcPp,
    double Accuracy95FcPp,
    double SsPp,
    int PassedObjects,
    DateTimeOffset ObservedAtUtc);
