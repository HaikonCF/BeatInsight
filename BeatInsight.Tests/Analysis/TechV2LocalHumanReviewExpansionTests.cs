using BeatInsight.Services.Persistence;
using Microsoft.Data.Sqlite;
using System.IO;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechV2LocalHumanReviewExpansionTests(ITestOutputHelper output)
{
    [Fact]
    public void Run_BuildsDeterministicUnlabeledLocalReviewShortlist()
    {
        string source = MlDatasetSampleRepository.DefaultDatabasePath;
        if (!File.Exists(source))
        {
            output.WriteLine("Local dataset cache unavailable; audit skipped safely.");
            return;
        }

        string copy = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase217-{Guid.NewGuid():N}.db");
        File.Copy(source, copy);
        TechV2LocalReviewExpansionReport report;
        try
        {
            report = TechV2LocalHumanReviewExpansion.Run(copy);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(copy);
        }

        output.WriteLine(TechV2LocalHumanReviewExpansion.FormatReport(report));
        Assert.True(report.SourceMapsConsidered <= 80);
        Assert.True(report.Shortlist.Count <= 24);
        Assert.All(report.Shortlist.GroupBy(candidate => candidate.DifficultyKey), group => Assert.True(group.Count() <= 2));
        Assert.Equal(report.Shortlist.Count, report.Shortlist.Select(candidate => candidate.ReviewId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(report.Shortlist, candidate => Assert.InRange(candidate.Phase216SupportedProduct, 0, 1));
    }
}
