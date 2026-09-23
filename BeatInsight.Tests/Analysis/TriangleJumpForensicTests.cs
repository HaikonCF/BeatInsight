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

/// <summary>Fixture-only report: it invokes the current Triangle detector without changing its policy.</summary>
public sealed class TriangleJumpForensicTests(ITestOutputHelper output)
{
    [Fact]
    public void AvailableFixtures_ReportOnlyValidatedTriangleSequences()
    {
        bool debug = DebugLogger.DebugMode;
        bool enabled = GameplayDebug.TriangleJumpEnabled;
        bool detailed = GameplayDebug.TriangleJumpDetailedEnabled;
        var report = new StringBuilder("# Triangle Jump fixture forensic\n\nIndices are zero-based and inclusive. Triangle points are central B objects in A -> B -> C.\n\n");
        report.AppendLine("|Map|Objects|Triangle points|Triangle objects|Coverage %|Longest|Avg angle|Min height|Avg height|Max height|Avg balance|Confidence|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        var details = new StringBuilder();

        try
        {
            string fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps");
            foreach (string path in Directory.GetFiles(fixtureDirectory, "*.osu").OrderBy(path => path, StringComparer.Ordinal))
            {
                var map = FixtureLoader.Load(Path.GetFileName(path));
                var profile = map.GameplayProfile;
                DebugLogger.DebugMode = true;
                GameplayDebug.TriangleJumpEnabled = true;
                GameplayDebug.TriangleJumpDetailedEnabled = true;
                var result = JumpPatternAnalyzer.AnalyzeTriangle(map.HitObjects, profile);
                DebugLogger.DebugMode = false;
                GameplayDebug.TriangleJumpEnabled = false;
                GameplayDebug.TriangleJumpDetailedEnabled = false;

                var allMatches = JumpPatternAnalyzer.Analyze(map.HitObjects, profile);
                var streamIndices = profile.StreamSequences.SelectMany(sequence =>
                    Enumerable.Range(sequence.StartObjectIndex, sequence.ObjectCount)).ToHashSet();
                var triangleIndices = result.Matches.SelectMany(match =>
                    Enumerable.Range(match.StartObjectIndex, match.ObjectCount)).ToHashSet();

                Assert.Equal(triangleIndices.Count, result.TriangleObjectCount);
                Assert.Equal(triangleIndices.Count / (double)map.HitObjects.Count, result.Coverage, 12);
                Assert.Empty(triangleIndices.Intersect(streamIndices));
                Assert.Equal(result.Points.Count, result.Points.Select(point => point.ObjectIndex).Distinct().Count());
                Assert.All(result.Points, point =>
                {
                    Assert.InRange(point.Angle, 30, 155);
                    Assert.True(point.PreviousDistance >= 60);
                    Assert.True(point.NextDistance >= 60);
                    Assert.True(point.OppositeDistance >= 60);
                    Assert.True(point.Height >= 30);
                    Assert.True(point.SideBalance >= .25);
                });
                Assert.InRange(result.Confidence, 0, 1);

                double minimumHeight = result.Points.Count == 0 ? 0 : result.Points.Min(point => point.Height);
                double maximumHeight = result.Points.Count == 0 ? 0 : result.Points.Max(point => point.Height);
                double averageBalance = result.Points.Count == 0 ? 0 : result.Points.Average(point => point.SideBalance);
                report.AppendLine(FormattableString.Invariant($"|{Path.GetFileNameWithoutExtension(path)}|{map.HitObjects.Count}|{result.TrianglePointCount}|{result.TriangleObjectCount}|{result.Coverage * 100:F3}|{result.LongestSequence}|{result.AverageTriangleAngle:F3}|{minimumHeight:F3}|{result.AverageHeight:F3}|{maximumHeight:F3}|{averageBalance:F4}|{result.Confidence:F4}|"));
                AppendDetails(details, map, result, allMatches);
            }
        }
        finally
        {
            DebugLogger.DebugMode = debug;
            GameplayDebug.TriangleJumpEnabled = enabled;
            GameplayDebug.TriangleJumpDetailedEnabled = detailed;
        }

        report.Append(details);
        string reportPath = Path.Combine(AppContext.BaseDirectory, "triangle-jump-forensic.md");
        File.WriteAllText(reportPath, report.ToString());
        output.WriteLine(report.ToString());
        output.WriteLine($"Report: {reportPath}");
    }

    private static void AppendDetails(StringBuilder details, Beatmap map, TriangleJumpAnalysis result,
        IReadOnlyList<JumpPatternMatch> allMatches)
    {
        details.AppendLine($"\n## {map.Title} [{map.Version}]\n");
        if (result.Matches.Count == 0)
        {
            details.AppendLine("No validated Triangle sequence.");
            return;
        }

        foreach (var match in result.Matches)
        {
            var points = result.Points.Where(point => point.ObjectIndex >= match.StartObjectIndex
                && point.ObjectIndex <= match.EndObjectIndex).ToArray();
            string coexistence = Coexistence(match, allMatches);
            details.AppendLine($"Sequence [{match.StartObjectIndex}..{match.EndObjectIndex}] {Time(match.StartTime)} -> {Time(match.EndTime)}; occurrences={points.Length}; objects={match.ObjectCount}; confidence={match.Confidence.ToString("F4", CultureInfo.InvariantCulture)}; coexistence={coexistence}");
            foreach (var point in points)
            {
                details.AppendLine(FormattableString.Invariant($"- i={point.ObjectIndex} | {Time(point.Time)} | A=({point.PreviousX},{point.PreviousY}) B=({point.CurrentX},{point.CurrentY}) C=({point.NextX},{point.NextY}) | AB={point.PreviousDistance:F3}px BC={point.NextDistance:F3}px AC={point.OppositeDistance:F3}px | angle={point.Angle:F3}° | height={point.Height:F3}px | balance={point.SideBalance:F4}"));
            }
        }
    }

    private static string Coexistence(JumpPatternMatch triangle, IReadOnlyList<JumpPatternMatch> matches)
    {
        var overlaps = matches.Where(match => match.Type is JumpPatternType.Sharp or JumpPatternType.Edge)
            .Where(match => Math.Min(triangle.EndObjectIndex, match.EndObjectIndex)
                >= Math.Max(triangle.StartObjectIndex, match.StartObjectIndex))
            .Select(match => match.Type.ToString());
        return string.Join(" + ", overlaps.DefaultIfEmpty("None"));
    }

    private static string Time(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds).ToString(@"mm\:ss\.fff");
}
