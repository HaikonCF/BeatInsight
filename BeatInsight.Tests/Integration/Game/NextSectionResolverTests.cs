using BeatInsight.Integration.Game;
using BeatInsight.Models.AnalysisCache;
using BeatInsight.Models.Fusion;
using BeatInsight.Models.Sections;
using BeatInsight.Tests.ViewModels;

namespace BeatInsight.Tests.Integration.Game;

public sealed class NextSectionResolverTests
{
    [Theory]
    [InlineData(1_000, 1)]
    [InlineData(5_000, 2)]
    [InlineData(9_999, 2)]
    public void SelectsFirstPlayableSectionStartingStrictlyAfterCurrentTime(
        double currentMilliseconds,
        int expectedIndex)
    {
        GameplaySectionV2Analysis[] sections =
        [
            Section(0, 0, 5_000),
            Section(1, 5_000, 10_000),
            Section(2, 10_000, 15_000)
        ];

        NextSectionSnapshot result = Assert.IsType<NextSectionSnapshot>(
            NextSectionResolver.Resolve(Live(currentMilliseconds, sections)));

        Assert.Equal(expectedIndex, result.Index);
    }

    [Fact]
    public void ExactStartOfLastSectionHasNoNextSection()
    {
        GameplaySectionV2Analysis[] sections =
        [
            Section(0, 0, 5_000),
            Section(1, 5_000, 10_000),
            Section(2, 10_000, 15_000)
        ];

        Assert.Null(NextSectionResolver.Resolve(Live(10_000, sections)));
    }

    [Fact]
    public void EmptyBreakSectionIsSkipped()
    {
        GameplaySectionV2Analysis[] sections =
        [
            Section(0, 0, 5_000),
            Section(1, 5_000, 10_000, playable: false),
            Section(2, 10_000, 15_000)
        ];

        NextSectionSnapshot result = Assert.IsType<NextSectionSnapshot>(
            NextSectionResolver.Resolve(Live(1_000, sections)));

        Assert.Equal(2, result.Index);
    }

    [Fact]
    public void ConsecutiveBreakSectionsAreSkipped()
    {
        GameplaySectionV2Analysis[] sections =
        [
            Section(0, 0, 5_000),
            Section(1, 5_000, 10_000, playable: false),
            Section(2, 10_000, 15_000, playable: false),
            Section(3, 15_000, 20_000)
        ];

        NextSectionSnapshot result = Assert.IsType<NextSectionSnapshot>(
            NextSectionResolver.Resolve(Live(4_000, sections)));

        Assert.Equal(3, result.Index);
    }

    [Fact]
    public void GapSelectsFirstFuturePlayableSection()
    {
        GameplaySectionV2Analysis[] sections =
        [
            Section(0, 0, 4_000),
            Section(1, 8_000, 13_000)
        ];

        NextSectionSnapshot result = Assert.IsType<NextSectionSnapshot>(
            NextSectionResolver.Resolve(Live(6_000, sections)));

        Assert.Equal(1, result.Index);
        Assert.Equal(TimeSpan.FromSeconds(2), result.TimeUntilStart);
    }

    [Fact]
    public void BeforeFirstSectionSelectsFirstPlayableSection()
    {
        GameplaySectionV2Analysis first = Section(0, 2_000, 7_000);

        NextSectionSnapshot result = Assert.IsType<NextSectionSnapshot>(
            NextSectionResolver.Resolve(Live(500, [first])));

        Assert.Same(first, result.Section);
    }

    [Fact]
    public void AfterLastSectionHasNoNextSection() =>
        Assert.Null(NextSectionResolver.Resolve(Live(
            20_000,
            [Section(0, 0, 5_000), Section(1, 5_000, 10_000)])));

    [Fact]
    public void AllEmptySectionsHaveNoNextSection() =>
        Assert.Null(NextSectionResolver.Resolve(Live(
            1_000,
            [
                Section(0, 0, 5_000, playable: false),
                Section(1, 5_000, 10_000, playable: false)
            ])));

    [Fact]
    public void OverlapFailsClosed() =>
        Assert.Null(NextSectionResolver.Resolve(Live(
            1_000,
            [Section(0, 0, 6_000), Section(1, 5_000, 10_000)])));

    [Fact]
    public void UnorderedSectionsFailClosed() =>
        Assert.Null(NextSectionResolver.Resolve(Live(
            1_000,
            [Section(1, 5_000, 10_000), Section(0, 0, 5_000)])));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteSectionBoundsFailClosed(double invalidStart)
    {
        Assert.Null(NextSectionResolver.Resolve(Live(
            1_000,
            [Section(0, invalidStart, 5_000)])));
    }

    [Fact]
    public void NegativeCurrentTimeFailsClosed() =>
        Assert.Null(NextSectionResolver.Resolve(Live(-1, [Section(0, 1_000, 5_000)])));

    [Fact]
    public void InvalidSectionBoundsFailClosed() =>
        Assert.Null(NextSectionResolver.Resolve(Live(
            1_000,
            [Section(0, 5_000, 5_000)])));

    [Fact]
    public void NullMissingAndNonGameplayInputsFailClosed()
    {
        Assert.Null(NextSectionResolver.Resolve(null));
        Assert.Null(NextSectionResolver.Resolve(Live(1_000, [])));
        Assert.Null(NextSectionResolver.Resolve(
            Live(1_000, [Section(0, 2_000, 7_000)]) with { IsGameplay = false }));
    }

    [Fact]
    public void SnapshotPreservesExactTimingAndAnalyzerOwnedIdentity()
    {
        GameplaySectionV2Analysis section = Section(
            7,
            12_345.5,
            17_890.25,
            GameplayStructureKind.Tech,
            GameplayStructureKind.Stream);

        NextSectionSnapshot result = Assert.IsType<NextSectionSnapshot>(
            NextSectionResolver.Resolve(Live(1_234.25, [section])));

        Assert.Same(section, result.Section);
        Assert.Equal(TimeSpan.FromMilliseconds(12_345.5), result.StartTime);
        Assert.Equal(TimeSpan.FromMilliseconds(17_890.25), result.EndTime);
        Assert.Equal(TimeSpan.FromMilliseconds(5_544.75), result.Duration);
        Assert.Equal(TimeSpan.FromMilliseconds(11_111.25), result.TimeUntilStart);
        Assert.Equal(GameplayStructureKind.Tech, result.DominantStructure);
        Assert.Equal(GameplayStructureKind.Stream, result.SecondaryStructure);
        Assert.Equal(section.Summary, result.Summary);
    }

    private static LiveGameplaySnapshot Live(
        double currentMilliseconds,
        IReadOnlyList<GameplaySectionV2Analysis> sections)
    {
        BeatmapAnalysisSnapshot source = OverviewViewModelTests.BuildSnapshot();
        BeatmapAnalysisSnapshot analysis = source with
        {
            SectionAnalysisV2 = source.SectionAnalysisV2 with { Sections = sections }
        };
        TimeSpan duration = TimeSpan.FromMinutes(2);
        TimeSpan current = TimeSpan.FromMilliseconds(currentMilliseconds);
        return new(
            true,
            current,
            duration,
            current.TotalMilliseconds / duration.TotalMilliseconds,
            analysis,
            123,
            456,
            "42@generation",
            DateTimeOffset.UtcNow);
    }

    private static GameplaySectionV2Analysis Section(
        int index,
        double start,
        double end,
        GameplayStructureKind dominant = GameplayStructureKind.Jump,
        GameplayStructureKind? secondary = null,
        bool playable = true)
    {
        GameplaySectionV2Analysis source =
            OverviewViewModelTests.BuildSnapshot().SectionAnalysisV2.Sections[0];
        return source with
        {
            SectionIndex = index,
            StartTime = start,
            EndTime = end,
            ObjectIndices = playable ? [index] : [],
            DominantStructure = dominant,
            SecondaryStructure = secondary,
            Summary = $"Section {index}: {dominant}"
        };
    }
}
