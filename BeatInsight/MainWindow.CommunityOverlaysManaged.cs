using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using BeatInsight.Diagnostics;
using BeatInsight.Services.CommunityOverlays;
using BeatInsight.ViewModels;

namespace BeatInsight;

/// <summary>
/// FEAT-003 : overlays communautaires gérés depuis Settings → Overlay → Community Overlays.
/// <list type="bullet">
/// <item>Compagnon géré : lancé par BeatInsight avec <c>--managed-by</c>, arrêté avec lui (étape de fermeture
/// « community overlays stopped », puis auto-arrêt du compagnon si BeatInsight disparaît).</item>
/// <item>Compagnon manuel (diagnostic CLI) : lancé à la main, jamais piloté ni arrêté par BeatInsight ;
/// les pipes restent ouverts par les options de diagnostic existantes.</item>
/// <item>Les pipes ne sont ouverts que lorsqu'un overlay géré est actif, ou par ces options de diagnostic.</item>
/// </list>
/// </summary>
public partial class MainWindow
{
    private CommunityOverlayManager? communityOverlayManager;
    private bool communityOverlaysManagedPipesRequested;
    private Overlay.ForegroundWindowChangedHook? communityOverlayForegroundHook;

    [ModuleInitializer]
    internal static void RegisterManagedCommunityOverlays()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            LoadedEvent,
            new RoutedEventHandler(static (sender, _) => ((MainWindow)sender).StartManagedCommunityOverlays()));
    }

    private void StartManagedCommunityOverlays()
    {
        if (communityOverlayManager is not null || shutdownCoordinator.State != Services.Lifecycle.ShutdownState.Running)
            return;

        var launcher = new CommunityOverlayCompanionLauncher(
            CommunityOverlayCompanionLauncher.ResolveExecutablePath(
                Environment.GetEnvironmentVariable(CommunityOverlayCompanionLauncher.CompanionVariable),
                AppContext.BaseDirectory),
            DebugLogger.Info);
        var manager = new CommunityOverlayManager(
            new CommunityOverlayLibraryStore(),
            launcher,
            Environment.ProcessId,
            action => Dispatcher.BeginInvoke(action),
            new DispatcherCommunityOverlayScheduler(Dispatcher),
            () => communityOverlayPipeServer?.ConnectedClients ?? 0,
            SetManagedCommunityOverlayPipes,
            DebugLogger.Info);
        communityOverlayManager = manager;
        mainViewModel.CommunityOverlays.Attach(manager);
        // BUG-003 : état de premier plan courant transmis dès le lancement, puis réévalué à chaque changement
        // de fenêtre active (Alt+Tab) en plus du tick de polling.
        manager.SetOsuForeground(overlayEngine.OsuIsForeground);
        communityOverlayForegroundHook ??= new Overlay.ForegroundWindowChangedHook(() =>
        {
            if (shutdownCoordinator.State == Services.Lifecycle.ShutdownState.Running)
                RefreshOverlayForeground(currentGameState ?? gameStateService.Current);
        });
        manager.LoadAndStartEnabled();
        DebugLogger.Info($"COMMUNITY OVERLAYS | gestionnaire prêt (compagnon : {launcher.ExecutablePath ?? "absent"})");
    }

    /// <summary>Étape de fermeture : arrête les compagnons gérés (jamais les compagnons manuels).</summary>
    private void StopManagedCommunityOverlays()
    {
        communityOverlayForegroundHook?.Dispose();
        communityOverlayForegroundHook = null;
        CommunityOverlayManager? manager = communityOverlayManager;
        communityOverlayManager = null;
        if (manager is null)
            return;

        mainViewModel.CommunityOverlays.Detach();
        manager.Dispose();
    }

    private void SetManagedCommunityOverlayPipes(bool active)
    {
        communityOverlaysManagedPipesRequested = active;
        string[] args = Environment.GetCommandLineArgs();
        if (active)
        {
            StartCommunityOverlayBridge();
            StartCommunityOverlayPrecise();
            StartCommunityOverlaySelection();
            return;
        }

        // Un canal ouvert par une option de diagnostic reste ouvert.
        if (!IsCommunityOverlaysPipeEnabled(args, Environment.GetEnvironmentVariable(CommunityOverlaysPipeVariable)))
            StopCommunityOverlayBridge();
        if (!IsCommunityOverlaysPreciseEnabled(args, Environment.GetEnvironmentVariable(CommunityOverlaysPreciseVariable)))
            StopCommunityOverlayPrecise();
        if (!IsCommunityOverlaysSelectionEnabled(args, Environment.GetEnvironmentVariable(CommunityOverlaysSelectionVariable)))
            StopCommunityOverlaySelection();
    }

    private sealed class DispatcherCommunityOverlayScheduler(Dispatcher dispatcher) : ICommunityOverlayScheduler
    {
        public IDisposable Schedule(TimeSpan delay, Action action)
        {
            var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = delay };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                action();
            };
            timer.Start();
            return new Cancellation(timer);
        }

        private sealed class Cancellation(DispatcherTimer timer) : IDisposable
        {
            public void Dispose() => timer.Stop();
        }
    }
}
