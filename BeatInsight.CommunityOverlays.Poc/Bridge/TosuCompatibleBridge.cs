using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BeatInsight.CommunityOverlays.Poc.Adapter;
using Microsoft.Web.WebView2.Core;

namespace BeatInsight.CommunityOverlays.Poc.Bridge;

/// <summary>
/// Pont tosu-compatible entre une <see cref="ICommunityOverlayDataSource"/>
/// et une page WebView2. Tout passe par l'hôte WebView2 (postMessage et
/// WebResourceRequested) : aucun socket, aucun port, aucun serveur.
/// Le bridge sert aussi les fichiers statiques de l'overlay, confinés à son
/// dossier, et bloque toute requête hors de l'origine de l'overlay.
/// </summary>
public sealed class TosuCompatibleBridge : IDisposable
{
    private const string ShimResourceName = "BeatInsight.CommunityOverlays.Poc.tosu-bridge-shim.js";

    /// <summary>
    /// Politique appliquée à toutes les pages servies : code tiers non fiable,
    /// aucune ressource ni connexion hors de l'origine de l'overlay.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval' blob:; " +
        "style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data:; " +
        "media-src 'self' data: blob:; connect-src 'self'; worker-src 'self' blob:; " +
        "frame-src 'self'; object-src 'none'; base-uri 'self'; form-action 'none'";

    private readonly CoreWebView2 _webView;
    private readonly ICommunityOverlayDataSource _source;
    private readonly string _virtualHost;
    private readonly string _overlayRoot;
    private readonly TosuCommandHandler _commands;
    private readonly BeatmapAssetProvider _beatmapAssets;

    /// <summary>Taille maximale lue en mémoire pour une réponse /files/beatmap (au-delà : intervalles uniquement).</summary>
    public const long MaxAssetResponseBytes = 64L * 1024 * 1024;

    private string _lastV2 = "{}";
    private string _lastV1 = "{}";
    private string _lastPrecise = "{}";
    private bool _preciseLazerGameplay;

    public TosuCompatibleBridge(
        CoreWebView2 webView, ICommunityOverlayDataSource source, string virtualHost, string overlayFolder,
        BeatmapAssetProvider? beatmapAssets = null)
    {
        _webView = webView;
        _source = source;
        _virtualHost = virtualHost;
        _overlayRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(overlayFolder)) + Path.DirectorySeparatorChar;
        CounterPath = Path.GetFileName(Path.TrimEndingDirectorySeparator(_overlayRoot));
        _commands = new TosuCommandHandler(CounterPath, () => CounterSettingsReader.ReadDefaults(_overlayRoot));
        _beatmapAssets = beatmapAssets ?? new BeatmapAssetProvider((string?)null);
    }

    /// <summary>Équivalent de <c>window.COUNTER_PATH</c> injecté par tosu : nom du dossier de l'overlay.</summary>
    public string CounterPath { get; }

    public long MessagesPosted { get; private set; }

    /// <summary>Messages reçus de l'overlay (filtres, commandes), pour diagnostic.</summary>
    public List<string> ReceivedFromOverlay { get; } = [];

    public IReadOnlyDictionary<string, string> ReceivedFilters => _commands.ReceivedFilters;

    /// <summary>Derniers payloads exacts, exposés uniquement au diagnostic hôte.</summary>
    public string LastV2 => _lastV2;
    public string? LastCommandReply { get; private set; }

    /// <summary>Diagnostic : chaque requête vue par le bridge (uri, statut HTTP renvoyé).</summary>
    public event Action<string, int>? RequestObserved;

    /// <summary>
    /// Diagnostic (smoke test uniquement) : ferme côté hôte toutes les sockets
    /// émulées, comme une coupure de tosu, pour observer la reconnexion de l'overlay.
    /// </summary>
    public void SimulateDisconnect() =>
        _webView.PostWebMessageAsJson(new JsonObject { ["type"] = "bridge.disconnect" }.ToJsonString());

    public static string LoadShimScript() => LoadEmbeddedScript(ShimResourceName);

    public static string LoadEmbeddedScript(string resourceName)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Ressource introuvable : {resourceName}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>À appeler avant la première navigation.</summary>
    public async Task AttachAsync()
    {
        // tosu ajoute window.COUNTER_PATH à la fin de chaque index.html servi ;
        // ici il est défini avant tout script de l'overlay.
        await _webView.AddScriptToExecuteOnDocumentCreatedAsync(
            $"window.COUNTER_PATH = {JsonSerializer.Serialize(CounterPath)};");
        await _webView.AddScriptToExecuteOnDocumentCreatedAsync(LoadShimScript());

        // Toutes les requêtes passent par le bridge : origine servie, le reste bloqué.
        _webView.AddWebResourceRequestedFilter(
            "*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        _webView.WebResourceRequested += OnWebResourceRequested;
        _webView.WebMessageReceived += OnWebMessageReceived;

        _source.SnapshotChanged += OnSnapshotChanged;
        _webView.NavigationCompleted += OnNavigationCompleted;
        Serialize(_source.Current);
    }

    /// <summary>V3.14-COM-KEY-07 : page de l'overlay (re)chargée avec succès (thread UI).</summary>
    public event Action? OverlayNavigationCompleted;

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess) OverlayNavigationCompleted?.Invoke();
    }

    public void Dispose()
    {
        _source.SnapshotChanged -= OnSnapshotChanged;
        _webView.WebResourceRequested -= OnWebResourceRequested;
        _webView.WebMessageReceived -= OnWebMessageReceived;
        _webView.NavigationCompleted -= OnNavigationCompleted;
    }

    /// <summary>Vrai pour http:// ou https:// sur l'hôte virtuel de l'overlay (port par défaut).</summary>
    public bool IsOverlayOrigin(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
        && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
        && string.Equals(parsed.Host, _virtualHost, StringComparison.OrdinalIgnoreCase)
        && parsed.IsDefaultPort;

    // Profilage (V3.14-COM-04) : coût par tick, mesuré sans changer le comportement.
    private readonly System.Diagnostics.Stopwatch _tickClock = System.Diagnostics.Stopwatch.StartNew();
    private double _lastTickMs = -1;

    public Diagnostics.RunningStats TickIntervalStats { get; } = new();
    public Diagnostics.RunningStats SerializeStats { get; } = new();
    public Diagnostics.RunningStats PostStats { get; } = new();
    public long PayloadBytesPosted { get; private set; }

    // COM-07 : dérive cumulée = écart entre la durée réelle et (ticks - 1) × période.
    public long TickCount { get; private set; }
    public double FirstTickMs { get; private set; }
    public double LastTickMs => _lastTickMs;
    public long TickBursts { get; private set; }
    public long TickLongGaps { get; private set; }
    public double TickMinIntervalMs { get; private set; } = double.MaxValue;

    private void OnSnapshotChanged(object? sender, CommunityOverlaySnapshot snapshot)
    {
        double start = _tickClock.Elapsed.TotalMilliseconds;
        if (_lastTickMs >= 0)
        {
            double interval = start - _lastTickMs;
            TickIntervalStats.Add(interval);
            if (interval < 50) TickBursts++;           // rafale : deux ticks rapprochés
            if (interval > 150) TickLongGaps++;
            if (interval < TickMinIntervalMs) TickMinIntervalMs = interval;
        }
        else
        {
            FirstTickMs = start;
        }
        _lastTickMs = start;
        TickCount++;

        Serialize(snapshot);
        double serialized = _tickClock.Elapsed.TotalMilliseconds;
        SerializeStats.Add(serialized - start);

        if (IsPaused)
        {
            // Overlay suspendu (O1) : l'état reste à jour côté hôte, rien n'est envoyé.
            TicksSkippedWhilePaused++;
            return;
        }

        PostLatest(includePrecise: PreciseFromSnapshot);
        PostStats.Add(_tickClock.Elapsed.TotalMilliseconds - serialized);
    }

    private void PostLatest(bool includePrecise = true)
    {
        Post("v2", _lastV2);
        V2Posted?.Invoke(_lastV2TimeLive);
        Post("v1", _lastV1);
        if (includePrecise)
            Post("precise", _lastPrecise);
    }

    // ---- V3.14-COM-KEY-04 : canal precise alimenté par les touches réelles (opt-in) ----

    /// <summary>
    /// Vrai (défaut, comportement COM-01 à COM-12) : le canal precise est construit à partir du snapshot
    /// à chaque tick. Faux : il n'est alimenté que par <see cref="PublishPrecise"/> (pipe precise),
    /// indépendamment du tick gameplay ; le tick ne republie plus de touches au repos par-dessus.
    /// </summary>
    public bool PreciseFromSnapshot { get; set; } = true;

    /// <summary>Levé (thread UI) après chaque post v2, avec la valeur <c>beatmap.time.live</c> postée.</summary>
    public event Action<double?>? V2Posted;

    private double? _lastV2TimeLive;

    public long PrecisePublished { get; private set; }

    /// <summary>Dernier payload precise publié (republié après un rechargement de page).</summary>
    public string LastPrecise => _lastPrecise;

    /// <summary>
    /// Réconcilie l'état v2 avec la preuve de gameplay du canal precise v2 Lazer.
    /// Le v1 reste inchangé et la preuve est retirée dès le premier message non valide.
    /// Thread UI uniquement.
    /// </summary>
    public void SetPreciseLazerGameplay(bool active)
    {
        if (_preciseLazerGameplay == active) return;
        _preciseLazerGameplay = active;
        Serialize(_source.Current);
        if (IsPaused) return;
        Post("v2", _lastV2);
        V2Posted?.Invoke(_lastV2TimeLive);
    }

    /// <summary>
    /// V3.14-COM-KEY-07 : recharge la page de l'overlay (thread UI). Les fichiers de l'overlay ne sont pas
    /// modifiés : seul son état en mémoire (cache, DOM) repart de zéro, comme à l'ouverture.
    /// </summary>
    public void ReloadOverlayPage()
    {
        _webView.Reload();
        OverlayReloads++;
    }

    public long OverlayReloads { get; private set; }

    /// <summary>Thread UI. Poste immédiatement un payload precise (gardé pour la reprise après suspension O1).</summary>
    public void PublishPrecise(string payload)
    {
        _lastPrecise = payload;
        if (IsPaused) return;
        Post("precise", payload);
        PrecisePublished++;
    }

    /// <summary>Vrai pendant une suspension O1 : aucune publication vers la page.</summary>
    public bool IsPaused { get; private set; }

    public long TicksSkippedWhilePaused { get; private set; }

    /// <summary>
    /// O1 : suspend ou reprend la publication. À la reprise, le dernier état est
    /// envoyé immédiatement sur les trois canaux, sans attendre le prochain tick.
    /// </summary>
    public void SetPaused(bool paused)
    {
        if (IsPaused == paused) return;
        IsPaused = paused;
        if (!paused)
            PostLatest();
    }

    /// <summary>Horloge murale en ms (sous-milliseconde) partagée avec le JS pour mesurer la latence.</summary>
    private static double UnixMsNow() => (DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;

    private void Serialize(CommunityOverlaySnapshot snapshot)
    {
        JsonObject v2 = TosuPayloadBuilder.BuildV2(
            snapshot,
            _preciseLazerGameplay ? CommunityOverlayRoute.Gameplay : null);
        _lastV2 = v2.ToJsonString();
        _lastV2TimeLive = (v2["beatmap"]?["time"]?["live"] as JsonValue)?.TryGetValue(out double live) == true ? live : null;
        _lastV1 = TosuPayloadBuilder.BuildV1(snapshot).ToJsonString();
        if (PreciseFromSnapshot)
            _lastPrecise = TosuPayloadBuilder.BuildPrecise(snapshot).ToJsonString();
    }

    private void Post(string channel, string payload)
    {
        // payload reste une chaîne JSON : les overlays tosu font JSON.parse(event.data).
        var message = new JsonObject
        {
            ["type"] = "bridge.data",
            ["channel"] = channel,
            ["payload"] = payload,
            ["ts"] = UnixMsNow(),
        };
        _webView.PostWebMessageAsJson(message.ToJsonString());
        MessagesPosted++;
        PayloadBytesPosted += payload.Length;
    }

    /// <summary>Réponse de commande : uniquement à la socket émettrice, comme tosu.</summary>
    private void Reply(int socketId, string payload)
    {
        var message = new JsonObject
        {
            ["type"] = "bridge.reply",
            ["socketId"] = socketId,
            ["payload"] = payload,
        };
        _webView.PostWebMessageAsJson(message.ToJsonString());
        MessagesPosted++;
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        string uri = e.Request.Uri;
        try
        {
            HandleResourceRequest(uri, e);
        }
        catch (Exception ex)
        {
            // Sans réponse, WebView2 enverrait la requête sur le réseau : toujours répondre.
            e.Response = _webView.Environment.CreateWebResourceResponse(null, 500, "Internal Error", "");
            RequestObserved?.Invoke($"{uri} [{ex.GetType().Name}: {ex.Message}]", 500);
        }
    }

    private void HandleResourceRequest(string uri, CoreWebView2WebResourceRequestedEventArgs e)
    {

        if (!IsOverlayOrigin(uri))
        {
            // Code tiers non fiable : aucune requête sortante (CDN, API, autre port local).
            e.Response = _webView.Environment.CreateWebResourceResponse(null, 403, "Blocked by BeatInsight", "");
            RequestObserved?.Invoke(uri, 403);
            return;
        }

        string path = new Uri(uri).AbsolutePath;
        if (BeatmapAssetProvider.IsBeatmapRoute(path))
        {
            e.Response = BeatmapAsset(path, e.Request);
            RequestObserved?.Invoke(uri, e.Response.StatusCode);
            return;
        }

        e.Response = path.TrimEnd('/') switch
        {
            "/json/v2" => Json(_lastV2),
            "/json/v2/precise" => Json(_lastPrecise),
            "/json" => Json(_lastV1),
            _ => StaticFile(path),
        };
        RequestObserved?.Invoke(uri, e.Response.StatusCode);
    }

    private CoreWebView2WebResourceResponse Json(string body) =>
        _webView.Environment.CreateWebResourceResponse(
            new MemoryStream(Encoding.UTF8.GetBytes(body)),
            200,
            "OK",
            "Content-Type: application/json; charset=utf-8\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff");

    /// <summary>
    /// <c>/files/beatmap/*</c> : fichier autorisé de la beatmap courante, lu en
    /// lecture seule et copié en mémoire (aucun handle laissé ouvert), avec
    /// prise en charge d'un intervalle <c>Range</c> comme tosu.
    /// </summary>
    private CoreWebView2WebResourceResponse BeatmapAsset(string urlPath, CoreWebView2WebResourceRequest request)
    {
        var asset = _beatmapAssets.Resolve(urlPath, _source.Current.Beatmap?.Files);
        switch (asset.Status)
        {
            case BeatmapAssetStatus.Rejected:
                return _webView.Environment.CreateWebResourceResponse(null, 403, "Forbidden", "");
            case BeatmapAssetStatus.NotFound:
                return _webView.Environment.CreateWebResourceResponse(null, 404, "Not Found", "");
        }

        string[] common = ["Cache-Control: no-store", "X-Content-Type-Options: nosniff", "Accept-Ranges: bytes"];
        try
        {
            using var file = new FileStream(asset.FullPath!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long size = file.Length;
            string? rangeHeader = request.Headers.Contains("Range") ? request.Headers.GetHeader("Range") : null;
            var range = ByteRange.Parse(rangeHeader, size, out bool satisfiable);

            if (!satisfiable)
                return _webView.Environment.CreateWebResourceResponse(
                    null, 416, "Range Not Satisfiable", Headers([$"Content-Range: bytes */{size}", .. common]));

            if (range is null)
            {
                if (size > MaxAssetResponseBytes)
                    return _webView.Environment.CreateWebResourceResponse(null, 403, "Too Large", "");

                byte[] content = ReadExactly(file, 0, size);
                return _webView.Environment.CreateWebResourceResponse(
                    new MemoryStream(content), 200, "OK",
                    Headers([$"Content-Type: {asset.ContentType}", $"Content-Length: {size}", .. common]));
            }

            var served = range.Value.Length > MaxAssetResponseBytes
                ? new ByteRange(range.Value.Start, range.Value.Start + MaxAssetResponseBytes - 1)
                : range.Value;
            byte[] partial = ReadExactly(file, served.Start, served.Length);
            return _webView.Environment.CreateWebResourceResponse(
                new MemoryStream(partial), 206, "Partial Content",
                Headers([
                    $"Content-Type: {asset.ContentType}",
                    $"Content-Range: bytes {served.Start}-{served.End}/{size}",
                    $"Content-Length: {served.Length}",
                    .. common]));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fichier disparu ou verrouillé entre la résolution et la lecture.
            return _webView.Environment.CreateWebResourceResponse(null, 404, "Not Found", "");
        }
    }

    private static string Headers(string[] lines) => string.Join("\r\n", lines);

    private static byte[] ReadExactly(FileStream file, long offset, long count)
    {
        var buffer = new byte[count];
        file.Seek(offset, SeekOrigin.Begin);
        file.ReadExactly(buffer);
        return buffer;
    }

    private CoreWebView2WebResourceResponse StaticFile(string urlPath)
    {
        var resolution = OverlayFileResolver.Resolve(_overlayRoot, urlPath);
        switch (resolution.Status)
        {
            case OverlayFileStatus.Rejected:
                return _webView.Environment.CreateWebResourceResponse(null, 403, "Forbidden", "");
            case OverlayFileStatus.NotFound:
                return _webView.Environment.CreateWebResourceResponse(null, 404, "Not Found", "");
        }

        string fullPath = resolution.FullPath!;
        string contentType = ContentTypeFor(fullPath);
        string headers = $"Content-Type: {contentType}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff";
        if (contentType.StartsWith("text/html", StringComparison.Ordinal))
            headers += $"\r\nContent-Security-Policy: {ContentSecurityPolicy}";

        return _webView.Environment.CreateWebResourceResponse(
            new MemoryStream(File.ReadAllBytes(fullPath)), 200, "OK", headers);
    }

    private static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" or ".apng" => "image/png",
        ".jpg" or ".jpeg" or ".jfif" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".ico" => "image/x-icon",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".ttf" => "font/ttf",
        ".otf" => "font/otf",
        ".mp3" => "audio/mpeg",
        ".ogg" => "audio/ogg",
        ".wav" => "audio/wav",
        _ => "application/octet-stream",
    };

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            string type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "";

            if (type == "bridge.send"
                && root.TryGetProperty("socketId", out var idElement) && idElement.TryGetInt32(out int socketId)
                && root.TryGetProperty("channel", out var c) && c.ValueKind == JsonValueKind.String
                && root.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String
                && root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.String)
            {
                string? reply = _commands.Handle(c.GetString()!, u.GetString()!, d.GetString()!);
                if (reply is not null)
                {
                    LastCommandReply = reply;
                    Reply(socketId, reply);
                }
            }

            if (ReceivedFromOverlay.Count < 100)
                ReceivedFromOverlay.Add(e.WebMessageAsJson);
        }
        catch (JsonException)
        {
            // Message non JSON d'un overlay tiers : ignoré.
        }
    }
}
