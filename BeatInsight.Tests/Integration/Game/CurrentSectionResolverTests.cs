using BeatInsight.Integration.Game;
using BeatInsight.Models.AnalysisCache;
using BeatInsight.Models.Fusion;
using BeatInsight.Models.Sections;
using BeatInsight.Tests.ViewModels;
using BeatInsight.Services.AnalysisCache;
using System.IO;

namespace BeatInsight.Tests.Integration.Game;

public sealed class CurrentSectionResolverTests
{
    [Fact]
    public void BeginningOfFirstSectionSelectsFirstSection()
    {
        CurrentSectionSnapshot result = Resolve(1_000);

        Assert.Equal(0, result.SectionIndex);
        Assert.Equal(TimeSpan.FromMilliseconds(1_000), result.StartTime);
        Assert.Equal(0, result.LocalProgress);
    }

    [Fact]
    public void MiddleOfSectionCalculatesLocalPosition()
    {
        CurrentSectionSnapshot result = Resolve(3_500);

        Assert.Equal(0, result.SectionIndex);
        Assert.Equal(TimeSpan.FromMilliseconds(6_000), result.EndTime);
        Assert.Equal(TimeSpan.FromMilliseconds(5_000), result.Duration);
        Assert.Equal(0.5, result.LocalProgress, 12);
        Assert.Equal(TimeSpan.FromMilliseconds(2_500), result.TimeRemaining);
    }

    [Fact]
    public void ExactContiguousBoundarySelectsFollowingSection()
    {
        CurrentSectionSnapshot result = Resolve(6_000);

        Assert.Equal(1, result.SectionIndex);
        Assert.Equal(0, result.LocalProgress);
    }

    [Fact]
    public void LastMillisecondBelongsToLastSection()
    {
        CurrentSectionSnapshot result = Resolve(10_999);

        Assert.Equal(1, result.SectionIndex);
        Assert.Equal(0.9998, result.LocalProgress, 12);
        Assert.Equal(TimeSpan.FromMilliseconds(1), result.TimeRemaining);
    }

    [Fact]
    public void ExactFinalBoundaryHasNoSection() =>
        Assert.Null(CurrentSectionResolver.Resolve(Live(11_000)));

    [Fact]
    public void BeforeFirstSectionHasNoSection() =>
        Assert.Null(CurrentSectionResolver.Resolve(Live(999)));

    [Fact]
    public void AfterLastSectionBeforeBeatmapEndHasNoSection() =>
        Assert.Null(CurrentSectionResolver.Resolve(Live(12_000)));

    [Fact]
    public void GapHasNoSection()
    {
        GameplaySectionV2Analysis[] sections =
        [Section(0, 1_000, 5_000), Section(1, 6_000, 11_000)];

        Assert.Null(CurrentSectionResolver.Resolve(Live(5_500, sections)));
    }

    [Fact]
    public void OverlapIsAmbiguousAndFailsClosed()
    {
        GameplaySectionV2Analysis[] sections =
        [Section(0, 1_000, 7_000), Section(1, 6_000, 11_000)];

        Assert.Null(CurrentSectionResolver.Resolve(Live(6_500, sections)));
    }

    [Fact]
    public void UnorderedRangesFailClosed()
    {
        GameplaySectionV2Analysis second = Section(1, 6_000, 11_000);
        GameplaySectionV2Analysis first = Section(0, 1_000, 6_000);

        Assert.Null(CurrentSectionResolver.Resolve(Live(3_000, [second, first])));
    }

    [Fact]
    public void NonGameplaySnapshotFailsClosed() =>
        Assert.Null(CurrentSectionResolver.Resolve(Live(3_000) with { IsGameplay = false }));

    [Fact]
    public void NegativeCurrentTimeFailsClosed() =>
        Assert.Null(CurrentSectionResolver.Resolve(Live(-1)));

    [Theory]
    [InlineData(7_000, 0)]
    [InlineData(11_999, 0)]
    [InlineData(12_000, 1)]
    [InlineData(20_000, 2)]
    [InlineData(21_999, 2)]
    public void AbsoluteTimelineResolvesIndependentlyOfRelativeBeatmapDuration(
        double currentMilliseconds,
        int expectedSectionIndex)
    {
        GameplaySectionV2Analysis[] sections =
        [
            Section(0, 7_000, 12_000),
            Section(1, 12_000, 17_000),
            Section(2, 17_000, 22_000)
        ];

        CurrentSectionSnapshot result = Assert.IsType<CurrentSectionSnapshot>(
            CurrentSectionResolver.Resolve(Live(
                currentMilliseconds,
                sections,
                beatmapDurationMilliseconds: 15_000)));

        Assert.Equal(expectedSectionIndex, result.SectionIndex);
    }

    [Theory]
    [InlineData(22_000)]
    [InlineData(25_000)]
    public void AbsoluteTimelineAtOrAfterFinalEndHasNoSection(double currentMilliseconds)
    {
        GameplaySectionV2Analysis[] sections =
        [
            Section(0, 7_000, 12_000),
            Section(1, 12_000, 17_000),
            Section(2, 17_000, 22_000)
        ];

        Assert.Null(CurrentSectionResolver.Resolve(Live(
            currentMilliseconds,
            sections,
            beatmapDurationMilliseconds: 15_000)));
    }

    [Fact]
    public void NullSnapshotFailsClosed() =>
        Assert.Null(CurrentSectionResolver.Resolve(null));

    [Fact]
    public void MissingAnalysisFailsClosed() =>
        Assert.Null(CurrentSectionResolver.Resolve(Live(3_000) with { BeatmapAnalysis = null! }));

    [Fact]
    public void EmptySectionsFailClosed() =>
        Assert.Null(CurrentSectionResolver.Resolve(Live(3_000, [])));

    [Fact]
    public void InvalidSectionBoundsFailClosed()
    {
        GameplaySectionV2Analysis invalid = Section(0, 5_000, 5_000);

        Assert.Null(CurrentSectionResolver.Resolve(Live(5_000, [invalid])));
    }

    [Fact]
    public void ExistingAnalyzerSectionAndIdentityAreReusedWithoutReanalysis()
    {
        GameplaySectionV2Analysis section = Section(
            7,
            1_000,
            6_000,
            GameplayStructureKind.Tech,
            GameplayStructureKind.Stream);
        LiveGameplaySnapshot live = Live(3_000, [section]);

        CurrentSectionSnapshot result = Assert.IsType<CurrentSectionSnapshot>(
            CurrentSectionResolver.Resolve(live));

        Assert.Same(section, result.Section);
        Assert.Same(live.BeatmapAnalysis.SectionAnalysisV2.Sections[0], result.Section);
        Assert.Equal(GameplayStructureKind.Tech, result.DominantStructure);
        Assert.Equal(GameplayStructureKind.Stream, result.SecondaryStructure);
        Assert.Equal(section.Summary, result.Summary);
    }

    [Fact]
    public void AbsoluteBlackRoverSectionsResolveWhenFinalEndExceedsLengthSpan()
    {
        GameplaySectionV2Analysis[] sections =
        [
            Section(0, 1_022, 6_022),
            Section(1, 6_022, 11_022),
            Section(2, 11_022, 16_022),
            Section(3, 16_022, 21_022),
            Section(4, 21_022, 26_022),
            Section(5, 26_022, 31_022),
            Section(6, 31_022, 36_022),
            Section(7, 36_022, 41_022),
            Section(8, 41_022, 46_022),
            Section(9, 46_022, 51_022),
            Section(10, 51_022, 56_022),
            Section(11, 56_022, 61_022),
            Section(12, 61_022, 66_022),
            Section(13, 66_022, 71_022),
            Section(14, 71_022, 76_022),
            Section(15, 76_022, 81_022),
            Section(16, 81_022, 85_796)
        ];

        CurrentSectionSnapshot result = Assert.IsType<CurrentSectionSnapshot>(
            CurrentSectionResolver.Resolve(Live(
                30_000,
                sections,
                beatmapDurationMilliseconds: 84_773)));

        Assert.Equal(5, result.SectionIndex);
        Assert.Equal(TimeSpan.FromMilliseconds(26_022), result.StartTime);
        Assert.Equal(TimeSpan.FromMilliseconds(31_022), result.EndTime);
        Assert.Equal(0.7956, result.LocalProgress, 4);
    }

    [Fact]
    public void RealBlackRoverExtraSnapshotResolvesSectionAtThirtySeconds()
    {
        string fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Maps",
            "Vickeblanka - Black Rover (TV Size) (Sotarks) [Extra].osu");
        BeatmapAnalysisSnapshot analysis = BeatmapAnalysisSnapshotFactory.Analyze(fixturePath);
        double duration = TimeSpan.FromTicks(analysis.Metadata.LengthTicks).TotalMilliseconds;
        Assert.True(analysis.SectionAnalysisV2.Sections[^1].EndTime > duration);

        CurrentSectionSnapshot result = Assert.IsType<CurrentSectionSnapshot>(
            CurrentSectionResolver.Resolve(Live(
                30_000,
                analysis.SectionAnalysisV2.Sections,
                duration,
                analysis)));

        Assert.True(result.StartTime <= TimeSpan.FromSeconds(30));
        Assert.True(result.EndTime > TimeSpan.FromSeconds(30));
    }

    private static CurrentSectionSnapshot Resolve(double currentMilliseconds) =>
        Assert.IsType<CurrentSectionSnapshot>(
            CurrentSectionResolver.Resolve(Live(currentMilliseconds)));

    private static LiveGameplaySnapshot Live(
        double currentMilliseconds,
        IReadOnlyList<GameplaySectionV2Analysis>? sections = null,
        double beatmapDurationMilliseconds = 15_000,
        BeatmapAnalysisSnapshot? sourceAnalysis = null)
    {
        BeatmapAnalysisSnapshot source = sourceAnalysis ?? OverviewViewModelTests.BuildSnapshot();
        BeatmapAnalysisSnapshot analysis = source with
        {
            SectionAnalysisV2 = source.SectionAnalysisV2 with
            {
                Sections = sections ??
                [Section(0, 1_000, 6_000), Section(1, 6_000, 11_000)]
            }
        };
        TimeSpan duration = TimeSpan.FromMilliseconds(beatmapDurationMilliseconds);
        TimeSpan current = TimeSpan.FromMilliseconds(currentMilliseconds);
        return new(
            true,
            current,
            duration,
            currentMilliseconds / duration.TotalMilliseconds,
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
        GameplayStructureKind? secondary = null)
    {
        GameplaySectionV2Analysis source =
            OverviewViewModelTests.BuildSnapshot().SectionAnalysisV2.Sections[0];
        return source with
        {
            SectionIndex = index,
            StartTime = start,
            EndTime = end,
            DominantStructure = dominant,
            SecondaryStructure = secondary,
            Summary = $"Section {index}: {dominant}"
        };
    }
}
