using BeatInsight.Services.Persistence;
using Microsoft.Data.Sqlite;
using System.IO;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechV2CombinedSupportAwareLocalContextAuditTests(ITestOutputHelper output)
{
    [Fact]
    public void Run_ProducesCombinedAuditReport()
    {
        string source = MlDatasetSampleRepository.DefaultDatabasePath;
        if (!File.Exists(source))
        {
            output.WriteLine("Local dataset cache unavailable; phase 2.21 audit skipped safely.");
            return;
        }

        string copy = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase221-{Guid.NewGuid():N}.db");
        File.Copy(source, copy);

        TechV2Phase221AuditReport report;
        try
        {
            report = TechV2CombinedSupportAwareLocalContextAudit.Run(copy);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(copy);
        }

        output.WriteLine(TechV2CombinedSupportAwareLocalContextAudit.FormatReport(report));

        Assert.Equal(4, report.Variants.Count);
        Assert.Contains(report.Variants, variant => variant.Variant == TechV2Phase221Variant.A_Baseline);
        Assert.Contains(report.Variants, variant => variant.Variant == TechV2Phase221Variant.B_SupportAwareOnly);
        Assert.Contains(report.Variants, variant => variant.Variant == TechV2Phase221Variant.C_LocalAggregationOnly);
        Assert.Contains(report.Variants, variant => variant.Variant == TechV2Phase221Variant.D_SupportAwarePlusLocalAggregation);

        Assert.NotEmpty(report.CaseScores);
        Assert.Contains(report.CaseScores, c => c.CaseId == "Exit R5");
        Assert.Contains(report.CaseScores, c => c.CaseId == "WorldEndsNow R1");
        Assert.Contains(report.CaseScores, c => c.CaseId == "TrustInYou R1");
        Assert.Contains(report.CaseScores, c => c.CaseId == "Inferno R1");
        Assert.Contains(report.CaseScores, c => c.CaseId == "TaoHuaXiao R1");
        Assert.Contains(report.CaseScores, c => c.CaseId == "HappppySong R1");
        Assert.Contains(report.CaseScores, c => c.CaseId == "HappppySong R2");

        Assert.False(string.IsNullOrWhiteSpace(report.PositiveImprovement));
        Assert.False(string.IsNullOrWhiteSpace(report.NegativeImprovement));
        Assert.False(string.IsNullOrWhiteSpace(report.GuardrailImpact));
        Assert.False(string.IsNullOrWhiteSpace(report.ContrastImprovement));
        Assert.False(string.IsNullOrWhiteSpace(report.RuntimeConsideration));
    }

    [Fact]
    public void Run_IsDeterministic_ForVariantsAndCaseScores()
    {
        string source = MlDatasetSampleRepository.DefaultDatabasePath;
        if (!File.Exists(source))
            return;

        string copyA = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase221-A-{Guid.NewGuid():N}.db");
        string copyB = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase221-B-{Guid.NewGuid():N}.db");
        File.Copy(source, copyA);
        File.Copy(source, copyB);

        TechV2Phase221AuditReport first;
        TechV2Phase221AuditReport second;
        try
        {
            first = TechV2CombinedSupportAwareLocalContextAudit.Run(copyA);
            second = TechV2CombinedSupportAwareLocalContextAudit.Run(copyB);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(copyA);
            File.Delete(copyB);
        }

        Assert.Equal(
            first.Variants.Select(VariantSignature),
            second.Variants.Select(VariantSignature));

        Assert.Equal(
            first.CaseScores.Select(CaseSignature),
            second.CaseScores.Select(CaseSignature));

        Assert.Equal(first.CombinationEffect, second.CombinationEffect);
        Assert.Equal(first.BestCombinedVariant, second.BestCombinedVariant);
        Assert.Equal(first.PositiveImprovement, second.PositiveImprovement);
        Assert.Equal(first.NegativeImprovement, second.NegativeImprovement);
        Assert.Equal(first.GuardrailImpact, second.GuardrailImpact);
        Assert.Equal(first.ContrastImprovement, second.ContrastImprovement);
        Assert.Equal(first.RuntimeConsideration, second.RuntimeConsideration);
    }

    private static string VariantSignature(TechV2Phase221VariantSummary variant) =>
        $"{variant.Variant}|{variant.PositiveCasesImproved}|{variant.NegativeCasesImproved}|{variant.TechVsNonTechContrast:F8}|{variant.KiraDelta:F8}|{variant.FrozenDelta:F8}|{variant.GloriousDelta:F8}";

    private static string CaseSignature(TechV2Phase221CaseScores score) =>
        $"{score.CaseId}|{score.Role}|{score.BaselineA:F8}|{score.SupportAwareB:F8}|{score.LocalAggregationC:F8}|{score.CombinedD:F8}";
}
