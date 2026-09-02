using BeatInsight.Analysis;
using BeatInsight.Models;
using BeatInsight.Models.Ml;
using BeatInsight.Models.Persistence;
using BeatInsight.Parser;
using BeatInsight.Services.Library;
using BeatInsight.Services.Persistence;
using System.Diagnostics;
using System.IO;

namespace BeatInsight.Services.Ml;

/// <summary>
/// Construit incrémentalement le dataset ML à partir des fichiers .osu
/// standard d'une bibliothèque.
///
/// Le builder est volontairement distinct de BeatmapAnalysisCacheService :
/// un snapshot de présentation cache ne contient ni HitObjects, ni séquences,
/// ni sections. Toute capture absente ou périmée effectue donc une analyse
/// fraîche avec BeatmapParser.Load avant MlFeatureExtractor.
/// </summary>
internal sealed class MlDatasetBuilder
{
    private const string OsuFileExtension = ".osu";

    private readonly MlDatasetSampleRepository repository;

    internal MlDatasetBuilder(MlDatasetSampleRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        this.repository = repository;
    }

    /// <summary>
    /// Parcourt séquentiellement <paramref name="rootFolder"/>. Le jeton
    /// d'annulation est vérifié entre deux fichiers : une capture déjà
    /// terminée reste durablement disponible dans le dataset.
    /// </summary>
    internal MlDatasetBuildResult Build(
        string rootFolder,
        IProgress<MlDatasetBuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootFolder);

        Stopwatch stopwatch = Stopwatch.StartNew();
        string[] files = EnumerateOsuFiles(rootFolder);

        repository.EnsureSchema();

        int totalFiles = files.Length;
        int processedFiles = 0;
        int capturedFiles = 0;
        int datasetUpToDateFiles = 0;
        int unsupportedFiles = 0;
        int failedFiles = 0;
        bool wasCancelled = false;

        foreach (string filePath in files)
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
                capturedFiles,
                datasetUpToDateFiles,
                unsupportedFiles,
                failedFiles,
                currentFile: filePath);

            try
            {
                // Le filtre de mode intervient avant le lookup et surtout
                // avant le parsing / l'analyse. Un ancien sample éventuel est
                // retiré : le dataset ne doit contenir aucun mode non standard.
                if (!BeatmapGameModeReader.IsSupportedForAnalysis(filePath))
                {
                    repository.Delete(filePath);
                    unsupportedFiles++;
                }
                else
                {
                    FileInfo fileInfo = new(filePath);
                    MlDatasetSample? existing =
                        repository.FindBySourceFilePath(filePath);

                    if (existing is not null
                        && IsCurrent(existing, filePath, fileInfo))
                    {
                        datasetUpToDateFiles++;
                    }
                    else
                    {
                        // Ne jamais utiliser BeatmapAnalysisRepository ici :
                        // un snapshot cache ne possède pas les collections de
                        // sections et séquences requises par les features ML.
                        Beatmap beatmap = BeatmapParser.Load(filePath);

                        MlDatasetSample freshSample =
                            MlFeatureExtractor.CreateSample(
                                beatmap,
                                new MlDatasetCaptureContext(
                                    SourceFilePath: filePath,
                                    FileSize: fileInfo.Length,
                                    FileLastWriteUtc:
                                        fileInfo.LastWriteTimeUtc,
                                    CapturedAtUtc: DateTime.UtcNow,
                                    BeatmapId: existing?.BeatmapId,
                                    Md5: existing?.Md5));

                        repository.Upsert(
                            PreserveAnnotationMetadata(
                                freshSample,
                                existing));
                        capturedFiles++;
                    }
                }
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException)
            {
                // Une beatmap invalide ou inaccessible n'empêche pas le
                // backfill du reste de la bibliothèque.
                failedFiles++;
            }

            processedFiles++;
        }

        stopwatch.Stop();

        ReportProgress(
            progress,
            totalFiles,
            processedFiles,
            capturedFiles,
            datasetUpToDateFiles,
            unsupportedFiles,
            failedFiles,
            currentFile: null);

        return new MlDatasetBuildResult
        {
            TotalFiles = totalFiles,
            ProcessedFiles = processedFiles,
            CapturedFiles = capturedFiles,
            DatasetUpToDateFiles = datasetUpToDateFiles,
            UnsupportedFiles = unsupportedFiles,
            FailedFiles = failedFiles,
            WasCancelled = wasCancelled,
            Elapsed = stopwatch.Elapsed,
        };
    }

    private static bool IsCurrent(
        MlDatasetSample sample,
        string sourceFilePath,
        FileInfo fileInfo)
    {
        return string.Equals(
                   sample.SourceFilePath,
                   sourceFilePath,
                   StringComparison.OrdinalIgnoreCase)
               && sample.FileSize == fileInfo.Length
               && sample.FileLastWriteUtc == fileInfo.LastWriteTimeUtc
               && sample.FeatureSchemaVersion
                      == MlFeatureSchemaVersion.Current
               && sample.AnalyzerVersion == AnalyzerVersion.Current;
    }

    /// <summary>
    /// Actualise seulement la représentation des features. Les annotations
    /// humaines et la preuve communautaire sont des métadonnées indépendantes
    /// qui doivent survivre à toute réanalyse locale.
    /// </summary>
    private static MlDatasetSample PreserveAnnotationMetadata(
        MlDatasetSample freshSample,
        MlDatasetSample? existingSample)
    {
        if (existingSample is null)
        {
            return freshSample;
        }

        return new MlDatasetSample
        {
            SourceFilePath = freshSample.SourceFilePath,
            BeatmapId = freshSample.BeatmapId,
            Md5 = freshSample.Md5,
            FileSize = freshSample.FileSize,
            FileLastWriteUtc = freshSample.FileLastWriteUtc,
            FeatureSchemaVersion = freshSample.FeatureSchemaVersion,
            AnalyzerVersion = freshSample.AnalyzerVersion,
            CapturedAtUtc = freshSample.CapturedAtUtc,
            RawFeaturesJson = freshSample.RawFeaturesJson,
            SectionFeaturesJson = freshSample.SectionFeaturesJson,
            HumanLabel = existingSample.HumanLabel,
            HumanValidated = existingSample.HumanValidated,
            CommunityEvidenceJson = existingSample.CommunityEvidenceJson,
            CommunityCapturedAtUtc =
                existingSample.CommunityCapturedAtUtc,
        };
    }

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
            .Where(path => Path.GetExtension(path).Equals(
                OsuFileExtension,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void ReportProgress(
        IProgress<MlDatasetBuildProgress>? progress,
        int totalFiles,
        int processedFiles,
        int capturedFiles,
        int datasetUpToDateFiles,
        int unsupportedFiles,
        int failedFiles,
        string? currentFile)
    {
        progress?.Report(new MlDatasetBuildProgress
        {
            TotalFiles = totalFiles,
            ProcessedFiles = processedFiles,
            CapturedFiles = capturedFiles,
            DatasetUpToDateFiles = datasetUpToDateFiles,
            UnsupportedFiles = unsupportedFiles,
            FailedFiles = failedFiles,
            CurrentFile = currentFile,
            Percent = totalFiles == 0
                ? 0.0
                : processedFiles * 100.0 / totalFiles,
        });
    }
}
