using BeatInsight.Services.Persistence;
using Microsoft.Data.Sqlite;
using System.IO;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechV2ZeroSupportSmoothingCalibrationAuditTests
{
    private readonly ITestOutputHelper output;

    public TechV2ZeroSupportSmoothingCalibrationAuditTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void Run_ZeroSupportPolicies_AreBoundedAndLeaveDecisionsUntouched()
    {
        string source = MlDatasetSampleRepository.DefaultDatabasePath;
        if (!File.Exists(source))
        {
            output.WriteLine("Local dataset cache unavailable; Phase 2.25 audit skipped safely.");
            return;
        }

        string copy = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase225-{Guid.NewGuid():N}.db");
        File.Copy(source, copy);

        TechV2Phase225AuditReport report;
        try
        {
            report = TechV2ZeroSupportSmoothingCalibrationAudit.Run(copy);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(copy);
        }

        output.WriteLine(TechV2ZeroSupportSmoothingCalibrationAudit.FormatReport(report));

        Assert.True(report.AllDecisionsUnchanged);
        Assert.Equal(4, report.Distributions.Count);
        Assert.All(report.Distributions, distribution =>
        {
            Assert.True(distribution.AllDecisionsUnchanged);
            Assert.Equal(0, distribution.ChangedDecisionCount);
            Assert.True(distribution.Count > 0);
            Assert.InRange(distribution.PercentIncreased, 0, 100);
            Assert.InRange(distribution.PercentDecreased, 0, 100);
        });

        Assert.NotEmpty(report.Extremes);
        Assert.Contains(report.Extremes, item => TechV2ZeroSupportSmoothingCalibrationAudit.MatchesToken(item.Map, "SANTA SAN"));
        Assert.Contains(report.Extremes, item => TechV2ZeroSupportSmoothingCalibrationAudit.MatchesToken(item.Map, "AliVe"));
        Assert.Contains(report.Extremes, item => TechV2ZeroSupportSmoothingCalibrationAudit.MatchesToken(item.Map, "Ringing Bloom"));
        Assert.Contains(report.Extremes, item => TechV2ZeroSupportSmoothingCalibrationAudit.MatchesToken(item.Map, "Koinu"));
        Assert.Contains(report.Extremes, item => TechV2ZeroSupportSmoothingCalibrationAudit.MatchesToken(item.Map, "Don't Let Go"));
        Assert.Contains(report.Extremes, item => TechV2ZeroSupportSmoothingCalibrationAudit.MatchesToken(item.Map, "Start Again"));
        Assert.Contains(report.Extremes, item => TechV2ZeroSupportSmoothingCalibrationAudit.MatchesToken(item.Map, "Yuuki wa Doko ni?"));
        Assert.Contains(report.Extremes, item => TechV2ZeroSupportSmoothingCalibrationAudit.MatchesToken(item.Map, "Deai Machi"));
        Assert.Contains(report.Extremes, item => TechV2ZeroSupportSmoothingCalibrationAudit.MatchesToken(item.Map, "More-more LOVERS!!"));

        Assert.All(report.Extremes, item =>
        {
            Assert.Equal(item.Corrected[TechV2ZeroSupportVariant.ZERO_A], item.Corrected[TechV2ZeroSupportVariant.ZERO_B], 10);
            Assert.Equal(item.Corrected[TechV2ZeroSupportVariant.ZERO_A], item.Corrected[TechV2ZeroSupportVariant.ZERO_C], 10);
            foreach (TechV2ZeroSupportVariant variant in Enum.GetValues<TechV2ZeroSupportVariant>())
            {
                Assert.InRange(item.Corrected[variant], 0, 1);
                double reconstructed = item.Attribution[variant].DeltaFromSupportAware
                    + item.Attribution[variant].DeltaFromPrevContext
                    + item.Attribution[variant].DeltaFromTopology
                    + item.Attribution[variant].DeltaFromOther;
                Assert.InRange(Math.Abs(reconstructed - item.Attribution[variant].TotalDelta), 0, 1e-8);
                Assert.All(item.Ratios[variant], ratio =>
                {
                    Assert.InRange(ratio.EffectiveRatio, 0, 1);
                    Assert.InRange(ratio.SupportFactor, 0, 1);
                    if (variant == TechV2ZeroSupportVariant.ZERO_D && ratio.Denominator <= 0)
                        Assert.Equal(0, ratio.EffectiveRatio);
                });
            }
        });

        Assert.NotEmpty(report.HumanImpact);
        Assert.Contains(report.HumanImpact, item => item.CaseId == "Exit R5");
        Assert.Contains(report.HumanImpact, item => item.CaseId == "WorldEndsNow R1");
        Assert.Contains(report.HumanImpact, item => item.CaseId == "TrustInYou R1");
        Assert.Contains(report.HumanImpact, item => item.CaseId == "Inferno R1");
        Assert.Contains(report.HumanImpact, item => item.CaseId == "TaoHuaXiao R1");
        Assert.Contains(report.HumanImpact, item => item.CaseId == "HappppySong R1");
        Assert.Contains(report.HumanImpact, item => item.CaseId == "HappppySong R2");
        Assert.Contains(report.HumanImpact, item => item.CaseId == "Kira R1");
        Assert.Contains(report.HumanImpact, item => item.CaseId == "Frozen R1");
        Assert.Contains(report.HumanImpact, item => item.CaseId == "GloriousDays R1");
    }

    [Fact]
    public void Run_ZeroSupportPolicies_IsDeterministic()
    {
        string source = MlDatasetSampleRepository.DefaultDatabasePath;
        if (!File.Exists(source))
            return;

        string copyA = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase225-A-{Guid.NewGuid():N}.db");
        string copyB = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase225-B-{Guid.NewGuid():N}.db");
        File.Copy(source, copyA);
        File.Copy(source, copyB);

        TechV2Phase225AuditReport first;
        TechV2Phase225AuditReport second;
        try
        {
            // Determinism is independent of corpus size. Keep the full 1,000-map
            // calibration pass in the attribution test; use the same fixed 64-map
            // prefix twice here so the test remains practical for local iteration.
            first = TechV2ZeroSupportSmoothingCalibrationAudit.Run(copyA, maxMaps: 64);
            second = TechV2ZeroSupportSmoothingCalibrationAudit.Run(copyB, maxMaps: 64);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(copyA);
            File.Delete(copyB);
        }

        Assert.Equal(Signature(first), Signature(second));
    }

    private static string Signature(TechV2Phase225AuditReport report) => string.Join("\n", report.Distributions
        .OrderBy(item => item.Variant)
        .Select(item => $"{item.Variant}|{item.Count}|{item.AverageDelta:F12}|{item.MedianDelta:F12}|{item.MaxAbsDelta:F12}|{item.Over015}|{item.Over020}|{item.Over030}"))
        + "\n" + string.Join("\n", report.Extremes.Select(item =>
            $"{item.Map}|{item.Difficulty}|{item.Timestamp:F3}|{string.Join(',', Enum.GetValues<TechV2ZeroSupportVariant>().Select(v => item.Corrected[v].ToString("F12")))}"));
}
