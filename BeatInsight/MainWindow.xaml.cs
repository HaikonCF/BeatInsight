using AutoMapper;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.Discovery;
using BeatInsight.Models.Library;
using BeatInsight.Models.Ml;
using BeatInsight.Models.Persistence;
using BeatInsight.Parser;
using BeatInsight.Services;
using BeatInsight.Services.CommunityDiscovery;
using BeatInsight.Services.Download;
using BeatInsight.Services.Library;
using BeatInsight.Services.Ml;
using BeatInsight.Services.Persistence;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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

        // Index persistant des analyses.
        //
        // Le service exécute le pipeline local inchangé en cas de miss
        // et se contente de restituer un résultat déjà calculé en cas
        // de hit. Toute défaillance de la base dégrade silencieusement
        // vers le pipeline V1.
        private readonly BeatmapAnalysisCacheService analysisCache =
            new(new BeatmapAnalysisRepository(
                BeatmapAnalysisRepository.DefaultDatabasePath));

        // Index runtime consulté uniquement pour charger en revue un candidat
        // déjà installé. Il ne déclenche jamais de parcours du dossier Songs.
        private readonly BeatmapAnalysisRepository localAnalysisRepository =
            new(BeatmapAnalysisRepository.DefaultDatabasePath);

        // Dataset ML indépendant du cache runtime : cette instance ne sert
        // qu'aux statistiques et au builder de captures fraîches.
        private readonly MlDatasetSampleRepository mlDatasetRepository =
            new(MlDatasetSampleRepository.DefaultDatabasePath);

        // Résolution du dossier Songs (préférence manuelle > tosu).
        private readonly SongsPathResolver songsPathResolver = new();

        // Découverte communautaire : service de lecture seul. Son backend
        // reste séparé des labels humains et des files de labellisation.
        private readonly CommunityBeatmapDiscoveryService
            communityDiscoveryService;
        private CancellationTokenSource? discoveryCancellation;
        private bool isDiscoveryRunning;
        private bool isLoadingDiscoveryReview;
        private CommunitySamplingFamily selectedDiscoveryFamily =
            CommunitySamplingFamily.Tech;
        private int selectedDiscoveryResultCount = 20;
        private readonly Dictionary<int, CommunityBeatmapCandidate>
            discoveryCandidatesByBeatmapId = [];

        // Téléchargement/import Community Discovery (V2.4.3). Le provider
        // n'écrit jamais d'octets aujourd'hui : voir
        // BrowserOpenBeatmapDownloadProvider pour l'audit d'authentification.
        // Aucune de ces deux instances ne modifie GameplayAnalyzer, le
        // scanner de bibliothèque ni le dataset ML.
        private readonly BeatmapDownloadService beatmapDownloadService =
            new(new BrowserOpenBeatmapDownloadProvider());
        private readonly BeatmapImportService beatmapImportService;
        private readonly Dictionary<int, CancellationTokenSource>
            discoveryDownloadCancellationsByBeatmapId = [];

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

        // État de labellisation de la map actuellement affichée. La
        // sélection est exclusivement déclenchée par les boutons humains :
        // elle n'est jamais initialisée depuis GameplayIdentity.
        private string? currentHumanLabelSourceFilePath;
        private long? currentHumanLabelSampleId;
        private bool hasCurrentDatasetSample;
        private MlHumanLabel? currentPrimaryHumanLabel;
        private MlHumanLabel? currentSecondaryHumanLabel;
        private MlHumanLabel? selectedPrimaryHumanLabel;
        private MlHumanLabel? selectedSecondaryHumanLabel;

        // Empêche une navigation Fast Labeling de se chevaucher avec une
        // autre (double-clic, ou Space maintenu) sans introduire d'état
        // persistant supplémentaire.
        private bool isNavigatingFastLabeling;

        // Tant que ce mode est actif, UpdateMap() ignore le polling tosu
        // afin qu'il ne remplace jamais la map chargée manuellement pour
        // la labellisation. Le timer continue de tourner : seule la
        // beatmap affichée est gelée, pas la connexion tosu elle-même.
        // Partagé par Fast Unlabeled, Calibration et Discovery Review :
        // leurs mécanismes de sélection restent distincts, mais ils utilisent
        // le même gel du polling tosu pour préserver la map manuelle.
        private bool isFastLabelingMode;

        // Ancre de navigation de la Calibration Queue, indépendante de
        // currentHumanLabelSampleId (ancre de la Fast Unlabeled Queue).
        private long? currentCalibrationSampleId;
        private bool isNavigatingCalibration;

        // Détermine vers quelle file Validate avance automatiquement.
        // None lorsque la map affichée n'a été chargée par aucune des
        // deux files (par exemple via tosu) : Validate ne navigue alors
        // nulle part, comme avant l'introduction des files.
        private LabelingQueueKind activeLabelingQueue =
            LabelingQueueKind.None;

        // État autonome du backfill BeatmapId. Il ne relance jamais
        // MlDatasetBuilder : seule la colonne BeatmapId des échantillons
        // existants est mise à jour (voir BeatmapIdBackfillService).
        private CancellationTokenSource? beatmapIdBackfillCancellation;
        private bool isBeatmapIdBackfillRunning;
        private bool acceptsBeatmapIdBackfillProgress;

        // Les workspaces ne sont qu'une séparation de présentation : le
        // pipeline d'analyse, les queues ML et leurs états restent partagés.
        private enum ActiveWorkspace { Analyzer, MlLab }

        private ActiveWorkspace activeWorkspace = ActiveWorkspace.Analyzer;

        private bool IsBackgroundLibraryWorkRunning =>
            isLibraryScanRunning
                || isDatasetBuildRunning
                || isBeatmapIdBackfillRunning;

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

            communityDiscoveryService = new CommunityBeatmapDiscoveryService(
                new OsuCommunityBeatmapDiscoverySource(osuApi),
                new RepositoryCommunityBeatmapLocalStateSource(
                    mlDatasetRepository,
                    localAnalysisRepository));

            // Sonde le dossier Songs directement (V2.4.3a) : un import osu!
            // réussi ne renseigne pas BeatmapAnalysisRepository, donc
            // confirmer via ce repository produisait un faux "Waiting for
            // osu! import..." permanent.
            beatmapImportService = new BeatmapImportService(
                new ProcessBeatmapImportShell(),
                new SongsFolderBeatmapInstallationProbe(
                    () => songsPathResolver.Resolve(lastTosuSongsPath)));

            SetActiveWorkspace(ActiveWorkspace.Analyzer);
            ApplyDiscoveryFilterVisuals();
            SetDiscoverySearchState(isRunning: false);




            mapTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };

            mapTimer.Tick += MapTimer_Tick;
            mapTimer.Start();

            RefreshSongsFolderDisplay();
            RefreshDatasetStatistics();
            RefreshFastLabelingProgress();
            RefreshCalibrationQueueProgress();
        }

        public string AppVersion => $"BeatInsight v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";

        private void AnalyzerWorkspace_Click(object sender, RoutedEventArgs e)
        {
            SetActiveWorkspace(ActiveWorkspace.Analyzer);
        }

        private void MlLabWorkspace_Click(object sender, RoutedEventArgs e)
        {
            SetActiveWorkspace(ActiveWorkspace.MlLab);
        }

        private void SetActiveWorkspace(ActiveWorkspace workspace)
        {
            activeWorkspace = workspace;

            bool isAnalyzer = workspace == ActiveWorkspace.Analyzer;

            AnalyzerHeaderPanel.Visibility =
                isAnalyzer ? Visibility.Visible : Visibility.Collapsed;
            AnalyzerLibraryPanel.Visibility =
                isAnalyzer ? Visibility.Visible : Visibility.Collapsed;
            TosuStatusText.Visibility =
                isAnalyzer ? Visibility.Visible : Visibility.Collapsed;
            AnalyzerWorkspacePanel.Visibility =
                isAnalyzer ? Visibility.Visible : Visibility.Collapsed;
            MlLabWorkspacePanel.Visibility =
                isAnalyzer ? Visibility.Collapsed : Visibility.Visible;

            AnalyzerWorkspaceButton.IsEnabled = !isAnalyzer;
            MlLabWorkspaceButton.IsEnabled = isAnalyzer;
        }

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
            bool otherWorkRunning =
                isDatasetBuildRunning || isBeatmapIdBackfillRunning;

            ChangeSongsFolderButton.IsEnabled =
                !isScanning && !otherWorkRunning;
            ScanLibraryButton.IsEnabled =
                !isScanning && !otherWorkRunning;
            BuildDatasetButton.IsEnabled =
                !isScanning && !otherWorkRunning;
            BackfillBeatmapIdsButton.IsEnabled =
                !isScanning && !otherWorkRunning;
            CancelLibraryScanButton.Visibility = isScanning
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (isScanning)
                CancelLibraryScanButton.IsEnabled = true;

            UpdateHumanLabelActionState();
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

        // ============================================================
        // COMMUNITY DISCOVERY
        // ============================================================

        private void DiscoveryFamily_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (isDiscoveryRunning
                || sender is not FrameworkElement { Tag: string familyName }
                || !Enum.TryParse(
                    familyName,
                    out CommunitySamplingFamily family))
            {
                return;
            }

            selectedDiscoveryFamily = family;
            ApplyDiscoveryFilterVisuals();
        }

        private void DiscoveryResultCount_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (isDiscoveryRunning
                || sender is not FrameworkElement { Tag: string countText }
                || !int.TryParse(countText, out int count))
            {
                return;
            }

            selectedDiscoveryResultCount = count;
            ApplyDiscoveryFilterVisuals();
        }

        private async void FindDiscoveryCandidates_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (isDiscoveryRunning || IsBackgroundLibraryWorkRunning)
            {
                return;
            }

            if (!CommunityDiscoveryUiRequestFactory.TryCreate(
                    selectedDiscoveryFamily,
                    selectedDiscoveryResultCount,
                    DiscoveryMinStarTextBox.Text,
                    DiscoveryMaxStarTextBox.Text,
                    out CommunityDiscoveryRequest? request,
                    out string inputError))
            {
                DiscoveryStatusText.Text = inputError;
                return;
            }

            // Les schemas préexistants sont seulement préparés avant les
            // lectures d'enrichissement. Aucun sample, label ou cache ne
            // peut être créé par une recherche.
            try
            {
                mlDatasetRepository.EnsureSchema();
                localAnalysisRepository.EnsureSchema();
            }
            catch (Exception ex)
            {
                DiscoveryStatusText.Text = "Discovery unavailable locally.";
                DebugLogger.Log($"COMMUNITY DISCOVERY STORAGE ERROR | {ex.Message}");
                DebugLogger.Detailed(ex.ToString());
                return;
            }

            var cancellation = new CancellationTokenSource();
            discoveryCancellation = cancellation;
            SetDiscoverySearchState(isRunning: true);
            DiscoveryStatusText.Text = "Searching...";

            try
            {
                IReadOnlyList<CommunityBeatmapCandidate> candidates =
                    await Task.Run(
                        () => communityDiscoveryService.FindCandidatesAsync(
                            request!,
                            cancellation.Token),
                        cancellation.Token);

                if (cancellation.IsCancellationRequested)
                {
                    return;
                }

                discoveryCandidatesByBeatmapId.Clear();
                CancelAllDiscoveryDownloads();

                foreach (CommunityBeatmapCandidate candidate in candidates)
                {
                    discoveryCandidatesByBeatmapId[candidate.BeatmapId] =
                        candidate;
                }

                DiscoveryResultsList.ItemsSource = candidates
                    .Select(CommunityDiscoveryCandidateViewFactory.Create)
                    .ToArray();

                int owned = candidates.Count(candidate => candidate.AlreadyOwned);
                int inDataset = candidates.Count(
                    candidate => candidate.AlreadyInMlDataset);
                int validated = candidates.Count(
                    candidate => candidate.HumanValidated);

                DiscoveryStatusText.Text = $"Found: {candidates.Count:N0}";
                DiscoverySummaryText.Text =
                    $"Owned: {owned:N0}   In Dataset: {inDataset:N0}   " +
                    $"Human Validated: {validated:N0}";
                DiscoveryEmptyText.Visibility = candidates.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            catch (OperationCanceledException) when (
                cancellation.IsCancellationRequested)
            {
                DiscoveryStatusText.Text = "Search cancelled.";
            }
            catch (OsuCommunityRateLimitException ex)
            {
                DiscoveryStatusText.Text =
                    "osu! rate limit reached. Please wait a moment and retry.";
                DebugLogger.Log(
                    "COMMUNITY DISCOVERY RATE LIMIT EXHAUSTED | "
                    + $"Request kind = {ex.RequestKind} | "
                    + $"Retry-After present = {ex.RetryAfter.HasValue}");
                DebugLogger.Log(ex.ToString());
            }
            catch (Exception ex)
            {
                DiscoveryStatusText.Text = "Search failed. Please try again.";
                string status = ex is HttpRequestException
                    {
                        StatusCode: HttpStatusCode statusCode,
                    }
                    ? statusCode.ToString()
                    : "Unavailable";
                DebugLogger.Log(
                    "COMMUNITY DISCOVERY ERROR | " +
                    $"{ex.GetType().Name} | {ex.Message} | " +
                    $"HTTP status = {status}");
                DebugLogger.Log(ex.ToString());
            }
            finally
            {
                if (ReferenceEquals(discoveryCancellation, cancellation))
                {
                    discoveryCancellation = null;
                    cancellation.Dispose();
                    SetDiscoverySearchState(isRunning: false);
                }
            }
        }

        private void CancelDiscovery_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (discoveryCancellation is null || !isDiscoveryRunning)
            {
                return;
            }

            DiscoveryStatusText.Text = "Cancelling...";
            discoveryCancellation.Cancel();
        }

        private void OpenDiscoveryCandidate_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: int beatmapId }
                || !discoveryCandidatesByBeatmapId.ContainsKey(beatmapId))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"https://osu.ppy.sh/b/{beatmapId}",
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                DiscoveryStatusText.Text = "Unable to open osu! page.";
                DebugLogger.Log(
                    $"COMMUNITY DISCOVERY OPEN ERROR | {ex.Message}");
                DebugLogger.Detailed(ex.ToString());
            }
        }

        private async void LoadDiscoveryCandidateForReview_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (isLoadingDiscoveryReview || IsBackgroundLibraryWorkRunning
                || sender is not FrameworkElement { Tag: int beatmapId }
                || !discoveryCandidatesByBeatmapId.TryGetValue(
                    beatmapId,
                    out CommunityBeatmapCandidate? candidate))
            {
                return;
            }

            isLoadingDiscoveryReview = true;

            try
            {
                string? sourceFilePath =
                    localAnalysisRepository.FindSourceFilePathByBeatmapId(
                        candidate.BeatmapId);
                CommunityDiscoveryReviewTarget target =
                    CommunityDiscoveryReviewResolver.Resolve(
                        candidate,
                        sourceFilePath);

                if (!target.CanLoad || target.SourceFilePath is null)
                {
                    DiscoveryStatusText.Text = target.Status;
                    return;
                }

                DiscoveryStatusText.Text = "Loading local map for review...";

                Beatmap beatmap = await Task.Run(() =>
                    analysisCache.GetOrAnalyze(
                        target.SourceFilePath,
                        candidate.BeatmapId));

                if (IsBackgroundLibraryWorkRunning)
                {
                    return;
                }

                currentMapPath = target.SourceFilePath;
                currentBeatmapUrl =
                    $"https://osu.ppy.sh/b/{candidate.BeatmapId}";
                BackgroundImage.Source = null;
                DataContext = beatmap;

                // Même isolation anti-tosu que les files ML, mais cette
                // source n'a aucune navigation automatique. RefreshHuman...
                // réinitialise volontairement les sélections humaines.
                activeLabelingQueue = LabelingQueueKind.DiscoveryReview;
                SetDiscoveryReviewMode();
                RefreshHumanLabelPanel(beatmap, target.SourceFilePath);
                DiscoveryStatusText.Text = "Loaded for review.";
            }
            catch (Exception ex)
            {
                DiscoveryStatusText.Text = "Unable to load local map.";
                DebugLogger.Log(
                    "COMMUNITY DISCOVERY REVIEW ERROR | " +
                    $"{ex.GetType().Name} | {ex.Message}");
                DebugLogger.Detailed(ex.ToString());
            }
            finally
            {
                isLoadingDiscoveryReview = false;
            }
        }

        /// <summary>
        /// Nombre d'intervalles de sondage borné après le lancement de
        /// l'import : un délai raisonnable pour laisser osu! importer,
        /// sans jamais rescanner le dossier Songs entier (voir
        /// SongsFolderBeatmapInstallationProbe, énumération ciblée/bornée).
        /// </summary>
        private const int ImportConfirmationMaxAttempts = 15;
        private static readonly TimeSpan ImportConfirmationInterval =
            TimeSpan.FromSeconds(4);

        private async void DownloadAndImportDiscoveryCandidate_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: int beatmapId }
                || !discoveryCandidatesByBeatmapId.TryGetValue(
                    beatmapId,
                    out CommunityBeatmapCandidate? candidate)
                || discoveryDownloadCancellationsByBeatmapId.ContainsKey(
                    beatmapId))
            {
                return;
            }

            CommunityDiscoveryCandidateViewModel? view =
                FindDiscoveryCandidateView(beatmapId);

            if (view is null || view.IsOwned)
            {
                return;
            }

            CancellationTokenSource cancellation = new();
            discoveryDownloadCancellationsByBeatmapId[beatmapId] = cancellation;

            view.IsDownloadOperationRunning = true;
            view.DownloadStatusText = "Opening download page...";

            try
            {
                BeatmapDownloadResult downloadResult =
                    await beatmapDownloadService.DownloadAsync(
                        candidate.BeatmapSetId,
                        cancellation.Token);

                switch (downloadResult.Outcome)
                {
                    case BeatmapDownloadOutcome.BrowserFallbackOpened:
                        // Le navigateur gère lui-même le téléchargement et
                        // sa propre session : BeatInsight n'a reçu aucun
                        // octet et ne peut donc pas déclencher lui-même
                        // l'ouverture du .osz. osu! l'importera via son
                        // association de fichier une fois le téléchargement
                        // terminé par l'utilisateur.
                        view.DownloadStatusText = "Waiting for osu! import...";
                        await WaitForDiscoveryImportConfirmationAsync(
                            view,
                            candidate.BeatmapId,
                            candidate.BeatmapSetId,
                            cancellation.Token);
                        break;

                    case BeatmapDownloadOutcome.Success:
                        view.DownloadStatusText = "Importing...";
                        BeatmapImportResult launchResult =
                            beatmapImportService.LaunchImport(
                                downloadResult.LocalOszFilePath!);

                        if (launchResult.Outcome
                            != BeatmapImportOutcome.LaunchedWaitingForConfirmation)
                        {
                            view.DownloadStatusText =
                                $"Failed: {launchResult.FailureReason ?? "could not open .osz"}";
                            break;
                        }

                        view.DownloadStatusText = "Waiting for osu! import...";
                        await WaitForDiscoveryImportConfirmationAsync(
                            view,
                            candidate.BeatmapId,
                            candidate.BeatmapSetId,
                            cancellation.Token);
                        break;

                    case BeatmapDownloadOutcome.Cancelled:
                        view.DownloadStatusText = "Cancelled";
                        break;

                    default:
                        view.DownloadStatusText =
                            $"Failed: {DescribeDownloadFailure(downloadResult)}";
                        break;
                }
            }
            catch (Exception ex)
            {
                view.DownloadStatusText = "Failed: unexpected error";
                DebugLogger.Log(
                    "COMMUNITY DISCOVERY DOWNLOAD ERROR | "
                        + $"BeatmapSetId={candidate.BeatmapSetId} | "
                        + $"{ex.GetType().Name}");
                DebugLogger.Detailed(ex.ToString());
            }
            finally
            {
                view.IsDownloadOperationRunning = false;

                if (discoveryDownloadCancellationsByBeatmapId.TryGetValue(
                        beatmapId,
                        out CancellationTokenSource? tracked)
                    && ReferenceEquals(tracked, cancellation))
                {
                    discoveryDownloadCancellationsByBeatmapId.Remove(beatmapId);
                }

                cancellation.Dispose();
            }
        }

        private async Task WaitForDiscoveryImportConfirmationAsync(
            CommunityDiscoveryCandidateViewModel view,
            int beatmapId,
            int beatmapSetId,
            CancellationToken cancellationToken)
        {
            BeatmapImportResult confirmation =
                await beatmapImportService.WaitForImportConfirmationAsync(
                    beatmapId,
                    beatmapSetId,
                    ImportConfirmationMaxAttempts,
                    ct => Task.Delay(ImportConfirmationInterval, ct),
                    cancellationToken);

            switch (confirmation.Outcome)
            {
                case BeatmapImportOutcome.Confirmed:
                    // "Installé localement" (fichier présent dans Songs) ne
                    // signifie pas "déjà analysé/indexé par BeatInsight" :
                    // IsOwned/AlreadyOwned restent liés à
                    // BeatmapAnalysisRepository (voir
                    // RepositoryCommunityBeatmapLocalStateSource) et ne sont
                    // jamais modifiés ici. Ne jamais créer de faux sample ni
                    // valider un label humain automatiquement : le dataset
                    // ML n'est mis à jour que par Build Dataset / Fast
                    // Labeling, jamais ici.
                    view.IsInstalledLocally = true;
                    view.DownloadStatusText =
                        "Imported. Run Scan Library, then Refresh Library "
                            + "State to enable Load for Review.";
                    break;

                case BeatmapImportOutcome.Cancelled:
                    view.DownloadStatusText = "Cancelled";
                    break;

                default:
                    view.DownloadStatusText =
                        "Import not confirmed. Use Refresh Library State, "
                            + "or run Scan Library once osu! finishes importing.";
                    break;
            }
        }

        private static string DescribeDownloadFailure(
            BeatmapDownloadResult result) => result.Outcome switch
            {
                BeatmapDownloadOutcome.RateLimited =>
                    "osu! rate limit reached. Please retry shortly.",
                BeatmapDownloadOutcome.AuthenticationRequired =>
                    "Authentication required.",
                BeatmapDownloadOutcome.ProviderUnavailable =>
                    result.FailureReason ?? "download provider unavailable.",
                BeatmapDownloadOutcome.InvalidDownloadedFile =>
                    result.FailureReason ?? "downloaded file was invalid.",
                _ => result.FailureReason ?? "download failed.",
            };

        private void CancelDiscoveryDownload_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: int beatmapId }
                || !discoveryDownloadCancellationsByBeatmapId.TryGetValue(
                    beatmapId,
                    out CancellationTokenSource? cancellation))
            {
                return;
            }

            cancellation.Cancel();
        }

        private void CancelAllDiscoveryDownloads()
        {
            foreach (CancellationTokenSource cancellation in
                discoveryDownloadCancellationsByBeatmapId.Values)
            {
                cancellation.Cancel();
            }

            discoveryDownloadCancellationsByBeatmapId.Clear();
        }

        /// <summary>
        /// Relit l'état local (AlreadyOwned/AlreadyInMlDataset) des
        /// candidats déjà affichés, sans relancer de recherche ni de scan
        /// de bibliothèque. C'est la seule voie de confirmation offerte
        /// lorsque le sondage borné après import n'a rien trouvé.
        /// </summary>
        private void RefreshDiscoveryLocalState_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (discoveryCandidatesByBeatmapId.Count == 0
                || DiscoveryResultsList.ItemsSource
                    is not CommunityDiscoveryCandidateViewModel[] views)
            {
                return;
            }

            try
            {
                localAnalysisRepository.EnsureSchema();
                mlDatasetRepository.EnsureSchema();

                var localStateSource = new RepositoryCommunityBeatmapLocalStateSource(
                    mlDatasetRepository,
                    localAnalysisRepository);

                IReadOnlyDictionary<int, CommunityBeatmapLocalState> states =
                    localStateSource.GetStates(
                        discoveryCandidatesByBeatmapId.Keys.ToArray());

                foreach (CommunityDiscoveryCandidateViewModel view in views)
                {
                    if (!states.TryGetValue(
                            view.BeatmapId,
                            out CommunityBeatmapLocalState state))
                    {
                        continue;
                    }

                    view.IsOwned = state.AlreadyOwned;
                    view.AlreadyOwned =
                        $"Already owned: {(state.AlreadyOwned ? "Yes" : "No")}";
                    view.AlreadyInMlDataset =
                        $"In ML Dataset: {(state.AlreadyInMlDataset ? "Yes" : "No")}";
                }

                DiscoveryStatusText.Text = "Local state refreshed.";
            }
            catch (Exception ex)
            {
                DiscoveryStatusText.Text = "Unable to refresh local state.";
                DebugLogger.Log(
                    $"COMMUNITY DISCOVERY REFRESH ERROR | {ex.Message}");
                DebugLogger.Detailed(ex.ToString());
            }
        }

        private CommunityDiscoveryCandidateViewModel? FindDiscoveryCandidateView(
            int beatmapId) =>
            DiscoveryResultsList.ItemsSource
                is CommunityDiscoveryCandidateViewModel[] views
                ? views.FirstOrDefault(view => view.BeatmapId == beatmapId)
                : null;

        private void SetDiscoverySearchState(bool isRunning)
        {
            isDiscoveryRunning = isRunning;

            FindDiscoveryCandidatesButton.IsEnabled = !isRunning &&
                !IsBackgroundLibraryWorkRunning;
            CancelDiscoveryButton.IsEnabled = isRunning;
            DiscoveryJumpFamilyButton.IsEnabled = !isRunning;
            DiscoveryStreamFamilyButton.IsEnabled = !isRunning;
            DiscoveryTechFamilyButton.IsEnabled = !isRunning;
            DiscoveryReadingFamilyButton.IsEnabled = !isRunning;
            DiscoveryHybridFamilyButton.IsEnabled = !isRunning;
            Discovery10ResultsButton.IsEnabled = !isRunning;
            Discovery20ResultsButton.IsEnabled = !isRunning;
            Discovery30ResultsButton.IsEnabled = !isRunning;
            Discovery50ResultsButton.IsEnabled = !isRunning;
            DiscoveryMinStarTextBox.IsEnabled = !isRunning;
            DiscoveryMaxStarTextBox.IsEnabled = !isRunning;
        }

        private void ApplyDiscoveryFilterVisuals()
        {
            SetDiscoveryChoiceSelected(
                DiscoveryJumpFamilyButton,
                selectedDiscoveryFamily == CommunitySamplingFamily.Jump);
            SetDiscoveryChoiceSelected(
                DiscoveryStreamFamilyButton,
                selectedDiscoveryFamily == CommunitySamplingFamily.Stream);
            SetDiscoveryChoiceSelected(
                DiscoveryTechFamilyButton,
                selectedDiscoveryFamily == CommunitySamplingFamily.Tech);
            SetDiscoveryChoiceSelected(
                DiscoveryReadingFamilyButton,
                selectedDiscoveryFamily == CommunitySamplingFamily.Reading);
            SetDiscoveryChoiceSelected(
                DiscoveryHybridFamilyButton,
                selectedDiscoveryFamily == CommunitySamplingFamily.Hybrid);
            SetDiscoveryChoiceSelected(
                Discovery10ResultsButton,
                selectedDiscoveryResultCount == 10);
            SetDiscoveryChoiceSelected(
                Discovery20ResultsButton,
                selectedDiscoveryResultCount == 20);
            SetDiscoveryChoiceSelected(
                Discovery30ResultsButton,
                selectedDiscoveryResultCount == 30);
            SetDiscoveryChoiceSelected(
                Discovery50ResultsButton,
                selectedDiscoveryResultCount == 50);
        }

        private static void SetDiscoveryChoiceSelected(
            Button button,
            bool isSelected)
        {
            button.FontWeight = isSelected
                ? FontWeights.Bold
                : FontWeights.Normal;
            button.Background = isSelected
                ? System.Windows.Media.Brushes.SteelBlue
                : SystemColors.ControlBrush;
        }

        // ============================================================
        // FAST LABELING
        // ============================================================

        private void RefreshFastLabelingProgress()
        {
            try
            {
                mlDatasetRepository.EnsureSchema();

                int validated = mlDatasetRepository.CountValidated();
                int unlabeled = mlDatasetRepository.CountUnlabeled();
                int total = validated + unlabeled;

                FastLabelingProgressText.Text =
                    $"Progress: {validated:N0} / {total:N0}";
                FastLabelingRemainingText.Text =
                    $"Remaining: {unlabeled:N0}";
            }
            catch (Exception ex)
            {
                FastLabelingProgressText.Text = "Progress: 0 / 0";
                FastLabelingRemainingText.Text = "Remaining: 0";

                DebugLogger.Log($"FAST LABELING PROGRESS ERROR | {ex.Message}");
                DebugLogger.Detailed(ex.ToString());
            }
        }

        private async void PreviousUnlabeled_Click(
            object sender,
            RoutedEventArgs e)
        {
            await NavigateToUnlabeledAsync(forward: false);
        }

        private async void NextUnlabeled_Click(
            object sender,
            RoutedEventArgs e)
        {
            await NavigateToUnlabeledAsync(forward: true);
        }

        /// <summary>
        /// Avance ou recule dans la file des échantillons non validés,
        /// sans jamais écrire ni recréer de sample. Le pipeline
        /// d'analyse existant est réutilisé pour afficher la map
        /// correspondante ; un fichier source manquant ou illisible
        /// dégrade proprement plutôt que de faire échouer la fenêtre.
        /// </summary>
        private async Task NavigateToUnlabeledAsync(bool forward)
        {
            if (IsBackgroundLibraryWorkRunning || isNavigatingFastLabeling)
                return;

            isNavigatingFastLabeling = true;
            SetFastLabelingMode(true);
            activeLabelingQueue = LabelingQueueKind.FastUnlabeled;

            try
            {
                mlDatasetRepository.EnsureSchema();

                MlDatasetSample? sample = forward
                    ? mlDatasetRepository.FindNextUnlabeled(
                        currentHumanLabelSampleId)
                    : mlDatasetRepository.FindPreviousUnlabeled(
                        currentHumanLabelSampleId);

                if (sample is null)
                {
                    RefreshFastLabelingProgress();
                    return;
                }

                Beatmap beatmap;

                try
                {
                    beatmap = await Task.Run(() =>
                        analysisCache.GetOrAnalyze(sample.SourceFilePath));
                }
                catch (Exception ex)
                {
                    DebugLogger.Log(
                        $"FAST LABELING LOAD ERROR | {ex.Message}");
                    DebugLogger.Detailed(ex.ToString());

                    HumanLabelSampleStatusText.Text =
                        "Dataset sample: Unavailable (file not found)";
                    return;
                }

                if (IsBackgroundLibraryWorkRunning)
                    return;

                currentMapPath = sample.SourceFilePath;
                currentBeatmapUrl = sample.BeatmapId is int beatmapId
                    ? $"https://osu.ppy.sh/b/{beatmapId}"
                    : null;
                BackgroundImage.Source = null;

                DataContext = beatmap;

                RefreshHumanLabelPanel(beatmap, sample.SourceFilePath);
                RefreshFastLabelingProgress();
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"FAST LABELING NAV ERROR | {ex.Message}");
                DebugLogger.Detailed(ex.ToString());
            }
            finally
            {
                isNavigatingFastLabeling = false;
            }
        }

        /// <summary>
        /// Active ou désactive le gel de la beatmap affichée face au
        /// polling tosu. La sortie est toujours explicite (bouton Exit
        /// Fast Labeling) : aucune navigation ne désactive le mode
        /// elle-même.
        /// </summary>
        private void SetFastLabelingMode(bool active)
        {
            isFastLabelingMode = active;

            FastLabelingModeText.Text = active ? "Mode: On" : "Mode: Off";
            ExitFastLabelingButton.Content = "Exit Fast Labeling";
            ExitFastLabelingButton.IsEnabled = active;
        }

        /// <summary>
        /// Même gel anti-tosu que les files Fast/Calibration, avec un libellé
        /// explicite pour éviter de faire croire qu'une revue Discovery est
        /// entrée dans la Fast Unlabeled Queue.
        /// </summary>
        private void SetDiscoveryReviewMode()
        {
            isFastLabelingMode = true;
            FastLabelingModeText.Text = "Mode: Review";
            ExitFastLabelingButton.Content = "Exit Review";
            ExitFastLabelingButton.IsEnabled = true;
        }

        private void ExitFastLabeling_Click(
            object sender,
            RoutedEventArgs e)
        {
            SetFastLabelingMode(false);
            activeLabelingQueue = LabelingQueueKind.None;
        }

        // ============================================================
        // CALIBRATION QUEUE
        // ============================================================

        private static IReadOnlyList<int> CalibrationPackBeatmapIds { get; } =
            CalibrationPack.Pack1
                .Select(entry => entry.BeatmapId)
                .ToList();

        /// <summary>
        /// Recharge le pack de calibration depuis le repository et le
        /// réordonne selon <see cref="CalibrationPack.Pack1"/>. N'écrit
        /// jamais rien : uniquement une lecture pour naviguer ou afficher
        /// des compteurs.
        /// </summary>
        private IReadOnlyList<MlDatasetSample> LoadOrderedCalibrationSamples()
        {
            mlDatasetRepository.EnsureSchema();

            IReadOnlyList<MlDatasetSample> matches =
                mlDatasetRepository.FindCalibrationSamples(
                    CalibrationPackBeatmapIds);

            return CalibrationQueue.OrderByPackSequence(
                CalibrationPack.Pack1,
                matches);
        }

        private void RefreshCalibrationQueueProgress()
        {
            try
            {
                IReadOnlyList<MlDatasetSample> ordered =
                    LoadOrderedCalibrationSamples();

                int found = ordered.Count;
                int validated = ordered.Count(sample => sample.HumanValidated);
                int remaining = found - validated;

                CalibrationFoundText.Text =
                    $"Found: {found:N0} / {CalibrationPack.Pack1.Count:N0}";
                CalibrationValidatedText.Text = $"Validated: {validated:N0}";
                CalibrationRemainingText.Text = $"Remaining: {remaining:N0}";
            }
            catch (Exception ex)
            {
                CalibrationFoundText.Text =
                    $"Found: 0 / {CalibrationPack.Pack1.Count:N0}";
                CalibrationValidatedText.Text = "Validated: 0";
                CalibrationRemainingText.Text = "Remaining: 0";

                DebugLogger.Log($"CALIBRATION QUEUE STATS ERROR | {ex.Message}");
                DebugLogger.Detailed(ex.ToString());
            }
        }

        private async void PreviousCalibration_Click(
            object sender,
            RoutedEventArgs e)
        {
            await NavigateCalibrationAsync(forward: false);
        }

        private async void NextCalibration_Click(
            object sender,
            RoutedEventArgs e)
        {
            await NavigateCalibrationAsync(forward: true);
        }

        /// <summary>
        /// Avance ou recule dans la Calibration Queue, en ignorant
        /// toujours les échantillons déjà validés. Réutilise le même gel
        /// anti-tosu que la Fast Unlabeled Queue, mais avec sa propre
        /// ancre de position (<see cref="currentCalibrationSampleId"/>) :
        /// les deux files restent des modes de navigation distincts.
        /// </summary>
        private async Task NavigateCalibrationAsync(bool forward)
        {
            if (IsBackgroundLibraryWorkRunning || isNavigatingCalibration)
                return;

            isNavigatingCalibration = true;
            SetFastLabelingMode(true);
            activeLabelingQueue = LabelingQueueKind.Calibration;

            try
            {
                IReadOnlyList<MlDatasetSample> ordered =
                    LoadOrderedCalibrationSamples();

                MlDatasetSample? sample = forward
                    ? CalibrationQueue.FindNextUnvalidated(
                        ordered,
                        currentCalibrationSampleId)
                    : CalibrationQueue.FindPreviousUnvalidated(
                        ordered,
                        currentCalibrationSampleId);

                if (sample is null)
                {
                    RefreshCalibrationQueueProgress();
                    return;
                }

                Beatmap beatmap;

                try
                {
                    beatmap = await Task.Run(() =>
                        analysisCache.GetOrAnalyze(sample.SourceFilePath));
                }
                catch (Exception ex)
                {
                    DebugLogger.Log(
                        $"CALIBRATION QUEUE LOAD ERROR | {ex.Message}");
                    DebugLogger.Detailed(ex.ToString());

                    HumanLabelSampleStatusText.Text =
                        "Dataset sample: Unavailable (file not found)";
                    return;
                }

                if (IsBackgroundLibraryWorkRunning)
                    return;

                currentMapPath = sample.SourceFilePath;
                currentBeatmapUrl = sample.BeatmapId is int beatmapId
                    ? $"https://osu.ppy.sh/b/{beatmapId}"
                    : null;
                BackgroundImage.Source = null;

                DataContext = beatmap;

                currentCalibrationSampleId = sample.SampleId;

                RefreshHumanLabelPanel(beatmap, sample.SourceFilePath);
                RefreshCalibrationSamplingDisplay(sample);
                RefreshCalibrationQueueProgress();
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"CALIBRATION QUEUE NAV ERROR | {ex.Message}");
                DebugLogger.Detailed(ex.ToString());
            }
            finally
            {
                isNavigatingCalibration = false;
            }
        }

        /// <summary>
        /// Affiche le bucket du pack (purement informatif : voir
        /// <see cref="CalibrationPackBucket"/>) et un résumé best-effort
        /// de Community Evidence, sans jamais toucher aux champs humains.
        /// </summary>
        private void RefreshCalibrationSamplingDisplay(MlDatasetSample sample)
        {
            CalibrationPackEntry? entry = sample.BeatmapId is int beatmapId
                ? CalibrationPack.Pack1
                    .Where(candidate => candidate.BeatmapId == beatmapId)
                    .Cast<CalibrationPackEntry?>()
                    .FirstOrDefault()
                : null;

            CalibrationHintText.Text = entry is null
                ? "Pack hint: —"
                : $"Pack hint: {entry.Value.Bucket} (sampling hint only)";

            CalibrationCommunityText.Text =
                FormatCommunityEvidenceSummary(sample.CommunityEvidenceJson);
        }

        private static string FormatCommunityEvidenceSummary(
            string? communityEvidenceJson)
        {
            if (string.IsNullOrWhiteSpace(communityEvidenceJson))
            {
                return "Community: unavailable";
            }

            try
            {
                using JsonDocument document =
                    JsonDocument.Parse(communityEvidenceJson);

                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return "Community: unavailable";
                }

                List<string> parts = [];

                foreach (JsonProperty property in
                    document.RootElement.EnumerateObject())
                {
                    if (parts.Count >= 3)
                        break;

                    parts.Add($"{property.Name}={property.Value}");
                }

                return parts.Count == 0
                    ? "Community: unavailable"
                    : $"Community: {string.Join(", ", parts)}";
            }
            catch (JsonException)
            {
                return "Community: unavailable";
            }
        }

        // ============================================================
        // HUMAN LABEL
        // ============================================================

        private void RefreshHumanLabelPanel(
            Beatmap beatmap,
            string sourceFilePath)
        {
            ArgumentNullException.ThrowIfNull(beatmap);
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

            GameplayIdentity identity = beatmap.GameplayProfile.Identity;

            HumanLabelIdentityPrimaryText.Text =
                $"Primary: {DisplayOrDash(identity.Primary)}";
            HumanLabelIdentitySecondaryText.Text =
                $"Secondary: {DisplayOrDash(identity.Secondary)}";
            HumanLabelIdentityConfidenceText.Text =
                $"Confidence: {identity.Confidence:F0}%";

            currentHumanLabelSourceFilePath = sourceFilePath;
            currentHumanLabelSampleId = null;
            hasCurrentDatasetSample = false;
            currentPrimaryHumanLabel = null;
            currentSecondaryHumanLabel = null;

            // Une map nouvellement chargée ne reçoit jamais une sélection
            // automatique, y compris quand elle possède déjà une annotation
            // humaine ou une Identity BeatInsight forte.
            selectedPrimaryHumanLabel = null;
            selectedSecondaryHumanLabel = null;

            // Le hint de pack et le résumé Community sont propres à la
            // Calibration Queue : ils sont réinitialisés à chaque
            // chargement de map et republiés par
            // RefreshCalibrationSamplingDisplay uniquement lorsque la
            // navigation vient de cette file.
            CalibrationHintText.Text = "Pack hint: —";
            CalibrationCommunityText.Text = "Community: unavailable";

            try
            {
                mlDatasetRepository.EnsureSchema();

                MlDatasetSample? sample =
                    mlDatasetRepository.FindBySourceFilePath(sourceFilePath);

                if (sample is null)
                {
                    HumanLabelSampleStatusText.Text = "Dataset sample: Missing";
                    HumanLabelCurrentPrimaryText.Text = "Primary: Unlabeled";
                    HumanLabelCurrentSecondaryText.Text = "Secondary: —";
                    UpdateHumanLabelActionState();
                    return;
                }

                hasCurrentDatasetSample = true;
                currentHumanLabelSampleId = sample.SampleId;
                currentPrimaryHumanLabel = sample.PrimaryHumanLabel;
                currentSecondaryHumanLabel = sample.SecondaryHumanLabel;

                HumanLabelSampleStatusText.Text = "Dataset sample: Ready";
                HumanLabelCurrentPrimaryText.Text = sample.PrimaryHumanLabel is null
                    ? "Primary: Unlabeled"
                    : $"Primary: {FormatHumanLabel(sample.PrimaryHumanLabel.Value)}"
                        + (sample.HumanValidated ? " ✓" : "");
                HumanLabelCurrentSecondaryText.Text = sample.SecondaryHumanLabel is null
                    ? "Secondary: —"
                    : $"Secondary: {FormatHumanLabel(sample.SecondaryHumanLabel.Value)}";
            }
            catch (Exception ex)
            {
                HumanLabelSampleStatusText.Text = "Dataset sample: Unavailable";
                HumanLabelCurrentPrimaryText.Text = "Primary: Unlabeled";
                HumanLabelCurrentSecondaryText.Text = "Secondary: —";

                DebugLogger.Log($"HUMAN LABEL LOAD ERROR | {ex.Message}");
                DebugLogger.Detailed(ex.ToString());
            }

            UpdateHumanLabelActionState();
        }

        private void RefreshHumanLabelPanelForCurrentBeatmap()
        {
            if (DataContext is Beatmap beatmap &&
                !string.IsNullOrWhiteSpace(currentMapPath))
            {
                RefreshHumanLabelPanel(beatmap, currentMapPath);
            }
        }

        private void SelectPrimaryHumanLabel_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!hasCurrentDatasetSample || IsBackgroundLibraryWorkRunning ||
                sender is not FrameworkElement { Tag: string labelName } ||
                !Enum.TryParse(labelName, out MlHumanLabel humanLabel))
            {
                return;
            }

            SetSelectedPrimaryHumanLabel(humanLabel);
        }

        private void SelectSecondaryHumanLabel_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!hasCurrentDatasetSample || IsBackgroundLibraryWorkRunning ||
                sender is not FrameworkElement { Tag: string labelName })
            {
                return;
            }

            if (string.IsNullOrEmpty(labelName))
            {
                SetSelectedSecondaryHumanLabel(null);
                return;
            }

            if (!Enum.TryParse(labelName, out MlHumanLabel humanLabel))
            {
                return;
            }

            SetSelectedSecondaryHumanLabel(humanLabel);
        }

        private void SetSelectedPrimaryHumanLabel(MlHumanLabel humanLabel)
        {
            selectedPrimaryHumanLabel = humanLabel;

            // Le label secondaire ne peut jamais être égal au primaire :
            // un changement de primaire invalide un secondaire identique.
            if (selectedSecondaryHumanLabel == humanLabel)
            {
                selectedSecondaryHumanLabel = null;
            }

            UpdateHumanLabelActionState();
        }

        /// <summary>
        /// null sélectionne explicitement "None" (secondaire absent). Une
        /// combinaison égale au primaire actuellement sélectionné est
        /// ignorée proprement plutôt que d'écrire un état invalide.
        /// </summary>
        private void SetSelectedSecondaryHumanLabel(MlHumanLabel? humanLabel)
        {
            if (humanLabel.HasValue &&
                humanLabel.Value == selectedPrimaryHumanLabel)
            {
                return;
            }

            selectedSecondaryHumanLabel = humanLabel;
            UpdateHumanLabelActionState();
        }

        private async void ValidateHumanLabel_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!hasCurrentDatasetSample ||
                selectedPrimaryHumanLabel is not MlHumanLabel primaryHumanLabel ||
                string.IsNullOrWhiteSpace(currentHumanLabelSourceFilePath) ||
                DataContext is not Beatmap beatmap ||
                IsBackgroundLibraryWorkRunning)
            {
                return;
            }

            try
            {
                // Cette opération SQL ne touche qu'aux deux champs humains
                // du sample existant et ne peut pas créer de faux sample.
                bool updated = mlDatasetRepository.UpdateHumanLabels(
                    currentHumanLabelSourceFilePath,
                    primaryHumanLabel,
                    selectedSecondaryHumanLabel);

                if (!updated)
                {
                    RefreshHumanLabelPanel(
                        beatmap,
                        currentHumanLabelSourceFilePath);
                    return;
                }

                RefreshHumanLabelPanel(
                    beatmap,
                    currentHumanLabelSourceFilePath);
                RefreshDatasetStatistics();
                RefreshFastLabelingProgress();
                RefreshCalibrationQueueProgress();

                // La file active (si on y est entré via Next/Previous/
                // Space/auto-advance) détermine où Validate avance
                // automatiquement. Hors de toute file (map tosu normale),
                // Validate ne navigue nulle part.
                if (LabelingQueueNavigationPolicy.ShouldAutoAdvanceAfterValidation(
                    activeLabelingQueue))
                {
                    switch (activeLabelingQueue)
                    {
                        case LabelingQueueKind.Calibration:
                            await NavigateCalibrationAsync(forward: true);
                            break;

                        case LabelingQueueKind.FastUnlabeled:
                            await NavigateToUnlabeledAsync(forward: true);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"HUMAN LABEL SAVE ERROR | {ex.Message}");
                DebugLogger.Detailed(ex.ToString());
            }
        }

        private void ClearHumanLabel_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!hasCurrentDatasetSample ||
                string.IsNullOrWhiteSpace(currentHumanLabelSourceFilePath) ||
                DataContext is not Beatmap beatmap ||
                IsBackgroundLibraryWorkRunning)
            {
                return;
            }

            try
            {
                bool cleared = mlDatasetRepository.ClearHumanLabel(
                    currentHumanLabelSourceFilePath);

                if (!cleared)
                {
                    RefreshHumanLabelPanel(
                        beatmap,
                        currentHumanLabelSourceFilePath);
                    return;
                }

                RefreshHumanLabelPanel(
                    beatmap,
                    currentHumanLabelSourceFilePath);
                RefreshDatasetStatistics();
                RefreshFastLabelingProgress();
                RefreshCalibrationQueueProgress();
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"HUMAN LABEL CLEAR ERROR | {ex.Message}");
                DebugLogger.Detailed(ex.ToString());
            }
        }

        /// <summary>
        /// Raccourcis Fast Labeling. Ignorés lorsque le focus est dans un
        /// champ texte (aucun n'existe actuellement dans MainWindow, mais
        /// la garde reste défensive) ou pendant un travail de bibliothèque
        /// en arrière-plan.
        /// </summary>
        private async void MainWindow_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (IsBackgroundLibraryWorkRunning || IsFocusInTextInput())
                return;

            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

            if (!shift &&
                HumanLabelHotkeys.TryMapPrimaryKey(e.Key, out MlHumanLabel primary))
            {
                if (!hasCurrentDatasetSample)
                    return;

                SetSelectedPrimaryHumanLabel(primary);
                e.Handled = true;
                return;
            }

            if (shift &&
                HumanLabelHotkeys.TryMapSecondaryKey(
                    e.Key,
                    out bool isNone,
                    out MlHumanLabel secondary))
            {
                if (!hasCurrentDatasetSample)
                    return;

                SetSelectedSecondaryHumanLabel(isNone ? null : secondary);
                e.Handled = true;
                return;
            }

            switch (e.Key)
            {
                case Key.Enter:
                    ValidateHumanLabel_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;

                case Key.Space:
                    e.Handled = true;

                    // DiscoveryReview ne saute vers aucune file. Pour les
                    // deux files historiques (et hors file), le comportement
                    // existant reste préservé par la policy dédiée.
                    switch (LabelingQueueNavigationPolicy.GetSkipTarget(
                        activeLabelingQueue))
                    {
                        case LabelingQueueKind.Calibration:
                            await NavigateCalibrationAsync(forward: true);
                            break;

                        case LabelingQueueKind.FastUnlabeled:
                            await NavigateToUnlabeledAsync(forward: true);
                            break;
                    }

                    break;

                case Key.Back:
                    ClearHumanLabel_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
            }
        }

        private static bool IsFocusInTextInput()
        {
            return Keyboard.FocusedElement is TextBoxBase or PasswordBox;
        }

        private void UpdateHumanLabelActionState()
        {
            bool canLabel = hasCurrentDatasetSample &&
                !IsBackgroundLibraryWorkRunning;

            HumanLabelPrimaryStreamButton.IsEnabled = canLabel;
            HumanLabelPrimaryJumpButton.IsEnabled = canLabel;
            HumanLabelPrimaryTechButton.IsEnabled = canLabel;
            HumanLabelPrimaryClassicMixedButton.IsEnabled = canLabel;

            HumanLabelSecondaryNoneButton.IsEnabled = canLabel;
            HumanLabelSecondaryStreamButton.IsEnabled = canLabel &&
                selectedPrimaryHumanLabel != MlHumanLabel.Stream;
            HumanLabelSecondaryJumpButton.IsEnabled = canLabel &&
                selectedPrimaryHumanLabel != MlHumanLabel.Jump;
            HumanLabelSecondaryTechButton.IsEnabled = canLabel &&
                selectedPrimaryHumanLabel != MlHumanLabel.Tech;
            HumanLabelSecondaryClassicMixedButton.IsEnabled = canLabel &&
                selectedPrimaryHumanLabel != MlHumanLabel.ClassicMixed;

            ValidateHumanLabelButton.IsEnabled = canLabel &&
                selectedPrimaryHumanLabel.HasValue;
            ClearHumanLabelButton.IsEnabled = canLabel &&
                (currentPrimaryHumanLabel.HasValue ||
                    currentSecondaryHumanLabel.HasValue);

            ApplyHumanLabelSelectionVisuals();
        }

        private void ApplyHumanLabelSelectionVisuals()
        {
            SetHumanLabelButtonSelected(
                HumanLabelPrimaryStreamButton,
                selectedPrimaryHumanLabel == MlHumanLabel.Stream);
            SetHumanLabelButtonSelected(
                HumanLabelPrimaryJumpButton,
                selectedPrimaryHumanLabel == MlHumanLabel.Jump);
            SetHumanLabelButtonSelected(
                HumanLabelPrimaryTechButton,
                selectedPrimaryHumanLabel == MlHumanLabel.Tech);
            SetHumanLabelButtonSelected(
                HumanLabelPrimaryClassicMixedButton,
                selectedPrimaryHumanLabel == MlHumanLabel.ClassicMixed);

            SetHumanLabelButtonSelected(
                HumanLabelSecondaryNoneButton,
                selectedSecondaryHumanLabel is null);
            SetHumanLabelButtonSelected(
                HumanLabelSecondaryStreamButton,
                selectedSecondaryHumanLabel == MlHumanLabel.Stream);
            SetHumanLabelButtonSelected(
                HumanLabelSecondaryJumpButton,
                selectedSecondaryHumanLabel == MlHumanLabel.Jump);
            SetHumanLabelButtonSelected(
                HumanLabelSecondaryTechButton,
                selectedSecondaryHumanLabel == MlHumanLabel.Tech);
            SetHumanLabelButtonSelected(
                HumanLabelSecondaryClassicMixedButton,
                selectedSecondaryHumanLabel == MlHumanLabel.ClassicMixed);
        }

        private static void SetHumanLabelButtonSelected(
            Button button,
            bool isSelected)
        {
            button.FontWeight = isSelected
                ? FontWeights.Bold
                : FontWeights.Normal;
            button.Background = isSelected
                ? System.Windows.Media.Brushes.SteelBlue
                : SystemColors.ControlBrush;
        }

        private static string DisplayOrDash(string value) =>
            string.IsNullOrWhiteSpace(value) ? "—" : value;

        private static string FormatHumanLabel(MlHumanLabel humanLabel) =>
            humanLabel switch
            {
                MlHumanLabel.Stream => "Stream",
                MlHumanLabel.Jump => "Jump",
                MlHumanLabel.Tech => "Tech",
                MlHumanLabel.ClassicMixed => "Classic/Mixed",
                _ => throw new ArgumentOutOfRangeException(
                    nameof(humanLabel),
                    humanLabel,
                    "Unsupported ML human label."),
            };

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
                RefreshHumanLabelPanelForCurrentBeatmap();
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
            bool otherWorkRunning =
                isLibraryScanRunning || isBeatmapIdBackfillRunning;

            ChangeSongsFolderButton.IsEnabled =
                !isBuilding && !otherWorkRunning;
            ScanLibraryButton.IsEnabled =
                !isBuilding && !otherWorkRunning;
            BuildDatasetButton.IsEnabled =
                !isBuilding && !otherWorkRunning;
            BackfillBeatmapIdsButton.IsEnabled =
                !isBuilding && !otherWorkRunning;
            CancelDatasetBuildButton.Visibility = isBuilding
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (isBuilding)
                CancelDatasetBuildButton.IsEnabled = true;

            UpdateHumanLabelActionState();
        }

        private void SetBeatmapIdBackfillControls(bool isRunning)
        {
            bool otherWorkRunning =
                isLibraryScanRunning || isDatasetBuildRunning;

            ChangeSongsFolderButton.IsEnabled =
                !isRunning && !otherWorkRunning;
            ScanLibraryButton.IsEnabled =
                !isRunning && !otherWorkRunning;
            BuildDatasetButton.IsEnabled =
                !isRunning && !otherWorkRunning;
            BackfillBeatmapIdsButton.IsEnabled =
                !isRunning && !otherWorkRunning;
            CancelBeatmapIdBackfillButton.Visibility = isRunning
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (isRunning)
                CancelBeatmapIdBackfillButton.IsEnabled = true;

            UpdateHumanLabelActionState();
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

        // ============================================================
        // BEATMAP ID BACKFILL
        // ============================================================

        private async void BackfillBeatmapIds_Click(
            object sender,
            RoutedEventArgs e)
        {
            await StartBeatmapIdBackfillAsync();
        }

        private async Task StartBeatmapIdBackfillAsync()
        {
            if (IsBackgroundLibraryWorkRunning)
                return;

            CancellationTokenSource cancellation = new();
            beatmapIdBackfillCancellation = cancellation;
            isBeatmapIdBackfillRunning = true;
            acceptsBeatmapIdBackfillProgress = true;

            SetBeatmapIdBackfillControls(isRunning: true);
            ShowBeatmapIdBackfillPreparingState();

            try
            {
                // Une mise à jour Tosu déjà lancée termine avant le backfill,
                // exactement comme pour StartDatasetBuildAsync : aucun appel
                // Community/API concurrent pendant l'écriture SQLite.
                while (isUpdating)
                    await Task.Delay(50);

                cancellation.Token.ThrowIfCancellationRequested();

                IProgress<BeatmapIdBackfillProgress> progress =
                    new Progress<BeatmapIdBackfillProgress>(
                        UpdateBeatmapIdBackfillProgress);

                BeatmapIdBackfillService backfillService =
                    new(mlDatasetRepository);

                BeatmapIdBackfillResult result = await Task.Run(() =>
                    backfillService.Run(progress, cancellation.Token));

                acceptsBeatmapIdBackfillProgress = false;
                ShowBeatmapIdBackfillSummary(result);
            }
            catch (OperationCanceledException)
                when (cancellation.IsCancellationRequested)
            {
                acceptsBeatmapIdBackfillProgress = false;
                ShowBeatmapIdBackfillCancelledWithoutResult();
            }
            catch (Exception ex)
            {
                acceptsBeatmapIdBackfillProgress = false;

                DebugLogger.Log($"BEATMAP ID BACKFILL ERROR | {ex.Message}");
                DebugLogger.Detailed(ex.ToString());

                ShowBeatmapIdBackfillFailure(ex);
            }
            finally
            {
                isBeatmapIdBackfillRunning = false;
                acceptsBeatmapIdBackfillProgress = false;

                if (ReferenceEquals(beatmapIdBackfillCancellation, cancellation))
                {
                    beatmapIdBackfillCancellation = null;
                    cancellation.Dispose();
                }

                // Le backfill ne relance jamais MlDatasetBuilder : seules les
                // statistiques dépendant de BeatmapId doivent être rafraîchies.
                RefreshDatasetStatistics();
                RefreshCalibrationQueueProgress();
                SetBeatmapIdBackfillControls(isRunning: false);
                RefreshHumanLabelPanelForCurrentBeatmap();
            }
        }

        private void CancelBeatmapIdBackfill_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (beatmapIdBackfillCancellation is not {
                IsCancellationRequested: false
            } cancellation)
            {
                return;
            }

            cancellation.Cancel();
            CancelBeatmapIdBackfillButton.IsEnabled = false;
            BeatmapIdBackfillStatusText.Text = "Cancelling backfill...";
        }

        private void ShowBeatmapIdBackfillPreparingState()
        {
            BeatmapIdBackfillPanel.Visibility = Visibility.Visible;
            BeatmapIdBackfillStatusText.Text = "Backfilling Beatmap IDs...";
            BeatmapIdBackfillProgressBar.Value = 0;
            BeatmapIdBackfillUpdatedText.Text = "Updated: 0";
            BeatmapIdBackfillAlreadyPopulatedText.Text =
                "Already populated: 0";
            BeatmapIdBackfillMissingInvalidText.Text = "Missing/invalid: 0";
            BeatmapIdBackfillFailedText.Text = "Failed: 0";
            BeatmapIdBackfillCurrentFileText.Text = "Current: —";
            BeatmapIdBackfillCurrentFileText.ToolTip = null;
        }

        private void UpdateBeatmapIdBackfillProgress(
            BeatmapIdBackfillProgress progress)
        {
            if (!acceptsBeatmapIdBackfillProgress)
                return;

            BeatmapIdBackfillPanel.Visibility = Visibility.Visible;
            BeatmapIdBackfillStatusText.Text = "Backfilling Beatmap IDs...";
            BeatmapIdBackfillProgressBar.Value = Math.Clamp(
                progress.Percent,
                0.0,
                100.0);
            BeatmapIdBackfillUpdatedText.Text =
                $"Updated: {progress.UpdatedCount:N0}";
            BeatmapIdBackfillMissingInvalidText.Text =
                $"Missing/invalid: {progress.MissingOrInvalidCount:N0}";
            BeatmapIdBackfillFailedText.Text =
                $"Failed: {progress.FailedCount:N0}";

            string currentFile = string.IsNullOrWhiteSpace(progress.CurrentFile)
                ? "—"
                : Path.GetFileName(progress.CurrentFile);

            BeatmapIdBackfillCurrentFileText.Text = $"Current: {currentFile}";
            BeatmapIdBackfillCurrentFileText.ToolTip = progress.CurrentFile;
        }

        private void ShowBeatmapIdBackfillSummary(BeatmapIdBackfillResult result)
        {
            int processed =
                result.UpdatedCount
                    + result.MissingOrInvalidCount
                    + result.FailedCount;

            double percent = result.CandidateCount == 0
                ? 100.0
                : processed * 100.0 / result.CandidateCount;

            BeatmapIdBackfillPanel.Visibility = Visibility.Visible;
            BeatmapIdBackfillStatusText.Text = result.WasCancelled
                ? "Beatmap ID backfill cancelled"
                : "Beatmap ID backfill complete";
            BeatmapIdBackfillProgressBar.Value = Math.Clamp(percent, 0.0, 100.0);
            BeatmapIdBackfillUpdatedText.Text =
                $"Updated: {result.UpdatedCount:N0}";
            BeatmapIdBackfillAlreadyPopulatedText.Text =
                $"Already populated: {result.AlreadyPopulatedCount:N0}";
            BeatmapIdBackfillMissingInvalidText.Text =
                $"Missing/invalid: {result.MissingOrInvalidCount:N0}";
            BeatmapIdBackfillFailedText.Text =
                $"Failed: {result.FailedCount:N0}";
            BeatmapIdBackfillCurrentFileText.Text = result.WasCancelled
                ? $"Cancelled after {result.Elapsed:mm\\:ss}."
                : $"Completed in {result.Elapsed:mm\\:ss}.";
            BeatmapIdBackfillCurrentFileText.ToolTip = null;
        }

        private void ShowBeatmapIdBackfillCancelledWithoutResult()
        {
            BeatmapIdBackfillPanel.Visibility = Visibility.Visible;
            BeatmapIdBackfillStatusText.Text = "Beatmap ID backfill cancelled";
            BeatmapIdBackfillCurrentFileText.Text =
                "Cancelled before a final summary was available.";
            BeatmapIdBackfillCurrentFileText.ToolTip = null;
        }

        private void ShowBeatmapIdBackfillFailure(Exception exception)
        {
            BeatmapIdBackfillPanel.Visibility = Visibility.Visible;
            BeatmapIdBackfillStatusText.Text = "Beatmap ID backfill failed";
            BeatmapIdBackfillCurrentFileText.Text =
                $"Error: {exception.Message}";
            BeatmapIdBackfillCurrentFileText.ToolTip = exception.ToString();
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
            // Le mode Fast Labeling gèle la beatmap affichée : le timer
            // continue de tourner (aucun arrêt définitif du polling), mais
            // aucun tick ne doit écraser la map chargée manuellement tant
            // que l'utilisateur n'en est pas sorti explicitement.
            if (IsBackgroundLibraryWorkRunning || isFastLabelingMode)
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

            // Le panneau de labellisation lit uniquement l'analyse locale et
            // le sample ML existant : il reste indépendant des tags/API.
            RefreshHumanLabelPanel(beatmap, chemin);

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
