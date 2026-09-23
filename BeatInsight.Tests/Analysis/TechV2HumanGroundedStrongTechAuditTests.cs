using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

/// <summary>Phase 2.9C runner: labels are explicit human references only.</summary>
public sealed class TechV2HumanGroundedStrongTechAuditTests(ITestOutputHelper output)
{
    [Fact]
    public void Run_ReportsHumanGroundedStrongTechForensicsWithoutRuntimeMutation()
    {
        TechV2HumanGroundedAuditReport report = TechV2HumanGroundedStrongTechAudit.Run();

        output.WriteLine(TechV2HumanGroundedStrongTechAudit.FormatReport(report));

        Assert.Equal(TechV2HumanGroundedStrongTechAudit.References.Count, report.Maps.Count);
        Assert.All(report.Maps.Where(map => map.Resolved), map =>
        {
            Assert.InRange(map.Presence.PresencePercent, 0, 100);
            Assert.All(map.StrongTechWindows, window => Assert.True(
                window.Detail.StrongFamilyCount >= 2
                || window.Detail.BestFamilyValue >= .80));
        });
        Assert.Equal(
            report.G3.HumanTechRemoved + report.G3.HumanTechKept,
            report.HumanTechDistribution.WindowCount);
        Assert.Equal(
            report.G3.HumanNonTechRemoved + report.G3.HumanNonTechKept,
            report.HumanNonTechDistribution.WindowCount);
        Assert.All(report.SecondGuardCandidates, candidate =>
        {
            Assert.True(candidate.HumanTechRemoved >= 0);
            Assert.True(candidate.HumanNonTechRemoved >= 0);
        });
    }
}
