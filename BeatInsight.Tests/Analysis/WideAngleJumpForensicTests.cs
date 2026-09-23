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

/// <summary>
/// Fixture-only forensic report. It executes the current Wide Angle detector unchanged.
/// Indices are zero-based and inclusive; 0° means straight continuation and 180° reversal.
/// </summary>
public sealed class WideAngleJumpForensicTests(ITestOutputHelper output)
{
    private static readonly string[] FixtureNames =
    [
        "(can you) understand me [uhh].osu",
        "Ashioto Tarte Tatin [Koori's Insane].osu",
        "Exit This Earth's Atmosphere [Primordial Nucleosynthesis].osu",
        "FREEDOM DiVE [Arles].osu",
        "FREEDOM DiVE [FrenZ's Insane].osu",
        "Frozen [Collab Insane].osu",
        "Kira Killer [Mocaotic's Insane].osu",
        "Snow Goose [Extra].osu",
        "Tower Of Heaven [Extra].osu",
        "forever we can make it! [Kudowari's Expert].osu",
        "Vickeblanka - Black Rover (TV Size) (Sotarks) [Extreme].osu"
    ];

    [Fact]
    public void RequestedFixtures_ReportOnlyValidatedWideSequences()
    {
        bool debugMode = DebugLogger.DebugMode;
        bool enabled = GameplayDebug.WideAngleJumpEnabled;
        bool detailed = GameplayDebug.WideAngleJumpDetailedEnabled;
        var report = new StringBuilder("# Wide Angle Jump fixture forensic\n\n");
        report.AppendLine("The detector is executed unchanged: circle-only, shared Stream mask, two Jump links, " +
            "60px minimum per link, inclusive 60°–115° band, and two adjacent retained pivots.");
        report.AppendLine();
        report.AppendLine("|Map|Objects|Wide points|Wide objects|Sequences|Coverage %|Longest|Avg angle|Min angle|Max angle|Avg distance|Confidence|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        var details = new StringBuilder();
        var classifications = new List<(string Map, int WidePoints, int SharpPoints)>();

        try
        {
            foreach (string fixture in FixtureNames)
            {
                Beatmap map = FixtureLoader.Load(fixture);
                GameplayProfile profile = map.GameplayProfile;

                DebugLogger.DebugMode = true;
                GameplayDebug.WideAngleJumpEnabled = true;
                GameplayDebug.WideAngleJumpDetailedEnabled = true;
                WideAngleJumpAnalysis result = JumpPatternAnalyzer.AnalyzeWideAngle(map.HitObjects, profile);
                DebugLogger.DebugMode = false;
                GameplayDebug.WideAngleJumpEnabled = false;
                GameplayDebug.WideAngleJumpDetailedEnabled = false;

                IReadOnlyList<JumpPatternMatch> allMatches = JumpPatternAnalyzer.Analyze(map.HitObjects, profile);
                SharpJumpAnalysis sharp = JumpPatternAnalyzer.AnalyzeSharp(map.HitObjects, profile);
                var streamIndices = profile.StreamSequences
                    .SelectMany(sequence => Enumerable.Range(sequence.StartObjectIndex, sequence.ObjectCount))
                    .ToHashSet();
                var wideIndices = result.Matches
                    .SelectMany(match => Enumerable.Range(match.StartObjectIndex, match.ObjectCount))
                    .ToHashSet();

                Assert.Equal(wideIndices.Count, result.WideObjectCount);
                Assert.Equal(wideIndices.Count / (double)map.HitObjects.Count, result.Coverage, 12);
                Assert.Empty(wideIndices.Intersect(streamIndices));
                Assert.Equal(result.Points.Count, result.Points.Select(point => point.ObjectIndex).Distinct().Count());
                Assert.InRange(result.Confidence, 0, 1);
                Assert.All(result.Points, point =>
                {
                    Assert.InRange(point.Angle, 60, 115);
                    Assert.True(point.PreviousDistance >= 60);
                    Assert.True(point.NextDistance >= 60);
                    Assert.True((map.HitObjects[point.ObjectIndex - 1].Type & 1) != 0);
                    Assert.True((map.HitObjects[point.ObjectIndex].Type & 1) != 0);
                    Assert.True((map.HitObjects[point.ObjectIndex + 1].Type & 1) != 0);
                });
                Assert.Empty(result.Points.Select(point => point.ObjectIndex)
                    .Intersect(sharp.Points.Select(point => point.ObjectIndex)));

                report.AppendLine(FormattableString.Invariant(
                    $"|{Path.GetFileNameWithoutExtension(fixture)}|{map.HitObjects.Count}|{result.WidePointCount}|{result.WideObjectCount}|{result.SequenceCount}|{result.Coverage * 100:F3}|{result.LongestSequence}|{result.AverageWideAngle:F3}|{result.MinimumWideAngleObserved:F3}|{result.MaximumWideAngleObserved:F3}|{result.AverageJumpDistance:F3}|{result.Confidence:F4}|"));
                AppendDetails(details, map, result, allMatches, sharp);
                classifications.Add((Path.GetFileNameWithoutExtension(fixture), result.WidePointCount, sharp.SharpPointCount));
            }
        }
        finally
        {
            DebugLogger.DebugMode = debugMode;
            GameplayDebug.WideAngleJumpEnabled = enabled;
            GameplayDebug.WideAngleJumpDetailedEnabled = detailed;
        }

        report.AppendLine();
        report.AppendLine("## Wide versus Sharp distribution\n");
        foreach (var item in classifications)
        {
            string category = item switch
            {
                { WidePoints: 0, SharpPoints: 0 } => "No retained Wide or Sharp turn chain",
                { WidePoints: > 0, SharpPoints: 0 } => "Wide-only retained turns",
                { WidePoints: 0, SharpPoints: > 0 } => "Sharp-only retained turns",
                { WidePoints: > 0, SharpPoints: > 0 } when item.WidePoints > item.SharpPoints => "Both; Wide points more frequent",
                { WidePoints: > 0, SharpPoints: > 0 } when item.SharpPoints > item.WidePoints => "Both; Sharp points more frequent",
                _ => "Both; equal retained point count"
            };
            report.AppendLine($"- {item.Map}: {category} (Wide={item.WidePoints}, Sharp={item.SharpPoints})");
        }

        report.Append(details);
        string reportPath = Path.Combine(AppContext.BaseDirectory, "wide-angle-jump-forensic.md");
        File.WriteAllText(reportPath, report.ToString());
        output.WriteLine(report.ToString());
        output.WriteLine($"Report: {reportPath}");
    }

    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(61, true)]
    [InlineData(114, true)]
    [InlineData(115, true)]
    [InlineData(116, false)]
    [InlineData(119, false)]
    [InlineData(120, false)]
    [InlineData(121, false)]
    public void IntegerCoordinateAngleBoundaries_RemainSeparated(int requestedAngle, bool expectedWide)
    {
        Beatmap map = TwoTurns(requestedAngle);
        WideAngleJumpAnalysis wide = Analyze(map);
        SharpJumpAnalysis sharp = JumpPatternAnalyzer.AnalyzeSharp(map.HitObjects, GameplayAnalyzer.Analyze(map));

        Assert.True(expectedWide == (wide.SequenceCount != 0),
            $"requested={requestedAngle}; measured={Angles(map)}");
        // The integer 120° construction can round infinitesimally below the Sharp
        // boundary; 121° is the robust synthetic confirmation of the adjacent band.
        if (requestedAngle >= 121)
            Assert.NotEmpty(sharp.Matches);
        if (requestedAngle is 116 or 119)
            Assert.Empty(sharp.Matches);
        Assert.Empty(wide.Points.Select(point => point.ObjectIndex)
            .Intersect(sharp.Points.Select(point => point.ObjectIndex)));
    }

    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(61, true)]
    public void DistanceBoundaries_RemainInclusiveAt60(int distance, bool expectedWide)
    {
        WideAngleJumpAnalysis result = Analyze(Map((0, 0), (distance, 0), (distance, distance),
            (distance * 2, distance)));
        Assert.Equal(expectedWide, result.SequenceCount != 0);
    }

    [Fact]
    public void ReversalAndIsolatedWidePoint_AreNotCompleteWidePatterns()
    {
        Assert.Empty(Analyze(TwoTurns(180)).Matches);
        Assert.Empty(Analyze(Map((0, 0), (100, 0), (100, 100))).Matches);
    }

    private static void AppendDetails(StringBuilder details, Beatmap map, WideAngleJumpAnalysis result,
        IReadOnlyList<JumpPatternMatch> allMatches, SharpJumpAnalysis sharp)
    {
        details.AppendLine($"\n## {map.Title} [{map.Version}]\n");
        if (result.Matches.Count == 0)
        {
            details.AppendLine("No validated Wide Angle sequence.");
            return;
        }

        foreach (JumpPatternMatch match in result.Matches)
        {
            WideAngleJumpPoint[] points = result.Points
                .Where(point => point.ObjectIndex >= match.StartObjectIndex
                    && point.ObjectIndex <= match.EndObjectIndex)
                .ToArray();
            string coexistence = Coexistence(match, allMatches, sharp);
            details.AppendLine($"Sequence [{match.StartObjectIndex}..{match.EndObjectIndex}] " +
                $"{Time(match.StartTime)} -> {Time(match.EndTime)}; objects={match.ObjectCount}; " +
                $"wide pivots={points.Length}; confidence={match.Confidence.ToString("F4", CultureInfo.InvariantCulture)}; " +
                $"coexistence={coexistence}");
            foreach (WideAngleJumpPoint point in points)
            {
                details.AppendLine(FormattableString.Invariant($"- i={point.ObjectIndex} | {Time(point.Time)} | ({point.PreviousX},{point.PreviousY})->({point.CurrentX},{point.CurrentY})->({point.NextX},{point.NextY}) | angle={point.Angle:F3}° | distances={point.PreviousDistance:F3}/{point.NextDistance:F3}px"));
            }
        }
    }

    private static string Coexistence(JumpPatternMatch wide, IReadOnlyList<JumpPatternMatch> allMatches,
        SharpJumpAnalysis sharp)
    {
        JumpPatternType[] types = [JumpPatternType.Spaced, JumpPatternType.Edge, JumpPatternType.Triangle,
            JumpPatternType.OneTwo];
        var overlaps = allMatches.Where(match => types.Contains(match.Type))
            .Where(match => Overlaps(wide, match))
            .Select(match => match.Type.ToString())
            .Distinct()
            .ToList();
        bool sharpElsewhere = sharp.Matches.Any(match => Overlaps(wide, match));
        if (sharpElsewhere) overlaps.Add("Sharp (different pivot)");
        return string.Join(" + ", overlaps.DefaultIfEmpty("None"));
    }

    private static bool Overlaps(JumpPatternMatch first, JumpPatternMatch second) =>
        Math.Min(first.EndObjectIndex, second.EndObjectIndex) >= Math.Max(first.StartObjectIndex, second.StartObjectIndex);

    private static WideAngleJumpAnalysis Analyze(Beatmap map) =>
        JumpPatternAnalyzer.AnalyzeWideAngle(map.HitObjects, GameplayAnalyzer.Analyze(map));

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

    private static Beatmap TwoTurns(int degrees)
    {
        const int length = 10_000;
        // osu! coordinates are integral. The 60° construction lies just inside the
        // inclusive lower boundary for both pivots after integer rounding.
        if (degrees == 60)
            return Map((0, 0), (length, 0), (15_000, 8_662), (10_000, 17_320));

        double radians = degrees * Math.PI / 180;
        var first = (0, 0);
        var second = (length, 0);
        var third = (second.Item1 + (int)Math.Round(length * Math.Cos(radians)),
            (int)Math.Round(length * Math.Sin(radians)));
        var fourth = (third.Item1 + (int)Math.Round(length * Math.Cos(2 * radians)),
            third.Item2 + (int)Math.Round(length * Math.Sin(2 * radians)));
        return Map(first, second, third, fourth);
    }

    private static string Angles(Beatmap map) => string.Join(", ", Enumerable.Range(1, map.HitObjects.Count - 2)
        .Select(index => GameplayAnalyzer.GetTurnAngle(map.HitObjects[index - 1], map.HitObjects[index],
            map.HitObjects[index + 1]).ToString("F6", CultureInfo.InvariantCulture)));

    private static string Time(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds).ToString(@"mm\:ss\.fff");
}
