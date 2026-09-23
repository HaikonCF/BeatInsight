using Xunit;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Phase 2.9b test-only runner. It corrects the Phase 2.9 calibration
/// corpus by excluding maps where BeatInsight's own existing
/// techSecondaryEligible gate (TechPresence &gt;= 0.08 &amp;&amp;
/// TechIntensity &gt;= 28.0) was never reachable, so their non-Tech
/// Primary/Secondary is a difficulty artifact rather than real evidence.
/// No new threshold is introduced, GameplayIdentity is never touched, and
/// G3 remains a purely offline simulation.
/// </summary>
public sealed class TechV2StrongTechGuardCalibrationCorpusFixTests
{
    private readonly ITestOutputHelper output;

    public TechV2StrongTechGuardCalibrationCorpusFixTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void RunEligibilityFiltered_ExcludesLowDifficultyIdentityBiasFromCalibrationCorpus()
    {
        TechV2StrongTechCalibrationCorpusReport report =
            TechV2StrongTechGuardCalibrationAudit.RunEligibilityFiltered();

        output.WriteLine(TechV2StrongTechGuardCalibrationAudit.FormatCorpusReport(report));

        Assert.True(report.GroupARawCount <= report.MapsScanned);
        Assert.Equal(
            report.GroupARawCount,
            report.LowDifficultyIdentityBiasExcludedCount
                + report.EligibleAfterApplicabilityFilterCount);

        Assert.True(
            report.ShortlistG3WouldReject.Count
                <= TechV2StrongTechGuardCalibrationAudit.CorpusShortlistSize);
        Assert.True(
            report.ShortlistG3WouldKeep.Count
                <= TechV2StrongTechGuardCalibrationAudit.CorpusShortlistSize);

        Assert.All(
            report.ShortlistG3WouldReject,
            entry => Assert.True(entry.Map.AffectedByG3));
        Assert.All(
            report.ShortlistG3WouldKeep,
            entry => Assert.False(entry.Map.AffectedByG3));

        // Every entry that made it past the filter must actually satisfy
        // BeatInsight's own existing techSecondaryEligible gate: this is
        // the corpus fix itself, not a new arbitrary threshold.
        Assert.All(
            report.ShortlistG3WouldReject.Concat(report.ShortlistG3WouldKeep),
            entry => Assert.True(entry.TechPresence >= 0.08 && entry.TechIntensity >= 28.0));
    }
}
