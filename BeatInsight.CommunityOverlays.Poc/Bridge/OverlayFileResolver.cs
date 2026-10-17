using System.IO;

namespace BeatInsight.CommunityOverlays.Poc.Bridge;

public enum OverlayFileStatus
{
    Found,
    NotFound,
    Rejected,
}

public sealed record OverlayFileResolution(OverlayFileStatus Status, string? FullPath, string Reason);

/// <summary>
/// Résout un chemin d'URL d'overlay vers un fichier de son dossier, en traitant
/// l'overlay comme du code tiers non fiable : aucun accès hors du dossier, ni par
/// <c>..</c>, ni par chemin absolu, ni par flux NTFS, nom de périphérique ou lien
/// (symbolique ou jonction).
/// </summary>
public static class OverlayFileResolver
{
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <param name="overlayRoot">Dossier de l'overlay (chemin absolu).</param>
    /// <param name="urlPath">Chemin d'URL encore encodé, ex. <c>/js/socket.js</c>.</param>
    public static OverlayFileResolution Resolve(string overlayRoot, string urlPath)
    {
        // Un seul décodage, comme un serveur HTTP : "%252e" reste "%2e" (nom littéral).
        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(urlPath);
        }
        catch (UriFormatException)
        {
            return Reject("encodage invalide");
        }

        return ResolveRelative(overlayRoot, decoded, defaultDocument: "index.html");
    }

    /// <summary>
    /// Même contrôle pour un chemin déjà décodé (ex. nom de fichier issu d'un
    /// instantané). Sans <paramref name="defaultDocument"/>, un dossier est refusé.
    /// </summary>
    public static OverlayFileResolution ResolveRelative(string allowedRoot, string decodedPath, string? defaultDocument = null)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(allowedRoot)) + Path.DirectorySeparatorChar;

        if (decodedPath.Contains('\0'))
            return Reject("caractère nul");

        string relative = decodedPath.Replace('\\', '/').TrimStart('/');
        if (relative.Length == 0 || relative.EndsWith('/'))
        {
            if (defaultDocument is null)
                return Reject("dossier");
            relative += defaultDocument;
        }

        // ":" : lettre de lecteur ("C:/...") ou flux alternatif NTFS ("fichier:flux").
        if (relative.Contains(':'))
            return Reject("chemin absolu ou flux NTFS");

        string[] segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (string segment in segments)
        {
            if (segment == "..")
                return Reject("remontée de dossier");

            // Windows ignore les points et espaces finaux ("notes.txt." = "notes.txt") :
            // refusé pour que l'extension contrôlée soit bien celle du fichier lu.
            if (segment != "." && (segment.EndsWith('.') || segment.EndsWith(' ')))
                return Reject("point ou espace final");

            string stem = segment.Split('.')[0].TrimEnd(' ');
            if (ReservedDeviceNames.Contains(stem))
                return Reject("nom de périphérique Windows");
        }

        string fullPath = Path.GetFullPath(Path.Combine(root, string.Join(Path.DirectorySeparatorChar, segments)));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return Reject("hors du dossier de l'overlay");

        if (ContainsReparsePoint(root, fullPath))
            return Reject("lien symbolique ou jonction");

        return File.Exists(fullPath)
            ? new OverlayFileResolution(OverlayFileStatus.Found, fullPath, "ok")
            : new OverlayFileResolution(OverlayFileStatus.NotFound, null, "fichier absent");
    }

    /// <summary>
    /// Vrai si un composant situé sous la racine (dossier ou fichier) est un point
    /// d'analyse (lien symbolique, jonction) : sa cible peut être hors du dossier.
    /// La racine elle-même est choisie par l'utilisateur et n'est pas contrôlée.
    /// </summary>
    private static bool ContainsReparsePoint(string root, string fullPath)
    {
        string current = Path.TrimEndingDirectorySeparator(root);
        foreach (string part in fullPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (IsReparsePoint(current))
                return true;
        }

        return false;
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.Exists(path) || Directory.Exists(path))
                && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true; // en cas de doute, refuser
        }
    }

    private static OverlayFileResolution Reject(string reason) =>
        new(OverlayFileStatus.Rejected, null, reason);
}
