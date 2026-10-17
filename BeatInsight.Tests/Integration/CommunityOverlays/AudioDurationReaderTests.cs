using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using BeatInsight.Integration.CommunityOverlays;
using BeatInsight.Integration.CommunityOverlays.Pipe;
using BeatInsight.Integration.Game;
using Xunit;

namespace BeatInsight.Tests.Integration.CommunityOverlays;

/// <summary>
/// V3.14-COM-11 : durée réelle du fichier audio de la beatmap courante (en-têtes seulement),
/// cache par identité de fichier, et compatibilité du pipe v1/v2.
/// Les fichiers audio sont synthétiques et construits octet par octet : aucun encodeur, aucun dossier Songs.
/// </summary>
public sealed class AudioDurationReaderTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "bi-com11-" + Guid.NewGuid().ToString("N"));
    private readonly string _folder;

    public AudioDurationReaderTests()
    {
        _folder = Path.Combine(_sandbox, "Songs", "1 A - B");
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_sandbox, recursive: true); } catch (IOException) { }
    }

    private string MapPath => Path.Combine(_folder, "A - B (M) [X].osu");

    private string Write(string name, byte[] content)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static AudioProbeResult Probe(byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);
        return AudioDurationProbe.Probe(stream);
    }

    // ------------------------------------------------------------ fichiers synthétiques

    /// <summary>WAV : en-tête RIFF, bloc fmt (16 ou 40 octets), bloc LIST facultatif, données.</summary>
    internal static byte[] Wav(int sampleRate, int channels, int bitsPerSample, long frames, ushort formatTag = 1,
        bool extensible = false, bool listChunk = false, long? declaredDataBytes = null, bool dataBeforeFmt = false)
    {
        int blockAlign = channels * bitsPerSample / 8;
        long dataBytes = frames * blockAlign;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(0u);
        w.Write("WAVE"u8);

        void Fmt()
        {
            w.Write("fmt "u8);
            w.Write(extensible ? 40u : 16u);
            w.Write(extensible ? (ushort)0xFFFE : formatTag);
            w.Write((ushort)channels);
            w.Write((uint)sampleRate);
            w.Write((uint)(sampleRate * blockAlign));
            w.Write((ushort)blockAlign);
            w.Write((ushort)bitsPerSample);
            if (extensible)
            {
                w.Write((ushort)22);
                w.Write((ushort)bitsPerSample);
                w.Write(0x3Fu);
                w.Write(formatTag); // 2 premiers octets du GUID de sous-format
                w.Write(new byte[14]);
            }
        }
        void Data()
        {
            w.Write("data"u8);
            w.Write((uint)(declaredDataBytes ?? dataBytes));
            w.Write(new byte[dataBytes]);
        }

        if (dataBeforeFmt) { Data(); Fmt(); }
        else
        {
            Fmt();
            if (listChunk)
            {
                w.Write("LIST"u8);
                w.Write(5u);
                w.Write("INFOx"u8);
                w.Write((byte)0); // octet de bourrage (taille impaire)
            }
            Data();
        }
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>MPEG-1 Layer III, 48 kHz, 128 kbit/s : trames de 384 octets sans bourrage, 1 152 échantillons.</summary>
    internal static byte[] Mp3Frame(bool mono = false, byte[]? payload = null)
    {
        var frame = new byte[384];
        frame[0] = 0xFF;
        frame[1] = 0xFB;
        frame[2] = 0x94;
        frame[3] = mono ? (byte)0xC0 : (byte)0x00;
        payload?.CopyTo(frame, 4);
        return frame;
    }

    internal static byte[] Mp3(int audioFrames, bool infoFrame = false, int lameDelay = -1, int lamePadding = 0,
        bool id3v2 = false, bool id3v1 = false, int vbriFrames = -1)
    {
        using var ms = new MemoryStream();
        if (id3v2)
        {
            // ID3v2.3 de 300 octets (taille « syncsafe »).
            ms.Write([(byte)'I', (byte)'D', (byte)'3', 3, 0, 0, 0, 0, 2, 44]);
            ms.Write(new byte[300]);
        }
        if (infoFrame)
        {
            var info = new byte[200];
            // Décalage 32 (MPEG-1 stéréo) : « Info », drapeaux trames + octets + TOC + qualité.
            Encoding.ASCII.GetBytes("Info").CopyTo(info, 32);
            BinaryPrimitives.WriteUInt32BigEndian(info.AsSpan(36), 0x0F);
            BinaryPrimitives.WriteUInt32BigEndian(info.AsSpan(40), (uint)audioFrames);
            BinaryPrimitives.WriteUInt32BigEndian(info.AsSpan(44), (uint)(audioFrames * 384));
            if (lameDelay >= 0)
            {
                int lame = 32 + 8 + 4 + 4 + 100 + 4;
                Encoding.ASCII.GetBytes("LAME3.100").CopyTo(info, lame);
                info[lame + 21] = (byte)(lameDelay >> 4);
                info[lame + 22] = (byte)(((lameDelay & 0x0F) << 4) | (lamePadding >> 8));
                info[lame + 23] = (byte)(lamePadding & 0xFF);
            }
            ms.Write(Mp3Frame(payload: info));
        }
        if (vbriFrames >= 0)
        {
            var vbri = new byte[60];
            Encoding.ASCII.GetBytes("VBRI").CopyTo(vbri, 32);
            BinaryPrimitives.WriteUInt32BigEndian(vbri.AsSpan(32 + 14), (uint)vbriFrames);
            ms.Write(Mp3Frame(payload: vbri));
        }
        for (int i = 0; i < audioFrames; i++)
            ms.Write(Mp3Frame());
        if (id3v1)
        {
            var tag = new byte[128];
            "TAG"u8.CopyTo(tag);
            ms.Write(tag);
        }
        return ms.ToArray();
    }

    private static byte[] OggPage(byte headerType, long granule, uint serial, uint sequence, byte[] body)
    {
        var lacing = new List<byte>();
        int remaining = body.Length;
        while (remaining >= 255) { lacing.Add(255); remaining -= 255; }
        lacing.Add((byte)remaining);
        var page = new byte[27 + lacing.Count + body.Length];
        "OggS"u8.CopyTo(page);
        page[5] = headerType;
        BinaryPrimitives.WriteInt64LittleEndian(page.AsSpan(6), granule);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(14), serial);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(18), sequence);
        page[26] = (byte)lacing.Count;
        lacing.ToArray().CopyTo(page, 27);
        body.CopyTo(page, 27 + lacing.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(22), AudioDurationProbe.OggCrc(page));
        return page;
    }

    internal static byte[] OggVorbis(int sampleRate, long lastGranule, bool trailingNoGranulePage = false,
        bool corruptLastPage = false, bool truncateLastPage = false)
    {
        var id = new byte[30];
        id[0] = 0x01;
        "vorbis"u8.CopyTo(id.AsSpan(1));
        id[11] = 2;
        BinaryPrimitives.WriteUInt32LittleEndian(id.AsSpan(12), (uint)sampleRate);
        id[29] = 1;
        const uint serial = 0x1234;
        using var ms = new MemoryStream();
        ms.Write(OggPage(0x02, 0, serial, 0, id));
        ms.Write(OggPage(0x00, 0, serial, 1, new byte[600]));
        ms.Write(OggPage(0x00, lastGranule / 2, serial, 2, new byte[4000]));
        // Une page d'un autre flux logique ne doit pas être prise pour la fin.
        ms.Write(OggPage(0x00, 999_999_999, 0x9999, 0, new byte[100]));
        var last = OggPage(0x04, lastGranule, serial, 3, new byte[3000]);
        if (corruptLastPage) last[100] ^= 0xFF;
        ms.Write(truncateLastPage ? last[..1000] : last);
        if (trailingNoGranulePage) ms.Write(OggPage(0x00, -1, serial, 4, new byte[50]));
        return ms.ToArray();
    }

    internal static byte[] OggOpus(int preSkip, long lastGranule)
    {
        var head = new byte[19];
        "OpusHead"u8.CopyTo(head);
        head[8] = 1;
        head[9] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(head.AsSpan(10), (ushort)preSkip);
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(12), 44_100);
        using var ms = new MemoryStream();
        ms.Write(OggPage(0x02, 0, 7, 0, head));
        ms.Write(OggPage(0x04, lastGranule, 7, 1, new byte[500]));
        return ms.ToArray();
    }

    // ------------------------------------------------------------ WAV

    [Fact]
    public void Wav_pcm_duration_is_exact()
    {
        var r = Probe(Wav(44_100, 2, 16, frames: 44_100 * 3 + 22_050));

        Assert.Equal(PipeAudioStatus.Known, r.Status);
        Assert.Equal(PipeAudioFormat.Wav, r.Format);
        Assert.Equal(3_500, r.DurationMs);
    }

    [Fact]
    public void Wav_extensible_float_and_extra_chunks_are_supported()
    {
        Assert.Equal(2_000, Probe(Wav(48_000, 6, 32, 96_000, formatTag: 3, extensible: true)).DurationMs);
        Assert.Equal(1_000, Probe(Wav(22_050, 1, 8, 22_050, listChunk: true)).DurationMs);
    }

    [Fact]
    public void Wav_truncated_counts_only_present_data()
    {
        // En-tête annonçant 10 s, seulement 1 s de données réellement présente.
        var r = Probe(Wav(44_100, 2, 16, 44_100, declaredDataBytes: 44_100L * 4 * 10));

        Assert.Equal(1_000, r.DurationMs);
    }

    [Fact]
    public void Wav_compressed_or_malformed_never_gets_an_estimated_duration()
    {
        Assert.Equal(PipeAudioStatus.Unsupported, Probe(Wav(44_100, 2, 16, 44_100, formatTag: 0x55)).Status); // MP3 dans WAV
        Assert.Equal(PipeAudioStatus.Invalid, Probe(Wav(44_100, 2, 16, 44_100, dataBeforeFmt: true)).Status);
        Assert.Null(Probe(Wav(44_100, 2, 16, 44_100, formatTag: 0x55)).DurationMs);
    }

    // ------------------------------------------------------------ MP3

    [Fact]
    public void Mp3_without_info_header_counts_every_frame()
    {
        var r = Probe(Mp3(audioFrames: 125)); // 125 × 1152 / 48000 = 3 s

        Assert.Equal(PipeAudioStatus.Known, r.Status);
        Assert.Equal(PipeAudioFormat.Mp3, r.Format);
        Assert.Equal(3_000, r.DurationMs);
    }

    [Fact]
    public void Mp3_info_header_frame_count_is_used_and_not_counted_as_audio()
    {
        Assert.Equal(3_000, Probe(Mp3(125, infoFrame: true)).DurationMs);
        Assert.Equal(3_000, Probe(Mp3(125, vbriFrames: 125)).DurationMs);
    }

    [Fact]
    public void Mp3_lame_delay_and_padding_are_removed()
    {
        // 125 trames = 144 000 échantillons ; 576 + 1 824 retirés → 141 600 / 48 000 = 2 950 ms.
        Assert.Equal(2_950, Probe(Mp3(125, infoFrame: true, lameDelay: 576, lamePadding: 1_824)).DurationMs);
    }

    [Fact]
    public void Mp3_id3_tags_at_start_and_end_are_skipped()
    {
        Assert.Equal(3_000, Probe(Mp3(125, id3v2: true, id3v1: true)).DurationMs);
        Assert.Equal(3_000, Probe(Mp3(125, infoFrame: true, id3v2: true)).DurationMs);
    }

    [Fact]
    public void Mp3_truncated_last_frame_is_not_counted()
    {
        byte[] full = Mp3(125);
        var r = Probe(full[..(full.Length - 100)]);

        Assert.Equal(Math.Round(124 * 1152 * 1000.0 / 48_000), r.DurationMs);
    }

    [Fact]
    public void Random_or_empty_content_is_never_given_a_duration()
    {
        var random = new byte[50_000];
        new Random(1234).NextBytes(random);
        for (int i = 0; i < random.Length; i++) if (random[i] == 0xFF) random[i] = 0; // aucune synchro possible

        Assert.Equal(PipeAudioStatus.Unsupported, Probe(random).Status);
        Assert.Equal(PipeAudioStatus.Unsupported, Probe([]).Status);
        Assert.Equal(PipeAudioStatus.Unsupported, Probe("fLaC\0\0\0\0"u8.ToArray()).Status);
        Assert.Null(Probe(random).DurationMs);
    }

    // ------------------------------------------------------------ OGG

    [Fact]
    public void Ogg_vorbis_duration_comes_from_the_last_granule()
    {
        var r = Probe(OggVorbis(44_100, lastGranule: 44_100 * 7 + 4_410));

        Assert.Equal(PipeAudioStatus.Known, r.Status);
        Assert.Equal(PipeAudioFormat.Ogg, r.Format);
        Assert.Equal(7_100, r.DurationMs);
    }

    [Fact]
    public void Ogg_page_without_granule_or_truncated_falls_back_to_the_previous_page()
    {
        Assert.Equal(7_000, Probe(OggVorbis(44_100, 44_100 * 7, trailingNoGranulePage: true)).DurationMs);
        // Dernière page coupée : la page complète précédente (moitié de la durée) est la vraie fin des données.
        Assert.Equal(3_500, Probe(OggVorbis(44_100, 44_100 * 7, truncateLastPage: true)).DurationMs);
    }

    [Fact]
    public void Ogg_corrupted_last_page_is_invalid_not_shortened()
    {
        var r = Probe(OggVorbis(44_100, 44_100 * 7, corruptLastPage: true));

        Assert.Equal(PipeAudioStatus.Invalid, r.Status);
        Assert.Null(r.DurationMs);
    }

    [Fact]
    public void Ogg_opus_removes_pre_skip_at_48_khz()
    {
        Assert.Equal(2_000, Probe(OggOpus(preSkip: 312, lastGranule: 96_000 + 312)).DurationMs);
    }

    [Fact]
    public void Content_is_detected_by_signature_not_extension()
    {
        string path = Write("audio.mp3", OggVorbis(44_100, 44_100 * 2));
        var reader = new AudioDurationReader(schedule: work => work());

        var audio = reader.Get(MapPath, "audio.mp3")!;

        Assert.Equal(PipeAudioFormat.Ogg, audio.Format);
        Assert.Equal(2_000, audio.DurationMs);
        Assert.True(File.Exists(path));
    }

    // ------------------------------------------------------------ lecteur et cache

    [Fact]
    public void Reader_reads_only_the_current_beatmap_audio_and_caches_by_identity()
    {
        Write("audio.wav", Wav(44_100, 2, 16, 44_100 * 2));
        var reader = new AudioDurationReader(schedule: work => work());

        var first = reader.Get(MapPath, "audio.wav")!;
        var second = reader.Get(MapPath, "audio.wav")!;
        // Autre difficulté du même set, même fichier audio : pas de nouvelle lecture.
        var otherDiff = reader.Get(Path.Combine(_folder, "A - B (M) [Y].osu"), "audio.wav")!;

        Assert.Equal(PipeAudioStatus.Known, first.Status);
        Assert.Equal(2_000, first.DurationMs);
        Assert.Equal(first, second);
        Assert.Equal(2_000, otherDiff.DurationMs);
        Assert.Equal(Path.Combine(_folder, "A - B (M) [Y].osu"), otherDiff.BeatmapPath);
        Assert.Equal(1, reader.ProbeCount);
    }

    [Fact]
    public void Reader_rereads_when_the_file_changes()
    {
        string path = Write("audio.wav", Wav(44_100, 2, 16, 44_100));
        var reader = new AudioDurationReader(schedule: work => work());
        Assert.Equal(1_000, reader.Get(MapPath, "audio.wav")!.DurationMs);

        File.WriteAllBytes(path, Wav(44_100, 2, 16, 44_100 * 4));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        Assert.Equal(4_000, reader.Get(MapPath, "audio.wav")!.DurationMs);
        Assert.Equal(2, reader.ProbeCount);
    }

    [Fact]
    public void Reader_is_pending_until_background_read_finishes()
    {
        Write("audio.wav", Wav(44_100, 2, 16, 44_100));
        var queued = new List<Action>();
        var reader = new AudioDurationReader(schedule: queued.Add);

        var pending = reader.Get(MapPath, "audio.wav")!;
        var stillPending = reader.Get(MapPath, "audio.wav")!;
        Assert.Equal(PipeAudioStatus.Pending, pending.Status);
        Assert.Null(pending.DurationMs);
        Assert.Equal(PipeAudioStatus.Pending, stillPending.Status);
        Assert.Single(queued); // une seule lecture lancée pour le même fichier

        queued[0]();

        Assert.Equal(1_000, reader.Get(MapPath, "audio.wav")!.DurationMs);
    }

    [Fact]
    public void Reader_drops_a_result_that_finishes_after_a_beatmap_change()
    {
        Write("a.wav", Wav(44_100, 2, 16, 44_100));
        Write("b.wav", Wav(44_100, 2, 16, 44_100 * 3));
        var queued = new List<Action>();
        var reader = new AudioDurationReader(schedule: queued.Add);

        reader.Get(MapPath, "a.wav");
        reader.Get(MapPath, "b.wav"); // la beatmap courante a changé
        queued[0](); // la lecture de a.wav se termine en retard

        Assert.Equal(PipeAudioStatus.Pending, reader.Get(MapPath, "b.wav")!.Status);
        queued[1]();
        Assert.Equal(3_000, reader.Get(MapPath, "b.wav")!.DurationMs);
    }

    [Fact]
    public void Reader_missing_unsafe_and_invalid_inputs_are_never_known()
    {
        var reader = new AudioDurationReader(schedule: work => work());
        Write("empty.mp3", []);
        Write("corrupt.ogg", OggVorbis(44_100, 44_100, corruptLastPage: true));

        Assert.Equal(PipeAudioStatus.Missing, reader.Get(MapPath, "absent.mp3")!.Status);
        Assert.Equal(PipeAudioStatus.Invalid, reader.Get(MapPath, "empty.mp3")!.Status);
        Assert.Equal(PipeAudioStatus.Invalid, reader.Get(MapPath, "corrupt.ogg")!.Status);
        Assert.Null(reader.Get(MapPath, @"..\..\secret.mp3"));
        Assert.Null(reader.Get(MapPath, "C:secret.mp3"));
        Assert.Null(reader.Get(MapPath, null));
        Assert.Null(reader.Get("relative\\map.osu", "audio.mp3"));
    }

    [Fact]
    public void Reader_locked_file_is_unreadable_then_retried()
    {
        string path = Write("audio.wav", Wav(44_100, 2, 16, 44_100));
        var now = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
        var reader = new AudioDurationReader(schedule: work => work(), clock: () => now);

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal(PipeAudioStatus.Unreadable, reader.Get(MapPath, "audio.wav")!.Status);
            now += TimeSpan.FromSeconds(1);
            Assert.Equal(PipeAudioStatus.Unreadable, reader.Get(MapPath, "audio.wav")!.Status);
            Assert.Equal(1, reader.ProbeCount); // pas de relecture à chaque tick
        }

        now += AudioDurationReader.RetryAfterError;
        Assert.Equal(1_000, reader.Get(MapPath, "audio.wav")!.DurationMs);
    }

    [Fact]
    public void Reader_deleted_file_becomes_missing()
    {
        string path = Write("audio.wav", Wav(44_100, 2, 16, 44_100));
        var reader = new AudioDurationReader(schedule: work => work());
        Assert.Equal(PipeAudioStatus.Known, reader.Get(MapPath, "audio.wav")!.Status);

        File.Delete(path);

        Assert.Equal(PipeAudioStatus.Missing, reader.Get(MapPath, "audio.wav")!.Status);
    }

    [Fact]
    public void Reader_does_not_block_osu_from_writing_or_deleting_the_file()
    {
        // Lecture avec partage complet : un autre process peut écrire ou supprimer pendant la lecture.
        string path = Write("audio.wav", Wav(44_100, 2, 16, 44_100));
        var reader = new AudioDurationReader(schedule: work =>
        {
            using var other = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            work();
        });

        Assert.Equal(1_000, reader.Get(MapPath, "audio.wav")!.DurationMs);
    }

    // ------------------------------------------------------------ pipe v1 / v2

    private static GameStateSnapshot Game(string path) =>
        new("NativeStableGameProvider", "osu! stable", 4242, "osu!.exe:4242:1", path, "123", "456", DateTimeOffset.UtcNow,
            GameStateAvailability.Available, null)
        {
            SongsDirectory = Path.GetDirectoryName(Path.GetDirectoryName(path)),
        };

    private static PipeOsuFile Osu(string path, string audio = "audio.wav") =>
        new(path, 100, DateTimeOffset.UtcNow, "0123456789abcdef0123456789abcdef", audio, null, null);

    [Fact]
    public void Mapper_attaches_audio_only_for_the_current_osu_and_its_audio_filename()
    {
        string path = MapPath;
        var frame = new CommunityOverlaySourceFrame(null, Game(path), false, false, false);
        var audio = new PipeAudioFile(path, "audio.wav", PipeAudioStatus.Known, PipeAudioFormat.Wav, 1_000, 10, DateTimeOffset.UtcNow);

        var ok = CommunityOverlayPipeSnapshotMapper.Map(frame, Osu(path), 1, DateTimeOffset.UtcNow, audio);
        var otherAudio = CommunityOverlayPipeSnapshotMapper.Map(frame, Osu(path, "other.mp3"), 1, DateTimeOffset.UtcNow, audio);
        var otherMap = CommunityOverlayPipeSnapshotMapper.Map(frame, Osu(path), 1, DateTimeOffset.UtcNow,
            audio with { BeatmapPath = Path.Combine(_folder, "other.osu") });

        Assert.Equal(2, ok.Version);
        Assert.Equal(1_000, ok.Snapshot.Audio!.DurationMs);
        Assert.Null(CommunityOverlayPipeProtocol.Validate(ok));
        Assert.Null(otherAudio.Snapshot.Audio);
        Assert.Null(otherMap.Snapshot.Audio);
    }

    [Fact]
    public void Version1_message_has_exactly_the_com10_shape()
    {
        string path = MapPath;
        var frame = new CommunityOverlaySourceFrame(null, Game(path), false, false, false);
        var audio = new PipeAudioFile(path, "audio.wav", PipeAudioStatus.Known, PipeAudioFormat.Wav, 1_000, 10, DateTimeOffset.UtcNow);
        var v2 = CommunityOverlayPipeSnapshotMapper.Map(frame, Osu(path), 1, DateTimeOffset.UtcNow, audio);

        var v1 = CommunityOverlayPipeSnapshotMapper.ToVersion1(v2);
        using var json = JsonDocument.Parse(CommunityOverlayPipeProtocol.Serialize(v1));

        Assert.Equal(1, json.RootElement.GetProperty("version").GetInt32());
        // Membres du snapshot de COM-10, sans aucun ajout (le compagnon COM-10 refuse les membres inconnus).
        Assert.Equal(
            ["game", "route", "metadata", "osuFile", "liveGameplay", "stats", "performance", "suspension"],
            json.RootElement.GetProperty("snapshot").EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(["protocol", "version", "sequence", "sentAtUtc", "snapshot"],
            json.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Null(CommunityOverlayPipeProtocol.Validate(v1));
    }

    [Fact]
    public void Audio_block_contract_is_strict()
    {
        string path = MapPath;
        var frame = new CommunityOverlaySourceFrame(null, Game(path), false, false, false);
        var audio = new PipeAudioFile(path, "audio.wav", PipeAudioStatus.Known, PipeAudioFormat.Wav, 1_000, 10, DateTimeOffset.UtcNow);
        var e = CommunityOverlayPipeSnapshotMapper.Map(frame, Osu(path), 1, DateTimeOffset.UtcNow, audio);
        PipeEnvelope With(PipeAudioFile a) => e with { Snapshot = e.Snapshot with { Audio = a } };

        Assert.NotNull(CommunityOverlayPipeProtocol.Validate(e with { Version = 1 })); // pas de bloc audio en v1
        Assert.NotNull(CommunityOverlayPipeProtocol.Validate(With(audio with { DurationMs = null })));
        Assert.NotNull(CommunityOverlayPipeProtocol.Validate(With(audio with { DurationMs = double.NaN })));
        Assert.NotNull(CommunityOverlayPipeProtocol.Validate(With(audio with { DurationMs = 0 })));
        Assert.NotNull(CommunityOverlayPipeProtocol.Validate(With(audio with { DurationMs = 4 * 3_600_000.0 })));
        Assert.NotNull(CommunityOverlayPipeProtocol.Validate(With(audio with { Status = PipeAudioStatus.Pending })));
        Assert.NotNull(CommunityOverlayPipeProtocol.Validate(With(audio with { AudioFilename = @"..\x.mp3" })));
        Assert.Null(CommunityOverlayPipeProtocol.Validate(With(audio with { Status = PipeAudioStatus.Missing, DurationMs = null })));
    }

    [Fact]
    public async Task Bridge_publishes_v2_with_audio_and_v1_without_on_separate_pipes()
    {
        Write("audio.wav", Wav(44_100, 2, 16, 44_100 * 2));
        File.WriteAllText(MapPath, "osu file format v14\n\n[General]\nAudioFilename: audio.wav\n\n[HitObjects]\n", new UTF8Encoding(false));
        string v2Name = "BeatInsight.Test." + Guid.NewGuid().ToString("N");
        string v1Name = "BeatInsight.Test." + Guid.NewGuid().ToString("N");
        var game = Game(MapPath);
        using var bridge = new CommunityOverlayLiveBridge(
            () => new CommunityOverlaySourceFrame(null, game, false, false, false),
            new CommunityOverlayPipeServer(v2Name),
            legacyServer: new CommunityOverlayPipeServer(v1Name),
            audio: new AudioDurationReader(schedule: work => work()));
        Assert.True(bridge.Start());

        async Task<PipeEnvelope> ReadOne(string name)
        {
            using var reader = new NamedPipeClientStream(".", name, PipeDirection.In, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await reader.ConnectAsync(5_000);
            byte[]? frame = await CommunityOverlayPipeProtocol.ReadFrameAsync(reader, new CancellationTokenSource(5_000).Token);
            var envelope = CommunityOverlayPipeProtocol.TryDeserialize(frame, out string? rejection);
            Assert.Null(rejection);
            return envelope!;
        }

        var v2 = await ReadOne(v2Name);
        var v1 = await ReadOne(v1Name);

        Assert.Equal(2, v2.Version);
        Assert.Equal(PipeAudioStatus.Known, v2.Snapshot.Audio!.Status);
        Assert.Equal(2_000, v2.Snapshot.Audio.DurationMs);
        Assert.Equal(1, v1.Version);
        Assert.Null(v1.Snapshot.Audio);
        Assert.Equal("audio.wav", v1.Snapshot.OsuFile!.AudioFilename);
    }
}
