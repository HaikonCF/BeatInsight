using BeatInsight.Models.AnalysisCache;
using BeatInsight.Models.Fusion;
using BeatInsight.Models.Identity;
using BeatInsight.Models.Rating;
using BeatInsight.Models.Sections;
using BeatInsight.Models.SkillPressure;
using BeatInsight.Models.Speed;
using BeatInsight.ViewModels;

namespace BeatInsight.Tests.ViewModels;

/// <summary>
/// Verrouille le câblage UI V2 : Speed doit provenir exclusivement de
/// GlobalTripletSpeed.SpeedPercent, Rating de RatingV2, Identity de
/// IdentityV3, et Sections/Spikes de SectionAnalysisV2.
/// </summary>
public sealed class OverviewViewModelTests
{
    private static BeatmapAnalysisSnapshot BuildSnapshot(
        double ratingScore = 5.4,
        double ratingConfidence = 0.82,
        double tripletSpeedPercent = 63.0,
        GameplayStructureKind primary = GameplayStructureKind.Stream,
        GameplayStructureKind? secondary = GameplayStructureKind.Jump)
    {
        var metadata = new BeatmapMetadataSnapshot(
            123, 456, "Artist", "Title", "Version", "Creator",
            TimeSpan.FromSeconds(90).Ticks, 180, 800,
            9.0, 8.0, 4.0, 5.0,
            300, 50, 2,
            6.5, 5.0);

        var gameplayProfile = new GameplayProfileSnapshot(
            50, 40, 30, 20,
            0.5, 0.2, 0.1,
            0.3, 0.3, 0.4,
            0.5, "Stream", "Jump", "Stream/Jump", 0.9,
            ["Trait A"]);

        SkillPressureMetric metric(double score) =>
            new(score, 0.8, true, true, new Dictionary<string, double>(), "");

        var skillPressure = new SkillPressureProfile(
            metric(70), metric(60), metric(50), metric(40),
            metric(35), metric(45), metric(55), metric(65),
            0.8, 8, []);

        var section = new GameplaySectionV2Analysis(
            SectionIndex: 0,
            StartTime: 0,
            EndTime: 5000,
            ObjectIndices: [0, 1, 2],
            CircleCount: 3,
            SliderCount: 0,
            SpinnerCount: 0,
            OtherObjectCount: 0,
            ObjectsPerSecond: 2.0,
            JumpScore: 10,
            StreamScore: 90,
            TechScore: 5,
            BurstScore: 5,
            DominantStructure: GameplayStructureKind.Stream,
            SecondaryStructure: null,
            BaseSkills: new SectionLocalSkills(50, 40, 30, 20),
            SkillPressure: skillPressure,
            JumpPatterns: SectionPatternFamilyV2.Empty,
            StreamPatterns: SectionPatternFamilyV2.Empty,
            TechPatterns: SectionPatternFamilyV2.Empty,
            LocalDifficultyScore: 78.5,
            LocalDifficultyConfidence: 0.9,
            Spike: new SectionDifficultySpike(true, 40, 38.5, 1.96, 0.9, ["StreamDensity"]),
            Transition: SectionGameplayTransition.First(GameplayStructureKind.Stream),
            Summary: "Stream-dominant section");

        var sectionAnalysis = new SectionAnalysisResult(
            [section], 78.5, 78.5, 0, 1, 0,
            [GameplayStructureKind.Stream],
            [78.5], [], [], 0.9, TimeSpan.FromMilliseconds(5));

        var identity = new GameplayIdentityV3Profile(
            0.2, 0.6, 0.1, 0.1,
            primary, secondary,
            [new GameplayIdentityV3Skill("Stream", 88, 0.9, GameplayIdentityV3EvidenceStatus.Reliable)],
            [new GameplayIdentityV3Pattern("Flow Stream", FusionPatternFamily.Stream, 0.7, 0.6, 0.85, 1.2)],
            new GameplayIdentityV3SectionContext(0.1, 0.8, 0.05, 0.05, 2, 0.4, 3, 1, [GameplayStructureKind.Stream]),
            0.85, 0.8, 0.75, 0.83,
            []);

        var rating = new BeatInsightRatingV2(
            ratingScore, ratingConfidence, 4.0, 6.0, 5.0,
            1.0, 0.5, 0.3, 0.2, 60, 78.5, 0.4, 0.2, 1,
            [], []);

        var tripletSpeed = new GlobalTripletSpeedAnalysis(
            tripletSpeedPercent, 150, 300, 298, 298, 0,
            80, 400, 150, 0.9, 200, 0.85, []);

        return new BeatmapAnalysisSnapshot(
            metadata, gameplayProfile, identity, skillPressure,
            sectionAnalysis, rating, tripletSpeed, identity.MainPatterns);
    }

    [Fact]
    public void LoadSnapshot_SpeedMetric_ComesExclusivelyFromGlobalTripletSpeed()
    {
        var overview = new OverviewViewModel();
        var snapshot = BuildSnapshot(tripletSpeedPercent: 91.0);

        overview.LoadSnapshot(snapshot);

        SkillMetricViewModel speed = overview.SkillMetrics.Single(m => m.Name == "Speed");
        Assert.Equal(91.0, speed.Value, 5);
    }

    [Fact]
    public void LoadSnapshot_Rating_ComesFromRatingV2()
    {
        var overview = new OverviewViewModel();
        var snapshot = BuildSnapshot(ratingScore: 7.26, ratingConfidence: 0.61);

        overview.LoadSnapshot(snapshot);

        Assert.Equal(7.26, overview.BeatInsightRatingScore, 5);
        Assert.Equal(0.61, overview.BeatInsightRatingConfidence, 5);
        Assert.Equal("7.3", overview.BeatInsightRatingDisplay);
    }

    [Fact]
    public void LoadSnapshot_Identity_ComesFromIdentityV3()
    {
        var overview = new OverviewViewModel();
        var snapshot = BuildSnapshot(
            primary: GameplayStructureKind.Tech,
            secondary: GameplayStructureKind.ClassicMixed);

        overview.LoadSnapshot(snapshot);

        Assert.Equal("TECH", overview.Identity.PrimaryStructure);
        Assert.Equal("CLASSICMIXED", overview.Identity.SecondaryStructure);
        Assert.True(overview.Identity.HasSecondary);
        Assert.Single(overview.Identity.MainSkills);
        Assert.Single(overview.Identity.MainPatterns);
    }

    [Fact]
    public void LoadSnapshot_Sections_And_Spikes_ComeFromSectionAnalysisV2()
    {
        var overview = new OverviewViewModel();
        var snapshot = BuildSnapshot();

        overview.LoadSnapshot(snapshot);

        Assert.Single(overview.Sections);
        Assert.Single(overview.DifficultyPoints);
        Assert.True(overview.DifficultyPoints[0].IsSpike);
        Assert.True(overview.HasSpikes);
        Assert.Contains("1 difficulty spike", overview.SpikesSummaryText);
        Assert.Equal("STREAM", overview.Sections[0].Structure.ToUpperInvariant());
    }

    [Fact]
    public void LoadSnapshot_NullSnapshot_ClearsOverview()
    {
        var overview = new OverviewViewModel();
        overview.LoadSnapshot(BuildSnapshot());

        overview.LoadSnapshot(null);

        Assert.False(overview.HasBeatmap);
        Assert.Empty(overview.Sections);
        Assert.Empty(overview.SkillMetrics);
    }

    [Fact]
    public void CommunityTags_AreAppliedSeparatelyAndClearedForNextSnapshot()
    {
        var overview = new OverviewViewModel();
        overview.LoadSnapshot(BuildSnapshot());
        overview.UpdateCommunityTags(
        [
            new CommunityTag { Name = "stream", Votes = 12 },
            new CommunityTag { Name = "speed", Votes = 7 },
        ]);

        Assert.True(overview.HasCommunityTags);
        Assert.Collection(
            overview.CommunityTags,
            tag => Assert.Equal("stream", tag.Name),
            tag => Assert.Equal("speed", tag.Name));

        overview.LoadSnapshot(BuildSnapshot());

        Assert.False(overview.HasCommunityTags);
        Assert.Empty(overview.CommunityTags);
    }

    [Fact]
    public void EmptyCommunityTags_RemainHidden()
    {
        var overview = new OverviewViewModel();

        overview.UpdateCommunityTags([]);

        Assert.False(overview.HasCommunityTags);
        Assert.Empty(overview.CommunityTags);
    }

    [Fact]
    public void ActiveRequest_LoadingToReady_AndLoadingToError()
    {
        var overview = new OverviewViewModel();
        var coordinator = new OverviewLoadCoordinator(overview);

        long first = coordinator.Begin("Analyzing...");
        Assert.True(overview.IsLoading);
        Assert.Equal(BiaVisualState.Analyzing, overview.Assistant.State);
        Assert.True(coordinator.Complete(first, BuildSnapshot()));
        Assert.False(overview.IsLoading);
        Assert.True(overview.HasBeatmap);
        Assert.Equal(BiaVisualState.Idle, overview.Assistant.State);

        long second = coordinator.Begin("Analyzing...");
        Assert.True(coordinator.Fail(second, "Analysis failed"));
        Assert.False(overview.IsLoading);
        Assert.True(overview.HasError);
        Assert.Equal(BiaVisualState.Error, overview.Assistant.State);
    }

    [Fact]
    public void RapidRequests_OnlyLatestCanChangeOverview()
    {
        var overview = new OverviewViewModel();
        var coordinator = new OverviewLoadCoordinator(overview);
        long a = coordinator.Begin("A");
        long b = coordinator.Begin("B");
        long c = coordinator.Begin("C");

        Assert.False(coordinator.Complete(a, BuildSnapshot()));
        Assert.False(coordinator.Fail(b, "Old error"));
        Assert.True(overview.IsLoading);
        Assert.Equal("C", overview.LoadingMessage);
        Assert.Equal(BiaVisualState.Analyzing, overview.Assistant.State);

        Assert.True(coordinator.Complete(c, BuildSnapshot()));
        Assert.False(coordinator.Fail(a, "Late error"));
        Assert.False(overview.IsLoading);
        Assert.False(overview.HasError);
        Assert.Equal(BiaVisualState.Idle, overview.Assistant.State);
    }

    [Fact]
    public void CancelledRequest_CannotLeaveLoadingAfterReset()
    {
        var overview = new OverviewViewModel();
        var coordinator = new OverviewLoadCoordinator(overview);
        long old = coordinator.Begin("A");

        coordinator.Invalidate();
        overview.Clear();

        Assert.False(coordinator.Complete(old, BuildSnapshot()));
        Assert.False(coordinator.Fail(old, "Late error"));
        Assert.False(overview.IsLoading);
        Assert.Equal(BiaVisualState.Idle, overview.Assistant.State);
    }
}
