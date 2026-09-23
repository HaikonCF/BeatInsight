using BeatInsight.Analysis;
using BeatInsight.Models;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class TechV2DecisionCalibrationAuditTests
{
    [Fact]
    public void Describe_FailureReasonMatchesCurrentDecisionRule()
    {
        TechV2WindowDecision twoStrong = TechV2ExperimentalDecisionEngine.Decide(
            [Window(geometry: 1, angleCoherence: 1, rhythm: 1, awkward: 1)])
            .Single();
        TechV2WindowDecision exceptionalPlusModerate = TechV2ExperimentalDecisionEngine.Decide(
            [Window(geometry: 1, angleCoherence: 1, rhythm: 0.7, awkward: 0.6)])
            .Single();
        TechV2WindowDecision oneStrong = TechV2ExperimentalDecisionEngine.Decide(
            [Window(geometry: 1, angleCoherence: 1)])
            .Single();
        TechV2WindowDecision noStrong = TechV2ExperimentalDecisionEngine.Decide(
            [Window(rhythm: 0.5, awkward: 0.5)])
            .Single();

        Assert.Equal(
            TechV2DecisionFailureReason.PASS_TWO_STRONG,
            TechV2DecisionCalibrationAudit.Describe(twoStrong).FailureReason);
        Assert.Equal(
            TechV2DecisionFailureReason.PASS_EXCEPTIONAL_PLUS_MODERATE,
            TechV2DecisionCalibrationAudit.Describe(exceptionalPlusModerate).FailureReason);
        Assert.Equal(
            TechV2DecisionFailureReason.FAIL_SECOND_FAMILY_TOO_LOW,
            TechV2DecisionCalibrationAudit.Describe(oneStrong).FailureReason);
        Assert.Equal(
            TechV2DecisionFailureReason.FAIL_NO_STRONG_FAMILY,
            TechV2DecisionCalibrationAudit.Describe(noStrong).FailureReason);
    }

    [Fact]
    public void Describe_RanksEqualFamiliesDeterministically()
    {
        TechV2WindowDecision decision = new(
            0, 11, 0, 100, false,
            GeometryEvidence: 0.60,
            MovementEvidence: 0.20,
            SliderEvidence: 0.60,
            MotifEvidence: 0.10,
            EvidenceFamilyCount: 2,
            RawScore: 0.375,
            IsModerateSupport: false,
            IsSustainedTech: false);

        TechV2DecisionCalibrationWindow audit =
            TechV2DecisionCalibrationAudit.Describe(decision);

        Assert.Equal("Geometry", audit.BestFamily.Name);
        Assert.Equal("Slider", audit.SecondBestFamily.Name);
    }

    [Fact]
    public void Analyze_NearMissAuditDoesNotAlterDecisions()
    {
        TechV2WindowDecision[] decisions =
        [
            new(0, 11, 0, 100, false, 0.70, 0.42, 0.10, 0, 1, 0.305, false, false),
            new(4, 15, 50, 150, false, 0.65, 0.41, 0.10, 0, 1, 0.290, false, false),
        ];
        TechV2PresenceResult presence = TechV2ExperimentalDecisionEngine.BuildPresence(
            decisions,
            0,
            1_000);
        TechV2WindowDecision[] snapshot = decisions.ToArray();

        TechV2DecisionCalibrationAuditResult audit =
            TechV2DecisionCalibrationAudit.Analyze("synthetic", decisions, presence);

        Assert.Equal(snapshot, decisions);
        Assert.Equal(2, audit.NearMissWindowCount);
        Assert.Equal(2, audit.LongestNearMissRun);
    }

    [Fact]
    public void FormatReport_IsDeterministic()
    {
        TechV2WindowDecision[] decisions =
        [
            new(0, 11, 0, 100, false, 0.70, 0.42, 0.10, 0, 1, 0.305, false, false),
            new(4, 15, 50, 150, false, 0.65, 0.41, 0.10, 0, 1, 0.290, false, false),
        ];
        TechV2PresenceResult presence = TechV2ExperimentalDecisionEngine.BuildPresence(
            decisions,
            0,
            1_000);

        string first = TechV2DecisionCalibrationAudit.FormatReport(
            TechV2DecisionCalibrationAudit.Analyze("synthetic", decisions, presence));
        string second = TechV2DecisionCalibrationAudit.FormatReport(
            TechV2DecisionCalibrationAudit.Analyze("synthetic", decisions, presence));

        Assert.Equal(first, second);
    }

    private static TechV2ExperimentalWindowResult Window(
        double geometry = 0,
        double angleCoherence = 0,
        double rhythm = 0,
        double awkward = 0) =>
        new(
            0, 11, 0, 100,
            geometry, rhythm, 0, awkward, 0, 0,
            angleCoherence, 0,
            new TechV2ExperimentalSliderTransitionTopology(11, 0, 0, 0, 1),
            0, 0, 0);
}

/// <summary>
/// Read-only fixture report for manual Phase 2.1 calibration review. It makes
/// no map-category assertion and does not alter the runtime decision engine.
/// </summary>
public sealed class TechV2DecisionCalibrationFixtureReportTests(ITestOutputHelper output)
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
        IReadOnlyList<TechV2ExperimentalWindowResult> windows = Analyze(map);
        IReadOnlyList<TechV2WindowDecision> decisions =
            TechV2ExperimentalDecisionEngine.Decide(windows);
        TechV2PresenceResult presence =
            TechV2ExperimentalDecisionEngine.BuildPresence(
                decisions,
                map.HitObjects.FirstOrDefault()?.Time ?? 0,
                map.HitObjects.LastOrDefault()?.Time ?? 0);

        TechV2DecisionCalibrationAuditResult audit =
            TechV2DecisionCalibrationAudit.Analyze(fixtureName, decisions, presence);

        output.WriteLine(TechV2DecisionCalibrationAudit.FormatReport(audit));

        Assert.Equal(audit.TotalWindows, audit.TechWindows + audit.NonTechWindows);
        Assert.InRange(audit.PresencePercent, 0, 100);
        Assert.All(audit.TopWindows, window => Assert.InRange(window.Decision.RawScore, 0, 1));
    }

    private static IReadOnlyList<TechV2ExperimentalWindowResult> Analyze(Beatmap map) =>
        TechDetectorV2Experimental.Analyze(
            map.HitObjects,
            GameplayAnalyzer.IsSpinner,
            GameplayAnalyzer.IsSlider,
            GameplayAnalyzer.IsComplexSlider,
            GameplayAnalyzer.GetObjectStartPosition,
            GameplayAnalyzer.GetObjectEndPosition,
            GameplayAnalyzer.GetTechTurnAngle);
}
