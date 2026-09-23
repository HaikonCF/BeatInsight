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
    internal static BeatmapAnalysisSnapshot BuildSnapshot(
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
    public void LoadSnapshot_ProjectsEverySkillPressureMetricWithoutReanalysis()
    {
        var overview = new OverviewViewModel();

        overview.LoadSnapshot(BuildSnapshot());

        Assert.Equal(
            ["Stamina", "Endurance", "Finger Control", "Burst Control", "Aim Control", "Precision", "Reading Pressure", "Rhythm Complexity"],
            overview.SkillPressureMetrics.Select(metric => metric.Name));
        Assert.Equal([70d, 60d, 50d, 40d, 35d, 45d, 55d, 65d],
            overview.SkillPressureMetrics.Select(metric => metric.Value));
        Assert.All(overview.SkillPressureMetrics, metric => Assert.True(metric.IsAvailable));
    }

    [Fact]
    public void LoadSnapshot_UnavailablePrecisionDisplaysNaInsteadOfZero()
    {
        BeatmapAnalysisSnapshot snapshot = BuildSnapshot();
        snapshot = snapshot with
        {
            SkillPressureV2 = snapshot.SkillPressureV2 with
            {
                Precision = SkillPressureMetric.Unavailable("Geometry unavailable")
            }
        };
        var overview = new OverviewViewModel();

        overview.LoadSnapshot(snapshot);

        SkillMetricViewModel precision = overview.SkillPressureMetrics.Single(metric => metric.Name == "Precision");
        Assert.False(precision.IsAvailable);
        Assert.Equal("N/A", precision.DisplayValue);
        Assert.Equal("Geometry unavailable", precision.Description);
        SkillMetricViewModel burst = overview.SkillPressureMetrics.Single(metric => metric.Name == "Burst Control");
        Assert.True(burst.IsAvailable);
        Assert.Equal("40%", burst.DisplayValue);
    }

    [Fact]
    public void LoadSnapshot_ReplacesSkillPressureMetricsForNextBeatmap()
    {
        var overview = new OverviewViewModel();
        BeatmapAnalysisSnapshot first = BuildSnapshot();
        BeatmapAnalysisSnapshot second = first with
        {
            SkillPressureV2 = first.SkillPressureV2 with
            {
                Stamina = new SkillPressureMetric(
                    12, 0.7, true, true,
                    new Dictionary<string, double>(), string.Empty)
            }
        };

        overview.LoadSnapshot(first);
        overview.LoadSnapshot(second);

        Assert.Equal(8, overview.SkillPressureMetrics.Count);
        Assert.Equal(12, overview.SkillPressureMetrics[0].Value);
        overview.Clear();
        Assert.Empty(overview.SkillPressureMetrics);
    }

    [Fact]
    public void MainWindowNavigation_ActivatesOnlySkillsWhenSelected()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.SelectedTab = NavigationTab.Skills;

        Assert.True(viewModel.IsSkillsTabActive);
        Assert.False(viewModel.IsOverviewTabActive);
        Assert.False(viewModel.IsSectionsTabActive);
        Assert.False(viewModel.IsIdentityTabActive);
        Assert.False(viewModel.IsDifficultyTabActive);
    }

    [Fact]
    public void MainWindowNavigation_ActivatesOnlyIdentityWhenSelected()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.SelectedTab = NavigationTab.Identity;

        Assert.True(viewModel.IsIdentityTabActive);
        Assert.False(viewModel.IsOverviewTabActive);
        Assert.False(viewModel.IsSkillsTabActive);
        Assert.False(viewModel.IsSectionsTabActive);
        Assert.False(viewModel.IsDifficultyTabActive);
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
        MainSkillChipViewModel mainSkill = Assert.Single(overview.Identity.MainSkills);
        Assert.Equal("Stream", mainSkill.Name);
        Assert.Equal(88, mainSkill.Score);
        Assert.Equal("Reliable", mainSkill.Reliability);
        MainPatternChipViewModel mainPattern = Assert.Single(overview.Identity.MainPatterns);
        Assert.Equal("Flow Stream", mainPattern.Name);
        Assert.Equal("60% coverage", mainPattern.OccurrenceDisplay);
        Assert.Equal("83 %", overview.Identity.GlobalConfidenceDisplay);
    }

    [Fact]
    public void LoadSnapshot_IdentityWithoutSecondaryKeepsSecondaryHidden()
    {
        var overview = new OverviewViewModel();

        overview.LoadSnapshot(BuildSnapshot(secondary: null));

        Assert.False(overview.Identity.HasSecondary);
        Assert.Equal("—", overview.Identity.SecondaryStructure);
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
        Assert.Equal("1 significant difficulty spike detected", overview.SpikesSummaryText);
        Assert.Equal("STREAM", overview.Sections[0].Structure.ToUpperInvariant());
        Assert.Equal(10, overview.Sections[0].JumpScore);
        Assert.Equal(90, overview.Sections[0].StreamScore);
        Assert.Equal(5, overview.Sections[0].TechScore);
        Assert.Equal(5, overview.Sections[0].BurstScore);
    }

    [Fact]
    public void LoadSnapshot_ZeroSectionSignals_DoNotClaimJumpDominance()
    {
        BeatmapAnalysisSnapshot snapshot = BuildSnapshot();
        GameplaySectionV2Analysis section = snapshot.SectionAnalysisV2.Sections[0] with
        {
            JumpScore = 0,
            StreamScore = 0,
            TechScore = 0,
            BurstScore = 0
        };
        snapshot = snapshot with
        {
            SectionAnalysisV2 = snapshot.SectionAnalysisV2 with { Sections = [section] }
        };

        var overview = new OverviewViewModel();
        overview.LoadSnapshot(snapshot);

        Assert.Equal("None", overview.Sections[0].DominantSignal);
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
    public void LoadSnapshot_FormatsConstantBpmAndReliableMaxCombo()
    {
        var overview = new OverviewViewModel();
        BeatmapAnalysisSnapshot snapshot = BuildSnapshot();
        snapshot = snapshot with { Metadata = snapshot.Metadata with
            { PrimaryBpm = 180, MinBpm = 180, MaxBpm = 180, IsVariableBpm = false, OsuMaxCombo = 1498 } };

        overview.LoadSnapshot(snapshot);

        Assert.Equal("180 BPM", overview.BpmDisplay);
        Assert.Equal("1498", overview.MaxComboDisplay);
    }

    [Fact]
    public void LoadSnapshot_FormatsVariableBpmAndDoesNotFallBackToLegacyValues()
    {
        var overview = new OverviewViewModel();
        BeatmapAnalysisSnapshot snapshot = BuildSnapshot();
        snapshot = snapshot with { Metadata = snapshot.Metadata with
            { PrimaryBpm = 180, MinBpm = 160, MaxBpm = 190, IsVariableBpm = true } };

        overview.LoadSnapshot(snapshot);
        Assert.Equal("160–190 BPM", overview.BpmDisplay);
        Assert.Equal("—", overview.MaxComboDisplay);

        overview.LoadSnapshot(BuildSnapshot());
        Assert.Equal("—", overview.BpmDisplay);
        Assert.Equal("—", overview.MaxComboDisplay);
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

    [Fact]
    public void CacheHit_ReadyPrecedesSecondaryProjection()
    {
        var overview = new OverviewViewModel();
        var coordinator = new OverviewLoadCoordinator(overview);
        long request = coordinator.Begin("Analyzing...");
        var snapshot = BuildSnapshot();

        Assert.True(coordinator.LoadSnapshot(
            request, snapshot, wasCacheHit: true,
            loadTime: TimeSpan.FromMilliseconds(4), backgroundPath: null));
        Assert.True(overview.IsLoading);
        Assert.True(overview.HasBeatmap);
        Assert.Empty(overview.Sections);

        Assert.True(coordinator.SetReady(request));
        Assert.False(overview.IsLoading);
        Assert.Equal(BiaVisualState.Idle, overview.Assistant.State);
        Assert.Empty(overview.Sections);

        Assert.True(coordinator.CompleteSecondaryDetails(
            request, snapshot, wasCacheHit: true));
        Assert.Single(overview.Sections);
    }

    [Fact]
    public void RapidAtoBtoA_OnlyFinalACompletes()
    {
        var overview = new OverviewViewModel();
        var coordinator = new OverviewLoadCoordinator(overview);
        long firstA = coordinator.Begin("A first");
        long b = coordinator.Begin("B");
        long finalA = coordinator.Begin("A final");

        Assert.False(coordinator.LoadSnapshot(
            firstA, BuildSnapshot(), false, TimeSpan.Zero, null));
        Assert.False(coordinator.Fail(b, "B failed late"));
        Assert.True(overview.IsLoading);
        Assert.Equal("A final", overview.LoadingMessage);

        Assert.True(coordinator.LoadSnapshot(
            finalA, BuildSnapshot(), true, TimeSpan.Zero, null));
        Assert.True(coordinator.SetReady(finalA));
        Assert.False(coordinator.CompleteSecondaryDetails(
            firstA, BuildSnapshot(), false));
        Assert.False(overview.IsLoading);
        Assert.False(overview.HasError);
        Assert.True(overview.HasBeatmap);
    }

    [Fact]
    public void AtoB_HidesPreviousMapAndRejectsLateA()
    {
        var overview = new OverviewViewModel();
        var coordinator = new OverviewLoadCoordinator(overview);
        long a = coordinator.Begin("A");
        Assert.True(coordinator.Complete(a, BuildSnapshot()));

        long b = coordinator.Begin("B");
        Assert.False(overview.HasBeatmap);
        Assert.True(overview.IsLoading);
        Assert.False(coordinator.Fail(a, "Late A error"));
        Assert.False(coordinator.Complete(a, BuildSnapshot()));

        Assert.True(coordinator.Complete(b, BuildSnapshot()));
        Assert.True(overview.HasBeatmap);
        Assert.False(overview.IsLoading);
        Assert.False(overview.HasError);
    }
}
