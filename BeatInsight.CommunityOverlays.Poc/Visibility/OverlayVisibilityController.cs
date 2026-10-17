using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using BeatInsight.CommunityOverlays.Poc.Bridge;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace BeatInsight.CommunityOverlays.Poc.OverlayVisibility;

/// <summary>
/// O1 (V3.14-COM-06) : suspend le rendu WebView2 d'un overlay réellement non visible
/// (fenêtre réduite, overlay désactivé, plein écran exclusif) et le reprend dès qu'il
/// redevient visible. Séquence de suspension : arrêt des publications du bridge,
/// contrôle masqué (<c>IsVisible = false</c> côté contrôleur), puis
/// <see cref="CoreWebView2.TrySuspendAsync"/>. Reprise : <see cref="CoreWebView2.Resume"/>,
/// contrôle visible, envoi immédiat du dernier état.
/// </summary>
public sealed class OverlayVisibilityController : IDisposable
{
    private const int QunsRunningD3DFullScreen = 3;

    private readonly Window _window;
    private readonly WebView2CompositionControl _view;
    private readonly CoreWebView2 _core;
    private readonly TosuCompatibleBridge _bridge;
    private readonly OverlayVisibilityPolicy _policy = new();
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock;
    private readonly List<JsonObject> _events = [];
    private bool _transitionInProgress;

    public OverlayVisibilityController(Window window, WebView2CompositionControl view, TosuCompatibleBridge bridge, Stopwatch clock)
    {
        _window = window;
        _view = view;
        _core = view.CoreWebView2;
        _bridge = bridge;
        _clock = clock;
        _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += async (_, _) => await EvaluateAsync();
    }

    /// <summary>Désactivation explicite de l'overlay (réglage utilisateur).</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Diagnostic : plein écran exclusif simulé (mesures sans osu!).</summary>
    public bool SimulateExclusiveFullscreen { get; set; }

    public bool IsSuspended => _policy.IsSuspended;

    public long Suspensions { get; private set; }

    public long Resumes { get; private set; }

    public JsonArray EventsJson() => new(_events.Select(e => (JsonNode)e.DeepClone()).ToArray());

    public void Start()
    {
        Log("start", new JsonObject { ["suspendDelayMs"] = _policy.SuspendDelay.TotalMilliseconds });
        _timer.Start();
    }

    public void Dispose() => _timer.Stop();

    public OverlayHiddenReason CurrentReason()
    {
        var reason = OverlayHiddenReason.None;
        if (_window.WindowState == WindowState.Minimized) reason |= OverlayHiddenReason.Minimized;
        if (!IsEnabled) reason |= OverlayHiddenReason.Disabled;
        if (SimulateExclusiveFullscreen || IsExclusiveFullscreenActive()) reason |= OverlayHiddenReason.ExclusiveFullscreen;
        return reason;
    }

    /// <summary>Applique immédiatement une évaluation (sans attendre le prochain tick).</summary>
    public async Task EvaluateAsync()
    {
        if (_transitionInProgress) return;
        var reason = CurrentReason();
        if (!_policy.Update(_clock.Elapsed, reason)) return;

        _transitionInProgress = true;
        try
        {
            if (_policy.IsSuspended) await SuspendAsync(reason);
            else Resume();
        }
        finally
        {
            _transitionInProgress = false;
        }
    }

    private async Task SuspendAsync(OverlayHiddenReason reason)
    {
        var sw = Stopwatch.StartNew();
        _bridge.SetPaused(true);
        _view.Visibility = Visibility.Hidden;
        await _window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background); // laisse appliquer IsVisible
        bool suspended = false;
        string? error = null;
        try
        {
            suspended = await _core.TrySuspendAsync();
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
        }
        Suspensions++;
        Log("suspend", new JsonObject
        {
            ["reason"] = reason.ToString(),
            ["trySuspendResult"] = suspended,
            ["error"] = error,
            ["durationMs"] = Math.Round(sw.Elapsed.TotalMilliseconds, 1),
        });
    }

    private void Resume()
    {
        var sw = Stopwatch.StartNew();
        string? error = null;
        try
        {
            _core.Resume();
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
        }
        _view.Visibility = Visibility.Visible;
        _bridge.SetPaused(false); // état récent envoyé immédiatement
        Resumes++;
        Log("resume", new JsonObject
        {
            ["error"] = error,
            ["durationMs"] = Math.Round(sw.Elapsed.TotalMilliseconds, 1),
            ["isSuspendedAfter"] = _core.IsSuspended,
        });
    }

    private void Log(string kind, JsonObject details)
    {
        details["atMs"] = _clock.ElapsedMilliseconds;
        details["kind"] = kind;
        if (_events.Count < 500) _events.Add(details);
    }

    private static bool IsExclusiveFullscreenActive() =>
        SHQueryUserNotificationState(out int state) == 0 && state == QunsRunningD3DFullScreen;

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    // Utilitaires pour les événements scriptés : réduire/restaurer sans prendre le focus.
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public void MinimizeWithoutActivation() => ShowWindow(new WindowInteropHelper(_window).Handle, 7);  // SW_SHOWMINNOACTIVE

    public void RestoreWithoutActivation() => ShowWindow(new WindowInteropHelper(_window).Handle, 4);   // SW_SHOWNOACTIVATE
}
