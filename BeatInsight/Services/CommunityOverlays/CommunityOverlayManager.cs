using System.Diagnostics;
using System.IO;
using BeatInsight.Integration.CommunityOverlays.Managed;
using BeatInsight.Integration.CommunityOverlays.Pipe;

namespace BeatInsight.Services.CommunityOverlays;

public enum CommunityOverlayRuntimeState
{
    /// <summary>Aucun processus.</summary>
    Stopped,
    /// <summary>Processus lancé, page pas encore chargée (ou relance programmée).</summary>
    Starting,
    /// <summary>Page chargée.</summary>
    Running,
    /// <summary>Fermé par le ✕ de l'overlay : pas de relance automatique.</summary>
    ClosedByUser,
    /// <summary>Échec de démarrage ou arrêts répétés.</summary>
    Failed,
    /// <summary>Un autre compagnon géré tient déjà cet overlay.</summary>
    Duplicate,
    /// <summary>Dossier ou index.html introuvable.</summary>
    Missing,
    /// <summary>Exécutable du compagnon absent.</summary>
    Unavailable,
    /// <summary>Plafond de clients par pipe atteint (compagnons manuels compris).</summary>
    LimitReached,
}

public enum CommunityOverlayLaunchMode
{
    /// <summary>Données réelles de BeatInsight (pipes).</summary>
    Live,
    /// <summary>Prévisualisation avec la source mock du compagnon (aucun pipe).</summary>
    Preview,
}

public sealed record CommunityOverlayStatus(
    CommunityOverlayEntry Entry,
    CommunityOverlayRuntimeState State,
    CommunityOverlayLaunchMode? Mode,
    string? Message,
    bool IsEditing)
{
    public bool IsProcessActive => State is CommunityOverlayRuntimeState.Starting or CommunityOverlayRuntimeState.Running;
}

/// <summary>Planification différée (relance, arrêt forcé). Abstraite pour les tests.</summary>
public interface ICommunityOverlayScheduler
{
    IDisposable Schedule(TimeSpan delay, Action action);
}

/// <summary>
/// FEAT-003 : overlays communautaires gérés par BeatInsight. Un processus compagnon par overlay actif,
/// jamais deux pour le même overlay. Toutes les méthodes s'appellent sur le thread UI ; les événements des
/// processus y sont ramenés par <c>post</c>.
/// <para>Distinction des modes : un compagnon géré reçoit <c>--managed-by</c> et s'arrête avec BeatInsight ;
/// un compagnon lancé à la main (diagnostic CLI) n'est jamais piloté ni arrêté par ce gestionnaire.</para>
/// </summary>
public sealed class CommunityOverlayManager : IDisposable
{
    public const int MaxPipeClients = CommunityOverlayPipeServer.DefaultMaxClients;
    public const int MaxConsecutiveFailures = 3;
    public static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan StopGracePeriod = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);

    private readonly CommunityOverlayLibraryStore store;
    private readonly ICommunityOverlayCompanionLauncher launcher;
    private readonly int ownerProcessId;
    private readonly Action<Action> post;
    private readonly ICommunityOverlayScheduler scheduler;
    private readonly Func<int> connectedPipeClients;
    private readonly Action<bool> setPipesActive;
    private readonly Action<string>? log;
    private readonly List<CommunityOverlayEntry> entries = [];
    private readonly Dictionary<string, Runtime> runtimes = new(StringComparer.Ordinal);
    private bool pipesActive;
    private bool disposed;
    private bool osuForeground; // BUG-003 : fermé par défaut tant qu'osu! n'est pas vu au premier plan

    public CommunityOverlayManager(
        CommunityOverlayLibraryStore store,
        ICommunityOverlayCompanionLauncher launcher,
        int ownerProcessId,
        Action<Action> post,
        ICommunityOverlayScheduler scheduler,
        Func<int> connectedPipeClients,
        Action<bool> setPipesActive,
        Action<string>? log = null)
    {
        this.store = store;
        this.launcher = launcher;
        this.ownerProcessId = ownerProcessId;
        this.post = post;
        this.scheduler = scheduler;
        this.connectedPipeClients = connectedPipeClients;
        this.setPipesActive = setPipesActive;
        this.log = log;
    }

    /// <summary>État modifié (thread UI).</summary>
    public event Action? Changed;

    public bool IsCompanionAvailable => launcher.ExecutablePath is not null;

    public bool PipesActive => pipesActive;

    public bool OsuForeground => osuForeground;

    /// <summary>
    /// BUG-003 : premier plan Windows de l'instance osu! suivie (même règle que les overlays natifs).
    /// Transmis aux overlays Live seulement quand il change ; le compagnon masque sa fenêtre hors osu!
    /// sauf pendant l'édition. La prévisualisation n'est jamais masquée. Aucun processus relancé.
    /// </summary>
    public void SetOsuForeground(bool foreground)
    {
        if (disposed || osuForeground == foreground) return;
        osuForeground = foreground;
        foreach (Runtime runtime in runtimes.Values)
        {
            if (runtime.Process is not null && runtime.Mode == CommunityOverlayLaunchMode.Live && !runtime.StopRequested)
                runtime.Process.TrySend(CommunityOverlayCompanionProtocol.Foreground(foreground));
        }
    }

    public IReadOnlyList<CommunityOverlayStatus> Statuses =>
        entries.Select(entry =>
        {
            Runtime runtime = RuntimeOf(entry.Id);
            return new CommunityOverlayStatus(entry, runtime.State, runtime.Mode, runtime.Message, runtime.Editing);
        }).ToArray();

    /// <summary>Charge la configuration et relance les overlays activés (démarrage de BeatInsight).</summary>
    public void LoadAndStartEnabled()
    {
        entries.Clear();
        entries.AddRange(store.Load());
        foreach (CommunityOverlayEntry entry in entries.Where(e => e.Enabled).ToArray())
            Launch(entry.Id, CommunityOverlayLaunchMode.Live);
        RaiseChanged();
    }

    public CommunityOverlayImportResult Import(string? folderPath)
    {
        CommunityOverlayImportResult result = CommunityOverlayImportValidator.Validate(folderPath, entries);
        if (result.Entry is { } entry)
        {
            entries.Add(entry);
            Save();
            log?.Invoke($"COMMUNITY OVERLAYS | importé : {entry.Name}");
            RaiseChanged();
        }
        return result;
    }

    /// <summary>Retire l'overlay de la configuration. Son dossier et son profil WebView2 sont conservés.</summary>
    public bool Remove(string id)
    {
        int index = entries.FindIndex(e => e.Id == id);
        if (index < 0) return false;
        StopProcess(RuntimeOf(id), CommunityOverlayRuntimeState.Stopped);
        entries.RemoveAt(index);
        runtimes.Remove(id);
        Save();
        UpdatePipes();
        RaiseChanged();
        return true;
    }

    /// <summary>Activation persistée : lance (Live) ou arrête l'overlay, et le relance au prochain démarrage.</summary>
    public void SetEnabled(string id, bool enabled)
    {
        if (!TryEntry(id, out CommunityOverlayEntry entry)) return;
        if (entry.Enabled != enabled)
        {
            Replace(entry with { Enabled = enabled });
            Save();
        }
        if (enabled) Launch(id, CommunityOverlayLaunchMode.Live);
        else StopProcess(RuntimeOf(id), CommunityOverlayRuntimeState.Stopped);
        RaiseChanged();
    }

    /// <summary>Lancement pour cette session, sans changer l'activation persistée.</summary>
    public void Start(string id)
    {
        Launch(id, CommunityOverlayLaunchMode.Live);
        RaiseChanged();
    }

    public void Preview(string id)
    {
        Launch(id, CommunityOverlayLaunchMode.Preview);
        RaiseChanged();
    }

    /// <summary>Arrêt pour cette session, sans changer l'activation persistée.</summary>
    public void Stop(string id)
    {
        StopProcess(RuntimeOf(id), CommunityOverlayRuntimeState.Stopped);
        RaiseChanged();
    }

    /// <summary>Mode édition du compagnon : poignée de déplacement et redimensionnement visibles.</summary>
    public void SetEditMode(string id, bool editing)
    {
        if (!TryEntry(id, out _)) return;
        Runtime runtime = RuntimeOf(id);
        runtime.Editing = editing;
        runtime.Process?.TrySend(CommunityOverlayCompanionProtocol.Edit(editing));
        RaiseChanged();
    }

    /// <summary>Position et taille depuis les Settings : persistées, et appliquées tout de suite si l'overlay tourne.</summary>
    public bool SetBounds(string id, CompanionBounds bounds)
    {
        if (!CommunityOverlayCompanionProtocol.IsValid(bounds) || !TryEntry(id, out CommunityOverlayEntry entry))
            return false;
        Replace(entry with { Bounds = bounds });
        Save();
        RuntimeOf(id).Process?.TrySend(CommunityOverlayCompanionProtocol.Bounds(bounds));
        RaiseChanged();
        return true;
    }

    /// <summary>
    /// FEAT-003.1 : échelle du contenu (zoom WebView2), normalisée dans [25, 200] au pas de 5, persistée,
    /// et appliquée tout de suite si l'overlay tourne (Live ou prévisualisation). La fenêtre garde sa taille.
    /// </summary>
    public bool SetScale(string id, int percent)
    {
        if (!TryEntry(id, out CommunityOverlayEntry entry))
            return false;
        int normalized = CommunityOverlayCompanionProtocol.NormalizeScale(percent);
        if (entry.Scale != normalized)
        {
            Replace(entry with { Scale = normalized });
            Save();
            RuntimeOf(id).Process?.TrySend(CommunityOverlayCompanionProtocol.Scale(normalized));
        }
        RaiseChanged();
        return true;
    }

    /// <summary>
    /// Fermeture de BeatInsight : demande l'arrêt de chaque compagnon (commande close puis fin de stdin),
    /// attend au plus <see cref="ShutdownTimeout"/> au total, puis termine ceux qui restent.
    /// Filet de sécurité côté compagnon : il s'arrête seul quand BeatInsight disparaît (<c>--managed-by</c>).
    /// </summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var active = runtimes.Values.Where(r => r.Process is not null).ToArray();
        foreach (Runtime runtime in active)
        {
            runtime.RestartTimer?.Dispose();
            runtime.KillTimer?.Dispose();
            runtime.StopRequested = true;
            runtime.Process!.TrySend(CommunityOverlayCompanionProtocol.Close());
            runtime.Process.CloseInput();
        }

        var deadline = Stopwatch.StartNew();
        foreach (Runtime runtime in active)
        {
            ICommunityOverlayCompanionProcess process = runtime.Process!;
            TimeSpan remaining = ShutdownTimeout - deadline.Elapsed;
            if (remaining <= TimeSpan.Zero || !process.WaitForExit(remaining))
            {
                log?.Invoke($"COMMUNITY OVERLAYS | arrêt forcé du compagnon (pid {process.ProcessId})");
                process.Kill();
            }
            process.Dispose();
            runtime.Process = null;
            runtime.State = CommunityOverlayRuntimeState.Stopped;
        }
        if (pipesActive)
        {
            pipesActive = false;
            setPipesActive(false);
        }
    }

    private void Launch(string id, CommunityOverlayLaunchMode mode)
    {
        if (disposed || !TryEntry(id, out CommunityOverlayEntry entry)) return;
        Runtime runtime = RuntimeOf(id);

        if (runtime.Process is not null)
        {
            if (runtime.Mode == mode) return; // jamais deux processus pour le même overlay
            StopProcess(runtime, CommunityOverlayRuntimeState.Stopped);
            if (runtime.Process is not null)
            {
                // L'ancien processus (autre mode) n'est pas encore sorti : relance à sa sortie.
                runtime.PendingMode = mode;
                return;
            }
        }

        runtime.RestartTimer?.Dispose();
        runtime.RestartTimer = null;
        runtime.PendingMode = null;
        runtime.Mode = mode;
        runtime.Desired = true;
        runtime.StopRequested = false;
        runtime.ClosedByUser = false;
        runtime.DuplicateReported = false;
        runtime.Failures = 0; // une relance automatique restaure son propre compteur
        runtime.Message = null;

        if (!Directory.Exists(entry.FolderPath) || !File.Exists(Path.Combine(entry.FolderPath, CommunityOverlayEntry.EntryDocument)))
        {
            SetIdle(runtime, CommunityOverlayRuntimeState.Missing, $"Folder or {CommunityOverlayEntry.EntryDocument} not found: {entry.FolderPath}");
            return;
        }

        if (!IsCompanionAvailable)
        {
            SetIdle(runtime, CommunityOverlayRuntimeState.Unavailable, "The Community Overlays companion is not installed next to BeatInsight.");
            return;
        }

        if (mode == CommunityOverlayLaunchMode.Live)
        {
            int managedLive = runtimes.Values.Count(r => r.Process is not null && r.Mode == CommunityOverlayLaunchMode.Live);
            int connected = pipesActive ? connectedPipeClients() : 0;
            if (Math.Max(managedLive, connected) >= MaxPipeClients)
            {
                SetIdle(runtime, CommunityOverlayRuntimeState.LimitReached,
                    $"Limit of {MaxPipeClients} connected overlays reached (manually launched companions included).");
                return;
            }
            runtime.LiveSlot = true;
            UpdatePipes(); // pipes prêts avant que le compagnon ne s'y connecte
        }

        ICommunityOverlayCompanionProcess process;
        try
        {
            process = launcher.Start(BuildArguments(entry, mode));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            runtime.LiveSlot = false;
            SetIdle(runtime, CommunityOverlayRuntimeState.Failed, $"Could not start: {ex.Message}");
            UpdatePipes();
            return;
        }

        runtime.Process = process;
        runtime.State = CommunityOverlayRuntimeState.Starting;
        runtime.Message = null;
        process.LineReceived += line => post(() => OnLine(runtime, process, line));
        process.Exited += code => post(() => OnExited(runtime, process, code));
        if (runtime.Editing)
            process.TrySend(CommunityOverlayCompanionProtocol.Edit(true));
        if (mode == CommunityOverlayLaunchMode.Live)
            process.TrySend(CommunityOverlayCompanionProtocol.Foreground(osuForeground));
        log?.Invoke($"COMMUNITY OVERLAYS | compagnon lancé : {entry.Name} ({mode}, pid {process.ProcessId})");
    }

    internal IReadOnlyList<string> BuildArguments(CommunityOverlayEntry entry, CommunityOverlayLaunchMode mode)
    {
        var arguments = new List<string> { "--overlay", entry.FolderPath };
        if (mode == CommunityOverlayLaunchMode.Live)
            arguments.AddRange(["--source", "live-pipe", "--precise-pipe", "--precise-v2", "--selection-pipe"]);
        else
            arguments.AddRange(["--source", "mock"]);
        if (entry.Bounds is { } bounds)
            arguments.AddRange(["--window", CommunityOverlayCompanionProtocol.WindowArgument(bounds)]);
        int scale = CommunityOverlayCompanionProtocol.NormalizeScale(entry.Scale);
        if (scale != CommunityOverlayCompanionProtocol.DefaultScalePercent)
            arguments.AddRange([CommunityOverlayCompanionProtocol.ScaleArgument,
                scale.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        arguments.AddRange([CommunityOverlayCompanionProtocol.ManagedByArgument,
            ownerProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        return arguments;
    }

    private void OnLine(Runtime runtime, ICommunityOverlayCompanionProcess process, string line)
    {
        if (disposed || !ReferenceEquals(runtime.Process, process)) return;
        if (CommunityOverlayCompanionProtocol.Parse(line) is not { } message) return;

        switch (message.Kind)
        {
            case CompanionMessageKind.Ready:
                runtime.State = CommunityOverlayRuntimeState.Running;
                runtime.Failures = 0;
                runtime.Message = null;
                break;
            case CompanionMessageKind.Bounds when message.Bounds is { } bounds:
                if (TryEntry(runtime.Id, out CommunityOverlayEntry entry) && entry.Bounds != bounds)
                {
                    Replace(entry with { Bounds = bounds });
                    Save();
                }
                break;
            case CompanionMessageKind.UserClosed:
                runtime.ClosedByUser = true;
                break;
            case CompanionMessageKind.Duplicate:
                runtime.DuplicateReported = true;
                break;
            case CompanionMessageKind.Error:
                runtime.Message = message.Message;
                break;
            default:
                return;
        }
        RaiseChanged();
    }

    private void OnExited(Runtime runtime, ICommunityOverlayCompanionProcess process, int code)
    {
        if (disposed || !ReferenceEquals(runtime.Process, process)) return;
        runtime.Process = null;
        runtime.LiveSlot = false;
        runtime.KillTimer?.Dispose();
        runtime.KillTimer = null;
        process.Dispose();

        if (runtime.StopRequested)
        {
            runtime.State = CommunityOverlayRuntimeState.Stopped;
            if (runtime.PendingMode is { } pending && runtimes.ContainsKey(runtime.Id))
            {
                Launch(runtime.Id, pending);
                RaiseChanged();
                return;
            }
        }
        else if (runtime.ClosedByUser)
        {
            runtime.Desired = false;
            runtime.State = CommunityOverlayRuntimeState.ClosedByUser;
            runtime.Message = "Closed from the overlay window. It will not restart automatically.";
        }
        else if (runtime.DuplicateReported || code == CommunityOverlayCompanionProtocol.ExitDuplicate)
        {
            runtime.Desired = false;
            runtime.State = CommunityOverlayRuntimeState.Duplicate;
            runtime.Message = "This overlay is already open in another managed companion.";
        }
        else
        {
            runtime.Failures++;
            runtime.Message ??= $"The companion stopped unexpectedly (exit code {code}).";
            if (runtime.Desired && runtime.Mode == CommunityOverlayLaunchMode.Live && runtime.Failures < MaxConsecutiveFailures)
            {
                runtime.State = CommunityOverlayRuntimeState.Starting;
                runtime.RestartPending = true;
                runtime.RestartTimer = scheduler.Schedule(RestartDelay, () =>
                {
                    runtime.RestartPending = false;
                    runtime.RestartTimer = null;
                    if (!disposed && runtime.Desired && runtime.Process is null && runtimes.ContainsKey(runtime.Id))
                    {
                        int failures = runtime.Failures;
                        string? message = runtime.Message;
                        Launch(runtime.Id, CommunityOverlayLaunchMode.Live);
                        if (runtime.State == CommunityOverlayRuntimeState.Starting)
                        {
                            runtime.Failures = failures; // la relance n'efface pas l'historique d'échecs
                            runtime.Message = message;
                        }
                    }
                    UpdatePipes();
                    RaiseChanged();
                });
                log?.Invoke($"COMMUNITY OVERLAYS | compagnon arrêté (code {code}), relance {runtime.Failures}/{MaxConsecutiveFailures - 1}");
            }
            else
            {
                runtime.Desired = false;
                runtime.State = CommunityOverlayRuntimeState.Failed;
                log?.Invoke($"COMMUNITY OVERLAYS | compagnon en échec (code {code}) : {runtime.Message}");
            }
        }

        UpdatePipes();
        RaiseChanged();
    }

    private void StopProcess(Runtime runtime, CommunityOverlayRuntimeState idleState)
    {
        runtime.Desired = false;
        runtime.PendingMode = null;
        runtime.RestartPending = false;
        runtime.RestartTimer?.Dispose();
        runtime.RestartTimer = null;

        ICommunityOverlayCompanionProcess? process = runtime.Process;
        if (process is null)
        {
            if (runtime.State is CommunityOverlayRuntimeState.Starting or CommunityOverlayRuntimeState.Running)
                SetIdle(runtime, idleState, null);
            UpdatePipes();
            return;
        }

        if (runtime.StopRequested) return; // arrêt déjà demandé
        runtime.StopRequested = true;
        process.TrySend(CommunityOverlayCompanionProtocol.Close());
        process.CloseInput();
        runtime.KillTimer = scheduler.Schedule(StopGracePeriod, () =>
        {
            if (ReferenceEquals(runtime.Process, process))
            {
                log?.Invoke($"COMMUNITY OVERLAYS | arrêt forcé du compagnon (pid {process.ProcessId})");
                process.Kill();
            }
        });
    }

    private static void SetIdle(Runtime runtime, CommunityOverlayRuntimeState state, string? message)
    {
        runtime.State = state;
        runtime.Message = message;
        if (state != CommunityOverlayRuntimeState.Starting)
            runtime.Desired = false;
    }

    /// <summary>Pipes BeatInsight actifs tant qu'un overlay Live tourne ou va être relancé.</summary>
    private void UpdatePipes()
    {
        if (disposed) return;
        bool needed = runtimes.Values.Any(r => r.LiveSlot || r.RestartPending ||
            (r.Process is not null && r.Mode == CommunityOverlayLaunchMode.Live));
        if (needed == pipesActive) return;
        pipesActive = needed;
        log?.Invoke($"COMMUNITY OVERLAYS | pipes {(needed ? "activés" : "désactivés")} par le mode géré");
        setPipesActive(needed);
    }

    private bool TryEntry(string id, out CommunityOverlayEntry entry)
    {
        entry = entries.FirstOrDefault(e => e.Id == id)!;
        return entry is not null;
    }

    private void Replace(CommunityOverlayEntry entry)
    {
        int index = entries.FindIndex(e => e.Id == entry.Id);
        if (index >= 0) entries[index] = entry;
    }

    private void Save()
    {
        if (!store.Save(entries))
            log?.Invoke("COMMUNITY OVERLAYS | configuration non enregistrée (écriture impossible)");
    }

    private Runtime RuntimeOf(string id)
    {
        if (!runtimes.TryGetValue(id, out Runtime? runtime))
        {
            runtime = new Runtime(id);
            runtimes[id] = runtime;
        }
        return runtime;
    }

    private void RaiseChanged() => Changed?.Invoke();

    private sealed class Runtime(string id)
    {
        public string Id { get; } = id;
        public CommunityOverlayRuntimeState State { get; set; } = CommunityOverlayRuntimeState.Stopped;
        public CommunityOverlayLaunchMode? Mode { get; set; }
        public string? Message { get; set; }
        public ICommunityOverlayCompanionProcess? Process { get; set; }
        public bool Desired { get; set; }
        public bool StopRequested { get; set; }
        public bool ClosedByUser { get; set; }
        public bool DuplicateReported { get; set; }
        public bool Editing { get; set; }
        public bool LiveSlot { get; set; }
        public bool RestartPending { get; set; }
        public int Failures { get; set; }
        public CommunityOverlayLaunchMode? PendingMode { get; set; }
        public IDisposable? RestartTimer { get; set; }
        public IDisposable? KillTimer { get; set; }
    }
}
