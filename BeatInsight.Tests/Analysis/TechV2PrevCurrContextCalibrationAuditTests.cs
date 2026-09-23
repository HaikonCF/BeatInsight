using BeatInsight.Services.Persistence;
using Microsoft.Data.Sqlite;
using System.IO;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechV2PrevCurrContextCalibrationAuditTests
{
    private readonly ITestOutputHelper output;

    public TechV2PrevCurrContextCalibrationAuditTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void Run_ThreePrevCurrPolicies_LeaveDecisionsUntouched()
    {
        string source = MlDatasetSampleRepository.DefaultDatabasePath;
        if (!File.Exists(source))
        {
            output.WriteLine("Local dataset cache unavailable; Phase 2.26 audit skipped safely.");
            return;
        }

        string copy = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase226-{Guid.NewGuid():N}.db");
        File.Copy(source, copy);
        TechV2Phase226AuditReport report;
        try { report = TechV2PrevCurrContextCalibrationAudit.Run(copy); }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(copy);
        }

        output.WriteLine(TechV2PrevCurrContextCalibrationAudit.FormatReport(report));
        Assert.True(report.AllDecisionsUnchanged);
        Assert.Equal(3, report.Summaries.Count);
        Assert.All(report.Summaries, item => Assert.Equal(0, item.DecisionsChanged));
        Assert.Contains(report.Extremes, item => TechV2PrevCurrContextCalibrationAudit.MatchesToken(item.Map, "SANTA SAN"));
        Assert.Contains(report.Extremes, item => TechV2PrevCurrContextCalibrationAudit.MatchesToken(item.Map, "AliVe"));
        Assert.Contains(report.Extremes, item => TechV2PrevCurrContextCalibrationAudit.MatchesToken(item.Map, "Ringing Bloom"));
        Assert.Contains(report.Extremes, item => TechV2PrevCurrContextCalibrationAudit.MatchesToken(item.Map, "Koinu"));
        Assert.Contains(report.Extremes, item => TechV2PrevCurrContextCalibrationAudit.MatchesToken(item.Map, "Don't Let Go"));
        Assert.Contains(report.Extremes, item => TechV2PrevCurrContextCalibrationAudit.MatchesToken(item.Map, "Start Again"));
        Assert.Contains(report.Extremes, item => TechV2PrevCurrContextCalibrationAudit.MatchesToken(item.Map, "Yuuki wa Doko ni?"));
        Assert.Contains(report.Extremes, item => TechV2PrevCurrContextCalibrationAudit.MatchesToken(item.Map, "Deai Machi"));
        Assert.Contains(report.Extremes, item => TechV2PrevCurrContextCalibrationAudit.MatchesToken(item.Map, "More-more LOVERS!!"));
        Assert.Equal(10, report.HumanCases.Count);
        Assert.All(report.Extremes, item =>
        {
            Assert.InRange(item.CurrentSupportFactor, 0, 1);
            Assert.All(item.Candidate.Values, value => Assert.InRange(value, 0, 1));
        });
    }

    [Fact]
    public void Run_IsDeterministic_OnFixedFastSample()
    {
        string source = MlDatasetSampleRepository.DefaultDatabasePath;
        if (!File.Exists(source)) return;

        string copyA = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase226-A-{Guid.NewGuid():N}.db");
        string copyB = Path.Combine(Path.GetTempPath(), $"BeatInsight-Phase226-B-{Guid.NewGuid():N}.db");
        File.Copy(source, copyA);
        File.Copy(source, copyB);
        TechV2Phase226AuditReport first;
        TechV2Phase226AuditReport second;
        try
        {
            first = TechV2PrevCurrContextCalibrationAudit.Run(copyA, maxGlobalMaps: 24);
            second = TechV2PrevCurrContextCalibrationAudit.Run(copyB, maxGlobalMaps: 24);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(copyA);
            File.Delete(copyB);
        }

        Assert.Equal(Signature(first), Signature(second));
    }

    private static string Signature(TechV2Phase226AuditReport report) => string.Join("\n", report.Summaries
        .OrderBy(item => item.Variant)
        .Select(item => $"{item.Variant}|{item.MaxAbsDelta:F12}|{item.AverageDelta:F12}|{item.HumanGainsKept}|{item.Over015}|{item.Over020}"));
}
