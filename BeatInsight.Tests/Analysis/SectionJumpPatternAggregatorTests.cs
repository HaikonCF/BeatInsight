using BeatInsight.Analysis.Sections;
using BeatInsight.Models.JumpPatterns;
using BeatInsight.Models.Sections;

namespace BeatInsight.Tests.Analysis;

public sealed class SectionJumpPatternAggregatorTests
{
    [Fact]
    public void Aggregate_OccurrenceInsideOneSectionContributesOnlyThere()
    {
        var sections = new[] { Section(0, 0, 1), Section(1, 2, 3, 4, 5), Section(2, 6, 7) };

        IReadOnlyList<GameplaySectionAnalysis> result = Aggregate(
            sections,
            Match(JumpPatternType.Spaced, 2, 4));

        AssertMetric(result[1].JumpPatterns.Spaced, 1, [2, 3, 4], 3d / 4);
        AssertMetric(result[0].JumpPatterns.Spaced, 0, [], 0);
        AssertMetric(result[2].JumpPatterns.Spaced, 0, [], 0);
    }

    [Fact]
    public void Aggregate_OccurrenceAcrossTwoSectionsSplitsCoverageButCountsStartOnce()
    {
        var sections = new[] { Section(0, 0, 1, 2), Section(1, 3, 4, 5) };

        IReadOnlyList<GameplaySectionAnalysis> result = Aggregate(
            sections,
            Match(JumpPatternType.Spaced, 0, 4));

        AssertMetric(result[0].JumpPatterns.Spaced, 1, [0, 1, 2], 1);
        AssertMetric(result[1].JumpPatterns.Spaced, 0, [3, 4], 2d / 3);
    }

    [Fact]
    public void Aggregate_OccurrenceAcrossThreeSectionsKeepsOnePrimarySequence()
    {
        var sections = new[] { Section(0, 0, 1), Section(1, 2, 3), Section(2, 4, 5) };

        IReadOnlyList<GameplaySectionAnalysis> result = Aggregate(
            sections,
            Match(JumpPatternType.Spaced, 1, 4));

        AssertMetric(result[0].JumpPatterns.Spaced, 1, [1], .5);
        AssertMetric(result[1].JumpPatterns.Spaced, 0, [2, 3], 1);
        AssertMetric(result[2].JumpPatterns.Spaced, 0, [4], .5);
        Assert.Equal(1, result.Sum(section => section.JumpPatterns.Spaced.SequenceCount));
    }

    [Fact]
    public void Aggregate_OverlappingOccurrencesDeduplicateObjectsWithinPattern()
    {
        var sections = new[] { Section(0, 0, 1, 2, 3, 4, 5) };

        IReadOnlyList<GameplaySectionAnalysis> result = Aggregate(
            sections,
            Match(JumpPatternType.Spaced, 0, 3),
            Match(JumpPatternType.Spaced, 2, 5));

        AssertMetric(result[0].JumpPatterns.Spaced, 2, [0, 1, 2, 3, 4, 5], 1);
    }

    [Fact]
    public void Aggregate_OrthogonalPatternsDoNotNormalizeEachOther()
    {
        var sections = new[] { Section(0, 0, 1, 2, 3) };

        GameplaySectionAnalysis result = Aggregate(
            sections,
            Match(JumpPatternType.Sharp, 0, 2),
            Match(JumpPatternType.Spaced, 0, 2))[0];

        AssertMetric(result.JumpPatterns.Sharp, 1, [0, 1, 2], .75);
        AssertMetric(result.JumpPatterns.Spaced, 1, [0, 1, 2], .75);
    }

    [Fact]
    public void Aggregate_EmptySectionHasPresentZeroProfile()
    {
        var sections = new[] { Section(0), Section(1, 0, 1) };

        GameplaySectionAnalysis empty = Aggregate(
            sections,
            Match(JumpPatternType.Linear, 0, 1))[0];

        Assert.All(Metrics(empty.JumpPatterns), metric =>
        {
            Assert.Equal(0, metric.SequenceCount);
            Assert.Equal(0, metric.ObjectCount);
            Assert.Equal(0, metric.Coverage);
        });
        Assert.Equal(0, empty.JumpPatterns.AcceleratingSequenceCount);
        Assert.Equal(0, empty.JumpPatterns.DeceleratingSequenceCount);
    }

    [Fact]
    public void Aggregate_ReversalRemainsIndependentSubsetOfSharp()
    {
        var sections = new[] { Section(0, 0, 1, 2, 3) };

        GameplaySectionAnalysis result = Aggregate(
            sections,
            Match(JumpPatternType.Sharp, 0, 3),
            Match(JumpPatternType.Reversal, 1, 2))[0];

        AssertMetric(result.JumpPatterns.Sharp, 1, [0, 1, 2, 3], 1);
        AssertMetric(result.JumpPatterns.Reversal, 1, [1, 2], .5);
    }

    [Fact]
    public void Aggregate_StartObjectOnTemporalBoundaryUsesPhaseOneAssignment()
    {
        var objects = new[] { TestHitObject(0), TestHitObject(7_500), TestHitObject(90_000) };
        IReadOnlyList<GameplaySectionAnalysis> sections = GameplaySectionAnalyzer.Analyze(objects);

        IReadOnlyList<GameplaySectionAnalysis> result = Aggregate(
            sections,
            Match(JumpPatternType.Linear, 1, 1));

        Assert.Equal(0, result[0].JumpPatterns.Linear.SequenceCount);
        Assert.Equal(1, result[1].JumpPatterns.Linear.SequenceCount);
    }

    [Fact]
    public void Aggregate_SpacingTrendCombinesCoverageAndPreservesDirectionCounts()
    {
        var sections = new[] { Section(0, 0, 1, 2, 3), Section(1, 4, 5, 6, 7) };

        IReadOnlyList<GameplaySectionAnalysis> result = Aggregate(
            sections,
            Match(JumpPatternType.Accelerating, 0, 2),
            Match(JumpPatternType.Decelerating, 3, 5));

        AssertMetric(result[0].JumpPatterns.SpacingTrend, 2, [0, 1, 2, 3], 1);
        Assert.Equal(1, result[0].JumpPatterns.AcceleratingSequenceCount);
        Assert.Equal(1, result[0].JumpPatterns.DeceleratingSequenceCount);
        AssertMetric(result[1].JumpPatterns.SpacingTrend, 0, [4, 5], .5);
    }

    [Fact]
    public void Aggregate_NoAlternatingSpacingStillReturnsZeroMetric()
    {
        GameplaySectionAnalysis section = Aggregate(
            [Section(0, 0, 1)],
            Match(JumpPatternType.Linear, 0, 1))[0];

        AssertMetric(section.JumpPatterns.AlternatingSpacing, 0, [], 0);
    }

    [Fact]
    public void Aggregate_AllPatternMetricsMaintainCoverageAndCountInvariants()
    {
        var sections = new[] { Section(0, 0, 1, 2), Section(1), Section(2, 3, 4, 5) };
        JumpPatternType[] types =
        [
            JumpPatternType.Linear, JumpPatternType.Edge, JumpPatternType.Sharp,
            JumpPatternType.Triangle, JumpPatternType.OneTwo, JumpPatternType.Spaced,
            JumpPatternType.WideAngle, JumpPatternType.Reversal, JumpPatternType.Arc,
            JumpPatternType.Accelerating, JumpPatternType.Decelerating,
            JumpPatternType.AlternatingSpacing
        ];

        IReadOnlyList<GameplaySectionAnalysis> result =
            SectionJumpPatternAggregator.Aggregate(
                sections,
                types.Select(type => Match(type, 1, 4)).ToArray());

        foreach (GameplaySectionAnalysis section in result)
        {
            foreach (SectionPatternMetric metric in Metrics(section.JumpPatterns))
            {
                Assert.InRange(metric.Coverage, 0, 1);
                Assert.True(metric.ObjectCount >= 0);
                Assert.True(metric.SequenceCount >= 0);
                Assert.Equal(metric.ObjectIndices.Count, metric.ObjectIndices.Distinct().Count());
                Assert.Equal(
                    section.ObjectCount == 0 ? 0 : (double)metric.ObjectCount / section.ObjectCount,
                    metric.Coverage,
                    12);
            }
        }
    }

    [Fact]
    public void Aggregate_GlobalUnionsAndSequenceTotalsEqualInputResults()
    {
        var sections = new[] { Section(0, 0, 1), Section(1, 2, 3), Section(2, 4, 5) };
        JumpPatternMatch[] matches =
        [
            Match(JumpPatternType.Spaced, 0, 3),
            Match(JumpPatternType.Spaced, 2, 5)
        ];

        IReadOnlyList<GameplaySectionAnalysis> result =
            SectionJumpPatternAggregator.Aggregate(sections, matches);
        int[] expected = matches
            .SelectMany(match => Enumerable.Range(match.StartObjectIndex, match.ObjectCount))
            .Distinct().Order().ToArray();
        int[] actual = result
            .SelectMany(section => section.JumpPatterns.Spaced.ObjectIndices)
            .Distinct().Order().ToArray();

        Assert.Equal(expected, actual);
        Assert.Equal(matches.Length, result.Sum(section => section.JumpPatterns.Spaced.SequenceCount));
    }

    [Fact]
    public void Aggregate_AverageConfidenceUsesSequencesStartingInSection()
    {
        var sections = new[] { Section(0, 0, 1, 2), Section(1, 3, 4, 5) };

        GameplaySectionAnalysis first = SectionJumpPatternAggregator.Aggregate(
            sections,
            [
                Match(JumpPatternType.Spaced, 0, 4, .25),
                Match(JumpPatternType.Spaced, 1, 5, .75)
            ])[0];

        Assert.Equal(.5, first.JumpPatterns.Spaced.AverageConfidence, 12);
        Assert.Equal(0, SectionJumpPatternAggregator.Aggregate(
            sections,
            [Match(JumpPatternType.Spaced, 0, 4, .25)])[1]
            .JumpPatterns.Spaced.AverageConfidence);
    }

    [Fact]
    public void Aggregate_RejectsOccurrenceIndexMissingFromPartition()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            Aggregate([Section(0, 0, 1)], Match(JumpPatternType.Spaced, 0, 2)));

        Assert.Equal("jumpPatternMatches", exception.ParamName);
    }

    private static IReadOnlyList<GameplaySectionAnalysis> Aggregate(
        IReadOnlyList<GameplaySectionAnalysis> sections,
        params JumpPatternMatch[] matches) =>
        SectionJumpPatternAggregator.Aggregate(sections, matches);

    private static JumpPatternMatch Match(
        JumpPatternType type,
        int start,
        int end,
        double confidence = .8) =>
        new(type, start, end, start * 100, end * 100, confidence);

    private static GameplaySectionAnalysis Section(int index, params int[] objectIndices) =>
        new(
            index,
            index * 1_000,
            (index + 1) * 1_000,
            objectIndices.Length,
            0,
            0,
            0,
            Array.AsReadOnly(objectIndices));

    private static BeatInsight.Models.HitObject TestHitObject(int time) =>
        new() { Time = time, Type = 1 };

    private static IReadOnlyList<SectionPatternMetric> Metrics(SectionJumpPatternProfile profile) =>
    [
        profile.Linear, profile.Edge, profile.Sharp, profile.Triangle,
        profile.OneTwo, profile.Spaced, profile.WideAngle, profile.Reversal,
        profile.Arc, profile.SpacingTrend, profile.AlternatingSpacing
    ];

    private static void AssertMetric(
        SectionPatternMetric metric,
        int sequenceCount,
        int[] objectIndices,
        double coverage)
    {
        Assert.Equal(sequenceCount, metric.SequenceCount);
        Assert.Equal(objectIndices, metric.ObjectIndices);
        Assert.Equal(objectIndices.Length, metric.ObjectCount);
        Assert.Equal(coverage, metric.Coverage, 12);
    }
}
