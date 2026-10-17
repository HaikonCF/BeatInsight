using System.Diagnostics;
using System.Runtime.InteropServices;
using BeatInsight.Diagnostics.NativeLazer;
using BeatInsight.Integration.KeyOverlay.Lazer;

namespace LazerKeyOverlayRunner;

/// <summary>
/// V3.14-COM-LAZER-KEY-03 — processus lecteur KeyOverlay osu!lazer.
/// Boucle à 100 Hz sur <see cref="NativeLazerKeyOverlayReader"/> (racine partagée, aucune découverte complète) ;
/// une ligne JSON <see cref="LazerKeyWireSample"/> sur stdout à chaque changement, plus un battement toutes les 250 ms.
/// S'arrête quand stdin est fermé (BeatInsight arrêté ou option désactivée) ou quand le processus parent disparaît.
/// Les journaux vont sur stderr. Aucune écriture mémoire, aucun hook.
/// </summary>
internal static class Program
{
    private static readonly TimeSpan Heartbeat = TimeSpan.FromMilliseconds(250);

    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint period);

    private static int Main(string[] args)
    {
        int? parent = args.Length >= 2 && args[0] == "--parent" && int.TryParse(args[1], out int p) ? p : null;
        bool standalone = args.Contains("--standalone-discovery");
        using var stopped = new ManualResetEventSlim(false);

        // stdin fermé = arrêt demandé.
        var stdinWatcher = new Thread(() =>
        {
            try { while (Console.In.Read() >= 0) { } } catch (IOException) { }
            stopped.Set();
        }) { IsBackground = true };
        stdinWatcher.Start();

        TextWriter output = Console.Out;
        string session = Guid.NewGuid().ToString("N");
        long sequence = 0;
        string? lastKey = null;
        long lastWrite = 0;
        long heartbeatTicks = (long)(Heartbeat.TotalSeconds * Stopwatch.Frequency);
        long period = Stopwatch.Frequency / 100;
        long ticks = 0, late = 0;

        using var reader = new NativeLazerKeyOverlayReader(log: m => Console.Error.WriteLine(m), standaloneDiscovery: standalone);
        Console.Error.WriteLine($"LAZER KEYS RUNNER | démarré (parent {parent?.ToString() ?? "-"}, session {session})");
        timeBeginPeriod(1);
        try
        {
            long next = Stopwatch.GetTimestamp();
            while (!stopped.IsSet)
            {
                long now = Stopwatch.GetTimestamp();
                if (now < next)
                {
                    stopped.Wait(1);
                    continue;
                }
                if (now - next > period) late++;
                next += period;
                if (now - next > 10 * period) next = now + period;
                ticks++;

                if (parent is { } pid && ticks % 100 == 0 && !Alive(pid)) break;

                LazerKeyOverlaySample s = reader.Read();
                LazerKeyWireSample wire = ToWire(s, session, 0);
                string key = $"{wire.Validity}|{wire.Failure}|{wire.ProcessIdentity}|{wire.PlayIdentity}|{wire.Transition}|{wire.RestartCount}|" +
                             $"{wire.LeftCount}|{wire.LeftActive}|{wire.RightCount}|{wire.RightActive}|{wire.Counting}|" +
                             $"{wire.K1Pressed}|{wire.K2Pressed}|{wire.M1Pressed}|{wire.M2Pressed}";
                if (key == lastKey && now - lastWrite < heartbeatTicks) continue;
                try
                {
                    output.WriteLine(LazerKeyWire.Serialize(wire with { Sequence = ++sequence }));
                    output.Flush();
                }
                catch (IOException)
                {
                    break; // BeatInsight a fermé le tube
                }
                lastKey = key;
                lastWrite = now;
            }
        }
        finally
        {
            timeEndPeriod(1);
            Console.Error.WriteLine($"LAZER KEYS RUNNER | arrêté : {ticks} lectures, {late} en retard, {sequence} lignes, {reader.Attachments} attachement(s)");
        }
        return 0;
    }

    private static bool Alive(int pid)
    {
        try
        {
            using Process p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    internal static LazerKeyWireSample ToWire(LazerKeyOverlaySample s, string session, long sequence)
    {
        bool valid = s.IsValid;
        return new LazerKeyWireSample(
            LazerKeyWire.Version,
            session,
            Math.Max(1, sequence),
            s.ObservedAtUtc,
            s.Validity.ToString(),
            s.FailureReason.ToString(),
            s.ProcessIdentity,
            valid ? s.PlayIdentity : null,
            valid ? s.Transition.ToString() : "None",
            s.RestartCount,
            valid ? s.LeftAction.Count : null,
            valid ? s.LeftAction.IsActive : null,
            valid ? s.RightAction.Count : null,
            valid ? s.RightAction.IsActive : null,
            valid ? s.LeftAction.IsCounting : null,
            valid ? s.Pressed?.K1 : null,
            valid ? s.Pressed?.K2 : null,
            valid ? s.Pressed?.M1 : null,
            valid ? s.Pressed?.M2 : null,
            Math.Round(s.ReadMs, 3),
            s.Reads);
    }
}
