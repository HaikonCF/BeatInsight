using BeatInsight.Services.Persistence;
using Microsoft.Data.Sqlite;
using System.IO;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechV2SupportAwareAggregationAuditTests(ITestOutputHelper output)
{
    [Fact]
    public void Run_ProducesPhase220Report_WithoutRuntimeMutation()
    {
        string source = MlDatasetSampleRepository.DefaultDatabasePath;
        if (!File.Exists(source))
        {
            output.WriteLine("Local dataset cache unavailable; phase 2.20 audit skipped safely.");
            return;
        }

        string copy = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase220-{Guid.NewGuid():N}.db");
        File.Copy(source, copy);
        TechV2Phase220AuditReport report;
        try
        {
            report = TechV2SupportAwareAggregationAudit.Run(copy);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(copy);
        }

        output.WriteLine(TechV2SupportAwareAggregationAudit.FormatReport(report));

        Assert.NotEmpty(report.Cases);
        Assert.Equal(3, report.SupportAwareCandidates.Count);
        Assert.Equal(3, report.LocalAggregationCandidates.Count);
        Assert.Equal(3, report.PriorEffects.Count);

        Assert.Contains(report.Cases, c => c.CaseId == "Exit R5");
        Assert.Contains(report.Cases, c => c.CaseId == "WorldEndsNow R1");
        Assert.Contains(report.Cases, c => c.CaseId == "TrustInYou R1");
        Assert.Contains(report.Cases, c => c.CaseId == "TaoHuaXiao R1");
        Assert.Contains(report.Cases, c => c.CaseId == "TrustInYou R2");
        Assert.Contains(report.Cases, c => c.CaseId == "Inferno R1");
        Assert.Contains(report.Cases, c => c.CaseId == "Kira R1");
        Assert.Contains(report.Cases, c => c.CaseId == "Frozen R1");
        Assert.Contains(report.Cases, c => c.CaseId == "GloriousDays R1");

        Assert.All(report.Cases, c =>
        {
            Assert.InRange(c.RatioBaseline, 0, 1);
            Assert.InRange(c.AggregationBaselineCurrentOnly, 0, 1);
        });

        Assert.Contains("Candidate | Positive cases improved | Negative cases improved", report.CandidateTable);
        Assert.Contains("Les vrais Tech sous-évalués remontent", report.MainAnswers);
        Assert.Contains("Les faux positifs surévalués descendent", report.MainAnswers);

        Assert.NotEmpty(report.SliderHeavyFindings);
        Assert.Contains(report.SliderHeavyFindings, line => line.Contains("Exit R5", StringComparison.Ordinal));
        Assert.Contains(report.SliderHeavyFindings, line => line.Contains("WorldEndsNow R1", StringComparison.Ordinal));
        Assert.Contains(report.SliderHeavyFindings, line => line.Contains("GloriousDays R1", StringComparison.Ordinal));
        Assert.Contains(report.SliderHeavyFindings, line => line.Contains("TrustInYou R1", StringComparison.Ordinal));

        Assert.NotEmpty(report.FalsePositiveDenominatorFindings);
        Assert.Contains(report.FalsePositiveDenominatorFindings, line => line.Contains("Inferno R1", StringComparison.Ordinal));
        Assert.Contains(report.FalsePositiveDenominatorFindings, line => line.Contains("TaoHuaXiao R1", StringComparison.Ordinal));
        Assert.Contains(report.FalsePositiveDenominatorFindings, line => line.Contains("HappppySong R1", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_IsDeterministic_ForCandidateTableAndImpacts()
    {
        string source = MlDatasetSampleRepository.DefaultDatabasePath;
        if (!File.Exists(source))
            return;

        string copyA = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase220-A-{Guid.NewGuid():N}.db");
        string copyB = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase220-B-{Guid.NewGuid():N}.db");
        File.Copy(source, copyA);
        File.Copy(source, copyB);

        TechV2Phase220AuditReport first;
        TechV2Phase220AuditReport second;
        try
        {
            first = TechV2SupportAwareAggregationAudit.Run(copyA);
            second = TechV2SupportAwareAggregationAudit.Run(copyB);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(copyA);
            File.Delete(copyB);
        }

        Assert.Equal(
            first.Cases.Select(Signature),
            second.Cases.Select(Signature));

        Assert.Equal(
            first.SupportAwareCandidates.Select(SummarySignature),
            second.SupportAwareCandidates.Select(SummarySignature));

        Assert.Equal(
            first.LocalAggregationCandidates.Select(SummarySignature),
            second.LocalAggregationCandidates.Select(SummarySignature));

        Assert.Equal(
            first.PriorEffects.Select(effect =>
                $"{effect.Prior}|{effect.PositiveMean:F8}|{effect.NegativeMean:F8}|{effect.Contrast:F8}|{effect.Interpretation}"),
            second.PriorEffects.Select(effect =>
                $"{effect.Prior}|{effect.PositiveMean:F8}|{effect.NegativeMean:F8}|{effect.Contrast:F8}|{effect.Interpretation}"));

        Assert.Equal(first.CandidateTable, second.CandidateTable);
        Assert.Equal(first.MainAnswers, second.MainAnswers);
        Assert.Equal(first.SliderHeavyFindings, second.SliderHeavyFindings);
        Assert.Equal(first.FalsePositiveDenominatorFindings, second.FalsePositiveDenominatorFindings);
    }

    private static string Signature(TechV2Phase220CaseBaseline item) =>
        $"{item.CaseId}|{item.Role}|{item.HumanLabel}|{item.RatioBaseline:F8}|{item.AggregationBaselineCurrentOnly:F8}|{item.HasSingleSupportDenominator}";

    private static string SummarySignature(TechV2Phase220CandidateSummary item) =>
        $"{item.Candidate}|{item.Kind}|{item.PositiveCasesImproved}|{item.NegativeCasesImproved}|{item.KiraImpact}|{item.FrozenImpact}|{item.GloriousImpact}|{item.ContrastBaseline:F8}|{item.ContrastCandidate:F8}";
}
