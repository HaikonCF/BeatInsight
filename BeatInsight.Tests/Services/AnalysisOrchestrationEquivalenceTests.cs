using System.Text.Json;
using System.IO;
using BeatInsight.Analysis.Identity;
using BeatInsight.Analysis.Rating;
using BeatInsight.Analysis.Sections;
using BeatInsight.Analysis.SkillPressure;
using BeatInsight.Models;
using BeatInsight.Parser;

namespace BeatInsight.Tests.Services;

public sealed class AnalysisOrchestrationEquivalenceTests
{
    [Theory]
    [InlineData("Tower Of Heaven [Extra].osu")]
    [InlineData("FREEDOM DiVE [FrenZ's Insane].osu")]
    public void PrecomputedDependenciesMatchHistoricalAnalyzerEntryPoints(string fileName)
    {
        string path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "BeatInsight.Tests", "Fixtures", "Maps", fileName));
        Beatmap map = BeatmapParser.Load(path);
        var sections = SectionAnalysisV2Analyzer.Analyze(map);
        var pressure = SkillPressureAnalyzer.Analyze(map);

        var historicalIdentity = GameplayIdentityV3Analyzer.Analyze(map);
        var reusedIdentity = GameplayIdentityV3Analyzer.Analyze(
            map, map.GameplayProfile, sections, pressure);
        Assert.Equal(JsonSerializer.Serialize(historicalIdentity), JsonSerializer.Serialize(reusedIdentity));

        var historicalRating = BeatInsightRatingV2Analyzer.Analyze(map);
        var reusedRating = BeatInsightRatingV2Analyzer.Analyze(
            map, map.GameplayProfile, sections, pressure, reusedIdentity);
        Assert.Equal(JsonSerializer.Serialize(historicalRating), JsonSerializer.Serialize(reusedRating));
    }
}
