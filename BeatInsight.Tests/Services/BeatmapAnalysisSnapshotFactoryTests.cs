using System.IO;
using BeatInsight.Analysis.Speed;
using BeatInsight.Models.Fusion;
using BeatInsight.Services.AnalysisCache;

namespace BeatInsight.Tests.Services;

public sealed class BeatmapAnalysisSnapshotFactoryTests
{
    [Fact]
    public void PatternSummariesUseAllPatternsAndPreserveDetectedOneTwo()
    {
        string fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Maps",
            "Vickeblanka - Black Rover (TV Size) (Sotarks) [Extreme].osu");

        var snapshot = BeatmapAnalysisSnapshotFactory.Analyze(fixturePath);

        Assert.Equal(
            snapshot.IdentityV3.AllPatterns.Select(pattern => pattern.Name),
            snapshot.PatternSummaries.Select(pattern => pattern.Name));
        Assert.True(snapshot.PatternSummaries.Count > snapshot.IdentityV3.MainPatterns.Count);
        Assert.Contains(
            snapshot.PatternSummaries,
            pattern => pattern.Name == "OneTwo" && pattern.Family == FusionPatternFamily.Jump);
        Assert.Contains(
            snapshot.PatternSummaries,
            pattern => snapshot.IdentityV3.MainPatterns.All(main => main.Name != pattern.Name));
    }

    [Fact]
    public void SnapshotPreservesLocalSpeedPressureWithoutChangingGlobalSpeed()
    {
        string fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Maps",
            "FREEDOM DiVE [Arles].osu");

        var snapshot = BeatmapAnalysisSnapshotFactory.Analyze(fixturePath);
        var map = BeatInsight.Parser.BeatmapParser.Load(fixturePath);

        Assert.Equal(
            GlobalTripletSpeedAnalyzer.Analyze(map).SpeedPercent,
            snapshot.GlobalTripletSpeed.SpeedPercent,
            10);
        var expectedLocal = LocalSpeedPressureAnalyzer.Analyze(map);
        Assert.Equal(expectedLocal.PressurePercent, snapshot.LocalSpeedPressure.PressurePercent, 10);
        Assert.Equal(expectedLocal.QualifyingTripletCount, snapshot.LocalSpeedPressure.QualifyingTripletCount);
        Assert.Equal(expectedLocal.QualifyingSectionCount, snapshot.LocalSpeedPressure.QualifyingSectionCount);
        Assert.Equal(expectedLocal.Sections, snapshot.LocalSpeedPressure.Sections);
    }
}
