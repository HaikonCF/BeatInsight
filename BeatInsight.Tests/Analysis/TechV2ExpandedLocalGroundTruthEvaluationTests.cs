using BeatInsight.Services.Persistence;
using Microsoft.Data.Sqlite;
using System.IO;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechV2ExpandedLocalGroundTruthEvaluationTests(ITestOutputHelper output)
{
    [Fact]
    public void Run_ReevaluatesExplicitHumanLocalLabelsWithoutChangingRuntime()
    {
        string source = MlDatasetSampleRepository.DefaultDatabasePath;
        if (!File.Exists(source))
        {
            output.WriteLine("Local dataset cache unavailable; audit skipped safely.");
            return;
        }

        string copy = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase218-{Guid.NewGuid():N}.db");
        File.Copy(source, copy);
        TechV2ExpandedLocalGroundTruthReport report;
        try
        {
            report = TechV2ExpandedLocalGroundTruthEvaluation.Run(copy);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(copy);
        }

        output.WriteLine(TechV2ExpandedLocalGroundTruthEvaluation.FormatReport(report));
        Assert.Equal(5 * 4, report.ClassStats.Count);
        Assert.Equal(5, report.Ordering.Count);
        Assert.Contains(report.LocalizationNotes, note => note.ReviewId == "R16");
        Assert.Contains(report.LocalizationNotes, note => note.ReviewId == "R23");
        Assert.All(report.Regions, region =>
        {
            Assert.InRange(region.Scored.Prototypes.BalancedProduct, 0, 1);
            Assert.InRange(region.Scored.Prototypes.SupportedProduct, 0, 1);
        });
    }
}
