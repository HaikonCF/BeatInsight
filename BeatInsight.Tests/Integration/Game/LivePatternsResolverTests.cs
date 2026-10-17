using BeatInsight.Integration.Game;
using BeatInsight.Models.Fusion;
using BeatInsight.Models.Sections;
using BeatInsight.Tests.ViewModels;

namespace BeatInsight.Tests.Integration.Game;

public sealed class LivePatternsResolverTests
{
    [Fact]
    public void CoverageMakesPatternEligibleEvenWhenOccurrenceCountIsZero()
    {
        GameplaySectionV2Analysis section = Section(
            jumps: Patterns(("OneTwo", Metric(0, .80, .80))));

        LivePatternItem item = Assert.Single(Resolve(section)!.Patterns);

        Assert.Equal("OneTwo", item.AnalyzerName);
        Assert.Equal("1-2 Jump", item.DisplayName);
        Assert.Equal(.80, item.Coverage, 10);
        Assert.Equal(80, item.Strength, 10);
    }

    [Theory]
    [InlineData(GameplayStructureKind.Jump, FusionPatternFamily.Jump, "OneTwo")]
    [InlineData(GameplayStructureKind.Stream, FusionPatternFamily.Stream, "Flow")]
    [InlineData(GameplayStructureKind.Tech, FusionPatternFamily.Tech, "SliderChain")]
    public void ClearDominantStructureUsesOnlyItsPatternFamily(
        GameplayStructureKind dominant,
        FusionPatternFamily expectedFamily,
        string expectedName)
    {
        GameplaySectionV2Analysis section = Section(
            dominant: dominant,
            jumps: Patterns(("OneTwo", Metric(1, .9, .9))),
            streams: Patterns(("Flow", Metric(1, .9, .9))),
            tech: Patterns(("SliderChain", Metric(1, .9, .9))));

        LivePatternItem item = Assert.Single(Resolve(section)!.Patterns);

        Assert.Equal(expectedFamily, item.Family);
        Assert.Equal(expectedName, item.AnalyzerName);
    }

    [Fact]
    public void ClassicMixedAllowsAllPatternFamilies()
    {
        GameplaySectionV2Analysis section = Section(
            dominant: GameplayStructureKind.ClassicMixed,
            jumps: Patterns(("OneTwo", Metric(1, .9, .9))),
            streams: Patterns(("Flow", Metric(1, .8, .8))),
            tech: Patterns(("SliderChain", Metric(1, .7, .7))));

        LivePatternsSnapshot snapshot = Resolve(section)!;

        Assert.Equal(
            [FusionPatternFamily.Jump, FusionPatternFamily.Stream, FusionPatternFamily.Tech],
            snapshot.Patterns.Select(item => item.Family));
    }

    [Fact]
    public void SecondaryStructureDoesNotExpandCandidatesForClearDominant()
    {
        GameplaySectionV2Analysis section = Section(
            dominant: GameplayStructureKind.Jump,
            secondary: GameplayStructureKind.Stream,
            jumps: Patterns(("OneTwo", Metric(1, .8, .8))),
            streams: Patterns(("Flow", Metric(1, 1, 1))));

        LivePatternItem item = Assert.Single(Resolve(section)!.Patterns);

        Assert.Equal(FusionPatternFamily.Jump, item.Family);
        Assert.Equal("OneTwo", item.AnalyzerName);
    }

    [Fact]
    public void MissingPatternInDominantFamilyDoesNotFallBackToAnotherFamily()
    {
        GameplaySectionV2Analysis section = Section(
            dominant: GameplayStructureKind.Jump,
            tech: Patterns(("SliderChain", Metric(1, 1, 1))));

        Assert.Empty(Resolve(section)!.Patterns);
    }

    [Fact]
    public void OneTwoIsExcludedOutsideJumpSectionsEvenWhenStrong()
    {
        GameplaySectionV2Analysis section = Section(
            dominant: GameplayStructureKind.Stream,
            jumps: Patterns(("OneTwo", Metric(1, 1, 1))),
            streams: Patterns(("Flow", Metric(1, .8, .8))));

        LivePatternItem item = Assert.Single(Resolve(section)!.Patterns);

        Assert.Equal("Flow", item.AnalyzerName);
        Assert.DoesNotContain(Resolve(section)!.Patterns, candidate => candidate.AnalyzerName == "OneTwo");
    }

    [Fact]
    public void ZeroCoverageIsExcludedEvenWhenOccurrenceCountIsPositive()
    {
        GameplaySectionV2Analysis section = Section(
            jumps: Patterns(("OneTwo", Metric(8, 0, 1))));

        Assert.Empty(Resolve(section)!.Patterns);
    }

    [Fact]
    public void SpacingTrendIsAlwaysExcluded()
    {
        GameplaySectionV2Analysis section = Section(
            jumps: Patterns(("SpacingTrend", Metric(4, 1, 1))));

        Assert.Empty(Resolve(section)!.Patterns);
    }

    [Fact]
    public void RankingUsesStrengthThenOrdinalAnalyzerNameAndKeepsAtMostThree()
    {
        GameplaySectionV2Analysis section = Section(
            jumps: Patterns(
                ("Sharp", Metric(1, .80, .80)),
                ("Edge", Metric(1, .80, .80)),
                ("Arc", Metric(1, .70, .70)),
                ("Triangle", Metric(1, .60, .60))));

        LivePatternsSnapshot snapshot = Resolve(section)!;

        Assert.Equal(3, snapshot.Patterns.Count);
        Assert.Equal(["Edge", "Sharp", "Arc"],
            snapshot.Patterns.Select(item => item.AnalyzerName));
    }

    [Fact]
    public void ThresholdIsMaximumOfFortyAndFiftyFivePercentOfTopStrength()
    {
        GameplaySectionV2Analysis section = Section(
            jumps: Patterns(
                ("Edge", Metric(1, 1, 1)),
                ("Sharp", Metric(1, .55, .55)),
                ("Triangle", Metric(1, .54, .54)),
                ("Arc", Metric(1, .39, .39))));

        Assert.Equal(["Edge", "Sharp"], Resolve(section)!.Patterns
            .Select(item => item.AnalyzerName));
    }

    [Fact]
    public void IntensityUsesExistingFiftyThirtyTwentyStrengthFormula()
    {
        GameplaySectionV2Analysis section = Section(
            dominant: GameplayStructureKind.Tech,
            tech: Patterns(("Overlap", new SectionPatternMetricV2(1, 1, .6, .4, .8))));

        LivePatternItem item = Assert.Single(Resolve(section)!.Patterns);

        Assert.Equal(58, item.Strength, 10);
    }

    [Theory]
    [InlineData("OneTwo", FusionPatternFamily.Jump, "1-2 Jump")]
    [InlineData("Spaced", FusionPatternFamily.Jump, "Spaced Jump")]
    [InlineData("WideAngle", FusionPatternFamily.Jump, "Wide Angle")]
    [InlineData("SliderDirectionChange", FusionPatternFamily.Tech, "Slider Direction Change")]
    [InlineData("CircleSliderTransition", FusionPatternFamily.Tech, "Circle-Slider Transition")]
    public void DisplayNamesArePresentationOnly(
        string analyzerName,
        FusionPatternFamily family,
        string expected) =>
        Assert.Equal(expected, LivePatternDisplayNames.Format(analyzerName, family));

    [Fact]
    public void ValidSectionBelowAbsoluteThresholdReturnsNeutralSnapshot()
    {
        GameplaySectionV2Analysis section = Section(
            dominant: GameplayStructureKind.Stream,
            streams: Patterns(("Flow", Metric(1, .39, .39))));

        LivePatternsSnapshot snapshot = Resolve(section)!;

        Assert.Same(section, snapshot.Section);
        Assert.Empty(snapshot.Patterns);
    }

    [Fact]
    public void NullCurrentSectionReturnsNull()
    {
        var resolver = new LivePatternsResolver();

        Assert.Null(resolver.Resolve(null));
    }

    [Fact]
    public void SameAnalyzerSectionReusesExactSnapshotAndCollection()
    {
        var resolver = new LivePatternsResolver();
        GameplaySectionV2Analysis section = Section(
            dominant: GameplayStructureKind.Stream,
            streams: Patterns(("Flow", Metric(1, .8, .8))));

        LivePatternsSnapshot first = resolver.Resolve(Current(section))!;
        LivePatternsSnapshot second = resolver.Resolve(Current(section))!;

        Assert.Same(first, second);
        Assert.Same(first.Patterns, second.Patterns);
    }

    [Fact]
    public void NewAnalyzerSectionCreatesNewProjection()
    {
        var resolver = new LivePatternsResolver();
        GameplaySectionV2Analysis firstSection = Section(
            jumps: Patterns(("OneTwo", Metric(1, .8, .8))));
        GameplaySectionV2Analysis secondSection = firstSection with { SectionIndex = 2 };

        LivePatternsSnapshot first = resolver.Resolve(Current(firstSection))!;
        LivePatternsSnapshot second = resolver.Resolve(Current(secondSection))!;

        Assert.NotSame(first, second);
        Assert.NotSame(first.Patterns, second.Patterns);
    }

    private static LivePatternsSnapshot? Resolve(GameplaySectionV2Analysis section) =>
        new LivePatternsResolver().Resolve(Current(section));

    private static CurrentSectionSnapshot Current(GameplaySectionV2Analysis section) => new(
        section,
        TimeSpan.FromMilliseconds(section.StartTime),
        TimeSpan.FromMilliseconds(section.EndTime),
        TimeSpan.FromMilliseconds(section.Duration),
        .5,
        TimeSpan.FromMilliseconds(section.Duration / 2));

    private static GameplaySectionV2Analysis Section(
        GameplayStructureKind dominant = GameplayStructureKind.Jump,
        GameplayStructureKind? secondary = null,
        IReadOnlyDictionary<string, SectionPatternMetricV2>? jumps = null,
        IReadOnlyDictionary<string, SectionPatternMetricV2>? streams = null,
        IReadOnlyDictionary<string, SectionPatternMetricV2>? tech = null)
    {
        GameplaySectionV2Analysis source =
            OverviewViewModelTests.BuildSnapshot().SectionAnalysisV2.Sections[0];
        return source with
        {
            SectionIndex = 1,
            StartTime = 1_000,
            EndTime = 6_000,
            DominantStructure = dominant,
            SecondaryStructure = secondary,
            JumpPatterns = Family(jumps),
            StreamPatterns = Family(streams),
            TechPatterns = Family(tech)
        };
    }

    private static SectionPatternFamilyV2 Family(
        IReadOnlyDictionary<string, SectionPatternMetricV2>? patterns) =>
        new(0, 0, 0, patterns ?? new Dictionary<string, SectionPatternMetricV2>());

    private static IReadOnlyDictionary<string, SectionPatternMetricV2> Patterns(
        params (string Name, SectionPatternMetricV2 Metric)[] items) =>
        items.ToDictionary(item => item.Name, item => item.Metric, StringComparer.Ordinal);

    private static SectionPatternMetricV2 Metric(
        int occurrenceCount,
        double coverage,
        double confidence) =>
        new(occurrenceCount, 0, coverage, confidence, null);
}
