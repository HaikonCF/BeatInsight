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

/// <summary>Fixture-only forensic report. It changes no detector policy or production output.</summary>
public sealed class SharpJumpForensicTests(ITestOutputHelper output)
{
    [Fact]
    public void AvailableFixtures_ReportOnlyValidSharpSequences()
    {
        bool debug = DebugLogger.DebugMode;
        bool enabled = GameplayDebug.SharpJumpEnabled;
        bool detailed = GameplayDebug.SharpJumpDetailedEnabled;
        var report = new StringBuilder("# Sharp Jump fixture forensic\n\nIndices are zero-based and inclusive. Angle convention: 0° straight, 180° reversal.\n\n");
        report.AppendLine("|Map|Objects|Sharp points|Reversals|Sharp objects|Coverage %|Longest|Avg angle|Max angle|Confidence|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        var details = new StringBuilder();
        try
        {
            string fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps");
            foreach (string path in Directory.GetFiles(fixtureDirectory, "*.osu").OrderBy(path => path, StringComparer.Ordinal))
            {
                var map = FixtureLoader.Load(Path.GetFileName(path));
                var profile = map.GameplayProfile;
                DebugLogger.DebugMode = true;
                GameplayDebug.SharpJumpEnabled = true;
                GameplayDebug.SharpJumpDetailedEnabled = true;
                var result = JumpPatternAnalyzer.AnalyzeSharp(map.HitObjects, profile);
                DebugLogger.DebugMode = false;
                GameplayDebug.SharpJumpEnabled = false;
                GameplayDebug.SharpJumpDetailedEnabled = false;

                var allMatches = JumpPatternAnalyzer.Analyze(map.HitObjects, profile);
                var streamIndices = profile.StreamSequences.SelectMany(sequence =>
                    Enumerable.Range(sequence.StartObjectIndex, sequence.ObjectCount)).ToHashSet();
                var sharpIndices = result.Matches.SelectMany(match =>
                    Enumerable.Range(match.StartObjectIndex, match.ObjectCount)).ToHashSet();

                Assert.Equal(sharpIndices.Count, result.SharpObjectCount);
                Assert.Equal(sharpIndices.Count / (double)map.HitObjects.Count, result.Coverage, 12);
                Assert.Empty(sharpIndices.Intersect(streamIndices));
                Assert.Equal(result.Points.Count, result.Points.Select(point => point.ObjectIndex).Distinct().Count());
                Assert.All(result.Points, point =>
                {
                    Assert.InRange(point.Angle, 120, 180);
                    Assert.True(point.PreviousDistance >= 60);
                    Assert.True(point.NextDistance >= 60);
                });
                Assert.InRange(result.Confidence, 0, 1);

                report.AppendLine(FormattableString.Invariant($"|{Path.GetFileNameWithoutExtension(path)}|{map.HitObjects.Count}|{result.SharpPointCount}|{result.ReversalPointCount}|{result.SharpObjectCount}|{result.Coverage * 100:F3}|{result.LongestSequence}|{result.AverageSharpAngle:F3}|{result.StrongestSharpAngle:F3}|{result.Confidence:F4}|"));
                details.AppendLine($"\n## {map.Title} [{map.Version}]\n");
                if (result.Matches.Count == 0)
                {
                    details.AppendLine("No validated Sharp sequence.");
                    continue;
                }
                foreach (var match in result.Matches)
                {
                    string coexistence = Coexistence(match, allMatches);
                    details.AppendLine($"Sequence [{match.StartObjectIndex}..{match.EndObjectIndex}] {Time(match.StartTime)} -> {Time(match.EndTime)}; objects={match.ObjectCount}; confidence={match.Confidence.ToString("F4", CultureInfo.InvariantCulture)}; coexistence={coexistence}");
                    foreach (var point in result.Points.Where(point => point.ObjectIndex >= match.StartObjectIndex && point.ObjectIndex <= match.EndObjectIndex))
                    {
                        details.AppendLine(FormattableString.Invariant($"- i={point.ObjectIndex} | {Time(point.Time)} | ({point.PreviousX},{point.PreviousY})->({point.CurrentX},{point.CurrentY})->({point.NextX},{point.NextY}) | angle={point.Angle:F3}° | distances={point.PreviousDistance:F3}/{point.NextDistance:F3}px | reversal={point.IsReversal}"));
                    }
                }
            }
        }
        finally
        {
            DebugLogger.DebugMode = debug;
            GameplayDebug.SharpJumpEnabled = enabled;
            GameplayDebug.SharpJumpDetailedEnabled = detailed;
        }
        report.Append(details);
        string reportPath = Path.Combine(AppContext.BaseDirectory, "sharp-jump-forensic.md");
        File.WriteAllText(reportPath, report.ToString());
        output.WriteLine(report.ToString());
        output.WriteLine($"Report: {reportPath}");
    }

    [Fact]
    public void SyntheticConventionAndBoundaries_AreStable()
    {
        Assert.Equal(0, Angle(0), 8);
        Assert.Equal(90, Angle(90), 4);
        Assert.InRange(Angle(135), 134.999, 135.001);
        Assert.Equal(180, Angle(180), 8);

        // Integer coordinates cannot encode every real angle exactly. The 120° vector is
        // represented at sufficient scale and its measured value is asserted against the policy.
        var at119 = AnalyzeTurn(119);
        var at120 = AnalyzeTurn(120);
        var at121 = AnalyzeTurn(121);
        Assert.Empty(at119.Matches);
        Assert.Single(at120.Matches);
        Assert.Single(at121.Matches);
        Assert.All(at120.Points, point => Assert.True(point.Angle >= 120));

        var at169 = AnalyzeTurn(169);
        var at170 = AnalyzeTurn(170);
        var at171 = AnalyzeTurn(171);
        Assert.Single(at169.Matches);
        Assert.Equal(0, at169.ReversalPointCount);
        Assert.Single(at170.Matches);
        Assert.Equal(2, at170.ReversalPointCount);
        Assert.Single(at171.Matches);
        Assert.Equal(2, at171.ReversalPointCount);

        Assert.Empty(AnalyzeDistance(59).Matches);
        Assert.Single(AnalyzeDistance(60).Matches);
        Assert.Single(AnalyzeDistance(61).Matches);
    }

    private static double Angle(double degrees)
    {
        var (previous, current, next) = TurnObjects(degrees);
        return GameplayAnalyzer.GetTurnAngle(previous, current, next);
    }

    private static SharpJumpAnalysis AnalyzeTurn(double degrees)
    {
        const int length = 1_000_000;
        double radians = degrees * Math.PI / 180;
        int x = (int)Math.Round(length + length * Math.Cos(radians));
        int y = (int)Math.Round(length * Math.Sin(radians));
        var map = Map((0, 0), (length, 0), (x, y), (x + length, y));
        return JumpPatternAnalyzer.AnalyzeSharp(map.HitObjects, GameplayAnalyzer.Analyze(map));
    }

    private static SharpJumpAnalysis AnalyzeDistance(int distance)
    {
        // The two turns are ~135°, but every link has the supplied non-trivial distance.
        var map = Map((0, 0), (distance, 0), (0, distance), (distance, distance));
        return JumpPatternAnalyzer.AnalyzeSharp(map.HitObjects, GameplayAnalyzer.Analyze(map));
    }

    private static (HitObject Previous, HitObject Current, HitObject Next) TurnObjects(double degrees)
    {
        const int length = 1_000_000;
        double radians = degrees * Math.PI / 180;
        return (new() { X = 0, Y = 0 }, new() { X = length, Y = 0 }, new()
        {
            X = (int)Math.Round(length + length * Math.Cos(radians)),
            Y = (int)Math.Round(length * Math.Sin(radians))
        });
    }

    private static Beatmap Map(params (int X, int Y)[] points) => new()
    {
        HitObjects = points.Select((point, index) => new HitObject
            { X = point.X, Y = point.Y, Time = index * 200, Type = 1 }).ToList()
    };

    private static string Coexistence(JumpPatternMatch sharp, IReadOnlyList<JumpPatternMatch> matches)
    {
        var overlaps = matches.Where(match => match.Type is JumpPatternType.Edge or JumpPatternType.Linear)
            .Where(match => Math.Min(sharp.EndObjectIndex, match.EndObjectIndex)
                >= Math.Max(sharp.StartObjectIndex, match.StartObjectIndex))
            .Select(match => match.Type.ToString());
        return string.Join(" + ", overlaps.DefaultIfEmpty("None"));
    }

    private static string Time(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds).ToString(@"mm\:ss\.fff");

}
