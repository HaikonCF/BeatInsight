using System.Runtime.CompilerServices;
using System.Windows;
using BeatInsight.Diagnostics;
using BeatInsight.Integration.CommunityOverlays;
using BeatInsight.Integration.CommunityOverlays.Pipe;

namespace BeatInsight;

/// <summary>
/// Raccordement en lecture seule au compagnon CommunityOverlays (V3.14-COM-10).
/// Désactivé par défaut : actif seulement avec l'argument <c>--community-overlays-pipe</c>
/// ou la variable d'environnement <c>BEATINSIGHT_COMMUNITY_OVERLAYS_PIPE=1</c>.
/// Ce fichier n'ajoute aucun appel dans les fichiers existants : il s'abonne à
/// l'événement Loaded de MainWindow et ne fait que lire des objets déjà produits.
/// </summary>
public partial class MainWindow
{
    public const string CommunityOverlaysPipeArgument = "--community-overlays-pipe";
    public const string CommunityOverlaysPipeVariable = "BEATINSIGHT_COMMUNITY_OVERLAYS_PIPE";

    private CommunityOverlayLiveBridge? communityOverlayBridge;
    private CommunityOverlayPipeServer? communityOverlayPipeServer;

    internal static bool IsCommunityOverlaysPipeEnabled(string[] args, string? variable) =>
        args.Any(arg => string.Equals(arg, CommunityOverlaysPipeArgument, StringComparison.OrdinalIgnoreCase)) ||
        string.Equals(variable, "1", StringComparison.Ordinal);

    [ModuleInitializer]
    internal static void RegisterCommunityOverlaysPipe()
    {
        // Sans l'option, rien n'est enregistré : comportement de BeatInsight inchangé (tests compris).
        if (!IsCommunityOverlaysPipeEnabled(
                Environment.GetCommandLineArgs(),
                Environment.GetEnvironmentVariable(CommunityOverlaysPipeVariable)))
            return;

        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            LoadedEvent,
            new RoutedEventHandler(static (sender, _) => ((MainWindow)sender).StartCommunityOverlayBridge()));
    }

    private void StartCommunityOverlayBridge()
    {
        if (communityOverlayBridge is not null)
            return;

        var server = new CommunityOverlayPipeServer(log: DebugLogger.Info);
        var bridge = new CommunityOverlayLiveBridge(
            CaptureCommunityOverlayFrame,
            server,
            log: DebugLogger.Info,
            // V3.14-COM-11 : pipe v1 conservé à l'identique pour les compagnons COM-10.
            legacyServer: new CommunityOverlayPipeServer(CommunityOverlayPipeProtocol.LegacyPipeNameV1, log: DebugLogger.Info));
        if (!bridge.Start())
        {
            DebugLogger.Warning("COMMUNITY OVERLAY PIPE | non démarré : nom de pipe indisponible");
            bridge.Dispose();
            return;
        }

        communityOverlayBridge = bridge;
        communityOverlayPipeServer = server;
        DebugLogger.Info($"COMMUNITY OVERLAY PIPE | démarré ({CommunityOverlayPipeProtocol.PipeName}, v{CommunityOverlayPipeProtocol.Version} ; {CommunityOverlayPipeProtocol.LegacyPipeNameV1}, v1)");
        Closed += (_, _) => StopCommunityOverlayBridge();
        Dispatcher.ShutdownStarted += (_, _) => StopCommunityOverlayBridge();
    }

    private void StopCommunityOverlayBridge()
    {
        communityOverlayBridge?.Dispose();
        communityOverlayBridge = null;
        communityOverlayPipeServer = null;
    }

    /// <summary>
    /// Appelée sur le pool de threads. Lectures uniquement : CurrentSnapshot et IsOverlayEditMode
    /// sont protégés par le verrou d'OverlayEngine, Current est une lecture volatile, et les
    /// drapeaux booléens sont lus sans effet de bord (une valeur en retard d'un tick est sans gravité).
    /// </summary>
    private CommunityOverlaySourceFrame CaptureCommunityOverlayFrame() => new(
        overlayEngine.CurrentSnapshot,
        gameStateService.Current,
        overlayEngine.IsOverlayEditMode,
        Volatile.Read(ref isFastLabelingMode),
        IsBackgroundLibraryWorkRunning);
}
