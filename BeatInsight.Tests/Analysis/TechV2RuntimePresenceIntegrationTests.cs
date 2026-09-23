using BeatInsight.Analysis;
using BeatInsight.Models;
using BeatInsight.Models.Persistence;
using BeatInsight.Services.Persistence;

namespace BeatInsight.Tests.Analysis;

public sealed class TechV2RuntimePresenceIntegrationTests
{
    [Fact]
    public void ZeroA_ZeroDenominator_ProducesNoSupportAwareInflation()
    {
        Assert.Equal(0, TechV2CombinedCorrectionShadowRuntime.SupportAwareRatio(0, 0));
        Assert.Equal(0, TechV2CombinedCorrectionShadowRuntime.SupportAwareRatio(4, 0));
        Assert.Equal(0.25, TechV2CombinedCorrectionShadowRuntime.SupportAwareRatio(0, 1), 10);
    }

    [Fact]
    public void PrevA_UnsupportedCurrentWindow_DoesNotInheritPreviousContext()
    {
        Assert.Equal(
            0.20,
            TechV2CombinedCorrectionShadowRuntime.ApplyPreviousContext(
                currentLocal: 0.20,
                previousLocal: 0.90,
                validTransitions: 0,
                sliderCount: 0),
            10);

        Assert.Equal(
            0.55,
            TechV2CombinedCorrectionShadowRuntime.ApplyPreviousContext(
                currentLocal: 0.20,
                previousLocal: 0.90,
                validTransitions: 1,
                sliderCount: 0),
            10);
    }

    [Fact]
    public void PresenceResult_WithoutRegions_IsZeroAndFinite()
    {
        TechV2PresenceResult result = TechV2ExperimentalDecisionEngine.BuildPresence([], 0, 0);

        Assert.Equal(0, result.PresenceRatio);
        Assert.Equal(0, result.PresencePercent);
        Assert.False(double.IsNaN(result.PresencePercent));
        Assert.False(double.IsInfinity(result.PresencePercent));
    }

    [Fact]
    public void Analyze_ExposesV2TemporalPresenceSeparatelyFromLegacyCoverage()
    {
        Beatmap map = FixtureLoader.Load("Exit This Earth's Atmosphere [Primordial Nucleosynthesis].osu");
        GameplayProfile profile = GameplayAnalyzer.Analyze(map);

        IReadOnlyList<TechV2ExperimentalWindowResult> windows = TechDetectorV2Experimental.Analyze(
            map.HitObjects,
            GameplayAnalyzer.IsSpinner,
            GameplayAnalyzer.IsSlider,
            GameplayAnalyzer.IsComplexSlider,
            GameplayAnalyzer.GetObjectStartPosition,
            GameplayAnalyzer.GetObjectEndPosition,
            GameplayAnalyzer.GetTechTurnAngle);
        IReadOnlyList<TechV2WindowDecision> decisions =
            TechV2ExperimentalDecisionEngine.PromoteSustainedTech(
                TechV2ExperimentalDecisionEngine.Decide(windows));
        TechV2PresenceResult expected = TechV2ExperimentalDecisionEngine.BuildPresence(
            decisions,
            map.HitObjects.First().Time,
            map.HitObjects.Last().Time);

        Assert.Equal(expected.PresenceRatio, profile.TechV2Presence, 10);
        Assert.InRange(profile.TechV2Presence, 0, 1);
        Assert.Equal(profile.TechPresence, profile.TechRatio, 10);
    }

    [Fact]
    public void ProfilePersistence_PreservesLegacyAndV2PresenceWithoutChangingIdentity()
    {
        GameplayIdentity identity = new()
        {
            Primary = "Jump",
            Secondary = "Tech",
            Pattern = "Jump / Tech",
            Confidence = 72,
            Traits = ["Tech Secondary"],
        };
        GameplayProfile source = new()
        {
            TechPresence = .38,
            TechV2Presence = .247,
            TechScore = 21,
            Identity = identity,
        };

        GameplayProfileRecord stored = BeatmapAnalysisMapper.ToProfileRecord(source);
        GameplayProfile restored = BeatmapAnalysisMapper.ToGameplayProfile(stored);

        Assert.Equal(.38, restored.TechPresence, 10);
        Assert.Equal(.247, restored.TechV2Presence, 10);
        Assert.Equal("Jump", restored.Identity.Primary);
        Assert.Equal("Tech", restored.Identity.Secondary);
        Assert.Equal("Jump / Tech", restored.Identity.Pattern);
        Assert.Equal(72, restored.Identity.Confidence, 10);
    }
}
