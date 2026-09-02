using System.Globalization;
using System.IO;

namespace BeatInsight.Services.Library;

/// <summary>
/// Lit le mode de jeu déclaré dans l'en-tête d'un fichier .osu sans
/// déclencher le parsing complet d'une beatmap.
///
/// BeatInsight V2.2 analyse exclusivement osu!standard (Mode: 0).
/// Un mode absent conserve le comportement historique : osu!standard
/// est la valeur par défaut des fichiers .osu legacy.
/// </summary>
internal static class BeatmapGameModeReader
{
    internal const int OsuStandardMode = 0;

    internal static bool IsSupportedForAnalysis(string filePath)
    {
        return ReadMode(filePath) == OsuStandardMode;
    }

    internal static int ReadMode(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        bool inGeneralSection = false;

        foreach (string rawLine in File.ReadLines(filePath))
        {
            string line = rawLine.Trim();

            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                if (inGeneralSection)
                {
                    break;
                }

                inGeneralSection = string.Equals(
                    line,
                    "[General]",
                    StringComparison.OrdinalIgnoreCase);

                continue;
            }

            if (!inGeneralSection
                || !line.StartsWith(
                    "Mode:",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value = line["Mode:".Length..].Trim();

            return int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int mode)
                ? mode
                : OsuStandardMode;
        }

        return OsuStandardMode;
    }
}
