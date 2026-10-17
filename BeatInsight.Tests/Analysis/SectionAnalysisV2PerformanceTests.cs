using System.IO;
using System.Text.Json;
using BeatInsight.Analysis.Sections;
using BeatInsight.Diagnostics;
using BeatInsight.Models.AnalysisCache;
using BeatInsight.Models.Sections;
using BeatInsight.Parser;
using BeatInsight.Services.AnalysisCache;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class SectionAnalysisV2PerformanceTests(ITestOutputHelper output)
{
    private const string Kareshi = @"D:\osu!\Songs\1563334 syudou - Kareshi no Jude\syudou - Kareshi no Jude (-Syncro) [bongo's Insane].osu";
    private const string GravityFalls = @"D:\osu!\Songs\2210696 Brad Breeck - Gravity Falls Theme Song (TV Size) [no video]\Brad Breeck - Gravity Falls Theme Song (TV Size) (TritoBandito) [Log Off Now's Mystery Shack].osu";
    private const string DesertTheater = @"D:\osu!\Songs\1382362 KikuoHana - Desert Theater\KikuoHana - Desert Theater (Keqing) [Mirage (#1)].osu";

    [Fact]
    public async Task InstalledPreInstrumentationSnapshotIsInvalidatedByCurrentSchema()
    {
        Assert.True(File.Exists(Kareshi), $"Missing local validation map: {Kareshi}");
        var cached = await new JsonBeatmapAnalysisCache().TryLoadAsync(Kareshi);
        Assert.Equal(AnalysisCacheResultKind.Invalid, cached.Result);
        Assert.Equal(AnalysisCacheMissReason.SchemaVersionMismatch, cached.Reason);
    }

    [Theory]
    [InlineData(GravityFalls)]
    [InlineData(Kareshi)]
    [InlineData(DesertTheater)]
    public void OptInProfilerMeasuresRealMapWithoutChangingAnalysis(string path)
    {
        Assert.True(File.Exists(path), $"Missing local validation map: {path}");
        var map = BeatmapParser.Load(path);

        GameplayDebug.SectionAnalysisV2PerformanceEnabled = false;
        SectionAnalysisResult reference = SectionAnalysisV2Analyzer.Analyze(map);
        Assert.Null(SectionAnalysisV2Analyzer.LastPerformanceProfile);

        try
        {
            GameplayDebug.SectionAnalysisV2PerformanceEnabled = true;
            SectionAnalysisResult instrumented = SectionAnalysisV2Analyzer.AnalyzePrepared(map, map.GameplayProfile);
            SectionAnalysisV2PerformanceProfile profile = Assert.IsType<SectionAnalysisV2PerformanceProfile>(
                SectionAnalysisV2Analyzer.LastPerformanceProfile);

            Assert.Equal(AnalyticalJson(reference), AnalyticalJson(instrumented));
            Assert.Equal(instrumented.SectionCount, profile.SectionCount);
            Assert.Equal(instrumented.SectionCount, Component(profile, "GameplayAnalyzer.Local").CallCount);
            Assert.Equal(instrumented.SectionCount, Component(profile, "LocalDifficultyScore").CallCount);
            Assert.Equal(instrumented.SectionCount, Component(profile, "SpikeDetection").CallCount);
            Assert.Equal(Math.Max(0, instrumented.SectionCount - 1), Component(profile, "TransitionDetection").CallCount);
            Assert.Equal(instrumented.SectionCount, profile.LocalBeatmapCount);
            Assert.Equal(instrumented.SectionCount, profile.LocalHitObjectListCount);

            output.WriteLine(
                $"PROFILE|Map={Path.GetFileName(path)}|TotalMs={profile.TotalDuration.TotalMilliseconds:F3}|" +
                $"Sections={profile.SectionCount}|SectionAvgMs={profile.AverageSectionDuration.TotalMilliseconds:F3}|" +
                $"SectionMinMs={profile.MinimumSectionDuration.TotalMilliseconds:F3}|SectionMaxMs={profile.MaximumSectionDuration.TotalMilliseconds:F3}|" +
                $"AllocatedBytes={profile.TotalAllocatedBytes}|LocalObjectRefs={profile.LocalHitObjectReferenceCount}");
            foreach (SectionAnalysisV2ComponentTiming component in profile.Components)
            {
                output.WriteLine(
                    $"COMPONENT|Map={Path.GetFileName(path)}|Name={component.Name}|Calls={component.CallCount}|" +
                    $"TotalMs={component.TotalDuration.TotalMilliseconds:F3}|AvgMs={component.TotalDuration.TotalMilliseconds / Math.Max(1, component.CallCount):F3}|" +
                    $"MinMs={component.MinimumDuration.TotalMilliseconds:F3}|MaxMs={component.MaximumDuration.TotalMilliseconds:F3}|" +
                    $"AllocatedBytes={component.AllocatedBytes}");
            }
        }
        finally
        {
            GameplayDebug.SectionAnalysisV2PerformanceEnabled = false;
        }
    }

    private static SectionAnalysisV2ComponentTiming Component(
        SectionAnalysisV2PerformanceProfile profile,
        string name) => Assert.Single(profile.Components, component => component.Name == name);

    private static string AnalyticalJson(SectionAnalysisResult result) =>
        JsonSerializer.Serialize(result with { AnalysisDuration = TimeSpan.Zero });
}
