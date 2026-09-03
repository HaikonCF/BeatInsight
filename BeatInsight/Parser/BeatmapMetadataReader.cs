using System.Globalization;
using System.IO;

namespace BeatInsight.Parser;

/// <summary>
/// Lecture minimale et volontairement séparée de <see cref="BeatmapParser"/> :
/// elle ne lit que l'en-tête d'un fichier .osu jusqu'à la fin de la section
/// [Metadata] et s'arrête toujours avant [TimingPoints]/[HitObjects]. Aucune
/// analyse de gameplay, aucun calcul de Star Rating, aucun HitObject.
///
/// Utilisée pour peupler <see cref="Models.Persistence.MlDatasetSample.BeatmapId"/>
/// à un coût quasi nul, sans dépendre de tosu ni d'un appel API.
/// </summary>
internal static class BeatmapMetadataReader
{
    private const string MetadataSectionName = "Metadata";
    private const string BeatmapIdPrefix = "BeatmapID:";

    /// <summary>
    /// Retourne l'identifiant osu! de la difficulté (champ <c>BeatmapID:</c>
    /// de la section [Metadata]), ou null si le fichier est absent/illisible,
    /// si la section [Metadata] n'a pas de ligne BeatmapID, si la valeur est
    /// malformée, ou si elle vaut zéro ou moins (convention osu! pour une
    /// difficulté jamais soumise).
    /// </summary>
    internal static int? ReadBeatmapId(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        try
        {
            using StreamReader reader = new(filePath);

            string? currentSection = null;
            bool visitedMetadataSection = false;
            string? line;

            while ((line = reader.ReadLine()) is not null)
            {
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    // Dès qu'on quitte [Metadata] après l'avoir visitée, la
                    // suite du fichier (Difficulty, Events, TimingPoints,
                    // HitObjects...) ne peut plus contenir BeatmapID : on
                    // arrête la lecture avant les sections coûteuses.
                    if (visitedMetadataSection)
                    {
                        return null;
                    }

                    currentSection = line.Trim('[', ']');
                    continue;
                }

                if (currentSection != MetadataSectionName)
                {
                    continue;
                }

                visitedMetadataSection = true;

                if (!line.StartsWith(BeatmapIdPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                string rawValue = line[BeatmapIdPrefix.Length..].Trim();

                if (!int.TryParse(
                        rawValue,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int beatmapId))
                {
                    return null;
                }

                return beatmapId > 0 ? beatmapId : null;
            }

            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
