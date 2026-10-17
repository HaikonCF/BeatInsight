using System.IO;

namespace BeatInsight.CommunityOverlays.Poc;

/// <summary>
/// Arguments :
/// <c>--overlay &lt;dossier&gt;</c> dossier contenant index.html (défaut : SampleOverlay),
/// <c>--devtools</c> active les DevTools WebView2,
/// <c>--smoke &lt;dossier&gt;</c> exécute une vérification automatique puis quitte,
/// <c>--probe &lt;expression JS&gt;</c> valeur supplémentaire relevée à chaque échantillon,
/// <c>--samples 1500,8000,...</c> instants d'échantillonnage (ms depuis le démarrage du mock),
/// <c>--disconnect-at &lt;ms&gt;</c> coupure simulée des sockets (0 = aucune),
/// <c>--beatmap-assets-root &lt;dossier&gt;</c> racine Songs autorisée pour <c>/files/beatmap/*</c>
/// (absente = ressources de beatmap désactivées, réponses 404),
/// <c>--window x,y,largeur,hauteur</c> position et taille de la fenêtre (unités WPF),
/// <c>--profile per-overlay|shared</c> profil WebView2 propre à l'overlay (défaut) ou profil partagé,
/// <c>--fps-cap &lt;n&gt;</c> (expérimental COM-05) limite requestAnimationFrame à n images/s,
/// <c>--throttle-main-frame</c> (expérimental COM-05) active la fonctionnalité Chromium ThrottleMainFrameTo60Hz,
/// <c>--trace-frames</c> (diagnostic) compte les images réellement produites via une trace CDP,
/// <c>--publish-priority background|normal|...</c> priorité du timer de publication (défaut : normal, O2),
/// <c>--visibility-manager on|off</c> suspension des overlays non visibles (défaut : on, O1),
/// <c>--publish-timer compensated|dispatcher</c> minuteur de publication (défaut : compensated, COM-07),
/// <c>--ui-busy-at &lt;ms&gt;</c> + <c>--ui-busy-ms &lt;ms&gt;</c> (diagnostic) bloque le thread UI pour tester l'absence de rafale,
/// <c>--minimize-at</c>, <c>--restore-at</c>, <c>--disable-at</c>, <c>--enable-at</c>,
/// <c>--simulate-exclusive-at</c>, <c>--simulate-exclusive-until</c> (ms, diagnostic) : événements de visibilité scriptés,
/// <c>--precise-pipe</c> (COM-KEY-04, expérimental) touches réelles via le pipe precise,
/// <c>--precise-diagnostics &lt;fichier&gt;</c> (diagnostic) relevé périodique du canal precise,
/// <c>--selection-pipe</c> (COM-PP-03, expérimental) PP SS de la sélection via le pipe v3,
/// <c>--managed-by &lt;pid&gt;</c> (FEAT-003) mode géré par BeatInsight : arrêt avec ce processus, instance unique par overlay,
/// commandes sur stdin et événements sur stdout. Sans cette option, le POC reste autonome et inchangé,
/// <c>--scale &lt;pourcentage&gt;</c> (FEAT-003.1) échelle du contenu par le zoom WebView2, 25 à 200 au pas de 5 (défaut 100 : zoom non modifié).
/// </summary>
public sealed record PocOptions(
    string OverlayFolder,
    bool DevTools,
    string? SmokeOutputFolder,
    string? Probe,
    IReadOnlyList<int> SampleTimesMs,
    int DisconnectAtMs,
    string? BeatmapAssetsRoot = null,
    WindowBounds? WindowBounds = null,
    OverlayProfileMode Profile = OverlayProfileMode.PerOverlay,
    int FpsCap = 0,
    bool ThrottleMainFrame = false,
    bool TraceFrames = false)
{
    /// <summary>O2 (V3.14-COM-06) : priorité du timer de publication du mock.</summary>
    public System.Windows.Threading.DispatcherPriority PublishPriority { get; init; } = System.Windows.Threading.DispatcherPriority.Normal;

    /// <summary>V3.14-COM-07 : minuteur de publication (compensé par défaut).</summary>
    public Adapter.PublishTimerMode PublishTimer { get; init; } = Adapter.PublishTimerMode.Compensated;

    /// <summary>Diagnostic COM-07 : instant (ms) et durée (ms) d'un blocage volontaire du thread UI.</summary>
    public (int AtMs, int DurationMs)? UiBusy { get; init; }

    /// <summary>
    /// V3.14-COM-09 : source des données. <c>mock</c> (défaut), <c>synthetic-live</c>
    /// (script d'entrées BeatInsight passé par l'adaptateur live) ou <c>live-pipe</c>
    /// (V3.14-COM-10 : BeatInsight réel par Named Pipe). Une seule source à la fois.
    /// </summary>
    public string Source { get; init; } = "mock";

    /// <summary>
    /// V3.14-COM-KEY-04 (expérimental, désactivé par défaut) : <c>--precise-pipe</c> alimente
    /// <c>/websocket/v2/precise</c> avec les touches réelles du pipe precise de BeatInsight.
    /// </summary>
    public bool PrecisePipe { get; init; }

    /// <summary>
    /// Diagnostic COM-KEY-04 : <c>--precise-diagnostics &lt;fichier.jsonl&gt;</c> relève chaque seconde
    /// les compteurs affichés par la page et les statistiques precise.
    /// </summary>
    public string? PreciseDiagnosticsFile { get; init; }

    /// <summary>
    /// V3.14-COM-PP-03 (expérimental, désactivé par défaut) : <c>--selection-pipe</c> lit le pipe v3 « selection »
    /// de BeatInsight pour afficher le PP SS de la beatmap sélectionnée (Simplistic <c>menu.pp</c>, Triangles
    /// <c>performance.accuracy</c>).
    /// </summary>
    public bool SelectionPipe { get; init; }

    /// <summary>
    /// V3.14-COM-LAZER-KEY-03 (expérimental) : <c>--precise-v2</c>, avec <c>--precise-pipe</c>, préfère le pipe precise v2
    /// (Stable et Lazer, disponibilité explicite des compteurs) et ne se replie sur le v1 que s'il est absent ou muet.
    /// </summary>
    public bool PreciseV2 { get; init; }

    /// <summary>
    /// FEAT-003 : processus BeatInsight propriétaire (<c>--managed-by &lt;pid&gt;</c>). Null = mode CLI autonome.
    /// </summary>
    public int? ManagedByPid { get; init; }

    public bool IsManaged => ManagedByPid is not null;

    /// <summary>FEAT-003.1 : échelle du contenu en pourcentage (zoom WebView2). 100 = comportement d'origine.</summary>
    public int ScalePercent { get; init; } = BeatInsight.Integration.CommunityOverlays.Managed.CommunityOverlayCompanionProtocol.DefaultScalePercent;

    /// <summary>O1 (V3.14-COM-06) : suspension WebView2 des overlays non visibles.</summary>
    public bool VisibilityManager { get; init; } = true;

    /// <summary>Événements de visibilité scriptés (ms depuis le démarrage), pour les mesures.</summary>
    public IReadOnlyDictionary<string, int> ScriptedVisibilityEvents { get; init; } = new Dictionary<string, int>();

    /// <summary>
    /// Couvre un cycle mock complet : SongSelect (0–3 s) → Gameplay (3–35 s) →
    /// Results (35–40 s) → SongSelect (40 s+), avec une coupure à 20 s.
    /// </summary>
    public static readonly IReadOnlyList<int> DefaultSampleTimesMs = [1_500, 8_000, 19_500, 23_000, 37_000, 41_500];

    public static PocOptions Parse(string[] args)
    {
        string overlay = Path.Combine(AppContext.BaseDirectory, "SampleOverlay");
        bool devTools = false;
        string? smoke = null;
        string? probe = null;
        IReadOnlyList<int> samples = DefaultSampleTimesMs;
        int disconnectAt = 20_000;
        string? beatmapAssetsRoot = null;
        WindowBounds? window = null;
        var profile = OverlayProfileMode.PerOverlay;
        int fpsCap = 0;
        bool throttleMainFrame = false;
        bool traceFrames = false;
        var publishPriority = System.Windows.Threading.DispatcherPriority.Normal;
        bool visibilityManager = true;
        var scripted = new Dictionary<string, int>();
        var publishTimer = Adapter.PublishTimerMode.Compensated;
        int uiBusyAt = 0, uiBusyMs = 0;
        string source = "mock";
        bool precisePipe = false;
        string? preciseDiagnostics = null;
        bool selectionPipe = false;
        bool preciseV2 = false;
        int? managedBy = null;
        int scale = BeatInsight.Integration.CommunityOverlays.Managed.CommunityOverlayCompanionProtocol.DefaultScalePercent;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--overlay" when i + 1 < args.Length:
                    overlay = args[++i];
                    break;
                case "--devtools":
                    devTools = true;
                    break;
                case "--smoke" when i + 1 < args.Length:
                    smoke = args[++i];
                    break;
                case "--probe" when i + 1 < args.Length:
                    probe = args[++i];
                    break;
                case "--samples" when i + 1 < args.Length:
                    samples = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
                    break;
                case "--disconnect-at" when i + 1 < args.Length:
                    disconnectAt = int.Parse(args[++i]);
                    break;
                case "--beatmap-assets-root" when i + 1 < args.Length:
                    beatmapAssetsRoot = Path.GetFullPath(args[++i]);
                    break;
                case "--window" when i + 1 < args.Length:
                    double[] v = args[++i].Split(',').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                    window = new WindowBounds(v[0], v[1], v[2], v[3]);
                    break;
                case "--fps-cap" when i + 1 < args.Length:
                    fpsCap = Math.Clamp(int.Parse(args[++i]), 0, 1000);
                    break;
                case "--throttle-main-frame":
                    throttleMainFrame = true;
                    break;
                case "--trace-frames":
                    traceFrames = true;
                    break;
                case "--publish-priority" when i + 1 < args.Length:
                    publishPriority = Enum.Parse<System.Windows.Threading.DispatcherPriority>(args[++i], ignoreCase: true);
                    break;
                case "--publish-timer" when i + 1 < args.Length:
                    publishTimer = args[++i] == "dispatcher" ? Adapter.PublishTimerMode.Dispatcher : Adapter.PublishTimerMode.Compensated;
                    break;
                case "--source" when i + 1 < args.Length:
                    source = args[++i] switch
                    {
                        "synthetic-live" => "synthetic-live",
                        "live-pipe" => "live-pipe",
                        _ => "mock",
                    };
                    break;
                case "--precise-pipe":
                    precisePipe = true;
                    break;
                case "--precise-v2":
                    preciseV2 = true;
                    break;
                case "--managed-by" when i + 1 < args.Length:
                    managedBy = int.TryParse(args[++i], System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out int pid) && pid > 0 ? pid : null;
                    break;
                case "--scale" when i + 1 < args.Length:
                    if (double.TryParse(args[++i], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double percent))
                        scale = BeatInsight.Integration.CommunityOverlays.Managed.CommunityOverlayCompanionProtocol.NormalizeScale(percent);
                    break;
                case "--selection-pipe":
                    selectionPipe = true;
                    break;
                case "--precise-diagnostics" when i + 1 < args.Length:
                    preciseDiagnostics = Path.GetFullPath(args[++i]);
                    break;
                case "--ui-busy-at" when i + 1 < args.Length:
                    uiBusyAt = int.Parse(args[++i]);
                    break;
                case "--ui-busy-ms" when i + 1 < args.Length:
                    uiBusyMs = int.Parse(args[++i]);
                    break;
                case "--visibility-manager" when i + 1 < args.Length:
                    visibilityManager = args[++i] != "off";
                    break;
                case "--minimize-at" or "--restore-at" or "--disable-at" or "--enable-at"
                    or "--simulate-exclusive-at" or "--simulate-exclusive-until" when i + 1 < args.Length:
                    scripted[args[i][2..]] = int.Parse(args[++i]);
                    break;
                case "--profile" when i + 1 < args.Length:
                    profile = args[++i] == "shared" ? OverlayProfileMode.Shared : OverlayProfileMode.PerOverlay;
                    break;
            }
        }

        return new PocOptions(overlay, devTools, smoke, probe, samples, disconnectAt, beatmapAssetsRoot, window, profile, fpsCap, throttleMainFrame, traceFrames)
        {
            PublishPriority = publishPriority,
            VisibilityManager = visibilityManager,
            ScriptedVisibilityEvents = scripted,
            PublishTimer = publishTimer,
            Source = source,
            PrecisePipe = precisePipe,
            PreciseDiagnosticsFile = preciseDiagnostics,
            SelectionPipe = selectionPipe,
            PreciseV2 = preciseV2,
            ManagedByPid = managedBy,
            ScalePercent = scale,
            UiBusy = uiBusyAt > 0 && uiBusyMs > 0 ? (uiBusyAt, uiBusyMs) : null,
        };
    }
}

public sealed record WindowBounds(double X, double Y, double Width, double Height);

public enum OverlayProfileMode
{
    /// <summary>Profil WebView2 dédié à l'overlay (défaut depuis V3.14-COM-03).</summary>
    PerOverlay,
    /// <summary>Profil par défaut partagé par tous les overlays (comportement COM-01/02).</summary>
    Shared,
}
