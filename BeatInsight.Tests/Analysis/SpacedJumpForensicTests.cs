using System.Globalization;
using System.IO;
using System.Text;
using BeatInsight.Analysis;
using BeatInsight.Analysis.JumpPatterns;
using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.JumpPatterns;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

/// <summary>Fixture-only forensic report. It invokes the current Spaced detector without changing policy.</summary>
public sealed class SpacedJumpForensicTests(ITestOutputHelper output)
{
    [Fact]
    public void AvailableFixtures_ReportOnlyValidatedSpacedSequences()
    {
        bool debug = DebugLogger.DebugMode;
        bool enabled = GameplayDebug.SpacedJumpEnabled;
        bool detailed = GameplayDebug.SpacedJumpDetailedEnabled;
        var report = new StringBuilder("# Spaced Jump fixture forensic\n\n");
        report.AppendLine("Indices are zero-based and inclusive. Coverage denominator includes all hitobjects. " +
            "Distance consistency is min/max over all retained links for the map.");
        report.AppendLine();
        report.AppendLine("|Map|Objects|Spaced transitions|Spaced objects|Sequences|Coverage %|Longest|Avg distance|Min distance|Max distance|Consistency|Confidence|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        var details = new StringBuilder();

        try
        {
            string fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps");
            string[] files = Directory.GetFiles(fixtureDirectory, "*.osu")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            Assert.NotEmpty(files);

            foreach (string path in files)
            {
                Beatmap map = FixtureLoader.Load(Path.GetFileName(path));
                GameplayProfile profile = map.GameplayProfile;
                DebugLogger.DebugMode = true;
                GameplayDebug.SpacedJumpEnabled = true;
                GameplayDebug.SpacedJumpDetailedEnabled = true;
                SpacedJumpAnalysis result = JumpPatternAnalyzer.AnalyzeSpaced(map.HitObjects, profile);
                DebugLogger.DebugMode = false;
                GameplayDebug.SpacedJumpEnabled = false;
                GameplayDebug.SpacedJumpDetailedEnabled = false;

                IReadOnlyList<JumpPatternMatch> allMatches = JumpPatternAnalyzer.Analyze(map.HitObjects, profile);
                HashSet<int> streamIndices = profile.StreamSequences
                    .SelectMany(sequence => Enumerable.Range(sequence.StartObjectIndex, sequence.ObjectCount))
                    .ToHashSet();
                HashSet<int> spacedIndices = result.Matches
                    .SelectMany(match => Enumerable.Range(match.StartObjectIndex, match.ObjectCount))
                    .ToHashSet();

                Assert.Equal(spacedIndices.Count, result.SpacedObjectCount);
                Assert.Equal(spacedIndices.Count / (double)map.HitObjects.Count, result.Coverage, 12);
                Assert.Empty(spacedIndices.Intersect(streamIndices));
                Assert.Equal(result.Matches.Sum(match => match.ObjectCount - 1), result.SpacedTransitionCount);
                Assert.Equal(result.Transitions.Count, result.Transitions.Select(transition => transition.StartObjectIndex).Distinct().Count());
                Assert.All(result.Transitions, transition =>
                {
                    Assert.True(transition.Distance >= 150);
                    Assert.True((map.HitObjects[transition.StartObjectIndex].Type & 1) != 0);
                    Assert.True((map.HitObjects[transition.EndObjectIndex].Type & 1) != 0);
                    Assert.True(GameplayAnalyzer.IsJumpLink(map.HitObjects[transition.StartObjectIndex],
                        map.HitObjects[transition.EndObjectIndex], out double distance));
                    Assert.Equal(distance, transition.Distance, 8);
                });
                Assert.InRange(result.Confidence, 0, 1);

                report.AppendLine(FormattableString.Invariant($"|{Path.GetFileNameWithoutExtension(path)}|{map.HitObjects.Count}|{result.SpacedTransitionCount}|{result.SpacedObjectCount}|{result.SequenceCount}|{result.Coverage * 100:F3}|{result.LongestSequence}|{result.AverageDistance:F3}|{result.MinimumDistance:F3}|{result.MaximumDistance:F3}|{result.DistanceConsistency:F4}|{result.Confidence:F4}|"));
                AppendDetails(details, map, result, allMatches);
            }
        }
        finally
        {
            DebugLogger.DebugMode = debug;
            GameplayDebug.SpacedJumpEnabled = enabled;
            GameplayDebug.SpacedJumpDetailedEnabled = detailed;
        }

        report.Append(details);
        string reportPath = Path.Combine(AppContext.BaseDirectory, "spaced-jump-forensic.md");
        File.WriteAllText(reportPath, report.ToString());
        output.WriteLine(report.ToString());
        output.WriteLine($"Report: {reportPath}");
    }

    [Theory]
    [InlineData(220, 500, false)] // .44
    [InlineData(225, 500, true)]  // .45
    [InlineData(230, 500, true)]  // .46
    public void DistanceConsistencyBoundary_IsInclusiveAt045(int minimumDistance, int maximumDistance,
        bool expected)
    {
        var map = Map((0, 0), (minimumDistance, 0),
            (minimumDistance + maximumDistance, 0), (minimumDistance + 2 * maximumDistance, 0));
        SpacedJumpAnalysis result = Analyze(map);

        Assert.Equal(expected, result.Matches.Count != 0);
        if (expected)
            Assert.InRange(result.DistanceConsistency, .45, 1);
    }

    [Theory]
    [InlineData(149, false)]
    [InlineData(150, true)]
    [InlineData(151, true)]
    public void DistanceBoundary_IsInclusiveAt150(int distance, bool expected)
    {
        SpacedJumpAnalysis result = Analyze(Map((0, 0), (distance, 0),
            (distance * 2, 0), (distance * 3, 0)));

        Assert.Equal(expected, result.Matches.Count != 0);
    }

    [Fact]
    public void ShortMovesBreakSequenceAndNeverBridgeIt()
    {
        SpacedJumpAnalysis result = Analyze(Map(
            (0, 0), (150, 0), (300, 0), (450, 0),
            (500, 0), (550, 0), (700, 0), (850, 0), (1000, 0)));

        Assert.Equal(2, result.Matches.Count);
        Assert.Equal((0, 3), (result.Matches[0].StartObjectIndex, result.Matches[0].EndObjectIndex));
        Assert.Equal((5, 8), (result.Matches[1].StartObjectIndex, result.Matches[1].EndObjectIndex));
        Assert.DoesNotContain(result.Transitions, transition => transition.StartObjectIndex is 3 or 4);
    }

    [Fact]
    public void EightObjectHighSpacingSequence_SaturatesConfidence()
    {
        var points = Enumerable.Range(0, 8).Select(index => (index * 300, 0)).ToArray();
        SpacedJumpAnalysis result = Analyze(Map(points));

        Assert.Single(result.Matches);
        Assert.Equal(8, result.LongestSequence);
        Assert.Equal(1, result.Confidence, 12);
    }

    private static void AppendDetails(StringBuilder details, Beatmap map, SpacedJumpAnalysis result,
        IReadOnlyList<JumpPatternMatch> allMatches)
    {
        details.AppendLine($"\n## {map.Title} [{map.Version}]\n");
        if (result.Matches.Count == 0)
        {
            details.AppendLine("No validated Spaced sequence.");
            return;
        }

        foreach (JumpPatternMatch match in result.Matches)
        {
            SpacedJumpTransition[] transitions = result.Transitions
                .Where(transition => transition.StartObjectIndex >= match.StartObjectIndex
                    && transition.EndObjectIndex <= match.EndObjectIndex)
                .ToArray();
            double minimum = transitions.Min(transition => transition.Distance);
            double maximum = transitions.Max(transition => transition.Distance);
            double average = transitions.Average(transition => transition.Distance);
            double consistency = minimum / maximum;
            string coexistence = Coexistence(match, allMatches);
            string distances = string.Join(", ", transitions.Select(transition =>
                transition.Distance.ToString("F3", CultureInfo.InvariantCulture)));
            details.AppendLine(FormattableString.Invariant(
                $"Sequence [{match.StartObjectIndex}..{match.EndObjectIndex}] {Time(match.StartTime)} -> {Time(match.EndTime)}; objects={match.ObjectCount}; links={transitions.Length}; distances=[{distances}] px; min={minimum:F3}px; max={maximum:F3}px; avg={average:F3}px; consistency={consistency:F4}; confidence={match.Confidence:F4}; coexistence={coexistence}"));
            foreach (SpacedJumpTransition transition in transitions)
            {
                details.AppendLine(FormattableString.Invariant(
                    $"- {transition.StartObjectIndex}->{transition.EndObjectIndex} | {Time(transition.StartTime)}->{Time(transition.EndTime)} | ({transition.FromX},{transition.FromY})->({transition.ToX},{transition.ToY}) | {transition.Distance:F3}px | SequenceId={transition.SequenceId}"));
            }
        }
    }

    private static string Coexistence(JumpPatternMatch spaced, IReadOnlyList<JumpPatternMatch> matches)
    {
        JumpPatternType[] otherTypes = [JumpPatternType.OneTwo, JumpPatternType.Sharp, JumpPatternType.Edge,
            JumpPatternType.Triangle, JumpPatternType.Linear];
        IEnumerable<string> overlaps = matches.Where(match => otherTypes.Contains(match.Type))
            .Where(match => Math.Min(spaced.EndObjectIndex, match.EndObjectIndex)
                >= Math.Max(spaced.StartObjectIndex, match.StartObjectIndex))
            .Select(match => match.Type.ToString());
        return string.Join(" + ", overlaps.DefaultIfEmpty("None"));
    }

    private static SpacedJumpAnalysis Analyze(Beatmap map) =>
        JumpPatternAnalyzer.AnalyzeSpaced(map.HitObjects, GameplayAnalyzer.Analyze(map));

    private static Beatmap Map(params (int X, int Y)[] points) => new()
    {
        CS = 4,
        SliderMultiplier = 1.4,
        TimingPoints = [new() { Time = 0, BeatLength = 500, Uninherited = true }],
        HitObjects = points.Select((point, index) => new HitObject
        {
            X = point.X,
            Y = point.Y,
            Time = index * 200,
            Type = 1
        }).ToList()
    };

    private static string Time(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds).ToString(@"mm\:ss\.fff");
}
