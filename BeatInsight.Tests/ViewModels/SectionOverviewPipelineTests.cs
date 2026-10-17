using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using BeatInsight.Models.AnalysisCache;
using BeatInsight.Models.Sections;
using BeatInsight.Services.AnalysisCache;
using BeatInsight.ViewModels;
using Xunit.Abstractions;

namespace BeatInsight.Tests.ViewModels;

public sealed class SectionOverviewPipelineTests(ITestOutputHelper output)
{
    private const string LastLoadedMap = @"D:\osu!\Songs\1563334 syudou - Kareshi no Jude\syudou - Kareshi no Jude (-Syncro) [bongo's Insane].osu";

    [Theory]
    [InlineData(LastLoadedMap, 26, 0)]
    [InlineData(@"D:\osu!\Songs\2210696 Brad Breeck - Gravity Falls Theme Song (TV Size) [no video]\Brad Breeck - Gravity Falls Theme Song (TV Size) (TritoBandito) [Log Off Now's Mystery Shack].osu", 8, 0)]
    [InlineData(@"D:\osu!\Songs\1382362 KikuoHana - Desert Theater\KikuoHana - Desert Theater (Keqing) [Mirage (#1)].osu", 46, 2)]
    public async Task RealShortAndLongMapsPreserveSectionsOnFreshAndCacheHit(string path, int expectedSections, int expectedSpikes)
    {
        Assert.True(File.Exists(path), $"Missing local validation map: {path}");
        string root = Path.Combine(Path.GetTempPath(), "BeatInsightSectionOverview", Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new JsonBeatmapAnalysisCache(root);
            var service = new CachedBeatmapAnalysisService(cache);
            var fresh = await service.AnalyzeAsync(path);
            Assert.False(fresh.WasCacheHit);
            Assert.Equal(AnalysisCacheMissReason.NotFound, fresh.InitialMissReason);
            VerifyProjection(fresh, expectedSections, expectedSpikes);
            var restartedService = new CachedBeatmapAnalysisService(cache);
            var cached = await restartedService.AnalyzeAsync(path);
            Assert.True(cached.WasCacheHit);
            Assert.Equal(0, restartedService.AnalyzerInvocationCount);
            VerifyProjection(cached, expectedSections, expectedSpikes);
            Assert.Equal(JsonSerializer.Serialize(fresh.Snapshot.SectionAnalysisV2), JsonSerializer.Serialize(cached.Snapshot.SectionAnalysisV2));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task LastLoadedMapPreviousSchemaCacheIsInvalidatedForLocalSpeedPressure()
    {
        // Read-only: does not invoke the service or alter the user's installed cache.
        var lookup = await new JsonBeatmapAnalysisCache().TryLoadAsync(LastLoadedMap);
        Assert.Equal(AnalysisCacheResultKind.Invalid, lookup.Result);
        Assert.Equal(AnalysisCacheMissReason.SchemaVersionMismatch, lookup.Reason);
        output.WriteLine($"Installed previous-schema snapshot is invalidated read-only: {lookup.Reason}");
    }

    [Fact]
    public async Task LegacyJsonMissingSectionAnalysisIsSafelyRegeneratedByExistingService()
    {
        string root = Path.Combine(Path.GetTempPath(), "BeatInsightSectionOverview", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "map.osu");
            File.WriteAllText(source, "test source");
            var cache = new JsonBeatmapAnalysisCache(Path.Combine(root, "cache"));
            var snapshot = OverviewViewModelTests.BuildSnapshot();
            var service = new CachedBeatmapAnalysisService(cache, (_, _) => Task.FromResult(snapshot));
            var original = await service.AnalyzeAsync(source);
            var json = JsonNode.Parse(File.ReadAllText(original.CachePath))!;
            json["analysis"]!.AsObject().Remove("sectionAnalysisV2");
            File.WriteAllText(original.CachePath, json.ToJsonString());
            var result = await service.AnalyzeAsync(source);
            Assert.False(result.WasCacheHit);
            Assert.Equal(AnalysisCacheMissReason.MissingRequiredData, result.InitialMissReason);
            Assert.Single(result.Snapshot.SectionAnalysisV2.Sections);
            Assert.True((await service.AnalyzeAsync(source)).WasCacheHit);
            Assert.Equal(2, service.AnalyzerInvocationCount);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingAndEmptySectionsNeverClaimNoSignificantSpikes(bool missing)
    {
        var snapshot = OverviewViewModelTests.BuildSnapshot() with
        { SectionAnalysisV2 = missing ? null! : SectionAnalysisResult.Empty(TimeSpan.Zero) };
        var vm = new OverviewViewModel();
        vm.LoadSnapshot(snapshot);
        Assert.Empty(vm.Sections);
        Assert.Empty(vm.DifficultyPoints);
        Assert.False(vm.HasSpikes);
        Assert.False(vm.HasSections);
        Assert.False(vm.HasDifficultyData);
        Assert.Equal("No section data available", vm.SpikesSummaryText);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteDifficultyDoesNotEnterGraphCollection(double value)
    {
        var snapshot = OverviewViewModelTests.BuildSnapshot();
        snapshot = snapshot with { SectionAnalysisV2 = snapshot.SectionAnalysisV2 with
        { Sections = [snapshot.SectionAnalysisV2.Sections[0] with { LocalDifficultyScore = value }] } };
        var vm = new OverviewViewModel();
        vm.LoadSnapshot(snapshot);
        Assert.Single(vm.Sections);
        Assert.Empty(vm.DifficultyPoints);
        Assert.False(vm.HasDifficultyData);
    }

    private void VerifyProjection(CachedBeatmapAnalysisResult result, int expectedSections, int expectedSpikes)
    {
        var vm = new OverviewViewModel();
        var coordinator = new OverviewLoadCoordinator(vm);
        long request = coordinator.Begin("validation");
        Assert.True(coordinator.LoadSnapshot(request, result.Snapshot, result.WasCacheHit, result.LoadTime, null));
        Assert.Empty(vm.Sections); // Deliberately deferred, same path as MainWindow.
        Assert.True(coordinator.SetReady(request));
        Assert.True(coordinator.CompleteSecondaryDetails(request, result.Snapshot, result.WasCacheHit));
        var data = result.Snapshot.SectionAnalysisV2;
        Assert.Equal(expectedSections, data.Sections.Count);
        Assert.Equal(expectedSections, vm.Sections.Count);
        Assert.Equal(expectedSections, vm.DifficultyPoints.Count);
        Assert.Equal(expectedSpikes, data.DifficultySpikeCount);
        Assert.Equal(expectedSpikes, vm.DifficultyPoints.Count(p => p.IsSpike));
        Assert.All(vm.DifficultyPoints, p => { Assert.True(double.IsFinite(p.LocalDifficulty)); Assert.True(double.IsFinite(p.TimeMs)); });
        Assert.Equal(expectedSpikes > 0, vm.HasSpikes);
        if (expectedSpikes == 0) Assert.Equal("No significant difficulty spikes detected", vm.SpikesSummaryText);
        output.WriteLine($"SECTION OVERVIEW DEBUG | Map={result.Snapshot.Metadata.Title} | Snapshot sections = {data.Sections.Count} | VM sections = {vm.Sections.Count} | Difficulty points = {vm.DifficultyPoints.Count} | Spikes = {data.DifficultySpikeCount} | Transitions = {data.GameplayTransitionCount} | Cached/Fresh = {(result.WasCacheHit ? "Cached" : "Fresh")} | Empty reason = None | LocalDifficulty={data.Sections.Min(s => s.LocalDifficultyScore):F2}..{data.Sections.Max(s => s.LocalDifficultyScore):F2}");
    }
}
