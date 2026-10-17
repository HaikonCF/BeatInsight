using BeatInsight.Integration.Game;
using BeatInsight.Diagnostics;
using BeatInsight.Models.AnalysisCache;
using BeatInsight.Overlay;
using BeatInsight.Overlay.MapOverview;
using BeatInsight.Overlay.Skills;
using BeatInsight.Overlay.CurrentSection;
using BeatInsight.Overlay.UpNext;
using BeatInsight.Overlay.DifficultyTimeline;
using BeatInsight.Overlay.LivePatterns;
using BeatInsight.Overlay.GameplayHud;
using BeatInsight.Services;
using BeatInsight.Theming;
using BeatInsight.Views;
using System.Diagnostics;
using System.Windows;

namespace BeatInsight;

public partial class MainWindow
{
    private readonly OverlayEngine overlayEngine = new(
        OverlayLayoutCatalog.Get(OverlayLayoutIds.Minimal),
        OverlayThemeCatalog.Get(OverlayThemeIds.Default));
    private readonly OverlaySettingsStore overlaySettingsStore = new();
    private readonly AppThemeManager appThemeManager = new();
    private readonly CommunityThemeRegistry communityThemeRegistry = new();
    private readonly OverlayLayoutStore overlayLayoutStore = new();
    private OverlaySettings activeOverlaySettings =
        OverlaySettings.ForPreset(OverlayLayoutIds.Minimal);
    private MapOverviewOverlayController? mapOverviewOverlayController;
    private SkillsOverlayController? skillsOverlayController;
    private GameplayHudOverlayController? gameplayHudOverlayController;
    private readonly DifficultyTimelineResolver difficultyTimelineResolver = new();
    private readonly LivePatternsResolver livePatternsResolver = new();
    private readonly OsuLivePerformanceCalculator livePerformanceCalculator = new();
    private readonly ResultsPerformanceLatch resultsPerformanceLatch = new();
    private readonly WindowsOverlayDisplayModeProbe overlayWindowProbe =
        new WindowsOverlayDisplayModeProbe();
    private int? lastOverlayDisplayProcessId;
    private long lastOverlayDisplayProbeTimestamp;
    private OverlayDisplayMode? lastOverlayDisplayMode;
    private string? lastStableCurrentSectionForensicState;
    private string? lastLiveStatsForensicState;
    private string? lastLiveStatsDiscoverySummaryState;
    private string? lastLiveStatsCandidateTraceState;
    private string? liveStatsMeasurementProcessIdentity;
    private long liveStatsGameplayRouteTimestamp;
    private long liveStatsMeasurementWindowTimestamp;
    private int liveStatsSuccessfulTicks;
    private int liveStatsFailedTicks;
    private bool liveStatsFirstSnapshotMeasured;
    private string? lastPerformanceForensicState;
    private string? livePerformanceMeasurementProcessIdentity;
    private long livePerformanceMeasurementWindowTimestamp;
    private int livePerformanceTotalTicks;
    private int livePerformanceValidTicks;
    private int livePerformanceNullTicks;
    private int livePerformanceHitCountMismatchTicks;
    private int livePerformanceInputUnavailableTicks;
    private int livePerformanceUnsupportedModsTicks;
    private int livePerformanceCalculationFailedTicks;
    private int livePerformanceOtherFailureTicks;
    private int livePerformanceMismatchDurationTicks;

    private static readonly TimeSpan OverlayDisplayProbeInterval =
        TimeSpan.FromSeconds(1);

    private void InitializeMapOverviewOverlay()
    {
        IReadOnlyList<ThemeDefinition> availableThemes =
            communityThemeRegistry.RefreshRuntimeCatalog();
        mainViewModel.OverlaySettings.RefreshThemeOptions(availableThemes);
        activeOverlaySettings = overlaySettingsStore.Load();
        activeOverlaySettings = activeOverlaySettings with
        {
            AppThemeId = ApplyAppTheme(activeOverlaySettings.AppThemeId)
        };
        overlayEngine.ApplyTheme(OverlayThemeCatalog.Get(activeOverlaySettings.OverlayThemeId));
        _ = overlaySettingsStore.Save(activeOverlaySettings);
        OverlayLayout builtIn = OverlayLayoutCatalog.Get(activeOverlaySettings.PresetId);
        overlayEngine.ApplyLayout(overlayLayoutStore.Load(activeOverlaySettings.PresetId, builtIn));
        mainViewModel.OverlaySettings.Apply(activeOverlaySettings);
        mapOverviewOverlayController = new(
            overlayEngine,
            static () => new MapOverviewOverlayWindow());
        skillsOverlayController = new(
            overlayEngine,
            static () => new SkillsOverlayWindow());
        gameplayHudOverlayController = new(
            overlayEngine,
            static () => new GameplayHudWindow(),
            activeOverlaySettings.GameplayHud);
        mainViewModel.OverlaySettings.SettingsChanged += ApplyOverlaySettings;
        mainViewModel.OverlaySettings.BeginEditRequested += BeginOverlayEdit;
        mainViewModel.OverlaySettings.SaveEditRequested += SaveOverlayEdit;
        mainViewModel.OverlaySettings.CancelEditRequested += CancelOverlayEdit;
        mainViewModel.OverlaySettings.ResetEditRequested += ResetOverlayEdit;
    }

    private void ApplyOverlaySettings(OverlaySettings settings)
    {
        string presetId = OverlaySettings.NormalizePresetId(settings.PresetId);
        bool presetChanged = !string.Equals(activeOverlaySettings.PresetId, presetId, StringComparison.Ordinal);
        activeOverlaySettings = settings with
        {
            Version = OverlaySettings.CurrentVersion,
            PresetId = presetId,
            AppThemeId = ApplyAppTheme(settings.AppThemeId),
            OverlayThemeId = OverlaySettings.NormalizeOverlayThemeId(settings.OverlayThemeId)
        };
        overlayEngine.ApplyTheme(OverlayThemeCatalog.Get(activeOverlaySettings.OverlayThemeId));

        if (!overlayEngine.IsOverlayEditMode && presetChanged)
        {
            OverlayLayout builtIn = OverlayLayoutCatalog.Get(presetId);
            overlayEngine.ApplyLayout(overlayLayoutStore.Load(presetId, builtIn));
        }

        gameplayHudOverlayController?.ApplyDisplayOptions(activeOverlaySettings.GameplayHud);
        _ = overlaySettingsStore.Save(activeOverlaySettings);
    }

    private string ApplyAppTheme(string? themeId)
    {
        string appliedThemeId = appThemeManager.Apply(Application.Current.Resources, themeId);
        mainViewModel.Overview.Assistant.SetVisualAssetsEnabled(
            string.Equals(appliedThemeId, AppThemeIds.Bia, StringComparison.Ordinal));
        return appliedThemeId;
    }

    private void CommunityThemes_Click(object? sender, RoutedEventArgs e)
    {
        RestoreCommunityThemePreview();
        var window = new CommunityThemesWindow(
            communityThemeRegistry,
            PreviewCommunityTheme,
            RestoreCommunityThemePreview,
            ApplyCommunityTheme,
            DeleteCommunityTheme)
        {
            Owner = sender as Window ?? this
        };
        window.ShowDialog();
        RefreshCommunityThemeOptions();
    }

    private void PreviewCommunityTheme(ThemeDefinition definition)
    {
        ThemeDefinition[] installed = communityThemeRegistry.LoadCatalog()
            .Where(theme => !theme.BuiltIn && !string.Equals(theme.Id, definition.Id, StringComparison.Ordinal))
            .Append(definition)
            .ToArray();
        CommunityThemeRuntimeCatalog.Replace(installed);
        if (definition.HasAppTheme)
            ApplyAppTheme(definition.Id);
        if (definition.HasOverlayTheme)
            overlayEngine.ApplyTheme(OverlayThemeCatalog.Get(definition.Id));
    }

    private void RestoreCommunityThemePreview()
    {
        communityThemeRegistry.RefreshRuntimeCatalog();
        ApplyAppTheme(activeOverlaySettings.AppThemeId);
        overlayEngine.ApplyTheme(OverlayThemeCatalog.Get(activeOverlaySettings.OverlayThemeId));
    }

    private void ApplyCommunityTheme(string themeId, bool app, bool overlay)
    {
        communityThemeRegistry.RefreshRuntimeCatalog();
        RefreshCommunityThemeOptions();
        if (app)
            mainViewModel.OverlaySettings.SelectedAppThemeId = themeId;
        if (overlay)
            mainViewModel.OverlaySettings.SelectedOverlayThemeId = themeId;
    }

    private ThemeOperationResult<bool> DeleteCommunityTheme(ThemeDefinition definition)
    {
        if (string.Equals(activeOverlaySettings.AppThemeId, definition.Id, StringComparison.Ordinal))
            mainViewModel.OverlaySettings.SelectedAppThemeId = AppThemeIds.Default;
        if (string.Equals(activeOverlaySettings.OverlayThemeId, definition.Id, StringComparison.Ordinal))
            mainViewModel.OverlaySettings.SelectedOverlayThemeId = OverlayThemeIds.Default;
        ThemeOperationResult<bool> result = communityThemeRegistry.Delete(definition);
        RefreshCommunityThemeOptions();
        RestoreCommunityThemePreview();
        return result;
    }

    private void RefreshCommunityThemeOptions()
    {
        IReadOnlyList<ThemeDefinition> themes = communityThemeRegistry.RefreshRuntimeCatalog();
        mainViewModel.OverlaySettings.RefreshThemeOptions(themes);
    }

    private void BeginOverlayEdit(object? sender, EventArgs e)
    {
        if (overlayEngine.BeginEdit())
            mainViewModel.OverlaySettings.SetEditState(true);
    }

    private void SaveOverlayEdit(object? sender, EventArgs e)
    {
        var screen = new OverlayVirtualScreen(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        OverlayLayout? layout = overlayEngine.PrepareWorkingLayoutForSave(screen);
        if (layout is null || !overlayLayoutStore.Save(activeOverlaySettings.PresetId, layout))
            return;

        if (overlayEngine.CompleteEdit(layout))
            mainViewModel.OverlaySettings.SetEditState(false);
    }

    private void CancelOverlayEdit(object? sender, EventArgs e)
    {
        if (overlayEngine.CancelEdit())
            mainViewModel.OverlaySettings.SetEditState(false);
    }

    private void CancelOverlayEditForWindowHide()
    {
        if (overlayEngine.CancelEdit())
            mainViewModel.OverlaySettings.SetEditState(false);
    }

    private void ResetOverlayEdit(object? sender, EventArgs e) =>
        overlayEngine.ResetWorkingLayout(
            OverlayLayoutCatalog.Get(activeOverlaySettings.PresetId));

    private void PublishMapOverviewOverlay(
        string sourcePath,
        BeatmapAnalysisSnapshot analysis)
    {
        if (currentGameState is not { IsAvailable: true } game ||
            !string.Equals(
                sourcePath,
                game.CurrentBeatmapPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        GameRouteSnapshot route = gameStateService.ReadGameRoute();
        overlayEngine.PublishSnapshot(new OverlaySnapshot(
            game,
            ToOverlayGameState(route.Route),
            analysis));
    }

    private void ClearMapOverviewOverlay() => overlayEngine.PublishSnapshot(null);

    private void RefreshLiveGameplayOverlay(GameStateSnapshot game)
    {
        BeatmapAnalysisSnapshot? analysis = overlayEngine.CurrentSnapshot?.Analysis;
        if (analysis is null)
            return;

        GameRouteSnapshot route = gameStateService.ReadGameRoute();
        OverlayGameState overlayRoute = ToOverlayGameState(route.Route);
        GameplayPositionSnapshot? position = null;
        LiveGameplayCompositionFailure compositionFailure =
            LiveGameplayCompositionFailure.None;
        LiveGameplaySnapshot? live = null;
        LiveGameplayStatsSnapshot? liveStats = null;
        LivePerformanceCalculationResult performanceResult = new(
            null,
            LivePerformanceFailureReason.InputUnavailable);
        LiveGameplayStatsSnapshot? observedLiveStats =
            gameStateService.ReadLiveGameplayStats(out LiveGameplayStatsDiagnostics liveStatsDiagnostics);
        if (route.Route == GameRoute.Gameplay)
        {
            live = gameStateService.ReadLiveGameplaySnapshot(
                analysis,
                out position,
                out compositionFailure);
            liveStats = observedLiveStats;
            if (liveStats is not null && !string.IsNullOrWhiteSpace(game.CurrentBeatmapPath))
            {
                performanceResult = livePerformanceCalculator.Calculate(new LivePerformanceInput(
                    game.CurrentBeatmapPath,
                    liveStats.Mods,
                    liveStats.CurrentCombo,
                    liveStats.MaxComboReached,
                    liveStats.Count300,
                    liveStats.Count100,
                    liveStats.Count50,
                    liveStats.CountMiss,
                    liveStats.Accuracy,
                    liveStats.CurrentTime,
                    liveStats.Score,
                    string.Equals(game.ProviderName, LazerGameProvider.ProviderName, StringComparison.Ordinal)
                        ? LivePerformanceRulesetProfile.Lazer
                        : LivePerformanceRulesetProfile.StableClassic,
                    liveStats.CountLargeTickMiss,
                    liveStats.CountSliderTailHit));
            }
        }
        else
        {
            liveStatsDiagnostics = liveStatsDiagnostics with
            {
                FailureReason = LiveGameplayStatsFailureReason.RouteNotGameplay
            };
            lastStableCurrentSectionForensicState = null;
        }
        CurrentSectionSnapshot? currentSection =
            CurrentSectionResolver.Resolve(live);
        NextSectionSnapshot? nextSection = NextSectionResolver.Resolve(live);
        DifficultyTimelineSnapshot? difficultyTimeline =
            difficultyTimelineResolver.Resolve(live, currentSection);
        LivePatternsSnapshot? livePatterns =
            livePatternsResolver.Resolve(currentSection);
        LivePerformanceSnapshot? routedPerformance = resultsPerformanceLatch.Select(
            overlayRoute,
            game,
            liveStats,
            live?.CurrentTime ?? liveStats?.CurrentTime,
            performanceResult.Snapshot);
        var overlaySnapshot = new OverlaySnapshot(
            game,
            overlayRoute,
            analysis,
            live?.CurrentTime,
            LiveGameplay: live,
            CurrentSection: currentSection,
            NextSection: nextSection,
            DifficultyTimeline: difficultyTimeline,
            LivePatterns: livePatterns,
            LiveStats: liveStats,
            Performance: routedPerformance);
        overlayEngine.PublishSnapshot(overlaySnapshot);
        LogLivePerformanceForensic(game, route, liveStats, performanceResult);
        LogStableLiveStatsForensic(
            game,
            route,
            liveStats,
            liveStatsDiagnostics,
            overlaySnapshot);
        LogStableCurrentSectionForensic(
            game,
            route,
            position,
            analysis,
            live,
            compositionFailure,
            currentSection,
            overlaySnapshot);
    }

    private void LogLivePerformanceForensic(
        GameStateSnapshot game,
        GameRouteSnapshot route,
        LiveGameplayStatsSnapshot? liveStats,
        LivePerformanceCalculationResult result)
    {
        LivePerformanceCacheDiagnostics diagnostics = livePerformanceCalculator.Diagnostics;
        LivePerformanceSynchronizationDiagnostics? synchronization =
            livePerformanceCalculator.LastSynchronizationDiagnostics;
        long now = Stopwatch.GetTimestamp();
        if (!string.Equals(
                livePerformanceMeasurementProcessIdentity,
                game.ProcessIdentity,
                StringComparison.Ordinal) ||
            route.Route != GameRoute.Gameplay)
        {
            livePerformanceMeasurementProcessIdentity = game.ProcessIdentity;
            livePerformanceMeasurementWindowTimestamp = route.Route == GameRoute.Gameplay ? now : 0;
            livePerformanceTotalTicks = 0;
            livePerformanceValidTicks = 0;
            livePerformanceNullTicks = 0;
            livePerformanceHitCountMismatchTicks = 0;
            livePerformanceInputUnavailableTicks = 0;
            livePerformanceUnsupportedModsTicks = 0;
            livePerformanceCalculationFailedTicks = 0;
            livePerformanceOtherFailureTicks = 0;
            livePerformanceMismatchDurationTicks = 0;
        }
        else if (livePerformanceMeasurementWindowTimestamp == 0)
        {
            livePerformanceMeasurementWindowTimestamp = now;
        }

        if (route.Route == GameRoute.Gameplay)
        {
            livePerformanceTotalTicks++;
            if (result.Snapshot is not null)
            {
                livePerformanceValidTicks++;
            }
            else
            {
                livePerformanceNullTicks++;
                switch (result.FailureReason)
                {
                    case LivePerformanceFailureReason.HitCountMismatch:
                        livePerformanceHitCountMismatchTicks++;
                        break;
                    case LivePerformanceFailureReason.InputUnavailable:
                        livePerformanceInputUnavailableTicks++;
                        break;
                    case LivePerformanceFailureReason.UnsupportedMods:
                        livePerformanceUnsupportedModsTicks++;
                        break;
                    case LivePerformanceFailureReason.CalculationFailed:
                        livePerformanceCalculationFailedTicks++;
                        break;
                    default:
                        livePerformanceOtherFailureTicks++;
                        break;
                }
            }

            int judgedObjects = liveStats is null
                ? 0
                : liveStats.Count300 + liveStats.Count100 + liveStats.Count50 + liveStats.CountMiss;
            string mods = liveStats is null
                ? "<null>"
                : string.Join(',', liveStats.Mods.Select(mod => mod.Acronym));
            if (result.Snapshot is LivePerformanceSnapshot performance)
            {
                DebugLogger.Log(
                    "LIVE PERFORMANCE SAMPLE | " +
                    $"BeatmapId={game.CurrentBeatmapId ?? "<null>"} | " +
                    $"BeatmapSetId={game.CurrentBeatmapSetId ?? "<null>"} | " +
                    $"CurrentTimeMs={liveStats?.CurrentTime.TotalMilliseconds.ToString("F1") ?? "<null>"} | " +
                    $"Mods={mods} | PassedObjects={performance.PassedObjects} | " +
                    $"JudgedObjects={judgedObjects} | " +
                    $"Combo={liveStats?.CurrentCombo.ToString() ?? "<null>"} | " +
                    $"Count300={liveStats?.Count300.ToString() ?? "<null>"} | " +
                    $"Count100={liveStats?.Count100.ToString() ?? "<null>"} | " +
                    $"Count50={liveStats?.Count50.ToString() ?? "<null>"} | " +
                    $"Miss={liveStats?.CountMiss.ToString() ?? "<null>"} | " +
                    $"CurrentPp={performance.CurrentPp:F3} | FcPp={performance.FcPp:F3} | " +
                    $"Accuracy95FcPp={performance.Accuracy95FcPp:F3} | SsPp={performance.SsPp:F3} | " +
                    $"BeatmapParseCount={diagnostics.BeatmapParseCount} | " +
                    $"DifficultyCalculationCount={diagnostics.DifficultyCalculationCount} | " +
                    $"CacheHitCount={diagnostics.CacheHitCount} | " +
                    $"NonFiniteCurrentNormalizations={diagnostics.NonFiniteCurrentNormalizationCount}");
            }
            else
            {
                DebugLogger.Log(
                    "LIVE PERFORMANCE NULL | " +
                    $"BeatmapId={game.CurrentBeatmapId ?? "<null>"} | " +
                    $"BeatmapSetId={game.CurrentBeatmapSetId ?? "<null>"} | " +
                    $"CurrentTimeMs={liveStats?.CurrentTime.TotalMilliseconds.ToString("F1") ?? "<null>"} | " +
                    $"Mods={mods} | JudgedObjects={judgedObjects} | " +
                    $"PassedObjects={synchronization?.PassedObjectsFromTime.ToString() ?? "<null>"} | " +
                    $"FailureReason={result.FailureReason}");
            }

            if (result.FailureReason == LivePerformanceFailureReason.HitCountMismatch &&
                synchronization is not null)
            {
                livePerformanceMismatchDurationTicks++;
                LogHitCountSynchronization(
                    "LIVE PERFORMANCE HITCOUNT MISMATCH",
                    synchronization,
                    livePerformanceMismatchDurationTicks,
                    result.FailureReason);
            }
            else if (livePerformanceMismatchDurationTicks > 0)
            {
                LogHitCountSynchronization(
                    "LIVE PERFORMANCE HITCOUNT RECOVERY",
                    synchronization,
                    livePerformanceMismatchDurationTicks,
                    result.FailureReason);
                livePerformanceMismatchDurationTicks = 0;
            }

            if (livePerformanceMeasurementWindowTimestamp != 0 &&
                Stopwatch.GetElapsedTime(livePerformanceMeasurementWindowTimestamp, now) >= TimeSpan.FromSeconds(60))
            {
                DebugLogger.Log(
                    "LIVE PERFORMANCE 60S SUMMARY | " +
                    $"ProcessIdentity={game.ProcessIdentity ?? "<null>"} | " +
                    $"TotalTicks={livePerformanceTotalTicks} | " +
                    $"ValidTicks={livePerformanceValidTicks} | " +
                    $"NullTicks={livePerformanceNullTicks} | " +
                    $"HitCountMismatchTicks={livePerformanceHitCountMismatchTicks} | " +
                    $"InputUnavailableTicks={livePerformanceInputUnavailableTicks} | " +
                    $"UnsupportedModsTicks={livePerformanceUnsupportedModsTicks} | " +
                    $"CalculationFailedTicks={livePerformanceCalculationFailedTicks} | " +
                    $"OtherFailureTicks={livePerformanceOtherFailureTicks}");
                livePerformanceMeasurementWindowTimestamp = now;
                livePerformanceTotalTicks = 0;
                livePerformanceValidTicks = 0;
                livePerformanceNullTicks = 0;
                livePerformanceHitCountMismatchTicks = 0;
                livePerformanceInputUnavailableTicks = 0;
                livePerformanceUnsupportedModsTicks = 0;
                livePerformanceCalculationFailedTicks = 0;
                livePerformanceOtherFailureTicks = 0;
            }
        }

        string state = string.Join('|', new object?[]
        {
            game.ProcessIdentity,
            game.CurrentBeatmapPath,
            route.Route,
            result.Snapshot is not null,
            result.FailureReason,
            activeOverlaySettings.GameplayHud.ShowPerformance
        });
        if (string.Equals(state, lastPerformanceForensicState, StringComparison.Ordinal))
            return;

        lastPerformanceForensicState = state;
        DebugLogger.Log(
            "LIVE PERFORMANCE | " +
            $"ProcessIdentity={game.ProcessIdentity ?? "<null>"} | " +
            $"GameRoute={route.Route} | " +
            $"BeatmapPathPresent={!string.IsNullOrWhiteSpace(game.CurrentBeatmapPath)} | " +
            $"SnapshotPresent={result.Snapshot is not null} | " +
            $"FailureReason={result.FailureReason} | " +
            $"PassedObjects={result.Snapshot?.PassedObjects.ToString() ?? "<null>"} | " +
            $"ShowPerformance={activeOverlaySettings.GameplayHud.ShowPerformance} | " +
            $"BeatmapParseCount={diagnostics.BeatmapParseCount} | " +
            $"DifficultyCalculationCount={diagnostics.DifficultyCalculationCount} | " +
            $"CacheHitCount={diagnostics.CacheHitCount} | " +
            $"NonFiniteCurrentNormalizations={diagnostics.NonFiniteCurrentNormalizationCount} | " +
            $"PreparationMs={diagnostics.LastPreparationDuration?.TotalMilliseconds.ToString("F1") ?? "<null>"} | " +
            $"CurrentPpMs={diagnostics.LastCurrentCalculationDuration?.TotalMilliseconds.ToString("F3") ?? "<null>"} | " +
            $"FcPpMs={diagnostics.LastFcCalculationDuration?.TotalMilliseconds.ToString("F3") ?? "<null>"} | " +
            $"StaticPpMs={diagnostics.LastStaticCalculationDuration?.TotalMilliseconds.ToString("F3") ?? "<null>"}");
    }

    private static void LogHitCountSynchronization(
        string eventName,
        LivePerformanceSynchronizationDiagnostics? synchronization,
        int durationTicks,
        LivePerformanceFailureReason nextTickFailure)
    {
        DebugLogger.Log(
            $"{eventName} | " +
            $"DurationTicks={durationTicks} | " +
            $"NextTickFailure={nextTickFailure} | " +
            $"CurrentTimeMs={synchronization?.CurrentTimeMs.ToString("F1") ?? "<null>"} | " +
            $"PassedObjectsFromTime={synchronization?.PassedObjectsFromTime.ToString() ?? "<null>"} | " +
            $"JudgedObjects={synchronization?.JudgedObjects.ToString() ?? "<null>"} | " +
            $"Delta={synchronization?.Delta.ToString() ?? "<null>"} | " +
            $"LastPassedObjectTimeMs={synchronization?.LastPassedObjectTimeMs?.ToString("F1") ?? "<null>"} | " +
            $"NextObjectTimeMs={synchronization?.NextObjectTimeMs?.ToString("F1") ?? "<null>"} | " +
            $"BoundaryObjectType={synchronization?.BoundaryObjectType ?? "<null>"} | " +
            $"BoundaryStartTimeMs={synchronization?.BoundaryObjectStartTimeMs?.ToString("F1") ?? "<null>"} | " +
            $"BoundaryEndTimeMs={synchronization?.BoundaryObjectEndTimeMs?.ToString("F1") ?? "<null>"} | " +
            $"TimeSinceBoundaryStartMs={synchronization?.TimeSinceBoundaryStartMs?.ToString("F1") ?? "<null>"} | " +
            $"TimeUntilBoundaryEndMs={synchronization?.TimeUntilBoundaryEndMs?.ToString("F1") ?? "<null>"}");
    }

    private void LogStableLiveStatsForensic(
        GameStateSnapshot game,
        GameRouteSnapshot route,
        LiveGameplayStatsSnapshot? liveStats,
        LiveGameplayStatsDiagnostics diagnostics,
        OverlaySnapshot overlaySnapshot)
    {
        bool showLiveStats = activeOverlaySettings.GameplayHud.ShowLiveStats;
        bool finalBlockVisible = GameplayHudSnapshotProjector.Project(
            overlaySnapshot,
            activeOverlaySettings.GameplayHud).IsLiveStatsVisible;
        long now = Stopwatch.GetTimestamp();

        if (!string.Equals(
                liveStatsMeasurementProcessIdentity,
                game.ProcessIdentity,
                StringComparison.Ordinal) ||
            route.Route != GameRoute.Gameplay)
        {
            liveStatsMeasurementProcessIdentity = game.ProcessIdentity;
            liveStatsGameplayRouteTimestamp = route.Route == GameRoute.Gameplay ? now : 0;
            liveStatsMeasurementWindowTimestamp = liveStatsGameplayRouteTimestamp;
            liveStatsSuccessfulTicks = 0;
            liveStatsFailedTicks = 0;
            liveStatsFirstSnapshotMeasured = false;
        }
        else if (liveStatsGameplayRouteTimestamp == 0)
        {
            liveStatsGameplayRouteTimestamp = now;
            liveStatsMeasurementWindowTimestamp = now;
        }

        if (route.Route == GameRoute.Gameplay)
        {
            if (liveStats is null)
                liveStatsFailedTicks++;
            else
                liveStatsSuccessfulTicks++;

            if (liveStats is not null && !liveStatsFirstSnapshotMeasured)
            {
                liveStatsFirstSnapshotMeasured = true;
                DebugLogger.Log(
                    "LIVE STATS FIRST SNAPSHOT | " +
                    $"ProcessIdentity={game.ProcessIdentity ?? "<null>"} | " +
                    $"GameplayRouteToFirstSnapshotMs={Stopwatch.GetElapsedTime(liveStatsGameplayRouteTimestamp, now).TotalMilliseconds:F1} | " +
                    $"ProcessAttachToDiscoveryReadyMs={diagnostics.ProcessAttachToDiscoveryReady?.TotalMilliseconds.ToString("F1") ?? "<null>"} | " +
                    $"DiscoveryAttempts={diagnostics.DiscoveryAttempts} | " +
                    $"SuccessfulDiscoveryCount={diagnostics.SuccessfulDiscoveryCount}");
            }

            if (liveStatsMeasurementWindowTimestamp != 0 &&
                Stopwatch.GetElapsedTime(liveStatsMeasurementWindowTimestamp, now) >= TimeSpan.FromSeconds(60))
            {
                DebugLogger.Log(
                    "LIVE STATS 60S SUMMARY | " +
                    $"ProcessIdentity={game.ProcessIdentity ?? "<null>"} | " +
                    $"SuccessfulTicks={liveStatsSuccessfulTicks} | " +
                    $"FailedTicks={liveStatsFailedTicks} | " +
                    $"DiscoveryAttempts={diagnostics.DiscoveryAttempts} | " +
                    $"SuccessfulDiscoveryCount={diagnostics.SuccessfulDiscoveryCount} | " +
                    $"LastFailure={diagnostics.FailureReason}");
                liveStatsMeasurementWindowTimestamp = now;
                liveStatsSuccessfulTicks = 0;
                liveStatsFailedTicks = 0;
            }
        }

        string state = string.Join("|", new object?[]
        {
            game.ProcessIdentity,
            game.Availability,
            route.Route,
            diagnostics.DiscoveryState,
            diagnostics.ReaderResolved,
            diagnostics.DiscoveryAttempts,
            diagnostics.SuccessfulDiscoveryCount,
            diagnostics.LastDiscoveryFailure,
            diagnostics.DiscoveryOutcome,
            diagnostics.RawCandidateCount,
            diagnostics.StructuralCandidateCount,
            diagnostics.ValidatedCandidateCount,
            diagnostics.PlausibleCandidateCount,
            diagnostics.ActiveCandidateCount,
            diagnostics.ContainerAddress,
            diagnostics.ActiveRulesetAddress,
            diagnostics.GameplayBaseAddress,
            diagnostics.ScoreBaseAddress,
            liveStats is not null,
            diagnostics.FailureReason,
            game.CurrentBeatmapId,
            game.CurrentBeatmapSetId,
            liveStats?.CurrentTime is not null,
            showLiveStats,
            finalBlockVisible
        });
        LogStableLiveStatsDiscoveryCandidates(game, diagnostics);
        if (string.Equals(state, lastLiveStatsForensicState, StringComparison.Ordinal))
            return;

        lastLiveStatsForensicState = state;
        DebugLogger.Log(
            "LIVE STATS FORENSIC | " +
            $"ProcessIdentity={game.ProcessIdentity ?? "<null>"} | " +
            $"ProviderAvailability={game.Availability} | " +
            $"GameRoute={route.Route} | " +
            $"StatsDiscoveryState={diagnostics.DiscoveryState} | " +
            $"StatsReaderResolved={diagnostics.ReaderResolved} | " +
            $"LiveGameplayStatsSnapshotPresent={liveStats is not null} | " +
            $"FailureReason={diagnostics.FailureReason} | " +
            $"BeatmapId={game.CurrentBeatmapId ?? "<null>"} | " +
            $"BeatmapSetId={game.CurrentBeatmapSetId ?? "<null>"} | " +
            $"CurrentTimeMs={liveStats?.CurrentTime.TotalMilliseconds.ToString("F1") ?? "<null>"} | " +
            $"HudShowLiveStats={showLiveStats} | " +
            $"FinalBlockVisible={finalBlockVisible} | " +
            $"DiscoveryAttempts={diagnostics.DiscoveryAttempts} | " +
            $"SuccessfulDiscoveryCount={diagnostics.SuccessfulDiscoveryCount} | " +
            $"LastDiscoveryFailure={diagnostics.LastDiscoveryFailure} | " +
            $"DiscoveryOutcome={diagnostics.DiscoveryOutcome} | " +
            $"SignatureMatches={diagnostics.RawCandidateCount} | " +
            $"ContainerAddress={diagnostics.ContainerAddress} | " +
            $"ActiveRuleset={diagnostics.ActiveRulesetAddress} | " +
            $"GameplayBase={diagnostics.GameplayBaseAddress} | " +
            $"ScoreBase={diagnostics.ScoreBaseAddress} | " +
            $"Combo={liveStats?.CurrentCombo.ToString() ?? "<null>"} | " +
            $"MaxCombo={liveStats?.MaxComboReached.ToString() ?? "<null>"} | " +
            $"Count300={liveStats?.Count300.ToString() ?? "<null>"} | " +
            $"Count100={liveStats?.Count100.ToString() ?? "<null>"} | " +
            $"Count50={liveStats?.Count50.ToString() ?? "<null>"} | " +
            $"Miss={liveStats?.CountMiss.ToString() ?? "<null>"} | " +
            $"Accuracy={liveStats?.Accuracy.ToString("F6") ?? "<null>"} | " +
            $"Mods={(liveStats is null ? "<null>" : string.Join(',', liveStats.Mods.Select(mod => mod.Acronym)))} | " +
            $"Score={liveStats?.Score?.ToString() ?? "<null>"} | " +
            $"RawCandidateCount={diagnostics.RawCandidateCount} | " +
            $"StructuralCandidateCount={diagnostics.StructuralCandidateCount} | " +
            $"ValidatedCandidateCount={diagnostics.ValidatedCandidateCount} | " +
            $"PlausibleCandidateCount={diagnostics.PlausibleCandidateCount} | " +
            $"ActiveCandidateCount={diagnostics.ActiveCandidateCount} | " +
            $"NextDiscoveryRetryMs={diagnostics.NextDiscoveryRetry?.TotalMilliseconds.ToString("F1") ?? "<null>"} | " +
            $"ProcessAttachToDiscoveryReadyMs={diagnostics.ProcessAttachToDiscoveryReady?.TotalMilliseconds.ToString("F1") ?? "<null>"} | " +
            $"LastDiscoveryDurationMs={diagnostics.LastDiscoveryDuration?.TotalMilliseconds.ToString("F1") ?? "<null>"}");

    }

    private void LogStableLiveStatsDiscoveryCandidates(
        GameStateSnapshot game,
        LiveGameplayStatsDiagnostics diagnostics)
    {
        if (diagnostics.DiscoveryAttempts == 0)
            return;

        string summaryState = $"{game.ProcessIdentity}|{diagnostics.DiscoveryAttempts}";
        if (string.Equals(
                summaryState,
                lastLiveStatsDiscoverySummaryState,
                StringComparison.Ordinal))
        {
            return;
        }
        lastLiveStatsDiscoverySummaryState = summaryState;

        string histogram = string.Join(",", diagnostics.DiscoveryCandidates
            .Where(candidate => !string.Equals(
                candidate.RejectionReason,
                "None",
                StringComparison.Ordinal))
            .GroupBy(candidate => candidate.RejectionReason, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key}:{group.Count()}"));
        DebugLogger.Log(
            "LIVE STATS DISCOVERY CANDIDATES | " +
            $"ProcessIdentity={game.ProcessIdentity ?? "<null>"} | " +
            $"Attempt={diagnostics.DiscoveryAttempts} | " +
            $"Outcome={diagnostics.DiscoveryOutcome} | " +
            $"Raw={diagnostics.RawCandidateCount} | " +
            $"Structural={diagnostics.StructuralCandidateCount} | " +
            $"Validated={diagnostics.ValidatedCandidateCount} | " +
            $"Plausible={diagnostics.PlausibleCandidateCount} | " +
            $"Active={diagnostics.ActiveCandidateCount} | " +
            $"RejectedBy={(string.IsNullOrEmpty(histogram) ? "<none>" : histogram)}");

        LiveGameplayStatsDiscoveryCandidateDiagnostics[] reasonableCandidates = diagnostics
            .DiscoveryCandidates
            .Where(candidate => candidate.ProbeReadsComplete)
            .ToArray();
        string candidateTraceState = string.Join(";",
            reasonableCandidates.Select(candidate => string.Join(",",
                candidate.RootAddress,
                candidate.GameplayBaseAddress,
                candidate.ScoreBaseAddress,
                candidate.AccuracyBaseAddress,
                candidate.ModsBaseAddress,
                candidate.RejectionReason,
                candidate.SemanticReadSucceeded,
                candidate.SemanticFailureReason,
                candidate.SemanticSample,
                candidate.RawSample,
                candidate.PlausibleCandidate,
                candidate.TemporalObservationCount,
                candidate.CoherentProgressionCount,
                candidate.TemporalEvidence,
                candidate.TemporallyActive,
                candidate.ActiveGameplayEvidence))
            .Prepend(diagnostics.DiscoveryOutcome)
            .Prepend(game.ProcessIdentity ?? "<null>"));
        if (string.Equals(
                candidateTraceState,
                lastLiveStatsCandidateTraceState,
                StringComparison.Ordinal))
        {
            return;
        }
        lastLiveStatsCandidateTraceState = candidateTraceState;

        for (int index = 0; index < reasonableCandidates.Length; index++)
        {
            LiveGameplayStatsDiscoveryCandidateDiagnostics candidate =
                reasonableCandidates[index];
            DebugLogger.Log(
                "LIVE STATS DISCOVERY CANDIDATE | " +
                $"ProcessIdentity={game.ProcessIdentity ?? "<null>"} | " +
                $"Attempt={diagnostics.DiscoveryAttempts} | " +
                $"Index={index} | " +
                $"Root={candidate.RootAddress} | " +
                $"Source={candidate.Source} | " +
                $"SourceRegion={candidate.SourceRegionAddress} | " +
                $"SourceOffset={candidate.SourceOffset} | " +
                $"GameplayBase={candidate.GameplayBaseAddress} | " +
                $"ScoreBase={candidate.ScoreBaseAddress} | " +
                $"AccuracyBase={candidate.AccuracyBaseAddress} | " +
                $"ModsBase={candidate.ModsBaseAddress} | " +
                $"PointerChainStructural={candidate.PointerChainStructurallyValid} | " +
                $"ProbeReadsComplete={candidate.ProbeReadsComplete} | " +
                $"RejectionReason={candidate.RejectionReason} | " +
                $"SemanticReadSucceeded={candidate.SemanticReadSucceeded} | " +
                $"SemanticFailureReason={candidate.SemanticFailureReason} | " +
                $"SemanticSample={candidate.SemanticSample} | " +
                $"RawSample={candidate.RawSample} | " +
                $"Plausible={candidate.PlausibleCandidate} | " +
                $"TemporalObservations={candidate.TemporalObservationCount} | " +
                $"CoherentProgressions={candidate.CoherentProgressionCount} | " +
                $"TemporalEvidence={candidate.TemporalEvidence} | " +
                $"TemporallyActive={candidate.TemporallyActive} | " +
                $"ActiveGameplayEvidence={candidate.ActiveGameplayEvidence}");
        }
    }

    private void LogStableCurrentSectionForensic(
        GameStateSnapshot game,
        GameRouteSnapshot route,
        GameplayPositionSnapshot? position,
        BeatmapAnalysisSnapshot analysis,
        LiveGameplaySnapshot? live,
        LiveGameplayCompositionFailure compositionFailure,
        CurrentSectionSnapshot? currentSection,
        OverlaySnapshot overlaySnapshot)
    {
        if (!string.Equals(
                game.ProviderName,
                NativeStableGameProvider.ProviderName,
                StringComparison.Ordinal) ||
            route.Route != GameRoute.Gameplay)
        {
            return;
        }

        IReadOnlyList<Models.Sections.GameplaySectionV2Analysis>? sections =
            analysis.SectionAnalysisV2?.Sections;
        double? firstSectionStart = sections is { Count: > 0 }
            ? sections[0]?.StartTime
            : null;
        double? lastSectionEnd = sections is { Count: > 0 }
            ? sections[^1]?.EndTime
            : null;
        double? durationMilliseconds = analysis.Metadata.LengthTicks > 0
            ? TimeSpan.FromTicks(analysis.Metadata.LengthTicks).TotalMilliseconds
            : null;
        double? timelineEnd = firstSectionStart is { } start &&
            durationMilliseconds is { } duration
                ? start + duration
                : null;

        OverlayGameState overlayRoute = overlaySnapshot.GameMode;
        overlayEngine.TryGetWidget(
            OverlayWidgetIds.GameplayHud,
            out OverlayWidgetRegistration? registration);
        OverlayWidgetPlacement? placement = overlayEngine.CurrentLayout.Widgets
            .FirstOrDefault(item => item.WidgetId == OverlayWidgetIds.GameplayHud);
        bool routePolicy =
            overlayRoute == OverlayGameState.Gameplay &&
            registration is { IsEnabled: true } &&
            registration.Definition.SupportedStates.Contains(overlayRoute) &&
            placement is { IsVisible: true };
        bool finalVisible = overlayEngine.GetVisibleWidgets().Any(item =>
            item.Registration.Definition.Id == OverlayWidgetIds.GameplayHud);

        string state = string.Join("|", new object?[]
        {
            game.ProcessIdentity,
            route.ProcessIdentity,
            position?.ProcessIdentity,
            route.Route,
            position?.IsGameplay,
            game.CurrentBeatmapId,
            game.CurrentBeatmapSetId,
            analysis.Metadata.BeatmapId,
            analysis.Metadata.BeatmapSetId,
            compositionFailure,
            live is not null,
            currentSection?.SectionIndex,
            routePolicy,
            overlayEngine.DisplayCompatibility.DisplayMode,
            overlayEngine.DisplayCompatibility.OverlayVisible,
            overlayEngine.OsuIsForeground,
            finalVisible
        });
        if (string.Equals(
                state,
                lastStableCurrentSectionForensicState,
                StringComparison.Ordinal))
        {
            return;
        }

        lastStableCurrentSectionForensicState = state;
        DebugLogger.Log(
            "STABLE CURRENT SECTION FORENSIC | " +
            $"GameIdentity={game.ProcessIdentity ?? "<null>"} | " +
            $"Route={route.Route} | RouteIdentity={route.ProcessIdentity ?? "<null>"} | " +
            $"PositionAvailable={position?.IsGameplay == true} | " +
            $"PositionIdentity={position?.ProcessIdentity ?? "<null>"} | " +
            $"CurrentTimeMs={position?.CurrentTime?.TotalMilliseconds.ToString("F1") ?? "<null>"} | " +
            $"GameBeatmapId={game.CurrentBeatmapId ?? "<null>"} | " +
            $"GameBeatmapSetId={game.CurrentBeatmapSetId ?? "<null>"} | " +
            $"AnalysisBeatmapId={analysis.Metadata.BeatmapId?.ToString() ?? "<null>"} | " +
            $"AnalysisBeatmapSetId={analysis.Metadata.BeatmapSetId?.ToString() ?? "<null>"} | " +
            $"BeatmapDurationMs={durationMilliseconds?.ToString("F1") ?? "<null>"} | " +
            $"SectionCount={sections?.Count ?? 0} | " +
            $"FirstSectionStartMs={firstSectionStart?.ToString("F1") ?? "<null>"} | " +
            $"LastSectionEndMs={lastSectionEnd?.ToString("F1") ?? "<null>"} | " +
            $"LiveComposed={live is not null} | ComposeFailure={compositionFailure} | " +
            $"TimelineStartMs={firstSectionStart?.ToString("F1") ?? "<null>"} | " +
            $"TimelineEndMs={timelineEnd?.ToString("F1") ?? "<null>"} | " +
            $"Progress={live?.Progress.ToString("F4") ?? "<null>"} | " +
            $"CurrentSectionResolved={currentSection is not null} | " +
            $"CurrentSectionIndex={currentSection?.SectionIndex.ToString() ?? "<null>"} | " +
            $"CurrentSectionStartMs={currentSection?.StartTime.TotalMilliseconds.ToString("F1") ?? "<null>"} | " +
            $"CurrentSectionEndMs={currentSection?.EndTime.TotalMilliseconds.ToString("F1") ?? "<null>"} | " +
            $"ResolverFailure={(currentSection is null ? live is null ? $"LiveUnavailable:{compositionFailure}" : "NoMatchingValidSection" : "None")} | " +
            $"OverlaySnapshotPresent={overlaySnapshot.CurrentSection is not null} | " +
            $"RoutePolicy={routePolicy} | " +
            $"DisplayMode={overlayEngine.DisplayCompatibility.DisplayMode} | " +
            $"DisplayGate={overlayEngine.DisplayCompatibility.OverlayVisible} | " +
            $"ForegroundGate={overlayEngine.OsuIsForeground} | " +
            $"FinalVisible={finalVisible}");
    }

    private static OverlayGameState ToOverlayGameState(GameRoute route) => route switch
    {
        GameRoute.SongSelect => OverlayGameState.SongSelect,
        GameRoute.Gameplay => OverlayGameState.Gameplay,
        GameRoute.Results => OverlayGameState.Results,
        _ => OverlayGameState.Unknown
    };

    private void RefreshOverlayDisplayCompatibility(GameStateSnapshot snapshot)
    {
        if (snapshot.ProcessId is null)
        {
            lastOverlayDisplayProcessId = null;
            lastOverlayDisplayProbeTimestamp = 0;
            ApplyOverlayDisplayCompatibility(OverlayDisplayCompatibility.ClientAbsent);
            return;
        }

        long now = Stopwatch.GetTimestamp();
        if (lastOverlayDisplayProcessId == snapshot.ProcessId &&
            lastOverlayDisplayProbeTimestamp != 0 &&
            Stopwatch.GetElapsedTime(lastOverlayDisplayProbeTimestamp, now) <
                OverlayDisplayProbeInterval)
        {
            return;
        }

        lastOverlayDisplayProcessId = snapshot.ProcessId;
        lastOverlayDisplayProbeTimestamp = now;
        ApplyOverlayDisplayCompatibility(overlayWindowProbe.Probe(snapshot));
    }

    private void RefreshOverlayForeground(GameStateSnapshot snapshot) =>
        ApplyOverlayForegroundState(overlayWindowProbe.IsOsuForeground(snapshot));

    private void ApplyOverlayDisplayCompatibility(
        OverlayDisplayCompatibility compatibility)
    {
        overlayEngine.SetDisplayCompatibility(compatibility);
        mainViewModel.OverlaySettings.SetEditAvailability(compatibility.OverlaySupported);
        mainViewModel.ApplyOverlayDisplayCompatibility(compatibility);
        if (lastOverlayDisplayMode != compatibility.DisplayMode)
        {
            DebugLogger.Log(
                $"OVERLAY DISPLAY MODE = {compatibility.DisplayMode} | " +
                $"Supported={compatibility.OverlaySupported} | " +
                $"Visible={compatibility.OverlayVisible}");
            lastOverlayDisplayMode = compatibility.DisplayMode;
        }
    }

    private void ApplyOverlayForegroundState(bool isOsuForeground)
    {
        bool changed = overlayEngine.SetForegroundState(isOsuForeground);
        mainViewModel.ApplyOverlayForegroundState(
            isOsuForeground,
            overlayEngine.OverlayVisible);
        // BUG-003 : même règle de premier plan pour les Community Overlays gérés.
        communityOverlayManager?.SetOsuForeground(isOsuForeground);
        if (changed)
        {
            DebugLogger.Log(
                $"OVERLAY FOREGROUND = {isOsuForeground} | " +
                $"Visible={overlayEngine.OverlayVisible}");
        }
    }

    private void DisposeOverlayLifecycle()
    {
        mainViewModel.OverlaySettings.SettingsChanged -= ApplyOverlaySettings;
        mainViewModel.OverlaySettings.BeginEditRequested -= BeginOverlayEdit;
        mainViewModel.OverlaySettings.SaveEditRequested -= SaveOverlayEdit;
        mainViewModel.OverlaySettings.CancelEditRequested -= CancelOverlayEdit;
        mainViewModel.OverlaySettings.ResetEditRequested -= ResetOverlayEdit;
        mapOverviewOverlayController?.Dispose();
        mapOverviewOverlayController = null;
        skillsOverlayController?.Dispose();
        skillsOverlayController = null;
        gameplayHudOverlayController?.Dispose();
        gameplayHudOverlayController = null;
    }

    protected override void OnClosed(EventArgs e)
    {
        BeginShutdown();
        base.OnClosed(e);
    }
}
