using System.IO;
using System.Security.Cryptography;
using System.Text;
using BeatInsight.Integration.CommunityOverlays.Pipe;

namespace BeatInsight.Integration.CommunityOverlays;

/// <summary>
/// Lit uniquement le fichier .osu de la beatmap courante, en lecture seule (V3.14-COM-10) :
/// MD5 réel du fichier, <c>AudioFilename</c>, <c>TitleUnicode</c>, <c>ArtistUnicode</c>.
/// Résultat mis en cache par identité de fichier (chemin, taille, date d'écriture) :
/// aucune relecture tant que le fichier ne change pas. Aucun parcours du dossier Songs.
/// Thread-safe ; à appeler hors du thread UI.
/// </summary>
public sealed class OsuFileIdentityReader
{
    /// <summary>Au-delà, le fichier n'est pas lu (un .osu dépasse rarement quelques Mo).</summary>
    public const long MaxFileBytes = 16 * 1024 * 1024;

    private readonly object _gate = new();
    private (string Path, long Length, DateTime LastWriteUtc)? _cachedKey;
    private PipeOsuFile? _cached;

    public int ReadCount { get; private set; }

    /// <summary>Renvoie l'identité du fichier, ou null s'il est absent, illisible ou hors limites.</summary>
    public PipeOsuFile? Read(string? beatmapPath)
    {
        if (string.IsNullOrWhiteSpace(beatmapPath) ||
            !Path.IsPathFullyQualified(beatmapPath) ||
            !beatmapPath.EndsWith(".osu", StringComparison.OrdinalIgnoreCase))
            return null;

        FileInfo info;
        try
        {
            info = new FileInfo(beatmapPath);
            if (!info.Exists || info.Length <= 0 || info.Length > MaxFileBytes ||
                (info.Attributes & FileAttributes.ReparsePoint) != 0)
                return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }

        var key = (info.FullName, info.Length, info.LastWriteTimeUtc);
        lock (_gate)
        {
            if (_cachedKey is { } cachedKey &&
                string.Equals(cachedKey.Path, key.FullName, StringComparison.OrdinalIgnoreCase) &&
                cachedKey.Length == key.Length && cachedKey.LastWriteUtc == key.LastWriteTimeUtc)
                return _cached;
        }

        PipeOsuFile? result = ReadUncached(info);
        lock (_gate)
        {
            _cachedKey = key;
            _cached = result;
            ReadCount++;
        }
        return result;
    }

    private static PipeOsuFile? ReadUncached(FileInfo info)
    {
        byte[] bytes;
        try
        {
            // osu! peut garder le fichier ouvert : partage complet, lecture seule.
            using var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
            if (stream.Length != info.Length) return null;
            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        string md5 = Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
        var header = ParseHeader(bytes);
        return new PipeOsuFile(
            info.FullName,
            info.Length,
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            md5,
            SafeFileName(header.GetValueOrDefault("AudioFilename")),
            Text(header.GetValueOrDefault("TitleUnicode")),
            Text(header.GetValueOrDefault("ArtistUnicode")));
    }

    /// <summary>Lit les clés des sections [General] et [Metadata] uniquement.</summary>
    internal static Dictionary<string, string> ParseHeader(byte[] bytes)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string text = Encoding.UTF8.GetString(bytes);
        string? section = null;
        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim().TrimStart('﻿');
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line;
                // [General] et [Metadata] précèdent toujours ces sections : inutile de lire la suite.
                if (section is "[Events]" or "[TimingPoints]" or "[HitObjects]")
                    break;
                continue;
            }
            if (section is not "[General]" and not "[Metadata]") continue;
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            string key = line[..colon].Trim();
            if (key is "AudioFilename" or "TitleUnicode" or "ArtistUnicode")
                values.TryAdd(key, line[(colon + 1)..].Trim());
        }
        return values;
    }

    /// <summary>Nom de fichier simple uniquement : aucun séparateur ni remontée.</summary>
    private static string? SafeFileName(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 255 || value is "." or ".." ||
            value.IndexOfAny(['/', '\\', ':']) >= 0 || value.Any(char.IsControl))
            return null;
        return value;
    }

    private static string? Text(string? value) =>
        string.IsNullOrEmpty(value) || value.Length > CommunityOverlayPipeProtocol.MaxTextLength || value.Any(char.IsControl)
            ? null
            : value;
}
