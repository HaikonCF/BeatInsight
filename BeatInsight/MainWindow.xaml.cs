using AutoMapper;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.Library;
using BeatInsight.Models.Ml;
using BeatInsight.Models.Persistence;
using BeatInsight.Parser;
using BeatInsight.Services;
using BeatInsight.Services.Library;
using BeatInsight.Services.Ml;
using BeatInsight.Services.Persistence;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using static BeatInsight.OsuApiService;

// Cette classe représente la fenêtre principale de BeatInsight et contient la logique qui surveille la map actuellement sélectionnée.

namespace BeatInsight
// On garde un HttpClient réutilisable pour demander régulièrement les données JSON d'osu! via l'API locale.
{
    // Le DispatcherTimer permet de vérifier périodiquement si la map sélectionnée a changé.
    public partial class MainWindow : Window
    {
        // Chemin complet de la dernière map traitée. Il sert à éviter de recharger la même map en boucle.
        private readonly HttpClient client = new HttpClient();
        // Indique qu'une mise à jour est déjà en cours pour éviter que deux appels UpdateMap() se chevauchent.
        private readonly DispatcherTimer mapTimer;
        private readonly OsuApiService osuApi = new OsuApiService();

        // Le constructeur prépare la fenêtre et démarre la surveillance automatique de la map.
        private string? currentMapPath;
        private bool isUpdating;
        private bool tosuConnected;
        private string? currentBeatmapUrl;
        private readonly OsuApiService osuApiService = new();

        // Index persistant des analyses.
        //
        // Le service exécute le pipeline local inchangé en cas de miss
        // et se contente de restituer un résultat déjà calculé en cas
        // de hit. Toute défaillance de la base dégrade silencieusement
        // vers le pipeline V1.
        private readonly BeatmapAnalysisCacheService analysisCache =
            new(new BeatmapAnalysisRepository(
                BeatmapAnalysisRepository.DefaultDatabasePath));

        // Dataset ML indépendant du cache runtime : cette instance ne sert
        // qu'aux statistiques et au builder de captures fraîches.
        private readonly MlDatasetSampleRepository mlDatasetRepository =
            new(MlDatasetSampleRepository.DefaultDatabasePath);

        // Résolution du dossier Songs (préférence manuelle > tosu).
        private readonly SongsPathResolver songsPathResolver = new();

        // Dernier chemin Songs rapporté par tosu, mémorisé afin que
        // les actions de la bibliothèque puissent résoudre un chemin
        // même entre deux mises à jour de MapTimer_Tick.
        private string? lastTosuSongsPath;

        // État du scan manuel de la bibliothèque. Un seul scan peut
        // utiliser le cache à la fois afin de préserver un traitement
        // strictement séquentiel et d'éviter toute course avec Tosu.
        private CancellationTokenSource? libraryScanCancellation;
        private bool isLibraryScanRunning;
        private bool acceptsLibraryScanProgress;

        // État autonome du backfill ML. Il bloque les actions concurrentes
        // de bibliothèque et la boucle Tosu, mais ne modifie jamais le scan
        // runtime ni son cache.
        private CancellationTokenSource? datasetBuildCancellation;
        private bool isDatasetBuildRunning;
        private bool acceptsDatasetBuildProgress;

        private bool IsBackgroundLibraryWorkRunning =>
            isLibraryScanRunning || isDatasetBuildRunning;

        private async Task TestOsuApi(int beatmapId)
        {
            try
            {
                string json = await osuApi.GetBeatmap(beatmapId);

                DebugLogger.Log(
                    $"OSU BEATMAP API OK | BeatmapId={beatmapId}");

                DebugLogger.Detailed(
                    $"OSU BEATMAP API RESPONSE | BeatmapId={beatmapId}");

                DebugLogger.Detailed(json);
            }
            catch (Exception ex)
            {
                DebugLogger.Log(
                    $"OSU BEATMAP API ERROR | " +
                    $"BeatmapId={beatmapId} | " +
                    $"{ex.Message}");

                DebugLogger.Detailed(
                    ex.ToString());
            }
        }


        public MainWindow()
        // On crée le timer qui servira à vérifier régulièrement l'état actuel d'osu!.
        {

            InitializeComponent();




            mapTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };

            mapTimer.Tick += MapTimer_Tick;
            mapTimer.Start();

            RefreshSongsFolderDisplay();
            RefreshDatasetStatistics();
        }

        public string AppVersion => $"BeatInsight v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";

        // Si une mise à jour précédente n'est pas terminée, on quitte immédiatement pour éviter deux traitements simultanés.

        private async void MapTimer_Tick(object? sender, EventArgs e)
        {
            if (isUpdating || IsBackgroundLibraryWorkRunning)
                return;

            isUpdating = true;

            try
            {
                await UpdateMap();
            }
            catch (Exception ex)
            {
                DebugLogger.Log(
                    $"UPDATE MAP ERROR | {ex.Message}");

                DebugLogger.Detailed(
                    ex.ToString());
            }
            finally
            {
                isUpdating = false;
            }
        }

        private void CopyAnalysis_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not Beatmap beatmap)
                return;

            GameplayProfile profile = beatmap.GameplayProfile;

            StringBuilder text = new();

            // ============================================================
            // MAP
            // ============================================================

            text.AppendLine($"MAP = {beatmap.Title}");

            if (!string.IsNullOrWhiteSpace(beatmap.Version))
                text.AppendLine($"DIFFICULTY = {beatmap.Version}");

            text.AppendLine();

            // ============================================================
            // IDENTITY
            // ============================================================

            text.AppendLine("GAMEPLAY IDENTITY");
            text.AppendLine(profile.Identity.FullName);

            text.AppendLine();

            text.AppendLine("CONFIDENCE");
            text.AppendLine($"{profile.Identity.Confidence:F0}%");

            text.AppendLine();

            // ============================================================
            // TRAITS
            // ============================================================

            text.AppendLine("TRAITS");

            var traits = profile.Identity.Traits
                .Distinct()
                .ToList();

            if (traits.Count == 0)
            {
                text.AppendLine("None");
            }
            else
            {
                foreach (string trait in traits)
                    text.AppendLine($"• {trait}");
            }

            text.AppendLine();

            // ============================================================
            // PATTERNS
            // ============================================================

            text.AppendLine("PATTERNS");

            text.AppendLine(
                $"Stream: {profile.StreamRatio * 100:F2}%");

            text.AppendLine(
                $"Jump: {profile.JumpRatio * 100:F2}%");

            text.AppendLine(
                $"Burst: {profile.BurstRatio * 100:F2}%");

            text.AppendLine();

            // ============================================================
            // SCORES
            // ============================================================

            text.AppendLine("SCORES");

            text.AppendLine(
                $"Tech: {profile.TechScore:F0}%");

            text.AppendLine(
                $"Read: {profile.ReadScore:F0}%");

            text.AppendLine(
                $"Speed: {profile.SpeedScore:F0}%");

            text.AppendLine(
                $"Aim: {profile.AimScore:F0}%");

            text.AppendLine();

            // ============================================================
            // SIGNALS
            // ============================================================

            text.AppendLine("SIGNALS");

            text.AppendLine(
                $"Tech: Transition {profile.TechTransitionSignal:F0}% / " +
                $"Structure {profile.TechStructureSignal:F0}% / " +
                $"Spatial {profile.TechSpatialSignal:F0}% / " +
                $"Temporal {profile.TechTemporalSignal:F0}%");

            text.AppendLine(
                $"Read: Density {profile.ReadDensitySignal:P0} / " +
                $"Clutter {profile.ReadClutterSignal:P0} / " +
                "Persistence neutralized / " +
                $"CS {profile.ReadCSSignal:P0} (neutralized)");

            text.AppendLine(
                $"Read: Intensity {profile.ReadIntensity} / " +
                $"Presence {profile.ReadCoverage:P0} / " +
                $"Sections {profile.ReadSectionCount}");

            text.AppendLine(
                $"Read: Predictability {profile.ReadPredictability:P0} / " +
                $"Novelty {profile.ReadNovelty:P0}");

            text.AppendLine(
                $"Read: Regularity Temporal {profile.ReadTemporalRegularity:P0} / " +
                $"Spacing {profile.ReadSpacingRegularity:P0} / " +
                $"Trajectory {profile.ReadTrajectoryRepetition:P0}");

            text.AppendLine(
                $"Read: Ambiguity {profile.ReadAmbiguity:P0}");

            text.AppendLine(
                $"Speed: Fast {profile.SpeedFastObjectRatio * 100:F0}% / " +
                $"Density {profile.SpeedDensitySignal:F0}% / " +
                $"AR {profile.SpeedARSignal:F0}%");

            text.AppendLine(
                $"Aim: Distance {profile.AimDistanceSignal:F0}% / " +
                $"Speed {profile.AimSpeedSignal:F0}% / " +
                $"Angle {profile.AimAngleSignal:F0}% / " +
                $"Temporal {profile.AimTemporalSignal:F0}%");

            // ============================================================
            // COPY
            // ============================================================

            Clipboard.SetText(text.ToString());
        }

        private void OpenBeatmap_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(currentBeatmapUrl))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = currentBeatmapUrl,
                UseShellExecute = true
            });
        }

        private void ReportClassification_Click(object sender, RoutedEventArgs e)
{
    if (DataContext is not Beatmap beatmap)
        return;

    GameplayProfile profile = beatmap.GameplayProfile;

    string traits = profile.Identity.Traits.Count == 0
        ? "None"
        : string.Join(
            "\n",
            profile.Identity.Traits
                .Distinct()
                .Select(t => $"- {t}")
        );

    string body = $"""
## Beatmap

**Title:** {beatmap.Title}

**Difficulty:** {beatmap.Version}

## BeatInsight

**Version:** {AppVersion}

**Identity:** {profile.Identity.FullName}

**Confidence:** {profile.Identity.Confidence:F0}%

### Traits

{traits}

### Gameplay

- Stream: {profile.StreamRatio * 100:F2}%
- Jump: {profile.JumpRatio * 100:F2}%
- Burst: {profile.BurstRatio * 100:F2}%

### Scores

- Tech: {profile.TechScore:F0}%
- Read: {profile.ReadScore:F0}%
- Speed: {profile.SpeedScore:F0}%
- Aim: {profile.AimScore:F0}%

## Expected classification

<!-- What should BeatInsight classify this map as? -->


## Why?

<!-- Explain why you think the classification is incorrect. -->

## Additional information

<!-- Any other useful information? -->
""";

    string title =
        $"Classification incorrecte - {beatmap.Title} [{beatmap.Version}]";

    string url =
        "https://github.com/HaikonCF/BeatInsight/issues/new" +
        $"?title={Uri.EscapeDataString(title)}" +
        $"&body={Uri.EscapeDataString(body)}";

    Process.Start(new ProcessStartInfo
    {
        FileName = url,
        UseShellExecute = true
    });
}

        // ============================================================
        // OSU! LIBRARY
        // ============================================================

        // Met à jour l'affichage du dossier Songs résolu (préférence
        // manuelle prioritaire, sinon chemin tosu). N'écrit jamais la
        // préférence : cette méthode ne fait que lire et afficher.
        private void RefreshSongsFolderDisplay()
        {
            string? resolved =
                songsPathResolver.Resolve(lastTosuSongsPath);

            SongsFolderText.Text = resolved ?? "Not set";
        }

        private void ChangeSongsFolder_Click(
            object sender,
            RoutedEventArgs e)
        {
            PromptForManualSongsFolder();
        }

        // Ouvre le sélecteur de dossier et, en cas de sélection
        // valide, sauvegarde la préférence manuelle. Un chemin manuel
        // déjà valide n'est jamais écrasé silencieusement : ce n'est
        // que lorsque l'utilisateur choisit explicitement un nouveau
        // dossier ici que la préférence change.
        private bool PromptForManualSongsFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select osu! Songs folder",
            };

            string? current =
                songsPathResolver.Resolve(lastTosuSongsPath);

            if (!string.IsNullOrWhiteSpace(current)
                && Directory.Exists(current))
            {
                dialog.InitialDirectory = current;
            }

            if (dialog.ShowDialog(this) != true
                || string.IsNullOrWhiteSpace(dialog.FolderName))
            {
                return false;
            }

            songsPathResolver.SaveManualPath(dialog.FolderName);
            RefreshSongsFolderDisplay();

            return true;
        }

        private async void ScanLibrary_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (IsBackgroundLibraryWorkRunning)
                return;

            string? songsFolder =
                songsPathResolver.Resolve(lastTosuSongsPath);

            // Aucun chemin résolu : on ouvre directement le
            // sélecteur plutôt que d'afficher une confirmation vide.
            if (songsFolder is null)
            {
                if (!PromptForManualSongsFolder())
                    return;

                songsFolder =
                    songsPathResolver.Resolve(lastTosuSongsPath);
            }

            if (string.IsNullOrWhiteSpace(songsFolder))
                return;

            MessageBoxResult confirmation = MessageBox.Show(
                this,
                $"BeatInsight va scanner :\n{songsFolder}\n\n"
                    + "Le premier scan peut prendre plusieurs minutes.\n"
                    + "Les résultats seront enregistrés localement et "
                    + "réutilisés ensuite.",
                "⚠ Library Scan",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (confirmation != MessageBoxResult.OK)
                return;

            await StartLibraryScanAsync(songsFolder);
        }

        private async Task StartLibraryScanAsync(string songsFolder)
        {
            if (IsBackgroundLibraryWorkRunning)
                return;

            CancellationTokenSource cancellation = new();
            libraryScanCancellation = cancellation;
            isLibraryScanRunning = true;
            acceptsLibraryScanProgress = true;

            SetLibraryScanControls(isScanning: true);
            ShowLibraryScanPreparingState();

            try
            {
                // Une mise à jour Tosu déjà lancée termine avant le
                // scan. La garde isLibraryScanRunning bloque ensuite
                // tout nouvel appel Tosu ou Community Tags.
                while (isUpdating)
                    await Task.Delay(50);

                cancellation.Token.ThrowIfCancellationRequested();

                IProgress<LibraryScanProgress> progress =
                    new Progress<LibraryScanProgress>(
                        UpdateLibraryScanProgress);

                BeatmapLibraryScanner scanner =
                    new(analysisCache);

                LibraryScanResult result = await Task.Run(() =>
                    scanner.Scan(
                        songsFolder,
                        progress,
                        cancellation.Token));

                acceptsLibraryScanProgress = false;
                ShowLibraryScanSummary(result);
            }
            catch (OperationCanceledException)
                when (cancellation.IsCancellationRequested)
            {
                acceptsLibraryScanProgress = false;
                ShowLibraryScanCancelledWithoutResult();
            }
            catch (Exception ex)
            {
                acceptsLibraryScanProgress = false;
                DebugLogger.Log(
                    $"LIBRARY SCAN ERROR | {ex.Message}");

                DebugLogger.Detailed(ex.ToString());

                ShowLibraryScanFailure(ex);
            }
            finally
            {
                isLibraryScanRunning = false;
                acceptsLibraryScanProgress = false;

                if (ReferenceEquals(libraryScanCancellation, cancellation))
                {
                    libraryScanCancellation = null;
                    cancellation.Dispose();
                }

                SetLibraryScanControls(isScanning: false);
            }
        }

        private void CancelLibraryScan_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (libraryScanCancellation is not {
                IsCancellationRequested: false
            } cancellation)
            {
                return;
            }

            cancellation.Cancel();
            CancelLibraryScanButton.IsEnabled = false;
            LibraryScanStatusText.Text = "Cancelling scan...";
        }

        private void SetLibraryScanControls(bool isScanning)
        {
            ChangeSongsFolderButton.IsEnabled =
                !isScanning && !isDatasetBuildRunning;
            ScanLibraryButton.IsEnabled =
                !isScanning && !isDatasetBuildRunning;
            BuildDatasetButton.IsEnabled =
                !isScanning && !isDatasetBuildRunning;
            CancelLibraryScanButton.Visibility = isScanning
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (isScanning)
                CancelLibraryScanButton.IsEnabled = true;
        }

        private void ShowLibraryScanPreparingState()
        {
            LibraryScanProgressPanel.Visibility = Visibility.Visible;
            LibraryScanStatusText.Text = "Scanning library...";
            LibraryScanProgressBar.Value = 0;
            LibraryScanProgressText.Text = "Finding beatmaps...";
            LibraryScanPercentText.Text = "0.0%";
            LibraryScanAnalyzedText.Text = "Analyzed: 0";
            LibraryScanUpToDateText.Text = "Up to date: 0";
            LibraryScanUnsupportedText.Text = "Unsupported: 0";
            LibraryScanFailedText.Text = "Failed: 0";
            LibraryScanCurrentFileText.Text = "Current file: —";
            LibraryScanCurrentFileText.ToolTip = null;
        }

        private void UpdateLibraryScanProgress(
            LibraryScanProgress progress)
        {
            if (!acceptsLibraryScanProgress)
                return;

            LibraryScanProgressPanel.Visibility = Visibility.Visible;
            LibraryScanStatusText.Text = "Scanning library...";
            LibraryScanProgressBar.Value = Math.Clamp(
                progress.Percent,
                0.0,
                100.0);
            LibraryScanProgressText.Text =
                $"Processed {progress.ProcessedFiles} / {progress.TotalFiles}";
            LibraryScanPercentText.Text = $"{progress.Percent:0.0}%";
            LibraryScanAnalyzedText.Text =
                $"Analyzed: {progress.AnalyzedFiles}";
            LibraryScanUpToDateText.Text =
                $"Up to date: {progress.SkippedUpToDateFiles}";
            LibraryScanUnsupportedText.Text =
                $"Unsupported: {progress.SkippedUnsupportedFiles}";
            LibraryScanFailedText.Text =
                $"Failed: {progress.FailedFiles}";

            string currentFile = string.IsNullOrWhiteSpace(progress.CurrentFile)
                ? "—"
                : Path.GetFileName(progress.CurrentFile);

            LibraryScanCurrentFileText.Text =
                $"Current file: {currentFile}";
            LibraryScanCurrentFileText.ToolTip = progress.CurrentFile;
        }

        private void ShowLibraryScanSummary(LibraryScanResult result)
        {
            double percent = result.TotalFiles == 0
                ? 0.0
                : result.ProcessedFiles * 100.0 / result.TotalFiles;

            LibraryScanProgressPanel.Visibility = Visibility.Visible;
            LibraryScanStatusText.Text = result.WasCancelled
                ? "Library scan cancelled"
                : "Library scan complete";
            LibraryScanProgressBar.Value = Math.Clamp(percent, 0.0, 100.0);
            LibraryScanProgressText.Text =
                $"Processed {result.ProcessedFiles} / {result.TotalFiles}";
            LibraryScanPercentText.Text = $"{percent:0.0}%";
            LibraryScanAnalyzedText.Text = $"Analyzed: {result.AnalyzedFiles}";
            LibraryScanUpToDateText.Text =
                $"Up to date: {result.SkippedUpToDateFiles}";
            LibraryScanUnsupportedText.Text =
                $"Unsupported: {result.SkippedUnsupportedFiles}";
            LibraryScanFailedText.Text = $"Failed: {result.FailedFiles}";
            LibraryScanCurrentFileText.Text = result.WasCancelled
                ? $"Cancelled after {result.Elapsed:mm\\:ss}."
                : $"Completed in {result.Elapsed:mm\\:ss}.";
            LibraryScanCurrentFileText.ToolTip = null;
        }

        private void ShowLibraryScanCancelledWithoutResult()
        {
            LibraryScanStatusText.Text = "Library scan cancelled";
            LibraryScanCurrentFileText.Text =
                "Cancelled before a final summary was available.";
            LibraryScanCurrentFileText.ToolTip = null;
        }

        private void ShowLibraryScanFailure(Exception exception)
        {
            LibraryScanProgressPanel.Visibility = Visibility.Visible;
            LibraryScanStatusText.Text = "Library scan failed";
            LibraryScanCurrentFileText.Text =
                $"Error: {exception.Message}";
            LibraryScanCurrentFileText.ToolTip = exception.ToString();
        }


        // ============================================================
        // ML DATASET
        // ============================================================

        private MlDatasetSampleStatistics? RefreshDatasetStatistics()
        {
            try
            {
                mlDatasetRepository.EnsureSchema();

                MlDatasetSampleStatistics statistics =
                    mlDatasetRepository.GetStatistics();

                DatasetSampleCountText.Text =
                    $"Samples: {statistics.SampleCount:N0}";
                DatasetValidatedCountText.Text =
                    $"Validated: {statistics.HumanValidatedCount:N0}";
                DatasetUnlabeledCountText.Text =
                    $"Unlabeled: {statistics.UnlabeledCount:N0}";

                return statistics;
            }
            catch (Exception ex)
            {
                // Une base temporairement indisponible ne doit pas empêcher
                // l'ouverture de la fenêtre. Le builder rapportera ensuite
                // son échec global dans son propre panneau, sans MessageBox.
                DatasetSampleCountText.Text = "Samples: 0";
                DatasetValidatedCountText.Text = "Validated: 0";
                DatasetUnlabeledCountText.Text = "Unlabeled: 0";

                DebugLogger.Log($"ML DATASET STATS ERROR | {ex.Message}");
                DebugLogger.Detailed(ex.ToString());

                return null;
            }
        }

        private async void BuildDataset_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (IsBackgroundLibraryWorkRunning)
                return;

            string? songsFolder =
                songsPathResolver.Resolve(lastTosuSongsPath);

            if (songsFolder is null)
            {
                if (!PromptForManualSongsFolder())
                    return;

                songsFolder = songsPathResolver.Resolve(lastTosuSongsPath);
            }

            if (string.IsNullOrWhiteSpace(songsFolder))
                return;

            MlDatasetSampleStatistics? statistics =
                RefreshDatasetStatistics();

            // La confirmation est réservée au premier corpus vide. Les
            // passages suivants sont incrémentaux et ne refont qu'une analyse
            // fraîche des fichiers absents ou périmés.
            if (statistics?.SampleCount == 0)
            {
                MessageBoxResult confirmation = MessageBox.Show(
                    this,
                    $"BeatInsight va construire un dataset ML local depuis :\n"
                        + $"{songsFolder}\n\n"
                        + "Le premier backfill peut prendre plusieurs minutes.\n"
                        + "Aucun entraînement ni label automatique ne sera créé.",
                    "⚠ Build ML Dataset",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);

                if (confirmation != MessageBoxResult.OK)
                    return;
            }

            await StartDatasetBuildAsync(songsFolder);
        }

        private async Task StartDatasetBuildAsync(string songsFolder)
        {
            if (IsBackgroundLibraryWorkRunning)
                return;

            CancellationTokenSource cancellation = new();
            datasetBuildCancellation = cancellation;
            isDatasetBuildRunning = true;
            acceptsDatasetBuildProgress = true;

            SetDatasetBuildControls(isBuilding: true);
            ShowDatasetBuildPreparingState();

            try
            {
                // Une mise à jour Tosu déjà lancée termine avant le backfill.
                // La garde IsBackgroundLibraryWorkRunning bloque ensuite tout
                // nouvel appel Tosu et donc tout appel Community/API.
                while (isUpdating)
                    await Task.Delay(50);

                cancellation.Token.ThrowIfCancellationRequested();

                IProgress<MlDatasetBuildProgress> progress =
                    new Progress<MlDatasetBuildProgress>(
                        UpdateDatasetBuildProgress);

                MlDatasetBuilder builder = new(mlDatasetRepository);

                MlDatasetBuildResult result = await Task.Run(() =>
                    builder.Build(
                        songsFolder,
                        progress,
                        cancellation.Token));

                acceptsDatasetBuildProgress = false;
                ShowDatasetBuildSummary(result);
            }
            catch (OperationCanceledException)
                when (cancellation.IsCancellationRequested)
            {
                acceptsDatasetBuildProgress = false;
                ShowDatasetBuildCancelledWithoutResult();
            }
            catch (Exception ex)
            {
                acceptsDatasetBuildProgress = false;

                DebugLogger.Log($"ML DATASET BUILD ERROR | {ex.Message}");
                DebugLogger.Detailed(ex.ToString());

                ShowDatasetBuildFailure(ex);
            }
            finally
            {
                isDatasetBuildRunning = false;
                acceptsDatasetBuildProgress = false;

                if (ReferenceEquals(datasetBuildCancellation, cancellation))
                {
                    datasetBuildCancellation = null;
                    cancellation.Dispose();
                }

                RefreshDatasetStatistics();
                SetDatasetBuildControls(isBuilding: false);
            }
        }

        private void CancelDatasetBuild_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (datasetBuildCancellation is not {
                IsCancellationRequested: false
            } cancellation)
            {
                return;
            }

            cancellation.Cancel();
            CancelDatasetBuildButton.IsEnabled = false;
            DatasetBuildStatusText.Text = "Cancelling dataset build...";
        }

        private void SetDatasetBuildControls(bool isBuilding)
        {
            ChangeSongsFolderButton.IsEnabled =
                !isBuilding && !isLibraryScanRunning;
            ScanLibraryButton.IsEnabled =
                !isBuilding && !isLibraryScanRunning;
            BuildDatasetButton.IsEnabled =
                !isBuilding && !isLibraryScanRunning;
            CancelDatasetBuildButton.Visibility = isBuilding
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (isBuilding)
                CancelDatasetBuildButton.IsEnabled = true;
        }

        private void ShowDatasetBuildPreparingState()
        {
            DatasetBuildProgressPanel.Visibility = Visibility.Visible;
            DatasetBuildStatusText.Text = "Building ML dataset...";
            DatasetBuildProgressBar.Value = 0;
            DatasetBuildProgressText.Text = "Finding beatmaps...";
            DatasetBuildPercentText.Text = "0.0%";
            DatasetBuildCapturedText.Text = "Captured: 0";
            DatasetBuildUpToDateText.Text = "Up to date: 0";
            DatasetBuildUnsupportedText.Text = "Unsupported: 0";
            DatasetBuildFailedText.Text = "Failed: 0";
            DatasetBuildCurrentFileText.Text = "Current: —";
            DatasetBuildCurrentFileText.ToolTip = null;
        }

        private void UpdateDatasetBuildProgress(
            MlDatasetBuildProgress progress)
        {
            if (!acceptsDatasetBuildProgress)
                return;

            DatasetBuildProgressPanel.Visibility = Visibility.Visible;
            DatasetBuildStatusText.Text = "Building ML dataset...";
            DatasetBuildProgressBar.Value = Math.Clamp(
                progress.Percent,
                0.0,
                100.0);
            DatasetBuildProgressText.Text =
                $"{progress.ProcessedFiles:N0} / {progress.TotalFiles:N0} "
                    + $"— {progress.Percent:0.0}%";
            DatasetBuildPercentText.Text = $"{progress.Percent:0.0}%";
            DatasetBuildCapturedText.Text =
                $"Captured: {progress.CapturedFiles:N0}";
            DatasetBuildUpToDateText.Text =
                $"Up to date: {progress.DatasetUpToDateFiles:N0}";
            DatasetBuildUnsupportedText.Text =
                $"Unsupported: {progress.UnsupportedFiles:N0}";
            DatasetBuildFailedText.Text =
                $"Failed: {progress.FailedFiles:N0}";

            string currentFile = string.IsNullOrWhiteSpace(progress.CurrentFile)
                ? "—"
                : Path.GetFileName(progress.CurrentFile);

            DatasetBuildCurrentFileText.Text = $"Current: {currentFile}";
            DatasetBuildCurrentFileText.ToolTip = progress.CurrentFile;
        }

        private void ShowDatasetBuildSummary(MlDatasetBuildResult result)
        {
            double percent = result.TotalFiles == 0
                ? 0.0
                : result.ProcessedFiles * 100.0 / result.TotalFiles;

            DatasetBuildProgressPanel.Visibility = Visibility.Visible;
            DatasetBuildStatusText.Text = result.WasCancelled
                ? "ML dataset build cancelled"
                : "ML dataset build complete";
            DatasetBuildProgressBar.Value = Math.Clamp(percent, 0.0, 100.0);
            DatasetBuildProgressText.Text =
                $"{result.ProcessedFiles:N0} / {result.TotalFiles:N0} "
                    + $"— {percent:0.0}%";
            DatasetBuildPercentText.Text = $"{percent:0.0}%";
            DatasetBuildCapturedText.Text =
                $"Captured: {result.CapturedFiles:N0}";
            DatasetBuildUpToDateText.Text =
                $"Up to date: {result.DatasetUpToDateFiles:N0}";
            DatasetBuildUnsupportedText.Text =
                $"Unsupported: {result.UnsupportedFiles:N0}";
            DatasetBuildFailedText.Text =
                $"Failed: {result.FailedFiles:N0}";
            DatasetBuildCurrentFileText.Text = result.WasCancelled
                ? $"Cancelled after {result.Elapsed:mm\\:ss}."
                : $"Completed in {result.Elapsed:mm\\:ss}.";
            DatasetBuildCurrentFileText.ToolTip = null;
        }

        private void ShowDatasetBuildCancelledWithoutResult()
        {
            DatasetBuildProgressPanel.Visibility = Visibility.Visible;
            DatasetBuildStatusText.Text = "ML dataset build cancelled";
            DatasetBuildCurrentFileText.Text =
                "Cancelled before a final summary was available.";
            DatasetBuildCurrentFileText.ToolTip = null;
        }

        private void ShowDatasetBuildFailure(Exception exception)
        {
            DatasetBuildProgressPanel.Visibility = Visibility.Visible;
            DatasetBuildStatusText.Text = "ML dataset build failed";
            DatasetBuildCurrentFileText.Text =
                $"Error: {exception.Message}";
            DatasetBuildCurrentFileText.ToolTip = exception.ToString();
        }

        private async Task<List<CommunityTag>> GetCommunityTags(int beatmapId)
        {
            try
            {
                List<OsuApiService.OsuTagVote> tags =
                    await osuApi.GetBeatmapCommunityTags(beatmapId);

                return tags
                    .Select(tag => new CommunityTag
                    {
                        Name = tag.Name,
                        Votes = tag.Votes
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                DebugLogger.Log(
                    $"COMMUNITY TAGS ERROR | " +
                    $"BeatmapId={beatmapId} | " +
                    $"{ex.Message}");

                DebugLogger.Detailed(
                    ex.ToString());

                return new List<CommunityTag>();
            }
        }

        private async Task TestTagsApi()
        {
            try
            {
                string token = await GetOsuAccessToken();

                using HttpRequestMessage request = new(
                    HttpMethod.Get,
                    "https://osu.ppy.sh/api/v2/tags"
                );

                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer",
                        token
                    );

                request.Headers.Add("Accept", "application/json");

                using HttpResponseMessage response =
                    await client.SendAsync(request);

                string body =
                    await response.Content.ReadAsStringAsync();

                DebugLogger.Log(
                    $"OSU TAGS API | " +
                    $"HTTP={(int)response.StatusCode} ({response.StatusCode})");

                DebugLogger.Detailed(
                    "OSU TAGS API RESPONSE:");

                DebugLogger.Detailed(
                    body);
            }
            catch (Exception ex)
            {
                DebugLogger.Log(
                    $"OSU TAGS API ERROR | {ex.Message}");

                DebugLogger.Detailed(
                    ex.ToString());
            }
        }

        private string? osuAccessToken;
        private DateTime osuTokenExpiration;

        private async Task<string> GetOsuAccessToken()
        {
            if (!string.IsNullOrWhiteSpace(osuAccessToken) &&
                DateTime.UtcNow < osuTokenExpiration)
            {
                return osuAccessToken;
            }

            string? clientSecret =
                Environment.GetEnvironmentVariable(
                    "BEATINSIGHT_OSU_CLIENT_SECRET");

            if (string.IsNullOrWhiteSpace(clientSecret))
            {
                throw new Exception(
                    "BEATINSIGHT_OSU_CLIENT_SECRET est introuvable.");
            }

            using HttpRequestMessage request =
                new(
                    HttpMethod.Post,
                    "https://osu.ppy.sh/oauth/token");

            request.Headers.Add(
                "Accept",
                "application/json");

            request.Content =
                new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["client_id"] = "66257",
                        ["client_secret"] = OsuSecrets.ClientSecret,
                        ["grant_type"] = "client_credentials",
                        ["scope"] = "public"
                    });

            using HttpResponseMessage response =
                await client.SendAsync(request);

            string responseBody =
                await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                DebugLogger.Log(
                    $"OSU API AUTH ERROR | " +
                    $"HTTP={(int)response.StatusCode} ({response.StatusCode})");

                DebugLogger.Detailed(
                    responseBody);

                throw new Exception(
                    $"osu! API HTTP {(int)response.StatusCode} ({response.StatusCode})");
            }

            using JsonDocument document =
                JsonDocument.Parse(responseBody);

            osuAccessToken =
                document.RootElement
                    .GetProperty("access_token")
                    .GetString()
                ?? throw new Exception(
                    "osu! API : access_token absent de la réponse.");

            int expiresIn =
                document.RootElement
                    .GetProperty("expires_in")
                    .GetInt32();

            osuTokenExpiration =
                DateTime.UtcNow.AddSeconds(expiresIn - 60);

            DebugLogger.Detailed(
                $"OSU API AUTH OK | ExpiresIn={expiresIn}s");

            return osuAccessToken!;
        }
        private async Task<bool> IsTosuAvailable()
        {
            try
            {
                using HttpResponseMessage response =
                    await client.GetAsync(
                        "http://127.0.0.1:24050/json",
                        HttpCompletionOption.ResponseHeadersRead);

                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private void SetTosuStatus(bool connected)
        {
            TosuStatusText.Text = connected
                ? "● Tosu connecté"
                : "● En attente de Tosu...";
        }
        private async Task UpdateMap()
        {
            if (IsBackgroundLibraryWorkRunning)
                return;

            // ============================================================
            // TOSU CONNECTION
            // ============================================================

            if (!await IsTosuAvailable())
            {
                if (tosuConnected)
                {
                    tosuConnected = false;

                    DebugLogger.Log(
                        "TOSU | Déconnecté.");
                }

                SetTosuStatus(false);

                return;
            }

            // Tosu vient d'être détecté.
            if (!tosuConnected)
            {
                tosuConnected = true;

                DebugLogger.Log(
                    "TOSU | Connecté.");

                SetTosuStatus(true);
            }

            // ============================================================
            // RÉCUPÉRATION DES DONNÉES TOSU
            // ============================================================

            string json = await client.GetStringAsync(
                $"http://127.0.0.1:24050/json?t={DateTime.UtcNow.Ticks}"
            );

            // On récupère le dossier racine dans lequel osu! stocke ses beatmaps.
            JsonDocument document = JsonDocument.Parse(json);

            int debugBeatmapId = document.RootElement
                .GetProperty("menu")
                .GetProperty("bm")
                .GetProperty("id")
                .GetInt32();

            string debugFile = document.RootElement
                .GetProperty("menu")
                .GetProperty("bm")
                .GetProperty("path")
                .GetProperty("file")
                .GetString()!;

            string folder =
                document.RootElement
                .GetProperty("menu")
                .GetProperty("bm")
                .GetProperty("path")
                .GetProperty("folder")
                // On récupère le nom du fichier .osu actuellement sélectionné.
                .GetString()!;

            string songs =
                document.RootElement
                .GetProperty("settings")
                .GetProperty("folders")
                .GetProperty("songs")
                .GetString()!;

            lastTosuSongsPath = songs;
            RefreshSongsFolderDisplay();

            // On récupère le chemin du background associé à la difficulté actuelle.

            string file =
                document.RootElement
                .GetProperty("menu")
                .GetProperty("bm")
                .GetProperty("path")
                .GetProperty("file")
                .GetString()!;

            // On assemble le dossier Songs, le dossier de la beatmap et le fichier .osu pour obtenir son chemin complet.

            string? bg = null;

            if (document.RootElement
                .GetProperty("menu")
                .GetProperty("bm")
                .GetProperty("path")
                .TryGetProperty("bg", out JsonElement bgElement))
            {
                bg = bgElement.GetString();
            }

            int beatmapId =
    document.RootElement
    .GetProperty("menu")
    .GetProperty("bm")
    .GetProperty("id")
    .GetInt32();



            string chemin = System.IO.Path.Combine(
                songs,
                folder,
                file
            );

            // ============================================================
            // MÊME MAP → ON NE FAIT RIEN
            // ============================================================

            if (chemin == currentMapPath)
            {
                return;
            }

            // ============================================================
            // NOUVELLE MAP
            // ============================================================

            // On mémorise immédiatement la nouvelle map
            currentMapPath = chemin;

            // URL osu!
            currentBeatmapUrl = $"https://osu.ppy.sh/b/{beatmapId}";


            Beatmap beatmap;

            try
            {
                beatmap = await Task.Run(() =>
                    analysisCache.GetOrAnalyze(chemin, beatmapId));
            }
            catch (Exception ex)
            {
                DebugLogger.Log(
                    $"EXCEPTION = {ex.GetType().FullName}");

                DebugLogger.Log(
                    $"MESSAGE = {ex.Message}");

                DebugLogger.Log(
                    $"STACK TRACE = {ex.StackTrace ?? "Unavailable"}");

                throw;
            }

            // Les opérations longues de bibliothèque suspendent les appels
            // Community Tags. Cette seconde garde couvre une demande de scan
            // ou de backfill faite pendant l'analyse locale de la map active.
            if (IsBackgroundLibraryWorkRunning)
                return;

            // ============================================================
            // COMMUNITY TAGS
            // ============================================================

            List<OsuApiService.OsuTagVote> osuCommunityTags =
                await osuApi.GetBeatmapCommunityTags(beatmapId);

            beatmap.CommunityTags =
                osuCommunityTags
                    .Select(tag => new CommunityTag
                    {
                        Name = tag.Name,
                        Votes = tag.Votes
                    })
                    .ToList();


            // ============================================================
            // COMMUNITY TAGS <-> GAMEPLAY IDENTITY
            // ============================================================

            GameplayProfile gameplayProfile =
                beatmap.GameplayProfile;

            GameplayIdentity identity =
                gameplayProfile.Identity;


            // ------------------------------------------------------------
            // Comparaison
            // ------------------------------------------------------------

            GameplayTagComparisonResult tagComparison =
                GameplayTagComparer.Compare(
                    beatmap.CommunityTags,
                    identity.FullName,
                    identity.Traits);


            // ------------------------------------------------------------
            // Stockage du résultat
            // ------------------------------------------------------------

            beatmap.TagComparison =
                tagComparison;

            CommunityIdentityAgreement communityIdentityAgreement =
                CommunityIdentityAgreementComparer.Compare(
                    beatmap.CommunityTags,
                    identity);

            beatmap.CommunityIdentityAgreement =
                communityIdentityAgreement;

            GameplayDebug.CommunityIdentityAgreement(
                communityIdentityAgreement);

            // ------------------------------------------------------------
            // Community
            // ------------------------------------------------------------

            if (GameplayDebug.CommunityTagsEnabled)
            {
                DebugLogger.Log(
                    "===== COMMUNITY TAG COMPARISON =====");

                if (!tagComparison.HasTags)
                {
                    DebugLogger.Log(
                        "TAG COMPARISON = Unavailable | No community tags");
                }
                else
                {
                    DebugLogger.Log(
                        $"TAG CONSISTENCY = {tagComparison.Score * 100:F1}%");

                    DebugLogger.Log(
                        $"TAG STATUS = {tagComparison.Status}");

                    DebugLogger.Log(
                        $"TOTAL COMMUNITY VOTES = {tagComparison.TotalVotes}");

                    foreach (GameplayTagComparison match
                             in tagComparison.Matches)
                    {
                        DebugLogger.Log(
                            $"TAG = {match.Tag} | " +
                            $"VOTES = {match.Votes} | " +
                            $"STATUS = {match.Status} | " +
                            $"SCORE = {match.Score * 100:F1}% | " +
                            $"WEIGHT = {match.VoteWeight:F3} | " +
                            $"CONCEPTS = {string.Join(", ", match.Concepts)}");
                    }
                }
            }



            // ============================================================
            // BACKGROUND
            // ============================================================

            if (!string.IsNullOrWhiteSpace(bg))
            {
                string backgroundPath = System.IO.Path.Combine(
                    songs,
                    folder,
                    bg
                );

                if (System.IO.File.Exists(backgroundPath))
                {
                    try
                    {
                        BackgroundImage.Source =
                            new BitmapImage(new Uri(backgroundPath));
                    }
                    catch
                    {
                        BackgroundImage.Source = null;
                    }
                }
                else
                {
                    BackgroundImage.Source = null;
                }
            }
            else
            {
                BackgroundImage.Source = null;
            }


            DataContext = beatmap;
        }
    }
}
