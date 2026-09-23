using BeatInsight.Analysis;
using BeatInsight.Models;

namespace BeatInsight.Tests.Analysis;

public sealed class TechV2ExperimentalDecisionEngineTests
{
    [Fact]
    public void Detect_NoObjects_ProducesEmptyZeroPresence()
    {
        TechV2PresenceResult result =
            TechV2ExperimentalDecisionEngine.Detect([], 0, 0);

        Assert.Empty(result.Regions);
        Assert.Equal(0, result.WindowCount);
        Assert.Equal(0, result.PresencePercent);
    }

    [Fact]
    public void Analyze_TooFewObjects_ProducesNoWindowsOrRegions()
    {
        HitObject[] objects = Enumerable.Range(0, 11)
            .Select(index => new HitObject
            {
                Type = 1,
                Time = index * 100,
                X = index * 20,
                Y = 192,
            })
            .ToArray();

        IReadOnlyList<TechV2ExperimentalWindowResult> windows =
            TechDetectorV2Experimental.Analyze(
                objects,
                GameplayAnalyzer.IsSpinner,
                GameplayAnalyzer.IsSlider,
                GameplayAnalyzer.IsComplexSlider,
                GameplayAnalyzer.GetObjectStartPosition,
                GameplayAnalyzer.GetObjectEndPosition,
                GameplayAnalyzer.GetTechTurnAngle);

        TechV2PresenceResult result =
            TechV2ExperimentalDecisionEngine.Detect(windows, 0, 1_000);

        Assert.Empty(windows);
        Assert.Empty(result.Regions);
        Assert.Equal(0, result.PresencePercent);
    }

    [Fact]
    public void Decide_OneIsolatedStrongFamily_IsNotTech()
    {
        TechV2WindowDecision decision = TechV2ExperimentalDecisionEngine
            .Decide([Window(geometry: 1.0)])
            .Single();

        Assert.False(decision.IsTech);
        Assert.Equal(1, decision.EvidenceFamilyCount);
    }

    [Fact]
    public void Decide_MultipleEvidenceFamilies_CanCreateTech()
    {
        TechV2WindowDecision decision = TechV2ExperimentalDecisionEngine
            .Decide([Window(geometry: 0.95, rhythm: 0.95, awkward: 0.75)])
            .Single();

        Assert.True(decision.IsTech);
        Assert.True(decision.EvidenceFamilyCount >= 2);
    }

    [Fact]
    public void BuildPresence_OverlappingTechWindowsMerge()
    {
        TechV2PresenceResult result = TechV2ExperimentalDecisionEngine.BuildPresence(
            [Decision(0, 11, 0, 100, isTech: true), Decision(4, 15, 50, 150, isTech: true)],
            0,
            1_000);

        TechV2Region region = Assert.Single(result.Regions);
        Assert.Equal(0, region.StartTime);
        Assert.Equal(150, region.EndTime);
        Assert.Equal(2, region.WindowCount);
    }

    [Fact]
    public void BuildPresence_AdjacentTechWindowsMerge()
    {
        TechV2PresenceResult result = TechV2ExperimentalDecisionEngine.BuildPresence(
            [Decision(0, 11, 0, 100, isTech: true), Decision(4, 15, 100, 200, isTech: true)],
            0,
            1_000);

        TechV2Region region = Assert.Single(result.Regions);
        Assert.Equal(200, region.EndTime);
    }

    [Fact]
    public void BuildPresence_NeighboringModerateSupportExtendsStrongRegion()
    {
        TechV2PresenceResult result = TechV2ExperimentalDecisionEngine.BuildPresence(
            [
                Decision(0, 11, 0, 100, isTech: true),
                Decision(4, 15, 50, 150, isTech: false, isModerateSupport: true),
            ],
            0,
            1_000);

        TechV2Region region = Assert.Single(result.Regions);
        Assert.Equal(150, region.EndTime);
        Assert.Equal(2, region.WindowCount);
        Assert.Equal(1, region.StrongTechWindowCount);
    }

    [Fact]
    public void PromoteSustainedTech_ThreeContinuousSupportWindowsBecomeSustained()
    {
        IReadOnlyList<TechV2WindowDecision> promoted =
            TechV2ExperimentalDecisionEngine.PromoteSustainedTech(
            [
                Decision(0, 11, 0, 100, isTech: false, isModerateSupport: true, geometry: 0.266),
                Decision(4, 15, 50, 150, isTech: false, isModerateSupport: true, geometry: 0.266),
                Decision(8, 19, 100, 200, isTech: false, isModerateSupport: true, geometry: 0.266),
            ]);

        Assert.All(promoted, decision => Assert.True(decision.IsTechSupport));
        Assert.All(promoted, decision => Assert.True(decision.IsSustainedTech));
        Assert.All(promoted, decision => Assert.True(decision.IsDetectedTech));

        TechV2PresenceResult presence = TechV2ExperimentalDecisionEngine.BuildPresence(
            promoted,
            0,
            1_000);

        TechV2Region region = Assert.Single(presence.Regions);
        Assert.Equal(200, region.EndTime);
        Assert.Equal(3, presence.TechWindowCount);
        Assert.Equal(0, region.StrongTechWindowCount);
    }

    [Fact]
    public void PromoteSustainedTech_TwoSupportWindowsRemainNonPositiveWithoutAnchor()
    {
        IReadOnlyList<TechV2WindowDecision> promoted =
            TechV2ExperimentalDecisionEngine.PromoteSustainedTech(
            [
                Decision(0, 11, 0, 100, isTech: false, isModerateSupport: true),
                Decision(4, 15, 50, 150, isTech: false, isModerateSupport: true),
            ]);

        Assert.All(promoted, decision => Assert.False(decision.IsSustainedTech));
        Assert.All(promoted, decision => Assert.False(decision.IsDetectedTech));
        Assert.Empty(TechV2ExperimentalDecisionEngine.BuildPresence(promoted, 0, 1_000).Regions);
    }

    [Fact]
    public void PromoteSustainedTech_RunBelowGeometryThresholdIsRejectedButRemainsSupport()
    {
        TechV2SustainedPromotionResult result =
            TechV2ExperimentalDecisionEngine.PromoteSustainedTechWithDiagnostics(
            [
                Decision(0, 11, 0, 100, false, true, geometry: 0.265),
                Decision(4, 15, 50, 150, false, true, geometry: 0.265),
                Decision(8, 19, 100, 200, false, true, geometry: 0.265),
            ]);

        TechV2SustainedRunGeometryRejection rejection = Assert.Single(result.RejectedRuns);
        Assert.Equal(3, rejection.WindowCount);
        Assert.Equal(0.265, rejection.AverageGeometryEvidence, 6);
        Assert.True(rejection.RejectedByGeometryGuard);
        Assert.All(result.Decisions, decision =>
        {
            Assert.True(decision.IsTechSupport);
            Assert.False(decision.IsSustainedTech);
        });
    }

    [Fact]
    public void PromoteSustainedTech_RunAtGeometryThresholdIsKept()
    {
        TechV2SustainedPromotionResult result =
            TechV2ExperimentalDecisionEngine.PromoteSustainedTechWithDiagnostics(
            [
                Decision(0, 11, 0, 100, false, true, geometry: 0.266),
                Decision(4, 15, 50, 150, false, true, geometry: 0.266),
                Decision(8, 19, 100, 200, false, true, geometry: 0.266),
            ]);

        Assert.Empty(result.RejectedRuns);
        Assert.All(result.Decisions, decision => Assert.True(decision.IsSustainedTech));
    }

    [Fact]
    public void PromoteSustainedTech_RunAboveGeometryThresholdIsKept()
    {
        TechV2SustainedPromotionResult result =
            TechV2ExperimentalDecisionEngine.PromoteSustainedTechWithDiagnostics(
            [
                Decision(0, 11, 0, 100, false, true, geometry: 0.500),
                Decision(4, 15, 50, 150, false, true, geometry: 0.500),
                Decision(8, 19, 100, 200, false, true, geometry: 0.500),
            ]);

        Assert.Empty(result.RejectedRuns);
        Assert.All(result.Decisions, decision => Assert.True(decision.IsSustainedTech));
    }

    [Fact]
    public void PromoteSustainedTech_StrongTechIsUnaffectedByGeometryGuard()
    {
        TechV2SustainedPromotionResult result =
            TechV2ExperimentalDecisionEngine.PromoteSustainedTechWithDiagnostics(
            [Decision(0, 11, 0, 100, isTech: true, geometry: 0)]);

        TechV2WindowDecision decision = Assert.Single(result.Decisions);
        Assert.True(decision.IsStrongTech);
        Assert.True(decision.IsDetectedTech);
        Assert.Empty(result.RejectedRuns);
    }

    [Fact]
    public void PromoteSustainedTech_MultipleIndependentRunsAreEvaluatedSeparately()
    {
        TechV2SustainedPromotionResult result =
            TechV2ExperimentalDecisionEngine.PromoteSustainedTechWithDiagnostics(
            [
                Decision(0, 11, 0, 100, false, true, geometry: 0.200),
                Decision(4, 15, 50, 150, false, true, geometry: 0.200),
                Decision(8, 19, 100, 200, false, true, geometry: 0.200),
                Decision(30, 41, 500, 600, false, true, geometry: 0.300),
                Decision(34, 45, 550, 650, false, true, geometry: 0.300),
                Decision(38, 49, 600, 700, false, true, geometry: 0.300),
            ]);

        Assert.Single(result.RejectedRuns);
        Assert.All(result.Decisions.Take(3), decision => Assert.False(decision.IsSustainedTech));
        Assert.All(result.Decisions.Skip(3), decision => Assert.True(decision.IsSustainedTech));
    }

    [Fact]
    public void BuildPresence_IsolatedStrongTechKeepsOnlyImmediateNeighboringSupport()
    {
        TechV2PresenceResult result = TechV2ExperimentalDecisionEngine.BuildPresence(
            [
                Decision(0, 11, 0, 100, isTech: true),
                Decision(4, 15, 50, 150, isTech: false, isModerateSupport: true),
                Decision(8, 19, 100, 200, isTech: false, isModerateSupport: true),
            ],
            0,
            1_000);

        TechV2Region region = Assert.Single(result.Regions);
        Assert.Equal(150, region.EndTime);
        Assert.Equal(2, region.WindowCount);
        Assert.Equal(1, region.StrongTechWindowCount);
        Assert.True(region.AnchorPropagationLimited);
    }

    [Fact]
    public void BuildPresence_IsolatedStrongTechKeepsOnlyImmediatePrecedingSupport()
    {
        TechV2PresenceResult result = TechV2ExperimentalDecisionEngine.BuildPresence(
            [
                Decision(0, 11, 0, 100, isTech: false, isModerateSupport: true),
                Decision(4, 15, 50, 150, isTech: false, isModerateSupport: true),
                Decision(8, 19, 100, 200, isTech: true),
            ],
            0,
            1_000);

        TechV2Region region = Assert.Single(result.Regions);
        Assert.Equal(50, region.StartTime);
        Assert.Equal(200, region.EndTime);
        Assert.Equal(2, region.WindowCount);
        Assert.Equal(1, region.StrongTechWindowCount);
        Assert.True(region.AnchorPropagationLimited);
    }

    [Fact]
    public void BuildPresence_SeparatedTechPassagesRemainSeparate()
    {
        TechV2PresenceResult result = TechV2ExperimentalDecisionEngine.BuildPresence(
            [Decision(0, 11, 0, 100, isTech: true), Decision(20, 31, 400, 500, isTech: true)],
            0,
            1_000);

        Assert.Equal(2, result.Regions.Count);
    }

    [Fact]
    public void BuildPresence_UsesTemporalDurationAndClampsPresence()
    {
        TechV2PresenceResult result = TechV2ExperimentalDecisionEngine.BuildPresence(
            [Decision(0, 11, -50, 1_500, isTech: true)],
            0,
            1_000);

        Assert.Equal(1_000, result.TotalTechDuration);
        Assert.Equal(1_000, result.PlayableDuration);
        Assert.Equal(1, result.PresenceRatio);
        Assert.Equal(100, result.PresencePercent);
    }

    [Fact]
    public void Detect_IsDeterministic()
    {
        TechV2ExperimentalWindowResult[] windows =
        [
            Window(geometry: 0.95, rhythm: 0.95, awkward: 0.75, startIndex: 0, startTime: 0, endTime: 100),
            Window(geometry: 0.95, rhythm: 0.95, awkward: 0.75, startIndex: 4, startTime: 50, endTime: 150),
        ];

        TechV2PresenceResult first =
            TechV2ExperimentalDecisionEngine.Detect(windows, 0, 1_000);
        TechV2PresenceResult second =
            TechV2ExperimentalDecisionEngine.Detect(windows, 0, 1_000);

        Assert.Equal(first.TotalTechDuration, second.TotalTechDuration);
        Assert.Equal(first.PlayableDuration, second.PlayableDuration);
        Assert.Equal(first.PresenceRatio, second.PresenceRatio);
        Assert.Equal(first.PresencePercent, second.PresencePercent);
        Assert.Equal(first.WindowCount, second.WindowCount);
        Assert.Equal(first.TechWindowCount, second.TechWindowCount);
        Assert.Equal(first.Regions, second.Regions);
    }

    [Fact]
    public void PromoteSustainedTechWithDiagnostics_IsDeterministic()
    {
        TechV2WindowDecision[] decisions =
        [
            Decision(0, 11, 0, 100, false, true, geometry: 0.200),
            Decision(4, 15, 50, 150, false, true, geometry: 0.200),
            Decision(8, 19, 100, 200, false, true, geometry: 0.200),
        ];

        TechV2SustainedPromotionResult first =
            TechV2ExperimentalDecisionEngine.PromoteSustainedTechWithDiagnostics(decisions);
        TechV2SustainedPromotionResult second =
            TechV2ExperimentalDecisionEngine.PromoteSustainedTechWithDiagnostics(decisions);

        Assert.Equal(first.Decisions, second.Decisions);
        Assert.Equal(first.RejectedRuns, second.RejectedRuns);
    }

    private static TechV2ExperimentalWindowResult Window(
        double geometry = 0,
        double rhythm = 0,
        double slider = 0,
        double awkward = 0,
        double spacing = 0,
        double motif = 0,
        int startIndex = 0,
        double startTime = 0,
        double endTime = 100) =>
        new(
            startIndex,
            startIndex + 11,
            startTime,
            endTime,
            geometry,
            rhythm,
            slider,
            awkward,
            spacing,
            0,
            0,
            0,
            new TechV2ExperimentalSliderTransitionTopology(11, 0, 0, 0, 1),
            0,
            motif,
            0);

    private static TechV2WindowDecision Decision(
        int startIndex,
        int endIndex,
        double startTime,
        double endTime,
        bool isTech,
        bool isModerateSupport = false,
        double geometry = 0) =>
        new(
            startIndex,
            endIndex,
            startTime,
            endTime,
            isTech,
            geometry,
            0,
            0,
            0,
            isTech ? 2 : 0,
            0,
            IsModerateSupport: isModerateSupport,
            IsSustainedTech: false);
}
