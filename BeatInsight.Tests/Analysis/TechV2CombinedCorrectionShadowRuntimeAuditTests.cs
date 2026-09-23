using BeatInsight.Services.Persistence;
using Microsoft.Data.Sqlite;
using System.IO;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechV2CombinedCorrectionShadowRuntimeAuditTests(ITestOutputHelper output)
{
    [Fact]
    public void Run_ProducesShadowReport_WithStrictInvariance()
    {
        string source = MlDatasetSampleRepository.DefaultDatabasePath;
        if (!File.Exists(source))
        {
            output.WriteLine("Local dataset cache unavailable; phase 2.22 audit skipped safely.");
            return;
        }

        string copy = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase222-{Guid.NewGuid():N}.db");
        File.Copy(source, copy);

        TechV2Phase222AuditReport report;
        try
        {
            report = TechV2CombinedCorrectionShadowRuntimeAudit.Run(copy);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(copy);
        }

        output.WriteLine(TechV2CombinedCorrectionShadowRuntimeAudit.FormatReport(report));

        Assert.NotEmpty(report.HumanCorpusShadowResults);
        Assert.True(report.ThousandMapDeltaDistribution.Count >= 0);
        Assert.True(report.ExtremePositiveDeltas.Count <= 10);
        Assert.True(report.ExtremeNegativeDeltas.Count <= 10);

        Assert.True(report.DecisionInvarianceCheck.AllDecisionKindUnchanged);
        Assert.True(report.DecisionInvarianceCheck.AllStrongTechCountsUnchanged);
        Assert.True(report.DecisionInvarianceCheck.AllTechSupportCountsUnchanged);
        Assert.True(report.DecisionInvarianceCheck.AllSustainedTechCountsUnchanged);
        Assert.True(report.DecisionInvarianceCheck.AllPresenceValuesUnchanged);

        Assert.Contains(report.HumanCorpusShadowResults, item => item.MapKey == "Exit");
        Assert.Contains(report.HumanCorpusShadowResults, item => item.MapKey == "Kira");
        Assert.Contains(report.HumanCorpusShadowResults, item => item.MapKey == "Frozen");
        Assert.Contains(report.HumanCorpusShadowResults, item => item.MapKey == "WorldEndsNow");
        Assert.Contains(report.HumanCorpusShadowResults, item => item.MapKey == "GloriousDays");
        Assert.Contains(report.HumanCorpusShadowResults, item => item.MapKey == "Inferno");
        Assert.Contains(report.HumanCorpusShadowResults, item => item.MapKey == "Howling");
        Assert.Contains(report.HumanCorpusShadowResults, item => item.MapKey == "TaoHuaXiao");
        Assert.Contains(report.HumanCorpusShadowResults, item => item.MapKey == "RockOnExpert");
        Assert.Contains(report.HumanCorpusShadowResults, item => item.MapKey == "TrustInYou");
        Assert.Contains(report.HumanCorpusShadowResults, item => item.MapKey == "HappppySong");
        Assert.Contains(report.HumanCorpusShadowResults, item => item.MapKey == "RockOnSeAqours");
        Assert.Contains(report.HumanCorpusShadowResults, item => item.MapKey == "FinestSky");
    }

    [Fact]
    public void Run_IsDeterministic_ForDistributionAndRecommendation()
    {
        string source = MlDatasetSampleRepository.DefaultDatabasePath;
        if (!File.Exists(source))
            return;

        string copyA = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase222-A-{Guid.NewGuid():N}.db");
        string copyB = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase222-B-{Guid.NewGuid():N}.db");
        File.Copy(source, copyA);
        File.Copy(source, copyB);

        TechV2Phase222AuditReport first;
        TechV2Phase222AuditReport second;
        try
        {
            first = TechV2CombinedCorrectionShadowRuntimeAudit.Run(copyA);
            second = TechV2CombinedCorrectionShadowRuntimeAudit.Run(copyB);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(copyA);
            File.Delete(copyB);
        }

        Assert.Equal(
            first.HumanCorpusShadowResults.Select(item =>
                $"{item.MapKey}|{item.RegionNumber}|{item.HumanLabel}|{item.CurrentValue:F8}|{item.ShadowCorrectedValue:F8}|{item.Delta:F8}"),
            second.HumanCorpusShadowResults.Select(item =>
                $"{item.MapKey}|{item.RegionNumber}|{item.HumanLabel}|{item.CurrentValue:F8}|{item.ShadowCorrectedValue:F8}|{item.Delta:F8}"));

        Assert.Equal(
            $"{first.ThousandMapDeltaDistribution.Count}|{first.ThousandMapDeltaDistribution.AverageDelta:F10}|{first.ThousandMapDeltaDistribution.MedianDelta:F10}|{first.ThousandMapDeltaDistribution.MinDelta:F10}|{first.ThousandMapDeltaDistribution.MaxDelta:F10}|{first.ThousandMapDeltaDistribution.PercentageIncreased:F10}|{first.ThousandMapDeltaDistribution.PercentageDecreased:F10}|{first.ThousandMapDeltaDistribution.PercentageUnchanged:F10}",
            $"{second.ThousandMapDeltaDistribution.Count}|{second.ThousandMapDeltaDistribution.AverageDelta:F10}|{second.ThousandMapDeltaDistribution.MedianDelta:F10}|{second.ThousandMapDeltaDistribution.MinDelta:F10}|{second.ThousandMapDeltaDistribution.MaxDelta:F10}|{second.ThousandMapDeltaDistribution.PercentageIncreased:F10}|{second.ThousandMapDeltaDistribution.PercentageDecreased:F10}|{second.ThousandMapDeltaDistribution.PercentageUnchanged:F10}");

        Assert.Equal(first.ExtremePositiveDeltas.Select(ExtremeSignature), second.ExtremePositiveDeltas.Select(ExtremeSignature));
        Assert.Equal(first.ExtremeNegativeDeltas.Select(ExtremeSignature), second.ExtremeNegativeDeltas.Select(ExtremeSignature));
        Assert.Equal(first.DenominatorOneBehavior.Select(DenominatorSignature), second.DenominatorOneBehavior.Select(DenominatorSignature));
        Assert.Equal(first.LocalContextContaminationFindings.Select(ContaminationSignature), second.LocalContextContaminationFindings.Select(ContaminationSignature));

        Assert.Equal(first.DecisionInvarianceCheck, second.DecisionInvarianceCheck);
        Assert.Equal(first.Recommendation, second.Recommendation);
    }

    private static string ExtremeSignature(TechV2Phase222ExtremeDelta value) =>
        $"{value.Map}|{value.Difficulty}|{value.Timestamp:F4}|{value.Current:F8}|{value.Corrected:F8}|{value.Delta:F8}|{value.SupportCounts.ValidTransitions}|{value.SupportCounts.StructuralCount}|{value.SupportCounts.SharpCount}|{value.SupportCounts.SpacingVariationCount}|{value.SupportCounts.AlternatingCount}|{value.SupportCounts.SliderCount}|{value.SupportCounts.ComplexSliderCount}|{value.SupportCounts.TopologyTransitionCount}|{value.SupportCounts.SliderToSliderTransitionCount}";

    private static string DenominatorSignature(TechV2Phase222DenominatorExample value) =>
        $"{value.Map}|{value.Difficulty}|{value.Ratio}|{value.Numerator}|{value.Denominator}|{value.Before:F8}|{value.After:F8}";

    private static string ContaminationSignature(TechV2Phase222ContaminationFinding value) =>
        $"{value.ReviewId}|{value.Current:F8}|{value.PrevCurr:F8}|{value.Delta:F8}|{value.Assessment}";
}
