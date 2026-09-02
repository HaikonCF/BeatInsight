using System.IO;

namespace BeatInsight.Services.Library;

/// <summary>
/// Stockage de la préférence de chemin Songs dans un fichier texte
/// local à la machine.
///
/// EMPLACEMENT
///
/// %LOCALAPPDATA%\BeatInsight\songs-path.txt, aux côtés de
/// beatinsight.db (voir BeatmapAnalysisRepository.DefaultDatabasePath)
/// pour rester cohérent avec le reste de la persistance locale.
///
/// Ce fichier ne doit jamais être versionné : il décrit une machine,
/// pas le projet. Il vit sous LOCALAPPDATA, entièrement hors du dépôt
/// Git, comme la base SQLite.
/// </summary>
internal sealed class FileSongsPathPreferenceStore
    : ISongsPathPreferenceStore
{
    private readonly string filePath;

    /// <summary>
    /// Emplacement par défaut du fichier de préférence, sous
    /// %LOCALAPPDATA%\BeatInsight.
    /// </summary>
    internal static string DefaultFilePath =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "BeatInsight",
            "songs-path.txt");

    /// <summary>
    /// Crée un store utilisant l'emplacement par défaut.
    /// </summary>
    internal FileSongsPathPreferenceStore()
        : this(DefaultFilePath)
    {
    }

    /// <summary>
    /// Crée un store utilisant un chemin de fichier explicite.
    ///
    /// Permet aux tests de pointer vers un emplacement temporaire
    /// dédié plutôt que vers la préférence réelle de l'utilisateur.
    /// </summary>
    internal FileSongsPathPreferenceStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        this.filePath = filePath;
    }

    public string? LoadManualPath()
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            string content = File.ReadAllText(filePath).Trim();

            return content.Length == 0 ? null : content;
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

    public void SaveManualPath(string path)
    {
        string? directory = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(filePath, path);
    }

    public void ClearManualPath()
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
