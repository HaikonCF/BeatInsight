using BeatInsight.Diagnostics;
using BeatInsight.Integration.CommunityOverlays;
using BeatInsight.Integration.CommunityOverlays.Pipe;
using BeatInsight.Integration.KeyOverlay;
using BeatInsight.Integration.KeyOverlay.Lazer;

namespace BeatInsight;

/// <summary>
/// Canal precise v2 expérimental (V3.14-COM-LAZER-KEY-03) : Stable et Lazer, disponibilité explicite des compteurs, sur un
/// pipe distinct du v1. Actif seulement avec le canal precise ET l'argument <c>--community-overlays-precise-v2</c>
/// (ou <c>BEATINSIGHT_COMMUNITY_OVERLAYS_PRECISE_V2=1</c>). Sans cette option, rien n'est créé : le canal precise v1
/// fonctionne exactement comme avant.
/// La partie Lazer exige en plus le processus lecteur (<c>LazerKeys\LazerKeyOverlayRunner.exe</c> à côté de BeatInsight,
/// ou le chemin de <c>BEATINSIGHT_LAZER_KEYS_RUNNER</c>) ; sans lui, la v2 publie Stable seul.
/// </summary>
public partial class MainWindow
{
    public const string CommunityOverlaysPreciseV2Argument = "--community-overlays-precise-v2";
    public const string CommunityOverlaysPreciseV2Variable = "BEATINSIGHT_COMMUNITY_OVERLAYS_PRECISE_V2";

    internal static bool IsCommunityOverlaysPreciseV2Enabled(string[] args, string? variable) =>
        args.Any(arg => string.Equals(arg, CommunityOverlaysPreciseV2Argument, StringComparison.OrdinalIgnoreCase)) ||
        string.Equals(variable, "1", StringComparison.Ordinal);

    /// <summary>Null si l'option est désactivée ou si le pipe v2 n'a pas pu être créé.</summary>
    private PreciseV2Channel? StartCommunityOverlayPreciseV2()
    {
        // FEAT-003 : les overlays gérés demandent aussi la v2 (--precise-v2 côté compagnon).
        if (!communityOverlaysManagedPipesRequested &&
            !IsCommunityOverlaysPreciseV2Enabled(Environment.GetCommandLineArgs(),
                Environment.GetEnvironmentVariable(CommunityOverlaysPreciseV2Variable)))
            return null;

        var server = new CommunityOverlayPipeServer(CommunityOverlayPreciseV2PipeProtocol.PipeName, log: DebugLogger.Info);
        if (!server.Start())
        {
            DebugLogger.Warning("COMMUNITY OVERLAY PRECISE V2 | non démarré : nom de pipe indisponible");
            server.Dispose();
            return null;
        }
        var publisher = new CommunityOverlayPreciseV2Publisher(server.Publish, () => gameStateService.Current);
        LazerKeyRunnerSource? lazer = null;
        string? runner = LazerKeyRunnerSource.ResolveRunnerPath(
            Environment.GetEnvironmentVariable(LazerKeyRunnerSource.RunnerVariable), AppContext.BaseDirectory);
        if (runner is not null)
        {
            lazer = new LazerKeyRunnerSource(runner, DebugLogger.Info);
            lazer.SampleReceived += publisher.OnLazerSample;
            lazer.Start();
        }
        publisher.Start();
        DebugLogger.Info($"COMMUNITY OVERLAY PRECISE V2 | démarré ({CommunityOverlayPreciseV2PipeProtocol.PipeName}, " +
                         $"Lazer : {(runner is null ? "lecteur absent, Stable seul" : runner)})");
        return new PreciseV2Channel(server, publisher, lazer);
    }

    private sealed class PreciseV2Channel(
        CommunityOverlayPipeServer server,
        CommunityOverlayPreciseV2Publisher publisher,
        LazerKeyRunnerSource? lazer) : IDisposable
    {
        public void OnStableSample(KeyOverlaySample sample) => publisher.OnStableSample(sample);

        public void Dispose()
        {
            // Ordre : plus de lecteur Lazer, plus de battement, puis le pipe.
            lazer?.Dispose();
            publisher.Dispose();
            DebugLogger.Info(
                $"COMMUNITY OVERLAY PRECISE V2 | arrêté : {publisher.MessagesPublished} messages ({publisher.HeartbeatsPublished} battements, " +
                $"{publisher.AmbiguousMessages} sources ambiguës, {publisher.StaleSourceMessages} sources silencieuses) ; " +
                $"lecteur Lazer : {lazer?.Starts ?? 0} démarrage(s), {lazer?.Lines ?? 0} lignes, {lazer?.Rejected ?? 0} rejetées, " +
                $"{lazer?.OutOfOrder ?? 0} hors séquence ; {server.MessagesWritten} écrits");
            server.Dispose();
        }
    }
}
