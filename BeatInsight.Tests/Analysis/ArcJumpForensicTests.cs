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

/// <summary>Fixture-only forensic report. It invokes the current Arc detector without changing policy.</summary>
public sealed class ArcJumpForensicTests(ITestOutputHelper output)
{
    private const double MinimumArcAngle = 20;
    private const double MaximumArcAngle = 110;
    private const double MinimumArcDistance = 60;
    private const double MinimumAngleConsistency = .50;
    private const double NeutralSine = .02;

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
    public void RequestedFixtures_ReportOnlyValidatedArcSequences()
    {
        bool debugMode = DebugLogger.DebugMode;
        bool enabled = GameplayDebug.ArcJumpEnabled;
        bool detailed = GameplayDebug.ArcJumpDetailedEnabled;
        var report = new StringBuilder("# Arc / Curved Jump fixture forensic\n\n");
        report.AppendLine("Policy is unchanged: circle-only, shared Stream mask, Jump links, 60px minimum, " +
            "20°–110° inclusive, same signed rotation, three adjacent pivots, and min/max angle consistency >= 0.50.");
        report.AppendLine();
        report.AppendLine("|Map|Objects|Arc points|Arc objects|Sequences|Coverage %|Longest|Avg angle|Min angle|Max angle|Angle consistency|Avg distance|Distance consistency|Avg cumulative turn|CW|CCW|Confidence|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        var details = new StringBuilder();

        try
        {
            foreach (string fixture in FixtureNames)
            {
                Beatmap map = FixtureLoader.Load(fixture);
                GameplayProfile profile = map.GameplayProfile;
                DebugLogger.DebugMode = true;
                GameplayDebug.ArcJumpEnabled = true;
                GameplayDebug.ArcJumpDetailedEnabled = true;
                ArcJumpAnalysis result = JumpPatternAnalyzer.AnalyzeArc(map.HitObjects, profile);
                DebugLogger.DebugMode = false;
                GameplayDebug.ArcJumpEnabled = false;
                GameplayDebug.ArcJumpDetailedEnabled = false;

                IReadOnlyList<JumpPatternMatch> allMatches = JumpPatternAnalyzer.Analyze(map.HitObjects, profile);
                var streamIndices = profile.StreamSequences
                    .SelectMany(sequence => Enumerable.Range(sequence.StartObjectIndex, sequence.ObjectCount))
                    .ToHashSet();
                var arcIndices = result.Matches
                    .SelectMany(match => Enumerable.Range(match.StartObjectIndex, match.ObjectCount))
                    .ToHashSet();

                Assert.Equal(arcIndices.Count, result.ArcObjectCount);
                Assert.Equal(arcIndices.Count / (double)map.HitObjects.Count, result.Coverage, 12);
                Assert.Empty(arcIndices.Intersect(streamIndices));
                Assert.All(result.Points, point =>
                {
                    Assert.InRange(point.Angle, MinimumArcAngle, MaximumArcAngle);
                    Assert.True(point.PreviousDistance >= MinimumArcDistance);
                    Assert.True(point.NextDistance >= MinimumArcDistance);
                    Assert.NotEqual(ArcTurnDirection.Neutral, point.TurnDirection);
                });
                // A fixture with no validated Arc run has no aggregate consistency; its value is 0 by design.
                if (result.ArcPointCount == 0)
                    Assert.Equal(0, result.AngleConsistency);
                else
                    Assert.InRange(result.AngleConsistency, MinimumAngleConsistency, 1);
                Assert.InRange(result.DistanceConsistency, 0, 1);
                Assert.InRange(result.Confidence, 0, 1);

                report.AppendLine(FormattableString.Invariant(
                    $"|{Path.GetFileNameWithoutExtension(fixture)}|{map.HitObjects.Count}|{result.ArcPointCount}|{result.ArcObjectCount}|{result.SequenceCount}|{result.Coverage * 100:F3}|{result.LongestSequence}|{result.AverageTurnAngle:F3}|{result.MinimumTurnAngleObserved:F3}|{result.MaximumTurnAngleObserved:F3}|{result.AngleConsistency:F4}|{result.AverageJumpDistance:F3}|{result.DistanceConsistency:F4}|{result.AverageCumulativeTurn:F3}|{result.ClockwiseSequenceCount}|{result.CounterClockwiseSequenceCount}|{result.Confidence:F4}|"));
                AppendDetails(details, map, result, allMatches);
                AppendConsistencyRejections(details, map, profile);
            }
        }
        finally
        {
            DebugLogger.DebugMode = debugMode;
            GameplayDebug.ArcJumpEnabled = enabled;
            GameplayDebug.ArcJumpDetailedEnabled = detailed;
        }

        report.Append(details);
        string reportPath = Path.Combine(AppContext.BaseDirectory, "arc-jump-forensic.md");
        File.WriteAllText(reportPath, report.ToString());
        output.WriteLine(report.ToString());
        output.WriteLine($"Report: {reportPath}");
    }

    private static void AppendDetails(StringBuilder details, Beatmap map, ArcJumpAnalysis result,
        IReadOnlyList<JumpPatternMatch> allMatches)
    {
        details.AppendLine($"\n## {map.Title} [{map.Version}]\n");
        if (result.Matches.Count == 0)
            details.AppendLine("No validated Arc sequence.");

        foreach (JumpPatternMatch match in result.Matches)
        {
            // A match spans its endpoint objects; only its internal pivots are Arc points.
            // Excluding those endpoints avoids attributing a neighbouring, opposite-direction run to this one.
            ArcJumpPoint[] points = result.Points
                .Where(point => point.ObjectIndex > match.StartObjectIndex && point.ObjectIndex < match.EndObjectIndex)
                .ToArray();
            double angleConsistency = Consistency(points.Select(point => point.Angle));
            double distanceConsistency = Consistency(points.SelectMany(point => new[]
                { point.PreviousDistance, point.NextDistance }));
            double cumulative = points.Sum(point => point.Angle);
            details.AppendLine($"Sequence [{match.StartObjectIndex}..{match.EndObjectIndex}] " +
                $"{Time(match.StartTime)} -> {Time(match.EndTime)}; objects={match.ObjectCount}; " +
                $"direction={points[0].TurnDirection}; angleConsistency={angleConsistency.ToString("F4", CultureInfo.InvariantCulture)}; " +
                $"distanceConsistency={distanceConsistency.ToString("F4", CultureInfo.InvariantCulture)}; " +
                $"cumulativeTurn={cumulative.ToString("F3", CultureInfo.InvariantCulture)}°; " +
                $"confidence={match.Confidence.ToString("F4", CultureInfo.InvariantCulture)}; coexistence={Coexistence(match, allMatches)}");
            foreach (ArcJumpPoint point in points)
            {
                details.AppendLine(FormattableString.Invariant($"- i={point.ObjectIndex} | {Time(point.Time)} | ({point.PreviousX},{point.PreviousY})->({point.CurrentX},{point.CurrentY})->({point.NextX},{point.NextY}) | angle={point.Angle:F3}° | distances={point.PreviousDistance:F3}/{point.NextDistance:F3}px | direction={point.TurnDirection}"));
            }
        }
    }

    // This is forensic-only reconstruction of the current candidate rules. It records
    // runs that were structurally long enough but excluded solely by AngleConsistency.
    private static void AppendConsistencyRejections(StringBuilder details, Beatmap map, GameplayProfile profile)
    {
        bool[] streams = BuildStreamMask(map.HitObjects.Count, profile.StreamSequences);
        var candidates = new List<ForensicCandidate>();
        for (int i = 1; i + 1 < map.HitObjects.Count; i++)
        {
            HitObject previous = map.HitObjects[i - 1];
            HitObject current = map.HitObjects[i];
            HitObject next = map.HitObjects[i + 1];
            if ((previous.Type & 1) == 0 || (current.Type & 1) == 0 || (next.Type & 1) == 0
                || streams[i - 1] || streams[i] || streams[i + 1]
                || !GameplayAnalyzer.IsJumpLink(previous, current, out double previousDistance)
                || !GameplayAnalyzer.IsJumpLink(current, next, out double nextDistance)
                || previousDistance < MinimumArcDistance || nextDistance < MinimumArcDistance)
                continue;
            double angle = GameplayAnalyzer.GetTurnAngle(previous, current, next);
            if (angle < MinimumArcAngle || angle > MaximumArcAngle) continue;
            double sine = ((current.X - previous.X) * (next.Y - current.Y)
                - (current.Y - previous.Y) * (next.X - current.X)) / (previousDistance * nextDistance);
            if (Math.Abs(sine) < NeutralSine) continue;
            candidates.Add(new(i, angle, Math.Sign(sine)));
        }

        int start = 0;
        while (start < candidates.Count)
        {
            int end = start;
            while (end + 1 < candidates.Count && candidates[end + 1].Index == candidates[end].Index + 1
                && candidates[end + 1].Direction == candidates[end].Direction)
                end++;
            ForensicCandidate[] run = candidates.Skip(start).Take(end - start + 1).ToArray();
            double consistency = Consistency(run.Select(candidate => candidate.Angle));
            if (run.Length >= 3 && consistency < MinimumAngleConsistency)
            {
                details.AppendLine(FormattableString.Invariant($"Candidate rejected only by AngleConsistency: [{run[0].Index - 1}..{run[^1].Index + 1}] | pivots={run.Length} | angles=[{string.Join(", ", run.Select(candidate => candidate.Angle.ToString("F3", CultureInfo.InvariantCulture)))}] | consistency={consistency:F4}"));
            }
            start = end + 1;
        }
    }

    private static string Coexistence(JumpPatternMatch arc, IReadOnlyList<JumpPatternMatch> allMatches)
    {
        JumpPatternType[] types = [JumpPatternType.WideAngle, JumpPatternType.Spaced, JumpPatternType.Edge,
            JumpPatternType.Triangle, JumpPatternType.Sharp, JumpPatternType.OneTwo];
        IEnumerable<string> overlaps = allMatches.Where(match => types.Contains(match.Type))
            .Where(match => Math.Min(arc.EndObjectIndex, match.EndObjectIndex)
                >= Math.Max(arc.StartObjectIndex, match.StartObjectIndex))
            .Select(match => match.Type.ToString())
            .Distinct();
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

    private static double Consistency(IEnumerable<double> values)
    {
        double[] finite = values.Where(value => double.IsFinite(value) && value > 0).ToArray();
        return finite.Length == 0 ? 0 : Math.Clamp(finite.Min() / finite.Max(), 0, 1);
    }

    private sealed record ForensicCandidate(int Index, double Angle, int Direction);

    private static string Time(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds).ToString(@"mm\:ss\.fff");
}
