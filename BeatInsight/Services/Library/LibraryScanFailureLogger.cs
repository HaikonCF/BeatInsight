using System.Globalization;
using System.IO;
using System.Text;

namespace BeatInsight.Services.Library;

/// <summary>
/// Journal local dédié aux fichiers .osu qui échouent pendant un scan
/// de bibliothèque. Ce journal est diagnostique uniquement : une
/// erreur d'écriture ne peut jamais modifier le résultat du scan.
/// </summary>
internal sealed class LibraryScanFailureLogger
    : ILibraryScanFailureLogger
{
    private readonly string logFilePath;
    private readonly object writeLock = new();

    /// <summary>
    /// Journal de production, hors du dépôt et du dossier Songs :
    /// %LOCALAPPDATA%\BeatInsight\Logs\library-scan-failures.log.
    /// </summary>
    internal static string DefaultLogFilePath =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "BeatInsight",
            "Logs",
            "library-scan-failures.log");

    internal LibraryScanFailureLogger()
        : this(DefaultLogFilePath)
    {
    }

    /// <summary>
    /// Constructeur prévu pour les tests : le journal peut être
    /// redirigé vers un chemin temporaire dédié.
    /// </summary>
    internal LibraryScanFailureLogger(string logFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logFilePath);

        this.logFilePath = logFilePath;
    }

    public void LogFailure(string filePath, Exception exception)
    {
        try
        {
            string? directory = Path.GetDirectoryName(logFilePath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string exceptionType = exception.GetType().FullName
                ?? exception.GetType().Name;

            string entry =
                $"[{DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)}]" +
                Environment.NewLine +
                $"File: {filePath}" + Environment.NewLine +
                $"Exception: {exceptionType}" + Environment.NewLine +
                $"Message: {exception.Message}" + Environment.NewLine +
                Environment.NewLine;

            lock (writeLock)
            {
                File.AppendAllText(logFilePath, entry, Encoding.UTF8);
            }
        }
        catch
        {
            // Ce journal est purement diagnostique. Tout échec de
            // son écriture doit rester invisible pour le scan.
        }
    }
}
