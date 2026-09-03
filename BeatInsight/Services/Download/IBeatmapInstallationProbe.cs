using BeatInsight.Parser;
using System.Globalization;
using System.IO;

namespace BeatInsight.Services.Download;

/// <summary>
/// Répond uniquement à "ce beatmap est-il présent dans le dossier Songs
/// configuré ?" — indépendamment de <c>BeatmapAnalysisRepository</c>.
///
/// AUDIT (V2.4.3a) : un import osu! réussi place les fichiers dans Songs
/// immédiatement, mais n'écrit rien dans l'index d'analyse persistant de
/// BeatInsight (celui-ci n'est peuplé que par tosu, une revue Discovery,
/// ou un Scan Library explicite). Confirmer un import via
/// <c>BeatmapAnalysisRepository</c> produisait donc un faux négatif
/// permanent ("Waiting for osu! import..." qui ne se résout jamais).
/// Cette interface remplace cet usage détourné par une vérification
/// directement sur le système de fichiers.
/// </summary>
internal interface IBeatmapInstallationProbe
{
    bool IsInstalledLocally(
        int beatmapId,
        int? beatmapSetId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Vérifie la présence locale sans jamais parcourir Songs entièrement ni
/// invoquer GameplayAnalyzer :
///
/// - avec un BeatmapSetId, une seule énumération NON récursive des
///   sous-dossiers de Songs suffit (osu! nomme un dossier de set
///   "{BeatmapSetId} Artist - Title" ; un .osz importé crée toujours ce
///   dossier, quel que soit le nombre de difficultés qu'il contient) ;
/// - sans BeatmapSetId, une énumération récursive des .osu s'arrête dès
///   la première correspondance de métadonnée (voir
///   <see cref="BeatmapMetadataReader"/>, qui lit uniquement l'en-tête
///   [Metadata] et jamais les HitObjects) — un repli plus coûteux,
///   volontairement réservé au cas où le SetId est indisponible.
/// </summary>
internal sealed class SongsFolderBeatmapInstallationProbe : IBeatmapInstallationProbe
{
    private readonly Func<string?> resolveSongsFolder;

    internal SongsFolderBeatmapInstallationProbe(Func<string?> resolveSongsFolder)
    {
        ArgumentNullException.ThrowIfNull(resolveSongsFolder);

        this.resolveSongsFolder = resolveSongsFolder;
    }

    public bool IsInstalledLocally(
        int beatmapId,
        int? beatmapSetId,
        CancellationToken cancellationToken)
    {
        string? songsFolder = resolveSongsFolder();

        if (string.IsNullOrWhiteSpace(songsFolder) || !Directory.Exists(songsFolder))
        {
            return false;
        }

        try
        {
            if (beatmapSetId is int setId
                && setId > 0
                && HasMatchingSetFolder(songsFolder, setId, cancellationToken))
            {
                return true;
            }

            if (beatmapId > 0
                && HasMatchingBeatmapIdFile(songsFolder, beatmapId, cancellationToken))
            {
                return true;
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }

    private static bool HasMatchingSetFolder(
        string songsFolder,
        int beatmapSetId,
        CancellationToken cancellationToken)
    {
        string prefix = beatmapSetId.ToString(CultureInfo.InvariantCulture);

        foreach (string folder in Directory.EnumerateDirectories(songsFolder))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string name = Path.GetFileName(folder);

            // "{setId} rest..." mais jamais "{setId}0..." (préfixe
            // numérique plus long coïncidant par accident).
            if (name.Length >= prefix.Length
                && name.StartsWith(prefix, StringComparison.Ordinal)
                && (name.Length == prefix.Length
                    || !char.IsDigit(name[prefix.Length])))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMatchingBeatmapIdFile(
        string songsFolder,
        int beatmapId,
        CancellationToken cancellationToken)
    {
        foreach (string osuFilePath in Directory.EnumerateFiles(
            songsFolder,
            "*.osu",
            SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (BeatmapMetadataReader.ReadBeatmapId(osuFilePath) == beatmapId)
            {
                return true;
            }
        }

        return false;
    }
}
