using BeatInsight.Services.Persistence;
using Microsoft.Data.Sqlite;
using System.IO;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechV2ZeroSupportLocalContextGuardAuditTests(ITestOutputHelper output)
{
    [Fact]
    public void Run_ReportsVariantComparisonAndExtremeReduction()
    {
        string source = MlDatasetSampleRepository.DefaultDatabasePath;
        if (!File.Exists(source))
        {
            output.WriteLine("Local dataset cache unavailable; phase 2.23 audit skipped safely.");
            return;
        }

        string copy = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase223-{Guid.NewGuid():N}.db");
        File.Copy(source, copy);

        TechV2Phase223AuditReport report;
        try
        {
            report = TechV2ZeroSupportLocalContextGuardAudit.Run(copy);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(copy);
        }

        output.WriteLine(TechV2ZeroSupportLocalContextGuardAudit.FormatReport(report));

        Assert.True(report.AllDecisionsUnchanged);
        Assert.NotEmpty(report.VariantComparison);
        Assert.Equal(3, report.VariantComparison.Count);
        Assert.Contains(report.VariantComparison, item => item.Variant == TechV2Phase223GuardVariant.GCTX_A);
        Assert.Contains(report.VariantComparison, item => item.Variant == TechV2Phase223GuardVariant.GCTX_B);
        Assert.Contains(report.VariantComparison, item => item.Variant == TechV2Phase223GuardVariant.GCTX_C);

        Assert.NotEmpty(report.HumanCorpusImpact);
        Assert.Contains(report.HumanCorpusImpact, item => item.CaseId == "Exit R5");
        Assert.Contains(report.HumanCorpusImpact, item => item.CaseId == "WorldEndsNow R1");
        Assert.Contains(report.HumanCorpusImpact, item => item.CaseId == "TrustInYou R1");
        Assert.Contains(report.HumanCorpusImpact, item => item.CaseId == "Inferno R1");
        Assert.Contains(report.HumanCorpusImpact, item => item.CaseId == "TaoHuaXiao R1");
        Assert.Contains(report.HumanCorpusImpact, item => item.CaseId == "HappppySong R1");
        Assert.Contains(report.HumanCorpusImpact, item => item.CaseId == "HappppySong R2");
        Assert.Contains(report.HumanCorpusImpact, item => item.CaseId == "Kira R1");
        Assert.Contains(report.HumanCorpusImpact, item => item.CaseId == "Frozen R1");
        Assert.Contains(report.HumanCorpusImpact, item => item.CaseId == "GloriousDays R1");

        Assert.NotEmpty(report.ThousandMapDeltaDistribution);
        Assert.Equal(4, report.ThousandMapDeltaDistribution.Count);
        Assert.All(report.ThousandMapDeltaDistribution, item =>
        {
            Assert.True(item.Count >= 0);
            Assert.True(item.MaxAbsoluteDelta >= 0);
        });

        Assert.NotEmpty(report.ExtremeDeltaReduction);
        Assert.Contains(report.ExtremeDeltaReduction, item => item.Map.Contains("SANTA SAN", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.ExtremeDeltaReduction, item => item.Map.Contains("AliVe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.ExtremeDeltaReduction, item => item.Map.Contains("Ringing Bloom", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.ExtremeDeltaReduction, item => item.Map.Contains("Koinu", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.ExtremeDeltaReduction, item => item.Map.Contains("Don't Let Go", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.ExtremeDeltaReduction, item => item.Map.Contains("Start Again", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.ExtremeDeltaReduction, item => item.Map.Contains("Yuuki wa Doko ni?", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.ExtremeDeltaReduction, item => item.Map.Contains("Deai Machi", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.ExtremeDeltaReduction, item => item.Map.Contains("More-more LOVERS!!", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(report.BestGuardCandidate, new[]
        {
            TechV2Phase223GuardVariant.GCTX_A,
            TechV2Phase223GuardVariant.GCTX_B,
            TechV2Phase223GuardVariant.GCTX_C,
        });
    }

    [Fact]
    public void Run_IsDeterministic_ForVariantMetrics()
    {
        string source = MlDatasetSampleRepository.DefaultDatabasePath;
        if (!File.Exists(source))
            return;

        string copyA = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase223-A-{Guid.NewGuid():N}.db");
        string copyB = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase223-B-{Guid.NewGuid():N}.db");
        File.Copy(source, copyA);
        File.Copy(source, copyB);

        TechV2Phase223AuditReport first;
        TechV2Phase223AuditReport second;
        try
        {
            first = TechV2ZeroSupportLocalContextGuardAudit.Run(copyA);
            second = TechV2ZeroSupportLocalContextGuardAudit.Run(copyB);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(copyA);
            File.Delete(copyB);
        }

        Assert.Equal(
            first.VariantComparison.Select(item =>
                $"{item.Variant}|{item.MaxAbsDelta:F8}|{item.ExtremeAbovePoint30Count}|{item.HumanUsefulGainsKept}|{item.HumanUsefulGainsTotal}|{item.Verdict}"),
            second.VariantComparison.Select(item =>
                $"{item.Variant}|{item.MaxAbsDelta:F8}|{item.ExtremeAbovePoint30Count}|{item.HumanUsefulGainsKept}|{item.HumanUsefulGainsTotal}|{item.Verdict}"));

        Assert.Equal(
            first.HumanCorpusImpact.Select(item =>
                $"{item.Variant}|{item.CaseId}|{item.BaselineD:F8}|{item.Guarded:F8}|{item.DeltaFromD:F8}|{item.UsefulGainKept}"),
            second.HumanCorpusImpact.Select(item =>
                $"{item.Variant}|{item.CaseId}|{item.BaselineD:F8}|{item.Guarded:F8}|{item.DeltaFromD:F8}|{item.UsefulGainKept}"));

        Assert.Equal(
            first.ThousandMapDeltaDistribution.Select(item =>
                $"{item.Variant}|{item.Count}|{item.AverageDelta:F10}|{item.MedianDelta:F10}|{item.MinDelta:F10}|{item.MaxDelta:F10}|{item.MaxAbsoluteDelta:F10}|{item.PercentageIncreased:F8}|{item.PercentageDecreased:F8}"),
            second.ThousandMapDeltaDistribution.Select(item =>
                $"{item.Variant}|{item.Count}|{item.AverageDelta:F10}|{item.MedianDelta:F10}|{item.MinDelta:F10}|{item.MaxDelta:F10}|{item.MaxAbsoluteDelta:F10}|{item.PercentageIncreased:F8}|{item.PercentageDecreased:F8}"));

        Assert.Equal(
            first.ExtremeDeltaReduction.Select(item =>
                $"{item.Variant}|{item.Map}|{item.Difficulty}|{item.Timestamp:F4}|{item.BaselineDelta:F8}|{item.GuardDelta:F8}|{item.Reduction:F8}"),
            second.ExtremeDeltaReduction.Select(item =>
                $"{item.Variant}|{item.Map}|{item.Difficulty}|{item.Timestamp:F4}|{item.BaselineDelta:F8}|{item.GuardDelta:F8}|{item.Reduction:F8}"));

        Assert.Equal(first.BestGuardCandidate, second.BestGuardCandidate);
        Assert.Equal(first.Recommendation, second.Recommendation);
        Assert.Equal(first.AllDecisionsUnchanged, second.AllDecisionsUnchanged);
    }

    [Fact]
    public void Run_Phase224_ExtremeDeltaAttributionReport_IsProduced()
    {
        string source = MlDatasetSampleRepository.DefaultDatabasePath;
        if (!File.Exists(source))
        {
            output.WriteLine("Local dataset cache unavailable; phase 2.24 audit skipped safely.");
            return;
        }

        string copy = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase224-Bridge-{Guid.NewGuid():N}.db");
        File.Copy(source, copy);

        TechV2Phase224AuditReport report;
        try
        {
            report = TechV2ExtremeShadowDeltaAttributionAudit.Run(copy);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(copy);
        }

        output.WriteLine(TechV2ExtremeShadowDeltaAttributionAudit.FormatReport(report));

        Assert.True(report.AllDecisionsUnchanged);
        Assert.NotEmpty(report.ExtremeWindows);
        Assert.NotEmpty(report.UsefulCases);
    }
}
