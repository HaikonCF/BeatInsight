using System.Runtime.CompilerServices;
using System.Windows;
using BeatInsight.Diagnostics;
using BeatInsight.Integration.CommunityOverlays;
using BeatInsight.Integration.CommunityOverlays.Pipe;
using BeatInsight.Integration.SongSelect;

namespace BeatInsight;

/// <summary>
/// Canal « selection » expérimental (V3.14-COM-PP-03) : PP SS de la beatmap sélectionnée dans osu!stable, avec les
/// mods actifs, vers le compagnon CommunityOverlays (pipe v3 distinct ; v1, v2 et precise inchangés).
/// Désactivé par défaut : actif seulement avec l'argument <c>--community-overlays-selection</c>
/// ou la variable <c>BEATINSIGHT_COMMUNITY_OVERLAYS_SELECTION=1</c>.
/// Comme MainWindow.CommunityOverlays.cs, ce fichier n'ajoute aucun appel dans les fichiers existants :
/// il s'abonne à Loaded, puis Closed / ShutdownStarted pour l'arrêt.
/// </summary>
public partial class MainWindow
{
    public const string CommunityOverlaysSelectionArgument = "--community-overlays-selection";
    public const string CommunityOverlaysSelectionVariable = "BEATINSIGHT_COMMUNITY_OVERLAYS_SELECTION";

    private SelectionChannel? communityOverlaySelection;

    internal static bool IsCommunityOverlaysSelectionEnabled(string[] args, string? variable) =>
        args.Any(arg => string.Equals(arg, CommunityOverlaysSelectionArgument, StringComparison.OrdinalIgnoreCase)) ||
        string.Equals(variable, "1", StringComparison.Ordinal);

    [ModuleInitializer]
    internal static void RegisterCommunityOverlaysSelection()
    {
        // Sans l'option, rien n'est enregistré : comportement de BeatInsight inchangé (tests compris).
        if (!IsCommunityOverlaysSelectionEnabled(
                Environment.GetCommandLineArgs(),
                Environment.GetEnvironmentVariable(CommunityOverlaysSelectionVariable)))
            return;

        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            LoadedEvent,
            new RoutedEventHandler(static (sender, _) => ((MainWindow)sender).StartCommunityOverlaySelection()));
    }

    private void StartCommunityOverlaySelection()
    {
        if (communityOverlaySelection is not null)
            return;

        var server = new CommunityOverlayPipeServer(CommunityOverlaySelectionPipeProtocol.PipeName, log: DebugLogger.Info);
        if (!server.Start())
        {
            DebugLogger.Warning("SONGSELECT PP | non démarré : nom de pipe indisponible");
            server.Dispose();
            return;
        }

        var reader = new NativeStableSongSelectModsReader(new GameStateSongSelectModsHost(gameStateService), log: DebugLogger.Info);
        var service = new SongSelectPerformanceService();
        var publisher = new CommunityOverlaySelectionPublisher(reader.Observe, () => gameStateService.Current, service, server.Publish,
            log: DebugLogger.Info);
        communityOverlaySelection = new SelectionChannel(server, reader, service, publisher);
        publisher.Start();

        DebugLogger.Info($"SONGSELECT PP | démarré ({CommunityOverlaySelectionPipeProtocol.PipeName}, v{CommunityOverlaySelectionPipeProtocol.Version}, calculateur {service.CalculatorVersion})");
        Closed += (_, _) => StopCommunityOverlaySelection();
        Dispatcher.ShutdownStarted += (_, _) => StopCommunityOverlaySelection();
    }

    private void StopCommunityOverlaySelection()
    {
        SelectionChannel? channel = communityOverlaySelection;
        communityOverlaySelection = null;
        channel?.Dispose();
    }

    private sealed class SelectionChannel(
        CommunityOverlayPipeServer server,
        NativeStableSongSelectModsReader reader,
        SongSelectPerformanceService service,
        CommunityOverlaySelectionPublisher publisher) : IDisposable
    {
        public void Dispose()
        {
            // Ordre : plus de tick, puis plus d'accès natif, puis plus de calcul, puis le pipe.
            publisher.Dispose();
            reader.Dispose();
            SongSelectPerformanceStatistics st = service.Statistics;
            service.Dispose();
            double[] reactions = publisher.ReactionTimesMs.Order().ToArray();
            string reaction = reactions.Length == 0
                ? "aucune"
                : $"médiane {reactions[reactions.Length / 2]:0} ms, max {reactions[^1]:0} ms sur {reactions.Length}";
            DebugLogger.Info(
                $"SONGSELECT PP | arrêté : {publisher.Published} messages ({publisher.ValidMessages} valides, {publisher.SkippedTicks} ticks sautés, " +
                $"tick max {publisher.TickMaxMs:0.0} ms, cumul {publisher.TickTotalMs:0} ms), " +
                $"découvertes {reader.Discoveries} ({reader.DiscoveryFailures} échecs), lectures du masque {reader.MaskReads}, " +
                $"cache {st.CacheHits} hits / {st.CacheMisses} miss ({st.CacheEntries} entrées), {st.Computations} calculs " +
                $"(total {st.ComputeTotalMs:0} ms, max {st.ComputeMaxMs:0} ms), {st.Cancelled} annulés, {st.StaleRejected} périmés, " +
                $"{st.Failures} échecs ; réaction {reaction}");
            server.Dispose();
        }
    }
}
