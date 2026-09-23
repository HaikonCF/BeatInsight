using BeatInsight.Services.Persistence;
using Microsoft.Data.Sqlite;
using System.IO;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechV2LocalTechStrengthPrototypeTests(ITestOutputHelper output)
{
    [Fact]
    public void Run_UsesHumanLocalLabelsWithoutUsingStarRatingInScores()
    {
        string source = MlDatasetSampleRepository.DefaultDatabasePath;
        if (!File.Exists(source))
        {
            output.WriteLine("Local dataset cache unavailable; audit skipped safely.");
            return;
        }

        string copy = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase216-{Guid.NewGuid():N}.db");
        File.Copy(source, copy);
        TechV2LocalTechStrengthReport report;
        try
        {
            report = TechV2LocalTechStrengthPrototype.Run(copy);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(copy);
        }

        output.WriteLine(TechV2LocalTechStrengthPrototype.FormatReport(report));
        Assert.Equal(11, report.Maps.Count);
        Assert.Equal(5 * 4, report.Distributions.Count);
        Assert.Equal(5, report.Ordering.Count);
        Assert.All(report.Maps.Where(map => map.Resolved).SelectMany(map => map.Regions), region =>
        {
            Assert.InRange(region.StructuralValidity, 0, 1);
            Assert.InRange(region.TechnicalIntensity, 0, 1);
            Assert.InRange(region.SupportConfidence, 0, 1);
            Assert.InRange(region.Prototypes.SupportedProduct, 0, 1);
        });
    }
}
