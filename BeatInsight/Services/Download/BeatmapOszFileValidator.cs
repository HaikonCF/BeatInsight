using System.IO;
using System.Text;

namespace BeatInsight.Services.Download;

/// <summary>
/// Vérifie qu'un fichier téléchargé ressemble réellement à un .osz avant
/// de le remettre à osu!. Un HTTP 200 seul ne garantit rien : une page
/// d'erreur ou de login HTML peut être servie avec ce statut.
/// </summary>
internal static class BeatmapOszFileValidator
{
    // Signature de fichier local ZIP ("PK\x03\x04"). Une archive vide
    // ("PK\x05\x06") est également acceptée : elle reste un .osz
    // structurellement valide même si son contenu est inhabituel.
    private static readonly byte[] ZipLocalFileSignature = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] ZipEmptyArchiveSignature = [0x50, 0x4B, 0x05, 0x06];

    private const int SniffLength = 512;

    internal static bool IsValidOsz(string filePath, out string? rejectionReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            rejectionReason = "File does not exist.";
            return false;
        }

        byte[] header;

        try
        {
            using FileStream stream = File.OpenRead(filePath);

            if (stream.Length == 0)
            {
                rejectionReason = "Downloaded file is empty.";
                return false;
            }

            header = new byte[Math.Min(SniffLength, stream.Length)];
            int read = stream.Read(header, 0, header.Length);

            if (read < header.Length)
            {
                Array.Resize(ref header, read);
            }
        }
        catch (IOException ex)
        {
            rejectionReason = $"Unable to read downloaded file: {ex.Message}";
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            rejectionReason = $"Unable to read downloaded file: {ex.Message}";
            return false;
        }

        if (StartsWith(header, ZipLocalFileSignature)
            || StartsWith(header, ZipEmptyArchiveSignature))
        {
            rejectionReason = null;
            return true;
        }

        if (LooksLikeHtmlOrText(header))
        {
            rejectionReason =
                "Downloaded file looks like an HTML/error page, not a .osz archive.";
            return false;
        }

        rejectionReason = "Downloaded file is not a recognizable .osz archive.";
        return false;
    }

    private static bool StartsWith(byte[] header, byte[] signature)
    {
        if (header.Length < signature.Length)
        {
            return false;
        }

        for (int i = 0; i < signature.Length; i++)
        {
            if (header[i] != signature[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool LooksLikeHtmlOrText(byte[] header)
    {
        string sample;

        try
        {
            sample = Encoding.UTF8.GetString(header).TrimStart();
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        return sample.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
            || sample.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
            || sample.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
            || sample.StartsWith("{\"error\"", StringComparison.OrdinalIgnoreCase);
    }
}
