using BeatInsight.Diagnostics;
using System.IO;

namespace BeatInsight.Services.Download;

/// <summary>
/// Provider de repli lorsqu'aucun téléchargement programmatique sûr n'est
/// disponible.
///
/// AUDIT (V2.4.3) : BeatInsight authentifie ses appels osu! avec un
/// jeton OAuth <c>client_credentials</c> (app-only, scope <c>public</c>,
/// voir <see cref="BeatInsight.OsuApiService"/>). L'API v2 n'expose
/// aucun endpoint de téléchargement de beatmapset, et le point de
/// téléchargement du site osu! (<c>/beatmapsets/{id}/download</c>)
/// exige une session utilisateur connectée dans un navigateur — jamais
/// un jeton d'application. Il n'existe donc aucune route programmatique
/// sûre avec l'authentification actuelle : ce provider ouvre la page de
/// téléchargement dans le navigateur par défaut de l'utilisateur, qui
/// gère lui-même sa propre session/authentification. BeatInsight ne
/// touche, ne stocke et ne journalise jamais de cookie ou de jeton
/// utilisateur.
/// </summary>
internal sealed class BrowserOpenBeatmapDownloadProvider : IBeatmapDownloadProvider
{
    private readonly Action<string> openUrl;

    public string ProviderName => "osu! website (browser)";

    internal BrowserOpenBeatmapDownloadProvider(Action<string>? openUrl = null)
    {
        this.openUrl = openUrl ?? DefaultOpenUrl;
    }

    public Task<BeatmapDownloadProviderResult> DownloadAsync(
        int beatmapSetId,
        Stream destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (beatmapSetId <= 0)
        {
            return Task.FromResult(
                BeatmapDownloadProviderResult.Failure(
                    BeatmapDownloadProviderOutcome.Failed,
                    "Invalid beatmapset id."));
        }

        cancellationToken.ThrowIfCancellationRequested();

        string url = $"https://osu.ppy.sh/beatmapsets/{beatmapSetId}/download";

        try
        {
            openUrl(url);

            DebugLogger.Log(
                "BEATMAP DOWNLOAD | Browser fallback opened | "
                    + $"BeatmapSetId={beatmapSetId}");

            return Task.FromResult(BeatmapDownloadProviderResult.BrowserFallback);
        }
        catch (Exception ex)
        {
            // Le message d'exception d'un échec de lancement navigateur ne
            // contient jamais l'URL avec un secret : cette URL n'en porte
            // aucun (voir commentaire de classe).
            DebugLogger.Log(
                $"BEATMAP DOWNLOAD ERROR | Browser open failed | {ex.Message}");

            return Task.FromResult(
                BeatmapDownloadProviderResult.Failure(
                    BeatmapDownloadProviderOutcome.ProviderUnavailable,
                    "Unable to open browser."));
        }
    }

    private static void DefaultOpenUrl(string url)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true,
        });
    }
}
