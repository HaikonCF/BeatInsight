using System.Diagnostics;
using Microsoft.Diagnostics.Runtime;

namespace BeatInsight.Diagnostics.NativeLazer;

/// <summary>
/// Table TypeDef → MethodTable par ClrMD en attachement <b>passif</b> (<c>suspend: false</c>, sans énumération du tas),
/// relue en tâche de fond au plus toutes les 2 s quand un type attendu manque (ex. SoloPlayer pas encore chargé).
/// Jamais appelée dans la boucle de lecture.
/// </summary>
internal sealed class LazerMethodTables(int processId, string installDirectory, LazerKeyLayout layout) : ILazerMethodTables
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(2);
    private readonly object gate = new();
    private Dictionary<string, ulong> tables = [];
    private Task? refreshing;
    private long lastRefresh = long.MinValue;

    public int Refreshes { get; private set; }

    public double LastRefreshMs { get; private set; }

    public ulong? Get(string typeName)
    {
        lock (gate) return tables.TryGetValue(typeName, out ulong mt) ? mt : null;
    }

    public void RequestRefresh()
    {
        lock (gate)
        {
            if (refreshing is { IsCompleted: false }) return;
            if (lastRefresh != long.MinValue && Stopwatch.GetElapsedTime(lastRefresh) < MinimumInterval) return;
            lastRefresh = Stopwatch.GetTimestamp();
            refreshing = Task.Run(RefreshNow);
        }
    }

    /// <summary>Lecture synchrone (attachement initial, hors boucle).</summary>
    public void RefreshNow()
    {
        var watch = Stopwatch.StartNew();
        var result = new Dictionary<string, ulong>();
        try
        {
            using DataTarget target = DataTarget.AttachToProcess(processId, suspend: false,
                new DataTargetOptions { SymbolPaths = Array.Empty<string>() });
            if (target.ClrVersions.Length != 1) return;
            using ClrRuntime runtime = target.ClrVersions[0].CreateRuntime(Path.Combine(installDirectory, "mscordaccore.dll"), ignoreMismatch: true);
            foreach (ClrModule module in runtime.EnumerateModules())
            {
                string file = Path.GetFileName(module.Name ?? "");
                var wanted = layout.Types.Where(kv => string.Equals(kv.Value.Module, file, StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(kv => kv.Value.Token, kv => kv.Key);
                if (wanted.Count == 0) continue;
                foreach ((ulong mt, int token) in module.EnumerateTypeDefToMethodTableMap())
                    if (mt >= 0x10000 && wanted.TryGetValue(token, out string? name)) result[name] = mt;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return;
        }
        finally
        {
            LastRefreshMs = watch.Elapsed.TotalMilliseconds;
        }
        lock (gate)
        {
            // Un MethodTable ne change pas pendant la vie du processus : on complète, sans jamais remplacer.
            var merged = new Dictionary<string, ulong>(tables);
            foreach ((string name, ulong mt) in result)
                if (!merged.TryGetValue(name, out ulong known) || known == mt) merged[name] = mt;
            tables = merged;
            Refreshes++;
        }
    }
}

/// <summary>
/// V3.14-COM-LAZER-KEY-02 — lecteur KeyOverlay osu!lazer autonome (option A), en lecture mémoire seule.
/// <list type="bullet">
/// <item>Attachement en tâche de fond, jamais dans <see cref="Read"/> : processus unique (<c>NativeLazerProcessDiscovery</c>),
/// racine OsuGame validée par la découverte NativeLazer existante (<c>NativeLazerFastLiveSession</c>), offsets mesurés
/// (<see cref="LazerKeyLayout"/>, <c>NativeLazerGameplayClockAssemblyInspector</c>), MethodTables passifs.</item>
/// <item><see cref="Read"/> : une lecture de la chaîne (≈ 60 à 90 lectures de quelques octets), identité de partie, aucun scan.</item>
/// <item>Racine déplacée par le GC : nouvelle découverte en arrière-plan ; Unknown en attendant.</item>
/// </list>
/// <see cref="Read"/> n'est appelé que par un seul thread (boucle 100 Hz).
/// </summary>
internal sealed class NativeLazerKeyOverlayReader : IDisposable
{
    private static readonly TimeSpan AttachRetry = TimeSpan.FromSeconds(2);

    private readonly Func<DateTimeOffset> clock;
    private readonly Action<string>? log;
    private readonly object gate = new();
    private readonly LazerPlayTracker tracker = new();
    private Session? session;
    private Task<(Session? Session, string Reason)>? attaching;
    private long nextAttach;
    private bool disposed;

    private readonly bool standaloneDiscovery;

    /// <param name="standaloneDiscovery">
    /// COM-LAZER-KEY-03 : faux par défaut. La racine vient alors exclusivement de la découverte NativeLazer de production
    /// (indice partagé, lecture seule) ; aucune découverte complète n'est lancée par ce lecteur. Vrai seulement pour un
    /// usage autonome (harnais sans BeatInsight) : la découverte NativeLazer complète est alors autorisée.
    /// </param>
    public NativeLazerKeyOverlayReader(Func<DateTimeOffset>? clock = null, Action<string>? log = null, bool standaloneDiscovery = false)
    {
        this.standaloneDiscovery = standaloneDiscovery;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        this.log = log;
    }

    public int Attachments { get; private set; }
    public long PlayerMoves => tracker.PlayerMoves;
    public AttachMetrics? LastAttach { get; private set; }
    public ILazerMethodTables? MethodTables => session?.MethodTables;
    public string? BindingDiagnostic => session?.Chain.BindingDiagnostic;

    public LazerKeyOverlaySample Read()
    {
        lock (gate)
        {
            DateTimeOffset now = clock();
            if (disposed) return Unknown(null, LazerKeyFailure.Disposed, now);

            if (session is not null && !session.Memory.IsCurrentGeneration(session.Generation))
            {
                string old = session.Identity;
                Close("processus terminé");
                return Unknown(old, LazerKeyFailure.ProcessExited, now);
            }

            if (session is null)
            {
                if (attaching is { IsCompleted: true } done)
                {
                    attaching = null;
                    (Session? opened, string reason) = done.IsCompletedSuccessfully ? done.Result : (null, "exception");
                    if (opened is not null)
                    {
                        session = opened;
                        Attachments++;
                        LastAttach = opened.Metrics;
                        log?.Invoke($"LAZER KEYS | attaché à {opened.Identity} : {opened.Metrics}");
                    }
                    else
                    {
                        log?.Invoke($"LAZER KEYS | attachement non abouti : {reason}");
                    }
                }
                if (session is null)
                {
                    if (attaching is null && Stopwatch.GetTimestamp() >= nextAttach)
                    {
                        nextAttach = Stopwatch.GetTimestamp() + (long)(AttachRetry.TotalSeconds * Stopwatch.Frequency);
                        bool standalone = standaloneDiscovery;
                        attaching = Task.Run(() => Session.TryOpen(standalone, out string reason) is { } s ? (s, "ok") : ((Session?)null, reason));
                    }
                    return Unknown(null, LazerKeyFailure.Attaching, now);
                }
            }

            Session active = session;
            LazerChainRead read = active.Chain.Read(active.Counting, active.Generation, active.Root, active.Memory.IsCurrentGeneration);
            if (read.Failure == LazerKeyFailure.RootMoved)
            {
                // Racine déplacée (GC) ou remplacée : rien n'est réutilisé, nouvelle découverte en arrière-plan.
                Close("racine OsuGame invalide");
                nextAttach = 0;
            }
            (string? playIdentity, LazerPlayTransition transition) = tracker.Observe(active.Identity, read);
            bool valid = read.Validity == LazerKeyValidity.Valid && playIdentity is not null;
            return new LazerKeyOverlaySample(
                LazerKeyOverlaySample.LazerSource,
                active.Identity,
                BeatmapIdentity: null,
                valid ? playIdentity : null,
                valid ? transition : LazerPlayTransition.None,
                now,
                valid ? LazerKeyValidity.Valid : read.Validity == LazerKeyValidity.Valid ? LazerKeyValidity.Unknown : read.Validity,
                read.Failure,
                valid ? read.Left : LazerKeyOverlaySample.UnknownCounter("LeftButton"),
                valid ? read.Right : LazerKeyOverlaySample.UnknownCounter("RightButton"),
                LazerCounterAvailability.Unavailable,
                LazerCounterAvailability.Unavailable,
                valid ? read.Pressed : null,
                read.RestartCount,
                read.PlayerType,
                read.Reads,
                read.ReadMs);
        }
    }

    private static LazerKeyOverlaySample Unknown(string? identity, LazerKeyFailure failure, DateTimeOffset now) => new(
        LazerKeyOverlaySample.LazerSource, identity, null, null, LazerPlayTransition.None, now, LazerKeyValidity.Unknown, failure,
        LazerKeyOverlaySample.UnknownCounter("LeftButton"), LazerKeyOverlaySample.UnknownCounter("RightButton"),
        LazerCounterAvailability.Unavailable, LazerCounterAvailability.Unavailable, null, null, null, 0, 0);

    private void Close(string reason)
    {
        if (session is null) return;
        log?.Invoke($"LAZER KEYS | session fermée : {reason}");
        session.Dispose();
        session = null;
    }

    public void Dispose()
    {
        Task<(Session? Session, string Reason)>? pending;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            Close("arrêt");
            pending = attaching;
            attaching = null;
        }
        try
        {
            if (pending is not null && pending.Wait(TimeSpan.FromSeconds(10))) pending.Result.Session?.Dispose();
        }
        catch (AggregateException) { }
    }

    public sealed record AttachMetrics(double RootMs, string RootSource, string? DiscoveryGeneration, double LayoutMs, double MethodTablesMs, int MethodTables)
    {
        public override string ToString() =>
            $"racine {RootMs:0} ms ({RootSource}{(DiscoveryGeneration is null ? "" : $", génération {DiscoveryGeneration}")}), offsets {LayoutMs:0} ms, MethodTables {MethodTablesMs:0} ms ({MethodTables} types)";
    }

    /// <summary>Un processus osu!lazer précis : handle en lecture seule, racine validée, offsets, MethodTables.</summary>
    private sealed class Session : IDisposable
    {
        private Session(NativeLazerProcessMemory memory, NativeLazerGenerationIdentity generation, ulong root,
            LazerKeyChainReader chain, LazerMethodTables tables, AttachMetrics metrics)
        {
            Memory = memory;
            Counting = new NativeLazerCountingMemory(memory);
            Generation = generation;
            Identity = generation.Display;
            Root = root;
            Chain = chain;
            MethodTables = tables;
            Metrics = metrics;
        }

        public NativeLazerProcessMemory Memory { get; }
        public NativeLazerCountingMemory Counting { get; }
        public NativeLazerGenerationIdentity Generation { get; }
        public string Identity { get; }
        public ulong Root { get; }
        public LazerKeyChainReader Chain { get; }
        public LazerMethodTables MethodTables { get; }
        public AttachMetrics Metrics { get; }

        public static Session? TryOpen(bool standaloneDiscovery, out string reason)
        {
            NativeLazerDiscovery discovery = NativeLazerProcessDiscovery.Discover();
            if (discovery.UniqueCandidate is not { } process)
            {
                reason = $"processus osu!lazer : {discovery.Status}";
                return null;
            }
            var generation = new NativeLazerGenerationIdentity(process.ProcessId, process.StartTimeUtc);
            string directory = Path.GetDirectoryName(process.ExecutablePath) ?? "";

            // Handle propre au lecteur (QUERY_INFORMATION | VM_READ), ouvert avant la racine : la racine n'est jamais
            // lue à travers un autre handle, et l'identité du processus est vérifiée sur ce handle.
            (NativeLazerProcessMemory? memory, NativeLazerAttachStatus status) = NativeLazerProcessMemory.TryAttach(process);
            if (memory is null)
            {
                reason = $"attachement refusé ({status})";
                return null;
            }
            bool keep = false;
            try
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                ulong root;
                string rootSource;
                string? discoveryGeneration = null;
                NativeLazerSharedRoot shared = NativeLazerSharedRootSource.Acquire(process, memory, generation);
                if (shared.Status == NativeLazerSharedRootStatus.Validated)
                {
                    root = shared.RootAddress;
                    rootSource = "racine partagée NativeLazer";
                    discoveryGeneration = shared.DiscoveryGeneration;
                }
                else if (standaloneDiscovery)
                {
                    var live = new NativeLazerFastLiveSession(gameRouteResolver: null, emitAudit: false);
                    NativeLazerFastRefreshResult refreshed = live.Refresh();
                    if (!refreshed.RootValid || refreshed.CachedRoot is not { } found || refreshed.ProcessIdentity != generation)
                    {
                        reason = $"racine OsuGame non validée ({refreshed.Outcome}, {refreshed.FailureReason})";
                        return null;
                    }
                    root = found;
                    rootSource = refreshed.PersistentHintAccepted ? "indice persistant (autonome)" : "découverte complète (autonome)";
                }
                else
                {
                    reason = $"racine partagée indisponible ({shared.Status} {shared.Detail}) ; en attente de la découverte NativeLazer";
                    return null;
                }
                double rootMs = watch.Elapsed.TotalMilliseconds;

                watch.Restart();
                LazerKeyLayout layout;
                NativeLazerGameplayClockLayout? stackLayout;
                try
                {
                    layout = LazerKeyLayout.Measure(directory);
                    stackLayout = NativeLazerGameplayClockAssemblyInspector.Inspect(directory).Layout;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    reason = $"offsets non mesurables ({ex.GetType().Name})";
                    return null;
                }
                if (stackLayout is null)
                {
                    reason = "layout ScreenStack indisponible";
                    return null;
                }
                double layoutMs = watch.Elapsed.TotalMilliseconds;

                var tables = new LazerMethodTables(process.ProcessId, directory, layout);
                tables.RefreshNow();
                // Avant toute lecture : la racine doit avoir le MethodTable d'OsuGameDesktop de CE processus.
                if (tables.Get(LazerKeyLayout.OsuGameDesktop) is not { } gameMt ||
                    new NativeLazerCountingMemory(memory).ReadPointer(root) != gameMt)
                {
                    reason = "MethodTable de la racine non confirmé";
                    return null;
                }
                if (!memory.IsCurrentGeneration(generation))
                {
                    reason = "processus changé pendant l'attachement";
                    return null;
                }
                reason = "ok";
                keep = true;
                int count = layout.Types.Keys.Count(name => tables.Get(name) is not null);
                return new Session(memory, generation, root, new LazerKeyChainReader(layout, stackLayout, tables), tables,
                    new AttachMetrics(rootMs, rootSource, discoveryGeneration, layoutMs, tables.LastRefreshMs, count));
            }
            finally
            {
                if (!keep) memory.Dispose();
            }
        }

        public void Dispose() => Memory.Dispose();
    }
}
