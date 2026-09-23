using Xunit;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Phase 2.9 test-only runner. It reuses the Phase 2.7 local discovery
/// mechanism and the Phase 2.8 G3 guard candidate to produce a larger,
/// offline-only human review shortlist. It asserts only the calibration
/// tool's own bookkeeping (counts add up, shortlists are bounded) and never
/// asserts what the "correct" Tech V2 classification should be for any map:
/// the current BeatInsight classification is not treated as ground truth,
/// and no accuracy number is computed here.
/// </summary>
public sealed class TechV2StrongTechGuardCalibrationTests
{
    private readonly ITestOutputHelper output;

    public TechV2StrongTechGuardCalibrationTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void Run_ProducesBoundedOfflineG3ShortlistsFromRealLocalLibrary()
    {
        TechV2StrongTechGuardCalibrationReport report =
            TechV2StrongTechGuardCalibrationAudit.Run();

        output.WriteLine(TechV2StrongTechGuardCalibrationAudit.FormatReport(report));

        Assert.True(report.GroupAMapCount <= report.MapsScanned);
        Assert.Equal(report.Maps.Count, report.GroupAMapCount);
        Assert.Equal(
            report.G3RejectedWindows + report.G3KeptWindows,
            report.TotalStrongTechWindows);
        Assert.Equal(
            report.TotalStrongTechWindows,
            report.Maps.Sum(map => map.Windows.Count));
        Assert.Equal(
            report.MapsAffectedByG3,
            report.Maps.Count(map => map.AffectedByG3));

        Assert.True(
            report.ShortlistG3WouldReject.Count
                <= TechV2StrongTechGuardCalibrationAudit.ShortlistSize);
        Assert.True(
            report.ShortlistG3WouldKeep.Count
                <= TechV2StrongTechGuardCalibrationAudit.ShortlistSize);
        Assert.All(report.ShortlistG3WouldReject, map => Assert.True(map.AffectedByG3));
        Assert.All(report.ShortlistG3WouldKeep, map => Assert.False(map.AffectedByG3));

        // Every window in the distribution tables must land in exactly one
        // Movement+Slider/other-pair bucket; the two distributions must sum
        // to the total window count.
        int movementSliderTotal = report.PivotDistributionMovementSlider.Values.Sum();
        int otherPairTotal = report.PivotDistributionOtherFamilyPairs.Values.Sum();
        Assert.Equal(report.TotalStrongTechWindows, movementSliderTotal + otherPairTotal);

        // The G3 guard predicate itself is Phase 2.8's, reused as-is: no
        // hardcoded expectation is asserted for any specific reference map
        // here, only that the guard produced *some* observed outcome for
        // whichever reference maps were resolvable on this machine.
        Assert.NotNull(report.ReferenceSanityCheck);
    }
}
