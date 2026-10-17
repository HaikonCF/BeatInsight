using System.Buffers.Binary;
using System.IO;
using BeatInsight.Integration.CommunityOverlays.Pipe;

namespace BeatInsight.Integration.CommunityOverlays;

/// <summary>Résultat brut d'une lecture d'en-têtes audio.</summary>
public readonly record struct AudioProbeResult(PipeAudioStatus Status, PipeAudioFormat Format, double? DurationMs)
{
    public static AudioProbeResult Known(PipeAudioFormat format, double durationMs) =>
        double.IsFinite(durationMs) && durationMs > 0 && durationMs <= AudioDurationProbe.MaxDurationMs
            ? new(PipeAudioStatus.Known, format, Math.Round(durationMs))
            : Invalid(format);

    public static AudioProbeResult Invalid(PipeAudioFormat format) => new(PipeAudioStatus.Invalid, format, null);

    public static AudioProbeResult Unsupported(PipeAudioFormat format) => new(PipeAudioStatus.Unsupported, format, null);
}

/// <summary>
/// Durée réelle d'un fichier audio de beatmap (V3.14-COM-11), lue dans ses seules structures de
/// conteneur, sans décodage ni lecteur multimédia :
/// <list type="bullet">
/// <item>WAV PCM : taille du bloc <c>data</c> / taille d'un échantillon / fréquence (exact).</item>
/// <item>OGG Vorbis ou Opus : position granulaire de la dernière page, moins le pre-skip Opus (exact).</item>
/// <item>MP3 : nombre de trames de l'en-tête Xing/Info ou VBRI, moins le retard et le remplissage de
/// l'extension LAME ; à défaut, comptage des en-têtes de trames sur tout le fichier, lu par blocs de
/// 64 Kio (exact, aucune estimation par débit).</item>
/// </list>
/// Tout autre cas (WAV compressé, FLAC, format inconnu) donne <see cref="PipeAudioStatus.Unsupported"/> :
/// aucune durée approchée n'est produite.
/// </summary>
public static class AudioDurationProbe
{
    /// <summary>Au-delà de 3 h, la valeur est considérée comme aberrante.</summary>
    public const double MaxDurationMs = 3 * 60 * 60 * 1000.0;

    private const int ChunkBytes = 64 * 1024;

    /// <summary>Lit la durée depuis un flux positionnable. Ne lève pas sur un contenu invalide.</summary>
    public static AudioProbeResult Probe(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek || !stream.CanRead) throw new ArgumentException("Flux positionnable requis.", nameof(stream));

        byte[] head = ReadAt(stream, 0, 12);
        if (head.Length >= 12 && Ascii(head, 0, "RIFF") && Ascii(head, 8, "WAVE"))
            return Wav(stream);
        if (head.Length >= 4 && (Ascii(head, 0, "RF64") || Ascii(head, 0, "fLaC")))
            return AudioProbeResult.Unsupported(PipeAudioFormat.Unknown);
        if (head.Length >= 4 && Ascii(head, 0, "OggS"))
            return Ogg(stream);
        return Mp3(stream);
    }

    // ------------------------------------------------------------------ WAV

    private static AudioProbeResult Wav(Stream stream)
    {
        long length = stream.Length;
        long position = 12;
        int formatTag = -1, channels = 0, blockAlign = 0;
        long sampleRate = 0;
        while (position + 8 <= length)
        {
            byte[] chunk = ReadAt(stream, position, 8);
            if (chunk.Length < 8) break;
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(chunk.AsSpan(4));
            long body = position + 8;

            if (Ascii(chunk, 0, "fmt "))
            {
                byte[] fmt = ReadAt(stream, body, (int)Math.Min(size, 40));
                if (fmt.Length < 16) return AudioProbeResult.Invalid(PipeAudioFormat.Wav);
                formatTag = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(2));
                sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(fmt.AsSpan(4));
                blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(12));
                // WAVE_FORMAT_EXTENSIBLE : le vrai format est dans les 2 premiers octets du sous-format.
                if (formatTag == 0xFFFE && fmt.Length >= 26)
                    formatTag = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(24));
            }
            else if (Ascii(chunk, 0, "data"))
            {
                if (formatTag < 0) return AudioProbeResult.Invalid(PipeAudioFormat.Wav);
                // PCM, flottant, A-law, µ-law : durée exacte. Un WAV compressé n'aurait qu'une durée estimée.
                if (formatTag is not (1 or 3 or 6 or 7)) return AudioProbeResult.Unsupported(PipeAudioFormat.Wav);
                if (channels <= 0 || blockAlign <= 0 || sampleRate is <= 0 or > 768_000)
                    return AudioProbeResult.Invalid(PipeAudioFormat.Wav);
                // Taille inconnue (flux) ou fichier tronqué : seules les données réellement présentes comptent.
                long dataBytes = Math.Min(size, length - body);
                long frames = dataBytes / blockAlign;
                return AudioProbeResult.Known(PipeAudioFormat.Wav, frames * 1000.0 / sampleRate);
            }

            position = body + size + (size & 1);
        }
        return AudioProbeResult.Invalid(PipeAudioFormat.Wav);
    }

    // ------------------------------------------------------------------ OGG

    private static AudioProbeResult Ogg(Stream stream)
    {
        long length = stream.Length;
        byte[] first = ReadAt(stream, 0, (int)Math.Min(length, 27 + 255 + 255 * 255));
        if (!TryParseOggPage(first, 0, out var firstPage) || !firstPage.CrcValid)
            return AudioProbeResult.Invalid(PipeAudioFormat.Ogg);

        // Premier paquet du flux : en-tête d'identification du codec.
        var packet = first.AsSpan(firstPage.BodyOffset, Math.Min(firstPage.FirstPacketLength, first.Length - firstPage.BodyOffset));
        long sampleRate;
        long preSkip = 0;
        if (packet.Length >= 16 && packet[0] == 0x01 && Ascii(packet, 1, "vorbis"))
        {
            sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(packet[12..]);
        }
        else if (packet.Length >= 19 && Ascii(packet, 0, "OpusHead"))
        {
            preSkip = BinaryPrimitives.ReadUInt16LittleEndian(packet[10..]);
            sampleRate = 48_000; // granule Opus toujours à 48 kHz
        }
        else
        {
            return AudioProbeResult.Unsupported(PipeAudioFormat.Ogg);
        }
        if (sampleRate is <= 0 or > 768_000) return AudioProbeResult.Invalid(PipeAudioFormat.Ogg);

        // Dernière page valide du même flux : deux pages maximales suffisent à la contenir.
        int tailLength = (int)Math.Min(length, 2 * (27 + 255 + 255 * 255));
        long tailStart = length - tailLength;
        byte[] tail = ReadAt(stream, tailStart, tailLength);
        for (int i = tail.Length - 27; i >= 0; i--)
        {
            if (tail[i] != (byte)'O' || !Ascii(tail, i, "OggS")) continue;
            // Page incomplète (fichier tronqué) ou d'un autre flux : on remonte à la page précédente.
            if (!TryParseOggPage(tail, i, out var page) || page.Serial != firstPage.Serial) continue;
            // Page complète mais altérée : la durée n'est pas fiable, aucune page antérieure ne la remplace.
            if (!page.CrcValid) return AudioProbeResult.Invalid(PipeAudioFormat.Ogg);
            if (page.Granule < 0) continue; // -1 : aucune trame ne se termine sur cette page
            long samples = page.Granule - preSkip;
            return samples > 0
                ? AudioProbeResult.Known(PipeAudioFormat.Ogg, samples * 1000.0 / sampleRate)
                : AudioProbeResult.Invalid(PipeAudioFormat.Ogg);
        }
        return AudioProbeResult.Invalid(PipeAudioFormat.Ogg);
    }

    private readonly record struct OggPage(long Granule, uint Serial, int BodyOffset, int FirstPacketLength, bool CrcValid);

    private static bool TryParseOggPage(byte[] buffer, int offset, out OggPage page)
    {
        page = default;
        if (offset + 27 > buffer.Length || !Ascii(buffer, offset, "OggS") || buffer[offset + 4] != 0) return false;
        int segments = buffer[offset + 26];
        int bodyOffset = offset + 27 + segments;
        if (bodyOffset > buffer.Length) return false;
        int bodyLength = 0, firstPacket = 0;
        bool firstPacketDone = false;
        for (int s = 0; s < segments; s++)
        {
            int lace = buffer[offset + 27 + s];
            bodyLength += lace;
            if (!firstPacketDone)
            {
                firstPacket += lace;
                firstPacketDone = lace < 255;
            }
        }
        if (bodyOffset + bodyLength > buffer.Length) return false;

        uint storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset + 22));
        uint crc = OggCrc(buffer.AsSpan(offset, bodyOffset + bodyLength - offset));
        page = new OggPage(
            BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(offset + 6)),
            BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset + 14)),
            bodyOffset,
            firstPacket,
            crc == storedCrc);
        return true;
    }

    private static readonly uint[] OggCrcTable = BuildOggCrcTable();

    private static uint[] BuildOggCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint r = i << 24;
            for (int b = 0; b < 8; b++)
                r = (r & 0x80000000) != 0 ? (r << 1) ^ 0x04C11DB7 : r << 1;
            table[i] = r;
        }
        return table;
    }

    /// <summary>CRC Ogg (polynôme 0x04C11DB7, sans réflexion), calculé avec le champ CRC à zéro.</summary>
    internal static uint OggCrc(ReadOnlySpan<byte> page)
    {
        uint crc = 0;
        for (int i = 0; i < page.Length; i++)
        {
            byte value = i is >= 22 and < 26 ? (byte)0 : page[i];
            crc = (crc << 8) ^ OggCrcTable[((crc >> 24) & 0xFF) ^ value];
        }
        return crc;
    }

    // ------------------------------------------------------------------ MP3

    private static readonly int[,] BitratesV1 =
    {
        { 0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448 }, // Layer I
        { 0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384 },    // Layer II
        { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320 },     // Layer III
    };

    private static readonly int[,] BitratesV2 =
    {
        { 0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256 },    // Layer I
        { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 },         // Layer II
        { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 },         // Layer III
    };

    private static readonly int[,] SampleRates =
    {
        { 11_025, 12_000, 8_000 },  // MPEG 2.5
        { 0, 0, 0 },                // réservé
        { 22_050, 24_000, 16_000 }, // MPEG 2
        { 44_100, 48_000, 32_000 }, // MPEG 1
    };

    internal readonly record struct Mp3Frame(int Version, int Layer, int SampleRate, int Length, int SamplesPerFrame, bool Mono);

    internal static bool TryParseMp3Header(ReadOnlySpan<byte> h, out Mp3Frame frame)
    {
        frame = default;
        if (h.Length < 4 || h[0] != 0xFF || (h[1] & 0xE0) != 0xE0) return false;
        int version = (h[1] >> 3) & 3;  // 0 = 2.5, 2 = 2, 3 = 1
        int layerBits = (h[1] >> 1) & 3; // 1 = III, 2 = II, 3 = I
        int bitrateIndex = (h[2] >> 4) & 0xF;
        int rateIndex = (h[2] >> 2) & 3;
        if (version == 1 || layerBits == 0 || bitrateIndex is 0 or 15 || rateIndex == 3) return false;

        int layer = 4 - layerBits; // 1, 2 ou 3
        int bitrate = (version == 3 ? BitratesV1 : BitratesV2)[layer - 1, bitrateIndex] * 1000;
        int sampleRate = SampleRates[version, rateIndex];
        int padding = (h[2] >> 1) & 1;
        int samples = layer == 1 ? 384 : layer == 2 || version == 3 ? 1152 : 576;
        int length = layer == 1
            ? (12 * bitrate / sampleRate + padding) * 4
            : samples / 8 * bitrate / sampleRate + padding;
        if (length < 4) return false;
        frame = new Mp3Frame(version, layer, sampleRate, length, samples, ((h[3] >> 6) & 3) == 3);
        return true;
    }

    private static AudioProbeResult Mp3(Stream stream)
    {
        long length = stream.Length;
        long start = 0;

        // Étiquettes ID3v2 en tête (parfois plusieurs).
        for (int guard = 0; guard < 4; guard++)
        {
            byte[] id3 = ReadAt(stream, start, 10);
            if (id3.Length < 10 || !Ascii(id3, 0, "ID3")) break;
            if ((id3[6] | id3[7] | id3[8] | id3[9]) >= 0x80) return AudioProbeResult.Invalid(PipeAudioFormat.Mp3);
            long size = (id3[6] << 21) | (id3[7] << 14) | (id3[8] << 7) | id3[9];
            start += 10 + size + ((id3[5] & 0x10) != 0 ? 10 : 0);
        }

        // Étiquettes de fin : ID3v1 (128 octets) puis APEv2.
        long end = length;
        if (end - start >= 128 && Ascii(ReadAt(stream, end - 128, 3), 0, "TAG")) end -= 128;
        if (end - start >= 32)
        {
            byte[] ape = ReadAt(stream, end - 32, 32);
            if (ape.Length == 32 && Ascii(ape, 0, "APETAGEX"))
            {
                long tagSize = BinaryPrimitives.ReadUInt32LittleEndian(ape.AsSpan(12));
                bool hasHeader = (BinaryPrimitives.ReadUInt32LittleEndian(ape.AsSpan(20)) & 0x80000000) != 0;
                end -= tagSize + (hasHeader ? 32 : 0);
            }
        }
        if (end <= start) return AudioProbeResult.Unsupported(PipeAudioFormat.Unknown);

        var reader = new WindowReader(stream, end);
        long first = FindFirstFrame(reader, start, Math.Min(end, start + ChunkBytes));
        if (first < 0) return AudioProbeResult.Unsupported(PipeAudioFormat.Unknown);

        TryParseMp3Header(reader.Span(first, 4), out var firstFrame);

        // En-tête Xing/Info (LAME) ou VBRI (Fraunhofer) dans la première trame : nombre exact de trames.
        int sideInfo = firstFrame.Version == 3 ? (firstFrame.Mono ? 17 : 32) : (firstFrame.Mono ? 9 : 17);
        long xingStart = first + 4 + sideInfo;
        var xing = reader.Span(xingStart, 12);
        bool hasInfoFrame = false;
        if (firstFrame.Layer == 3 && xing.Length >= 8 && (Ascii(xing, 0, "Xing") || Ascii(xing, 0, "Info")))
        {
            hasInfoFrame = true;
            uint flags = BinaryPrimitives.ReadUInt32BigEndian(xing[4..]);
            if ((flags & 1) != 0 && xing.Length >= 12)
            {
                uint frames = BinaryPrimitives.ReadUInt32BigEndian(xing[8..]);
                if (frames > 0)
                {
                    double samples = frames * (double)firstFrame.SamplesPerFrame;
                    // Extension LAME : retard d'encodeur et remplissage final, retirés comme le fait une
                    // lecture « gapless » (BASS, ffmpeg). Ignorée si incohérente.
                    long lameStart = xingStart + 8 + 4 + ((flags & 2) != 0 ? 4 : 0) + ((flags & 4) != 0 ? 100 : 0) + ((flags & 8) != 0 ? 4 : 0);
                    var lame = reader.Span(lameStart, 24);
                    if (lame.Length == 24 && (Ascii(lame, 0, "LAME") || Ascii(lame, 0, "Lavc") || Ascii(lame, 0, "Lavf")))
                    {
                        int delay = (lame[21] << 4) | (lame[22] >> 4);
                        int padding = ((lame[22] & 0x0F) << 8) | lame[23];
                        if (delay + padding < samples)
                            samples -= delay + padding;
                    }
                    return AudioProbeResult.Known(PipeAudioFormat.Mp3, samples * 1000 / firstFrame.SampleRate);
                }
            }
        }
        var vbri = reader.Span(first + 4 + 32, 18);
        if (firstFrame.Layer == 3 && vbri.Length >= 18 && Ascii(vbri, 0, "VBRI"))
        {
            hasInfoFrame = true;
            uint frames = BinaryPrimitives.ReadUInt32BigEndian(vbri[14..]);
            if (frames > 0)
                return AudioProbeResult.Known(PipeAudioFormat.Mp3, frames * (double)firstFrame.SamplesPerFrame * 1000 / firstFrame.SampleRate);
        }

        // Sans compteur : on compte les trames une à une (en-têtes seulement, aucun décodage).
        double seconds = 0;
        long frameCount = 0;
        long position = first;
        if (hasInfoFrame) position += firstFrame.Length; // la trame d'information ne contient pas d'audio
        while (position + 4 <= end)
        {
            if (TryParseMp3Header(reader.Span(position, 4), out var frame) && position + frame.Length <= end)
            {
                seconds += (double)frame.SamplesPerFrame / frame.SampleRate;
                frameCount++;
                position += frame.Length;
                continue;
            }
            // Synchronisation perdue (octets parasites) : resynchronisation bornée, sinon fin des données.
            long next = FindFirstFrame(reader, position + 1, Math.Min(end, position + 4096));
            if (next < 0) break;
            position = next;
        }
        return frameCount > 0
            ? AudioProbeResult.Known(PipeAudioFormat.Mp3, seconds * 1000)
            : AudioProbeResult.Invalid(PipeAudioFormat.Mp3);
    }

    /// <summary>Première position où deux en-têtes de trame cohérents se suivent (évite les faux synchronismes).</summary>
    private static long FindFirstFrame(WindowReader reader, long from, long limit)
    {
        for (long p = from; p + 4 <= limit; p++)
        {
            var header = reader.Span(p, 4);
            if (header.Length < 4 || header[0] != 0xFF || !TryParseMp3Header(header, out var frame)) continue;
            long next = p + frame.Length;
            if (next == reader.End) return p;
            if (TryParseMp3Header(reader.Span(next, 4), out var following) &&
                following.Version == frame.Version && following.Layer == frame.Layer && following.SampleRate == frame.SampleRate)
                return p;
        }
        return -1;
    }

    /// <summary>Fenêtre glissante de lecture : seuls des blocs de 64 Kio sont en mémoire.</summary>
    private sealed class WindowReader(Stream stream, long end)
    {
        private readonly byte[] _buffer = new byte[ChunkBytes];
        private long _start = -1;
        private int _length;

        public long End => end;

        public ReadOnlySpan<byte> Span(long position, int count)
        {
            if (position < 0 || position >= end) return [];
            count = (int)Math.Min(count, end - position);
            if (_start < 0 || position < _start || position + count > _start + _length)
            {
                stream.Position = position;
                _start = position;
                _length = stream.ReadAtLeast(_buffer.AsSpan(0, (int)Math.Min(_buffer.Length, end - position)), count, throwOnEndOfStream: false);
            }
            int offset = (int)(position - _start);
            return _buffer.AsSpan(offset, Math.Min(count, _length - offset));
        }
    }

    // ------------------------------------------------------------------ outils

    private static byte[] ReadAt(Stream stream, long position, int count)
    {
        if (position < 0 || position >= stream.Length || count <= 0) return [];
        stream.Position = position;
        var buffer = new byte[(int)Math.Min(count, stream.Length - position)];
        int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return read == buffer.Length ? buffer : buffer[..read];
    }

    private static bool Ascii(ReadOnlySpan<byte> buffer, int offset, string text)
    {
        if (offset < 0 || offset + text.Length > buffer.Length) return false;
        for (int i = 0; i < text.Length; i++)
            if (buffer[offset + i] != text[i]) return false;
        return true;
    }
}

/// <summary>
/// Durée audio de la beatmap courante pour le pipe (V3.14-COM-11). Lit uniquement le fichier
/// <c>AudioFilename</c> du dossier de la beatmap courante, en lecture seule avec partage complet.
/// La lecture se fait en tâche de fond : tant qu'elle n'est pas finie, le statut est
/// <see cref="PipeAudioStatus.Pending"/>. Le résultat est mis en cache par identité de fichier
/// (chemin, taille, date d'écriture) ; une erreur d'accès est retentée après <see cref="RetryAfterError"/>.
/// Thread-safe ; ne lève jamais.
/// </summary>
public sealed class AudioDurationReader
{
    public const long MaxFileBytes = 256L * 1024 * 1024;

    public static readonly TimeSpan RetryAfterError = TimeSpan.FromSeconds(5);

    private readonly Action<Action> _schedule;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private FileKey? _cachedKey;
    private AudioProbeResult _cached;
    private DateTimeOffset _cachedAt;
    private FileKey? _pendingKey;
    private int _probeCount;

    /// <param name="schedule">Exécution de la lecture ; par défaut le pool de threads (les tests l'exécutent en ligne).</param>
    public AudioDurationReader(Action<Action>? schedule = null, Func<DateTimeOffset>? clock = null)
    {
        _schedule = schedule ?? (work => ThreadPool.UnsafeQueueUserWorkItem(_ => work(), null));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Nombre de fichiers réellement ouverts (hors cache).</summary>
    public int ProbeCount => Volatile.Read(ref _probeCount);

    private readonly record struct FileKey(string Path, long Length, DateTime LastWriteUtc);

    /// <summary>État de la durée audio de <paramref name="audioFilename"/>, dans le dossier de <paramref name="beatmapPath"/>.</summary>
    public PipeAudioFile? Get(string beatmapPath, string? audioFilename)
    {
        if (string.IsNullOrEmpty(audioFilename) || string.IsNullOrEmpty(beatmapPath) || !Path.IsPathFullyQualified(beatmapPath))
            return null;
        if (audioFilename.Length > 255 || audioFilename is "." or ".." ||
            audioFilename.IndexOfAny(['/', '\\', ':']) >= 0 || audioFilename.Any(char.IsControl))
            return null;
        string? folder = Path.GetDirectoryName(beatmapPath);
        if (folder is null) return null;

        PipeAudioFile Result(PipeAudioStatus status, PipeAudioFormat format = PipeAudioFormat.Unknown, double? duration = null,
            long length = 0, DateTimeOffset? lastWrite = null) =>
            new(beatmapPath, audioFilename, status, format, duration, length, lastWrite);

        FileInfo info;
        try
        {
            info = new FileInfo(Path.Combine(folder, audioFilename));
            if (!info.Exists) return Result(PipeAudioStatus.Missing);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) return Result(PipeAudioStatus.Unreadable);
            if (info.Length <= 0) return Result(PipeAudioStatus.Invalid);
            if (info.Length > MaxFileBytes) return Result(PipeAudioStatus.Unsupported);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Result(PipeAudioStatus.Unreadable);
        }

        var key = new FileKey(info.FullName, info.Length, info.LastWriteTimeUtc);
        var lastWriteUtc = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
        var now = _clock();
        lock (_gate)
        {
            if (_cachedKey == key && !(_cached.Status == PipeAudioStatus.Unreadable && now - _cachedAt >= RetryAfterError))
                return Result(_cached.Status, _cached.Format, _cached.DurationMs, key.Length, lastWriteUtc);
            if (_pendingKey == key)
                return Result(PipeAudioStatus.Pending, length: key.Length, lastWrite: lastWriteUtc);
            _pendingKey = key;
        }

        _schedule(() =>
        {
            AudioProbeResult probe = ProbeFile(key);
            lock (_gate)
            {
                _probeCount++;
                if (_pendingKey != key) return; // une autre beatmap est devenue courante entre-temps
                _pendingKey = null;
                _cachedKey = key;
                _cached = probe;
                _cachedAt = _clock();
            }
        });

        lock (_gate)
        {
            // Exécution en ligne (tests) : le résultat est déjà là.
            if (_cachedKey == key && _pendingKey != key)
                return Result(_cached.Status, _cached.Format, _cached.DurationMs, key.Length, lastWriteUtc);
        }
        return Result(PipeAudioStatus.Pending, length: key.Length, lastWrite: lastWriteUtc);
    }

    private static AudioProbeResult ProbeFile(FileKey key)
    {
        try
        {
            // osu! garde le fichier ouvert pendant la lecture : partage complet, lecture seule.
            using var stream = new FileStream(key.Path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
            if (stream.Length != key.Length) return new(PipeAudioStatus.Unreadable, PipeAudioFormat.Unknown, null);
            return AudioDurationProbe.Probe(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(PipeAudioStatus.Unreadable, PipeAudioFormat.Unknown, null);
        }
        catch (Exception)
        {
            // Contenu inattendu : jamais d'exception vers BeatInsight, jamais de durée inventée.
            return AudioProbeResult.Invalid(PipeAudioFormat.Unknown);
        }
    }
}
