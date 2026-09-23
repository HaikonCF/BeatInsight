using BeatInsight.Services.Persistence;
using Microsoft.Data.Sqlite;
using System.IO;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechV2ExistingFeatureGapAuditTests(ITestOutputHelper output)
{
    [Fact]
    public void Run_AuditsExistingSignalsWithoutChangingRuntimeDecisions()
    {
        string source = MlDatasetSampleRepository.DefaultDatabasePath;
        if (!File.Exists(source))
        {
            output.WriteLine("Local dataset cache unavailable; audit skipped safely.");
            return;
        }

        string copy = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase213-{Guid.NewGuid():N}.db");
        File.Copy(source, copy);
        TechV2ExistingFeatureGapReport report;
        try
        {
            report = TechV2ExistingFeatureGapAudit.Run(copy);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(copy);
        }

        output.WriteLine(TechV2ExistingFeatureGapAudit.FormatReport(report));
        Assert.Equal(13, report.Maps.Count);
        Assert.NotEmpty(report.Inventory);
        Assert.Equal(3, report.StrongPairs.Count);
        Assert.Equal(3, report.SustainedPairs.Count);
        Assert.All(report.StrongComparison.Concat(report.SustainedComparison), comparison =>
        {
            Assert.False(double.IsNaN(comparison.Tech.Mean));
            Assert.False(double.IsNaN(comparison.NonTech.Mean));
        });
    }
}
