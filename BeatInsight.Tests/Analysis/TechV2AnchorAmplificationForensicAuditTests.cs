using BeatInsight.Analysis;
using BeatInsight.Services.Persistence;
using Microsoft.Data.Sqlite;
using System.IO;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

/// <summary>Phase 2.10 structural tests plus the human-grounded forensic runner.</summary>
public sealed class TechV2AnchorAmplificationForensicAuditTests(ITestOutputHelper output)
{
    [Fact]
    public void Run_ReportsAnchorAmplificationWithoutMutatingRuntimeDecisions()
    {
        string source = MlDatasetSampleRepository.DefaultDatabasePath;
        if (!File.Exists(source))
        {
            output.WriteLine("Local dataset cache unavailable; fixture-only audit remains covered by structural tests.");
            return;
        }

        string temporaryDatabase = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase210-{Guid.NewGuid():N}.db");
        File.Copy(source, temporaryDatabase);
        TechV2AnchorAmplificationReport report;
        try
        {
            report = TechV2AnchorAmplificationForensicAudit.Run(temporaryDatabase);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(temporaryDatabase);
        }
        output.WriteLine(TechV2AnchorAmplificationForensicAudit.FormatReport(report));

        Assert.Equal(TechV2HumanGroundedStrongTechAudit.References.Count, report.Maps.Count);
        Assert.Equal(4, report.PolicyResults.Count);
        Assert.All(report.Maps.Where(map => map.Resolved), map =>
        {
            Assert.All(map.Decisions, decision =>
            {
                Assert.InRange(decision.GeometryEvidence, 0, 1);
                Assert.InRange(decision.MovementEvidence, 0, 1);
                Assert.InRange(decision.SliderEvidence, 0, 1);
                Assert.InRange(decision.MotifEvidence, 0, 1);
            });
            Assert.InRange(map.Presence.PresencePercent, 0, 100);
        });
        Assert.All(report.PolicyResults.SelectMany(policy => policy.Maps), result =>
            Assert.InRange(result.PresenceAfterPercent, 0, 100));
    }

    [Fact]
    public void AnalyzeRegionForTest_IsDeterministic_AndMeasuresSupportDistance()
    {
        TechV2Region region = new(0, 1200, 1200, 0, 23, 4, 2);
        IReadOnlyList<TechV2WindowDecision> decisions =
        [
            Decision(0, true, false),
            Decision(4, false, true),
            Decision(8, false, true),
            Decision(12, true, false),
        ];

        TechV2AnchorAmplificationRegion first = TechV2AnchorAmplificationForensicAudit.AnalyzeRegionForTest(
            "synthetic", TechV2HumanTruth.Tech, region, decisions);
        TechV2AnchorAmplificationRegion second = TechV2AnchorAmplificationForensicAudit.AnalyzeRegionForTest(
            "synthetic", TechV2HumanTruth.Tech, region, decisions);

        Assert.Equal(first.MapKey, second.MapKey);
        Assert.Equal(first.StrongTechCount, second.StrongTechCount);
        Assert.Equal(first.TechSupportCount, second.TechSupportCount);
        Assert.Equal(first.SupportWindows.Select(window => window.DistanceInStrides),
            second.SupportWindows.Select(window => window.DistanceInStrides));
        Assert.Equal(2, first.StrongTechCount);
        Assert.Equal(2, first.TechSupportCount);
        Assert.Equal(1, first.MaximumSupportDistanceInStrides);
        Assert.Equal(0, first.SupportWindowsBeforeFirstStrong);
        Assert.Equal(0, first.SupportWindowsAfterLastStrong);
    }

    [Fact]
    public void AnalyzeRegionForTest_SingleAndMultiStrongClassification_IsStable()
    {
        TechV2Region region = new(0, 1_600, 1_600, 0, 19, 3, 1);
        TechV2AnchorAmplificationRegion single = TechV2AnchorAmplificationForensicAudit.AnalyzeRegionForTest(
            "synthetic", TechV2HumanTruth.NonTech, region,
            [Decision(0, false, true), Decision(4, true, false), Decision(8, false, true)]);
        TechV2AnchorAmplificationRegion multi = TechV2AnchorAmplificationForensicAudit.AnalyzeRegionForTest(
            "synthetic", TechV2HumanTruth.NonTech, region,
            [Decision(0, true, false), Decision(4, false, true), Decision(8, true, false)]);

        Assert.Equal(1, single.StrongTechCount);
        Assert.Equal(2, multi.StrongTechCount);
        Assert.True(single.AmplificationRatio >= 1);
        Assert.True(multi.AmplificationRatio >= 1);
    }

    private static TechV2WindowDecision Decision(int startIndex, bool strong, bool support) => new(
        startIndex, startIndex + 11, startIndex * 100, (startIndex * 100) + 700,
        strong, .5, .5, .5, .2, 2, .425, support, support);
}
