using BeatInsight.Services.Persistence;
using Microsoft.Data.Sqlite;
using System.IO;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechV2LocalHumanGroundTruthReviewTests(ITestOutputHelper output)
{
    [Fact]
    public void Run_ListsCurrentPresenceRegionsWithoutInferringLocalLabels()
    {
        string source = MlDatasetSampleRepository.DefaultDatabasePath;
        if (!File.Exists(source))
        {
            output.WriteLine("Local dataset cache unavailable; audit skipped safely.");
            return;
        }

        string copy = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase215-{Guid.NewGuid():N}.db");
        File.Copy(source, copy);
        TechV2LocalHumanReviewReport report;
        try
        {
            report = TechV2LocalHumanGroundTruthReview.Run(copy);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(copy);
        }

        output.WriteLine(TechV2LocalHumanGroundTruthReview.FormatReport(report));
        Assert.Equal(6, report.Maps.Count);
        Assert.All(report.Maps.Where(map => map.Resolved).SelectMany(map => map.Regions), region =>
        {
            Assert.InRange(region.PresenceContributionPercent, 0, 100);
            Assert.Contains(region.RegionType, new[] { "STRONG_ONLY", "SUSTAINED_ONLY", "STRONG_PLUS_SUSTAINED" });
            Assert.True(region.Duration > 0);
        });
    }
}
