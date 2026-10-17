using System.Diagnostics;
using System.IO;

namespace BeatInsight.Services.CommunityOverlays;

/// <summary>Processus compagnon géré (abstrait pour les tests du gestionnaire).</summary>
public interface ICommunityOverlayCompanionProcess : IDisposable
{
    int ProcessId { get; }

    /// <summary>Ligne écrite par le compagnon sur sa sortie standard (thread quelconque).</summary>
    event Action<string>? LineReceived;

    /// <summary>Fin du processus avec son code de sortie (thread quelconque, une seule fois).</summary>
    event Action<int>? Exited;

    bool TrySend(string line);

    void CloseInput();

    bool WaitForExit(TimeSpan timeout);

    void Kill();
}

public interface ICommunityOverlayCompanionLauncher
{
    /// <summary>Exécutable du compagnon, ou null s'il est absent (BeatInsight fonctionne alors sans overlay communautaire).</summary>
    string? ExecutablePath { get; }

    ICommunityOverlayCompanionProcess Start(IReadOnlyList<string> arguments);
}

/// <summary>
/// Lancement réel : <c>CommunityOverlays\BeatInsight.CommunityOverlays.Poc.exe</c> à côté de BeatInsight,
/// ou le chemin de <c>BEATINSIGHT_COMMUNITY_OVERLAYS_COMPANION</c> (diagnostic).
/// </summary>
public sealed class CommunityOverlayCompanionLauncher : ICommunityOverlayCompanionLauncher
{
    public const string CompanionVariable = "BEATINSIGHT_COMMUNITY_OVERLAYS_COMPANION";
    public const string CompanionFolder = "CommunityOverlays";
    public const string CompanionExecutable = "BeatInsight.CommunityOverlays.Poc.exe";

    private readonly Action<string>? log;

    public CommunityOverlayCompanionLauncher(string? executablePath, Action<string>? log = null)
    {
        ExecutablePath = executablePath;
        this.log = log;
    }

    public string? ExecutablePath { get; }

    public static string? ResolveExecutablePath(string? variable, string baseDirectory)
    {
        string candidate = !string.IsNullOrWhiteSpace(variable)
            ? Path.GetFullPath(variable)
            : Path.Combine(baseDirectory, CompanionFolder, CompanionExecutable);
        return File.Exists(candidate) ? candidate : null;
    }

    public ICommunityOverlayCompanionProcess Start(IReadOnlyList<string> arguments)
    {
        string executable = ExecutablePath ?? throw new FileNotFoundException("Compagnon CommunityOverlays introuvable.");
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
        };
        foreach (string argument in arguments)
            info.ArgumentList.Add(argument);

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var wrapper = new CompanionProcess(process, log);
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("Processus compagnon non démarré.");
        }
        wrapper.BeginReading();
        return wrapper;
    }

    private sealed class CompanionProcess : ICommunityOverlayCompanionProcess
    {
        private readonly Process process;
        private readonly Action<string>? log;
        private int exitedRaised;

        public CompanionProcess(Process process, Action<string>? log)
        {
            this.process = process;
            this.log = log;
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is { Length: > 0 } line) LineReceived?.Invoke(line);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is { Length: > 0 } line) this.log?.Invoke($"COMMUNITY OVERLAY COMPANION | stderr : {line}");
            };
            process.Exited += (_, _) => RaiseExited();
        }

        public int ProcessId { get; private set; }

        public event Action<string>? LineReceived;
        public event Action<int>? Exited;

        public void BeginReading()
        {
            ProcessId = process.Id;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (process.HasExited) RaiseExited();
        }

        public bool TrySend(string line)
        {
            try
            {
                if (process.HasExited) return false;
                process.StandardInput.WriteLine(line);
                process.StandardInput.Flush();
                return true;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
            {
                return false;
            }
        }

        public void CloseInput()
        {
            try
            {
                process.StandardInput.Close();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
            {
            }
        }

        public bool WaitForExit(TimeSpan timeout)
        {
            try
            {
                return process.WaitForExit(timeout);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return true;
            }
        }

        public void Kill()
        {
            try
            {
                // Pas d'arbre : le navigateur WebView2 peut être partagé avec d'autres compagnons (même dossier de données).
                if (!process.HasExited) process.Kill(entireProcessTree: false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
            }
        }

        public void Dispose() => process.Dispose();

        private void RaiseExited()
        {
            if (Interlocked.Exchange(ref exitedRaised, 1) != 0) return;
            int code;
            try
            {
                // Laisse la sortie standard se vider avant de signaler la fin.
                process.WaitForExit();
                code = process.ExitCode;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                code = -1;
            }
            Exited?.Invoke(code);
        }
    }
}
