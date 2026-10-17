using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BeatInsight.Integration.KeyOverlay;

/// <summary>
/// Minuterie haute résolution locale (CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, Windows 10 1803+) :
/// aucune modification de la résolution globale du timer Windows (pas de timeBeginPeriod).
/// </summary>
internal sealed class HighResolutionWaitableTimer : WaitHandle
{
    private const uint CreateWaitableTimerHighResolution = 0x00000002;
    private const uint TimerAllAccess = 0x1F0003;

    public HighResolutionWaitableTimer()
    {
        nint handle = CreateWaitableTimerExW(0, null, CreateWaitableTimerHighResolution, TimerAllAccess);
        IsHighResolution = handle != 0;
        if (handle == 0)
            handle = CreateWaitableTimerExW(0, null, 0, TimerAllAccess);
        if (handle == 0)
            throw new InvalidOperationException("CreateWaitableTimerExW a échoué.");
        SafeWaitHandle = new SafeWaitHandle(handle, ownsHandle: true);
    }

    public bool IsHighResolution { get; }

    /// <summary>Arme la minuterie pour un déclenchement unique après <paramref name="delay"/>.</summary>
    public bool Arm(TimeSpan delay)
    {
        long due = -Math.Max(1, delay.Ticks); // relatif, unités de 100 ns
        return SetWaitableTimer(SafeWaitHandle, ref due, 0, 0, 0, false);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWaitableTimerExW(nint attributes, string? name, uint flags, uint access);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long dueTime, int period, nint completion, nint arg, bool resume);
}

/// <summary>
/// Statistiques de la boucle (lecture seule après Stop). Les mesures détaillées sont bornées
/// (fenêtre glissante) pour qu'une boucle longue ne fasse pas croître la mémoire.
/// </summary>
internal sealed class KeyOverlayLoopStats
{
    public const int MaxRetainedMeasures = 60_000;

    public long Ticks;
    public long ValidSamples;
    public long SkippedTicks;
    public long SinkFailures;
    public double TickDurationSumMs;
    public double TickDurationMaxMs;
    public readonly BoundedMeasures TickDurationsMs = new(MaxRetainedMeasures);
    public readonly BoundedMeasures IntervalsMs = new(MaxRetainedMeasures);
    public TimeSpan Cpu;
    public double WallSeconds;
    public bool HighResolution;
}

/// <summary>Fenêtre glissante de mesures (les plus récentes), de taille bornée.</summary>
internal sealed class BoundedMeasures(int capacity) : IEnumerable<double>
{
    private readonly double[] values = new double[capacity];
    private long added;

    public int Count => (int)Math.Min(added, values.Length);

    public void Add(double value) => values[added++ % values.Length] = value;

    public IEnumerator<double> GetEnumerator()
    {
        for (int i = 0; i < Count; i++) yield return values[i];
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Boucle indépendante à cadence fixe sur un thread dédié (pas de Dispatcher WPF, pas de ThreadPool).
/// Garanties : un seul tick à la fois (thread unique), aucun rattrapage en rafale (les ticks en retard
/// sont sautés et comptés), arrêt immédiat (attente sur la minuterie OU l'événement d'arrêt),
/// Dispose attend la fin du tick courant avant de rendre la main.
/// </summary>
internal sealed class KeyOverlayPollingLoop : IDisposable
{
    private readonly Func<KeyOverlaySample> read;
    private readonly Action<KeyOverlaySample> sink;
    private readonly TimeSpan period;
    private readonly ManualResetEvent stopEvent = new(false);
    private readonly Thread thread;
    private readonly KeyOverlayLoopStats stats = new();
    private int state; // 0 créé, 1 démarré, 2 arrêté

    public KeyOverlayPollingLoop(Func<KeyOverlaySample> read, Action<KeyOverlaySample> sink, TimeSpan period)
    {
        if (period <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(period));
        this.read = read;
        this.sink = sink;
        this.period = period;
        thread = new Thread(Run) { IsBackground = true, Name = "KeyOverlayPollingLoop", Priority = ThreadPriority.AboveNormal };
    }

    public KeyOverlayLoopStats Stats => stats;

    public void Start()
    {
        if (Interlocked.CompareExchange(ref state, 1, 0) != 0)
            throw new InvalidOperationException("Boucle déjà démarrée ou arrêtée.");
        thread.Start();
    }

    /// <summary>Demande l'arrêt et attend la fin du tick courant (borné).</summary>
    public bool Stop(TimeSpan timeout)
    {
        int previous = Interlocked.Exchange(ref state, 2);
        stopEvent.Set();
        if (previous != 1) return true;
        return thread.Join(timeout);
    }

    public void Dispose()
    {
        Stop(TimeSpan.FromSeconds(2));
        stopEvent.Dispose();
    }

    private void Run()
    {
        using var timer = new HighResolutionWaitableTimer();
        stats.HighResolution = timer.IsHighResolution;
        WaitHandle[] handles = [stopEvent, timer];
        long periodTicks = (long)(period.TotalSeconds * Stopwatch.Frequency);
        var wall = Stopwatch.StartNew();
        TimeSpan cpu0 = Process.GetCurrentProcess().TotalProcessorTime;
        long next = Stopwatch.GetTimestamp();
        long lastStart = 0;

        while (!stopEvent.WaitOne(0))
        {
            long start = Stopwatch.GetTimestamp();
            if (lastStart != 0) stats.IntervalsMs.Add(Ms(start - lastStart));
            lastStart = start;

            KeyOverlaySample sample = read();
            stats.Ticks++;
            if (sample.IsValid) stats.ValidSamples++;
            try { sink(sample); }
            catch (Exception) { stats.SinkFailures++; }
            double tickMs = Ms(Stopwatch.GetTimestamp() - start);
            stats.TickDurationsMs.Add(tickMs);
            stats.TickDurationSumMs += tickMs;
            if (tickMs > stats.TickDurationMaxMs) stats.TickDurationMaxMs = tickMs;

            // Échéance suivante ; si elle est déjà passée, on saute (pas de rafale).
            next += periodTicks;
            long now = Stopwatch.GetTimestamp();
            if (next <= now)
            {
                long late = now - next;
                long skipped = late / periodTicks + 1;
                stats.SkippedTicks += skipped;
                next += skipped * periodTicks;
            }
            if (!timer.Arm(TimeSpan.FromSeconds((double)(next - now) / Stopwatch.Frequency)))
            {
                if (stopEvent.WaitOne(period)) break;
                continue;
            }
            if (WaitHandle.WaitAny(handles) == 0) break;
        }

        stats.WallSeconds = wall.Elapsed.TotalSeconds;
        stats.Cpu = Process.GetCurrentProcess().TotalProcessorTime - cpu0;
    }

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}
