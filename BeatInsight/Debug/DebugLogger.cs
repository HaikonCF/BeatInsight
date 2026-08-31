using BeatInsight.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace BeatInsight.Diagnostics;

public static class DebugLogger
{
    public static bool DebugMode = true;
    public static bool DetailedDebug = false;
    private static readonly object Lock = new();

    private static readonly string LogDirectory =
        Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Logs");

    private static readonly string LogFile =
        Path.Combine(
            LogDirectory,
            "beatinsight-debug.log");
    

    // ============================================================
    // GENERAL
    // ============================================================

    public static void Log(string message)
    {

        if (!DebugMode)
        {
            return;
        }

        

        lock (Lock)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);

                string line =
                    $"[{DateTime.Now:HH:mm:ss.fff}] {message}";

                File.AppendAllText(
                    LogFile,
                    line + Environment.NewLine,
                    Encoding.UTF8);

                Debug.WriteLine(line);
            }
            catch
            {
                // Le système de debug ne doit jamais faire planter
                // l'analyse de la beatmap.
            }
        }
    }
    public static void Detailed(string message)
    {
        if (!DetailedDebug)
        {
            return;
        }

        Log(message);
    }

    public static void Section(string title)
    {
        Log("");
        Log($"===== {title} =====");
    }

    public static void Separator()
    {
        Log("------------------------------------------------------------");
    }

    // ============================================================
    // MAP
    // ============================================================

    public static void NewMap(
        string map,
        string difficulty)
    {
        Log("");
        Log("============================================================");
        Log($"NEW MAP");
        Log($"MAP = {map}");
        Log($"DIFFICULTY = {difficulty}");
        Log("============================================================");
    }

    // ============================================================
    // GAMEPLAY IDENTITY
    // ============================================================

    public static void GameplayIdentity(
        GameplayIdentity identity,
        GameplayProfile profile)
    {
        Section("TAG / GAMEPLAY IDENTITY");

        Log($"GAMEPLAY IDENTITY = {identity.FullName}");
        Log($"PRIMARY = {identity.Primary}");
        Log($"SECONDARY = {identity.Secondary}");
        Log($"PATTERN = {identity.Pattern}");
        Log($"IDENTITY CONFIDENCE = {identity.Confidence:F1}%");

        Log(
            $"STRUCTURAL PRESENCE | " +
            $"Stream={profile.StreamRatio:P2} | " +
            $"Jump={profile.JumpRatio:P2} | " +
            $"Tech={profile.TechRatio:P2}");

        Log(
            $"TECH SIGNAL | " +
            $"Score={profile.TechScore:F1}/100");

        Log(
            $"TRAITS = {(identity.Traits.Count > 0
                ? string.Join(" | ", identity.Traits)
                : "None")}");

        Log(
            $"CONCEPTS = {(identity.Concepts.Count > 0
                ? string.Join(" | ", identity.Concepts)
                : "None")}");
    }

    // ============================================================
    // IDENTITY CALCULATION
    // ============================================================

    public static void IdentityScores(
        double streamScore,
        double jumpScore,
        double techScore)
    {
        Section("IDENTITY SCORES");

        Log($"Stream = {streamScore:F2}");
        Log($"Jump   = {jumpScore:F2}");
        Log($"Tech   = {techScore:F2}");

        Log(
            $"WINNER = {GetHighestIdentity(
                streamScore,
                jumpScore,
                techScore)}");
    }

    private static string GetHighestIdentity(
        double stream,
        double jump,
        double tech)
    {
        if (stream >= jump && stream >= tech)
            return "Stream";

        if (jump >= stream && jump >= tech)
            return "Jump";

        return "Tech";
    }

    // ============================================================
    // TECH IDENTITY CALCULATION
    // ============================================================

    public static void TechIdentityCalculation(
        double streamCoverage,
        double jumpCoverage,
        double techCoverage,
        double techScore,
        double coverageComponent,
        double scoreComponent,
        double dominanceComponent,
        double identityScore)
    {
        Section("TECH IDENTITY CALCULATION");

        Log(
            $"Coverage       = {techCoverage:P2}");

        Log(
            $"Tech Score     = {techScore:F2}");

        Log(
            $"Stream Coverage = {streamCoverage:P2}");

        Log(
            $"Jump Coverage   = {jumpCoverage:P2}");

        Log(
            $"Coverage Component  = {coverageComponent:F4}");

        Log(
            $"Score Component     = {scoreComponent:F4}");

        Log(
            $"Dominance Component = {dominanceComponent:F4}");

        Log(
            $"FINAL TECH IDENTITY = {identityScore:F2}");
    }

    // ============================================================
    // GAMEPLAY PROFILE
    // ============================================================

    public static void GameplayProfile(
        GameplayProfile profile)
    {
        Section("GAMEPLAY PROFILE");

        Log(
            $"ANALYSED CIRCLES = " +
            $"{profile.AnalysedCircleCount}");

        Log(
            $"STREAMS = " +
            $"{profile.StreamSequenceCount} sequences / " +
            $"{profile.StreamObjectCount} circles / " +
            $"{profile.StreamRatio:P2}");

        Log(
            $"JUMPS = " +
            $"{profile.JumpSequenceCount} sequences / " +
            $"{profile.JumpObjectCount} circles / " +
            $"{profile.JumpRatio:P2}");

        Log(
            $"BURSTS = " +
            $"{profile.BurstSequenceCount} sequences / " +
            $"{profile.BurstObjectCount} circles / " +
            $"{profile.BurstRatio:P2} / " +
            $"Max {profile.LongestBurstLength}");

        Log(
            $"TECH = " +
            $"{profile.TechObjectCount} circles / " +
            $"{profile.TechRatio:P2} / " +
            $"Signal {profile.TechScore:F0}/100 " +
            $"({profile.TechLevel})");

        Log(
            $"READ = " +
            $"{profile.ReadObjectCount} circles / " +
            $"{profile.ReadRatio:P2} / " +
            $"Score {profile.ReadScore:F0}/100 " +
            $"({profile.ReadLevel})");

        Log(
            $"AIM = " +
            $"Score {profile.AimScore:F0}/100");

        Log(
            $"SPEED = " +
            $"Score {profile.SpeedScore:F0}/100");

        Log(
            $"PRIMARY TYPE = " +
            $"{profile.PrimaryType}");
    }
}