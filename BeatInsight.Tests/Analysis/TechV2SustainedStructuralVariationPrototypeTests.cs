using BeatInsight.Services.Persistence;
using Microsoft.Data.Sqlite;
using System.IO;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechV2SustainedStructuralVariationPrototypeTests(ITestOutputHelper output)
{
    [Fact]
    public void Run_ComparesSustainedStructuralVariationWithoutChangingRuntime()
    {
        string source = MlDatasetSampleRepository.DefaultDatabasePath;
        if (!File.Exists(source))
        {
            output.WriteLine("Local dataset cache unavailable; audit skipped safely.");
            return;
        }

        string copy = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase214-{Guid.NewGuid():N}.db");
        File.Copy(source, copy);
        TechV2SustainedStructuralVariationReport report;
        try
        {
            report = TechV2SustainedStructuralVariationPrototype.Run(copy);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(copy);
        }

        output.WriteLine(TechV2SustainedStructuralVariationPrototype.FormatReport(report));
        Assert.Equal(13, report.Maps.Count);
        Assert.Equal(6, report.Comparisons.Count);
        Assert.Equal(3, report.Candidates.Count);
        Assert.All(report.Comparisons, comparison =>
        {
            Assert.False(double.IsNaN(comparison.Tech.Mean));
            Assert.False(double.IsNaN(comparison.NonTech.Mean));
        });
        Assert.All(report.Maps.Where(map => map.Resolved).SelectMany(map => map.Runs), run =>
            Assert.Equal(run.WindowCount, run.WindowSignatures.Count));
    }
}
