using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Phase 2.6 diagnostic-only pressure pack.
/// It validates determinism and non-mutation invariants without changing runtime rules.
/// </summary>
public sealed class TechV2NegativePressureAuditTests(ITestOutputHelper output)
{
    [Fact]
    public void CorpusDiscovery_AndCategoryAssignment_AreDeterministic()
    {
        TechV2NegativePressureReport first = TechV2NegativePressureAudit.Analyze();
        TechV2NegativePressureReport second = TechV2NegativePressureAudit.Analyze();

        Assert.Equal(first.MapsDiscovered, second.MapsDiscovered);
        Assert.Equal(
            first.Maps.Select(map => (map.FixtureName, map.ReferenceCategory, map.Disposition, map.Family)),
            second.Maps.Select(map => (map.FixtureName, map.ReferenceCategory, map.Disposition, map.Family)));

        Assert.All(first.Maps, map =>
        {
            Assert.InRange(map.PresenceBeforeGuard, 0, 100);
            Assert.InRange(map.PresenceAfterGuard, 0, 100);
            Assert.All(map.CandidateRuns, run =>
            {
                Assert.True(double.IsFinite(run.AverageGeometry));
                Assert.True(double.IsFinite(run.AverageMovement));
                Assert.True(double.IsFinite(run.AverageSlider));
                Assert.True(double.IsFinite(run.AverageMotif));
            });
        });
    }

    [Fact]
    public void HardNegativeDetection_AndReportOrdering_AreDeterministic()
    {
        TechV2NegativePressureReport first = TechV2NegativePressureAudit.Analyze();
        TechV2NegativePressureReport second = TechV2NegativePressureAudit.Analyze();

        Assert.Equal(
            first.Maps.Select(MapOrderKey),
            second.Maps.Select(MapOrderKey));

        Assert.Equal(
            first.Maps.Where(map => map.IsHardNegativeCandidate).Select(map => map.FixtureName),
            second.Maps.Where(map => map.IsHardNegativeCandidate).Select(map => map.FixtureName));

        Assert.Equal(
            first.TopHardNegatives.Select(map => map.FixtureName),
            second.TopHardNegatives.Select(map => map.FixtureName));
    }

    [Fact]
    public void ThresholdSimulation_DoesNotMutateCurrentGuardOutput()
    {
        TechV2NegativePressureReport report = TechV2NegativePressureAudit.Analyze();
        TechV2NegativePressureReport rerun = TechV2NegativePressureAudit.Analyze();

        Assert.Equal(
            report.Maps.Select(map => (map.FixtureName, map.PresenceAfterGuard, map.SustainedTechAfterGuard)),
            rerun.Maps.Select(map => (map.FixtureName, map.PresenceAfterGuard, map.SustainedTechAfterGuard)));

        Assert.Equal(
            report.ThresholdTradeoff.Select(item =>
                (item.Threshold,
                    item.NonTechRunsKept,
                    item.NonTechRunsRejected,
                    item.NonTechMapsWithPresence,
                    item.MaxNonTechPresence,
                    item.AverageNonTechPresence,
                    item.TechRunsKept,
                    item.TechRunsRejected,
                    item.ExitPresence,
                    item.KiraPresence,
                    item.FrozenPresence)),
            rerun.ThresholdTradeoff.Select(item =>
                (item.Threshold,
                    item.NonTechRunsKept,
                    item.NonTechRunsRejected,
                    item.NonTechMapsWithPresence,
                    item.MaxNonTechPresence,
                    item.AverageNonTechPresence,
                    item.TechRunsKept,
                    item.TechRunsRejected,
                    item.ExitPresence,
                    item.KiraPresence,
                    item.FrozenPresence)));
    }

    [Fact]
    public void Report_CanBeProduced_ForManualCalibrationReview()
    {
        TechV2NegativePressureReport report = TechV2NegativePressureAudit.Analyze();
        string text = TechV2NegativePressureAudit.FormatReport(report);

        Assert.Contains("PHASE 2.6 NEGATIVE PRESSURE PACK", text, StringComparison.Ordinal);
        Assert.Contains("THRESHOLD TRADEOFF TABLE", text, StringComparison.Ordinal);
        output.WriteLine(text);
    }

    private static string MapOrderKey(TechV2NegativePressureMapReport map) =>
        $"{map.ReferenceCategory}|{map.Map}|{map.Difficulty}|{map.FixtureName}";
}