using BeatInsight.Models.AnalysisCache;

namespace BeatInsight.Services.AnalysisCache;

public interface IBeatmapAnalysisCache
{
    bool Enabled { get; }
    string CacheDirectory { get; }
    string AnalyzerVersion { get; }
    int SchemaVersion { get; }

    Task<AnalysisCacheLookupResult> TryLoadAsync(string sourcePath, CancellationToken cancellationToken = default);
    Task<long> SaveAsync(BeatmapAnalysisCacheEntry entry, CancellationToken cancellationToken = default);
    Task InvalidateAsync(string cacheKey, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
    Task<AnalysisCacheStats> GetStatsAsync(CancellationToken cancellationToken = default);
}
