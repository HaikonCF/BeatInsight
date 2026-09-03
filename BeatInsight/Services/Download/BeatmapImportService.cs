using BeatInsight.Diagnostics;
using System.IO;

namespace BeatInsight.Services.Download;

/// <summary>
/// Remet un .osz au système (association de fichier Windows) puis attend
/// une confirmation d'import bornée dans le temps, sans jamais toucher à
/// la base interne d'osu! ni relancer un scan complet de la bibliothèque.
/// </summary>
internal sealed class BeatmapImportService
{
    private readonly IBeatmapImportShell shell;
    private readonly IBeatmapInstallationProbe installationProbe;

    internal BeatmapImportService(
        IBeatmapImportShell shell,
        IBeatmapInstallationProbe installationProbe)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(installationProbe);

        this.shell = shell;
        this.installationProbe = installationProbe;
    }

    /// <summary>
    /// Ouvre <paramref name="oszFilePath"/> via l'association de fichier
    /// par défaut. N'attend jamais que l'import soit instantané : voir
    /// <see cref="WaitForImportConfirmationAsync"/> pour la suite.
    /// </summary>
    internal BeatmapImportResult LaunchImport(string oszFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oszFilePath);

        if (!File.Exists(oszFilePath))
        {
            return new BeatmapImportResult(
                BeatmapImportOutcome.ImportLaunchFailed,
                "Downloaded file no longer exists.");
        }

        if (!shell.TryOpen(oszFilePath, out string? failureReason))
        {
            DebugLogger.Log(
                $"BEATMAP IMPORT LAUNCH FAILED | {failureReason}");

            return new BeatmapImportResult(
                BeatmapImportOutcome.ImportLaunchFailed,
                failureReason);
        }

        return new BeatmapImportResult(
            BeatmapImportOutcome.LaunchedWaitingForConfirmation);
    }

    /// <summary>
    /// Interroge <see cref="IBeatmapInstallationProbe"/> (jamais
    /// <c>BeatmapAnalysisRepository</c> : un import osu! réussi ne
    /// l'alimente pas) par intervalles bornés jusqu'à confirmation ou
    /// épuisement de <paramref name="maxAttempts"/>.
    /// <paramref name="delayBetweenAttempts"/> est injectable afin que les
    /// tests n'attendent jamais réellement.
    /// </summary>
    internal async Task<BeatmapImportResult> WaitForImportConfirmationAsync(
        int beatmapId,
        int? beatmapSetId,
        int maxAttempts,
        Func<CancellationToken, Task> delayBetweenAttempts,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        ArgumentNullException.ThrowIfNull(delayBetweenAttempts);

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new BeatmapImportResult(BeatmapImportOutcome.Cancelled);
            }

            if (installationProbe.IsInstalledLocally(
                    beatmapId,
                    beatmapSetId,
                    cancellationToken))
            {
                return new BeatmapImportResult(BeatmapImportOutcome.Confirmed);
            }

            if (attempt < maxAttempts - 1)
            {
                try
                {
                    await delayBetweenAttempts(cancellationToken);
                }
                catch (OperationCanceledException) when (
                    cancellationToken.IsCancellationRequested)
                {
                    return new BeatmapImportResult(BeatmapImportOutcome.Cancelled);
                }
            }
        }

        return new BeatmapImportResult(BeatmapImportOutcome.ImportNotConfirmed);
    }
}
