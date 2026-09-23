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

public sealed class EdgeJumpForensicTests(ITestOutputHelper output)
{
    [Fact]
    public void AvailableFixtures_ReportValidatedSequencesAndVerifyInvariants()
    {
        bool debug = DebugLogger.DebugMode, detail = DebugLogger.DetailedDebug;
        bool edge = GameplayDebug.EdgeJumpEnabled, edgeDetail = GameplayDebug.EdgeJumpDetailedEnabled;
        var report = new StringBuilder("# Edge Jump fixture forensic\n\nIndices are zero-based and inclusive. Coverage denominator includes all hitobjects.\n\n");
        var details = new StringBuilder();
        report.AppendLine("|Fixture|Objects|Edge objects|Transitions|Strong|Coverage %|Longest objects|Confidence|Sequences|Peripheral circles|Linear overlaps|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        try
        {
            var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps"), "*.osu")
                .OrderBy(p => p, StringComparer.Ordinal).ToArray();
            Assert.NotEmpty(files);
            foreach (var path in files)
            {
                var map = FixtureLoader.Load(Path.GetFileName(path));
                var profile = map.GameplayProfile;
                DebugLogger.DebugMode = true;
                GameplayDebug.EdgeJumpEnabled = true;
                GameplayDebug.EdgeJumpDetailedEnabled = true;
                var result = JumpPatternAnalyzer.AnalyzeEdge(map.HitObjects, profile);
                // Restore disabled diagnostic mode before other analysis/report operations.
                DebugLogger.DebugMode = false;
                GameplayDebug.EdgeJumpEnabled = false;
                GameplayDebug.EdgeJumpDetailedEnabled = false;
                var linear = JumpPatternAnalyzer.Analyze(map.HitObjects, profile)
                    .Where(m => m.Type == JumpPatternType.Linear).ToArray();
                var streams = profile.StreamSequences.SelectMany(s =>
                    Enumerable.Range(s.StartObjectIndex, s.ObjectCount)).ToHashSet();
                var indices = result.Matches.SelectMany(m =>
                    Enumerable.Range(m.StartObjectIndex, m.ObjectCount)).ToHashSet();
                Assert.Equal(indices.Count, result.EdgeObjectCount);
                Assert.Equal(indices.Count / (double)map.HitObjects.Count, result.Coverage, 12);
                Assert.Empty(indices.Intersect(streams));
                Assert.Equal(result.Transitions.Count, result.Transitions.Select(t => t.StartObjectIndex).Distinct().Count());
                Assert.InRange(result.Confidence, 0, 1);
                Assert.Equal(result.Matches.Sum(m => m.ObjectCount - 1), result.EdgeTransitionCount);

                int near = map.HitObjects.Count(o => (o.Type & 1) != 0 && Peripheral(o));
                int overlaps = result.Matches.Count(e => linear.Any(l =>
                    Math.Min(e.EndObjectIndex, l.EndObjectIndex) - Math.Max(e.StartObjectIndex, l.StartObjectIndex) + 1 >= 3));
                report.AppendLine(FormattableString.Invariant($"|{Path.GetFileNameWithoutExtension(path)}|{map.HitObjects.Count}|{result.EdgeObjectCount}|{result.EdgeTransitionCount}|{result.StrongTransitionCount}|{result.Coverage * 100:F3}|{result.LongestSequence}|{result.Confidence:F4}|{result.Matches.Count}|{near}|{overlaps}|"));
                details.AppendLine($"\n## {map.Title} [{map.Version}]\n");
                if (result.Matches.Count == 0) details.AppendLine("No validated Edge sequence.");
                foreach (var match in result.Matches)
                {
                    Assert.True(match.ObjectCount >= 3);
                    details.AppendLine($"Sequence [{match.StartObjectIndex}..{match.EndObjectIndex}] {Time(match.StartTime)} -> {Time(match.EndTime)}; objects={match.ObjectCount}; confidence={match.Confidence.ToString("F4", CultureInfo.InvariantCulture)}");
                    foreach (var t in result.Transitions.Where(t => t.StartObjectIndex >= match.StartObjectIndex && t.EndObjectIndex <= match.EndObjectIndex))
                    {
                        var a = map.HitObjects[t.StartObjectIndex];
                        var b = map.HitObjects[t.EndObjectIndex];
                        Assert.True(Peripheral(a) && Peripheral(b));
                        Assert.Equal(1, a.Type & 1);
                        Assert.Equal(1, b.Type & 1);
                        double distance = Math.Sqrt(Math.Pow((double)b.X - a.X, 2) + Math.Pow((double)b.Y - a.Y, 2));
                        Assert.True(distance >= 120);
                        Assert.Equal(distance, t.Distance, 8);
                        Assert.InRange(b.Time - a.Time, 30, 600);
                        Assert.Equal((a.X <= 64 && b.X >= 448) || (b.X <= 64 && a.X >= 448)
                            || (a.Y <= 64 && b.Y >= 320) || (b.Y <= 64 && a.Y >= 320), t.Strong);
                        details.AppendLine(FormattableString.Invariant($"- {t.StartObjectIndex}->{t.EndObjectIndex} | {Time(t.StartTime)}->{Time(t.EndTime)} | ({a.X},{a.Y})->({b.X},{b.Y}) | {t.From}->{t.To} | {distance:F3} px | Strong={t.Strong}"));
                    }
                }
            }
        }
        finally
        {
            DebugLogger.DebugMode = debug;
            DebugLogger.DetailedDebug = detail;
            GameplayDebug.EdgeJumpEnabled = edge;
            GameplayDebug.EdgeJumpDetailedEnabled = edgeDetail;
        }
        report.Append(details);
        string reportPath = Path.Combine(AppContext.BaseDirectory, "edge-jump-forensic.md");
        File.WriteAllText(reportPath, report.ToString());
        output.WriteLine(report.ToString());
        output.WriteLine($"Report: {reportPath}");
    }

    private static bool Peripheral(HitObject o) => o.X <= 64 || o.X >= 448 || o.Y <= 64 || o.Y >= 320;

    [Theory]
    [InlineData(7, .9375)]
    [InlineData(8, 1)]
    [InlineData(9, 1)]
    public void ConfidenceSaturation_UsesEightObjects(int count, double expected)
    {
        var objects = Enumerable.Range(0, count).Select(i => new HitObject
            { X = i % 2 == 0 ? 0 : 512, Y = 192, Time = i * 200, Type = 1 }).ToArray();
        var result = JumpPatternAnalyzer.AnalyzeEdge(objects, new GameplayProfile());
        Assert.Equal(expected, result.Confidence, 12);
    }

    [Fact]
    public void ManyCentralJumpsAndOnlyOnePeripheralObject_DoNotCreateCoverage()
    {
        var objects = Enumerable.Range(0, 20).Select(i => new HitObject
            { X = i % 2 == 0 ? 100 : 400, Y = 192, Time = i * 200, Type = 1 }).ToArray();
        objects[10].X = 64;
        var result = JumpPatternAnalyzer.AnalyzeEdge(objects, new GameplayProfile());
        Assert.Empty(result.Matches);
        Assert.Equal(0, result.Coverage);
    }

    private static string Time(int ms) => TimeSpan.FromMilliseconds(ms).ToString(@"mm\:ss\.fff");

    [Theory]
    [InlineData(63, 192, true)]
    [InlineData(64, 192, true)]
    [InlineData(65, 192, false)]
    [InlineData(447, 192, false)]
    [InlineData(448, 192, true)]
    [InlineData(449, 192, true)]
    [InlineData(256, 63, true)]
    [InlineData(256, 64, true)]
    [InlineData(256, 65, false)]
    [InlineData(256, 319, false)]
    [InlineData(256, 320, true)]
    [InlineData(256, 321, true)]
    public void MarginBoundary_IsInclusive(int x, int y, bool expected) =>
        Assert.Equal(expected, JumpPatternAnalyzer.GetEdgeZone(new() { X = x, Y = y }) != EdgeZone.None);

    [Theory]
    [InlineData(119, false)]
    [InlineData(120, true)]
    [InlineData(121, true)]
    public void DistanceBoundary_IsInclusive(int distance, bool expected)
    {
        var map = new Beatmap
        {
            HitObjects = Enumerable.Range(0, 3).Select(i => new HitObject
                { X = 100 + i * distance, Y = 64, Time = i * 200, Type = 1 }).ToList()
        };
        var result = JumpPatternAnalyzer.AnalyzeEdge(map.HitObjects, GameplayAnalyzer.Analyze(map));
        Assert.Equal(expected ? 1 : 0, result.Matches.Count);
        Assert.Equal(expected ? 3 : 0, result.EdgeObjectCount);
    }
}
