using System.Text;
using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Analysis.Sections;
using BeatInsight.Models;
using BeatInsight.Models.JumpPatterns;
using BeatInsight.Models.Sections;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

public sealed class SectionJumpPatternForensicTests(ITestOutputHelper output)
{
    private static readonly string[] Fixtures =
    [
        "Vickeblanka - Black Rover (TV Size) (Sotarks) [Extreme].osu",
        "(can you) understand me [uhh].osu",
        "FREEDOM DiVE [Arles].osu",
        "Kira Killer [Mocaotic's Insane].osu",
        "Tower Of Heaven [Extra].osu"
    ];

    [Fact]
    public void ReferenceJumpMaps_DistributeFrozenResultsWithoutChangingGlobalTotals()
    {
        var report = new StringBuilder();
        report.AppendLine("===== SECTION JUMP PATTERNS =====");

        foreach (string fixture in Fixtures)
        {
            Beatmap map = FixtureLoader.Load(fixture);
            GameplayProfile gameplay = GameplayAnalyzer.Analyze(map);
            IReadOnlyList<JumpPatternMatch> global =
                JumpPatternAnalyzer.Analyze(map.HitObjects, gameplay);
            IReadOnlyList<GameplaySectionAnalysis> sections =
                SectionJumpPatternAggregator.Aggregate(
                    GameplaySectionAnalyzer.Analyze(map),
                    global);

            AssertGlobalConsistency(global, sections);
            AppendMap(report, map, sections);

            if (fixture.Contains("Black Rover", StringComparison.Ordinal))
            {
                Assert.Equal(4, sections.Sum(section => section.JumpPatterns.OneTwo.SequenceCount));
                Assert.True(sections.Sum(section => section.JumpPatterns.Spaced.SequenceCount) > 0);
                Assert.True(sections.Sum(section => section.JumpPatterns.Sharp.SequenceCount) > 0);
                Assert.True(sections.Sum(section => section.JumpPatterns.Triangle.SequenceCount) > 0);
                Assert.True(sections.Sum(section => section.JumpPatterns.Edge.SequenceCount) > 0);
            }

            if (fixture.StartsWith("(can you)", StringComparison.Ordinal))
            {
                int oneTwo = sections.Sum(section => section.JumpPatterns.OneTwo.ObjectCount);
                int sharp = sections.SelectMany(section => section.JumpPatterns.Sharp.ObjectIndices).Distinct().Count();
                int spaced = sections.SelectMany(section => section.JumpPatterns.Spaced.ObjectIndices).Distinct().Count();
                Assert.True(sharp > oneTwo);
                Assert.True(spaced > oneTwo);
            }
        }

        output.WriteLine(report.ToString());
    }

    private static void AssertGlobalConsistency(
        IReadOnlyList<JumpPatternMatch> global,
        IReadOnlyList<GameplaySectionAnalysis> sections)
    {
        foreach ((JumpPatternType type, Func<SectionJumpPatternProfile, SectionPatternMetric> select) in Patterns())
        {
            JumpPatternMatch[] occurrences = type == JumpPatternType.Accelerating
                ? global.Where(match => match.Type is JumpPatternType.Accelerating or JumpPatternType.Decelerating).ToArray()
                : global.Where(match => match.Type == type).ToArray();
            int[] expectedObjects = occurrences
                .SelectMany(match => Enumerable.Range(match.StartObjectIndex, match.ObjectCount))
                .Distinct().Order().ToArray();
            int[] actualObjects = sections
                .SelectMany(section => select(section.JumpPatterns).ObjectIndices)
                .Distinct().Order().ToArray();

            Assert.Equal(expectedObjects, actualObjects);
            Assert.Equal(
                occurrences.Length,
                sections.Sum(section => select(section.JumpPatterns).SequenceCount));
        }
    }

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
            foreach ((string name, SectionPatternMetric metric) in NamedMetrics(section.JumpPatterns)
                .Where(item => item.metric.Coverage > 0))
            {
                report.AppendLine($"  {name}: Coverage={metric.Coverage:P2}; Sequences={metric.SequenceCount}");
            }
        }
    }

    private static IEnumerable<(JumpPatternType Type, Func<SectionJumpPatternProfile, SectionPatternMetric> Select)> Patterns()
    {
        yield return (JumpPatternType.Linear, profile => profile.Linear);
        yield return (JumpPatternType.Edge, profile => profile.Edge);
        yield return (JumpPatternType.Sharp, profile => profile.Sharp);
        yield return (JumpPatternType.Triangle, profile => profile.Triangle);
        yield return (JumpPatternType.OneTwo, profile => profile.OneTwo);
        yield return (JumpPatternType.Spaced, profile => profile.Spaced);
        yield return (JumpPatternType.WideAngle, profile => profile.WideAngle);
        yield return (JumpPatternType.Reversal, profile => profile.Reversal);
        yield return (JumpPatternType.Arc, profile => profile.Arc);
        yield return (JumpPatternType.Accelerating, profile => profile.SpacingTrend);
        yield return (JumpPatternType.AlternatingSpacing, profile => profile.AlternatingSpacing);
    }

    private static IEnumerable<(string name, SectionPatternMetric metric)> NamedMetrics(
        SectionJumpPatternProfile profile)
    {
        yield return ("Linear", profile.Linear);
        yield return ("Edge", profile.Edge);
        yield return ("Sharp", profile.Sharp);
        yield return ("Triangle", profile.Triangle);
        yield return ("OneTwo", profile.OneTwo);
        yield return ("Spaced", profile.Spaced);
        yield return ("WideAngle", profile.WideAngle);
        yield return ("Reversal", profile.Reversal);
        yield return ("Arc", profile.Arc);
        yield return ("SpacingTrend", profile.SpacingTrend);
        yield return ("AlternatingSpacing", profile.AlternatingSpacing);
    }
}
