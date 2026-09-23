using System.IO;
using System.Text;
using BeatInsight.Analysis.JumpPatterns;
using Xunit.Abstractions;

namespace BeatInsight.Tests.Analysis;

/// <summary>Fixture-only control report for the local 1-2 pattern detector.</summary>
public sealed class OneTwoJumpForensicTests(ITestOutputHelper output)
{
    [Fact]
    public void AvailableFixtures_ReportOneTwoControls()
    {
        var report = new StringBuilder("# 1-2 Jump fixture controls\n\n");
        report.AppendLine("|Map|Objects|Points|Sequences|Coverage %|Longest|Distance consistency|Interval consistency|Alternation|Confidence|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        string fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps");
        foreach (string path in Directory.GetFiles(fixtureDirectory, "*.osu").OrderBy(path => path, StringComparer.Ordinal))
        {
            var map = FixtureLoader.Load(Path.GetFileName(path));
            var result = JumpPatternAnalyzer.AnalyzeOneTwo(map.HitObjects, map.GameplayProfile);
            report.AppendLine(FormattableString.Invariant($"|{Path.GetFileNameWithoutExtension(path)}|{map.HitObjects.Count}|{result.OneTwoPointCount}|{result.SequenceCount}|{result.Coverage * 100:F3}|{result.LongestSequence}|{result.DistanceConsistency:F4}|{result.IntervalConsistency:F4}|{result.AlternationConsistency:F4}|{result.Confidence:F4}|"));
            if (result.Matches.Count > 0)
            {
                report.AppendLine($"\n## {map.Title} [{map.Version}]");
                foreach (var match in result.Matches)
                {
                    var points = result.Points.Where(point => point.ObjectIndex >= match.StartObjectIndex
                        && point.ObjectIndex <= match.EndObjectIndex).ToArray();
                    report.AppendLine($"Sequence [{match.StartObjectIndex}..{match.EndObjectIndex}] occurrences={points.Length} objects={match.ObjectCount}");
                    foreach (var point in points)
                        report.AppendLine($"- i={point.ObjectIndex} direction={point.TurnDirection} angle={point.Angle:F2} distances={point.PreviousDistance:F2}/{point.NextDistance:F2} intervals={point.PreviousInterval}/{point.NextInterval} newCombo={map.HitObjects[point.ObjectIndex].IsNewCombo}");
                }
            }
        }

        string reportPath = Path.Combine(AppContext.BaseDirectory, "one-two-jump-controls.md");
        File.WriteAllText(reportPath, report.ToString());
        output.WriteLine(report.ToString());
    }
}
