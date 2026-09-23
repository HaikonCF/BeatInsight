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

/// <summary>Fixture-only forensic report. It invokes the current Reversal detector without changing policy.</summary>
public sealed class ReversalJumpForensicTests(ITestOutputHelper output)
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
    public void RequestedFixtures_ReportOnlyValidatedReversalSequences()
    {
        bool debugMode = DebugLogger.DebugMode;
        bool enabled = GameplayDebug.ReversalJumpEnabled;
        bool detailed = GameplayDebug.ReversalJumpDetailedEnabled;
        var report = new StringBuilder("# Reversal Jump fixture forensic\n\n");
        report.AppendLine("Detector policy is unchanged: circles only, shared Stream mask, two Jump links, " +
            "60px minimum per link, inclusive >=170° angle, and two adjacent retained pivots.");
        report.AppendLine();
        report.AppendLine("|Map|Objects|Sharp points|Reversal points|Reversal objects|Sequences|Coverage %|Longest|Avg angle|Min angle|Max angle|Avg distance|Distance consistency|Avg return distance|Reversal/Sharp|Confidence|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        var details = new StringBuilder();

        try
        {
            foreach (string fixture in FixtureNames)
            {
                Beatmap map = FixtureLoader.Load(fixture);
                GameplayProfile profile = map.GameplayProfile;
                DebugLogger.DebugMode = true;
                GameplayDebug.ReversalJumpEnabled = true;
                GameplayDebug.ReversalJumpDetailedEnabled = true;
                ReversalJumpAnalysis result = JumpPatternAnalyzer.AnalyzeReversal(map.HitObjects, profile);
                DebugLogger.DebugMode = false;
                GameplayDebug.ReversalJumpEnabled = false;
                GameplayDebug.ReversalJumpDetailedEnabled = false;

                SharpJumpAnalysis sharp = JumpPatternAnalyzer.AnalyzeSharp(map.HitObjects, profile);
                IReadOnlyList<JumpPatternMatch> allMatches = JumpPatternAnalyzer.Analyze(map.HitObjects, profile);
                var streamIndices = profile.StreamSequences
                    .SelectMany(sequence => Enumerable.Range(sequence.StartObjectIndex, sequence.ObjectCount))
                    .ToHashSet();
                var reversalIndices = result.Matches
                    .SelectMany(match => Enumerable.Range(match.StartObjectIndex, match.ObjectCount))
                    .ToHashSet();
                var sharpPivots = sharp.Points.Select(point => point.ObjectIndex).ToHashSet();

                Assert.Equal(reversalIndices.Count, result.ReversalObjectCount);
                Assert.Equal(reversalIndices.Count / (double)map.HitObjects.Count, result.Coverage, 12);
                Assert.Empty(reversalIndices.Intersect(streamIndices));
                Assert.All(result.Points, point =>
                {
                    Assert.True(point.Angle >= 170);
                    Assert.True(point.PreviousDistance >= 60);
                    Assert.True(point.NextDistance >= 60);
                    Assert.Contains(point.ObjectIndex, sharpPivots);
                });
                Assert.InRange(result.DistanceConsistency, 0, 1);
                Assert.InRange(result.Confidence, 0, 1);

                string ratio = sharp.SharpPointCount == 0 ? "n/a" :
                    (result.ReversalPointCount / (double)sharp.SharpPointCount).ToString("F4", CultureInfo.InvariantCulture);
                report.AppendLine(FormattableString.Invariant(
                    $"|{Path.GetFileNameWithoutExtension(fixture)}|{map.HitObjects.Count}|{sharp.SharpPointCount}|{result.ReversalPointCount}|{result.ReversalObjectCount}|{result.SequenceCount}|{result.Coverage * 100:F3}|{result.LongestSequence}|{result.AverageReversalAngle:F3}|{result.MinimumReversalAngleObserved:F3}|{result.MaximumReversalAngleObserved:F3}|{result.AverageJumpDistance:F3}|{result.DistanceConsistency:F4}|{result.AverageReturnDistance:F3}|{ratio}|{result.Confidence:F4}|"));
                AppendDetails(details, map, result, allMatches);
            }
        }
        finally
        {
            DebugLogger.DebugMode = debugMode;
            GameplayDebug.ReversalJumpEnabled = enabled;
            GameplayDebug.ReversalJumpDetailedEnabled = detailed;
        }

        report.Append(details);
        string reportPath = Path.Combine(AppContext.BaseDirectory, "reversal-jump-forensic.md");
        File.WriteAllText(reportPath, report.ToString());
        output.WriteLine(report.ToString());
        output.WriteLine($"Report: {reportPath}");
    }

    [Theory]
    [InlineData(169, false)]
    [InlineData(170, true)]
    [InlineData(171, true)]
    [InlineData(179, true)]
    [InlineData(180, true)]
    public void IntegerCoordinateAngleBoundaries_AreStable(int requestedAngle, bool expectedReversal)
    {
        ReversalJumpAnalysis result = Analyze(TwoTurns(requestedAngle));
        Assert.Equal(expectedReversal, result.SequenceCount != 0);
    }

    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(61, true)]
    public void DistanceBoundaries_AreInclusiveAt60(int distance, bool expectedReversal)
    {
        ReversalJumpAnalysis result = Analyze(Map((0, 0), (distance, 0), (0, 0), (distance, 0)));
        Assert.Equal(expectedReversal, result.SequenceCount != 0);
    }

    [Fact]
    public void Sharp150AndAnIsolatedReversal_DoNotMakeACompleteReversalPattern()
    {
        Assert.Empty(Analyze(TwoTurns(150)).Matches);
        Assert.Empty(Analyze(Map((0, 0), (100, 0), (0, 0))).Matches);
    }

    private static void AppendDetails(StringBuilder details, Beatmap map, ReversalJumpAnalysis result,
        IReadOnlyList<JumpPatternMatch> allMatches)
    {
        details.AppendLine($"\n## {map.Title} [{map.Version}]\n");
        if (result.Matches.Count == 0)
        {
            details.AppendLine("No validated Reversal sequence.");
            return;
        }

        foreach (JumpPatternMatch match in result.Matches)
        {
            ReversalJumpPoint[] points = result.Points
                .Where(point => point.ObjectIndex >= match.StartObjectIndex && point.ObjectIndex <= match.EndObjectIndex)
                .ToArray();
            details.AppendLine($"Sequence [{match.StartObjectIndex}..{match.EndObjectIndex}] " +
                $"{Time(match.StartTime)} -> {Time(match.EndTime)}; objects={match.ObjectCount}; " +
                $"reversal pivots={points.Length}; confidence={match.Confidence.ToString("F4", CultureInfo.InvariantCulture)}; " +
                $"coexistence={Coexistence(match, allMatches)}");
            foreach (ReversalJumpPoint point in points)
            {
                details.AppendLine(FormattableString.Invariant($"- i={point.ObjectIndex} | {Time(point.Time)} | ({point.PreviousX},{point.PreviousY})->({point.CurrentX},{point.CurrentY})->({point.NextX},{point.NextY}) | angle={point.Angle:F3}° | distances={point.PreviousDistance:F3}/{point.NextDistance:F3}px | return={point.ReturnDistance:F3}px"));
            }
        }
    }

    private static string Coexistence(JumpPatternMatch reversal, IReadOnlyList<JumpPatternMatch> allMatches)
    {
        JumpPatternType[] types = [JumpPatternType.Sharp, JumpPatternType.OneTwo, JumpPatternType.Spaced,
            JumpPatternType.Edge, JumpPatternType.Triangle, JumpPatternType.WideAngle];
        IEnumerable<string> overlaps = allMatches.Where(match => types.Contains(match.Type))
            .Where(match => Math.Min(reversal.EndObjectIndex, match.EndObjectIndex)
                >= Math.Max(reversal.StartObjectIndex, match.StartObjectIndex))
            .Select(match => match.Type.ToString())
            .Distinct();
        return string.Join(" + ", overlaps.DefaultIfEmpty("None"));
    }

    private static ReversalJumpAnalysis Analyze(Beatmap map) =>
        JumpPatternAnalyzer.AnalyzeReversal(map.HitObjects, GameplayAnalyzer.Analyze(map));

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
        const int length = 100_000;
        // Integral osu! coordinates require a small inward representation at exactly 170°.
        if (degrees == 170)
            return Map((0, 0), (length, 0), (1_519, 17_364), (95_488, -16_836));

        double radians = degrees * Math.PI / 180;
        var first = (0, 0);
        var second = (length, 0);
        var third = (second.Item1 + (int)Math.Round(length * Math.Cos(radians)),
            (int)Math.Round(length * Math.Sin(radians)));
        var fourth = (third.Item1 + (int)Math.Round(length * Math.Cos(2 * radians)),
            third.Item2 + (int)Math.Round(length * Math.Sin(2 * radians)));
        return Map(first, second, third, fourth);
    }

    private static string Time(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds).ToString(@"mm\:ss\.fff");
}
