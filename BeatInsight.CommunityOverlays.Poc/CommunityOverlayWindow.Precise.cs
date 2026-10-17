using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using BeatInsight.CommunityOverlays.Poc.Adapter.Live;
using BeatInsight.CommunityOverlays.Poc.Bridge;
using BeatInsight.Integration.CommunityOverlays.Pipe;
using Microsoft.Web.WebView2.Core;

namespace BeatInsight.CommunityOverlays.Poc;

/// <summary>V3.14-COM-KEY-04 : raccordement opt-in du pipe precise (<c>--precise-pipe</c>).</summary>
public partial class CommunityOverlayWindow
{
    private PreciseBridgeConnector? _precise;
    private DispatcherTimer? _preciseDiagnosticsTimer;
    private StreamWriter? _preciseDiagnostics;

    private void StartPreciseIfRequested(CoreWebView2 core)
    {
        if (!_options.PrecisePipe || _bridge is null) return;
        _precise = new PreciseBridgeConnector(_bridge, new PrecisePipeClient(), new PreciseKeyChannel(resetPageOnNewPlay: true),
            _options.PreciseV2 ? new PreciseV2PipeClient() : null); // COM-LAZER-KEY-03, opt-in

        if (_options.PreciseDiagnosticsFile is not { } file) return;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        _preciseDiagnostics = new StreamWriter(file, append: true) { AutoFlush = true };
        _preciseDiagnosticsTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _preciseDiagnosticsTimer.Tick += async (_, _) => await WritePreciseDiagnosticsAsync(core);
        _preciseDiagnosticsTimer.Start();
    }

    /// <summary>Compteurs réellement affichés par la page (DOM de KeyOverlay) + statistiques precise et shim.</summary>
    private async Task WritePreciseDiagnosticsAsync(CoreWebView2 core)
    {
        if (_precise is null || _preciseDiagnostics is null) return;
        string page;
        try
        {
            page = await core.ExecuteScriptAsync(
                "(() => { const t = id => { const e = document.getElementById(id); return e ? e.textContent : null; };" +
                " const d = k => { const e = document.querySelector('.keys.' + k); const c = e ? getComputedStyle(e) : null;" +
                " return e ? { classes: e.className, hidden: e.classList.contains('hidden'), opacity: c.opacity, display: c.display } : null; };" +
                " const s = window.__beatInsightTosuBridge ? window.__beatInsightTosuBridge.stats() : null;" +
                " return { k1: t('k1Count'), k2: t('k2Count'), m1: t('m1Count'), m2: t('m2Count')," +
                " k1Bpm: t('k1Bpm'), k2Bpm: t('k2Bpm'), precise: s && s.reception ? s.reception.precise : null," +
                " v2: s && s.reception ? s.reception.v2 : null, open: s ? s.open : null, k1Dom: d('k1'), k2Dom: d('k2') }; })()");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            page = JsonSerializer.Serialize(new { error = ex.GetType().Name });
        }
        var line = new JsonObject
        {
            ["at"] = DateTimeOffset.Now.ToString("O"),
            ["sourceRoute"] = _source.Current.Route.ToString(),
            ["page"] = JsonNode.Parse(page),
            ["payloads"] = new JsonObject
            {
                ["getSettings"] = ParseDiagnosticJson(_bridge?.LastCommandReply),
                ["api_v2"] = ParseDiagnosticJson(_bridge?.LastV2),
                ["api_v2_precise"] = ParseDiagnosticJson(_bridge?.LastPrecise),
            },
            ["precise"] = _precise.Stats(),
            ["sliderBreaks"] = SliderBreakDiagnostics(),
            ["selection"] = SelectionDiagnostics(),
            ["process"] = new JsonObject
            {
                ["cpuMs"] = Math.Round(System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds),
                ["workingSetMb"] = Math.Round(System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1048576.0, 1),
            },
        };
        _preciseDiagnostics.WriteLine(line.ToJsonString());
    }

    private static JsonNode? ParseDiagnosticJson(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonNode.Parse(json); }
        catch (JsonException) { return JsonValue.Create(json); }
    }

    /// <summary>
    /// COM-PP-03 : PP de sélection reçu (v3), valeur réellement appliquée au snapshot, et SS gameplay du pipe v2
    /// (même beatmap et mêmes mods une fois en partie) pour comparer les deux calculs.
    /// </summary>
    private JsonObject? SelectionDiagnostics()
    {
        if (_source is not PipeLiveCommunityOverlayDataSource pipe || pipe.Selection is not { } client) return null;
        SelectionPipeEnvelope? sel = client.Latest?.Envelope;
        PipeSnapshot? raw = pipe.Client.Latest?.Envelope.Snapshot;
        var shown = _source.Current.Pp.ByAccuracy;
        return new JsonObject
        {
            ["route"] = _source.Current.Route.ToString(),
            ["applied"] = pipe.LastSelectionStatus,
            ["shownSs"] = shown.TryGetValue(100, out var ss) && ss.HasValue ? ss.Value : null,
            ["shown95"] = shown.TryGetValue(95, out var p95) && p95.HasValue ? p95.Value : null,
            ["validity"] = sel?.Validity.ToString(),
            ["reason"] = sel?.Reason,
            ["generation"] = sel?.SelectionGeneration,
            ["mods"] = sel?.ModsKey,
            ["ss"] = sel?.Performance?.SsPp,
            ["pp95"] = sel?.Performance?.Accuracy95Pp,
            ["beatmap"] = sel?.Beatmap is { } b ? Path.GetFileName(b.Path) : null,
            ["gameplaySs"] = raw?.Performance?.SsPp,
            ["gameplay95"] = raw?.Performance?.Accuracy95FcPp,
            ["gameplayMods"] = raw?.Stats is { } st ? string.Join("+", st.Mods) : null,
            ["gameplayBeatmap"] = raw?.Game?.CurrentBeatmapPath is { } g ? Path.GetFileName(g) : null,
            ["received"] = client.Received,
            ["rejected"] = client.Rejected,
            ["stale"] = client.Stale,
        };
    }

    /// <summary>COM-SB-01 : estimation courante (valeur, provenance, nombre d'observations natives traitées).</summary>
    private JsonObject? SliderBreakDiagnostics()
    {
        var field = _source.Current.Hits.SliderBreaks;
        var play = (_source as PipeLiveCommunityOverlayDataSource)?.State.Play;
        return new JsonObject
        {
            ["state"] = field.State.ToString(),
            ["provenance"] = field.Provenance.ToString(),
            ["value"] = field.HasValue ? field.Value : null,
            ["playId"] = play?.PlayId,
            ["observations"] = play?.SliderBreaks?.Observations,
            ["combo"] = play?.SliderBreaks?.PreviousCombo,
            ["misses"] = play?.SliderBreaks?.PreviousMisses,
        };
    }

    private void StopPrecise()
    {
        _preciseDiagnosticsTimer?.Stop();
        _precise?.Dispose();
        _precise = null;
        _preciseDiagnostics?.Dispose();
        _preciseDiagnostics = null;
    }
}
