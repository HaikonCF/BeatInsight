using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using BeatInsight.Integration.CommunityOverlays.Managed;

namespace BeatInsight.CommunityOverlays.Poc.Managed;

/// <summary>
/// FEAT-003 : mode géré par BeatInsight (<c>--managed-by &lt;pid&gt;</c>), sans effet en mode CLI autonome.
/// <list type="bullet">
/// <item>Instance unique par overlay (mutex local dérivé du profil WebView2).</item>
/// <item>Arrêt quand BeatInsight disparaît ou ferme l'entrée standard.</item>
/// <item>Commandes reçues sur stdin (édition, bornes, fermeture), événements écrits sur stdout.</item>
/// </list>
/// </summary>
public sealed class ManagedCompanionHost : IDisposable
{
    private static readonly TimeSpan BoundsDebounce = TimeSpan.FromMilliseconds(300);

    private readonly Mutex _mutex;
    private readonly TextWriter _output;
    private readonly object _outputGate = new();
    private Window? _window;
    private DispatcherTimer? _boundsTimer;
    private CompanionBounds? _lastReported;
    private bool _applyingBounds;
    private bool _disposed;

    private ManagedCompanionHost(Mutex mutex, TextWriter output)
    {
        _mutex = mutex;
        _output = output;
    }

    /// <summary>Vrai tant que BeatInsight a demandé le mode édition (poignée visible).</summary>
    public bool IsEditMode { get; private set; }

    /// <summary>Changement du mode édition (thread UI).</summary>
    public event Action<bool>? EditModeChanged;

    /// <summary>FEAT-003.1 : nouvelle échelle du contenu en pourcentage (thread UI).</summary>
    public event Action<int>? ScaleChanged;

    /// <summary>
    /// BUG-003 : osu! au premier plan selon BeatInsight. Vrai tant qu'aucun état n'a été reçu
    /// (BeatInsight n'en envoie qu'aux overlays Live : la prévisualisation reste affichée).
    /// </summary>
    public bool IsOsuForeground { get; private set; } = true;

    /// <summary>BUG-003 : contenu affiché si osu! est au premier plan, ou toujours pendant l'édition.</summary>
    public bool ShouldDisplay => IsEditMode || IsOsuForeground;

    /// <summary>BUG-003 : changement de <see cref="ShouldDisplay"/> (thread UI).</summary>
    public event Action<bool>? DisplayChanged;

    /// <summary>
    /// Prend le mutex de l'overlay. Null si un autre compagnon géré tient déjà cet overlay
    /// (l'événement <c>duplicate</c> est alors écrit).
    /// </summary>
    public static ManagedCompanionHost? TryAcquire(string profileName, TextWriter output)
    {
        var mutex = new Mutex(initiallyOwned: false, CommunityOverlayCompanionProtocol.MutexNameFor(profileName));
        bool owned;
        try
        {
            owned = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            owned = true; // ancien compagnon terminé brutalement : le mutex nous revient
        }

        if (!owned)
        {
            mutex.Dispose();
            WriteLine(output, new object(), CommunityOverlayCompanionProtocol.Duplicate());
            return null;
        }

        return new ManagedCompanionHost(mutex, output);
    }

    /// <summary>Raccorde la fenêtre : lecture stdin, surveillance du parent, remontée des bornes.</summary>
    public void Attach(Window window, int parentPid, TextReader input)
    {
        _window = window;
        _boundsTimer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher) { Interval = BoundsDebounce };
        _boundsTimer.Tick += (_, _) =>
        {
            _boundsTimer.Stop();
            ReportBounds();
        };
        window.LocationChanged += (_, _) => ScheduleBoundsReport();
        window.SizeChanged += (_, _) => ScheduleBoundsReport();

        WatchParent(window.Dispatcher, parentPid);
        var reader = new Thread(() => ReadCommands(window.Dispatcher, input))
        {
            IsBackground = true,
            Name = "CommunityOverlays managed stdin",
        };
        reader.Start();
    }

    public void NotifyReady() => Write(CommunityOverlayCompanionProtocol.Ready());

    public void NotifyUserClosed() => Write(CommunityOverlayCompanionProtocol.UserClosed());

    public void NotifyError(string message) => Write(CommunityOverlayCompanionProtocol.Error(message));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _boundsTimer?.Stop();
        try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex.Dispose();
    }

    /// <summary>Application d'une commande (thread UI). Publique pour les tests.</summary>
    public void Apply(CompanionMessage message)
    {
        bool displayed = ShouldDisplay;
        switch (message.Kind)
        {
            case CompanionMessageKind.Edit:
                if (IsEditMode == message.EditEnabled) return;
                IsEditMode = message.EditEnabled;
                EditModeChanged?.Invoke(IsEditMode);
                break;
            case CompanionMessageKind.Foreground:
                IsOsuForeground = message.OsuForeground;
                break;
            case CompanionMessageKind.Bounds when message.Bounds is { } bounds && _window is not null:
                _applyingBounds = true;
                try
                {
                    _window.Left = bounds.X;
                    _window.Top = bounds.Y;
                    _window.Width = bounds.Width;
                    _window.Height = bounds.Height;
                }
                finally
                {
                    _applyingBounds = false;
                }
                _lastReported = bounds; // déjà connues de BeatInsight : pas d'écho
                break;
            case CompanionMessageKind.Scale:
                ScaleChanged?.Invoke(message.ScalePercent);
                break;
            case CompanionMessageKind.Close:
                _window?.Close();
                break;
        }

        if (ShouldDisplay != displayed)
            DisplayChanged?.Invoke(ShouldDisplay);
    }

    private void ScheduleBoundsReport()
    {
        if (_applyingBounds || _boundsTimer is null || _window?.WindowState != WindowState.Normal) return;
        _boundsTimer.Stop();
        _boundsTimer.Start();
    }

    private void ReportBounds()
    {
        if (_window is null) return;
        var bounds = new CompanionBounds(_window.Left, _window.Top, _window.Width, _window.Height);
        if (!CommunityOverlayCompanionProtocol.IsValid(bounds) || bounds == _lastReported) return;
        _lastReported = bounds;
        Write(CommunityOverlayCompanionProtocol.Bounds(bounds));
    }

    private void ReadCommands(Dispatcher dispatcher, TextReader input)
    {
        try
        {
            string? line;
            while ((line = input.ReadLine()) is not null)
            {
                if (CommunityOverlayCompanionProtocol.Parse(line) is { } message)
                    dispatcher.BeginInvoke(() => Apply(message));
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
        }

        // Entrée standard fermée : BeatInsight s'arrête ou nous libère.
        dispatcher.BeginInvoke(() => Application.Current?.Shutdown(CommunityOverlayCompanionProtocol.ExitOk));
    }

    private static void WatchParent(Dispatcher dispatcher, int parentPid)
    {
        Process parent;
        try
        {
            parent = Process.GetProcessById(parentPid);
        }
        catch (ArgumentException)
        {
            dispatcher.BeginInvoke(() => Application.Current?.Shutdown(CommunityOverlayCompanionProtocol.ExitOk));
            return;
        }

        _ = parent.WaitForExitAsync().ContinueWith(_ =>
            dispatcher.BeginInvoke(() => Application.Current?.Shutdown(CommunityOverlayCompanionProtocol.ExitOk)),
            TaskScheduler.Default);
    }

    private void Write(string line) => WriteLine(_output, _outputGate, line);

    private static void WriteLine(TextWriter output, object gate, string line)
    {
        lock (gate)
        {
            try
            {
                output.WriteLine(line);
                output.Flush();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
            }
        }
    }
}
