using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using BeatInsight.CommunityOverlays.Poc.Adapter;
using BeatInsight.CommunityOverlays.Poc.Bridge;
using Microsoft.Web.WebView2.Core;

namespace BeatInsight.CommunityOverlays.Poc.Diagnostics;

/// <summary>
/// Vérification automatique d'un overlay (option <c>--smoke</c>) : journalise la
/// console, les exceptions JS et les requêtes, échantillonne le DOM sur un cycle
/// mock complet, simule une coupure des sockets, capture des PNG et écrit
/// <c>smoke.json</c>. Ne modifie jamais les fichiers de l'overlay.
/// </summary>
public sealed class SmokeHarness
{
    private readonly CoreWebView2 _core;
    private readonly TosuCompatibleBridge _bridge;
    private readonly ICommunityOverlayDataSource _source;
    private readonly PocOptions _options;
    private readonly string _outputFolder;
    private readonly string _origin;
    private readonly Stopwatch _clock = new();

    private readonly List<object> _console = [];
    private readonly List<object> _requests = [];
    private readonly List<object> _navigations = [];

    public SmokeHarness(
        CoreWebView2 core, TosuCompatibleBridge bridge, ICommunityOverlayDataSource source,
        PocOptions options, string origin)
    {
        _core = core;
        _bridge = bridge;
        _source = source;
        _options = options;
        _outputFolder = options.SmokeOutputFolder ?? throw new ArgumentException("Smoke désactivé.");
        _origin = origin;
    }

    /// <summary>À appeler avant la navigation pour ne rien manquer du chargement initial.</summary>
    public async Task AttachAsync()
    {
        Directory.CreateDirectory(_outputFolder);

        await _core.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");
        await _core.CallDevToolsProtocolMethodAsync("Performance.enable", "{}");
        await _core.CallDevToolsProtocolMethodAsync("Log.enable", "{}");
        _core.GetDevToolsProtocolEventReceiver("Runtime.consoleAPICalled").DevToolsProtocolEventReceived +=
            (_, e) => Record("console", e.ParameterObjectAsJson);
        _core.GetDevToolsProtocolEventReceiver("Runtime.exceptionThrown").DevToolsProtocolEventReceived +=
            (_, e) => Record("exception", e.ParameterObjectAsJson);
        _core.GetDevToolsProtocolEventReceiver("Log.entryAdded").DevToolsProtocolEventReceived +=
            (_, e) => Record("log", e.ParameterObjectAsJson);

        _bridge.RequestObserved += (uri, status) => _requests.Add(new { atMs = _clock.ElapsedMilliseconds, uri, status });

        _core.NavigationStarting += (_, e) => _navigations.Add(new { atMs = _clock.ElapsedMilliseconds, uri = e.Uri, cancelled = e.Cancel });
        _core.FrameNavigationStarting += (_, e) => _navigations.Add(new { atMs = _clock.ElapsedMilliseconds, frame = true, uri = e.Uri, cancelled = e.Cancel });
        _clock.Start();
    }

    /// <summary>Le mock et le chronomètre démarrent ensemble, juste avant la navigation.</summary>
    /// <summary>Données supplémentaires ajoutées au rapport (ex. journal de visibilité O1).</summary>
    public Func<JsonNode?>? Extra { get; set; }

    public async Task<int> RunAsync()
    {
        var samples = new List<object>();
        int disconnectAt = _options.DisconnectAtMs;
        bool disconnected = disconnectAt <= 0;

        foreach (int atMs in _options.SampleTimesMs)
        {
            if (!disconnected && atMs > disconnectAt)
            {
                await DelayUntil(disconnectAt);
                _bridge.SimulateDisconnect();
                _console.Add(new { atMs = _clock.ElapsedMilliseconds, kind = "harness", text = "SimulateDisconnect()" });
                disconnected = true;
            }

            await DelayUntil(atMs);
            samples.Add(await SampleAsync(atMs));
            // Écrit après chaque échantillon : le rapport survit à une fermeture externe de la fenêtre.
            await WriteReportAsync(samples);
        }

        return 0;
    }

    private async Task WriteReportAsync(List<object> samples)
    {
        var report = new JsonObject
        {
            ["overlayFolder"] = _options.OverlayFolder,
            ["runtimeVersion"] = _core.Environment.BrowserVersionString,
            ["profileName"] = _core.Profile.ProfileName,
            ["durationMs"] = _clock.ElapsedMilliseconds,
            ["messagesPosted"] = _bridge.MessagesPosted,
            ["payloadBytesPosted"] = _bridge.PayloadBytesPosted,
            ["bridgeTiming"] = new JsonObject
            {
                ["tickInterval"] = _bridge.TickIntervalStats.ToJson(),
                ["serialize"] = _bridge.SerializeStats.ToJson(),
                ["post"] = _bridge.PostStats.ToJson(),
                ["tickCount"] = _bridge.TickCount,
                ["spanMs"] = Math.Round(_bridge.LastTickMs - _bridge.FirstTickMs, 1),
                ["averagePeriodMs"] = _bridge.TickCount > 1 ? Math.Round((_bridge.LastTickMs - _bridge.FirstTickMs) / (_bridge.TickCount - 1), 3) : 0,
                ["cumulativeDriftMs"] = _bridge.TickCount > 1 ? Math.Round((_bridge.LastTickMs - _bridge.FirstTickMs) - (_bridge.TickCount - 1) * 100.0, 1) : 0,
                ["bursts"] = _bridge.TickBursts,
                ["longGaps"] = _bridge.TickLongGaps,
                ["minIntervalMs"] = _bridge.TickCount > 1 ? Math.Round(_bridge.TickMinIntervalMs, 2) : 0,
            },
            ["counterPath"] = _bridge.CounterPath,
            ["visibility"] = Extra?.Invoke(),
            ["receivedFromOverlay"] = JsonSerializer.SerializeToNode(_bridge.ReceivedFromOverlay),
            ["receivedFilters"] = JsonSerializer.SerializeToNode(_bridge.ReceivedFilters),
            ["samples"] = JsonSerializer.SerializeToNode(samples),
            ["console"] = JsonSerializer.SerializeToNode(_console),
            ["requests"] = JsonSerializer.SerializeToNode(_requests),
            ["navigations"] = JsonSerializer.SerializeToNode(_navigations),
        };

        await File.WriteAllTextAsync(Path.Combine(_outputFolder, "smoke.json"),
            report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task DelayUntil(int atMs)
    {
        long wait = atMs - _clock.ElapsedMilliseconds;
        if (wait > 0) await Task.Delay(TimeSpan.FromMilliseconds(wait));
    }

    private async Task<object> SampleAsync(int atMs)
    {
        string probe = JsonSerializer.Serialize(_options.Probe ?? "null");
        string script =
            "JSON.stringify({" +
            " text: document.body ? document.body.innerText : null," +
            " htmlLength: document.documentElement ? document.documentElement.outerHTML.length : 0," +
            " bridge: window.__beatInsightTosuBridge && window.__beatInsightTosuBridge.stats ? window.__beatInsightTosuBridge.stats() : null," +
            $" probe: (() => {{ try {{ return (0, eval)({probe}); }} catch (e) {{ return 'probe error: ' + e; }} }})()" +
            "})";

        string raw = await _core.ExecuteScriptAsync(script);

        // Cadence requestAnimationFrame sur 1 s (mesure courte : un compteur rAF permanent
        // forcerait le rendu continu et fausserait le profilage).
        // rafPerSec : cadence vue par l'overlay (après limiteur éventuel) ;
        // nativeRafPerSec : cadence native du navigateur (rAF non limité).
        await _core.ExecuteScriptAsync(
            "window.__biRaf = null; window.__biNativeRaf = null; (() => {" +
            " const measure = (raf, key) => { let n = 0; const t0 = performance.now();" +
            "   const f = (t) => { n++; if (t - t0 < 1000) raf(f); else window[key] = +(n * 1000 / (t - t0)).toFixed(1); }; raf(f); };" +
            " measure(requestAnimationFrame, '__biRaf');" +
            " const lim = window.__beatInsightFrameLimiter; measure(lim ? lim.nativeRequestAnimationFrame : requestAnimationFrame, '__biNativeRaf'); })();");
        await Task.Delay(1300);
        string raf = await _core.ExecuteScriptAsync("window.__biRaf");
        string nativeRaf = await _core.ExecuteScriptAsync("window.__biNativeRaf");
        JsonObject? frames = _options.TraceFrames ? await TraceFramesAsync(2000) : null;

        // Métriques Chromium cumulées (durées de tâches, script, layout, style).
        string metrics = await _core.CallDevToolsProtocolMethodAsync("Performance.getMetrics", "{}");

        string png = Path.Combine(_outputFolder, $"capture-{atMs:D5}ms.png");
        await using (var file = File.Create(png))
            await _core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);

        return new
        {
            atMs,
            measuredAtMs = _clock.ElapsedMilliseconds,
            route = _source.Current.Route.ToString(),
            currentTimeMs = Math.Round(_source.Current.CurrentTimeMs.Or(0)),
            source = SourceDiagnostics.Describe(_source),
            page = JsonNode.Parse(JsonSerializer.Deserialize<string>(raw) ?? "null"),
            rafPerSec = JsonNode.Parse(raf),
            nativeRafPerSec = JsonNode.Parse(nativeRaf),
            tracedFramesPerSec = frames,
            chromiumMetrics = SelectMetrics(metrics),
            capture = Path.GetFileName(png),
        };
    }

    /// <summary>
    /// Compte, par processus, les événements de production d'images d'une trace CDP courte :
    /// images composées (DrawFrame), images principales (BeginMainThreadFrame), BeginFrame.
    /// Mesure la cadence réelle de rendu, indépendamment de requestAnimationFrame.
    /// </summary>
    private async Task<JsonObject> TraceFramesAsync(int durationMs)
    {
        string[] names = ["DrawFrame", "BeginMainThreadFrame", "BeginFrame", "Commit", "UpdateLayerTree", "Paint"];
        var counts = new Dictionary<string, int>();
        var complete = new TaskCompletionSource();
        var dataReceiver = _core.GetDevToolsProtocolEventReceiver("Tracing.dataCollected");
        var completeReceiver = _core.GetDevToolsProtocolEventReceiver("Tracing.tracingComplete");
        EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs> onData = (_, e) =>
        {
            foreach (var ev in JsonNode.Parse(e.ParameterObjectAsJson)?["value"]?.AsArray() ?? [])
            {
                string? name = ev?["name"]?.GetValue<string>();
                if (name is null || Array.IndexOf(names, name) < 0) continue;
                string key = $"{name}@{ev!["pid"]}";
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        };
        EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs> onComplete = (_, _) => complete.TrySetResult();
        dataReceiver.DevToolsProtocolEventReceived += onData;
        completeReceiver.DevToolsProtocolEventReceived += onComplete;
        try
        {
            await _core.CallDevToolsProtocolMethodAsync("Tracing.start",
                "{\"categories\":\"devtools.timeline,disabled-by-default-devtools.timeline.frame,cc,viz\",\"transferMode\":\"ReportEvents\"}");
            await Task.Delay(durationMs);
            await _core.CallDevToolsProtocolMethodAsync("Tracing.end", "{}");
            await Task.WhenAny(complete.Task, Task.Delay(5000));
        }
        catch (Exception ex)
        {
            return new JsonObject { ["error"] = ex.Message };
        }
        finally
        {
            dataReceiver.DevToolsProtocolEventReceived -= onData;
            completeReceiver.DevToolsProtocolEventReceived -= onComplete;
        }

        var result = new JsonObject();
        foreach (var (key, count) in counts.OrderBy(kv => kv.Key))
            result[key] = Math.Round(count * 1000.0 / durationMs, 1);
        return result;
    }

    private static JsonObject SelectMetrics(string json)
    {
        var wanted = new HashSet<string> { "TaskDuration", "ScriptDuration", "LayoutDuration", "RecalcStyleDuration", "LayoutCount", "RecalcStyleCount", "JSHeapUsedSize" };
        var result = new JsonObject();
        foreach (var metric in JsonNode.Parse(json)?["metrics"]?.AsArray() ?? [])
        {
            string? name = metric?["name"]?.GetValue<string>();
            if (name is not null && wanted.Contains(name))
                result[name] = metric!["value"]!.GetValue<double>();
        }
        return result;
    }

    private void Record(string kind, string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            string text = kind switch
            {
                "console" => $"[{node?["type"]}] " + string.Join(" ",
                    (node?["args"]?.AsArray() ?? []).Select(a => a?["value"]?.ToJsonString() ?? a?["description"]?.GetValue<string>() ?? a?["type"]?.GetValue<string>())),
                "exception" => $"{node?["exceptionDetails"]?["text"]} {node?["exceptionDetails"]?["exception"]?["description"]} @ {node?["exceptionDetails"]?["url"]}:{node?["exceptionDetails"]?["lineNumber"]}",
                "log" => $"[{node?["entry"]?["level"]}/{node?["entry"]?["source"]}] {node?["entry"]?["text"]} {node?["entry"]?["url"]}",
                _ => json,
            };
            if (_console.Count < 2_000)
                _console.Add(new { atMs = _clock.ElapsedMilliseconds, kind, text });
        }
        catch (JsonException)
        {
            _console.Add(new { atMs = _clock.ElapsedMilliseconds, kind, text = json });
        }
    }
}
