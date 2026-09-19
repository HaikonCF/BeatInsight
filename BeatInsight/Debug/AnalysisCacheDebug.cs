using BeatInsight.Models.AnalysisCache;

namespace BeatInsight.Diagnostics;

public static class AnalysisCacheDebug
{
    public static bool Enabled = false;

    public static void Log(CachedBeatmapAnalysisResult result, string analyzerVersion, int schemaVersion)
    {
        if (!Enabled) return;
        DebugLogger.Log("===== ANALYSIS CACHE DEBUG =====");
        DebugLogger.Log($"Cache Key = {result.CacheKey}");
        DebugLogger.Log($"Source Hash = {result.ContentHash}");
        DebugLogger.Log($"Analyzer Version = {analyzerVersion}");
        DebugLogger.Log($"Schema Version = {schemaVersion}");
        DebugLogger.Log($"Result = {(result.WasCacheHit ? "HIT" : result.InitialMissReason == AnalysisCacheMissReason.Disabled ? "MISS" : "MISS / INVALID")}");
        DebugLogger.Log($"Reason = {result.InitialMissReason}");
        DebugLogger.Log($"Cache Path = {result.CachePath}");
        DebugLogger.Log($"Load Time = {result.LoadTime.TotalMilliseconds:F2} ms");
        DebugLogger.Log($"Analysis Time = {result.AnalysisTime.TotalMilliseconds:F2} ms");
        DebugLogger.Log($"Save Time = {result.SaveTime.TotalMilliseconds:F2} ms");
        DebugLogger.Log($"Entry Size = {result.EntrySizeBytes} bytes");
    }
}
