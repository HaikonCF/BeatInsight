using System.Diagnostics;
using System.IO;

namespace BeatInsight.Integration.KeyOverlay.Lazer;

/// <summary>
/// V3.14-COM-LAZER-KEY-03 (expérimental, opt-in) : pilote le processus lecteur Lazer (.NET 10, hors de BeatInsight).
/// <list type="bullet">
/// <item>Le lecteur tourne dans son propre processus : BeatInsight (.NET 8) ne charge aucun assembly de Lazer et ne mesure
/// aucun offset ; aucun accès mémoire à Lazer n'a lieu dans BeatInsight.</item>
/// <item>Chaque ligne de sa sortie est validée (<see cref="LazerKeyWire"/>), avec session et séquence strictement
/// croissantes ; une ligne refusée est ignorée, jamais corrigée.</item>
/// <item>Le processus s'arrête quand BeatInsight ferme son entrée standard ; s'il s'arrête seul, il est relancé (2 s).</item>
/// </list>
/// </summary>
internal sealed class LazerKeyRunnerSource : IDisposable
{
    public const string RunnerVariable = "BEATINSIGHT_LAZER_KEYS_RUNNER";
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(2);

    private readonly string runnerPath;
    private readonly Action<string>? log;
    private readonly Func<DateTimeOffset> clock;
    private readonly CancellationTokenSource stop = new();
    private readonly object gate = new();
    private Thread? thread;
    private Process? process;
    private string? session;
    private long lastSequence;
    private (LazerKeyWireSample Sample, DateTimeOffset ReceivedAtUtc)? latest;

    public LazerKeyRunnerSource(string runnerPath, Action<string>? log = null, Func<DateTimeOffset>? clock = null)
    {
        this.runnerPath = runnerPath;
        this.log = log;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Échantillon valide reçu (thread de lecture du processus).</summary>
    public event Action<LazerKeyWireSample, DateTimeOffset>? SampleReceived;

    public long Lines { get; private set; }
    public long Rejected { get; private set; }
    public long OutOfOrder { get; private set; }
    public int Starts { get; private set; }
    public string? LastRejection { get; private set; }

    public (LazerKeyWireSample Sample, DateTimeOffset ReceivedAtUtc)? Latest
    {
        get { lock (gate) return latest; }
    }

    /// <summary>Chemin du lecteur : variable d'environnement, sinon <c>LazerKeys\LazerKeyOverlayRunner.exe</c> à côté de BeatInsight.</summary>
    public static string? ResolveRunnerPath(string? variable, string baseDirectory)
    {
        string candidate = !string.IsNullOrWhiteSpace(variable)
            ? Path.GetFullPath(variable)
            : Path.Combine(baseDirectory, "LazerKeys", "LazerKeyOverlayRunner.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    public void Start()
    {
        if (thread is not null) return;
        thread = new Thread(Run) { IsBackground = true, Name = "BeatInsight Lazer keys runner" };
        thread.Start();
    }

    private void Run()
    {
        while (!stop.IsCancellationRequested)
        {
            Process? started = null;
            try
            {
                var info = new ProcessStartInfo(runnerPath, $"--parent {Environment.ProcessId}")
                {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(runnerPath)!,
                };
                started = Process.Start(info);
                if (started is null) throw new InvalidOperationException("processus non démarré");
                lock (gate) process = started;
                Starts++;
                log?.Invoke($"LAZER KEYS | lecteur démarré (pid {started.Id})");
                started.ErrorDataReceived += (_, e) => { if (e.Data is { Length: > 0 } line) log?.Invoke(line); };
                started.BeginErrorReadLine();

                string? line;
                while (!stop.IsCancellationRequested && (line = started.StandardOutput.ReadLine()) is not null)
                    Accept(line);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                log?.Invoke($"LAZER KEYS | lecteur indisponible : {ex.GetType().Name}");
            }
            finally
            {
                lock (gate)
                {
                    process = null;
                    latest = null; // plus de lecteur : aucune donnée n'est actuelle
                    session = null;
                    lastSequence = 0;
                }
                Close(started);
            }
            if (stop.Token.WaitHandle.WaitOne(RestartDelay)) break;
        }
    }

    /// <summary>Validation d'une ligne (testable sans processus).</summary>
    internal bool Accept(string line)
    {
        Lines++;
        DateTimeOffset now = clock();
        LazerKeyWireSample? sample = LazerKeyWire.TryParse(line, out string? rejection);
        if (sample is null)
        {
            Rejected++;
            LastRejection = rejection;
            return false;
        }
        lock (gate)
        {
            if (session is not null && sample.Session == session && sample.Sequence <= lastSequence)
            {
                OutOfOrder++;
                return false;
            }
            session = sample.Session;
            lastSequence = sample.Sequence;
            latest = (sample, now);
        }
        SampleReceived?.Invoke(sample, now);
        return true;
    }

    private static void Close(Process? p)
    {
        if (p is null) return;
        try
        {
            if (!p.HasExited)
            {
                try { p.StandardInput.Close(); } catch (IOException) { }
                if (!p.WaitForExit(2000)) p.Kill();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        finally
        {
            p.Dispose();
        }
    }

    public void Dispose()
    {
        if (stop.IsCancellationRequested) return;
        stop.Cancel();
        Process? running;
        lock (gate) running = process;
        // Fermer l'entrée standard arrête le lecteur ; la lecture de sa sortie se termine alors d'elle-même.
        try { running?.StandardInput.Close(); } catch (Exception ex) when (ex is IOException or InvalidOperationException) { }
        thread?.Join(TimeSpan.FromSeconds(5));
        stop.Dispose();
    }
}
