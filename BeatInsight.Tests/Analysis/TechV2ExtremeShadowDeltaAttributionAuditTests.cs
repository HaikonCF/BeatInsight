using BeatInsight.Services.Persistence;
using Microsoft.Data.Sqlite;
using System.IO;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechV2ExtremeShadowDeltaAttributionAuditTests
{
    private readonly ITestOutputHelper output;

    public TechV2ExtremeShadowDeltaAttributionAuditTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void Run_ExplainsExtremeShadowDeltas_WithAttribution()
    {
        string source = MlDatasetSampleRepository.DefaultDatabasePath;
        if (!File.Exists(source))
        {
            output.WriteLine("Local dataset cache unavailable; phase 2.24 audit skipped safely.");
            return;
        }

        string copy = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase224-{Guid.NewGuid():N}.db");
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
        Assert.Contains(report.ExtremeWindows, item => TechV2ExtremeShadowDeltaAttributionAudit.MatchesToken(item.Map, "SANTA SAN"));
        Assert.Contains(report.ExtremeWindows, item => TechV2ExtremeShadowDeltaAttributionAudit.MatchesToken(item.Map, "AliVe"));
        Assert.Contains(report.ExtremeWindows, item => TechV2ExtremeShadowDeltaAttributionAudit.MatchesToken(item.Map, "Ringing Bloom"));
        Assert.Contains(report.ExtremeWindows, item => TechV2ExtremeShadowDeltaAttributionAudit.MatchesToken(item.Map, "Koinu"));
        Assert.Contains(report.ExtremeWindows, item => TechV2ExtremeShadowDeltaAttributionAudit.MatchesToken(item.Map, "Don't Let Go"));
        Assert.Contains(report.ExtremeWindows, item => TechV2ExtremeShadowDeltaAttributionAudit.MatchesToken(item.Map, "Start Again"));
        Assert.Contains(report.ExtremeWindows, item => TechV2ExtremeShadowDeltaAttributionAudit.MatchesToken(item.Map, "Yuuki wa Doko ni?"));
        Assert.Contains(report.ExtremeWindows, item => TechV2ExtremeShadowDeltaAttributionAudit.MatchesToken(item.Map, "Deai Machi"));
        Assert.Contains(report.ExtremeWindows, item => TechV2ExtremeShadowDeltaAttributionAudit.MatchesToken(item.Map, "More-more LOVERS!!"));

        Assert.All(report.ExtremeWindows, item =>
        {
            Assert.NotEmpty(item.CurrentTerms);
            Assert.NotEmpty(item.Ratios);
            Assert.NotEmpty(item.PrevCurr);
            Assert.InRange(item.Topology.TopologyDenominator, 0, int.MaxValue);
            Assert.Equal(
                item.Topology.TopologyDenominator,
                item.Topology.SliderToSliderCount
                + item.Topology.SliderToCircleCount
                + item.Topology.CircleToSliderCount
                + item.Topology.CircleToCircleCount);

            double currentReconstructed = item.CurrentTerms.Sum(term => term.WeightedContribution);
            Assert.InRange(Math.Abs(currentReconstructed - item.CurrentValue), 0, 1e-6);

            double reconstructed = item.Attribution.DeltaFromSupportAware
                + item.Attribution.DeltaFromPrevContext
                + item.Attribution.DeltaFromTopology
                + item.Attribution.DeltaFromOther;
            Assert.InRange(
                Math.Abs(reconstructed - item.Attribution.TotalDelta),
                0,
                1e-6);
        });

        Assert.NotEmpty(report.UsefulCases);
        Assert.Contains(report.UsefulCases, item => item.CaseId == "Exit R5");
        Assert.Contains(report.UsefulCases, item => item.CaseId == "WorldEndsNow R1");
        Assert.Contains(report.UsefulCases, item => item.CaseId == "TrustInYou R1");
        Assert.Contains(report.UsefulCases, item => item.CaseId == "Kira R1");
        Assert.Contains(report.UsefulCases, item => item.CaseId == "Frozen R1");
        Assert.Contains(report.UsefulCases, item => item.CaseId == "GloriousDays R1");
    }

    [Fact]
    public void Run_IsDeterministic_ForAttributionAndCause()
    {
        string source = MlDatasetSampleRepository.DefaultDatabasePath;
        if (!File.Exists(source))
            return;

        string copyA = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase224-A-{Guid.NewGuid():N}.db");
        string copyB = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase224-B-{Guid.NewGuid():N}.db");
        File.Copy(source, copyA);
        File.Copy(source, copyB);

        TechV2Phase224AuditReport first;
        TechV2Phase224AuditReport second;
        try
        {
            first = TechV2ExtremeShadowDeltaAttributionAudit.Run(copyA);
            second = TechV2ExtremeShadowDeltaAttributionAudit.Run(copyB);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(copyA);
            File.Delete(copyB);
        }

        Assert.Equal(
            first.ExtremeWindows.Select(ExtremeSignature),
            second.ExtremeWindows.Select(ExtremeSignature));

        Assert.Equal(
            first.UsefulCases.Select(item =>
                $"{item.CaseId}|{item.MeanTotalDelta:F10}|{item.MeanSupportAwareDelta:F10}|{item.MeanPrevContextDelta:F10}|{item.MeanTopologyDelta:F10}|{item.MeanOtherDelta:F10}"),
            second.UsefulCases.Select(item =>
                $"{item.CaseId}|{item.MeanTotalDelta:F10}|{item.MeanSupportAwareDelta:F10}|{item.MeanPrevContextDelta:F10}|{item.MeanTopologyDelta:F10}|{item.MeanOtherDelta:F10}"));

        Assert.Equal(first.DominantCause, second.DominantCause);
        Assert.Equal(first.AllDecisionsUnchanged, second.AllDecisionsUnchanged);
    }

    private static string ExtremeSignature(TechV2Phase224ExtremeWindow item) =>
        $"{item.Map}|{item.Difficulty}|{item.Timestamp:F3}|{item.CurrentValue:F8}|{item.CorrectedValue:F8}|{item.Attribution.TotalDelta:F8}|{item.Attribution.DeltaFromSupportAware:F8}|{item.Attribution.DeltaFromPrevContext:F8}|{item.Attribution.DeltaFromTopology:F8}|{item.Attribution.DeltaFromOther:F8}";
}
