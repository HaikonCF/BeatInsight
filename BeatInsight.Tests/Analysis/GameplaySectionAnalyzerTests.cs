using BeatInsight.Analysis.Sections;
using BeatInsight.Models;
using BeatInsight.Models.Sections;

namespace BeatInsight.Tests.Analysis;

public sealed class GameplaySectionAnalyzerTests
{
    [Fact]
    public void Analyze_AssignsEverySourceObjectExactlyOnce()
    {
        Beatmap map = Map(
            Circle(1_000),
            Slider(3_000),
            Spinner(8_500),
            Other(15_000),
            Circle(31_000));

        IReadOnlyList<GameplaySectionAnalysis> sections =
            GameplaySectionAnalyzer.Analyze(map);
        int[] assigned = sections
            .SelectMany(section => section.ObjectIndices)
            .ToArray();

        Assert.Equal(map.HitObjects.Count, assigned.Length);
        Assert.Equal(map.HitObjects.Count, assigned.Distinct().Count());
        Assert.Equal(Enumerable.Range(0, map.HitObjects.Count), assigned.Order());
    }

    [Fact]
    public void Analyze_AssignsExactInternalBoundaryToFollowingSection()
    {
        Beatmap map = Map(Circle(0), Circle(7_500), Circle(90_000));

        IReadOnlyList<GameplaySectionAnalysis> sections =
            GameplaySectionAnalyzer.Analyze(map);

        Assert.Equal(12, sections.Count);
        Assert.Equal(7_500, sections[0].EndTime);
        Assert.DoesNotContain(1, sections[0].ObjectIndices);
        Assert.Contains(1, sections[1].ObjectIndices);
    }

    [Fact]
    public void Analyze_AssignsFirstAndLastObjectsToOuterSections()
    {
        IReadOnlyList<GameplaySectionAnalysis> sections =
            GameplaySectionAnalyzer.Analyze(
                Map(Circle(-5_000), Circle(85_000)));

        Assert.Contains(0, sections[0].ObjectIndices);
        Assert.Contains(1, sections[^1].ObjectIndices);
        Assert.Equal(-5_000, sections[0].StartTime);
        Assert.Equal(85_000, sections[^1].EndTime);
    }

    [Fact]
    public void Analyze_ShortMapKeepsTwelveSectionsIncludingEmptyCells()
    {
        IReadOnlyList<GameplaySectionAnalysis> sections =
            GameplaySectionAnalyzer.Analyze(Map(Circle(10_000), Circle(70_000)));

        Assert.Equal(12, sections.Count);
        Assert.Contains(sections, section => section.ObjectCount == 0);
        AssertPartitionInvariants(sections, 10_000, 70_000);
    }

    [Fact]
    public void Analyze_VerySmallPositiveDurationStillCreatesTwelveSections()
    {
        IReadOnlyList<GameplaySectionAnalysis> sections =
            GameplaySectionAnalyzer.Analyze(Map(Circle(-1), Circle(0)));

        Assert.Equal(12, sections.Count);
        Assert.All(sections, section => Assert.True(section.Duration > 0));
        AssertPartitionInvariants(sections, -1, 0);
    }

    [Fact]
    public void Analyze_LongMapCapsSectionCountAtThirtyTwo()
    {
        IReadOnlyList<GameplaySectionAnalysis> sections =
            GameplaySectionAnalyzer.Analyze(Map(Circle(0), Circle(300_000)));

        Assert.Equal(32, sections.Count);
        AssertPartitionInvariants(sections, 0, 300_000);
    }

    [Theory]
    [InlineData(11.49, 12)]
    [InlineData(11.50, 12)]
    [InlineData(12.49, 12)]
    [InlineData(12.50, 13)]
    [InlineData(31.49, 31)]
    [InlineData(31.50, 32)]
    [InlineData(32.49, 32)]
    [InlineData(32.50, 32)]
    public void CalculateSectionCount_UsesAwayFromZeroMidpointRounding(
        double rawSectionCount,
        int expected)
    {
        double duration = rawSectionCount
            * GameplaySectionAnalyzer.TargetSectionDurationMilliseconds;

        Assert.Equal(
            expected,
            GameplaySectionAnalyzer.CalculateSectionCount(duration));
    }

    [Fact]
    public void Analyze_PreservesEmptySectionsAcrossLargeGap()
    {
        IReadOnlyList<GameplaySectionAnalysis> sections =
            GameplaySectionAnalyzer.Analyze(
                Map(Circle(0), Circle(1_000), Circle(89_000), Circle(90_000)));

        Assert.Equal(12, sections.Count);
        Assert.True(sections.Count(section => section.ObjectCount == 0) >= 8);
    }

    [Fact]
    public void Analyze_AssignsEqualTimestampsToSameSection()
    {
        IReadOnlyList<GameplaySectionAnalysis> sections =
            GameplaySectionAnalyzer.Analyze(
                Map(
                    Circle(0),
                    Circle(7_500),
                    Slider(7_500),
                    Spinner(7_500),
                    Circle(90_000)));

        GameplaySectionAnalysis containing = Assert.Single(
            sections,
            section => section.ObjectIndices.Contains(1));

        Assert.Equal(new[] { 1, 2, 3 }, containing.ObjectIndices);
    }

    [Fact]
    public void Analyze_OneObjectProducesOneZeroDurationSection()
    {
        IReadOnlyList<GameplaySectionAnalysis> sections =
            GameplaySectionAnalyzer.Analyze(Map(Circle(-250)));

        GameplaySectionAnalysis section = Assert.Single(sections);
        Assert.Equal(0, section.Index);
        Assert.Equal(-250, section.StartTime);
        Assert.Equal(-250, section.EndTime);
        Assert.Equal(0, section.Duration);
        Assert.Equal(new[] { 0 }, section.ObjectIndices);
    }

    [Fact]
    public void Analyze_ZeroDurationMapProducesOneSectionForAllObjects()
    {
        IReadOnlyList<GameplaySectionAnalysis> sections =
            GameplaySectionAnalyzer.Analyze(
                Map(Circle(500), Slider(500), Spinner(500)));

        GameplaySectionAnalysis section = Assert.Single(sections);
        Assert.Equal(new[] { 0, 1, 2 }, section.ObjectIndices);
        Assert.Equal(0, section.Duration);
        Assert.Equal(1, section.CircleCount);
        Assert.Equal(1, section.SliderCount);
        Assert.Equal(1, section.SpinnerCount);
    }

    [Fact]
    public void Analyze_EmptyMapProducesNoSections()
    {
        Assert.Empty(GameplaySectionAnalyzer.Analyze(new Beatmap()));
    }

    [Fact]
    public void Analyze_CountsPrimitiveObjectTypesWithoutGameplayMetrics()
    {
        IReadOnlyList<GameplaySectionAnalysis> sections =
            GameplaySectionAnalyzer.Analyze(
                Map(
                    Circle(0, newCombo: true),
                    Slider(0, newCombo: true),
                    Spinner(0, newCombo: true),
                    Other(0)));

        GameplaySectionAnalysis section = Assert.Single(sections);
        Assert.Equal(4, section.ObjectCount);
        Assert.Equal(1, section.CircleCount);
        Assert.Equal(1, section.SliderCount);
        Assert.Equal(1, section.SpinnerCount);
        Assert.Equal(1, section.OtherObjectCount);
        Assert.Equal(
            section.ObjectCount,
            section.CircleCount
                + section.SliderCount
                + section.SpinnerCount
                + section.OtherObjectCount);
    }

    [Fact]
    public void Analyze_RejectsUnsortedObjectsWithoutReorderingSource()
    {
        Beatmap map = Map(Circle(1_000), Circle(500));

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => GameplaySectionAnalyzer.Analyze(map));

        Assert.Equal("objects", exception.ParamName);
        Assert.Equal(new[] { 1_000, 500 }, map.HitObjects.Select(item => item.Time));
    }

    [Fact]
    public void CalculateSectionCount_RejectsNonPositiveOrNonFiniteDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GameplaySectionAnalyzer.CalculateSectionCount(0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GameplaySectionAnalyzer.CalculateSectionCount(-1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GameplaySectionAnalyzer.CalculateSectionCount(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GameplaySectionAnalyzer.CalculateSectionCount(
                double.PositiveInfinity));
    }

    private static void AssertPartitionInvariants(
        IReadOnlyList<GameplaySectionAnalysis> sections,
        double expectedStart,
        double expectedEnd)
    {
        Assert.InRange(
            sections.Count,
            GameplaySectionAnalyzer.MinimumSectionCount,
            GameplaySectionAnalyzer.MaximumSectionCount);
        Assert.Equal(expectedStart, sections[0].StartTime);
        Assert.Equal(expectedEnd, sections[^1].EndTime);

        for (int index = 0; index < sections.Count; index++)
        {
            GameplaySectionAnalysis section = sections[index];
            Assert.Equal(index, section.Index);
            Assert.True(section.StartTime <= section.EndTime);
            Assert.True(section.Duration >= 0);

            if (index + 1 < sections.Count)
            {
                Assert.Equal(
                    section.EndTime,
                    sections[index + 1].StartTime);
            }
        }
    }

    private static Beatmap Map(params HitObject[] objects) => new()
    {
        HitObjects = objects.ToList()
    };

    private static HitObject Circle(int time, bool newCombo = false) => new()
    {
        Time = time,
        Type = newCombo ? 5 : 1
    };

    private static HitObject Slider(int time, bool newCombo = false) => new()
    {
        Time = time,
        Type = newCombo ? 6 : 2
    };

    private static HitObject Spinner(int time, bool newCombo = false) => new()
    {
        Time = time,
        Type = newCombo ? 12 : 8
    };

    private static HitObject Other(int time) => new()
    {
        Time = time,
        Type = 128
    };
}
