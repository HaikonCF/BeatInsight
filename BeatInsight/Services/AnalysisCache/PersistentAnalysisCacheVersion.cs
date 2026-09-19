namespace BeatInsight.Services.AnalysisCache;

/// <summary>
/// Explicit compatibility contract for the complete JSON analysis snapshot.
/// This is independent from application/UI versions and the historical SQLite schema.
/// </summary>
public static class PersistentAnalysisCacheVersion
{
    public const int SchemaVersion = 1;
    public const string AnalyzerVersion = "analysis-v1.0.0";
}
