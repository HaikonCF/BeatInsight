using BeatInsight.Models.Fusion;
using BeatInsight.Models.Identity;
using BeatInsight.ViewModels;

namespace BeatInsight.Tests.ViewModels;

public sealed class PatternsViewModelTests
{
    [Fact]
    public void LoadSnapshot_ProjectsRealPatternMetricsByFamilyAndRanksCoverage()
    {
        var overview = new OverviewViewModel();
        var patterns = new[]
        {
            Pattern("Spaced", FusionPatternFamily.Jump, .285, .82),
            Pattern("Sharp", FusionPatternFamily.Jump, .269, .77),
            Pattern("LongStream", FusionPatternFamily.Stream, .341, .91, .63),
            Pattern("FlowStream", FusionPatternFamily.Stream, .193, .72),
            Pattern("SliderChain", FusionPatternFamily.Tech, .112, .68),
            Pattern("RhythmTransition", FusionPatternFamily.Tech, .084, .74),
            Pattern("ZeroPattern", FusionPatternFamily.Tech, 0, 0, 0, score: 0)
        };
        var snapshot = OverviewViewModelTests.BuildSnapshot() with { PatternSummaries = patterns };

        overview.LoadSnapshot(snapshot);

        Assert.Equal(2, overview.Patterns.JumpPatterns.Count);
        Assert.Equal(2, overview.Patterns.StreamPatterns.Count);
        Assert.Equal(2, overview.Patterns.TechPatterns.Count);
        Assert.DoesNotContain(
            overview.Patterns.JumpPatterns
                .Concat(overview.Patterns.StreamPatterns)
                .Concat(overview.Patterns.TechPatterns),
            pattern => pattern.Name.Contains("Zero", StringComparison.Ordinal));

        PatternDisplayItemViewModel longStream = overview.Patterns.StreamPatterns[0];
        Assert.Equal("Long Stream", longStream.Name);
        Assert.Equal("Coverage 34.1%", longStream.CoverageDisplay);
        Assert.Equal("Confidence 0.91", longStream.ConfidenceDisplay);
        Assert.Equal("Intensity 0.63", longStream.IntensityDisplay);
        Assert.DoesNotContain('%', longStream.ConfidenceDisplay);

        Assert.Equal(
            ["Long Stream", "Spaced Jump", "Sharp Jump", "Flow Stream", "Slider Chain"],
            overview.Patterns.DominantPatterns.Select(pattern => pattern.Name));
        Assert.Equal(PatternsViewModel.MaximumDominantPatternCount, overview.Patterns.DominantPatterns.Count);

        MainPatternChipViewModel identityPattern = Assert.Single(overview.Identity.MainPatterns);
        Assert.Equal("Flow Stream", identityPattern.Name);
    }

    [Fact]
    public void LoadSnapshot_ReplacesPatternsAndSupportsEmptyState()
    {
        var overview = new OverviewViewModel();
        var first = OverviewViewModelTests.BuildSnapshot() with
        {
            PatternSummaries =
            [
                Pattern("Spaced", FusionPatternFamily.Jump, .28, .8),
                Pattern("Burst", FusionPatternFamily.Stream, .19, .7)
            ]
        };
        var second = OverviewViewModelTests.BuildSnapshot() with
        {
            PatternSummaries =
            [
                Pattern("Overlap", FusionPatternFamily.Tech, .13, .65)
            ]
        };

        overview.LoadSnapshot(first);
        overview.LoadSnapshot(second);

        Assert.Empty(overview.Patterns.JumpPatterns);
        Assert.Empty(overview.Patterns.StreamPatterns);
        PatternDisplayItemViewModel overlap = Assert.Single(overview.Patterns.TechPatterns);
        Assert.Equal("Overlap", overlap.Name);
        Assert.Single(overview.Patterns.DominantPatterns);

        overview.LoadSnapshot(second with { PatternSummaries = [] });

        Assert.False(overview.Patterns.HasPatterns);
        Assert.Empty(overview.Patterns.DominantPatterns);
        Assert.Empty(overview.Patterns.TechPatterns);
    }

    [Fact]
    public void Navigation_ActivatesPatternsWithoutChangingSnapshotData()
    {
        var viewModel = new MainWindowViewModel();
        viewModel.Overview.LoadSnapshot(OverviewViewModelTests.BuildSnapshot());
        IdentityViewModel identity = viewModel.Overview.Identity;
        PatternDisplayItemViewModel dominant = Assert.Single(viewModel.Overview.Patterns.DominantPatterns);

        viewModel.SelectedTab = NavigationTab.Patterns;

        Assert.True(viewModel.IsPatternsTabActive);
        Assert.False(viewModel.IsOverviewTabActive);
        Assert.False(viewModel.IsSkillsTabActive);
        Assert.False(viewModel.IsSectionsTabActive);
        Assert.False(viewModel.IsIdentityTabActive);
        Assert.False(viewModel.IsDifficultyTabActive);
        Assert.Same(identity, viewModel.Overview.Identity);
        Assert.Same(dominant, Assert.Single(viewModel.Overview.Patterns.DominantPatterns));
    }

    private static GameplayIdentityV3Pattern Pattern(
        string name,
        FusionPatternFamily family,
        double coverage,
        double confidence,
        double? intensity = null,
        double score = 80) =>
        new(name, family, score, coverage, confidence, intensity);
}
