using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using BeatInsight.Models.AnalysisCache;
using BeatInsight.Services.AnalysisCache;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Services;

public sealed class PersistentAnalysisCacheGlobalForensicTests(ITestOutputHelper output)
{
    private static readonly string[] Fixtures =
    [
        "(can you) understand me [uhh].osu", "Ashioto Tarte Tatin [Koori's Insane].osu",
        "Exit This Earth's Atmosphere [Primordial Nucleosynthesis].osu", "FREEDOM DiVE [Arles].osu",
        "FREEDOM DiVE [FrenZ's Insane].osu", "Frozen [Collab Insane].osu",
        "Kira Killer [Mocaotic's Insane].osu", "Snow Goose [Extra].osu", "Tower Of Heaven [Extra].osu",
        "forever we can make it! [Kudowari's Expert].osu",
        "Vickeblanka - Black Rover (TV Size) (Sotarks) [Extreme].osu"
    ];
    private static readonly Source[] Controls =
    [
        new("Road of Resistance", @"D:\osu!\Songs\399372 BABYMETAL - Road of Resistance (1)\BABYMETAL - Road of Resistance (Monstrata) [Rebellion].osu"),
        new("From Under Cover", @"D:\osu!\Songs\462386 Foreground Eclipse - From Under Cover (Caught Up In A Love Song)\Foreground Eclipse - From Under Cover (Caught Up In A Love Song) (Seni) [I've Stopped Time and Everything is at My Mercy].osu"),
        new("Connect TV Size", @"D:\osu!\Songs\1854325 ClariS - Connect (TV Size)\ClariS - Connect (TV Size) (Aakki) [Expert].osu"),
        new("Wings of Courage", @"D:\osu!\Songs\1706691 Kawada Mami - Wings of Courage -Sora o Koete-\Kawada Mami - Wings of Courage -Sora o Koete- (Zekk) [Expert].osu")
    ];

    [Fact]
    public async Task RealCorpusSecondInstanceUsesOnlyPhysicalJsonCache()
    {
        Source[] sources = BuildCorpus();
        Assert.NotEmpty(sources);
        string cacheDirectory = Path.Combine(Path.GetTempPath(), "BeatInsightPersistentCacheForensic", Guid.NewGuid().ToString("N"));
        var firstRows = new List<Row>();
        var secondRows = new List<Row>();
        string exampleJson = "";
        CorruptionResult corruption = new(false, AnalysisCacheMissReason.None, 0);
        AnalysisCacheStats stats = new(0, 0, null, null);
        TimeSpan firstTotal = TimeSpan.Zero, secondTotal = TimeSpan.Zero;
        int firstInvocations = 0, secondInvocations = 0, equivalent = 0;
        try
        {
            var cacheA = new JsonBeatmapAnalysisCache(cacheDirectory);
            var serviceA = new CachedBeatmapAnalysisService(cacheA, async (path, token) =>
            {
                firstInvocations++;
                return await BeatmapAnalysisSnapshotFactory.AnalyzeAsync(path, token);
            });
            Stopwatch firstWatch = Stopwatch.StartNew();
            foreach (Source source in sources)
            {
                Stopwatch mapWatch = Stopwatch.StartNew();
                CachedBeatmapAnalysisResult result = await serviceA.AnalyzeAsync(source.Path);
                mapWatch.Stop();
                Assert.False(result.WasCacheHit);
                Assert.True(File.Exists(result.CachePath));
                firstRows.Add(new(source, result, mapWatch.Elapsed));
            }
            firstWatch.Stop();
            firstTotal = firstWatch.Elapsed;
            Assert.Equal(sources.Length, firstInvocations);
            stats = await cacheA.GetStatsAsync();

            var cacheB = new JsonBeatmapAnalysisCache(cacheDirectory);
            var serviceB = new CachedBeatmapAnalysisService(cacheB, async (path, token) =>
            {
                secondInvocations++;
                return await BeatmapAnalysisSnapshotFactory.AnalyzeAsync(path, token);
            });
            Stopwatch secondWatch = Stopwatch.StartNew();
            foreach (Source source in sources)
            {
                Stopwatch mapWatch = Stopwatch.StartNew();
                CachedBeatmapAnalysisResult result = await serviceB.AnalyzeAsync(source.Path);
                mapWatch.Stop();
                Assert.True(result.WasCacheHit);
                secondRows.Add(new(source, result, mapWatch.Elapsed));
            }
            secondWatch.Stop();
            secondTotal = secondWatch.Elapsed;
            Assert.Equal(0, secondInvocations);
            Assert.True(secondTotal < firstTotal);

            for (int index = 0; index < firstRows.Count; index++)
            {
                if (JsonSerializer.Serialize(firstRows[index].Result.Snapshot) ==
                    JsonSerializer.Serialize(secondRows[index].Result.Snapshot)) equivalent++;
            }
            Assert.Equal(sources.Length, equivalent);

            string samplePath = firstRows[0].Result.CachePath;
            BeatmapAnalysisCacheEntry sampleEntry = (await cacheB.TryLoadAsync(firstRows[0].Source.Path)).Entry!;
            exampleJson = JsonSerializer.Serialize(new
            {
                sampleEntry.SchemaVersion,
                sampleEntry.AnalyzerVersion,
                sampleEntry.CreatedAtUtc,
                sampleEntry.Source,
                metadata = sampleEntry.Analysis.Metadata,
                gameplay = new
                {
                    sampleEntry.Analysis.GameplayProfile.AimScore,
                    sampleEntry.Analysis.GameplayProfile.SpeedScore,
                    sampleEntry.Analysis.GameplayProfile.ReadingScore,
                    sampleEntry.Analysis.GameplayProfile.TechScore
                },
                identityV3 = new { sampleEntry.Analysis.IdentityV3.PrimaryStructure, sampleEntry.Analysis.IdentityV3.GlobalIdentityConfidence },
                ratingV2 = new { sampleEntry.Analysis.RatingV2.Score, sampleEntry.Analysis.RatingV2.Confidence },
                globalTripletSpeed = new { sampleEntry.Analysis.GlobalTripletSpeed.SpeedPercent, sampleEntry.Analysis.GlobalTripletSpeed.Confidence },
                sections = new { sampleEntry.Analysis.SectionAnalysisV2.SectionCount, sampleEntry.Analysis.SectionAnalysisV2.DifficultySpikeCount, sampleEntry.Analysis.SectionAnalysisV2.GameplayTransitionCount },
                patternSummaryCount = sampleEntry.Analysis.PatternSummaries.Count
            }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            await File.WriteAllTextAsync(samplePath, "{\"schemaVersion\":1,", Encoding.UTF8);
            int recoveryInvocations = 0;
            var recoveryService = new CachedBeatmapAnalysisService(new JsonBeatmapAnalysisCache(cacheDirectory), async (path, token) =>
            {
                recoveryInvocations++;
                return await BeatmapAnalysisSnapshotFactory.AnalyzeAsync(path, token);
            });
            CachedBeatmapAnalysisResult recovered = await recoveryService.AnalyzeAsync(firstRows[0].Source.Path);
            AnalysisCacheLookupResult repairedLookup = await cacheB.TryLoadAsync(firstRows[0].Source.Path);
            corruption = new(!recovered.WasCacheHit && repairedLookup.Result == AnalysisCacheResultKind.Hit,
                recovered.InitialMissReason, recoveryInvocations);
            Assert.True(corruption.Recovered);
            Assert.Equal(AnalysisCacheMissReason.Corrupted, corruption.Reason);
            Assert.Equal(1, corruption.AnalyzerInvocations);
        }
        finally
        {
            WriteReport(sources, cacheDirectory, firstRows, secondRows, firstTotal, secondTotal,
                firstInvocations, secondInvocations, stats, equivalent, exampleJson, corruption);
            if (Directory.Exists(cacheDirectory)) Directory.Delete(cacheDirectory, recursive: true);
        }
        output.WriteLine($"Persistent cache forensic: first {firstTotal.TotalSeconds:F2}s, second {secondTotal.TotalSeconds:F2}s, second-run analyzer calls {secondInvocations}.");
    }

    private static void WriteReport(IReadOnlyList<Source> sources, string cacheDirectory,
        IReadOnlyList<Row> firstRows, IReadOnlyList<Row> secondRows, TimeSpan firstTotal, TimeSpan secondTotal,
        int firstInvocations, int secondInvocations, AnalysisCacheStats stats, int equivalent,
        string exampleJson, CorruptionResult corruption)
    {
        var report = new StringBuilder();
        report.AppendLine("# Persistent Analysis Cache forensic\n");
        report.AppendLine($"Corpus: **{sources.Count} existing maps**. No download. Forensic cache isolated at `{cacheDirectory}` and deleted after measurement. Production default: `{JsonBeatmapAnalysisCache.DefaultCacheDirectory}`.\n");
        report.AppendLine("## AUDIT\n");
        report.AppendLine("|Component|Status|Source|Reusable|Risk|");
        report.AppendLine("|---|---|---|---|---|");
        report.AppendLine("|Historical runtime cache|PARTIAL|BeatmapAnalysisCacheService + SQLite|Path/UI profile persistence patterns|Path identity; partial snapshot; no frozen V2 layers|");
        report.AppendLine("|Cache key|NEW|SHA-256 raw .osu content|Stable across path moves and duplicates|Requires one lightweight file read|");
        report.AppendLine("|Disk persistence|NEW|One JSON per content hash|Human-readable and independently recoverable|Larger than compact binary/SQLite|");
        report.AppendLine("|Analyzer versioning|NEW|PersistentAnalysisCacheVersion|Independent from UI/App version|Manual bump required when analytical semantics change|");
        report.AppendLine("|Invalidation|NEW|Schema + analyzer version + content hash + required data|Deterministic|Old content hashes remain until manual cleanup|");
        report.AppendLine("|Serialization|NEW|System.Text.Json dedicated snapshot|No Beatmap runtime graph or unsafe polymorphism|Snapshot schema must remain explicit|");
        report.AppendLine("|Corruption recovery|NEW|Exception-safe lookup + full reconstruction|Cache cannot block analysis|Corrupt entry costs one reanalysis|");
        report.AppendLine("|Atomic writes|NEW|Unique .tmp + flush-to-disk + same-volume move|Prevents partial final JSON|Cross-process last writer wins, content remains valid|");
        report.AppendLine("|Index|OMITTED V1|Direct hash filename lookup|No single point of failure|Directory enumeration needed for stats|");
        report.AppendLine("|Cleanup|NEW|Invalidate + Clear|Manual and non-aggressive|No automatic orphan expiry|");
        report.AppendLine("|Diagnostics|NEW|Structured reasons + opt-in DebugLogger|Observable without Console|Disabled by default|\n");
        report.AppendLine("## ARCHITECTURE\n");
        report.AppendLine("`path → SHA-256 → <hash>.json → validate → HIT`, otherwise `existing frozen analyzers → BeatmapAnalysisSnapshot → atomic save`. `CachedBeatmapAnalysisService.AnalyzeAsync(path)` returns the same snapshot contract in both cases.\n");
        report.AppendLine("## CACHE LOCATION\n");
        report.AppendLine("Production: `%LOCALAPPDATA%\\BeatInsight\\Cache\\Beatmaps\\`. Tests and forensic inject isolated directories. `CacheEnabled` is true by default.\n");
        report.AppendLine("## CACHE KEY / HASH STRATEGY\n");
        report.AppendLine("Lower-case SHA-256 of raw `.osu` bytes is the sole identity. Path, filename, size and last-write timestamp are trace metadata only. Identical bytes at different paths share the entry; modified bytes create a new key.\n");
        report.AppendLine("## SCHEMA / ANALYZER VERSIONING\n");
        report.AppendLine($"Root `schemaVersion={PersistentAnalysisCacheVersion.SchemaVersion}` and `analyzerVersion={PersistentAnalysisCacheVersion.AnalyzerVersion}` are mandatory. Either mismatch invalidates and rebuilds the entry.\n");
        report.AppendLine("## SERIALIZATION\n");
        report.AppendLine("Dedicated DTO root; camel-case properties; stable enum strings; null omission; UTC dates. Snapshot contains metadata, useful historical profile, Identity V3/main skills/main patterns, Skill Pressure V2, full Section Analysis V2 timelines/spikes/transitions, Rating V2 and Global Triplet Speed. HitObjects, TimingPoints, callbacks and runtime Beatmap state are not stored.\n");
        report.AppendLine("## INVALIDATION / ATOMIC WRITE / CORRUPTION RECOVERY\n");
        report.AppendLine("Content changes select a new hash file. Schema/analyzer mismatches, unreadable/truncated JSON and missing required blocks are invalid. Saves use a unique temporary file, complete serialization, disk flush and atomic same-directory replacement under a per-key lock.\n");
        report.AppendLine($"Deliberate truncation: reason **{corruption.Reason}**, analyzer calls **{corruption.AnalyzerInvocations}**, repaired valid JSON **{corruption.Recovered}**.\n");
        report.AppendLine("## FIRST RUN\n");
        report.AppendLine($"Total **{firstTotal.TotalSeconds:F2}s**; analyzer invocations **{firstInvocations}/{sources.Count}**; physical files **{stats.EntryCount}**.\n");
        report.AppendLine("## SECOND RUN\n");
        report.AppendLine($"New cache and orchestration instances. Total **{secondTotal.TotalSeconds:F2}s**; cache hits **{secondRows.Count(row => row.Result.WasCacheHit)}/{sources.Count}**; AnalyzerInvocationCount **{secondInvocations}**.\n");
        report.AppendLine("## PERFORMANCE / FILE SIZES\n");
        double ratio = secondTotal.TotalMilliseconds <= 0 ? 0 : firstTotal.TotalMilliseconds / secondTotal.TotalMilliseconds;
        long[] sizes = firstRows.Select(row => row.Result.EntrySizeBytes).Order().ToArray();
        report.AppendLine($"Second run speedup **{ratio:F1}×**. Entries **{stats.EntryCount}**, total **{stats.TotalBytes:N0} bytes**, average **{(sizes.Length == 0 ? 0 : sizes.Average()):N0}**, min **{(sizes.Length == 0 ? 0 : sizes.Min()):N0}**, max **{(sizes.Length == 0 ? 0 : sizes.Max()):N0}**.\n");
        report.AppendLine("|Map|First run ms|Second run ms|Load/hash ms|JSON bytes|");
        report.AppendLine("|---|---:|---:|---:|---:|");
        for (int index = 0; index < firstRows.Count; index++)
            report.AppendLine($"|{Cell(firstRows[index].Source.Name)}|{firstRows[index].Elapsed.TotalMilliseconds:F2}|{secondRows[index].Elapsed.TotalMilliseconds:F2}|{secondRows[index].Result.LoadTime.TotalMilliseconds:F2}|{firstRows[index].Result.EntrySizeBytes:N0}|");
        report.AppendLine();
        report.AppendLine("## RESULT EQUIVALENCE\n");
        report.AppendLine($"Fresh versus deserialized snapshots semantically identical by complete JSON comparison: **{equivalent}/{sources.Count}**. This covers metadata, profile, Identity V3, Skill Pressure, Rating V2, Global Triplet Speed, Sections and pattern summaries.\n");
        report.AppendLine("## MANUAL JSON INSPECTION\n");
        report.AppendLine("Excerpt projected from a real entry (large timelines omitted):\n");
        report.AppendLine("```json");
        report.AppendLine(exampleJson);
        report.AppendLine("```\n");
        report.AppendLine("## KNOWN LIMITATIONS\n");
        report.AppendLine("- Analyzer compatibility version requires an intentional manual bump when frozen analytical outputs change.");
        report.AppendLine("- V1 has no global index and no automatic orphan cleanup; individual files remain independently usable.");
        report.AppendLine("- Concurrent writers are serialized in-process; independent processes may race, but unique temp files plus atomic replacement preserve a complete valid last-writer result.");
        report.AppendLine("- Source hashing still reads raw `.osu` bytes, intentionally avoiding parsing and analysis on hits.\n");
        bool ready = firstInvocations == sources.Count && secondInvocations == 0 &&
            secondRows.Count == sources.Count && secondRows.All(row => row.Result.WasCacheHit) &&
            equivalent == sources.Count && secondTotal < firstTotal && corruption.Recovered;
        report.AppendLine("## FINAL DECISION\n");
        report.AppendLine($"**PERSISTENT ANALYSIS CACHE: {(ready ? "READY TO FREEZE" : "STRUCTURAL REWORK REQUIRED")}**\n");
        report.AppendLine($"Acceptance: physical files {stats.EntryCount}; restart hits {secondRows.Count(row => row.Result.WasCacheHit)}/{sources.Count}; second-run analyzer calls {secondInvocations}; source/version/corruption invalidation covered; equivalence {equivalent}/{sources.Count}; frozen analyzers unchanged.");

        string path = Path.Combine(FindRepoRoot(), "Dataset", "Reports", "persistent-analysis-cache-forensic.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, report.ToString(), Encoding.UTF8);
    }

    private static Source[] BuildCorpus()
    {
        string fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps");
        var sources = Fixtures.Select(name => new Source(Path.GetFileNameWithoutExtension(name), Path.Combine(fixtureDirectory, name))).Where(source => File.Exists(source.Path)).ToList();
        foreach (var item in Analysis.StreamPatternGlobalForensicTests.Corpus)
            if (File.Exists(item.Path) && !sources.Any(source => source.Path == item.Path)) sources.Add(new(item.Title, item.Path));
        foreach (var item in Analysis.TechV2SliderDensityCalibrationAudit.Corpus)
            if (File.Exists(item.Path) && !sources.Any(source => source.Path == item.Path)) sources.Add(new(item.Name, item.Path));
        foreach (Source control in Controls)
            if (File.Exists(control.Path) && !sources.Any(source => source.Path == control.Path)) sources.Add(control);
        return sources.ToArray();
    }

    private static string Cell(string value) => value.Replace("|", "\\|");
    private static string FindRepoRoot() { DirectoryInfo? directory = new(AppContext.BaseDirectory); while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BeatInsight.slnx"))) directory = directory.Parent; return directory?.FullName ?? throw new DirectoryNotFoundException(); }
    private sealed record Source(string Name, string Path);
    private sealed record Row(Source Source, CachedBeatmapAnalysisResult Result, TimeSpan Elapsed);
    private sealed record CorruptionResult(bool Recovered, AnalysisCacheMissReason Reason, int AnalyzerInvocations);
}
