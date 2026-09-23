using BeatInsight.Models.Sections;
using BeatInsight.ViewModels;

namespace BeatInsight.Tests.ViewModels;

public sealed class DifficultyAnalysisViewModelTests
{
    [Fact]
    public void LoadSnapshot_UsesRatingV2AndOsuStarRatingWithoutRecalculation()
    {
        var overview = new OverviewViewModel();

        overview.LoadSnapshot(OverviewViewModelTests.BuildSnapshot(
            ratingScore: 7.26,
            ratingConfidence: .61));

        Assert.Equal("7.3", overview.BeatInsightRatingDisplay);
        Assert.Equal("61 %", overview.BeatInsightRatingConfidenceDisplay);
        Assert.Equal("6.50 ★", overview.OsuStarRatingDisplay);
        Assert.Equal(
            [
                "Base difficulty",
                "Sustained difficulty",
                "Peak difficulty",
                "Skill pressure",
                "Pattern complexity",
                "Endurance",
                "Specialization contribution"
            ],
            overview.Difficulty.RatingBreakdown.Select(item => item.Name));
        Assert.Equal([4d, 5d, 6d, 1d, .5d, .3d, .2d],
            overview.Difficulty.RatingBreakdown.Select(item => item.Value));
        Assert.Equal("4.0 / 100", overview.Difficulty.RatingBreakdown[0].ValueDisplay);
        Assert.Equal("0.20 rating pts", overview.Difficulty.RatingBreakdown[^1].ValueDisplay);
    }

    [Fact]
    public void LoadSnapshot_ProjectsTopFiveExistingSectionsInDescendingOrder()
    {
        var overview = new OverviewViewModel();
        var source = OverviewViewModelTests.BuildSnapshot();
        double[] difficulty = [20, 90, 50, 80, 70, 60];
        GameplaySectionV2Analysis[] sections = difficulty
            .Select((value, index) => source.SectionAnalysisV2.Sections[0] with
            {
                SectionIndex = index,
                StartTime = index * 5000,
                EndTime = (index + 1) * 5000,
                LocalDifficultyScore = value,
                Spike = index == 1
                    ? new SectionDifficultySpike(true, 40, 50, 2.25, .9, ["Peak"])
                    : SectionDifficultySpike.None
            })
            .ToArray();
        var snapshot = source with
        {
            SectionAnalysisV2 = source.SectionAnalysisV2 with
            {
                Sections = sections,
                DifficultySpikeCount = 1
            }
        };

        overview.LoadSnapshot(snapshot);

        Assert.Equal([1, 3, 4, 5, 2],
            overview.Difficulty.TopSections.Select(section => section.Index));
        Assert.True(overview.Difficulty.TopSections[0].IsSpike);
        Assert.Equal("SECTION 2", overview.Difficulty.TopSections[0].SectionNumberDisplay);
        Assert.Equal("00:05 - 00:10", overview.Difficulty.TopSections[0].TimeRangeDisplay);
        Assert.Equal("1 significant difficulty spike detected", overview.SpikesSummaryText);
    }

    [Fact]
    public void LoadSnapshot_ReplacesDifficultyProjectionAndSupportsNoSpikeAndEmptyStates()
    {
        var overview = new OverviewViewModel();
        var source = OverviewViewModelTests.BuildSnapshot();
        GameplaySectionV2Analysis replacement = source.SectionAnalysisV2.Sections[0] with
        {
            SectionIndex = 8,
            LocalDifficultyScore = 12,
            Spike = SectionDifficultySpike.None
        };
        var replacementSnapshot = source with
        {
            SectionAnalysisV2 = source.SectionAnalysisV2 with
            {
                Sections = [replacement],
                DifficultySpikeCount = 0
            }
        };

        overview.LoadSnapshot(source);
        overview.LoadSnapshot(replacementSnapshot);

        SectionTimelineItemViewModel top = Assert.Single(overview.Difficulty.TopSections);
        Assert.Equal(8, top.Index);
        Assert.Equal(12, top.LocalDifficulty);
        Assert.Equal("No significant difficulty spikes detected", overview.SpikesSummaryText);

        overview.LoadSnapshot(replacementSnapshot with
        {
            SectionAnalysisV2 = replacementSnapshot.SectionAnalysisV2 with { Sections = [] }
        });

        Assert.False(overview.Difficulty.HasTopSections);
        Assert.Empty(overview.Difficulty.TopSections);
        Assert.Equal("No section data available", overview.SpikesSummaryText);
    }

    [Fact]
    public void Navigation_ActivatesDifficultyWithoutMutatingExistingAnalysisData()
    {
        var viewModel = new MainWindowViewModel();
        viewModel.Overview.LoadSnapshot(OverviewViewModelTests.BuildSnapshot());
        RatingBreakdownItemViewModel firstBreakdown = viewModel.Overview.Difficulty.RatingBreakdown[0];
        SectionTimelineItemViewModel firstTopSection = viewModel.Overview.Difficulty.TopSections[0];

        viewModel.SelectedTab = NavigationTab.Difficulty;

        Assert.True(viewModel.IsDifficultyTabActive);
        Assert.False(viewModel.IsOverviewTabActive);
        Assert.False(viewModel.IsSkillsTabActive);
        Assert.False(viewModel.IsSectionsTabActive);
        Assert.False(viewModel.IsPatternsTabActive);
        Assert.False(viewModel.IsIdentityTabActive);
        Assert.Same(firstBreakdown, viewModel.Overview.Difficulty.RatingBreakdown[0]);
        Assert.Same(firstTopSection, viewModel.Overview.Difficulty.TopSections[0]);
    }
}
