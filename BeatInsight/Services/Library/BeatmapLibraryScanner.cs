using BeatInsight.Models.Library;
using BeatInsight.Services.Persistence;
using System.Diagnostics;
using System.IO;

namespace BeatInsight.Services.Library;

/// <summary>
/// Parcourt récursivement un dossier à la recherche de fichiers
/// .osu et s'assure que chacun est présent et à jour dans le cache
/// d'analyses.
///
/// RÔLE
///
/// Ce scanner ne fait aucune analyse lui-même : il délègue chaque
/// fichier à BeatmapAnalysisCacheService, qui décide seul si une
/// entrée est réutilisable. Le scanner se contente de compter les
/// résultats et de rapporter la progression.
///
/// SÉQUENTIEL, ANNULABLE, TOLÉRANT AUX ÉCHECS
///
/// Le traitement reste strictement séquentiel : aucun parallélisme.
/// L'échec d'un fichier (analyse impossible) est compté puis le scan
/// continue sur le fichier suivant. Une annulation demandée via
/// <see cref="CancellationToken"/> arrête le scan avant le prochain
/// fichier ; tout ce qui a déjà été persisté par le cache reste en
/// base, ce type ne fait aucune opération de nettoyage ni de retour
/// en arrière.
///
/// AUCUNE DÉPENDANCE UI, AUCUN APPEL RÉSEAU
///
/// Ce type ne référence ni WPF, ni l'API osu!, ni les tags
/// communautaires.
/// </summary>
internal sealed class BeatmapLibraryScanner
{
    private const string OsuFileExtension = ".osu";

    private readonly BeatmapAnalysisCacheService cacheService;
    private readonly ILibraryScanFailureLogger failureLogger;

    internal BeatmapLibraryScanner(
        BeatmapAnalysisCacheService cacheService,
        ILibraryScanFailureLogger? failureLogger = null)
    {
        ArgumentNullException.ThrowIfNull(cacheService);

        this.cacheService = cacheService;
        this.failureLogger = failureLogger
            ?? new LibraryScanFailureLogger();
    }

    /// <summary>
    /// Scanne récursivement <paramref name="rootFolder"/> et met à
    /// jour le cache pour chaque fichier .osu trouvé.
    /// </summary>
    /// <param name="rootFolder">Dossier racine à parcourir.</param>
    /// <param name="progress">
    /// Récepteur optionnel de progression, rapporté après le
    /// traitement de chaque fichier.
    /// </param>
    /// <param name="cancellationToken">
    /// Jeton d'annulation coopératif. Vérifié avant chaque fichier :
    /// le scan ne s'interrompt jamais au milieu du traitement d'un
    /// fichier.
    /// </param>
    internal LibraryScanResult Scan(
        string rootFolder,
        IProgress<LibraryScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootFolder);

        Stopwatch stopwatch = Stopwatch.StartNew();

        // Matérialisée avant tout traitement : TotalFiles doit être
        // connu dès le premier rapport de progression.
        string[] files = EnumerateOsuFiles(rootFolder);

        int totalFiles = files.Length;
        int processedFiles = 0;
        int analyzedFiles = 0;
        int skippedUpToDateFiles = 0;
        int skippedUnsupportedFiles = 0;
        int failedFiles = 0;
        bool wasCancelled = false;

        foreach (string file in files)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                wasCancelled = true;
                break;
            }

            ReportProgress(
                progress,
                totalFiles,
                processedFiles,
                analyzedFiles,
                skippedUpToDateFiles,
                skippedUnsupportedFiles,
                failedFiles,
                currentFile: file);

            try
            {
                // BeatInsight analyse exclusivement osu!standard.
                // La lecture de l'en-tête évite de créer une beatmap,
                // d'appeler le cache ou de lancer l'analyse pour les
                // modes non supportés.
                if (!BeatmapGameModeReader.IsSupportedForAnalysis(file))
                {
                    skippedUnsupportedFiles++;
                }
                else
                {
                    BeatmapAnalysisOutcome outcome =
                        cacheService.GetOrAnalyzeDetailed(file);

                    if (outcome.WasCacheHit)
                    {
                        skippedUpToDateFiles++;
                    }
                    else
                    {
                        analyzedFiles++;
                    }
                }
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException)
            {
                // Un fichier illisible ou invalide ne doit pas
                // interrompre le scan des autres maps.
                failedFiles++;
                LogFailureSafely(file, ex);
            }

            processedFiles++;
        }

        stopwatch.Stop();

        ReportProgress(
            progress,
            totalFiles,
            processedFiles,
            analyzedFiles,
            skippedUpToDateFiles,
            skippedUnsupportedFiles,
            failedFiles,
            currentFile: null);

        return new LibraryScanResult
        {
            TotalFiles = totalFiles,
            ProcessedFiles = processedFiles,
            AnalyzedFiles = analyzedFiles,
            SkippedUpToDateFiles = skippedUpToDateFiles,
            SkippedUnsupportedFiles = skippedUnsupportedFiles,
            FailedFiles = failedFiles,
            WasCancelled = wasCancelled,
            Elapsed = stopwatch.Elapsed,
        };
    }


    // ============================================================
    // ÉCHECS
    // ============================================================

    private void LogFailureSafely(string filePath, Exception exception)
    {
        try
        {
            failureLogger.LogFailure(filePath, exception);
        }
        catch
        {
            // Un logger alternatif ne doit pas pouvoir interrompre
            // le scan ni modifier ses compteurs.
        }
    }


    // ============================================================
    // ÉNUMÉRATION
    // ============================================================

    private static string[] EnumerateOsuFiles(string rootFolder)
    {
        if (!Directory.Exists(rootFolder))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(
                rootFolder,
                "*" + OsuFileExtension,
                SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path)
                .Equals(
                    OsuFileExtension,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }


    // ============================================================
    // PROGRESSION
    // ============================================================

    private static void ReportProgress(
        IProgress<LibraryScanProgress>? progress,
        int totalFiles,
        int processedFiles,
        int analyzedFiles,
        int skippedUpToDateFiles,
        int skippedUnsupportedFiles,
        int failedFiles,
        string? currentFile)
    {
        progress?.Report(new LibraryScanProgress
        {
            TotalFiles = totalFiles,
            ProcessedFiles = processedFiles,
            AnalyzedFiles = analyzedFiles,
            SkippedUpToDateFiles = skippedUpToDateFiles,
            SkippedUnsupportedFiles = skippedUnsupportedFiles,
            FailedFiles = failedFiles,
            CurrentFile = currentFile,
            Percent = totalFiles == 0
                ? 0.0
                : processedFiles * 100.0 / totalFiles,
        });
    }
}
