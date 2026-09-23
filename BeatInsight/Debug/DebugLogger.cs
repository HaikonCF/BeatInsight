using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace BeatInsight.Diagnostics;

public static class DebugLogger
{
    // ============================================================
    // GLOBAL DEBUG SWITCHES
    // ============================================================

    /// <summary>
    /// Interrupteur principal du système de debug.
    /// False = aucun log BeatInsight.
    /// </summary>
    public static bool DebugMode = true;

    /// <summary>
    /// Active les traces internes très détaillées.
    /// Exemples :
    /// - transitions Tech
    /// - contexte sliders
    /// - calculs intermédiaires
    /// </summary>
    // Keep high-level performance/lifecycle logging enabled through DebugMode,
    // but do not synchronously emit analyzer internals for every local section.
    public static bool DetailedDebug = false;

    private static readonly object Lock = new();
    
    // ============================================================
    // DEBUG SWITCHES
    // ============================================================
    
    public static bool IdentityEnabled = false;
    public static bool TechEnabled = true;
    public static bool TechV2ExperimentalEnabled = false;
    public static bool TechV2RegionExperimentalEnabled = false;
    public static bool ReadEnabled = true;
    public static bool SpeedEnabled = false;
    public static bool AimEnabled = false;
    public static bool SummaryEnabled = false;

    private static readonly string LogDirectory =
        Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Logs");

    private static readonly string LogFile =
        Path.Combine(
            LogDirectory,
            "beatinsight-debug.log");

    // ============================================================
    // STANDARD LOG
    // ============================================================

    public static void Log(string message)
    {
        if (!DebugMode)
            return;

        string line =
            $"[{DateTime.Now:HH:mm:ss.fff}] {message}";

        // Visual Studio Output
        Debug.WriteLine(line);

        // Fichier de log
        lock (Lock)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);

                File.AppendAllText(
                    LogFile,
                    line + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch (Exception ex)
            {
                // Le système de debug ne doit jamais
                // faire planter BeatInsight.
                Debug.WriteLine(
                    $"[DebugLogger] File logging failed: {ex.Message}");
            }
        }
    }

    // ============================================================
    // DETAILED LOG
    // ============================================================

    public static void Detailed(string message)
    {
        if (!DebugMode || !DetailedDebug)
            return;

        Log(message);
    }

    // ============================================================
    // FORMATTING HELPERS
    // ============================================================

    public static void Section(string title)
    {
        if (!DebugMode)
            return;

        Log("");
        Log($"===== {title} =====");
    }

    public static void Separator()
    {
        if (!DebugMode)
            return;

        Log(
            "------------------------------------------------------------");
    }

    // ============================================================
    // MAP
    // ============================================================

    public static void NewMap(
        string map,
        string difficulty)
    {
        if (!DebugMode)
            return;

        Log("");
        Log("============================================================");
        Log("NEW MAP");
        Log($"MAP = {map}");
        Log($"DIFFICULTY = {difficulty}");
        Log("============================================================");
    }
}
