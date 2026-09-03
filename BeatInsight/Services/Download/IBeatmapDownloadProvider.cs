using System.IO;

namespace BeatInsight.Services.Download;

/// <summary>
/// Source capable d'obtenir les octets d'un beatmapset (.osz). Aucune
/// logique UI ni de fichier temporaire ici : un provider écrit dans le
/// flux fourni par <see cref="BeatmapDownloadService"/>, ou délègue au
/// navigateur lorsqu'aucun octet ne peut être obtenu en toute sécurité
/// avec l'authentification disponible.
///
/// Ne doit jamais journaliser ni exposer un jeton, cookie, secret client,
/// ou URL de téléchargement authentifiée.
/// </summary>
internal interface IBeatmapDownloadProvider
{
    /// <summary>Nom court utilisé uniquement en diagnostic (jamais de secret).</summary>
    string ProviderName { get; }

    /// <summary>
    /// Tente d'écrire le contenu du beatmapset dans
    /// <paramref name="destination"/>. Si le provider ne peut pas
    /// télécharger d'octets en toute sécurité, il peut ouvrir une page
    /// de repli (navigateur) lui-même et retourner
    /// <see cref="BeatmapDownloadProviderOutcome.BrowserFallbackOpened"/>
    /// sans avoir écrit quoi que ce soit.
    /// </summary>
    Task<BeatmapDownloadProviderResult> DownloadAsync(
        int beatmapSetId,
        Stream destination,
        CancellationToken cancellationToken);
}
