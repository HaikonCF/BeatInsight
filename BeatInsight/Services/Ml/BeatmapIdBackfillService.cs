using BeatInsight.Models.Persistence;
using BeatInsight.Parser;
using BeatInsight.Services.Persistence;
using System.Diagnostics;
using System.IO;

namespace BeatInsight.Services.Ml;

/// <summary>
/// Progression d'un backfill de BeatmapId en cours.
/// </summary>
internal sealed record BeatmapIdBackfillProgress
{
    internal int TotalCandidates { get; init; }
    internal int ProcessedCandidates { get; init; }
    internal int UpdatedCount { get; init; }
    internal int MissingOrInvalidCount { get; init; }
    internal int FailedCount { get; init; }
    internal string? CurrentFile { get; init; }
    internal double Percent { get; init; }
}

/// <summary>
/// Résultat final d'un backfill de BeatmapId.
/// </summary>
internal sealed record BeatmapIdBackfillResult
{
    internal int TotalSamples { get; init; }
    internal int AlreadyPopulatedCount { get; init; }
    internal int CandidateCount { get; init; }
    internal int UpdatedCount { get; init; }
    internal int MissingOrInvalidCount { get; init; }
    internal int FailedCount { get; init; }
    internal bool WasCancelled { get; init; }
    internal TimeSpan Elapsed { get; init; }
}

/// <summary>
/// Peuple <see cref="MlDatasetSample.BeatmapId"/> pour les échantillons
/// existants dont l'ID est encore NULL, en relisant uniquement l'en-tête
/// [Metadata] de leur fichier .osu source via
/// <see cref="BeatmapMetadataReader"/>.
///
/// Ce service ne relance jamais GameplayAnalyzer, OsuStarRatingCalculator
/// ni MlFeatureExtractor : il ne touche que la colonne BeatmapId d'une
/// ligne déjà présente, via
/// <see cref="MlDatasetSampleRepository.UpdateBeatmapId"/>. Aucun sample
/// n'est créé, aucun label humain ni Community Evidence n'est modifié.
/// </summary>
internal sealed class BeatmapIdBackfillService
{
    private readonly MlDatasetSampleRepository repository;

    internal BeatmapIdBackfillService(MlDatasetSampleRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        this.repository = repository;
    }

    /// <summary>
    /// Parcourt séquentiellement les échantillons à BeatmapId NULL. Un
    /// fichier source manquant/illisible ou un BeatmapID absent/invalide
    /// n'interrompt jamais le lot : ils sont comptés séparément et le
    /// backfill continue.
    /// </summary>
    internal BeatmapIdBackfillResult Run(
        IProgress<BeatmapIdBackfillProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        repository.EnsureSchema();

        int totalSamples = repository.GetStatistics().SampleCount;

        IReadOnlyList<MlDatasetSample> candidates =
            repository.FindSamplesMissingBeatmapId();

        int updated = 0;
        int missingOrInvalid = 0;
        int failed = 0;
        int processed = 0;
        bool wasCancelled = false;

        foreach (MlDatasetSample candidate in candidates)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                wasCancelled = true;
                break;
            }

            ReportProgress(
                progress,
                candidates.Count,
                processed,
                updated,
                missingOrInvalid,
                failed,
                currentFile: candidate.SourceFilePath);

            try
            {
                if (!File.Exists(candidate.SourceFilePath))
                {
                    // Fichier déplacé/supprimé depuis la capture : ce n'est
                    // pas une erreur de lecture de métadonnées, mais on ne
                    // peut rien backfiller sans le fichier.
                    failed++;
                }
                else
                {
                    int? beatmapId = BeatmapMetadataReader.ReadBeatmapId(
                        candidate.SourceFilePath);

                    if (beatmapId is int id)
                    {
                        repository.UpdateBeatmapId(
                            candidate.SourceFilePath,
                            id);
                        updated++;
                    }
                    else
                    {
                        // BeatmapID absent, malformé, ou <= 0 (difficulté
                        // jamais soumise) : on laisse BeatmapId à NULL.
                        missingOrInvalid++;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
            }

            processed++;
        }

        stopwatch.Stop();

        ReportProgress(
            progress,
            candidates.Count,
            processed,
            updated,
            missingOrInvalid,
            failed,
            currentFile: null);

        return new BeatmapIdBackfillResult
        {
            TotalSamples = totalSamples,
            AlreadyPopulatedCount = totalSamples - candidates.Count,
            CandidateCount = candidates.Count,
            UpdatedCount = updated,
            MissingOrInvalidCount = missingOrInvalid,
            FailedCount = failed,
            WasCancelled = wasCancelled,
            Elapsed = stopwatch.Elapsed,
        };
    }

    private static void ReportProgress(
        IProgress<BeatmapIdBackfillProgress>? progress,
        int totalCandidates,
        int processedCandidates,
        int updatedCount,
        int missingOrInvalidCount,
        int failedCount,
        string? currentFile)
    {
        progress?.Report(new BeatmapIdBackfillProgress
        {
            TotalCandidates = totalCandidates,
            ProcessedCandidates = processedCandidates,
            UpdatedCount = updatedCount,
            MissingOrInvalidCount = missingOrInvalidCount,
            FailedCount = failedCount,
            CurrentFile = currentFile,
            Percent = totalCandidates == 0
                ? 0.0
                : processedCandidates * 100.0 / totalCandidates,
        });
    }
}
