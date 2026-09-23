using AutoMapper;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.AnalysisCache;
using BeatInsight.Models.Discovery;
using BeatInsight.Models.Library;
using BeatInsight.Models.Ml;
using BeatInsight.Models.Persistence;
using BeatInsight.Parser;
using BeatInsight.Services;
using BeatInsight.Services.AnalysisCache;
using BeatInsight.Services.CommunityDiscovery;
using BeatInsight.Services.Download;
using BeatInsight.Services.Library;
using BeatInsight.Services.Ml;
using BeatInsight.Services.Persistence;
using BeatInsight.ViewModels;
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
        private long lastMapTimerTickTimestamp;
        private long mapTimerTickSequence;
        private string? pendingTosuMapPath;
        private long pendingTosuMapFirstSeenTimestamp;
        private long pendingTosuMapFileAvailableTimestamp;
        private CancellationTokenSource? mapEnrichmentCancellation;

        // Index persistant des analyses.
        //
        // Le service exécute le pipeline local inchangé en cas de miss
        // et se contente de restituer un résultat déjà calculé en cas
        // de hit. Toute défaillance de la base dégrade silencieusement
        // vers le pipeline V1.
        private readonly BeatmapAnalysisCacheService analysisCache =
            new(new BeatmapAnalysisRepository(
                BeatmapAnalysisRepository.DefaultDatabasePath));

        // UI V2 : ViewModel racine du Window (Overview/Skills/Sections/
        // Identity/Difficulty). Entièrement indépendant du pipeline V1
        // ci-dessus : celui-ci reste la seule source pour le ML Lab, la
        // labellisation et la découverte communautaire.
        private readonly MainWindowViewModel mainViewModel = new();
        private OverviewLoadCoordinator? overviewLoadCoordinator;

        // Snapshot V2 (analyzers frozen) utilisé uniquement pour peupler
        // Overview. Le cache JSON est propre à cette instance et ne
        // partage rien avec BeatmapAnalysisCacheService (V1) ni avec
        // le dataset ML.
        private readonly CachedBeatmapAnalysisService overviewAnalysisService =
            new(new JsonBeatmapAnalysisCache());

        // Dernière Beatmap V1 affichée, utilisée par CopyAnalysis_Click.
        // Remplace l'ancienne lecture via DataContext, désormais réservé
        // à mainViewModel.
        private Beatmap? currentBeatmap;

        // Index runtime consulté uniquement pour charger en revue un candidat
        // déjà installé. Il ne déclenche jamais de parcours du dossier Songs.
        private readonly BeatmapAnalysisRepository localAnalysisRepository =
            new(BeatmapAnalysisRepository.DefaultDatabasePath);

        // Dataset ML indépendant du cache runtime : cette instance ne sert
        // qu'aux statistiques et au builder de captures fraîches.
        private readonly MlDatasetSampleRepository mlDatasetRepository =
            new(MlDatasetSampleRepository.DefaultDatabasePath);

        // File de revue Tech locale : elle lit l'Identity persistée pour
        // ordonner les candidats mais n'écrit jamais de label automatiquement.
        private readonly TechHumanLabelingCandidateSelector
            techHumanLabelingCandidateSelector;

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

        // Ancre et garde de la file Tech ciblée. Elles sont indépendantes de
        // la file Fast Unlabeled afin de ne pas mélanger leurs parcours.
        private long? currentTechCandidateSampleId;
        private bool isNavigatingTechCandidates;

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

            DataContext = mainViewModel;
            overviewLoadCoordinator = new OverviewLoadCoordinator(mainViewModel.Overview);
            DashboardViewHost.ChangeSongsFolderRequested +=
                ChangeSongsFolder_Click;
            DashboardViewHost.ScanLibraryRequested +=
                ScanLibrary_Click;
            DashboardViewHost.CancelLibraryScanRequested +=
                CancelLibraryScan_Click;

            techHumanLabelingCandidateSelector =
                new TechHumanLabelingCandidateSelector(
                    mlDatasetRepository,
                    localAnalysisRepository);

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


        }

        // Si une mise à jour précédente n'est pas terminée, on quitte immédiatement pour éviter deux traitements simultanés.

        private async void MapTimer_Tick(object? sender, EventArgs e)
        {
            long tickTimestamp = Stopwatch.GetTimestamp();
            long tickSequence = ++mapTimerTickSequence;
            TimeSpan tickGap = lastMapTimerTickTimestamp == 0
                ? TimeSpan.Zero
                : Stopwatch.GetElapsedTime(
                    lastMapTimerTickTimestamp,
                    tickTimestamp);
            lastMapTimerTickTimestamp = tickTimestamp;

            DebugLogger.Log(
                $"MAP SWITCH E2E TICK | Sequence={tickSequence} | " +
                $"Utc={DateTime.UtcNow:O} | SW={tickTimestamp} | " +
                $"Gap={tickGap.TotalMilliseconds:F1} ms | " +
                $"IsUpdating={isUpdating} | " +
                $"BackgroundWork={IsBackgroundLibraryWorkRunning}");

            if (isUpdating || IsBackgroundLibraryWorkRunning)
            {
                DebugLogger.Log(
                    $"MAP SWITCH E2E TICK SKIPPED | " +
                    $"Sequence={tickSequence} | SW={Stopwatch.GetTimestamp()} | " +
                    $"Reason={(isUpdating ? "IsUpdating" : "BackgroundWork")}");
                return;
            }

            isUpdating = true;

            try
            {
                await UpdateMap(tickGap, tickSequence, tickTimestamp);
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
                long completedTimestamp = Stopwatch.GetTimestamp();
                DebugLogger.Log(
                    $"MAP SWITCH E2E TICK COMPLETE | " +
                    $"Sequence={tickSequence} | SW={completedTimestamp} | " +
                    $"Duration={Stopwatch.GetElapsedTime(tickTimestamp, completedTimestamp).TotalMilliseconds:F1} ms");
            }
        }

        private void CopyAnalysis_Click(object sender, RoutedEventArgs e)
        {
            if (currentBeatmap is not { } beatmap)
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
            mainViewModel.Library.SetSongsFolder(resolved);
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
            mainViewModel.Library.ShowCancelling();
            SyncLegacyLibraryPanel();
        }

        private void SetLibraryScanControls(bool isScanning)
        {
            bool otherWorkRunning =
                isDatasetBuildRunning || isBeatmapIdBackfillRunning;

            mainViewModel.Library.SetControls(
                isScanning,
                otherWorkRunning);

            ChangeSongsFolderButton.IsEnabled =
                mainViewModel.Library.CanChangeFolder;
            ScanLibraryButton.IsEnabled =
                mainViewModel.Library.CanScan;
            BuildDatasetButton.IsEnabled =
                !isScanning && !otherWorkRunning;
            BackfillBeatmapIdsButton.IsEnabled =
                !isScanning && !otherWorkRunning;
            CancelLibraryScanButton.Visibility =
                mainViewModel.Library.IsScanning
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            CancelLibraryScanButton.IsEnabled =
                mainViewModel.Library.CanCancel;

            UpdateHumanLabelActionState();
        }

        private void ShowLibraryScanPreparingState()
        {
            mainViewModel.Library.ShowPreparing();
            SyncLegacyLibraryPanel();
        }

        private void UpdateLibraryScanProgress(
            LibraryScanProgress progress)
        {
            if (!acceptsLibraryScanProgress)
                return;

            mainViewModel.Library.UpdateProgress(progress);
            SyncLegacyLibraryPanel();
        }

        private void ShowLibraryScanSummary(LibraryScanResult result)
        {
            mainViewModel.Library.ShowSummary(result);
            SyncLegacyLibraryPanel();
        }

        private void ShowLibraryScanCancelledWithoutResult()
        {
            mainViewModel.Library.ShowCancelledWithoutResult();
            SyncLegacyLibraryPanel();
        }

        private void ShowLibraryScanFailure(Exception exception)
        {
            mainViewModel.Library.ShowFailure(exception);
            SyncLegacyLibraryPanel();
        }

        private void SyncLegacyLibraryPanel()
        {
            LibraryPanelViewModel library = mainViewModel.Library;

            LibraryScanProgressPanel.Visibility = library.HasProgress
                ? Visibility.Visible
                : Visibility.Collapsed;
            LibraryScanStatusText.Text = library.StatusText;
            LibraryScanProgressBar.Value = library.ProgressValue;
            LibraryScanProgressText.Text = library.ProcessedText;
            LibraryScanPercentText.Text = library.ProgressPercentText;
            LibraryScanAnalyzedText.Text = library.AnalyzedText;
            LibraryScanUpToDateText.Text = library.UpToDateText;
            LibraryScanUnsupportedText.Text = library.UnsupportedText;
            LibraryScanFailedText.Text = library.FailedText;
            LibraryScanCurrentFileText.Text = library.CurrentFileText;
            LibraryScanCurrentFileText.ToolTip = library.CurrentFileToolTip;
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
                currentBeatmap = beatmap;
                await UpdateOverviewFromSourceAsync(target.SourceFilePath);

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

        private async void NextTechCandidate_Click(
            object sender,
            RoutedEventArgs e)
        {
            await NavigateToNextTechCandidateAsync();
        }

        private async Task NavigateToNextTechCandidateAsync()
        {
            if (IsBackgroundLibraryWorkRunning || isNavigatingTechCandidates)
            {
                return;
            }

            isNavigatingTechCandidates = true;
            NextTechCandidateButton.IsEnabled = false;
            SetFastLabelingMode(true);
            FastLabelingModeText.Text = "Mode: Tech candidates";
            activeLabelingQueue = LabelingQueueKind.TechCandidates;

            try
            {
                TechHumanLabelingCandidate? candidate = await Task.Run(() =>
                    techHumanLabelingCandidateSelector.FindNext(
                        currentTechCandidateSampleId));

                if (candidate is null)
                {
                    HumanLabelSampleStatusText.Text =
                        "No unlabeled Tech candidate remains.";
                    return;
                }

                Beatmap beatmap;

                try
                {
                    beatmap = await Task.Run(() =>
                        analysisCache.GetOrAnalyze(
                            candidate.Sample.SourceFilePath));
                }
                catch (Exception ex)
                {
                    DebugLogger.Log(
                        $"TECH LABELING LOAD ERROR | {ex.Message}");
                    DebugLogger.Detailed(ex.ToString());

                    HumanLabelSampleStatusText.Text =
                        "Dataset sample: Unavailable (file not found)";
                    return;
                }

                if (IsBackgroundLibraryWorkRunning)
                {
                    return;
                }

                currentMapPath = candidate.Sample.SourceFilePath;
                currentBeatmapUrl = candidate.Sample.BeatmapId is int beatmapId
                    ? $"https://osu.ppy.sh/b/{beatmapId}"
                    : null;
                BackgroundImage.Source = null;
                currentBeatmap = beatmap;
                await UpdateOverviewFromSourceAsync(candidate.Sample.SourceFilePath);

                currentTechCandidateSampleId = candidate.Sample.SampleId;
                RefreshHumanLabelPanel(beatmap, candidate.Sample.SourceFilePath);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"TECH LABELING NAV ERROR | {ex.Message}");
                DebugLogger.Detailed(ex.ToString());
            }
            finally
            {
                isNavigatingTechCandidates = false;
                NextTechCandidateButton.IsEnabled =
                    !IsBackgroundLibraryWorkRunning;
            }
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

                currentBeatmap = beatmap;
                await UpdateOverviewFromSourceAsync(sample.SourceFilePath);

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
            if (activeLabelingQueue == LabelingQueueKind.TechCandidates)
            {
                currentTechCandidateSampleId = null;
            }

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

        private void OpenTechReviewWindow_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                TechReviewWindow window = new()
                {
                    Owner = this
                };

                window.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "Unable to open Tech Review UI: " + ex.Message,
                    "Tech Review UI",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                DebugLogger.Log($"TECH REVIEW UI OPEN ERROR | {ex.Message}");
                DebugLogger.Detailed(ex.ToString());
            }
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

                currentBeatmap = beatmap;
                await UpdateOverviewFromSourceAsync(sample.SourceFilePath);

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

                if (!sample.HumanValidated
                    && sample.PrimaryHumanLabel is null
                    && HumanLabelPrefillPolicy.TryCreate(
                        beatmap.GameplayProfile.Identity,
                        out HumanLabelPrefill prefill))
                {
                    selectedPrimaryHumanLabel = prefill.Primary;
                    selectedSecondaryHumanLabel = prefill.Secondary;
                }

                HumanLabelSampleStatusText.Text = "Dataset sample: Ready";
                HumanLabelCurrentPrimaryText.Text = sample.PrimaryHumanLabel is null
                    ? "Primary: Unlabeled"
                    : $"Primary: {FormatHumanLabel(sample.PrimaryHumanLabel.Value)}"
                        + (sample.HumanValidated ? " ✓" : "");
                HumanLabelCurrentSecondaryText.Text = sample.SecondaryHumanLabel is null
                    ? "Secondary: —"
                    : $"Secondary: {FormatHumanLabel(sample.SecondaryHumanLabel.Value)}";

                HumanLabelSampleStatusText.Text =
                    sample.HumanValidated
                        ? "Dataset sample: Ready"
                        : selectedPrimaryHumanLabel.HasValue
                            ? "Dataset sample: Ready (prefilled from BeatInsight)"
                            : "Dataset sample: Ready";
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
            if (currentBeatmap is { } beatmap &&
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
                currentBeatmap is not { } beatmap ||
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
                if (LabelingQueueNavigationPolicy
                    .ShouldAutoAdvanceAfterSuccessfulValidation(
                        saveSucceeded: updated,
                        activeQueue: activeLabelingQueue))
                {
                    switch (activeLabelingQueue)
                    {
                        case LabelingQueueKind.Calibration:
                            await NavigateCalibrationAsync(forward: true);
                            break;

                        case LabelingQueueKind.FastUnlabeled:
                            await NavigateToUnlabeledAsync(forward: true);
                            break;

                        case LabelingQueueKind.TechCandidates:
                            await NavigateToNextTechCandidateAsync();
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
                currentBeatmap is not { } beatmap ||
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
            NextTechCandidateButton.IsEnabled =
                !IsBackgroundLibraryWorkRunning && !isNavigatingTechCandidates;

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

        private void GenerateCommunityDataset_Click(
            object sender,
            RoutedEventArgs e)
        {
        }

        private void CancelCommunityDatasetGeneration_Click(
            object sender,
            RoutedEventArgs e)
        {
        }

        private void ExportCommunityDatasetCsv_Click(
            object sender,
            RoutedEventArgs e)
        {
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
        private async Task UpdateMap(
            TimeSpan timerTickGap = default,
            long tickSequence = 0,
            long tickTimestamp = 0)
        {
            var performance = new MapChangePerformance(timerTickGap);

            // Le mode Fast Labeling gèle la beatmap affichée : le timer
            // continue de tourner (aucun arrêt définitif du polling), mais
            // aucun tick ne doit écraser la map chargée manuellement tant
            // que l'utilisateur n'en est pas sorti explicitement.
            if (IsBackgroundLibraryWorkRunning || isFastLabelingMode)
                return;

            // ============================================================
            // TOSU CONNECTION
            // ============================================================

            long tosuRequestStartTimestamp = Stopwatch.GetTimestamp();
            Stopwatch tosuWatch = Stopwatch.StartNew();
            if (!await IsTosuAvailable())
            {
                tosuWatch.Stop();
                long unavailableTimestamp = Stopwatch.GetTimestamp();
                DebugLogger.Log(
                    $"MAP SWITCH E2E TOSU UNAVAILABLE | " +
                    $"Sequence={tickSequence} | " +
                    $"RequestStartSW={tosuRequestStartTimestamp} | " +
                    $"RequestEndSW={unavailableTimestamp} | " +
                    $"Duration={tosuWatch.Elapsed.TotalMilliseconds:F1} ms");
                if (tosuConnected)
                {
                    tosuConnected = false;

                    DebugLogger.Log(
                        "TOSU | Déconnecté.");
                }

                ResetPolledBeatmapState();

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

            long tosuSnapshotRequestStartTimestamp = Stopwatch.GetTimestamp();
            string json = await client.GetStringAsync(
                $"http://127.0.0.1:24050/json?t={DateTime.UtcNow.Ticks}"
            );
            long tosuRequestEndTimestamp = Stopwatch.GetTimestamp();
            tosuWatch.Stop();
            performance.TosuRequest = tosuWatch.Elapsed;

            // On récupère le dossier racine dans lequel osu! stocke ses beatmaps.
            Stopwatch jsonWatch = Stopwatch.StartNew();
            JsonDocument document = JsonDocument.Parse(json);

            int debugBeatmapId = document.RootElement
                .GetProperty("menu")
                .GetProperty("bm")
                .GetProperty("id")
                .GetInt32();

            string? debugFile = document.RootElement
                .GetProperty("menu")
                .GetProperty("bm")
                .GetProperty("path")
                .GetProperty("file")
                .GetString();

            string? folder =
                document.RootElement
                .GetProperty("menu")
                .GetProperty("bm")
                .GetProperty("path")
                .GetProperty("folder")
                // On récupère le nom du fichier .osu actuellement sélectionné.
                .GetString();

            string? songs =
                document.RootElement
                .GetProperty("settings")
                .GetProperty("folders")
                .GetProperty("songs")
                .GetString();

            lastTosuSongsPath = string.IsNullOrWhiteSpace(songs)
                ? null
                : songs;
            RefreshSongsFolderDisplay();

            // On récupère le chemin du background associé à la difficulté actuelle.

            string? file =
                document.RootElement
                .GetProperty("menu")
                .GetProperty("bm")
                .GetProperty("path")
                .GetProperty("file")
                .GetString();

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

            jsonWatch.Stop();
            performance.JsonParse = jsonWatch.Elapsed;



            string? rawPath = TosuBeatmapPollingGuard.ResolvePath(
                songs,
                folder,
                file);
            bool isSameMap = string.Equals(
                rawPath,
                currentMapPath,
                StringComparison.OrdinalIgnoreCase);
            bool fileExistsAtResponse = rawPath is not null
                && File.Exists(rawPath);
            long tosuParsedTimestamp = Stopwatch.GetTimestamp();

            DebugLogger.Log(
                $"MAP SWITCH E2E TOSU RESPONSE | " +
                $"Sequence={tickSequence} | Utc={DateTime.UtcNow:O} | " +
                $"TickSW={tickTimestamp} | " +
                $"RequestStartSW={tosuRequestStartTimestamp} | " +
                $"SnapshotRequestStartSW={tosuSnapshotRequestStartTimestamp} | " +
                $"RequestEndSW={tosuRequestEndTimestamp} | " +
                $"ParsedSW={tosuParsedTimestamp} | " +
                $"RequestDuration={Stopwatch.GetElapsedTime(tosuRequestStartTimestamp, tosuRequestEndTimestamp).TotalMilliseconds:F1} ms | " +
                $"BeatmapId={beatmapId} | SameMap={isSameMap} | " +
                $"FileExists={fileExistsAtResponse} | " +
                $"Path={rawPath ?? "<null>"}");

            if (!isSameMap)
            {
                if (rawPath is not null
                    && !string.Equals(
                        pendingTosuMapPath,
                        rawPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    pendingTosuMapPath = rawPath;
                    pendingTosuMapFirstSeenTimestamp = tosuParsedTimestamp;
                    pendingTosuMapFileAvailableTimestamp = 0;
                }

                if (fileExistsAtResponse
                    && pendingTosuMapFileAvailableTimestamp == 0)
                {
                    pendingTosuMapFileAvailableTimestamp =
                        Stopwatch.GetTimestamp();
                }

                DebugLogger.Log(
                    $"MAP SWITCH E2E NEW TOSU DATA | " +
                    $"Sequence={tickSequence} | SW={tosuParsedTimestamp} | " +
                    $"BeatmapId={beatmapId} | " +
                    $"FileExists={fileExistsAtResponse} | " +
                    $"Path={rawPath ?? "<null>"}");
            }

            Stopwatch pathWatch = Stopwatch.StartNew();
            string? chemin = TosuBeatmapPollingGuard.ResolveExistingPath(
                songs,
                folder,
                file
            );
            pathWatch.Stop();
            performance.PathResolve = pathWatch.Elapsed;

            if (chemin is not null)
            {
                DebugLogger.Log(
                    $"MAP SWITCH E2E FILE AVAILABLE | " +
                    $"Sequence={tickSequence} | SW={Stopwatch.GetTimestamp()} | " +
                    $"Path={chemin}");
            }

            if (chemin is null)
            {
                if (currentMapPath is not null || currentBeatmap is not null)
                {
                    DebugLogger.Log(
                        $"TOSU MAP WAITING | File unavailable | " +
                        $"Songs={songs ?? "<null>"} | " +
                        $"Folder={folder ?? "<null>"} | " +
                        $"File={file ?? debugFile ?? "<null>"}");
                }

                ResetPolledBeatmapState();
                return;
            }

            SetTosuStatus(true);

            // ============================================================
            // MÊME MAP → ON NE FAIT RIEN
            // ============================================================

            if (chemin == currentMapPath)
            {
                return;
            }

            performance.FirstNewTosuDataTimestamp =
                string.Equals(
                    pendingTosuMapPath,
                    chemin,
                    StringComparison.OrdinalIgnoreCase)
                    ? pendingTosuMapFirstSeenTimestamp
                    : tosuParsedTimestamp;
            performance.FileAvailableTimestamp =
                pendingTosuMapFileAvailableTimestamp != 0
                    ? pendingTosuMapFileAvailableTimestamp
                    : Stopwatch.GetTimestamp();
            performance.DetectedDifferentPathTimestamp =
                Stopwatch.GetTimestamp();
            performance.TickTimestamp = tickTimestamp;
            performance.TickSequence = tickSequence;

            // ============================================================
            // NOUVELLE MAP
            // ============================================================

            performance.OverviewBeginTimestamp = Stopwatch.GetTimestamp();
            performance.CommunityTagsDeferred = true;
            long overviewRequest = overviewLoadCoordinator!.Begin(
                "Analyzing the selected beatmap...");
            currentMapPath = chemin;
            currentBeatmapUrl = $"https://osu.ppy.sh/b/{beatmapId}";
            currentBeatmap = null;
            BackgroundImage.Source = null;
            pendingTosuMapPath = null;
            pendingTosuMapFirstSeenTimestamp = 0;
            pendingTosuMapFileAvailableTimestamp = 0;
            mapEnrichmentCancellation?.Cancel();
            var cancellation = new CancellationTokenSource();
            mapEnrichmentCancellation = cancellation;
            _ = LoadSelectedMapAsync(
                chemin, beatmapId, songs, folder, bg,
                overviewRequest, performance, cancellation);
        }

        private async Task LoadSelectedMapAsync(
            string sourcePath,
            int beatmapId,
            string? songs,
            string? folder,
            string? backgroundFile,
            long request,
            MapChangePerformance performance,
            CancellationTokenSource cancellation)
        {
            try
            {
                await UpdateOverviewFromSourceAsync(
                    sourcePath,
                    performance: performance,
                    existingRequest: request,
                    cancellationToken: cancellation.Token);

                if (!overviewLoadCoordinator!.IsCurrent(request)
                    || cancellation.IsCancellationRequested)
                    return;

                // V1, le fond et les tags servent aux panneaux annexes :
                // aucun de ces traitements ne retient l'overlay Overview.
                performance.V1StartTimestamp = Stopwatch.GetTimestamp();
                BeatmapAnalysisOutcome? outcome = null;
                Beatmap? beatmap = await TosuBeatmapPollingGuard.TryLoadAsync(
                    sourcePath,
                    async path =>
                    {
                        outcome = await Task.Run(() =>
                            analysisCache.GetOrAnalyzeDetailed(path, beatmapId));
                        return outcome.Beatmap;
                    });
                performance.V1EndTimestamp = Stopwatch.GetTimestamp();
                performance.V1Load = Stopwatch.GetElapsedTime(
                    performance.V1StartTimestamp,
                    performance.V1EndTimestamp);
                performance.V1CacheHit = outcome?.WasCacheHit;
                performance.LogOverviewLifecycle("V1 complete");

                if (!overviewLoadCoordinator.IsCurrent(request)
                    || cancellation.IsCancellationRequested
                    || beatmap is null)
                    return;

                currentBeatmap = beatmap;
                RefreshHumanLabelPanel(beatmap, sourcePath);

                if (!string.IsNullOrWhiteSpace(songs)
                    && !string.IsNullOrWhiteSpace(folder)
                    && !string.IsNullOrWhiteSpace(backgroundFile))
                {
                    string backgroundPath = Path.Combine(
                        songs, folder, backgroundFile);
                    if (File.Exists(backgroundPath))
                    {
                        try
                        {
                            BackgroundImage.Source =
                                new BitmapImage(new Uri(backgroundPath));
                            mainViewModel.Overview.BackgroundPath =
                                backgroundPath;
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.Log(
                                $"OVERVIEW BACKGROUND ERROR | {ex.Message}");
                        }
                    }
                }

                if (!IsBackgroundLibraryWorkRunning
                    && overviewLoadCoordinator.IsCurrent(request)
                    && !cancellation.IsCancellationRequested)
                {
                    await UpdateCommunityTagsAsync(
                        beatmap, beatmapId, sourcePath,
                        cancellation.Token);
                }
            }
            catch (OperationCanceledException)
                when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"MAP ENRICHMENT ERROR | {ex.Message}");
                DebugLogger.Detailed(ex.ToString());
            }
            finally
            {
                if (ReferenceEquals(mapEnrichmentCancellation, cancellation))
                    mapEnrichmentCancellation = null;
                cancellation.Dispose();
            }
        }

        private async Task UpdateCommunityTagsAsync(
            Beatmap beatmap,
            int beatmapId,
            string sourcePath,
            CancellationToken cancellationToken)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                List<OsuApiService.OsuTagVote> osuCommunityTags =
                    await osuApi.GetBeatmapCommunityTags(
                        beatmapId,
                        cancellationToken);

                if (!ReferenceEquals(currentBeatmap, beatmap)
                    || !TosuBeatmapPollingGuard.ShouldApplyDeferredResult(
                        sourcePath,
                        currentMapPath,
                        cancellationToken.IsCancellationRequested))
                {
                    return;
                }

                beatmap.CommunityTags = osuCommunityTags
                    .Select(tag => new CommunityTag
                    {
                        Name = tag.Name,
                        Votes = tag.Votes
                    })
                    .ToList();
                mainViewModel.Overview.UpdateCommunityTags(
                    beatmap.CommunityTags);

                GameplayIdentity identity = beatmap.GameplayProfile.Identity;
                GameplayTagComparisonResult tagComparison =
                    GameplayTagComparer.Compare(
                        beatmap.CommunityTags,
                        identity.FullName,
                        identity.Traits);
                beatmap.TagComparison = tagComparison;

                CommunityIdentityAgreement communityIdentityAgreement =
                    CommunityIdentityAgreementComparer.Compare(
                        beatmap.CommunityTags,
                        identity);
                beatmap.CommunityIdentityAgreement =
                    communityIdentityAgreement;
                GameplayDebug.CommunityIdentityAgreement(
                    communityIdentityAgreement);

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
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                DebugLogger.Log(
                    $"COMMUNITY TAGS BACKGROUND ERROR | " +
                    $"BeatmapId={beatmapId} | {ex.Message}");
                DebugLogger.Detailed(ex.ToString());
            }
            finally
            {
                stopwatch.Stop();
                DebugLogger.Log(
                    $"MAP CHANGE PERF | Community tags background=" +
                    $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms | " +
                    $"BeatmapId={beatmapId} | " +
                    $"Cancelled={cancellationToken.IsCancellationRequested}");
            }
        }

        private void ResetPolledBeatmapState()
        {
            overviewLoadCoordinator?.Invalidate();
            bool shouldClearOverview =
                currentMapPath is not null
                || currentBeatmap is not null
                || mainViewModel.Overview.HasBeatmap
                || mainViewModel.Overview.IsLoading
                || mainViewModel.Overview.HasError;

            currentMapPath = null;
            currentBeatmapUrl = null;
            currentBeatmap = null;
            mapEnrichmentCancellation?.Cancel();
            mapEnrichmentCancellation = null;

            if (shouldClearOverview)
            {
                BackgroundImage.Source = null;
                mainViewModel.Overview.Clear();
            }

            SetTosuStatus(false);
        }

        // ============================================================
        // UI V2 — OVERVIEW VIEWMODEL BRIDGE
        // ============================================================

        /// <summary>
        /// Recalcule un BeatmapAnalysisSnapshot (V2, via les analyzers
        /// frozen) pour le fichier donné et met à jour Overview. Ce
        /// pipeline est entièrement indépendant du cache V1
        /// (analysisCache/GetOrAnalyze) qui continue de piloter
        /// currentBeatmap, le ML Lab et la labellisation.
        /// </summary>
        private async Task UpdateOverviewFromSourceAsync(
            string sourceFilePath,
            string? backgroundPath = null,
            MapChangePerformance? performance = null,
            long? existingRequest = null,
            CancellationToken cancellationToken = default)
        {
            long request = existingRequest
                ?? overviewLoadCoordinator!.Begin(
                    "Analyzing the selected beatmap...");

            try
            {
                if (performance is not null)
                {
                    performance.V2StartTimestamp = Stopwatch.GetTimestamp();
                    DebugLogger.Log(
                        $"MAP SWITCH E2E V2 START | " +
                        $"Sequence={performance.TickSequence} | " +
                        $"SW={performance.V2StartTimestamp} | " +
                        $"Path={sourceFilePath}");
                }

                Stopwatch snapshotWatch = Stopwatch.StartNew();
                CachedBeatmapAnalysisResult result =
                    await overviewAnalysisService.AnalyzeAsync(
                        sourceFilePath, cancellationToken);
                snapshotWatch.Stop();

                if (!overviewLoadCoordinator!.IsCurrent(request)
                    || cancellationToken.IsCancellationRequested
                    || !string.Equals(sourceFilePath, currentMapPath,
                        StringComparison.OrdinalIgnoreCase))
                    return;

                if (performance is not null)
                    performance.OverviewSnapshotAvailableTimestamp =
                        Stopwatch.GetTimestamp();

                if (performance is not null)
                {
                    performance.V2Snapshot = snapshotWatch.Elapsed;
                    performance.V2CacheLookup = result.LoadTime;
                    performance.V2AnalysisTime = result.AnalysisTime;
                    performance.V2SaveTime = result.SaveTime;
                    performance.V2CacheHit = result.WasCacheHit;
                    performance.V2EndTimestamp = Stopwatch.GetTimestamp();
                    DebugLogger.Log(
                        $"MAP SWITCH E2E V2 END | " +
                        $"Sequence={performance.TickSequence} | " +
                        $"SW={performance.V2EndTimestamp} | " +
                        $"Duration={performance.V2Snapshot.TotalMilliseconds:F1} ms | " +
                        $"CacheHit={performance.V2CacheHit}");
                }

                if (performance is not null)
                {
                    performance.ViewModelStartTimestamp =
                        Stopwatch.GetTimestamp();
                }

                Stopwatch loadSnapshotWatch = Stopwatch.StartNew();
                bool loaded = overviewLoadCoordinator.LoadSnapshot(
                    request,
                    result.Snapshot,
                    result.WasCacheHit,
                    result.LoadTime,
                    backgroundPath);
                loadSnapshotWatch.Stop();
                if (!loaded)
                    return;

                if (performance is not null)
                    performance.ViewModelEndTimestamp =
                        Stopwatch.GetTimestamp();

                if (!overviewLoadCoordinator.SetReady(request))
                    return;

                if (performance is not null)
                {
                    performance.OverviewReadyTimestamp =
                        Stopwatch.GetTimestamp();
                    performance.OverviewLoadSnapshot =
                        loadSnapshotWatch.Elapsed;
                    DebugLogger.Log(
                        $"MAP SWITCH E2E VIEWMODEL | " +
                        $"Sequence={performance.TickSequence} | " +
                        $"StartSW={performance.ViewModelStartTimestamp} | " +
                        $"EndSW={performance.ViewModelEndTimestamp} | " +
                        $"Duration={performance.OverviewLoadSnapshot.TotalMilliseconds:F1} ms | " +
                        $"Title={mainViewModel.Overview.Title} | " +
                        $"Difficulty={mainViewModel.Overview.Version}");
                    performance.LogOverviewLifecycle("Ready state set");
                }

                await CompleteOverviewAfterReadyAsync(
                    request, sourceFilePath, result.Snapshot,
                    result.WasCacheHit, performance);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                DebugLogger.Log(
                    $"OVERVIEW SNAPSHOT ERROR | {ex.Message}");
                DebugLogger.Detailed(ex.ToString());

                if (string.Equals(sourceFilePath, currentMapPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    overviewLoadCoordinator!.Fail(
                        request,
                        "Unable to analyze this beatmap for the Overview panel.");
                }
            }
        }

        private async Task CompleteOverviewAfterReadyAsync(
            long request,
            string sourceFilePath,
            BeatmapAnalysisSnapshot snapshot,
            bool wasCacheHit,
            MapChangePerformance? performance)
        {
            try
            {
                await Dispatcher.InvokeAsync(
                    () => { }, DispatcherPriority.ContextIdle);
                if (!overviewLoadCoordinator!.IsCurrent(request))
                    return;

                bool overlayVisible =
                    DashboardViewHost.IsLoadingOverlayVisible;
                if (performance is not null)
                {
                    if (!overlayVisible)
                        performance.OverviewOverlayHiddenTimestamp =
                            Stopwatch.GetTimestamp();
                    performance.LogOverviewLifecycle(
                        overlayVisible ? "Overlay still visible" : "Overlay hidden");
                }

                overviewLoadCoordinator.CompleteSecondaryDetails(
                    request, snapshot, wasCacheHit);

                if (performance is not null
                    && overviewLoadCoordinator.IsCurrent(request))
                {
                    await performance.LogAsync(
                        snapshot.Metadata?.BeatmapId ?? 0,
                        sourceFilePath,
                        Dispatcher,
                        () => DashboardViewHost.IsOverviewDisplayed(
                            mainViewModel.Overview.Title,
                            mainViewModel.Overview.Version));
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log(
                    $"OVERVIEW SECONDARY ERROR | {ex.Message}");
                DebugLogger.Detailed(ex.ToString());
            }
        }

        private sealed class MapChangePerformance
        {
            private readonly Stopwatch total = Stopwatch.StartNew();

            internal MapChangePerformance(TimeSpan timerTickGap)
            {
                TimerTickGap = timerTickGap;
            }

            internal TimeSpan TimerTickGap { get; }
            internal TimeSpan TosuRequest { get; set; }
            internal TimeSpan JsonParse { get; set; }
            internal TimeSpan PathResolve { get; set; }
            internal TimeSpan V1Load { get; set; }
            internal TimeSpan CommunityTags { get; set; }
            internal TimeSpan V2Snapshot { get; set; }
            internal TimeSpan V2CacheLookup { get; set; }
            internal TimeSpan V2AnalysisTime { get; set; }
            internal TimeSpan V2SaveTime { get; set; }
            internal TimeSpan OverviewLoadSnapshot { get; set; }
            internal bool? V1CacheHit { get; set; }
            internal bool? V2CacheHit { get; set; }
            internal bool CommunityTagsDeferred { get; set; }
            internal long TickSequence { get; set; }
            internal long TickTimestamp { get; set; }
            internal long FirstNewTosuDataTimestamp { get; set; }
            internal long DetectedDifferentPathTimestamp { get; set; }
            internal long FileAvailableTimestamp { get; set; }
            internal long V1StartTimestamp { get; set; }
            internal long V1EndTimestamp { get; set; }
            internal long V2StartTimestamp { get; set; }
            internal long V2EndTimestamp { get; set; }
            internal long ViewModelStartTimestamp { get; set; }
            internal long ViewModelEndTimestamp { get; set; }
            internal long OverviewBeginTimestamp { get; set; }
            internal long OverviewSnapshotAvailableTimestamp { get; set; }
            internal long OverviewReadyTimestamp { get; set; }
            internal long OverviewOverlayHiddenTimestamp { get; set; }

            internal void LogOverviewLifecycle(string milestone)
            {
                string At(long timestamp) => timestamp == 0
                    ? "pending"
                    : $"{Stopwatch.GetElapsedTime(OverviewBeginTimestamp, timestamp).TotalMilliseconds:F1} ms";

                DebugLogger.Log(
                    "OVERVIEW LOAD LIFECYCLE | " +
                    $"Sequence={TickSequence} | {milestone} | " +
                    "BeginLoad=0 ms | " +
                    $"V1 complete={At(V1EndTimestamp)} | " +
                    $"V2 snapshot available={At(OverviewSnapshotAvailableTimestamp)} | " +
                    $"LoadSnapshot complete={At(ViewModelEndTimestamp)} | " +
                    $"Ready state set={At(OverviewReadyTimestamp)} | " +
                    $"Overlay hidden={At(OverviewOverlayHiddenTimestamp)} | " +
                    $"IsLoading true={At(OverviewReadyTimestamp)} | " +
                    $"V2 cache hit={V2CacheHit?.ToString() ?? "pending"} | " +
                    $"V2 lookup={V2CacheLookup.TotalMilliseconds:F1} ms | " +
                    $"V2 analysis={V2AnalysisTime.TotalMilliseconds:F1} ms | " +
                    $"V2 save={V2SaveTime.TotalMilliseconds:F1} ms");
            }

            internal async Task LogAsync(
                int beatmapId,
                string path,
                Dispatcher dispatcher,
                Func<bool> isOverviewDisplayed)
            {
                long uiDispatchStartTimestamp = Stopwatch.GetTimestamp();
                await dispatcher.InvokeAsync(
                    () => { },
                    DispatcherPriority.DataBind);
                await dispatcher.InvokeAsync(
                    () => { },
                    DispatcherPriority.ContextIdle);
                long uiRenderTimestamp = Stopwatch.GetTimestamp();
                bool overviewDisplayed = isOverviewDisplayed();
                total.Stop();

                DebugLogger.Log(
                    $"MAP SWITCH E2E UI RENDER | " +
                    $"Sequence={TickSequence} | Utc={DateTime.UtcNow:O} | " +
                    $"SW={uiRenderTimestamp} | DisplayMatches={overviewDisplayed} | " +
                    $"Path={path}");

                DebugLogger.Log(
                    "MAP SWITCH E2E | " +
                    $"BeatmapId={beatmapId} | Path={path} | " +
                    $"FirstNewTosuSW={FirstNewTosuDataTimestamp} | " +
                    $"FinalUiSW={uiRenderTimestamp} | " +
                    $"Timer interval=500.0 ms | " +
                    $"Tick delay={TimerTickGap.TotalMilliseconds:F1} ms | " +
                    $"Tosu request={TosuRequest.TotalMilliseconds:F1} ms | " +
                    $"JSON parse={JsonParse.TotalMilliseconds:F1} ms | " +
                    $"Path resolve={PathResolve.TotalMilliseconds:F1} ms | " +
                    $"Tosu new-map visibility delay=" +
                    $"{Stopwatch.GetElapsedTime(FirstNewTosuDataTimestamp, DetectedDifferentPathTimestamp).TotalMilliseconds:F1} ms | " +
                    $"Path/File availability delay=" +
                    $"{Stopwatch.GetElapsedTime(FirstNewTosuDataTimestamp, FileAvailableTimestamp).TotalMilliseconds:F1} ms | " +
                    $"V1 cache/load=" +
                    (V1EndTimestamp == 0
                        ? "deferred | "
                        : $"{V1Load.TotalMilliseconds:F1} ms | ") +
                    $"Community tags=" +
                    (CommunityTagsDeferred
                        ? "deferred | "
                        : $"{CommunityTags.TotalMilliseconds:F1} ms | ") +
                    $"V2 snapshot={V2Snapshot.TotalMilliseconds:F1} ms | " +
                    $"V2 cache lookup={V2CacheLookup.TotalMilliseconds:F1} ms | " +
                    $"Overview LoadSnapshot={OverviewLoadSnapshot.TotalMilliseconds:F1} ms | " +
                    $"UI dispatch/render=" +
                    $"{Stopwatch.GetElapsedTime(uiDispatchStartTimestamp, uiRenderTimestamp).TotalMilliseconds:F1} ms | " +
                    $"Total from first new tosu data=" +
                    $"{Stopwatch.GetElapsedTime(FirstNewTosuDataTimestamp, uiRenderTimestamp).TotalMilliseconds:F1} ms | " +
                    $"CacheHit V1={V1CacheHit?.ToString() ?? "unknown"} | " +
                    $"CacheHit V2={V2CacheHit?.ToString() ?? "unknown"} | " +
                    $"DisplayMatches={overviewDisplayed}");
            }
        }

        private void AnalyzerTab_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: string tabName }
                || !Enum.TryParse(tabName, out NavigationTab tab))
            {
                return;
            }

            mainViewModel.SelectedTab = tab;
        }
    }
}
