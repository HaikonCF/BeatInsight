using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using BeatInsight.CommunityOverlays.Poc.Adapter;
using BeatInsight.CommunityOverlays.Poc.Bridge;
using BeatInsight.CommunityOverlays.Poc.Diagnostics;
using BeatInsight.CommunityOverlays.Poc.OverlayVisibility;
using Microsoft.Web.WebView2.Core;

namespace BeatInsight.CommunityOverlays.Poc;

public partial class CommunityOverlayWindow : Window
{
    /// <summary>
    /// Origine de l'overlay. Toutes ses requêtes sont servies par le bridge
    /// (WebResourceRequested) ; le TLD .invalid garantit qu'aucune résolution
    /// réseau réelle n'est possible.
    /// </summary>
    public const string VirtualHost = "overlay.beatinsight.invalid";

    private readonly PocOptions _options;
    private readonly ICommunityOverlayDataSource _source;
    private TosuCompatibleBridge? _bridge;
    private OverlayVisibilityController? _visibility;
    private readonly Managed.ManagedCompanionHost? _managed;
    private int _scalePercent;

    private static PublishTicker? PublishTickerOf(ICommunityOverlayDataSource source) => source switch
    {
        MockCommunityOverlayDataSource mock => mock.Ticker,
        Adapter.Live.SyntheticLiveCommunityOverlayDataSource synthetic => synthetic.Ticker,
        Adapter.Live.PipeLiveCommunityOverlayDataSource pipe => pipe.Ticker,
        _ => null,
    };

    public CommunityOverlayWindow(PocOptions options, ICommunityOverlayDataSource source, Managed.ManagedCompanionHost? managed = null)
    {
        _options = options;
        _source = source;
        _managed = managed;
        _scalePercent = options.ScalePercent;
        InitializeComponent();
        if (managed is not null)
        {
            // FEAT-003 : poignée et redimensionnement seulement pendant l'édition demandée par BeatInsight.
            ShowInTaskbar = false;
            // FEAT-003.2 : click-through hors édition (curseur Windows et clics laissés à osu!).
            Managed.ManagedWindowInteraction.Attach(this, () => managed.IsEditMode);
            ApplyManagedEditMode(managed.IsEditMode);
            managed.EditModeChanged += ApplyManagedEditMode;
            managed.ScaleChanged += ApplyScale;
            // BUG-003 : transparent quand osu! n'est pas au premier plan (hors édition).
            Managed.ManagedForegroundVisibility.Attach(this, managed);
        }

        Title = $"BeatInsight overlay POC — {Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.OverlayFolder)))}";
        if (options.WindowBounds is { } bounds)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = bounds.X;
            Top = bounds.Y;
            Width = bounds.Width;
            Height = bounds.Height;
        }
        Loaded += async (_, _) => await InitializeOverlayAsync();
        Closed += (_, _) =>
        {
            StopPrecise();
            _visibility?.Dispose();
            _bridge?.Dispose();
            _source.Dispose();
        };
    }

    private async Task InitializeOverlayAsync()
    {
        try
        {
            string overlayFolder = Path.GetFullPath(_options.OverlayFolder);
            if (!File.Exists(Path.Combine(overlayFolder, "index.html")))
                throw new FileNotFoundException("index.html introuvable dans le dossier overlay.", overlayFolder);

            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BeatInsight", "CommunityOverlaysPoc", "WebView2");

            // Expérimental COM-05 : les arguments Chromium s'appliquent au processus navigateur
            // de ce dossier de données (overlays du POC uniquement, jamais au reste de la machine).
            // Tous les overlays partageant le dossier doivent utiliser les mêmes arguments.
            var environmentOptions = new CoreWebView2EnvironmentOptions();
            if (_options.ThrottleMainFrame)
                environmentOptions.AdditionalBrowserArguments = "--enable-features=ThrottleMainFrameTo60Hz";
            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, environmentOptions);
            if (_options.Profile == OverlayProfileMode.PerOverlay)
            {
                // Un profil WebView2 par overlay (même dossier, même processus navigateur) :
                // stockage, cookies, cache et permissions séparés entre overlays.
                var controllerOptions = environment.CreateCoreWebView2ControllerOptions();
                controllerOptions.ProfileName = ProfileNameFor(overlayFolder);
                await OverlayView.EnsureCoreWebView2Async(environment, controllerOptions);
            }
            else
            {
                await OverlayView.EnsureCoreWebView2Async(environment); // profil par défaut partagé (COM-01/02)
            }
            CoreWebView2 core = OverlayView.CoreWebView2;

            core.Settings.AreDevToolsEnabled = _options.DevTools;
            core.Settings.AreDefaultContextMenusEnabled = _options.DevTools;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsWebMessageEnabled = true;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            // FEAT-003.1 : en mode géré, l'échelle vient des Settings de BeatInsight (pas de Ctrl+molette).
            if (_managed is not null)
                core.Settings.IsZoomControlEnabled = false;
            ApplyScale(_scalePercent);

            _bridge = new TosuCompatibleBridge(
                core, _source, VirtualHost, overlayFolder,
                _source is Adapter.Live.PipeLiveCommunityOverlayDataSource live
                    ? new BeatmapAssetProvider(() => live.SongsRoot)
                    : new BeatmapAssetProvider(_options.BeatmapAssetsRoot));

            // Overlay = code tiers non fiable : confiné à son origine, sans popup,
            // téléchargement ni permission navigateur (caméra, micro, géolocalisation...).
            core.NavigationStarting += (_, e) =>
            {
                if (!_bridge.IsOverlayOrigin(e.Uri))
                    e.Cancel = true;
            };
            core.FrameNavigationStarting += (_, e) =>
            {
                if (!_bridge.IsOverlayOrigin(e.Uri) && e.Uri != "about:blank" && e.Uri != "about:srcdoc")
                    e.Cancel = true;
            };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;

            await _bridge.AttachAsync();
            StartPreciseIfRequested(core); // COM-KEY-04, opt-in
            if (_options.FpsCap > 0)
                await core.AddScriptToExecuteOnDocumentCreatedAsync(
                    TosuCompatibleBridge.LoadEmbeddedScript("BeatInsight.CommunityOverlays.Poc.frame-limiter.js")
                        .Replace("__BI_FPS_CAP__", _options.FpsCap.ToString(System.Globalization.CultureInfo.InvariantCulture)));

            bool devToolsOpened = false;
            core.NavigationCompleted += (_, e) =>
            {
                StatusText.Text = e.IsSuccess
                    ? $"BeatInsight · overlay POC · {Path.GetFileName(overlayFolder)}"
                    : $"BeatInsight · erreur navigation ({e.WebErrorStatus})";
                if (e.IsSuccess) _managed?.NotifyReady();
                else _managed?.NotifyError($"navigation : {e.WebErrorStatus}");
                if (_options.DevTools && e.IsSuccess && !devToolsOpened)
                {
                    devToolsOpened = true;
                    try { core.OpenDevToolsWindow(); } catch { }
                }
            };

            // http:// comme tosu (http://127.0.0.1:24050) : les overlays qui construisent
            // `http://${location.host}/...` restent sur la même origine.
            string origin = $"http://{VirtualHost}/";

            SmokeHarness? harness = null;
            if (_options.SmokeOutputFolder is not null)
            {
                harness = new SmokeHarness(core, _bridge, _source, _options, origin);
                await harness.AttachAsync();
            }

            // O1 : contrôleur de visibilité (actif sauf --visibility-manager off).
            var visibilityClock = System.Diagnostics.Stopwatch.StartNew();
            _visibility = new OverlayVisibilityController(this, OverlayView, _bridge, visibilityClock);
            if (_options.VisibilityManager)
                _visibility.Start();
            ScheduleScriptedVisibilityEvents(visibilityClock);
            if (_options.UiBusy is { } busy)
            {
                // Diagnostic COM-07 : bloque volontairement le thread UI (simule une surcharge).
                var busyTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Normal)
                {
                    Interval = TimeSpan.FromMilliseconds(busy.AtMs),
                };
                busyTimer.Tick += (_, _) => { busyTimer.Stop(); System.Threading.Thread.Sleep(busy.DurationMs); };
                busyTimer.Start();
            }
            if (harness is not null)
                harness.Extra = () => new System.Text.Json.Nodes.JsonObject
                {
                    ["visibilityManager"] = _options.VisibilityManager,
                    ["publishPriority"] = _options.PublishPriority.ToString(),
                    ["publishTimer"] = _options.PublishTimer.ToString(),
                    ["earlyWakeups"] = PublishTickerOf(_source)?.EarlyWakeups ?? 0,
                    ["skippedDeadlines"] = PublishTickerOf(_source)?.SkippedDeadlines ?? 0,
                    ["suspensions"] = _visibility.Suspensions,
                    ["resumes"] = _visibility.Resumes,
                    ["isSuspended"] = _visibility.IsSuspended,
                    ["ticksSkippedWhilePaused"] = _bridge.TicksSkippedWhilePaused,
                    ["events"] = _visibility.EventsJson(),
                };

            _source.Start();
            core.Navigate($"{origin}index.html");

            if (harness is not null)
            {
                await harness.RunAsync();
                Close(); // fermeture normale de la fenêtre : vérifie aussi le chemin Closed.
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"BeatInsight · échec : {ex.Message}";
            if (_managed is not null)
            {
                // Mode géré : l'échec est remonté à BeatInsight, qui l'affiche dans les Settings.
                _managed.NotifyError(ex.Message);
                Application.Current.Shutdown(BeatInsight.Integration.CommunityOverlays.Managed.CommunityOverlayCompanionProtocol.ExitInitializationFailed);
                return;
            }
            if (_options.SmokeOutputFolder is not null)
            {
                Directory.CreateDirectory(_options.SmokeOutputFolder);
                await File.WriteAllTextAsync(Path.Combine(_options.SmokeOutputFolder, "smoke.json"),
                    JsonSerializer.Serialize(new { success = false, error = ex.ToString() }));
                Application.Current.Shutdown(1);
            }
        }
    }

    /// <summary>Diagnostic : réduction/restauration/désactivation/plein écran exclusif simulé à heure fixe.</summary>
    private void ScheduleScriptedVisibilityEvents(System.Diagnostics.Stopwatch clock)
    {
        foreach (var (name, atMs) in _options.ScriptedVisibilityEvents)
        {
            var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMilliseconds(Math.Max(1, atMs - clock.ElapsedMilliseconds)),
            };
            timer.Tick += async (_, _) =>
            {
                timer.Stop();
                switch (name)
                {
                    case "minimize-at": _visibility!.MinimizeWithoutActivation(); break;
                    case "restore-at": _visibility!.RestoreWithoutActivation(); break;
                    case "disable-at": _visibility!.IsEnabled = false; break;
                    case "enable-at": _visibility!.IsEnabled = true; break;
                    case "simulate-exclusive-at": _visibility!.SimulateExclusiveFullscreen = true; break;
                    case "simulate-exclusive-until": _visibility!.SimulateExclusiveFullscreen = false; break;
                }
                if (_options.VisibilityManager)
                    await _visibility!.EvaluateAsync();
            };
            timer.Start();
        }
    }

    /// <summary>Nom de profil stable et sûr, dérivé du chemin complet du dossier de l'overlay.</summary>
    public static string ProfileNameFor(string overlayFolder)
    {
        string key = Path.TrimEndingDirectorySeparator(Path.GetFullPath(overlayFolder)).ToUpperInvariant();
        byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        return "overlay_" + Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    private void OnHandleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        // Mode géré : fermeture volontaire, BeatInsight ne relance pas l'overlay.
        _managed?.NotifyUserClosed();
        Close();
    }

    /// <summary>
    /// FEAT-003.1 : échelle du contenu par le zoom natif WebView2 (ZoomFactor du contrôleur), sans toucher aux fichiers
    /// de l'overlay ni à la taille de la fenêtre. Le zoom défini par l'hôte persiste à travers les navigations.
    /// À 100 % hors mode géré, le zoom n'est jamais modifié (comportement CLI d'origine).
    /// </summary>
    private void ApplyScale(int percent)
    {
        _scalePercent = percent;
        if (OverlayView.CoreWebView2 is null)
            return; // reçu avant l'initialisation : appliqué dès que WebView2 est prêt
        if (_managed is null && percent == BeatInsight.Integration.CommunityOverlays.Managed.CommunityOverlayCompanionProtocol.DefaultScalePercent)
            return;
        OverlayView.ZoomFactor = BeatInsight.Integration.CommunityOverlays.Managed.CommunityOverlayCompanionProtocol.ZoomFactorFor(percent);
    }

    private void ApplyManagedEditMode(bool editing)
    {
        HandleBar.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        HandleRow.Height = new GridLength(editing ? 18 : 0);
        ResizeMode = editing ? ResizeMode.CanResizeWithGrip : ResizeMode.NoResize;
        Managed.ManagedWindowInteraction.Apply(this, editing);
    }
}
