using System.IO;
using System.Text.RegularExpressions;
using BeatInsight.CommunityOverlays.Poc.Adapter;

namespace BeatInsight.CommunityOverlays.Poc.Bridge;

public enum BeatmapAssetStatus
{
    /// <summary>Fichier autorisé, prêt à être servi.</summary>
    Found,
    /// <summary>Rien à servir (fournisseur désactivé, pas de beatmap, fichier absent) : 404.</summary>
    NotFound,
    /// <summary>Requête refusée par le confinement : 403.</summary>
    Rejected,
}

public sealed record BeatmapAssetResult(BeatmapAssetStatus Status, string? FullPath, string ContentType, string Reason);

/// <summary>
/// Fournisseur en lecture seule des ressources <c>/files/beatmap/*</c> de tosu,
/// indépendant des readers natifs BeatInsight. Il ne sert que des fichiers :
/// <list type="bullet">
/// <item>situés sous une racine Songs explicitement autorisée ;</item>
/// <item>dans le dossier de la beatmap courante uniquement (tosu autorise tout Songs) ;</item>
/// <item>avec une extension de ressource de beatmap (image, audio, vidéo, .osu) ;</item>
/// <item>contrôlés par <see cref="OverlayFileResolver"/> (remontée, absolu, flux NTFS,
/// périphériques, liens symboliques et jonctions).</item>
/// </list>
/// Sans racine autorisée, il est désactivé et répond toujours 404.
/// </summary>
public sealed class BeatmapAssetProvider
{
    public const string RoutePrefix = "/files/beatmap/";

    // tosu (router/v2.ts) : regex non ancrée, testée sur le pathname seul (query ignorée).
    private static readonly Regex ShortcutRoute = new(@"/files/beatmap/(?<type>background|audio|file)", RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png",
        [".gif"] = "image/gif", [".webp"] = "image/webp", [".bmp"] = "image/bmp",
    };

    private static readonly Dictionary<string, string> AudioTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"] = "audio/mpeg", [".ogg"] = "audio/ogg", [".wav"] = "audio/wav",
    };

    private static readonly Dictionary<string, string> VideoTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = "video/mp4", [".webm"] = "video/webm", [".avi"] = "video/x-msvideo", [".flv"] = "video/x-flv",
    };

    private static readonly Dictionary<string, string> BeatmapFileTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".osu"] = "text/plain; charset=utf-8",
    };

    private readonly Func<string?> _songsRootSource;

    /// <param name="songsRoot">Racine Songs autorisée (chemin absolu), ou null pour désactiver.</param>
    public BeatmapAssetProvider(string? songsRoot)
    {
        string? root = Normalize(songsRoot);
        _songsRootSource = () => root;
    }

    /// <summary>
    /// Racine relue à chaque requête (V3.14-COM-10) : en live, c'est le dossier Songs annoncé
    /// par BeatInsight pour la beatmap courante. Mêmes contrôles qu'une racine fixe.
    /// </summary>
    public BeatmapAssetProvider(Func<string?> songsRoot)
    {
        ArgumentNullException.ThrowIfNull(songsRoot);
        _songsRootSource = () => Normalize(songsRoot());
    }

    private static string? Normalize(string? root) =>
        string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root) ? null : Path.GetFullPath(root);

    public bool IsEnabled => _songsRootSource() is not null;

    public static bool IsBeatmapRoute(string urlPath) =>
        urlPath.StartsWith(RoutePrefix, StringComparison.Ordinal);

    /// <param name="urlPath">Pathname encore encodé (sans query), ex. <c>/files/beatmap/background</c>.</param>
    /// <param name="current">Fichiers de la beatmap courante, ou null si inconnus.</param>
    public BeatmapAssetResult Resolve(string urlPath, CommunityOverlayBeatmapFiles? current)
    {
        string? _songsRoot = _songsRootSource();
        if (_songsRoot is null)
            return NotFound("fournisseur désactivé (aucune racine autorisée)");
        if (current is null || string.IsNullOrEmpty(current.Folder))
            return NotFound("aucune beatmap courante");

        // Le dossier vient de l'instantané : il est contrôlé comme une entrée non fiable.
        var folder = OverlayFileResolver.ResolveRelative(_songsRoot, current.Folder + "/" + "_");
        if (folder.Status == OverlayFileStatus.Rejected)
            return Rejected($"dossier de beatmap refusé ({folder.Reason})");
        string beatmapFolder = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(_songsRoot, current.Folder, "_")))!;

        var shortcut = ShortcutRoute.Match(urlPath);
        if (shortcut.Success)
        {
            return shortcut.Groups["type"].Value switch
            {
                "background" => ResolveIn(_songsRoot, beatmapFolder, current.Background, ImageTypes),
                "audio" => ResolveIn(_songsRoot, beatmapFolder, current.Audio, AudioTypes),
                _ => ResolveIn(_songsRoot, beatmapFolder, current.OsuFile, BeatmapFileTypes),
            };
        }

        if (!IsBeatmapRoute(urlPath))
            return NotFound("route inconnue");

        // Chemin générique /files/beatmap/<dossier>/<fichier> (ex. directPath.beatmapBackground).
        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(urlPath[RoutePrefix.Length..]);
        }
        catch (UriFormatException)
        {
            return Rejected("encodage invalide");
        }

        var resolution = OverlayFileResolver.ResolveRelative(_songsRoot, decoded);
        if (resolution.Status == OverlayFileStatus.Rejected)
            return Rejected(resolution.Reason);

        string candidate = Path.GetFullPath(Path.Combine(_songsRoot, decoded.Replace('\\', '/').TrimStart('/')));
        string folderPrefix = Path.TrimEndingDirectorySeparator(beatmapFolder) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
            return Rejected("hors du dossier de la beatmap courante");

        return Finish(resolution, AllTypes());
    }

    private static BeatmapAssetResult ResolveIn(string songsRoot, string beatmapFolder, string? fileName, Dictionary<string, string> allowedTypes)
    {
        if (string.IsNullOrEmpty(fileName))
            return NotFound("fichier non renseigné pour la beatmap courante");

        // Nom de fichier relatif au dossier de la beatmap, contrôlé depuis la racine Songs.
        string relativeToSongs = Path.GetRelativePath(songsRoot, beatmapFolder).Replace('\\', '/') + "/" + fileName;
        var resolution = OverlayFileResolver.ResolveRelative(songsRoot, relativeToSongs);
        if (resolution.Status == OverlayFileStatus.Rejected)
            return Rejected(resolution.Reason);

        string folderPrefix = Path.TrimEndingDirectorySeparator(beatmapFolder) + Path.DirectorySeparatorChar;
        if (resolution.FullPath is not null && !resolution.FullPath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
            return Rejected("hors du dossier de la beatmap courante");

        return Finish(resolution, allowedTypes);
    }

    private static BeatmapAssetResult Finish(OverlayFileResolution resolution, IReadOnlyDictionary<string, string> allowedTypes)
    {
        if (resolution.Status == OverlayFileStatus.NotFound || resolution.FullPath is null)
            return NotFound(resolution.Reason);

        string extension = Path.GetExtension(resolution.FullPath);
        if (!allowedTypes.TryGetValue(extension, out string? contentType))
            return Rejected($"extension non autorisée ({extension})");

        return new BeatmapAssetResult(BeatmapAssetStatus.Found, resolution.FullPath, contentType, "ok");
    }

    private static Dictionary<string, string> AllTypes()
    {
        var all = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var types in new[] { ImageTypes, AudioTypes, VideoTypes, BeatmapFileTypes })
            foreach (var (extension, contentType) in types)
                all[extension] = contentType;
        return all;
    }

    private static BeatmapAssetResult NotFound(string reason) => new(BeatmapAssetStatus.NotFound, null, "", reason);

    private static BeatmapAssetResult Rejected(string reason) => new(BeatmapAssetStatus.Rejected, null, "", reason);
}
