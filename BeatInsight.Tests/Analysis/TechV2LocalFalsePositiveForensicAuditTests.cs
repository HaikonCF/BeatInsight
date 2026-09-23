using BeatInsight.Services.Persistence;
using Microsoft.Data.Sqlite;
using System.IO;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechV2LocalFalsePositiveForensicAuditTests(ITestOutputHelper output)
{
    [Fact]
    public void Run_SeparatesStrongAndSustainedFalsePositiveBranches_WithoutRuntimeMutation()
    {
        string source = MlDatasetSampleRepository.DefaultDatabasePath;
        if (!File.Exists(source))
        {
            output.WriteLine("Local dataset cache unavailable; audit skipped safely.");
            return;
        }

        string copy = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase212-{Guid.NewGuid():N}.db");
        File.Copy(source, copy);
        TechV2LocalFalsePositiveReport report;
        try
        {
            report = TechV2LocalFalsePositiveForensicAudit.Run(copy);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(copy);
        }

        output.WriteLine(TechV2LocalFalsePositiveForensicAudit.FormatReport(report));

        Assert.Equal(13, report.Maps.Count);
        Assert.Equal(3, report.StrongGuardResults.Count);
        Assert.Equal(3, report.SustainedGuardResults.Count);
        Assert.All(report.Maps.Where(map => map.Resolved), map =>
        {
            Assert.InRange(map.Presence.PresencePercent, 0, 100);
            Assert.True(map.StrongRegionPresencePercent >= 0);
            Assert.True(map.PureSustainedRegionPresencePercent >= 0);
            Assert.True(map.StrongPlusSustainedRegionPresencePercent >= 0);
        });
    }
}
