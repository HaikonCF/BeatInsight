using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using BeatInsight.Models.AnalysisCache;

namespace BeatInsight.Services.AnalysisCache;

/// <summary>
/// Content-addressed, one-JSON-file-per-beatmap persistent cache.
/// Individual entries are authoritative; no global index is required.
/// </summary>
public sealed class JsonBeatmapAnalysisCache : IBeatmapAnalysisCache
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EntryLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static string DefaultCacheDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BeatInsight", "Cache", "Beatmaps");

    public bool Enabled { get; }
    public string CacheDirectory { get; }
    public string AnalyzerVersion { get; }
    public int SchemaVersion { get; }

    public JsonBeatmapAnalysisCache(
        string? cacheDirectory = null,
        bool enabled = true,
        string analyzerVersion = PersistentAnalysisCacheVersion.AnalyzerVersion,
        int schemaVersion = PersistentAnalysisCacheVersion.SchemaVersion)
    {
        CacheDirectory = Path.GetFullPath(cacheDirectory ?? DefaultCacheDirectory);
        Enabled = enabled;
        AnalyzerVersion = string.IsNullOrWhiteSpace(analyzerVersion)
            ? throw new ArgumentException("Analyzer version is required.", nameof(analyzerVersion))
            : analyzerVersion;
        SchemaVersion = schemaVersion > 0
            ? schemaVersion
            : throw new ArgumentOutOfRangeException(nameof(schemaVersion));
    }

    public async Task<AnalysisCacheLookupResult> TryLoadAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        if (!Enabled)
            return Result(AnalysisCacheResultKind.Miss, AnalysisCacheMissReason.Disabled, "", "", "", null, stopwatch.Elapsed, 0);

        string contentHash = await ComputeContentHashAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        string cacheKey = contentHash;
        string cachePath = GetEntryPath(cacheKey);
        if (!File.Exists(cachePath))
            return Result(AnalysisCacheResultKind.Miss, AnalysisCacheMissReason.NotFound, cacheKey, contentHash, cachePath, null, stopwatch.Elapsed, 0);

        try
        {
            await using FileStream stream = new(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            BeatmapAnalysisCacheEntry? entry = await JsonSerializer.DeserializeAsync<BeatmapAnalysisCacheEntry>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            long size = stream.Length;
            if (entry is null)
                return Result(AnalysisCacheResultKind.Invalid, AnalysisCacheMissReason.MissingRequiredData, cacheKey, contentHash, cachePath, null, stopwatch.Elapsed, size);
            if (entry.SchemaVersion != SchemaVersion)
                return Result(AnalysisCacheResultKind.Invalid, AnalysisCacheMissReason.SchemaVersionMismatch, cacheKey, contentHash, cachePath, null, stopwatch.Elapsed, size);
            if (!string.Equals(entry.AnalyzerVersion, AnalyzerVersion, StringComparison.Ordinal))
                return Result(AnalysisCacheResultKind.Invalid, AnalysisCacheMissReason.AnalyzerVersionMismatch, cacheKey, contentHash, cachePath, null, stopwatch.Elapsed, size);
            if (!HasRequiredData(entry))
                return Result(AnalysisCacheResultKind.Invalid, AnalysisCacheMissReason.MissingRequiredData, cacheKey, contentHash, cachePath, null, stopwatch.Elapsed, size);
            if (!string.Equals(entry.Source.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase))
                return Result(AnalysisCacheResultKind.Invalid, AnalysisCacheMissReason.HashMismatch, cacheKey, contentHash, cachePath, null, stopwatch.Elapsed, size);
            return Result(AnalysisCacheResultKind.Hit, AnalysisCacheMissReason.None, cacheKey, contentHash, cachePath, entry, stopwatch.Elapsed, size);
        }
        catch (JsonException)
        {
            return Result(AnalysisCacheResultKind.Invalid, AnalysisCacheMissReason.Corrupted, cacheKey, contentHash, cachePath, null, stopwatch.Elapsed, SafeLength(cachePath));
        }
        catch (NotSupportedException)
        {
            return Result(AnalysisCacheResultKind.Invalid, AnalysisCacheMissReason.Corrupted, cacheKey, contentHash, cachePath, null, stopwatch.Elapsed, SafeLength(cachePath));
        }
        catch (IOException)
        {
            return Result(AnalysisCacheResultKind.Invalid, AnalysisCacheMissReason.Corrupted, cacheKey, contentHash, cachePath, null, stopwatch.Elapsed, SafeLength(cachePath));
        }
        catch (UnauthorizedAccessException)
        {
            return Result(AnalysisCacheResultKind.Invalid, AnalysisCacheMissReason.Corrupted, cacheKey, contentHash, cachePath, null, stopwatch.Elapsed, SafeLength(cachePath));
        }
    }

    public async Task<long> SaveAsync(BeatmapAnalysisCacheEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!Enabled) return 0;
        if (!HasRequiredData(entry)) throw new ArgumentException("Cache entry is incomplete.", nameof(entry));
        if (!IsSafeCacheKey(entry.Source.ContentHash)) throw new ArgumentException("Content hash is not a SHA-256 cache key.", nameof(entry));

        Directory.CreateDirectory(CacheDirectory);
        string finalPath = GetEntryPath(entry.Source.ContentHash);
        SemaphoreSlim entryLock = EntryLocks.GetOrAdd(finalPath, _ => new SemaphoreSlim(1, 1));
        await entryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        string tempPath = finalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (FileStream stream = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, entry, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(tempPath, finalPath, overwrite: true);
            return new FileInfo(finalPath).Length;
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            entryLock.Release();
        }
    }

    public Task InvalidateAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enabled || !IsSafeCacheKey(cacheKey)) return Task.CompletedTask;
        string path = GetEntryPath(cacheKey);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enabled || !Directory.Exists(CacheDirectory)) return Task.CompletedTask;
        foreach (string path in Directory.EnumerateFiles(CacheDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(path);
        }
        foreach (string path in Directory.EnumerateFiles(CacheDirectory, "*.tmp", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(path);
        }
        return Task.CompletedTask;
    }

    public Task<AnalysisCacheStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(CacheDirectory)) return Task.FromResult(new AnalysisCacheStats(0, 0, null, null));
        FileInfo[] files = Directory.EnumerateFiles(CacheDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path)).ToArray();
        return Task.FromResult(new AnalysisCacheStats(
            files.Length,
            files.Sum(file => file.Length),
            files.Length == 0 ? null : files.Min(file => file.CreationTimeUtc),
            files.Length == 0 ? null : files.Max(file => file.LastWriteTimeUtc)));
    }

    public string GetEntryPath(string cacheKey)
    {
        if (!IsSafeCacheKey(cacheKey)) throw new ArgumentException("Invalid cache key.", nameof(cacheKey));
        return Path.Combine(CacheDirectory, cacheKey.ToLowerInvariant() + ".json");
    }

    public static async Task<string> ComputeContentHashAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        await using FileStream stream = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool IsSafeCacheKey(string value) => value.Length == 64 && value.All(character =>
        character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    internal static JsonSerializerOptions SerializationOptions => JsonOptions;

    private static bool HasRequiredData(BeatmapAnalysisCacheEntry entry) =>
        entry.Source is not null && entry.Analysis is not null &&
        entry.Analysis.Metadata is not null && entry.Analysis.GameplayProfile is not null &&
        entry.Analysis.IdentityV3 is not null && entry.Analysis.SkillPressureV2 is not null &&
        entry.Analysis.SectionAnalysisV2 is not null && entry.Analysis.RatingV2 is not null &&
        entry.Analysis.GlobalTripletSpeed is not null && entry.Analysis.LocalSpeedPressure is not null &&
        entry.Analysis.PatternSummaries is not null &&
        !string.IsNullOrWhiteSpace(entry.Source.ContentHash) && !string.IsNullOrWhiteSpace(entry.AnalyzerVersion);

    private static long SafeLength(string path) { try { return new FileInfo(path).Length; } catch { return 0; } }
    private static AnalysisCacheLookupResult Result(AnalysisCacheResultKind result, AnalysisCacheMissReason reason,
        string key, string hash, string path, BeatmapAnalysisCacheEntry? entry, TimeSpan load, long size) =>
        new(result, reason, key, hash, path, entry, load, size);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
