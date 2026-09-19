using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using BeatInsight.Diagnostics;
using BeatInsight.Models.AnalysisCache;

namespace BeatInsight.Services.AnalysisCache;

/// <summary>
/// Single future-UI entry point. Callers receive the same snapshot contract on
/// disk hits and fresh analyses and do not need cache-specific branches.
/// </summary>
public sealed class CachedBeatmapAnalysisService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> AnalysisLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly IBeatmapAnalysisCache cache;
    private readonly Func<string, CancellationToken, Task<BeatmapAnalysisSnapshot>> analyzer;

    public int AnalyzerInvocationCount { get; private set; }

    public CachedBeatmapAnalysisService(
        IBeatmapAnalysisCache cache,
        Func<string, CancellationToken, Task<BeatmapAnalysisSnapshot>>? analyzer = null)
    {
        this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        this.analyzer = analyzer ?? BeatmapAnalysisSnapshotFactory.AnalyzeAsync;
    }

    public async Task<CachedBeatmapAnalysisResult> AnalyzeAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        AnalysisCacheLookupResult initial = await cache.TryLoadAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        if (initial.Result == AnalysisCacheResultKind.Hit)
            return Hit(initial);

        string lockKey = initial.CacheKey.Length == 0 ? Path.GetFullPath(sourcePath) : initial.CacheKey;
        SemaphoreSlim analysisLock = AnalysisLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await analysisLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AnalysisCacheLookupResult lookup = initial;
            if (cache.Enabled)
            {
                lookup = await cache.TryLoadAsync(sourcePath, cancellationToken).ConfigureAwait(false);
                if (lookup.Result == AnalysisCacheResultKind.Hit)
                    return Hit(lookup);
            }

            Stopwatch analysisWatch = Stopwatch.StartNew();
            AnalyzerInvocationCount++;
            BeatmapAnalysisSnapshot snapshot = await analyzer(sourcePath, cancellationToken).ConfigureAwait(false);
            analysisWatch.Stop();

            Stopwatch saveWatch = Stopwatch.StartNew();
            long size = 0;
            string contentHash = lookup.ContentHash;
            string cacheKey = lookup.CacheKey;
            string cachePath = lookup.CachePath;
            if (cache.Enabled)
            {
                FileInfo source = new(sourcePath);
                DateTime now = DateTime.UtcNow;
                var entry = new BeatmapAnalysisCacheEntry(
                    cache.SchemaVersion,
                    cache.AnalyzerVersion,
                    now,
                    now,
                    new BeatmapAnalysisSourceSnapshot(
                        source.FullName,
                        source.Name,
                        source.Length,
                        source.LastWriteTimeUtc,
                        contentHash),
                    snapshot);
                size = await cache.SaveAsync(entry, cancellationToken).ConfigureAwait(false);
            }
            saveWatch.Stop();

            var result = new CachedBeatmapAnalysisResult(
                snapshot, false, initial.Reason, cacheKey, contentHash, cachePath,
                initial.LoadTime, analysisWatch.Elapsed, saveWatch.Elapsed, size);
            AnalysisCacheDebug.Log(result, cache.AnalyzerVersion, cache.SchemaVersion);
            return result;
        }
        finally
        {
            analysisLock.Release();
        }
    }

    private CachedBeatmapAnalysisResult Hit(AnalysisCacheLookupResult lookup)
    {
        var result = new CachedBeatmapAnalysisResult(
            lookup.Entry!.Analysis, true, AnalysisCacheMissReason.None,
            lookup.CacheKey, lookup.ContentHash, lookup.CachePath,
            lookup.LoadTime, TimeSpan.Zero, TimeSpan.Zero, lookup.EntrySizeBytes);
        AnalysisCacheDebug.Log(result, cache.AnalyzerVersion, cache.SchemaVersion);
        return result;
    }
}
