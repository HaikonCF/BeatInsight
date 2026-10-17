using System.Text;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
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
    private const double MinimumOneTwoJumpDistance = 60;
    private const double MinimumOneTwoTurnDegrees = 35;
    private const double NearReversalTurnDegrees = 170;
    private const double MinimumOneTwoDistanceConsistency = .55;
    private const double MinimumOneTwoIntervalConsistency = .55;
    private const double MinimumStableReversalTurnSine = .02;

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
        int? blackRoverOneTwoSequenceCount = null;

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
                blackRoverOneTwoSequenceCount =
                    sections.Sum(section => section.JumpPatterns.OneTwo.SequenceCount);
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
        // Intentional block/reset semantics allow at most one reset per occurrence;
        // the Black Rover forensic validated all nine final occurrences.
        Assert.Equal(9, blackRoverOneTwoSequenceCount);
    }

    [Fact]
    public void BlackRoverExtreme_OneTwoForensicOutput()
    {
        const string fixture =
            "Vickeblanka - Black Rover (TV Size) (Sotarks) [Extreme].osu";
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps", fixture);
        byte[] bytes = File.ReadAllBytes(path);
        Assert.Equal(
            "C5938B9CB04281D58A4611F31AAFF1AE2DD97F613C8BE782D4AF2DEA2E58756D",
            Convert.ToHexString(SHA256.HashData(bytes)));
        Assert.Contains("BeatmapID:1655981", Encoding.UTF8.GetString(bytes));

        Beatmap map = FixtureLoader.Load(fixture);
        GameplayProfile gameplay = GameplayAnalyzer.Analyze(map);
        OneTwoJumpAnalysis current =
            JumpPatternAnalyzer.AnalyzeOneTwo(map.HitObjects, gameplay);
        IReadOnlyList<JumpPatternMatch> oldMatches =
            AnalyzeOneTwoUsingOldStrictAlternation(map.HitObjects, gameplay);
        IReadOnlyList<GameplaySectionAnalysis> sections =
            GameplaySectionAnalyzer.Analyze(map);

        var report = new StringBuilder();
        report.AppendLine(
            $"MAP {map.Artist} - {map.Title} ({map.Creator}) [{map.Version}] | " +
            $"Objects={map.HitObjects.Count} | New={current.SequenceCount} | Old={oldMatches.Count}");
        report.AppendLine("OLD STRICT MATCHES");
        for (int oldIndex = 0; oldIndex < oldMatches.Count; oldIndex++)
        {
            JumpPatternMatch old = oldMatches[oldIndex];
            report.AppendLine(
                $"  OLD#{oldIndex + 1}: Time={old.StartTime}-{old.EndTime} | " +
                $"Objects={old.StartObjectIndex}-{old.EndObjectIndex} ({old.ObjectCount})");
        }

        for (int occurrenceIndex = 0; occurrenceIndex < current.Matches.Count; occurrenceIndex++)
        {
            JumpPatternMatch match = current.Matches[occurrenceIndex];
            OneTwoJumpPoint[] points = current.Points
                .Where(point => point.ObjectIndex > match.StartObjectIndex &&
                                point.ObjectIndex < match.EndObjectIndex)
                .GroupBy(point => point.ObjectIndex)
                .Select(group => group.First())
                .OrderBy(point => point.ObjectIndex)
                .ToArray();
            double[] distances = Enumerable
                .Range(match.StartObjectIndex, match.ObjectCount - 1)
                .Select(index => Distance(map.HitObjects[index], map.HitObjects[index + 1]))
                .ToArray();
            double[] intervals = Enumerable
                .Range(match.StartObjectIndex, match.ObjectCount - 1)
                .Select(index => (double)(map.HitObjects[index + 1].Time - map.HitObjects[index].Time))
                .ToArray();
            int alternatingBlockCount = points.Skip(1).Count(point => point.AlternatingWithPrevious);
            int resetCount = points.Skip(1).Count(point => !point.AlternatingWithPrevious);
            double alternationScore = points.Length <= 1
                ? 0
                : (double)alternatingBlockCount / (points.Length - 1);
            int[] containedOldMatches = oldMatches
                .Select((candidate, index) => (candidate, index))
                .Where(item => item.candidate.StartObjectIndex >= match.StartObjectIndex &&
                               item.candidate.EndObjectIndex <= match.EndObjectIndex)
                .Select(item => item.index + 1)
                .ToArray();
            int[] contributingSections = sections
                .Where(section => section.ObjectIndices.Any(index =>
                    index >= match.StartObjectIndex && index <= match.EndObjectIndex))
                .Select(section => section.Index)
                .ToArray();
            int overlapCount = current.Matches.Count(other =>
                !ReferenceEquals(other, match) &&
                other.StartObjectIndex <= match.EndObjectIndex &&
                other.EndObjectIndex >= match.StartObjectIndex);

            report.AppendLine(
                $"#{occurrenceIndex + 1} {(containedOldMatches.Length > 0 ? "OLD-DERIVED" : "NEW")} | " +
                $"Time={match.StartTime}-{match.EndTime} | " +
                $"Objects={match.StartObjectIndex}-{match.EndObjectIndex} ({match.ObjectCount}) | " +
                $"Pivots={points.Length} | Blocks={alternatingBlockCount} | Resets={resetCount} | " +
                $"Alt={alternationScore.ToString("F4", CultureInfo.InvariantCulture)} | " +
                $"Confidence={match.Confidence.ToString("F6", CultureInfo.InvariantCulture)} | " +
                $"Intensity=N/A | DistConsistency={Consistency(distances):F4} | " +
                $"IntervalConsistency={Consistency(intervals):F4} | " +
                $"Sections=[{string.Join(',', contributingSections)}] | Overlaps={overlapCount} | " +
                $"ContainsOld=[{string.Join(',', containedOldMatches)}]");
            report.AppendLine($"  Distances=[{Format(distances)}]");
            report.AppendLine($"  Intervals=[{Format(intervals)}]");
            report.AppendLine($"  Angles=[{Format(points.Select(point => point.Angle))}]");
            report.AppendLine(
                $"  Rotations=[{string.Join(',', points.Select(point => point.TurnDirection))}] | " +
                $"Alternating=[{string.Join(',', points.Select(point => point.AlternatingWithPrevious))}]");
        }

        output.WriteLine(report.ToString());
        Assert.Equal(9, current.SequenceCount);
        Assert.Equal(4, oldMatches.Count);
    }

    private static IReadOnlyList<JumpPatternMatch> AnalyzeOneTwoUsingOldStrictAlternation(
        IReadOnlyList<HitObject> objects,
        GameplayProfile profile)
    {
        bool[] streamObjects = new bool[objects.Count];
        foreach (PatternSequence sequence in profile.StreamSequences)
        {
            for (int index = Math.Max(0, sequence.StartObjectIndex);
                 index <= Math.Min(objects.Count - 1, sequence.EndObjectIndex);
                 index++)
            {
                streamObjects[index] = true;
            }
        }

        var candidates = new List<ForensicCandidate>();
        for (int index = 1; index + 1 < objects.Count; index++)
        {
            HitObject previous = objects[index - 1];
            HitObject current = objects[index];
            HitObject next = objects[index + 1];
            if ((previous.Type & 1) == 0 || (current.Type & 1) == 0 || (next.Type & 1) == 0 ||
                streamObjects[index - 1] || streamObjects[index] || streamObjects[index + 1] ||
                !GameplayAnalyzer.IsJumpLink(previous, current, out double previousDistance) ||
                !GameplayAnalyzer.IsJumpLink(current, next, out double nextDistance) ||
                previousDistance < MinimumOneTwoJumpDistance ||
                nextDistance < MinimumOneTwoJumpDistance)
            {
                continue;
            }

            double angle = GameplayAnalyzer.GetTurnAngle(previous, current, next);
            double signedTurn = SignedTurn(previous, current, next, previousDistance, nextDistance);
            OneTwoTurnDirection direction = Direction(angle, signedTurn);
            if (direction != OneTwoTurnDirection.Neutral)
                candidates.Add(new(index, direction, signedTurn));
        }

        var matches = new List<JumpPatternMatch>();
        int start = 0;
        while (start < candidates.Count)
        {
            int end = start;
            while (end + 1 < candidates.Count &&
                   candidates[end + 1].ObjectIndex == candidates[end].ObjectIndex + 1 &&
                   AreAlternating(candidates[end], candidates[end + 1]))
            {
                end++;
            }

            int pointCount = end - start + 1;
            if (pointCount >= 3)
            {
                ForensicCandidate[] sequence = candidates.Skip(start).Take(pointCount).ToArray();
                int firstObjectIndex = sequence[0].ObjectIndex - 1;
                int lastObjectIndex = sequence[^1].ObjectIndex + 1;
                double[] distances = Enumerable.Range(firstObjectIndex, lastObjectIndex - firstObjectIndex)
                    .Select(index => Distance(objects[index], objects[index + 1]))
                    .ToArray();
                double[] intervals = Enumerable.Range(firstObjectIndex, lastObjectIndex - firstObjectIndex)
                    .Select(index => (double)(objects[index + 1].Time - objects[index].Time))
                    .ToArray();
                if (Consistency(distances) >= MinimumOneTwoDistanceConsistency &&
                    Consistency(intervals) >= MinimumOneTwoIntervalConsistency)
                {
                    matches.Add(new(
                        JumpPatternType.OneTwo,
                        firstObjectIndex,
                        lastObjectIndex,
                        objects[firstObjectIndex].Time,
                        objects[lastObjectIndex].Time,
                        0));
                }
            }

            start = end + 1;
        }

        return matches;
    }

    private static OneTwoTurnDirection Direction(double angle, double signedTurn)
    {
        if (angle >= NearReversalTurnDegrees)
            return OneTwoTurnDirection.Reversal;
        if (angle < MinimumOneTwoTurnDegrees)
            return OneTwoTurnDirection.Neutral;
        return signedTurn > 0 ? OneTwoTurnDirection.Left : OneTwoTurnDirection.Right;
    }

    private static double SignedTurn(
        HitObject previous,
        HitObject current,
        HitObject next,
        double previousDistance,
        double nextDistance)
    {
        double previousX = current.X - previous.X;
        double previousY = current.Y - previous.Y;
        double nextX = next.X - current.X;
        double nextY = next.Y - current.Y;
        double sine = (previousX * nextY - previousY * nextX) /
                      (previousDistance * nextDistance);
        return Math.Abs(sine) < MinimumStableReversalTurnSine ? 0 : Math.Sign(sine);
    }

    private static bool AreAlternating(ForensicCandidate left, ForensicCandidate right)
    {
        if (left.Direction == OneTwoTurnDirection.Reversal &&
            right.Direction == OneTwoTurnDirection.Reversal &&
            left.TurnSign == 0 && right.TurnSign == 0)
        {
            return true;
        }

        return left.TurnSign != 0 && right.TurnSign != 0 && left.TurnSign != right.TurnSign;
    }

    private static double Distance(HitObject left, HitObject right)
    {
        double deltaX = right.X - left.X;
        double deltaY = right.Y - left.Y;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    private static double Consistency(IEnumerable<double> values)
    {
        double[] sample = values.Where(value => double.IsFinite(value) && value > 0).ToArray();
        return sample.Length == 0 ? 0 : Math.Clamp(sample.Min() / sample.Max(), 0, 1);
    }

    private static string Format(IEnumerable<double> values) =>
        string.Join(',', values.Select(value => value.ToString("F1", CultureInfo.InvariantCulture)));

    private sealed record ForensicCandidate(
        int ObjectIndex,
        OneTwoTurnDirection Direction,
        double TurnSign);

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
        report.AppendLine(
            $"TOTAL OneTwo={sections.Sum(section => section.JumpPatterns.OneTwo.SequenceCount)}; " +
            $"Sharp={sections.Sum(section => section.JumpPatterns.Sharp.SequenceCount)}; " +
            $"Spaced={sections.Sum(section => section.JumpPatterns.Spaced.SequenceCount)}; " +
            $"Triangle={sections.Sum(section => section.JumpPatterns.Triangle.SequenceCount)}; " +
            $"Edge={sections.Sum(section => section.JumpPatterns.Edge.SequenceCount)}");
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
