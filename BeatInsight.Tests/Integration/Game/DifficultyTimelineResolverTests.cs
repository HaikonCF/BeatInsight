using BeatInsight.Integration.Game;
using BeatInsight.Models.AnalysisCache;
using BeatInsight.Models.Fusion;
using BeatInsight.Models.Sections;
using BeatInsight.Tests.ViewModels;

namespace BeatInsight.Tests.Integration.Game;

public sealed class DifficultyTimelineResolverTests
{
    [Fact]
    public void ContiguousSectionsProduceExpectedRatios()
    {
        BeatmapAnalysisSnapshot analysis = Analysis(
            Section(0, 0, 5_000),
            Section(1, 5_000, 10_000),
            Section(2, 10_000, 15_000));

        DifficultyTimelineModel model = Resolve(analysis, 1_000).Model;

        Assert.Equal(0, model.AxisStart);
        Assert.Equal(15_000, model.AxisEnd);
        Assert.Equal(0, model.Segments[0].StartRatio);
        Assert.Equal(1d / 3, model.Segments[0].EndRatio, 12);
        Assert.Equal(1d / 3, model.Segments[1].StartRatio, 12);
        Assert.Equal(2d / 3, model.Segments[1].EndRatio, 12);
        Assert.Equal(1, model.Segments[2].EndRatio);
    }

    [Fact]
    public void DifferentDurationsProduceDifferentRelativeWidths()
    {
        DifficultyTimelineModel model = Resolve(Analysis(
            Section(0, 0, 2_000),
            Section(1, 2_000, 8_000),
            Section(2, 8_000, 10_000)), 1_000).Model;

        Assert.Equal(0.2, Width(model.Segments[0]), 12);
        Assert.Equal(0.6, Width(model.Segments[1]), 12);
        Assert.Equal(0.2, Width(model.Segments[2]), 12);
    }

    [Fact]
    public void NonZeroAxisStartIsUsedDirectly()
    {
        DifficultyTimelineModel model = Resolve(Analysis(
            Section(0, 1_000, 3_000),
            Section(1, 3_000, 11_000)), 2_000).Model;

        Assert.Equal(1_000, model.AxisStart);
        Assert.Equal(11_000, model.AxisEnd);
        Assert.Equal(0.2, model.Segments[0].EndRatio, 12);
        Assert.Equal(0.2, model.Segments[1].StartRatio, 12);
    }

    [Fact]
    public void EmptySectionsRemainInTimeline()
    {
        DifficultyTimelineModel model = Resolve(Analysis(
            Section(0, 0, 5_000),
            Section(1, 5_000, 10_000, playable: false),
            Section(2, 10_000, 15_000)), 1_000).Model;

        Assert.Equal(3, model.Segments.Count);
        Assert.True(model.Segments[1].IsEmpty);
        Assert.Equal(0, model.Segments[1].ObjectCount);
        Assert.Equal(1d / 3, Width(model.Segments[1]), 12);
    }

    [Theory]
    [InlineData(double.NaN, 5_000)]
    [InlineData(0, double.PositiveInfinity)]
    [InlineData(5_000, 5_000)]
    public void InvalidTimestampsFailClosed(double start, double end)
    {
        var resolver = new DifficultyTimelineResolver();

        Assert.Null(resolver.Resolve(Live(Analysis(Section(0, start, end)), 1_000), null));
    }

    [Fact]
    public void OverlapFailsClosed()
    {
        var resolver = new DifficultyTimelineResolver();

        Assert.Null(resolver.Resolve(Live(Analysis(
            Section(0, 0, 6_000),
            Section(1, 5_000, 10_000)), 1_000), null));
    }

    [Fact]
    public void InvalidOrderFailsClosed()
    {
        var resolver = new DifficultyTimelineResolver();

        Assert.Null(resolver.Resolve(Live(Analysis(
            Section(1, 5_000, 10_000),
            Section(0, 0, 5_000)), 1_000), null));
    }

    [Fact]
    public void DifficultyScoresMapToAbsoluteZeroToOneRatios()
    {
        DifficultyTimelineModel model = Resolve(Analysis(
            Section(0, 0, 5_000, difficulty: 0),
            Section(1, 5_000, 10_000, difficulty: 50),
            Section(2, 10_000, 15_000, difficulty: 100)), 1_000).Model;

        Assert.Equal(0, model.Segments[0].DifficultyRatio);
        Assert.Equal(0.5, model.Segments[1].DifficultyRatio);
        Assert.Equal(1, model.Segments[2].DifficultyRatio);
    }

    [Fact]
    public void FiniteOutOfRangeDifficultyIsVisuallyClamped()
    {
        DifficultyTimelineModel model = Resolve(Analysis(
            Section(0, 0, 5_000, difficulty: -0.01),
            Section(1, 5_000, 10_000, difficulty: 100.01)), 1_000).Model;

        Assert.Equal(0, model.Segments[0].DifficultyRatio);
        Assert.Equal(1, model.Segments[1].DifficultyRatio);
        Assert.Equal(-0.01, model.Segments[0].LocalDifficultyScore);
        Assert.Equal(100.01, model.Segments[1].LocalDifficultyScore);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NonFiniteDifficultyFailsClosed(double difficulty)
    {
        var resolver = new DifficultyTimelineResolver();

        Assert.Null(resolver.Resolve(Live(Analysis(
            Section(0, 0, 5_000, difficulty: difficulty)), 1_000), null));
    }

    [Theory]
    [InlineData(500, 0)]
    [InlineData(6_000, 0.5)]
    [InlineData(20_000, 1)]
    public void MarkerUsesAbsoluteSectionAxisAndClamps(
        double currentMilliseconds,
        double expectedRatio)
    {
        DifficultyTimelineSnapshot snapshot = Resolve(Analysis(
            Section(0, 1_000, 6_000),
            Section(1, 6_000, 11_000)), currentMilliseconds);

        Assert.Equal(expectedRatio, snapshot.MarkerRatio, 12);
    }

    [Fact]
    public void SameAnalysisReferenceReusesModelAndSegmentsCollection()
    {
        BeatmapAnalysisSnapshot analysis = Analysis(
            Section(0, 0, 5_000),
            Section(1, 5_000, 10_000));
        var resolver = new DifficultyTimelineResolver();

        DifficultyTimelineSnapshot first = Assert.IsType<DifficultyTimelineSnapshot>(
            resolver.Resolve(Live(analysis, 1_000), null));
        DifficultyTimelineSnapshot second = Assert.IsType<DifficultyTimelineSnapshot>(
            resolver.Resolve(Live(analysis, 2_000), null));

        Assert.Same(first.Model, second.Model);
        Assert.Same(first.Model.Segments, second.Model.Segments);
        Assert.NotEqual(first.MarkerRatio, second.MarkerRatio);
    }

    [Fact]
    public void NewAnalysisReferenceBuildsNewModel()
    {
        BeatmapAnalysisSnapshot firstAnalysis = Analysis(Section(0, 0, 5_000));
        BeatmapAnalysisSnapshot secondAnalysis = Analysis(Section(0, 0, 5_000));
        var resolver = new DifficultyTimelineResolver();

        DifficultyTimelineSnapshot first = Assert.IsType<DifficultyTimelineSnapshot>(
            resolver.Resolve(Live(firstAnalysis, 1_000), null));
        DifficultyTimelineSnapshot second = Assert.IsType<DifficultyTimelineSnapshot>(
            resolver.Resolve(Live(secondAnalysis, 1_000), null));

        Assert.NotSame(first.Model, second.Model);
        Assert.NotSame(first.Model.Segments, second.Model.Segments);
    }

    [Fact]
    public void CurrentSectionReferenceProvidesHighlightIndex()
    {
        GameplaySectionV2Analysis section = Section(7, 0, 5_000);
        BeatmapAnalysisSnapshot analysis = Analysis(section);
        var current = new CurrentSectionSnapshot(
            section,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5),
            0.2,
            TimeSpan.FromSeconds(4));

        DifficultyTimelineSnapshot snapshot = Assert.IsType<DifficultyTimelineSnapshot>(
            new DifficultyTimelineResolver().Resolve(Live(analysis, 1_000), current));

        Assert.Equal(7, snapshot.CurrentSectionIndex);
    }

    [Fact]
    public void GapOrForeignCurrentSectionProducesNoHighlight()
    {
        BeatmapAnalysisSnapshot analysis = Analysis(Section(0, 0, 5_000));
        GameplaySectionV2Analysis foreign = Section(9, 0, 5_000);
        var current = new CurrentSectionSnapshot(
            foreign,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5),
            0.2,
            TimeSpan.FromSeconds(4));

        DifficultyTimelineSnapshot snapshot = Assert.IsType<DifficultyTimelineSnapshot>(
            new DifficultyTimelineResolver().Resolve(Live(analysis, 1_000), current));

        Assert.Null(snapshot.CurrentSectionIndex);
    }

    [Fact]
    public void MissingSectionsAndNonGameplayFailClosed()
    {
        BeatmapAnalysisSnapshot empty = Analysis();
        var resolver = new DifficultyTimelineResolver();

        Assert.Null(resolver.Resolve(Live(empty, 0), null));
        Assert.Null(resolver.Resolve(Live(Analysis(Section(0, 0, 5_000)), 1_000) with
        {
            IsGameplay = false
        }, null));
    }

    private static double Width(DifficultyTimelineSegment segment) =>
        segment.EndRatio - segment.StartRatio;

    private static DifficultyTimelineSnapshot Resolve(
        BeatmapAnalysisSnapshot analysis,
        double currentMilliseconds) =>
        Assert.IsType<DifficultyTimelineSnapshot>(
            new DifficultyTimelineResolver().Resolve(
                Live(analysis, currentMilliseconds),
                null));

    private static LiveGameplaySnapshot Live(
        BeatmapAnalysisSnapshot analysis,
        double currentMilliseconds)
    {
        TimeSpan current = TimeSpan.FromMilliseconds(currentMilliseconds);
        TimeSpan duration = TimeSpan.FromMinutes(2);
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

    private static BeatmapAnalysisSnapshot Analysis(
        params GameplaySectionV2Analysis[] sections)
    {
        BeatmapAnalysisSnapshot source = OverviewViewModelTests.BuildSnapshot();
        return source with
        {
            SectionAnalysisV2 = source.SectionAnalysisV2 with { Sections = sections }
        };
    }

    private static GameplaySectionV2Analysis Section(
        int index,
        double start,
        double end,
        bool playable = true,
        double difficulty = 50)
    {
        GameplaySectionV2Analysis source =
            OverviewViewModelTests.BuildSnapshot().SectionAnalysisV2.Sections[0];
        return source with
        {
            SectionIndex = index,
            StartTime = start,
            EndTime = end,
            ObjectIndices = playable ? [index] : [],
            LocalDifficultyScore = difficulty
        };
    }
}
