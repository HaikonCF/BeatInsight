using System.IO;
using System.Text;
using BeatInsight.Analysis.Sections;
using BeatInsight.Analysis.StreamPatterns;
using BeatInsight.Models;
using BeatInsight.Models.Sections;
using BeatInsight.Models.StreamPatterns;
using BeatInsight.Parser;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class SectionStreamPatternForensicTests(ITestOutputHelper output)
{
    private static readonly string[] Titles =
    [
        "Road of Resistance (GoldenWolf Metal Mix)",
        "Extreme Fantasy",
        "d.m.c (Band Ver.)",
        "End Time",
        "Reign of Fear"
    ];

    [Fact]
    public void ReferenceStreamMaps_DistributeFrozenResultsWithoutChangingGlobalTotals()
    {
        var report = new StringBuilder("===== SECTION STREAM PATTERNS =====\n");

        foreach (StreamPatternGlobalForensicTests.MapSpec spec in
            StreamPatternGlobalForensicTests.Corpus.Where(spec => Titles.Contains(spec.Title)))
        {
            Assert.True(File.Exists(spec.Path), $"Missing corpus map: {spec.Path}");
            Beatmap map = BeatmapParser.Load(spec.Path);
            SectionStreamPatternAnalysisResults global = Analyze(map);
            IReadOnlyList<SectionStreamPatternOccurrence> occurrences =
                SectionStreamPatternOccurrenceAdapter.Adapt(global);
            IReadOnlyList<GameplaySectionAnalysis> sections =
                SectionStreamPatternAggregator.Aggregate(
                    GameplaySectionAnalyzer.Analyze(map),
                    occurrences);

            AssertGlobalConsistency(occurrences, sections);
            AssertControls(spec.Title, global, occurrences, sections);
            AppendMap(report, map, sections);
        }

        Assert.Equal(Titles.Length, report.ToString().Split("\nMAP ").Length - 1);
        output.WriteLine(report.ToString());
    }

    private static SectionStreamPatternAnalysisResults Analyze(Beatmap map) => new(
        StreamPatternAnalyzer.AnalyzeBurst(map.HitObjects),
        StreamPatternAnalyzer.AnalyzeShortStream(map.HitObjects),
        StreamPatternAnalyzer.AnalyzeLongStream(map.HitObjects),
        StreamPatternAnalyzer.AnalyzeDeathstream(map.HitObjects),
        StreamPatternAnalyzer.AnalyzeStaminaStream(map.HitObjects),
        StreamPatternAnalyzer.AnalyzeIrregularStream(map.HitObjects),
        StreamPatternAnalyzer.AnalyzeSpeedStream(map.HitObjects),
        StreamPatternAnalyzer.AnalyzeFlowStream(map.HitObjects),
        StreamPatternAnalyzer.AnalyzeAlternatingStream(map.HitObjects));

    private static void AssertControls(
        string title,
        SectionStreamPatternAnalysisResults global,
        IReadOnlyList<SectionStreamPatternOccurrence> occurrences,
        IReadOnlyList<GameplaySectionAnalysis> sections)
    {
        IReadOnlyDictionary<SectionStreamPatternType, int> expected = title switch
        {
            "Road of Resistance (GoldenWolf Metal Mix)" => Counts(19, 8, 0, 3, 1, 1, 3, 2, 0),
            "Extreme Fantasy" => Counts(59, 7, 3, 2, 2, 0, 0, 8, 1),
            "d.m.c (Band Ver.)" => Counts(33, 4, 4, 0, 3, 0, 8, 8, 0),
            "End Time" => Counts(16, 7, 2, 2, 2, 0, 17, 8, 0),
            "Reign of Fear" => Counts(
                global.Burst.BurstSequenceCount,
                global.Short.ShortStreamSequenceCount,
                global.Long.LongStreamSequenceCount,
                global.Deathstream.DeathstreamSequenceCount,
                global.Stamina.StaminaSequenceCount,
                global.Irregular.IrregularStreamSequenceCount,
                global.Speed.SpeedStreamSequenceCount,
                6,
                global.Alternating.AlternatingStreamSequenceCount),
            _ => throw new ArgumentOutOfRangeException(nameof(title))
        };

        foreach ((SectionStreamPatternType type, int count) in expected)
            Assert.Equal(count, sections.Sum(section => Select(section.StreamPatterns, type).SequenceCount));

        if (title.StartsWith("Road of Resistance", StringComparison.Ordinal))
        {
            SectionStreamPatternOccurrence stamina = Assert.Single(
                occurrences, occurrence => occurrence.Type == SectionStreamPatternType.Stamina);
            Assert.Equal(195, stamina.ObjectIndices.Distinct().Count());
            Assert.Equal(.4875, global.Stamina.Coverage, 4);
            var irregular = Assert.Single(global.Irregular.Sequences);
            Assert.Equal(25_546, irregular.StartTime);
            Assert.Equal(26_492, irregular.EndTime);
        }
        else if (title == "Extreme Fantasy")
        {
            Assert.Single(occurrences, occurrence => occurrence.Type == SectionStreamPatternType.Alternating);
        }
        else if (title == "Reign of Fear")
        {
            Assert.Contains(global.Flow.Sequences,
                sequence => sequence.StartTime == 256_486 && sequence.EndTime == 257_062);
        }
    }

    private static void AssertGlobalConsistency(
        IReadOnlyList<SectionStreamPatternOccurrence> occurrences,
        IReadOnlyList<GameplaySectionAnalysis> sections)
    {
        foreach (SectionStreamPatternType type in Enum.GetValues<SectionStreamPatternType>())
        {
            int[] expected = occurrences.Where(occurrence => occurrence.Type == type)
                .SelectMany(occurrence => occurrence.ObjectIndices)
                .Distinct().Order().ToArray();
            int[] actual = sections
                .SelectMany(section => Select(section.StreamPatterns, type).ObjectIndices)
                .Distinct().Order().ToArray();
            Assert.Equal(expected, actual);
            Assert.Equal(
                occurrences.Count(occurrence => occurrence.Type == type),
                sections.Sum(section => Select(section.StreamPatterns, type).SequenceCount));
        }
    }

    private static IReadOnlyDictionary<SectionStreamPatternType, int> Counts(
        int burst, int shortCount, int longCount, int deathstream, int stamina,
        int irregular, int speed, int flow, int alternating) =>
        new Dictionary<SectionStreamPatternType, int>
        {
            [SectionStreamPatternType.Burst] = burst,
            [SectionStreamPatternType.Short] = shortCount,
            [SectionStreamPatternType.Long] = longCount,
            [SectionStreamPatternType.Deathstream] = deathstream,
            [SectionStreamPatternType.Stamina] = stamina,
            [SectionStreamPatternType.Irregular] = irregular,
            [SectionStreamPatternType.Speed] = speed,
            [SectionStreamPatternType.Flow] = flow,
            [SectionStreamPatternType.Alternating] = alternating
        };

    private static SectionPatternMetric Select(
        SectionStreamPatternProfile profile,
        SectionStreamPatternType type) => type switch
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

    private static void AppendMap(
        StringBuilder report,
        Beatmap map,
        IReadOnlyList<GameplaySectionAnalysis> sections)
    {
        report.AppendLine();
        report.AppendLine($"MAP {map.Title} [{map.Version}]");
        foreach (GameplaySectionAnalysis section in sections.Where(section => section.ObjectCount > 0))
        {
            report.AppendLine($"SECTION {section.Index:D2} | {section.StartTime:F0}-{section.EndTime:F0} ms | Objects={section.ObjectCount}");
            foreach ((string name, SectionPatternMetric metric) in Metrics(section.StreamPatterns)
                .Where(item => item.metric.Coverage > 0))
                report.AppendLine($"  {name}: Coverage={metric.Coverage:P2}; Sequences={metric.SequenceCount}");
        }
    }

    private static IEnumerable<(string name, SectionPatternMetric metric)> Metrics(
        SectionStreamPatternProfile profile)
    {
        yield return ("Burst", profile.Burst);
        yield return ("Short", profile.Short);
        yield return ("Long", profile.Long);
        yield return ("Deathstream", profile.Deathstream);
        yield return ("Stamina", profile.Stamina);
        yield return ("Irregular", profile.Irregular);
        yield return ("Speed", profile.Speed);
        yield return ("Flow", profile.Flow);
        yield return ("Alternating", profile.Alternating);
    }
}
