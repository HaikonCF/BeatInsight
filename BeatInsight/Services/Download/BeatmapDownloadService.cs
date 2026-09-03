using BeatInsight.Diagnostics;
using System.Diagnostics;
using System.IO;

namespace BeatInsight.Services.Download;

/// <summary>
/// Orchestre un provider de téléchargement : choix du fichier temporaire,
/// écriture en <c>.osz.part</c>, validation, puis renommage atomique en
/// <c>.osz</c>. Ne contient aucune logique réseau propre : celle-ci reste
/// entièrement dans <see cref="IBeatmapDownloadProvider"/>, réutilisant la
/// politique de requêtes osu! existante lorsqu'un provider en dépend.
/// </summary>
internal sealed class BeatmapDownloadService
{
    private readonly IBeatmapDownloadProvider provider;
    private readonly string downloadsDirectory;

    /// <summary>
    /// Emplacement par défaut, scoped à l'application, distinct du
    /// dossier Songs et de la base SQLite.
    /// </summary>
    internal static string DefaultDownloadsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BeatInsight",
        "Downloads");

    internal BeatmapDownloadService(
        IBeatmapDownloadProvider provider,
        string? downloadsDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(provider);

        this.provider = provider;
        this.downloadsDirectory = downloadsDirectory ?? DefaultDownloadsDirectory;
    }

    /// <summary>
    /// Chemin final déterministe (avant même de savoir si le téléchargement
    /// réussira) : un nom de fichier dérivé uniquement du BeatmapSetId,
    /// jamais du titre fourni par l'utilisateur/l'API.
    /// </summary>
    internal string GetFinalOszFilePath(int beatmapSetId) =>
        Path.Combine(downloadsDirectory, $"{beatmapSetId}.osz");

    private string GetPartFilePath(int beatmapSetId) =>
        Path.Combine(downloadsDirectory, $"{beatmapSetId}.osz.part");

    internal async Task<BeatmapDownloadResult> DownloadAsync(
        int beatmapSetId,
        CancellationToken cancellationToken = default)
    {
        if (beatmapSetId <= 0)
        {
            return BeatmapDownloadResult.Failure(
                BeatmapDownloadOutcome.Failed,
                TimeSpan.Zero,
                "Invalid beatmapset id.");
        }

        Stopwatch stopwatch = Stopwatch.StartNew();

        Directory.CreateDirectory(downloadsDirectory);

        string partFilePath = GetPartFilePath(beatmapSetId);
        string finalFilePath = GetFinalOszFilePath(beatmapSetId);

        BeatmapDownloadProviderResult providerResult;

        try
        {
            await using (FileStream partStream = new(
                partFilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                providerResult = await provider.DownloadAsync(
                    beatmapSetId,
                    partStream,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            DeletePartialFileQuietly(partFilePath);
            return BeatmapDownloadResult.Failure(
                BeatmapDownloadOutcome.Cancelled,
                stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            DeletePartialFileQuietly(partFilePath);

            DebugLogger.Log(
                $"BEATMAP DOWNLOAD ERROR | {provider.ProviderName} | "
                    + $"BeatmapSetId={beatmapSetId} | {ex.GetType().Name}");

            return BeatmapDownloadResult.Failure(
                BeatmapDownloadOutcome.Failed,
                stopwatch.Elapsed,
                "Unexpected error while downloading.");
        }

        switch (providerResult.Outcome)
        {
            case BeatmapDownloadProviderOutcome.BrowserFallbackOpened:
                DeletePartialFileQuietly(partFilePath);
                stopwatch.Stop();
                return BeatmapDownloadResult.BrowserFallback(stopwatch.Elapsed);

            case BeatmapDownloadProviderOutcome.RateLimited:
                DeletePartialFileQuietly(partFilePath);
                stopwatch.Stop();
                return BeatmapDownloadResult.Failure(
                    BeatmapDownloadOutcome.RateLimited,
                    stopwatch.Elapsed,
                    providerResult.FailureReason);

            case BeatmapDownloadProviderOutcome.AuthenticationRequired:
                DeletePartialFileQuietly(partFilePath);
                stopwatch.Stop();
                return BeatmapDownloadResult.Failure(
                    BeatmapDownloadOutcome.AuthenticationRequired,
                    stopwatch.Elapsed,
                    providerResult.FailureReason);

            case BeatmapDownloadProviderOutcome.ProviderUnavailable:
                DeletePartialFileQuietly(partFilePath);
                stopwatch.Stop();
                return BeatmapDownloadResult.Failure(
                    BeatmapDownloadOutcome.ProviderUnavailable,
                    stopwatch.Elapsed,
                    providerResult.FailureReason);

            case BeatmapDownloadProviderOutcome.Cancelled:
                DeletePartialFileQuietly(partFilePath);
                stopwatch.Stop();
                return BeatmapDownloadResult.Failure(
                    BeatmapDownloadOutcome.Cancelled,
                    stopwatch.Elapsed);

            case BeatmapDownloadProviderOutcome.Failed:
                DeletePartialFileQuietly(partFilePath);
                stopwatch.Stop();
                return BeatmapDownloadResult.Failure(
                    BeatmapDownloadOutcome.Failed,
                    stopwatch.Elapsed,
                    providerResult.FailureReason);
        }

        // BytesWritten : le fichier .part existe, mais un HTTP 200 ne
        // garantit rien — on valide avant de le considérer utilisable.
        if (!BeatmapOszFileValidator.IsValidOsz(
                partFilePath,
                out string? rejectionReason))
        {
            DeletePartialFileQuietly(partFilePath);
            stopwatch.Stop();

            DebugLogger.Log(
                $"BEATMAP DOWNLOAD REJECTED | {provider.ProviderName} | "
                    + $"BeatmapSetId={beatmapSetId} | {rejectionReason}");

            return BeatmapDownloadResult.Failure(
                BeatmapDownloadOutcome.InvalidDownloadedFile,
                stopwatch.Elapsed,
                rejectionReason);
        }

        // Renommage atomique : le fichier final n'apparaît jamais dans un
        // état incomplet aux yeux d'un autre processus (ex. osu!).
        File.Move(partFilePath, finalFilePath, overwrite: true);
        stopwatch.Stop();

        DebugLogger.Log(
            $"BEATMAP DOWNLOAD OK | {provider.ProviderName} | "
                + $"BeatmapSetId={beatmapSetId} | "
                + $"Bytes={providerResult.BytesWritten} | "
                + $"Elapsed={stopwatch.Elapsed.TotalSeconds:F1}s");

        return BeatmapDownloadResult.Success(
            finalFilePath,
            providerResult.BytesWritten,
            stopwatch.Elapsed);
    }

    private static void DeletePartialFileQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Un nettoyage best-effort : un fichier verrouillé
            // temporairement ne doit jamais faire échouer l'appelant.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
