using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO;
using BeatInsight.Models.AnalysisCache;
using BeatInsight.Models.Fusion;
using BeatInsight.Models.Identity;
using BeatInsight.Models.Rating;
using BeatInsight.Models.Sections;
using BeatInsight.Models.SkillPressure;
using BeatInsight.Models.Speed;
using BeatInsight.Services.AnalysisCache;
using BeatInsight.Diagnostics;

namespace BeatInsight.Tests.Services;

public sealed class PersistentAnalysisCacheTests
{
    [Fact]
    public async Task SaveCreatesDirectoryAndPhysicalJsonFile()
    {
        using var scope = new TempScope();
        string source = scope.Source("map.osu", "alpha");
        var cache = scope.Cache();
        AnalysisCacheLookupResult miss = await cache.TryLoadAsync(source);
        long size = await cache.SaveAsync(Entry(cache, source, miss.ContentHash, Snapshot("physical")));
        Assert.True(Directory.Exists(cache.CacheDirectory));
        Assert.True(File.Exists(cache.GetEntryPath(miss.CacheKey)));
        Assert.True(size > 0);
    }

    [Fact]
    public async Task SaveThenLoadReturnsEquivalentSnapshot()
    {
        using var scope = new TempScope();
        string source = scope.Source("map.osu", "same");
        var cache = scope.Cache();
        BeatmapAnalysisSnapshot snapshot = Snapshot("roundtrip");
        AnalysisCacheLookupResult miss = await cache.TryLoadAsync(source);
        await cache.SaveAsync(Entry(cache, source, miss.ContentHash, snapshot));
        AnalysisCacheLookupResult hit = await cache.TryLoadAsync(source);
        Assert.Equal(AnalysisCacheResultKind.Hit, hit.Result);
        AssertJsonEquivalent(snapshot, hit.Entry!.Analysis);
    }

    [Fact]
    public async Task NewBpmAndComboMetadataRoundTripsThroughJsonCache()
    {
        using var scope = new TempScope();
        string source = scope.Source("map.osu", "v2");
        var cache = scope.Cache(schemaVersion: PersistentAnalysisCacheVersion.SchemaVersion,
            analyzerVersion: PersistentAnalysisCacheVersion.AnalyzerVersion);
        BeatmapAnalysisSnapshot snapshot = Snapshot("v2") with
        {
            Metadata = Snapshot("v2").Metadata with
            { PrimaryBpm = 180, MinBpm = 160, MaxBpm = 190, IsVariableBpm = true, OsuMaxCombo = 1787 }
        };

        AnalysisCacheLookupResult miss = await cache.TryLoadAsync(source);
        await cache.SaveAsync(Entry(cache, source, miss.ContentHash, snapshot));
        BeatmapMetadataSnapshot restored = (await cache.TryLoadAsync(source)).Entry!.Analysis.Metadata;

        Assert.Equal(180, restored.PrimaryBpm);
        Assert.Equal(160, restored.MinBpm);
        Assert.Equal(190, restored.MaxBpm);
        Assert.True(restored.IsVariableBpm);
        Assert.Equal(1787, restored.OsuMaxCombo);
    }

    [Fact]
    public async Task LocalSpeedPressureRoundTripsThroughJsonCache()
    {
        using var scope = new TempScope();
        string source = scope.Source("map.osu", "local-speed-pressure");
        var cache = scope.Cache();
        var section = new LocalSpeedPressureSection(12, 15, 1000, 1210, 2, 90, 0.4, 36);
        BeatmapAnalysisSnapshot snapshot = Snapshot("local-speed-pressure") with
        {
            LocalSpeedPressure = new LocalSpeedPressureAnalysis(30.6, 2, 1, [section])
        };

        AnalysisCacheLookupResult miss = await cache.TryLoadAsync(source);
        await cache.SaveAsync(Entry(cache, source, miss.ContentHash, snapshot));
        BeatmapAnalysisSnapshot restored = (await cache.TryLoadAsync(source)).Entry!.Analysis;

        AssertJsonEquivalent(snapshot, restored);
        Assert.Equal(30.6, restored.LocalSpeedPressure.PressurePercent);
        Assert.Equal(section, Assert.Single(restored.LocalSpeedPressure.Sections));
    }

    [Fact]
    public void OldSnapshotJsonWithoutV2MetadataDeserializesWithUnavailableFields()
    {
        JsonNode oldSnapshot = JsonNode.Parse(JsonSerializer.Serialize(Snapshot("old")))!;
        JsonObject metadata = oldSnapshot["Metadata"]!.AsObject();
        metadata.Remove("PrimaryBpm");
        metadata.Remove("MinBpm");
        metadata.Remove("MaxBpm");
        metadata.Remove("IsVariableBpm");
        metadata.Remove("OsuMaxCombo");
        BeatmapAnalysisSnapshot? restored = oldSnapshot.Deserialize<BeatmapAnalysisSnapshot>();

        Assert.NotNull(restored);
        Assert.Null(restored.Metadata.PrimaryBpm);
        Assert.Null(restored.Metadata.MinBpm);
        Assert.Null(restored.Metadata.MaxBpm);
        Assert.Null(restored.Metadata.IsVariableBpm);
        Assert.Null(restored.Metadata.OsuMaxCombo);
    }

    [Fact]
    public async Task NewServiceInstanceLoadsPhysicalEntry()
    {
        using var scope = new TempScope();
        string source = scope.Source("map.osu", "restart");
        JsonBeatmapAnalysisCache first = scope.Cache();
        AnalysisCacheLookupResult miss = await first.TryLoadAsync(source);
        await first.SaveAsync(Entry(first, source, miss.ContentHash, Snapshot("restart")));
        JsonBeatmapAnalysisCache second = scope.Cache();
        Assert.Equal(AnalysisCacheResultKind.Hit, (await second.TryLoadAsync(source)).Result);
    }

    [Fact]
    public async Task ProcessLikeRestartDoesNotInvokeAnalyzerAgain()
    {
        using var scope = new TempScope();
        string source = scope.Source("map.osu", "restart-counter");
        int counter = 0;
        Task<BeatmapAnalysisSnapshot> Analyze(string _, CancellationToken __) { counter++; return Task.FromResult(Snapshot("counter")); }
        var first = new CachedBeatmapAnalysisService(scope.Cache(), Analyze);
        Assert.False((await first.AnalyzeAsync(source)).WasCacheHit);
        Assert.Equal(1, counter);
        var second = new CachedBeatmapAnalysisService(scope.Cache(), Analyze);
        Assert.True((await second.AnalyzeAsync(source)).WasCacheHit);
        Assert.Equal(1, counter);
        Assert.Equal(0, second.AnalyzerInvocationCount);
    }

    [Fact]
    public async Task SameContentHashIsHit()
    {
        using var scope = new TempScope();
        string source = scope.Source("map.osu", "hash");
        var service = new CachedBeatmapAnalysisService(scope.Cache(), Fake("hash"));
        _ = await service.AnalyzeAsync(source);
        Assert.True((await service.AnalyzeAsync(source)).WasCacheHit);
        Assert.Equal(1, service.AnalyzerInvocationCount);
    }

    [Fact]
    public async Task ModifiedSourceGetsNewHashAndReanalysis()
    {
        using var scope = new TempScope();
        string source = scope.Source("map.osu", "before");
        var service = new CachedBeatmapAnalysisService(scope.Cache(), Fake("modified"));
        CachedBeatmapAnalysisResult first = await service.AnalyzeAsync(source);
        File.WriteAllText(source, "after", Encoding.UTF8);
        CachedBeatmapAnalysisResult second = await service.AnalyzeAsync(source);
        Assert.False(second.WasCacheHit);
        Assert.NotEqual(first.ContentHash, second.ContentHash);
        Assert.Equal(2, service.AnalyzerInvocationCount);
        Assert.Equal(2, (await scope.Cache().GetStatsAsync()).EntryCount);
    }

    [Fact]
    public async Task AnalyzerVersionMismatchReanalyzesAndOverwrites()
    {
        using var scope = new TempScope();
        string source = scope.Source("map.osu", "version");
        var v1 = scope.Cache(analyzerVersion: "v1");
        await new CachedBeatmapAnalysisService(v1, Fake("v1")).AnalyzeAsync(source);
        var v2 = scope.Cache(analyzerVersion: "v2");
        var service = new CachedBeatmapAnalysisService(v2, Fake("v2"));
        CachedBeatmapAnalysisResult result = await service.AnalyzeAsync(source);
        Assert.False(result.WasCacheHit);
        Assert.Equal(AnalysisCacheMissReason.AnalyzerVersionMismatch, result.InitialMissReason);
        Assert.Equal(1, service.AnalyzerInvocationCount);
        Assert.Equal("v2", (await v2.TryLoadAsync(source)).Entry!.AnalyzerVersion);
    }

    [Fact]
    public async Task SchemaVersionMismatchIsInvalid()
    {
        using var scope = new TempScope();
        string source = scope.Source("map.osu", "schema");
        var v1 = scope.Cache(schemaVersion: 1);
        AnalysisCacheLookupResult miss = await v1.TryLoadAsync(source);
        await v1.SaveAsync(Entry(v1, source, miss.ContentHash, Snapshot("schema")));
        AnalysisCacheLookupResult invalid = await scope.Cache(schemaVersion: 2).TryLoadAsync(source);
        Assert.Equal(AnalysisCacheResultKind.Invalid, invalid.Result);
        Assert.Equal(AnalysisCacheMissReason.SchemaVersionMismatch, invalid.Reason);
    }

    [Fact]
    public async Task PreviousPatternSummarySchemaCannotBeLoadedAsCurrent()
    {
        using var scope = new TempScope();
        string source = scope.Source("map.osu", "old-pattern-summaries");
        var oldCache = scope.Cache(
            analyzerVersion: PersistentAnalysisCacheVersion.AnalyzerVersion,
            schemaVersion: PersistentAnalysisCacheVersion.SchemaVersion - 1);
        AnalysisCacheLookupResult miss = await oldCache.TryLoadAsync(source);
        await oldCache.SaveAsync(Entry(oldCache, source, miss.ContentHash, Snapshot("old")));

        var currentCache = scope.Cache(
            analyzerVersion: PersistentAnalysisCacheVersion.AnalyzerVersion,
            schemaVersion: PersistentAnalysisCacheVersion.SchemaVersion);
        AnalysisCacheLookupResult result = await currentCache.TryLoadAsync(source);

        Assert.Equal(AnalysisCacheResultKind.Invalid, result.Result);
        Assert.Equal(AnalysisCacheMissReason.SchemaVersionMismatch, result.Reason);
    }

    [Fact]
    public async Task HeaderHashMismatchIsInvalid()
    {
        using var scope = new TempScope();
        string source = scope.Source("map.osu", "hash-mismatch");
        var cache = scope.Cache();
        AnalysisCacheLookupResult miss = await cache.TryLoadAsync(source);
        await cache.SaveAsync(Entry(cache, source, miss.ContentHash, Snapshot("hash")));
        string path = cache.GetEntryPath(miss.CacheKey);
        string json = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, json.Replace(miss.ContentHash, new string('0', 64), StringComparison.Ordinal));
        AnalysisCacheLookupResult invalid = await cache.TryLoadAsync(source);
        Assert.Equal(AnalysisCacheResultKind.Invalid, invalid.Result);
        Assert.Equal(AnalysisCacheMissReason.HashMismatch, invalid.Reason);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"schemaVersion\":1,")]
    public async Task CorruptedOrTruncatedJsonIsInvalidNotCrash(string json)
    {
        using var scope = new TempScope();
        string source = scope.Source("map.osu", "corrupt");
        var cache = scope.Cache();
        AnalysisCacheLookupResult miss = await cache.TryLoadAsync(source);
        Directory.CreateDirectory(cache.CacheDirectory);
        File.WriteAllText(cache.GetEntryPath(miss.CacheKey), json);
        AnalysisCacheLookupResult invalid = await cache.TryLoadAsync(source);
        Assert.Equal(AnalysisCacheResultKind.Invalid, invalid.Result);
        Assert.Equal(AnalysisCacheMissReason.Corrupted, invalid.Reason);
    }

    [Fact]
    public async Task MissingRequiredDataIsInvalid()
    {
        using var scope = new TempScope();
        string source = scope.Source("map.osu", "missing");
        var cache = scope.Cache();
        AnalysisCacheLookupResult miss = await cache.TryLoadAsync(source);
        Directory.CreateDirectory(cache.CacheDirectory);
        string json = $"{{\"schemaVersion\":1,\"analyzerVersion\":\"{cache.AnalyzerVersion}\",\"createdAtUtc\":\"2026-01-01T00:00:00Z\",\"updatedAtUtc\":\"2026-01-01T00:00:00Z\",\"source\":{{\"fileName\":\"map.osu\",\"fileSize\":7,\"lastWriteTimeUtc\":\"2026-01-01T00:00:00Z\",\"contentHash\":\"{miss.ContentHash}\"}}}}";
        File.WriteAllText(cache.GetEntryPath(miss.CacheKey), json);
        AnalysisCacheLookupResult invalid = await cache.TryLoadAsync(source);
        Assert.Equal(AnalysisCacheMissReason.MissingRequiredData, invalid.Reason);
    }

    [Fact]
    public async Task AtomicSaveLeavesOnlyFinalJson()
    {
        using var scope = new TempScope();
        string source = scope.Source("map.osu", "atomic");
        var cache = scope.Cache();
        AnalysisCacheLookupResult miss = await cache.TryLoadAsync(source);
        await cache.SaveAsync(Entry(cache, source, miss.ContentHash, Snapshot("atomic")));
        Assert.Empty(Directory.EnumerateFiles(cache.CacheDirectory, "*.tmp"));
        Assert.Single(Directory.EnumerateFiles(cache.CacheDirectory, "*.json"));
        Assert.Equal(AnalysisCacheResultKind.Hit, (await cache.TryLoadAsync(source)).Result);
    }

    [Fact]
    public async Task TemporaryFileIsNeverAnEntry()
    {
        using var scope = new TempScope();
        string source = scope.Source("map.osu", "temp");
        var cache = scope.Cache();
        AnalysisCacheLookupResult miss = await cache.TryLoadAsync(source);
        Directory.CreateDirectory(cache.CacheDirectory);
        File.WriteAllText(cache.GetEntryPath(miss.CacheKey) + ".tmp", "{}", Encoding.UTF8);
        Assert.Equal(AnalysisCacheResultKind.Miss, (await cache.TryLoadAsync(source)).Result);
        Assert.Equal(0, (await cache.GetStatsAsync()).EntryCount);
    }

    [Fact]
    public async Task ConcurrentSaveSameKeyProducesReadableEntry()
    {
        using var scope = new TempScope();
        string source = scope.Source("map.osu", "concurrent");
        var first = scope.Cache();
        var second = scope.Cache();
        AnalysisCacheLookupResult miss = await first.TryLoadAsync(source);
        await Task.WhenAll(
            first.SaveAsync(Entry(first, source, miss.ContentHash, Snapshot("A"))),
            second.SaveAsync(Entry(second, source, miss.ContentHash, Snapshot("B"))));
        AnalysisCacheLookupResult hit = await first.TryLoadAsync(source);
        Assert.Equal(AnalysisCacheResultKind.Hit, hit.Result);
        Assert.Contains(hit.Entry!.Analysis.Metadata.Title, new[] { "A", "B" });
        Assert.Empty(Directory.EnumerateFiles(first.CacheDirectory, "*.tmp"));
    }

    [Fact]
    public async Task CorruptedEntryIsRebuiltByOrchestrator()
    {
        using var scope = new TempScope();
        string source = scope.Source("map.osu", "repair");
        var cache = scope.Cache();
        AnalysisCacheLookupResult miss = await cache.TryLoadAsync(source);
        Directory.CreateDirectory(cache.CacheDirectory);
        File.WriteAllText(cache.GetEntryPath(miss.CacheKey), "broken");
        var service = new CachedBeatmapAnalysisService(cache, Fake("repaired"));
        CachedBeatmapAnalysisResult result = await service.AnalyzeAsync(source);
        Assert.False(result.WasCacheHit);
        Assert.Equal(AnalysisCacheMissReason.Corrupted, result.InitialMissReason);
        Assert.Equal(AnalysisCacheResultKind.Hit, (await cache.TryLoadAsync(source)).Result);
    }

    [Fact]
    public async Task InvalidateDeletesOneEntry()
    {
        using var scope = new TempScope();
        string source = scope.Source("map.osu", "delete-one");
        var cache = scope.Cache();
        AnalysisCacheLookupResult miss = await cache.TryLoadAsync(source);
        await cache.SaveAsync(Entry(cache, source, miss.ContentHash, Snapshot("delete")));
        await cache.InvalidateAsync(miss.CacheKey);
        Assert.Equal(AnalysisCacheResultKind.Miss, (await cache.TryLoadAsync(source)).Result);
    }

    [Fact]
    public async Task ClearDeletesAllEntries()
    {
        using var scope = new TempScope();
        var cache = scope.Cache();
        foreach (string name in new[] { "a", "b" })
        {
            string source = scope.Source(name + ".osu", name);
            AnalysisCacheLookupResult miss = await cache.TryLoadAsync(source);
            await cache.SaveAsync(Entry(cache, source, miss.ContentHash, Snapshot(name)));
        }
        Assert.Equal(2, (await cache.GetStatsAsync()).EntryCount);
        await cache.ClearAsync();
        Assert.Equal(0, (await cache.GetStatsAsync()).EntryCount);
    }

    [Fact]
    public async Task DisabledCacheNeverLoadsOrSaves()
    {
        using var scope = new TempScope();
        string source = scope.Source("map.osu", "disabled");
        var cache = scope.Cache(enabled: false);
        var service = new CachedBeatmapAnalysisService(cache, Fake("disabled"));
        Assert.False((await service.AnalyzeAsync(source)).WasCacheHit);
        Assert.False((await service.AnalyzeAsync(source)).WasCacheHit);
        Assert.Equal(2, service.AnalyzerInvocationCount);
        Assert.False(Directory.Exists(cache.CacheDirectory));
        Assert.Equal(AnalysisCacheMissReason.Disabled, (await cache.TryLoadAsync(source)).Reason);
    }

    [Fact]
    public async Task CacheFilenameIsSha256AndFilesystemSafe()
    {
        using var scope = new TempScope();
        string source = scope.Source("weird name [x].osu", "safe");
        var cache = scope.Cache();
        AnalysisCacheLookupResult miss = await cache.TryLoadAsync(source);
        Assert.True(JsonBeatmapAnalysisCache.IsSafeCacheKey(miss.CacheKey));
        Assert.Equal(miss.CacheKey + ".json", Path.GetFileName(miss.CachePath));
    }

    [Fact]
    public async Task IdenticalFilesAtDifferentPathsShareEntry()
    {
        using var scope = new TempScope();
        string firstSource = scope.Source("one.osu", "duplicate");
        string secondSource = scope.Source("sub/two.osu", "duplicate");
        var cache = scope.Cache();
        AnalysisCacheLookupResult first = await cache.TryLoadAsync(firstSource);
        await cache.SaveAsync(Entry(cache, firstSource, first.ContentHash, Snapshot("portable")));
        AnalysisCacheLookupResult second = await cache.TryLoadAsync(secondSource);
        Assert.Equal(first.CacheKey, second.CacheKey);
        Assert.Equal(AnalysisCacheResultKind.Hit, second.Result);
    }

    [Fact]
    public async Task StatsReportEntryCountAndBytes()
    {
        using var scope = new TempScope();
        string source = scope.Source("map.osu", "stats");
        var cache = scope.Cache();
        AnalysisCacheLookupResult miss = await cache.TryLoadAsync(source);
        long bytes = await cache.SaveAsync(Entry(cache, source, miss.ContentHash, Snapshot("stats")));
        AnalysisCacheStats stats = await cache.GetStatsAsync();
        Assert.Equal(1, stats.EntryCount);
        Assert.Equal(bytes, stats.TotalBytes);
        Assert.NotNull(stats.OldestEntryUtc);
        Assert.NotNull(stats.NewestEntryUtc);
    }

    [Fact]
    public void DefaultsArePersistentAndDebugIsOptIn()
    {
        var cache = new JsonBeatmapAnalysisCache();
        Assert.True(cache.Enabled);
        Assert.EndsWith(Path.Combine("BeatInsight", "Cache", "Beatmaps"), cache.CacheDirectory);
        Assert.False(AnalysisCacheDebug.Enabled);
    }

    private static Func<string, CancellationToken, Task<BeatmapAnalysisSnapshot>> Fake(string title) =>
        (_, _) => Task.FromResult(Snapshot(title));

    private static BeatmapAnalysisCacheEntry Entry(JsonBeatmapAnalysisCache cache, string source, string hash, BeatmapAnalysisSnapshot snapshot)
    {
        FileInfo file = new(source);
        DateTime now = DateTime.UtcNow;
        return new(cache.SchemaVersion, cache.AnalyzerVersion, now, now,
            new(file.FullName, file.Name, file.Length, file.LastWriteTimeUtc, hash), snapshot);
    }

    private static BeatmapAnalysisSnapshot Snapshot(string title)
    {
        SkillPressureMetric unavailable = SkillPressureMetric.Unavailable("test");
        var pressure = new SkillPressureProfile(unavailable, unavailable, unavailable, unavailable,
            unavailable, unavailable, unavailable, unavailable, 0, 0, Array.Empty<string>());
        var context = new GameplayIdentityV3SectionContext(0, 0, 0, 1, 0, 0, 0, 0, Array.Empty<GameplayStructureKind>());
        var identity = new GameplayIdentityV3Profile(0, 0, 0, 100, GameplayStructureKind.ClassicMixed,
            null, Array.Empty<GameplayIdentityV3Skill>(), Array.Empty<GameplayIdentityV3Pattern>(),
            context, 1, 0, 0, 1, Array.Empty<GameplayIdentityV3DiagnosticFlag>());
        return new(
            new(null, null, "Artist", title, "Version", "Creator", 0, 120, 0, 9, 8, 4, 6, 3, 0, 0, 0, 0),
            new(10, 20, 30, 40, .1, .2, .3, .4, .5, .6, .7, "Classic", "", "", .8, Array.Empty<string>()),
            identity, pressure, SectionAnalysisResult.Empty(TimeSpan.Zero), BeatInsightRatingV2.Empty,
            GlobalTripletSpeedAnalysis.Empty, LocalSpeedPressureAnalysis.Empty,
            Array.Empty<GameplayIdentityV3Pattern>());
    }

    private static void AssertJsonEquivalent(BeatmapAnalysisSnapshot expected, BeatmapAnalysisSnapshot actual) =>
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));

    private sealed class TempScope : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "BeatInsightCacheTests", Guid.NewGuid().ToString("N"));
        public JsonBeatmapAnalysisCache Cache(bool enabled = true, string analyzerVersion = "test-v1", int schemaVersion = 1) =>
            new(Path.Combine(root, "cache"), enabled, analyzerVersion, schemaVersion);
        public string Source(string relativePath, string content)
        {
            string path = Path.Combine(root, "sources", relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, Encoding.UTF8);
            return path;
        }
        public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
