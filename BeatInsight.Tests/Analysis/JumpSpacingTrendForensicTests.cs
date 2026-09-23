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

/// <summary>Fixture-only report for the current Jump spacing-trend policy. No detector policy is changed here.</summary>
public sealed class JumpSpacingTrendForensicTests(ITestOutputHelper output)
{
    private const double MinimumDistance = 60;
    private const int MinimumLinks = 4;
    private const double MinimumConsistency = .75;
    private const double MinimumMagnitude = .25;
    private const double MinimumStep = 10;

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
    public void RequestedFixtures_ReportOnlyValidatedSpacingTrends()
    {
        bool debugMode = DebugLogger.DebugMode;
        bool enabled = GameplayDebug.JumpSpacingTrendEnabled;
        bool detailed = GameplayDebug.JumpSpacingTrendDetailedEnabled;
        var report = new StringBuilder("# Accelerating / Decelerating Jump fixture forensic\n\n");
        report.AppendLine("Policy is unchanged: circle-only, shared Stream mask, Jump links, 60px minimum, " +
            "four links, meaningful deltas >=10px, consistency >=0.75, magnitude >=0.25, " +
            "and two consecutive opposite meaningful deltas split a run.");
        report.AppendLine();
        report.AppendLine("|Map|Accel seq.|Accel objects|Accel coverage %|Accel longest|Accel start|Accel end|Accel magnitude|Accel consistency|Accel avg distance|Accel confidence|Decel seq.|Decel objects|Decel coverage %|Decel longest|Decel start|Decel end|Decel magnitude|Decel consistency|Decel avg distance|Decel confidence|Jump links|Trend links|Trend link ratio %|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        var details = new StringBuilder();

        try
        {
            foreach (string fixture in FixtureNames)
            {
                Beatmap map = FixtureLoader.Load(fixture);
                GameplayProfile profile = map.GameplayProfile;
                DebugLogger.DebugMode = true;
                GameplayDebug.JumpSpacingTrendEnabled = true;
                GameplayDebug.JumpSpacingTrendDetailedEnabled = true;
                JumpSpacingTrendAnalysis result = JumpPatternAnalyzer.AnalyzeSpacingTrend(map.HitObjects, profile);
                DebugLogger.DebugMode = false;
                GameplayDebug.JumpSpacingTrendEnabled = false;
                GameplayDebug.JumpSpacingTrendDetailedEnabled = false;

                IReadOnlyList<JumpPatternMatch> allMatches = JumpPatternAnalyzer.Analyze(map.HitObjects, profile);
                bool[] streamMask = BuildStreamMask(map.HitObjects.Count, profile.StreamSequences);
                var accelObjects = ObjectsFor(result.Sequences, JumpSpacingTrendDirection.Accelerating);
                var decelObjects = ObjectsFor(result.Sequences, JumpSpacingTrendDirection.Decelerating);
                var trendLinks = LinksFor(result.Sequences);
                int qualifiedJumpLinks = CountQualifiedJumpLinks(map.HitObjects, streamMask);

                Assert.Equal(accelObjects.Count, result.AcceleratingObjectCount);
                Assert.Equal(decelObjects.Count, result.DeceleratingObjectCount);
                Assert.Equal(accelObjects.Count / (double)map.HitObjects.Count, result.AcceleratingCoverage, 12);
                Assert.Equal(decelObjects.Count / (double)map.HitObjects.Count, result.DeceleratingCoverage, 12);
                Assert.Empty(accelObjects.Intersect(Enumerable.Range(0, streamMask.Length).Where(index => streamMask[index])));
                Assert.Empty(decelObjects.Intersect(Enumerable.Range(0, streamMask.Length).Where(index => streamMask[index])));
                Assert.All(result.Sequences, sequence => AssertSequenceInvariant(sequence, map.HitObjects, streamMask));
                Assert.InRange(result.Accelerating.Confidence, 0, 1);
                Assert.InRange(result.Decelerating.Confidence, 0, 1);

                report.AppendLine(Row(fixture, result, qualifiedJumpLinks, trendLinks.Count));
                AppendDetails(details, map, result, allMatches, qualifiedJumpLinks, trendLinks.Count);
            }
        }
        finally
        {
            DebugLogger.DebugMode = debugMode;
            GameplayDebug.JumpSpacingTrendEnabled = enabled;
            GameplayDebug.JumpSpacingTrendDetailedEnabled = detailed;
        }

        report.Append(details);
        string reportPath = Path.Combine(AppContext.BaseDirectory, "jump-spacing-trend-forensic.md");
        File.WriteAllText(reportPath, report.ToString());
        output.WriteLine(report.ToString());
        output.WriteLine($"Report: {reportPath}");
    }

    private static string Row(string fixture, JumpSpacingTrendAnalysis result, int jumpLinks, int trendLinks)
    {
        JumpSpacingTrendMetrics a = result.Accelerating;
        JumpSpacingTrendMetrics d = result.Decelerating;
        double ratio = jumpLinks == 0 ? 0 : (double)trendLinks / jumpLinks * 100;
        return FormattableString.Invariant($"|{Path.GetFileNameWithoutExtension(fixture)}|{a.SequenceCount}|{a.ObjectCount}|{result.AcceleratingCoverage * 100:F3}|{a.LongestSequence}|{a.AverageStartDistance:F2}|{a.AverageEndDistance:F2}|{a.AverageTrendMagnitude:F4}|{a.AverageTrendConsistency:F4}|{a.AverageDistance:F2}|{a.Confidence:F4}|{d.SequenceCount}|{d.ObjectCount}|{result.DeceleratingCoverage * 100:F3}|{d.LongestSequence}|{d.AverageStartDistance:F2}|{d.AverageEndDistance:F2}|{d.AverageTrendMagnitude:F4}|{d.AverageTrendConsistency:F4}|{d.AverageDistance:F2}|{d.Confidence:F4}|{jumpLinks}|{trendLinks}|{ratio:F3}|");
    }

    private static void AppendDetails(StringBuilder details, Beatmap map, JumpSpacingTrendAnalysis result,
        IReadOnlyList<JumpPatternMatch> allMatches, int jumpLinks, int trendLinks)
    {
        details.AppendLine($"\n## {map.Title} [{map.Version}]\n");
        details.AppendLine($"Qualified Jump links={jumpLinks}; unique Trend links={trendLinks}; ratio=" +
            $"{(jumpLinks == 0 ? 0 : (double)trendLinks / jumpLinks):P2}.");
        if (result.Sequences.Count == 0)
            details.AppendLine("No validated spacing-trend sequence.");

        foreach (JumpSpacingTrendSequence sequence in result.Sequences.OrderBy(sequence => sequence.StartObjectIndex))
        {
            string significant = string.Join(", ", sequence.Deltas.Select(delta =>
                Math.Abs(delta) >= MinimumStep ? delta.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) :
                $"{delta.ToString("+0.00;-0.00", CultureInfo.InvariantCulture)} (ignored)"));
            details.AppendLine($"Sequence [{sequence.StartObjectIndex}..{sequence.EndObjectIndex}] " +
                $"{Time(sequence.StartTime)} -> {Time(sequence.EndTime)}; direction={sequence.Direction}; " +
                $"objects={sequence.ObjectCount}; distances=[{Values(sequence.Distances)}]; " +
                $"deltas=[{Values(sequence.Deltas)}]; significantDeltas=[{significant}]; " +
                $"averageDistance={sequence.Distances.Average().ToString("F3", CultureInfo.InvariantCulture)}; " +
                $"trendConsistency={sequence.TrendConsistency.ToString("F4", CultureInfo.InvariantCulture)}; " +
                $"trendMagnitude={sequence.TrendMagnitude.ToString("F4", CultureInfo.InvariantCulture)}; " +
                $"confidence={sequence.Confidence.ToString("F4", CultureInfo.InvariantCulture)}; " +
                $"coexistence={Coexistence(sequence, allMatches)}");
        }
    }

    private static void AssertSequenceInvariant(JumpSpacingTrendSequence sequence,
        IReadOnlyList<HitObject> objects, bool[] streamMask)
    {
        Assert.True(sequence.LinkCount >= MinimumLinks);
        Assert.Equal(sequence.LinkCount - 1, sequence.Deltas.Count);
        Assert.InRange(sequence.TrendConsistency, MinimumConsistency, 1);
        Assert.True(sequence.TrendMagnitude >= MinimumMagnitude);
        Assert.InRange(sequence.Confidence, 0, 1);
        for (int index = sequence.StartObjectIndex; index <= sequence.EndObjectIndex; index++)
            Assert.False(streamMask[index]);
        for (int index = 0; index < sequence.LinkCount; index++)
        {
            HitObject from = objects[sequence.StartObjectIndex + index];
            HitObject to = objects[sequence.StartObjectIndex + index + 1];
            Assert.True(GameplayAnalyzer.IsJumpLink(from, to, out double distance));
            Assert.True(distance >= MinimumDistance);
            Assert.Equal(sequence.Distances[index], distance, 9);
        }
    }

    private static HashSet<int> ObjectsFor(IEnumerable<JumpSpacingTrendSequence> all,
        JumpSpacingTrendDirection direction) => all.Where(sequence => sequence.Direction == direction)
        .SelectMany(sequence => Enumerable.Range(sequence.StartObjectIndex, sequence.ObjectCount)).ToHashSet();

    private static HashSet<(int Start, int End)> LinksFor(IEnumerable<JumpSpacingTrendSequence> all) => all
        .SelectMany(sequence => Enumerable.Range(sequence.StartObjectIndex, sequence.LinkCount)
            .Select(index => (index, index + 1))).ToHashSet();

    private static int CountQualifiedJumpLinks(IReadOnlyList<HitObject> objects, bool[] streamMask)
    {
        int count = 0;
        for (int index = 0; index + 1 < objects.Count; index++)
        {
            if ((objects[index].Type & 1) != 0 && (objects[index + 1].Type & 1) != 0
                && !streamMask[index] && !streamMask[index + 1]
                && GameplayAnalyzer.IsJumpLink(objects[index], objects[index + 1], out double distance)
                && distance >= MinimumDistance)
                count++;
        }
        return count;
    }

    private static string Coexistence(JumpSpacingTrendSequence trend, IReadOnlyList<JumpPatternMatch> allMatches)
    {
        JumpPatternType[] types = [JumpPatternType.Spaced, JumpPatternType.Linear, JumpPatternType.Arc,
            JumpPatternType.OneTwo, JumpPatternType.WideAngle, JumpPatternType.Sharp, JumpPatternType.Edge];
        IEnumerable<string> overlaps = allMatches.Where(match => types.Contains(match.Type))
            .Where(match => Math.Min(trend.EndObjectIndex, match.EndObjectIndex)
                >= Math.Max(trend.StartObjectIndex, match.StartObjectIndex))
            .Select(match => match.Type.ToString()).Distinct();
        return string.Join(" + ", overlaps.DefaultIfEmpty("None"));
    }

    private static bool[] BuildStreamMask(int count, IReadOnlyList<PatternSequence> sequences)
    {
        var mask = new bool[count];
        foreach (PatternSequence sequence in sequences)
            for (int index = Math.Max(0, sequence.StartObjectIndex);
                 index <= Math.Min(count - 1, sequence.EndObjectIndex); index++)
                mask[index] = true;
        return mask;
    }

    private static string Values(IEnumerable<double> values) => string.Join(", ", values
        .Select(value => value.ToString("F3", CultureInfo.InvariantCulture)));

    private static string Time(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds).ToString(@"mm\:ss\.fff");
}
