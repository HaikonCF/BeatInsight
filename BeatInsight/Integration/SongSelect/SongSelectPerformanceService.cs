using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using BeatInsight.Integration.Game;
using BeatInsight.Services;

namespace BeatInsight.Integration.SongSelect;

/// <summary>Sélection courante : processus, beatmap et mods validés (V3.14-COM-PP-03).</summary>
internal sealed record SongSelectSelection(string ProcessIdentity, string BeatmapPath, IReadOnlyList<string> Mods, string ModsKey);

/// <summary>
/// Clé d'un résultat : il ne vaut que pour ce processus, ce fichier .osu exact (taille et date) et ces mods,
/// avec cette version du calculateur.
/// </summary>
internal sealed record SongSelectPerformanceKey(
    string ProcessIdentity,
    string BeatmapPath,
    long FileLength,
    long LastWriteTicks,
    string ModsKey,
    string CalculatorVersion);

internal enum SongSelectPerformanceStatus
{
    /// <summary>Aucune sélection valide (mods ou beatmap inconnus, autre écran).</summary>
    Unknown,

    /// <summary>Calcul en attente ou en cours pour la sélection courante.</summary>
    Pending,

    Ready,

    /// <summary>Calcul impossible pour cette sélection (mods non pris en charge, autre mode, fichier illisible).</summary>
    Failed,
}

/// <summary>
/// État pour la sélection courante. <see cref="Generation"/> croît strictement à chaque changement de sélection :
/// un résultat n'est jamais présenté pour une autre génération que la sienne.
/// </summary>
internal sealed record SongSelectPerformanceState(
    SongSelectPerformanceStatus Status,
    long Generation,
    SongSelectPerformanceKey? Key,
    LiveStaticPerformance? Value,
    DateTimeOffset? ComputedAtUtc,
    string Reason)
{
    public static SongSelectPerformanceState None(long generation, string reason) =>
        new(SongSelectPerformanceStatus.Unknown, generation, null, null, null, reason);
}

internal sealed record SongSelectPerformanceStatistics(
    long Requests,
    long CacheHits,
    long CacheMisses,
    long Computations,
    long Cancelled,
    long StaleRejected,
    long Failures,
    int CacheEntries,
    double ComputeTotalMs,
    double ComputeMaxMs,
    double? LastComputeMs);

/// <summary>
/// V3.14-COM-PP-03 : PP SS (et 95 %) de la beatmap sélectionnée, calculés en arrière-plan.
/// <list type="bullet">
/// <item><see cref="Update"/> ne calcule jamais : il lit le cache ou demande un calcul, puis rend l'état courant.
/// Il peut être appelé à chaque tick sans coût (un <c>FileInfo</c> et une recherche de dictionnaire).</item>
/// <item>Un seul thread de calcul dédié (le calculateur n'est pas thread-safe) ; debounce après chaque changement ;
/// annulation coopérative du calcul devenu inutile ; résultat périmé (autre génération) rejeté.</item>
/// <item>Cache LRU borné (résultats et échecs) ; une nouvelle instance du calculateur par calcul, donc aucune
/// croissance des caches internes du calculateur.</item>
/// </list>
/// </summary>
internal sealed class SongSelectPerformanceService : IDisposable
{
    public const int DefaultCapacity = 32;
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(150);

    public delegate LiveStaticPerformanceResult Compute(SongSelectPerformanceKey key, IReadOnlyList<string> mods, CancellationToken cancellationToken);

    private readonly Compute compute;
    private readonly string calculatorVersion;
    private readonly TimeSpan debounce;
    private readonly int capacity;
    private readonly Func<DateTimeOffset> clock;
    private readonly object gate = new();
    private readonly AutoResetEvent wake = new(false);
    private readonly Thread worker;
    private readonly Dictionary<SongSelectPerformanceKey, LinkedListNode<CacheEntry>> cache = [];
    private readonly LinkedList<CacheEntry> lru = new();

    private long generation;
    private SongSelectPerformanceKey? currentKey;
    private (string Process, string Path, string Mods)? currentSelection;
    private IReadOnlyList<string> currentMods = [];
    private long changedAtTicks;
    private string currentReason = "aucune sélection";
    private CancellationTokenSource? running;
    private SongSelectPerformanceKey? runningKey;
    private bool disposed;

    private long requests, hits, misses, computations, cancelled, staleRejected, failures;
    private double computeTotalMs, computeMaxMs;
    private double? lastComputeMs;

    public SongSelectPerformanceService(
        Compute? compute = null,
        string? calculatorVersion = null,
        TimeSpan? debounce = null,
        int capacity = DefaultCapacity,
        Func<DateTimeOffset>? clock = null)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.compute = compute ?? DefaultCompute;
        this.calculatorVersion = calculatorVersion ?? OsuLivePerformanceCalculator.StaticPerformanceVersion;
        this.debounce = debounce ?? DefaultDebounce;
        this.capacity = capacity;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        worker = new Thread(Run) { IsBackground = true, Name = "BeatInsight SongSelect PP", Priority = ThreadPriority.BelowNormal };
        worker.Start();
    }

    public string CalculatorVersion => calculatorVersion;

    /// <summary>
    /// Déclare la sélection courante (null : aucune sélection valide) et rend l'état à publier. Jamais bloquant.
    /// </summary>
    public SongSelectPerformanceState Update(SongSelectSelection? selection)
    {
        string? reason = null;
        SongSelectPerformanceKey? key = selection is null ? null : KeyFor(selection, out reason);
        lock (gate)
        {
            if (disposed) return SongSelectPerformanceState.None(generation, "service arrêté");
            requests++;
            (string, string, string)? identity = selection is null ? null : (selection.ProcessIdentity, selection.BeatmapPath, selection.ModsKey);
            if (!Equals(key, currentKey) || !Equals(identity, currentSelection))
            {
                generation++;
                currentKey = key;
                currentSelection = identity;
                currentMods = selection?.Mods ?? [];
                changedAtTicks = Stopwatch.GetTimestamp();
                currentReason = selection is null ? "aucune sélection" : key is null ? reason ?? "beatmap indisponible" : "calcul en attente";
                // Le calcul en cours ne sert plus : annulation coopérative.
                if (running is not null && !Equals(runningKey, key))
                    running.Cancel();
                if (key is not null)
                {
                    if (cache.ContainsKey(key)) hits++;
                    else
                    {
                        misses++;
                        wake.Set();
                    }
                }
            }

            if (key is null)
                return SongSelectPerformanceState.None(generation, currentReason);
            if (cache.TryGetValue(key, out LinkedListNode<CacheEntry>? node))
            {
                Touch(node);
                CacheEntry entry = node.Value;
                return entry.Result.Value is { } value
                    ? new(SongSelectPerformanceStatus.Ready, generation, key, value, entry.ComputedAtUtc, "ok")
                    : new(SongSelectPerformanceStatus.Failed, generation, key, null, entry.ComputedAtUtc, entry.Result.FailureReason.ToString());
            }
            return new(SongSelectPerformanceStatus.Pending, generation, key, null, null, Equals(runningKey, key) ? "calcul en cours" : "calcul en attente");
        }
    }

    public SongSelectPerformanceStatistics Statistics
    {
        get
        {
            lock (gate)
                return new(requests, hits, misses, computations, cancelled, staleRejected, failures, cache.Count,
                    computeTotalMs, computeMaxMs, lastComputeMs);
        }
    }

    private SongSelectPerformanceKey? KeyFor(SongSelectSelection selection, out string? reason)
    {
        try
        {
            string fullPath = Path.GetFullPath(selection.BeatmapPath);
            var file = new FileInfo(fullPath);
            if (!file.Exists)
            {
                reason = "beatmap absente";
                return null;
            }
            reason = null;
            return new SongSelectPerformanceKey(selection.ProcessIdentity, fullPath, file.Length, file.LastWriteTimeUtc.Ticks,
                selection.ModsKey, calculatorVersion);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            reason = "beatmap illisible";
            return null;
        }
    }

    private void Run()
    {
        while (true)
        {
            wake.WaitOne();
            while (true)
            {
                SongSelectPerformanceKey key;
                IReadOnlyList<string> mods;
                long jobGeneration;
                CancellationTokenSource cts;
                lock (gate)
                {
                    if (disposed) return;
                    if (currentKey is null || cache.ContainsKey(currentKey)) break;
                    TimeSpan waited = Stopwatch.GetElapsedTime(changedAtTicks);
                    if (waited < debounce)
                    {
                        TimeSpan remaining = debounce - waited;
                        Monitor.Exit(gate);
                        try { wake.WaitOne(remaining); }
                        finally { Monitor.Enter(gate); }
                        continue;
                    }
                    key = currentKey;
                    mods = currentMods;
                    jobGeneration = generation;
                    cts = new CancellationTokenSource();
                    running = cts;
                    runningKey = key;
                }

                LiveStaticPerformanceResult? result = null;
                long started = Stopwatch.GetTimestamp();
                try
                {
                    result = compute(key, mods, cts.Token);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception)
                {
                    result = new(null, LivePerformanceFailureReason.CalculationFailed);
                }
                double ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

                lock (gate)
                {
                    running = null;
                    runningKey = null;
                    if (result is null)
                    {
                        cancelled++;
                    }
                    else
                    {
                        computations++;
                        computeTotalMs += ms;
                        computeMaxMs = Math.Max(computeMaxMs, ms);
                        lastComputeMs = ms;
                        if (result.Value is null) failures++;
                        // Le résultat vaut pour SA clé : il entre au cache. Il n'est publié que si la sélection
                        // n'a pas changé entre-temps (sinon il est compté comme périmé et non présenté).
                        Store(key, result);
                        if (jobGeneration != generation || !Equals(currentKey, key))
                            staleRejected++;
                    }
                }
                cts.Dispose();
            }
        }
    }

    private void Store(SongSelectPerformanceKey key, LiveStaticPerformanceResult result)
    {
        if (cache.TryGetValue(key, out LinkedListNode<CacheEntry>? existing))
        {
            lru.Remove(existing);
            cache.Remove(key);
        }
        var node = lru.AddFirst(new CacheEntry(key, result, clock()));
        cache[key] = node;
        while (cache.Count > capacity)
        {
            LinkedListNode<CacheEntry> last = lru.Last!;
            lru.RemoveLast();
            cache.Remove(last.Value.Key);
        }
    }

    private void Touch(LinkedListNode<CacheEntry> node)
    {
        if (node.Previous is null) return;
        lru.Remove(node);
        lru.AddFirst(node);
    }

    private static LiveStaticPerformanceResult DefaultCompute(SongSelectPerformanceKey key, IReadOnlyList<string> mods, CancellationToken cancellationToken)
    {
        // Instance dédiée par calcul : aucun partage avec le calculateur du gameplay, aucun cache interne qui grossit.
        var calculator = new OsuLivePerformanceCalculator();
        ImmutableArray<LiveGameplayMod> liveMods = mods.Select(acronym => new LiveGameplayMod(acronym)).ToImmutableArray();
        return calculator.CalculateStatic(key.BeatmapPath, liveMods, cancellationToken);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            running?.Cancel();
        }
        wake.Set();
        // Un calcul non interruptible (rare) peut dépasser le délai : l'événement reste alors en vie,
        // le thread de fond le relira avant de sortir.
        if (worker.Join(TimeSpan.FromSeconds(5)))
            wake.Dispose();
    }

    private sealed record CacheEntry(SongSelectPerformanceKey Key, LiveStaticPerformanceResult Result, DateTimeOffset ComputedAtUtc);
}
