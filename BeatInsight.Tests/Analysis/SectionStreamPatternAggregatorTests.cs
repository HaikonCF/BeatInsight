using BeatInsight.Analysis.Sections;
using BeatInsight.Models;
using BeatInsight.Models.Sections;
using BeatInsight.Models.StreamPatterns;

namespace BeatInsight.Tests.Analysis;

public sealed class SectionStreamPatternAggregatorTests
{
    [Fact]
    public void Aggregate_BurstInsideOneSectionContributesOnlyThere()
    {
        var sections = new[] { Section(0, 0, 1), Section(1, 2, 3, 4, 5) };

        IReadOnlyList<GameplaySectionAnalysis> result = Aggregate(
            sections, Occurrence(SectionStreamPatternType.Burst, 2, 2, 3, 4));

        AssertMetric(result[1].StreamPatterns.Burst, 1, [2, 3, 4], .75);
        AssertMetric(result[0].StreamPatterns.Burst, 0, [], 0);
    }

    [Fact]
    public void Aggregate_LongAcrossTwoSectionsSplitsObjectsAndCountsStartOnce()
    {
        var sections = new[] { Section(0, 0, 1, 2, 3, 4, 5, 6, 7), Section(1, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17) };

        IReadOnlyList<GameplaySectionAnalysis> result = Aggregate(
            sections, Occurrence(SectionStreamPatternType.Long, 0, Enumerable.Range(0, 18).ToArray()));

        AssertMetric(result[0].StreamPatterns.Long, 1, Enumerable.Range(0, 8).ToArray(), 1);
        AssertMetric(result[1].StreamPatterns.Long, 0, Enumerable.Range(8, 10).ToArray(), 1);
    }

    [Fact]
    public void Aggregate_DeathstreamAcrossThreeSectionsHasOneSequence()
    {
        var sections = new[] { Section(0, 0, 1), Section(1, 2, 3), Section(2, 4, 5) };

        IReadOnlyList<GameplaySectionAnalysis> result = Aggregate(
            sections, Occurrence(SectionStreamPatternType.Deathstream, 0, 0, 1, 2, 3, 4, 5));

        Assert.All(result, section => Assert.Equal(1, section.StreamPatterns.Deathstream.Coverage));
        Assert.Equal(1, result.Sum(section => section.StreamPatterns.Deathstream.SequenceCount));
        Assert.Equal(1, result[0].StreamPatterns.Deathstream.SequenceCount);
    }

    [Fact]
    public void Aggregate_StaminaRecoveryOnlySectionReceivesNoObjects()
    {
        var sections = new[] { Section(0, 0, 1), Section(1, 2, 3, 4, 5), Section(2, 6, 7) };

        IReadOnlyList<GameplaySectionAnalysis> result = Aggregate(
            sections, Occurrence(SectionStreamPatternType.Stamina, 0, 0, 1, 6, 7));

        AssertMetric(result[0].StreamPatterns.Stamina, 1, [0, 1], 1);
        AssertMetric(result[1].StreamPatterns.Stamina, 0, [], 0);
        AssertMetric(result[2].StreamPatterns.Stamina, 0, [6, 7], 1);
    }

    [Fact]
    public void Aggregate_StaminaThreeRunsRemainOneSequenceWithExactRapidUnion()
    {
        var sections = new[] { Section(0, 0, 1, 2), Section(1, 3, 4, 5), Section(2, 6, 7, 8) };
        int[] rapidObjects = [0, 1, 3, 4, 7, 8];

        IReadOnlyList<GameplaySectionAnalysis> result = Aggregate(
            sections, Occurrence(SectionStreamPatternType.Stamina, 0, rapidObjects));

        Assert.Equal(1, result.Sum(section => section.StreamPatterns.Stamina.SequenceCount));
        Assert.Equal(rapidObjects, result.SelectMany(section => section.StreamPatterns.Stamina.ObjectIndices).Order());
        Assert.DoesNotContain(2, result.SelectMany(section => section.StreamPatterns.Stamina.ObjectIndices));
        Assert.DoesNotContain(5, result.SelectMany(section => section.StreamPatterns.Stamina.ObjectIndices));
        Assert.DoesNotContain(6, result.SelectMany(section => section.StreamPatterns.Stamina.ObjectIndices));
    }

    [Fact]
    public void Aggregate_ShortSpeedAndFlowCoexistWithoutCrossNormalization()
    {
        var sections = new[] { Section(0, 0, 1, 2, 3) };
        int[] objects = [0, 1, 2];

        GameplaySectionAnalysis result = Aggregate(
            sections,
            Occurrence(SectionStreamPatternType.Short, 0, objects),
            Occurrence(SectionStreamPatternType.Speed, 0, objects),
            Occurrence(SectionStreamPatternType.Flow, 0, objects))[0];

        AssertMetric(result.StreamPatterns.Short, 1, objects, .75);
        AssertMetric(result.StreamPatterns.Speed, 1, objects, .75);
        AssertMetric(result.StreamPatterns.Flow, 1, objects, .75);
    }

    [Fact]
    public void Aggregate_ValidLengthFamiliesRemainDisjoint()
    {
        var sections = new[] { Section(0, Enumerable.Range(0, 16).ToArray()) };
        SectionStreamPatternOccurrence[] input =
        [
            Occurrence(SectionStreamPatternType.Burst, 0, 0, 1, 2),
            Occurrence(SectionStreamPatternType.Short, 3, 3, 4, 5, 6),
            Occurrence(SectionStreamPatternType.Long, 7, 7, 8, 9, 10),
            Occurrence(SectionStreamPatternType.Deathstream, 11, 11, 12, 13, 14, 15)
        ];

        Assert.All(input
            .SelectMany(occurrence => occurrence.ObjectIndices.Select(index => (occurrence.Type, index)))
            .GroupBy(item => item.index),
            group => Assert.Single(group.Select(item => item.Type).Distinct()));

        SectionStreamPatternProfile profile = Aggregate(sections, input)[0].StreamPatterns;
        Assert.All(new[] { profile.Burst, profile.Short, profile.Long, profile.Deathstream }
            .SelectMany(metric => metric.ObjectIndices)
            .GroupBy(index => index),
            group => Assert.Single(group));
    }

    [Fact]
    public void Aggregate_RejectsCrossFamilyLengthOverlap()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => Aggregate(
            [Section(0, 0, 1, 2, 3)],
            Occurrence(SectionStreamPatternType.Burst, 0, 0, 1, 2),
            Occurrence(SectionStreamPatternType.Short, 2, 2, 3)));

        Assert.Equal("occurrences", exception.ParamName);
    }

    [Fact]
    public void Aggregate_OverlappingFlowOccurrencesDeduplicateObjects()
    {
        var sections = new[] { Section(0, Enumerable.Range(1, 10).ToArray()) };

        GameplaySectionAnalysis result = Aggregate(
            sections,
            Occurrence(SectionStreamPatternType.Flow, 1, 1, 2, 3, 4, 5, 6),
            Occurrence(SectionStreamPatternType.Flow, 5, 5, 6, 7, 8, 9, 10))[0];

        AssertMetric(result.StreamPatterns.Flow, 2, Enumerable.Range(1, 10).ToArray(), 1);
    }

    [Fact]
    public void Aggregate_EmptySectionHasZeroMetrics()
    {
        GameplaySectionAnalysis empty = Aggregate(
            [Section(0), Section(1, 0, 1)],
            Occurrence(SectionStreamPatternType.Burst, 0, 0, 1))[0];

        Assert.All(Metrics(empty.StreamPatterns), metric =>
        {
            Assert.Equal(0, metric.SequenceCount);
            Assert.Equal(0, metric.ObjectCount);
            Assert.Equal(0, metric.Coverage);
            Assert.Equal(0, metric.AverageConfidence);
        });
    }

    [Fact]
    public void Aggregate_StartOnTemporalBoundaryUsesFollowingSection()
    {
        HitObject[] objects = [HitObject(0), HitObject(7_500), HitObject(90_000)];
        IReadOnlyList<GameplaySectionAnalysis> sections = GameplaySectionAnalyzer.Analyze(objects);

        IReadOnlyList<GameplaySectionAnalysis> result = Aggregate(
            sections, Occurrence(SectionStreamPatternType.Burst, 1, 1));

        Assert.Equal(0, result[0].StreamPatterns.Burst.SequenceCount);
        Assert.Equal(1, result[1].StreamPatterns.Burst.SequenceCount);
    }

    [Fact]
    public void Aggregate_IrregularConsumesPrequalifiedOccurrenceWithoutQualification()
    {
        GameplaySectionAnalysis result = Aggregate(
            [Section(0, 0, 1, 2, 3, 4, 5)],
            Occurrence(SectionStreamPatternType.Irregular, 0, 0, 1, 2, 3, 4, 5))[0];

        AssertMetric(result.StreamPatterns.Irregular, 1, [0, 1, 2, 3, 4, 5], 1);
    }

    [Fact]
    public void Aggregate_FlowAndAlternatingRemainIndependent()
    {
        GameplaySectionAnalysis result = Aggregate(
            [Section(0, 0, 1, 2, 3)],
            Occurrence(SectionStreamPatternType.Flow, 0, 0, 1, 2),
            Occurrence(SectionStreamPatternType.Alternating, 1, 1, 2, 3))[0];

        AssertMetric(result.StreamPatterns.Flow, 1, [0, 1, 2], .75);
        AssertMetric(result.StreamPatterns.Alternating, 1, [1, 2, 3], .75);
    }

    [Fact]
    public void Aggregate_GlobalUnionsAndSequenceTotalsEqualEveryInputPattern()
    {
        var sections = new[]
        {
            Section(0, Enumerable.Range(0, 14).ToArray()),
            Section(1, Enumerable.Range(14, 13).ToArray())
        };
        SectionStreamPatternOccurrence[] input = Enum.GetValues<SectionStreamPatternType>()
            .Select((type, index) => Occurrence(
                type,
                index * 3,
                index * 3,
                index * 3 + 1,
                index * 3 + 2))
            .ToArray();
        IReadOnlyList<GameplaySectionAnalysis> result = Aggregate(sections, input);

        foreach (SectionStreamPatternType type in Enum.GetValues<SectionStreamPatternType>())
        {
            int[] expected = input.Where(occurrence => occurrence.Type == type)
                .SelectMany(occurrence => occurrence.ObjectIndices).Distinct().Order().ToArray();
            int[] actual = result.SelectMany(section => Select(section.StreamPatterns, type).ObjectIndices)
                .Distinct().Order().ToArray();
            Assert.Equal(expected, actual);
            Assert.Equal(
                input.Count(occurrence => occurrence.Type == type),
                result.Sum(section => Select(section.StreamPatterns, type).SequenceCount));
        }
    }

    [Fact]
    public void Aggregate_AverageConfidenceUsesOnlySequencesStartingInSection()
    {
        var sections = new[] { Section(0, 0, 1, 2), Section(1, 3, 4, 5) };

        IReadOnlyList<GameplaySectionAnalysis> result = Aggregate(
            sections,
            Occurrence(SectionStreamPatternType.Flow, 0, .25, 0, 1, 2, 3),
            Occurrence(SectionStreamPatternType.Flow, 1, .75, 1, 2, 3, 4));

        Assert.Equal(.5, result[0].StreamPatterns.Flow.AverageConfidence, 12);
        Assert.Equal(0, result[1].StreamPatterns.Flow.AverageConfidence);
    }

    [Fact]
    public void ProductionAdapter_StaminaIncludesRapidRunsButNotRecoveryObjects()
    {
        var sequence = new StaminaStreamPatternSequence(
            0, 7, 0, 700,
            [
                new StaminaRapidRun(0, 1, 0, 100, [100]),
                new StaminaRapidRun(6, 7, 600, 700, [100])
            ],
            [500],
            true,
            "Qualified");
        SectionStreamPatternAnalysisResults results = EmptyResults(
            new StaminaStreamPatternAnalysis(
                8, 2, 4, 4, 700, 2, 4, 700, 500, 2d / 7, 100, 1, .8, [sequence]));

        SectionStreamPatternOccurrence stamina = Assert.Single(
            SectionStreamPatternOccurrenceAdapter.Adapt(results),
            occurrence => occurrence.Type == SectionStreamPatternType.Stamina);

        Assert.Equal([0, 1, 6, 7], stamina.ObjectIndices);
        Assert.Equal(0, stamina.StartObjectIndex);
    }

    private static IReadOnlyList<GameplaySectionAnalysis> Aggregate(
        IReadOnlyList<GameplaySectionAnalysis> sections,
        params SectionStreamPatternOccurrence[] occurrences) =>
        SectionStreamPatternAggregator.Aggregate(sections, occurrences);

    private static SectionStreamPatternOccurrence Occurrence(
        SectionStreamPatternType type,
        int start,
        params int[] indices) => Occurrence(type, start, .8, indices);

    private static SectionStreamPatternOccurrence Occurrence(
        SectionStreamPatternType type,
        int start,
        double confidence,
        params int[] indices) => new(type, start, Array.AsReadOnly(indices), confidence);

    private static GameplaySectionAnalysis Section(int index, params int[] indices) =>
        new(index, index * 1_000, (index + 1) * 1_000, indices.Length, 0, 0, 0, Array.AsReadOnly(indices));

    private static HitObject HitObject(int time) => new() { Time = time, Type = 1 };

    private static IReadOnlyList<SectionPatternMetric> Metrics(SectionStreamPatternProfile profile) =>
    [
        profile.Burst, profile.Short, profile.Long, profile.Deathstream,
        profile.Stamina, profile.Irregular, profile.Speed, profile.Flow, profile.Alternating
    ];

    private static SectionPatternMetric Select(SectionStreamPatternProfile profile, SectionStreamPatternType type) =>
        type switch
        {
            SectionStreamPatternType.Burst => profile.Burst,
            SectionStreamPatternType.Short => profile.Short,
            SectionStreamPatternType.Long => profile.Long,
            SectionStreamPatternType.Deathstream => profile.Deathstream,
            SectionStreamPatternType.Stamina => profile.Stamina,
            SectionStreamPatternType.Irregular => profile.Irregular,
            SectionStreamPatternType.Speed => profile.Speed,
            SectionStreamPatternType.Flow => profile.Flow,
            SectionStreamPatternType.Alternating => profile.Alternating,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

    private static void AssertMetric(SectionPatternMetric metric, int sequences, int[] indices, double coverage)
    {
        Assert.Equal(sequences, metric.SequenceCount);
        Assert.Equal(indices, metric.ObjectIndices);
        Assert.Equal(indices.Length, metric.ObjectCount);
        Assert.Equal(coverage, metric.Coverage, 12);
    }

    private static SectionStreamPatternAnalysisResults EmptyResults(StaminaStreamPatternAnalysis stamina) => new(
        new BurstPatternAnalysis(0, 0, 0, 0, 0, 0, 0, 0, []),
        new ShortStreamPatternAnalysis(0, 0, 0, 0, 0, 0, 0, 0, []),
        new LongStreamPatternAnalysis(0, 0, 0, 0, 0, 0, 0, 0, []),
        new DeathstreamPatternAnalysis(0, 0, 0, 0, 0, 0, 0, 0, []),
        stamina,
        new IrregularStreamPatternAnalysis(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, []),
        new SpeedStreamPatternAnalysis(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, []),
        new FlowStreamPatternAnalysis(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, []),
        new AlternatingStreamPatternAnalysis(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, []));
}
