using System.Runtime.CompilerServices;
using System.Windows;
using BeatInsight.Diagnostics;
using BeatInsight.Integration.CommunityOverlays;
using BeatInsight.Integration.CommunityOverlays.Pipe;
using BeatInsight.Integration.KeyOverlay;

namespace BeatInsight;

/// <summary>
/// Canal precise expérimental (V3.14-COM-KEY-04) : touches réelles d'osu!stable vers le compagnon
/// CommunityOverlays, sur un Named Pipe distinct du pipeline gameplay.
/// Désactivé par défaut : actif seulement avec l'argument <c>--community-overlays-precise</c>
/// ou la variable <c>BEATINSIGHT_COMMUNITY_OVERLAYS_PRECISE=1</c>.
/// Comme MainWindow.CommunityOverlays.cs, ce fichier n'ajoute aucun appel dans les fichiers existants :
/// il s'abonne à Loaded, puis Closed / ShutdownStarted pour l'arrêt.
/// </summary>
public partial class MainWindow
{
    public const string CommunityOverlaysPreciseArgument = "--community-overlays-precise";
    public const string CommunityOverlaysPreciseVariable = "BEATINSIGHT_COMMUNITY_OVERLAYS_PRECISE";
    public const string CommunityOverlaysPreciseModeVariable = "BEATINSIGHT_COMMUNITY_OVERLAYS_PRECISE_MODE";

    private static readonly TimeSpan PrecisePollingPeriod = TimeSpan.FromMilliseconds(10);

    private PreciseChannel? communityOverlayPrecise;

    internal static bool IsCommunityOverlaysPreciseEnabled(string[] args, string? variable) =>
        args.Any(arg => string.Equals(arg, CommunityOverlaysPreciseArgument, StringComparison.OrdinalIgnoreCase)) ||
        string.Equals(variable, "1", StringComparison.Ordinal);

    internal static PrecisePublishMode PrecisePublishModeFrom(string? variable) =>
        string.Equals(variable, "continuous", StringComparison.OrdinalIgnoreCase)
            ? PrecisePublishMode.Continuous
            : PrecisePublishMode.OnChange;

    [ModuleInitializer]
    internal static void RegisterCommunityOverlaysPrecise()
    {
        // Sans l'option, rien n'est enregistré : comportement de BeatInsight inchangé (tests compris).
        if (!IsCommunityOverlaysPreciseEnabled(
                Environment.GetCommandLineArgs(),
                Environment.GetEnvironmentVariable(CommunityOverlaysPreciseVariable)))
            return;

        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            LoadedEvent,
            new RoutedEventHandler(static (sender, _) => ((MainWindow)sender).StartCommunityOverlayPrecise()));
    }

    private void StartCommunityOverlayPrecise()
    {
        if (communityOverlayPrecise is not null)
            return;

        var server = new CommunityOverlayPipeServer(CommunityOverlayPrecisePipeProtocol.PipeName, log: DebugLogger.Info);
        if (!server.Start())
        {
            DebugLogger.Warning("COMMUNITY OVERLAY PRECISE | non démarré : nom de pipe indisponible");
            server.Dispose();
            return;
        }

        PrecisePublishMode mode = PrecisePublishModeFrom(Environment.GetEnvironmentVariable(CommunityOverlaysPreciseModeVariable));
        var source = new CommunityOverlayPreciseKeySource(gameStateService, DebugLogger.Info);
        var publisher = new CommunityOverlayPrecisePublisher(server.Publish, mode);
        // COM-LAZER-KEY-03 (opt-in) : v2 en plus du v1. Sans l'option, la boucle publie le v1 seul, comme avant.
        PreciseV2Channel? v2 = StartCommunityOverlayPreciseV2();
        Action<KeyOverlaySample> onSample = v2 is null
            ? publisher.OnSample
            : sample =>
            {
                publisher.OnSample(sample);
                v2.OnStableSample(sample);
            };
        // Thread dédié, hors Dispatcher WPF, indépendant du mapTimer, du pipe gameplay et du canal JSON.
        var loop = new KeyOverlayPollingLoop(source.Read, onSample, PrecisePollingPeriod);
        communityOverlayPrecise = new PreciseChannel(server, source, publisher, loop, v2);
        loop.Start();

        DebugLogger.Info($"COMMUNITY OVERLAY PRECISE | démarré ({CommunityOverlayPrecisePipeProtocol.PipeName}, v{CommunityOverlayPrecisePipeProtocol.Version}, {mode}, {1000 / PrecisePollingPeriod.TotalMilliseconds:0} Hz)");
        Closed += (_, _) => StopCommunityOverlayPrecise();
        Dispatcher.ShutdownStarted += (_, _) => StopCommunityOverlayPrecise();
    }

    private void StopCommunityOverlayPrecise()
    {
        PreciseChannel? channel = communityOverlayPrecise;
        communityOverlayPrecise = null;
        channel?.Dispose();
    }

    private sealed class PreciseChannel(
        CommunityOverlayPipeServer server,
        CommunityOverlayPreciseKeySource source,
        CommunityOverlayPrecisePublisher publisher,
        KeyOverlayPollingLoop loop,
        PreciseV2Channel? v2) : IDisposable
    {
        public void Dispose()
        {
            // Ordre : plus de tick (boucle arrêtée et jointe), puis plus d'accès natif, puis le pipe.
            loop.Dispose();
            v2?.Dispose();
            source.Dispose();
            KeyOverlayLoopStats st = loop.Stats;
            DebugLogger.Info(
                $"COMMUNITY OVERLAY PRECISE | arrêté : {st.Ticks} lectures ({st.ValidSamples} valides, {st.SkippedTicks} sautées), " +
                $"{publisher.MessagesPublished} messages ({publisher.HeartbeatsPublished} battements, {publisher.BytesPublished} octets), " +
                $"{server.MessagesWritten} écrits, {source.Attachments} attachement(s), " +
                $"temps de lecture cumulé {st.TickDurationSumMs:0} ms (max {st.TickDurationMaxMs:0.0} ms) sur {st.WallSeconds:0} s");
            server.Dispose();
        }
    }
}
