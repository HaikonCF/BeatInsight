namespace BeatInsight.Services.AnalysisCache;

/// <summary>
/// Explicit compatibility contract for the complete JSON analysis snapshot.
/// This is independent from application/UI versions and the historical SQLite schema.
/// </summary>
public static class PersistentAnalysisCacheVersion
{
    public const int SchemaVersion = 2;
    public const string AnalyzerVersion = "analysis-v2.0.0";
}
