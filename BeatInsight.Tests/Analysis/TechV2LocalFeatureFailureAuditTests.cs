using BeatInsight.Services.Persistence;
using Microsoft.Data.Sqlite;
using System.IO;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechV2LocalFeatureFailureAuditTests(ITestOutputHelper output)
{
    [Fact]
    public void Run_ReportsLocalFeatureFailuresWithoutChangingRuntime()
    {
        string source = MlDatasetSampleRepository.DefaultDatabasePath;
        if (!File.Exists(source))
        {
            output.WriteLine("Local dataset cache unavailable; audit skipped safely.");
            return;
        }

        string copy = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase219-{Guid.NewGuid():N}.db");
        File.Copy(source, copy);
        TechV2LocalFeatureFailureAuditReport report;
        try
        {
            report = TechV2LocalFeatureFailureAudit.Run(copy);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(copy);
        }

        output.WriteLine(TechV2LocalFeatureFailureAudit.FormatReport(report));

        Assert.NotEmpty(report.Regions);
        Assert.NotEmpty(report.PerFeatureFailures);
        Assert.NotEmpty(report.TopRootCauses);
        Assert.False(string.IsNullOrWhiteSpace(report.BestNextFixCandidate));

        Assert.All(report.Regions, region =>
        {
            Assert.InRange(region.SupportedProduct, 0, 1);
            Assert.InRange(region.StructuralValidity, 0, 1);
            Assert.InRange(region.TechnicalIntensity, 0, 1);
            Assert.InRange(region.SupportConfidence, 0, 1);
            Assert.NotEmpty(region.Windows);
            Assert.All(region.Windows, window =>
            {
                Assert.InRange(window.Geometry, 0, 1);
                Assert.InRange(window.Movement, 0, 1);
                Assert.InRange(window.Slider, 0, 1);
                Assert.InRange(window.Motif, 0, 1);
                Assert.NotEmpty(window.Ratios);
                Assert.NotEmpty(window.Normalization);
            });
        });
    }

    [Fact]
    public void Run_IsDeterministic_ForOrderingAndClassification()
    {
        string source = MlDatasetSampleRepository.DefaultDatabasePath;
        if (!File.Exists(source))
            return;

        string copyA = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase219-A-{Guid.NewGuid():N}.db");
        string copyB = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase219-B-{Guid.NewGuid():N}.db");
        File.Copy(source, copyA);
        File.Copy(source, copyB);
        TechV2LocalFeatureFailureAuditReport first;
        TechV2LocalFeatureFailureAuditReport second;
        try
        {
            first = TechV2LocalFeatureFailureAudit.Run(copyA);
            second = TechV2LocalFeatureFailureAudit.Run(copyB);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(copyA);
            File.Delete(copyB);
        }

        Assert.Equal(
            first.Regions.Select(Signature),
            second.Regions.Select(Signature));

        Assert.Equal(
            first.PerFeatureFailures.Select(item =>
                (item.Feature, item.PositiveLowScoringCases, item.NegativeHighScoringCases, item.FailureDirection,
                    string.Join(",", item.FailureTypes))),
            second.PerFeatureFailures.Select(item =>
                (item.Feature, item.PositiveLowScoringCases, item.NegativeHighScoringCases, item.FailureDirection,
                    string.Join(",", item.FailureTypes))));
    }

    private static string Signature(TechV2LocalFeatureFailureRegion region) =>
        $"{region.MapKey}|{region.RegionNumber}|{region.HumanLabel}|{region.SupportedProduct:F6}|{region.StructuralValidity:F6}|{region.TechnicalIntensity:F6}|{region.SupportConfidence:F6}";
}