using BeatInsight.Analysis;
using BeatInsight.Models;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Fixture report only: it asserts invariants, not category outcomes.
/// The textual output is intentionally used for Phase 2 calibration review.
/// </summary>
public sealed class TechV2Phase2DiagnosticTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("Exit This Earth's Atmosphere [Primordial Nucleosynthesis].osu")]
    [InlineData("Kira Killer [Mocaotic's Insane].osu")]
    [InlineData("Frozen [Collab Insane].osu")]
    [InlineData("(can you) understand me [uhh].osu")]
    [InlineData("Tower Of Heaven [Extra].osu")]
    public void ReportFixture(string fixtureName)
    {
        Beatmap map = FixtureLoader.Load(fixtureName);
        IReadOnlyList<TechV2ExperimentalWindowResult> windows =
            TechDetectorV2Experimental.Analyze(
                map.HitObjects,
                GameplayAnalyzer.IsSpinner,
                GameplayAnalyzer.IsSlider,
                GameplayAnalyzer.IsComplexSlider,
                GameplayAnalyzer.GetObjectStartPosition,
                GameplayAnalyzer.GetObjectEndPosition,
                GameplayAnalyzer.GetTechTurnAngle);

        IReadOnlyList<TechV2WindowDecision> strongAndSupportDecisions =
            TechV2ExperimentalDecisionEngine.Decide(windows);
        TechV2SustainedPromotionResult beforeGuardPromotion =
            TechV2ExperimentalDecisionEngine.PromoteSustainedTechWithDiagnostics(
                strongAndSupportDecisions,
                applyGeometryGuard: false);
        TechV2SustainedPromotionResult promotion =
            TechV2ExperimentalDecisionEngine.PromoteSustainedTechWithDiagnostics(
                strongAndSupportDecisions);
        IReadOnlyList<TechV2WindowDecision> decisions = promotion.Decisions;

        double playableStartTime = map.HitObjects.FirstOrDefault()?.Time ?? 0;
        double playableEndTime = map.HitObjects.LastOrDefault()?.Time ?? 0;
        TechV2PresenceResult strongOnlyPresence =
            TechV2ExperimentalDecisionEngine.BuildPresence(
                strongAndSupportDecisions,
                playableStartTime,
                playableEndTime);
        TechV2PresenceResult beforeGuardPresence =
            TechV2ExperimentalDecisionEngine.BuildPresence(
                beforeGuardPromotion.Decisions,
                playableStartTime,
                playableEndTime);
        TechV2PresenceResult presence =
            TechV2ExperimentalDecisionEngine.BuildPresence(
                decisions,
                playableStartTime,
                playableEndTime);

        IReadOnlyList<TechV2WindowDecision> positive = decisions
            .Where(decision => decision.IsDetectedTech)
            .ToArray();

        double longestRegion = presence.Regions.Count == 0
            ? 0
            : presence.Regions.Max(region => region.Duration);

        output.WriteLine($"Map: {fixtureName}");
        output.WriteLine($"Windows: {presence.WindowCount}");
        output.WriteLine(
            $"Strong Tech windows: {decisions.Count(decision => decision.IsStrongTech)} | " +
            $"Tech Support: {decisions.Count(decision => decision.IsTechSupport)} | " +
            $"Sustained before guard: {beforeGuardPromotion.Decisions.Count(decision => decision.IsSustainedTech)} | " +
            $"Sustained after guard: {decisions.Count(decision => decision.IsSustainedTech)} | " +
            $"Rejected runs: {promotion.RejectedRuns.Count}");
        output.WriteLine(
            $"Strong-only: Tech windows={strongOnlyPresence.TechWindowCount}, " +
            $"regions={strongOnlyPresence.Regions.Count}, " +
            $"presence={strongOnlyPresence.PresencePercent:F2}%");
        output.WriteLine(
            $"Before guard: Tech windows={beforeGuardPresence.TechWindowCount}, " +
            $"regions={beforeGuardPresence.Regions.Count}, " +
            $"presence={beforeGuardPresence.PresencePercent:F2}% | " +
            $"After guard: Tech windows={presence.TechWindowCount}, " +
            $"regions={presence.Regions.Count}, " +
            $"presence={presence.PresencePercent:F2}%");
        foreach (TechV2SustainedRunGeometryRejection rejection in promotion.RejectedRuns)
        {
            output.WriteLine(
                $"Rejected sustained run: {rejection.StartTime:F0}->{rejection.EndTime:F0}ms | " +
                $"Windows={rejection.WindowCount} | AverageGeometry={rejection.AverageGeometryEvidence:F3} | " +
                $"GuardThreshold={rejection.GuardThreshold:F3} | " +
                $"RejectedByGeometryGuard={rejection.RejectedByGeometryGuard}");
        }
        output.WriteLine($"Longest region: {longestRegion / 1000.0:F2}s");
        output.WriteLine($"Total Tech duration: {presence.TotalTechDuration / 1000.0:F2}s");
        output.WriteLine($"Playable duration: {presence.PlayableDuration / 1000.0:F2}s");
        output.WriteLine($"Presence: {presence.PresencePercent:F2}%");
        output.WriteLine(
            $"Positive family averages: Geometry={Average(positive, item => item.GeometryEvidence):F3}, " +
            $"Movement={Average(positive, item => item.MovementEvidence):F3}, " +
            $"Slider={Average(positive, item => item.SliderEvidence):F3}, " +
            $"Motif={Average(positive, item => item.MotifEvidence):F3}");

        Assert.InRange(presence.PresencePercent, 0, 100);
        Assert.All(presence.Regions, region => Assert.True(region.Duration > 0));
    }

    private static double Average(
        IReadOnlyList<TechV2WindowDecision> decisions,
        Func<TechV2WindowDecision, double> selector) =>
        decisions.Count == 0 ? 0 : decisions.Average(selector);
}
