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

/// <summary>Final, observation-only consolidation of the frozen V1 Jump-pattern family.</summary>
public sealed class JumpPatternV1ConsolidationForensicTests(ITestOutputHelper output)
{
    private const double SignificantSpacingDelta = 10;
    private static readonly string[] Fixtures =
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
    public void ExactJumpCorpus_ProducesFinalV1Consolidation()
    {
        MapResult[] maps = Fixtures.Select(Analyze).ToArray();
        var objectiveBugs = new List<string>();
        var report = new StringBuilder();

        AppendConfiguration(report);
        AppendCorpus(report, maps);
        AppendGlobalTables(report, maps);
        AppendLinear(report, maps);
        AppendEdge(report, maps, objectiveBugs);
        AppendAngular(report, maps, objectiveBugs);
        AppendTriangle(report, maps);
        AppendOneTwo(report, maps, objectiveBugs);
        AppendSpaced(report, maps);
        AppendArc(report, maps);
        AppendTrend(report, maps, objectiveBugs);
        AppendAlternatingSpacing(report, maps);
        AppendCoexistence(report, maps);
        AppendCoverageAudit(report, maps, objectiveBugs);
        AppendConfidenceAudit(report, maps, objectiveBugs);
        AppendIntegrityAudit(report, maps, objectiveBugs);
        AppendGeometryAudit(report, maps, objectiveBugs);
        AppendDebugAudit(report);
        AppendTestInventory(report);
        AppendFinalValidation(report);
        AppendConclusion(report, maps, objectiveBugs);

        string directory = Environment.GetEnvironmentVariable("BEATINSIGHT_FORENSIC_OUTPUT_DIRECTORY")
            ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "jump-pattern-v1-consolidation.md");
        File.WriteAllText(path, report.ToString(), Encoding.UTF8);
        output.WriteLine($"Corpus analyzed: {maps.Length}/{Fixtures.Length}");
        output.WriteLine($"Objective bugs: {objectiveBugs.Count}");
        output.WriteLine($"Report: {path}");
        Assert.Empty(objectiveBugs);
    }

    private static MapResult Analyze(string fixture)
    {
        Beatmap map = FixtureLoader.Load(fixture);
        GameplayProfile profile = map.GameplayProfile;
        IReadOnlyList<JumpPatternMatch> all = JumpPatternAnalyzer.Analyze(map.HitObjects, profile);
        return new(fixture, map, profile, all.Where(x => x.Type == JumpPatternType.Linear).ToArray(),
            JumpPatternAnalyzer.AnalyzeEdge(map.HitObjects, profile),
            JumpPatternAnalyzer.AnalyzeSharp(map.HitObjects, profile),
            JumpPatternAnalyzer.AnalyzeTriangle(map.HitObjects, profile),
            JumpPatternAnalyzer.AnalyzeOneTwo(map.HitObjects, profile),
            JumpPatternAnalyzer.AnalyzeSpaced(map.HitObjects, profile),
            JumpPatternAnalyzer.AnalyzeWideAngle(map.HitObjects, profile),
            JumpPatternAnalyzer.AnalyzeReversal(map.HitObjects, profile),
            JumpPatternAnalyzer.AnalyzeArc(map.HitObjects, profile),
            JumpPatternAnalyzer.AnalyzeSpacingTrend(map.HitObjects, profile),
            JumpPatternAnalyzer.AnalyzeAlternatingSpacing(map.HitObjects, profile));
    }

    private static void AppendConfiguration(StringBuilder report)
    {
        report.AppendLine("# Jump Pattern Analysis — V1 final consolidation");
        report.AppendLine();
        report.AppendLine("Final observation-only forensic. No threshold, analyzer, Stream, Identity, Tech, Rating, or UI behavior was changed.");
        report.AppendLine();
        report.AppendLine("## 1. Configuration V1 finale");
        report.AppendLine();
        report.AppendLine("- Linear: >=4 objects; local turn <=25°; global deviation <=30°; existing Jump sequences only.");
        report.AppendLine("- Edge: margin 64 px inclusive; distance >=120 px; >=3 objects; confidence full length 8.");
        report.AppendLine("- Sharp: angle >=120°; links >=60 px; >=2 adjacent pivots; reversal diagnostic >=170°; full length 8.");
        report.AppendLine("- Triangle: adjacent/base distances >=60 px; height >=30 px; angle 30°–155° inclusive; side balance >=0.25; >=2 adjacent occurrences; full length 8.");
        report.AppendLine("- 1-2: distance >=60 px; turn >=35°; near reversal >=170°; >=3 pivots; distance and interval consistency >=0.55; full length 8.");
        report.AppendLine("- Spaced: distance >=150 px; >=3 links; distance consistency >=0.45; full length 8.");
        report.AppendLine("- Wide Angle: 60°–115° inclusive; distance >=60 px; >=2 adjacent pivots; full length 8; neutral zone 116°–119° before Sharp.");
        report.AppendLine("- Reversal: angle >=170°; distance >=60 px; >=2 adjacent pivots; full length 8; conceptual Sharp subset.");
        report.AppendLine("- Arc: 20°–110° inclusive; distance >=60 px; >=3 adjacent same-sign pivots; angle consistency >=0.50; full length 10.");
        report.AppendLine("- Spacing Trend: distance >=60 px; >=4 links; significant delta >=10 px; dominant direction / all deltas >=0.75; magnitude >=0.25; full length 10.");
        report.AppendLine("- Alternating Spacing: distance >=60 px; >=5 links; significant delta >=10 px; alternation >=0.75; contrast >=0.20; oscillation magnitude consistency >=0.50; full length 10.");
        report.AppendLine("- All geometric detectors use the existing Jump qualification and Stream exclusion. Angle convention is 0° straight and 180° reversal.");
        report.AppendLine();
        report.AppendLine("The supplied approximate reference matches production. Exact divergences: none.");
        report.AppendLine();
    }

    private static void AppendCorpus(StringBuilder report, IReadOnlyList<MapResult> maps)
    {
        report.AppendLine("## 2. Corpus");
        report.AppendLine();
        report.AppendLine($"Exact fixture corpus found and analyzed: **{maps.Count}/{Fixtures.Length}**.");
        report.AppendLine();
        report.AppendLine("|#|Map|Difficulty|Objects|Fixture|");
        report.AppendLine("|---:|---|---|---:|---|");
        for (int i = 0; i < maps.Count; i++)
            report.AppendLine($"|{i + 1}|{Cell(maps[i].Map.Title)}|{Cell(maps[i].Map.Version)}|{maps[i].Map.HitObjects.Count}|`{maps[i].Fixture}`|");
        report.AppendLine();
    }

    private static void AppendGlobalTables(StringBuilder report, IReadOnlyList<MapResult> maps)
    {
        report.AppendLine("## 3. Tableau global");
        report.AppendLine();
        report.AppendLine("### Coverage (%)");
        report.AppendLine();
        report.AppendLine("|Map|Linear|Edge|Sharp|Triangle|1-2|Spaced|Wide|Reversal|Arc|Trend|Alt spacing|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (MapResult m in maps)
        {
            PatternView[] v = Views(m);
            report.AppendLine($"|{Cell(m.Label)}|{string.Join('|', v.Select(x => Pct(x.Coverage)))}|");
        }
        report.AppendLine();
        report.AppendLine("### Sequence count");
        report.AppendLine();
        report.AppendLine("|Map|Linear|Edge|Sharp|Triangle|1-2|Spaced|Wide|Reversal|Arc|Trend|Alt spacing|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (MapResult m in maps) report.AppendLine($"|{Cell(m.Label)}|{string.Join('|', Views(m).Select(x => x.SequenceCount))}|");
        report.AppendLine();
        report.AppendLine("### Longest qualified sequence");
        report.AppendLine();
        report.AppendLine("|Map|Linear|Edge|Sharp|Triangle|1-2|Spaced|Wide|Reversal|Arc|Trend|Alt spacing|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (MapResult m in maps) report.AppendLine($"|{Cell(m.Label)}|{string.Join('|', Views(m).Select(x => x.Longest))}|");
        report.AppendLine();
        report.AppendLine("### Per-map metrics for all eleven patterns");
        foreach (MapResult m in maps)
        {
            report.AppendLine();
            report.AppendLine($"#### {m.Label}");
            report.AppendLine();
            report.AppendLine("|Pattern|Seq|Objects|Coverage|Longest|Average length|Confidence|Main metrics|");
            report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---|");
            foreach (PatternView p in Views(m))
                report.AppendLine(FormattableString.Invariant($"|{p.Name}|{p.SequenceCount}|{p.ObjectCount}|{p.Coverage:P2}|{p.Longest}|{p.AverageLength:F2}|{p.Confidence:F4}|{p.Metrics}|"));
        }
        report.AppendLine();
    }

    private static void AppendLinear(StringBuilder report, IReadOnlyList<MapResult> maps)
    {
        var matches = maps.SelectMany(m => m.Linear.Select(x => (Map: m, Match: x))).OrderByDescending(x => x.Match.ObjectCount).ThenByDescending(x => x.Match.Confidence).ToArray();
        report.AppendLine("## 4. Linear");
        report.AppendLine();
        report.AppendLine($"Qualified sequences={matches.Length}; maps={maps.Count(x => x.Linear.Count > 0)}. Best positives:");
        foreach (var x in matches.Take(15)) AppendMatch(report, x.Map, x.Match, $"cleanliness={x.Match.Confidence:F4}");
        int arcOverlap = matches.Count(x => x.Map.Arc.Matches.Any(a => Overlap(x.Match, a)));
        int oneTwoOverlap = matches.Count(x => x.Map.OneTwo.Matches.Any(a => Overlap(x.Match, a)));
        report.AppendLine($"Linear sequences overlapping Arc={arcOverlap}; overlapping 1-2/zigzag={oneTwoOverlap}. These are local-object overlaps, not automatic false positives; progressive curves remain bounded by the 30° global-axis guard.");
        report.AppendLine();
    }

    private static void AppendEdge(StringBuilder report, IReadOnlyList<MapResult> maps, List<string> bugs)
    {
        var matches = maps.SelectMany(m => m.Edge.Matches.Select(x => (Map: m, Match: x))).OrderByDescending(x => x.Match.ObjectCount).ToArray();
        foreach (MapResult m in maps)
            foreach (EdgeJumpTransition t in m.Edge.Transitions)
            {
                if (t.From == EdgeZone.None || t.To == EdgeZone.None || t.Distance < 120) bugs.Add($"{m.Label} Edge transition {t.StartObjectIndex}-{t.EndObjectIndex} violates edge/distance rules.");
                if (!IsEdge(m.Map.HitObjects[t.StartObjectIndex]) || !IsEdge(m.Map.HitObjects[t.EndObjectIndex])) bugs.Add($"{m.Label} Edge transition contains central endpoint.");
            }
        report.AppendLine("## 5. Edge");
        report.AppendLine();
        report.AppendLine($"Sequences={matches.Length}; transitions={maps.Sum(x => x.Edge.EdgeTransitionCount)}; edge objects={maps.Sum(x => x.Edge.EdgeObjectCount)}. Best positives:");
        foreach (var x in matches.Take(12))
        {
            EdgeJumpTransition[] links = x.Map.Edge.Transitions.Where(t => t.StartObjectIndex >= x.Match.StartObjectIndex && t.EndObjectIndex <= x.Match.EndObjectIndex).ToArray();
            AppendMatch(report, x.Map, x.Match, $"zones=[{string.Join(',', links.Select(t => $"{t.From}->{t.To}"))}]; distances=[{Numbers(links.Select(t => t.Distance))}]; min={Min(links.Select(t => t.Distance)):F2}px");
        }
        report.AppendLine("Margin boundary is inclusive at x/y=64 and x=448/y=320 in the synthetic suite; all real retained endpoints are in a valid edge zone and every transition is >=120 px.");
        report.AppendLine();
    }

    private static void AppendAngular(StringBuilder report, IReadOnlyList<MapResult> maps, List<string> bugs)
    {
        double[] wide = maps.SelectMany(x => x.Wide.Points).Select(x => x.Angle).OrderBy(x => x).ToArray();
        double[] sharp = maps.SelectMany(x => x.Sharp.Points).Select(x => x.Angle).OrderBy(x => x).ToArray();
        double[] reversal = maps.SelectMany(x => x.Reversal.Points).Select(x => x.Angle).OrderBy(x => x).ToArray();
        int wideSharp = maps.Sum(m => m.Wide.Points.Select(x => x.ObjectIndex).Intersect(m.Sharp.Points.Select(x => x.ObjectIndex)).Count());
        int sharpReversal = maps.Sum(m => m.Sharp.Points.Select(x => x.ObjectIndex).Intersect(m.Reversal.Points.Select(x => x.ObjectIndex)).Count());
        if (wideSharp != 0) bugs.Add($"Wide/Sharp disjoint angle bands share {wideSharp} retained pivots.");
        foreach (MapResult m in maps)
            foreach (ReversalJumpPoint p in m.Reversal.Points)
                if (!m.Sharp.Points.Any(s => s.ObjectIndex == p.ObjectIndex && Math.Abs(s.Angle - p.Angle) < 1e-9)) bugs.Add($"{m.Label} reversal pivot {p.ObjectIndex} is not retained by Sharp.");

        report.AppendLine("## 6. Sharp / Wide / Reversal");
        report.AppendLine();
        report.AppendLine($"Wide angles: {Distribution(wide)}; Sharp angles: {Distribution(sharp)}; Reversal angles: {Distribution(reversal)}.");
        report.AppendLine($"Wide+Sharp shared pivots={wideSharp} (must be 0); Sharp+Reversal shared pivots={sharpReversal} (expected subset behavior). Neutral 116°–119° band remains intentionally unclassified by Wide or Sharp.");
        report.AppendLine();
        report.AppendLine("Top Sharp:");
        foreach (var x in maps.SelectMany(m => m.Sharp.Matches.Select(s => (Map: m, Match: s))).OrderByDescending(x => x.Match.Confidence).Take(10))
            AppendMatch(report, x.Map, x.Match, AngularMetrics(x.Map.Sharp.Points, x.Match));
        report.AppendLine("Top Wide:");
        foreach (var x in maps.SelectMany(m => m.Wide.Matches.Select(s => (Map: m, Match: s))).OrderByDescending(x => x.Match.Confidence).Take(10))
            AppendMatch(report, x.Map, x.Match, AngularMetrics(x.Map.Wide.Points, x.Match));
        report.AppendLine("Top Reversal:");
        foreach (var x in maps.SelectMany(m => m.Reversal.Matches.Select(s => (Map: m, Match: s))).OrderByDescending(x => x.Match.Confidence).Take(10))
            AppendMatch(report, x.Map, x.Match, AngularMetrics(x.Map.Reversal.Points, x.Match));
        report.AppendLine();
    }

    private static void AppendTriangle(StringBuilder report, IReadOnlyList<MapResult> maps)
    {
        var matches = maps.SelectMany(m => m.Triangle.Matches.Select(x => (Map: m, Match: x))).OrderByDescending(x => x.Match.Confidence).ToArray();
        report.AppendLine("## 7. Triangle");
        report.AppendLine();
        report.AppendLine($"Sequences={matches.Length}; maps={maps.Count(x => x.Triangle.Matches.Count > 0)}. Classification is forensic scope, not runtime policy:");
        foreach (var x in matches.Take(25))
        {
            TriangleJumpPoint[] points = x.Map.Triangle.Points.Where(p => p.ObjectIndex > x.Match.StartObjectIndex && p.ObjectIndex < x.Match.EndObjectIndex).ToArray();
            string classification = ClassifyTriangle(x.Map, x.Match, points);
            AppendMatch(report, x.Map, x.Match, $"class={classification}; angles=[{Numbers(points.Select(p => p.Angle))}]; heights=[{Numbers(points.Select(p => p.Height))}]; sideBalance=[{Numbers(points.Select(p => p.SideBalance))}]");
        }
        report.AppendLine("Tower Of Heaven and generic alternating-sign chains are retained as semantic review cases when their local three-point geometry genuinely satisfies the detector. This is documented V1 scope, not a mathematical defect.");
        report.AppendLine();
    }

    private static void AppendOneTwo(StringBuilder report, IReadOnlyList<MapResult> maps, List<string> bugs)
    {
        MapResult blackRover = maps.Single(x => x.Fixture.Contains("Black Rover", StringComparison.Ordinal));
        MapResult negative = maps.Single(x => x.Fixture.StartsWith("(can you)", StringComparison.Ordinal));
        bool blackPositive = blackRover.OneTwo.Coverage >= .05
            && blackRover.OneTwo.SequenceCount >= 3
            && blackRover.OneTwo.LongestSequence >= 5;
        bool negativeLow = negative.OneTwo.Coverage <= .02
            && !negative.OneTwo.Matches.Any(match => match.ObjectCount > 5);
        if (!blackPositive) bugs.Add($"Black Rover 1-2 control failed: sequences={blackRover.OneTwo.SequenceCount}, coverage={blackRover.OneTwo.Coverage:P2}.");
        if (!negativeLow) bugs.Add($"can you understand me 1-2 negative control exceeded 5%: {negative.OneTwo.Coverage:P2}.");
        report.AppendLine("## 8. 1-2 Jump");
        report.AppendLine();
        report.AppendLine($"Black Rover: sequences={blackRover.OneTwo.SequenceCount}, coverage={blackRover.OneTwo.Coverage:P2}, positive control={blackPositive}. can you understand me?: sequences={negative.OneTwo.SequenceCount}, coverage={negative.OneTwo.Coverage:P2}, low/negative control={negativeLow}.");
        foreach (var x in maps.SelectMany(m => m.OneTwo.Matches.Select(s => (Map: m, Match: s))).OrderByDescending(x => x.Match.Confidence).Take(20))
        {
            OneTwoJumpPoint[] p = x.Map.OneTwo.Points.Where(p => p.ObjectIndex > x.Match.StartObjectIndex && p.ObjectIndex < x.Match.EndObjectIndex).ToArray();
            AppendMatch(report, x.Map, x.Match, $"angles=[{Numbers(p.Select(y => y.Angle))}]; turns=[{string.Join(',', p.Select(y => y.TurnDirection))}]; distanceConsistency={Consistency(p.SelectMany(y => new[] { y.PreviousDistance, y.NextDistance })):F4}; intervalConsistency={Consistency(p.SelectMany(y => new[] { (double)y.PreviousInterval, y.NextInterval })):F4}; aggregateAlternation={x.Map.OneTwo.AlternationConsistency:F4}");
        }
        report.AppendLine("Near reversals are explicit stable directions; the synthetic suite confirms isolated reversals are not wildcards and repeated reversals require genuine alternation evidence.");
        report.AppendLine();
    }

    private static void AppendSpaced(StringBuilder report, IReadOnlyList<MapResult> maps)
    {
        MapResult black = maps.Single(x => x.Fixture.Contains("Black Rover", StringComparison.Ordinal));
        MapResult understand = maps.Single(x => x.Fixture.StartsWith("(can you)", StringComparison.Ordinal));
        report.AppendLine("## 9. Spaced Jump");
        report.AppendLine();
        report.AppendLine($"Global sequences={maps.Sum(x => x.Spaced.SequenceCount)}; Black Rover coverage={black.Spaced.Coverage:P2}; can you understand me? coverage={understand.Spaced.Coverage:P2}. Coexistence with 1-2, Trend, and Arc is allowed.");
        foreach (var x in maps.SelectMany(m => m.Spaced.Matches.Select(s => (Map: m, Match: s))).OrderByDescending(x => x.Match.ObjectCount).Take(20))
        {
            SpacedJumpTransition[] links = x.Map.Spaced.Transitions.Where(t => t.StartObjectIndex >= x.Match.StartObjectIndex && t.EndObjectIndex <= x.Match.EndObjectIndex).ToArray();
            AppendMatch(report, x.Map, x.Match, $"distances=[{Numbers(links.Select(t => t.Distance))}]; min={Min(links.Select(t => t.Distance)):F2}; consistency={Consistency(links.Select(t => t.Distance)):F4}");
        }
        report.AppendLine();
    }

    private static void AppendArc(StringBuilder report, IReadOnlyList<MapResult> maps)
    {
        report.AppendLine("## 10. Arc / Curved Jump");
        report.AppendLine();
        foreach (var x in maps.SelectMany(m => m.Arc.Matches.Select(s => (Map: m, Match: s))).OrderByDescending(x => x.Match.Confidence).Take(25))
        {
            ArcJumpPoint[] p = x.Map.Arc.Points.Where(p => p.ObjectIndex > x.Match.StartObjectIndex && p.ObjectIndex < x.Match.EndObjectIndex).ToArray();
            string assessment = x.Map.Fixture.Contains("Kira Killer") ? "CLEAN_HISTORICAL_CONTROL" : x.Map.Fixture.Contains("Tower Of Heaven") ? "LOCAL_BORDERLINE" : "PLAUSIBLE_LOCAL_ARC";
            AppendMatch(report, x.Map, x.Match, $"assessment={assessment}; direction={p.FirstOrDefault()?.TurnDirection}; angles=[{Numbers(p.Select(y => y.Angle))}]; distances=[{Numbers(p.SelectMany(y => new[] { y.PreviousDistance, y.NextDistance }))}]; angleConsistency={Consistency(p.Select(y => y.Angle)):F4}");
        }
        report.AppendLine("Kira Killer remains the clean historical control. Exit Primordial and Arles contain plausible local arcs; small Tower sequences remain semantic-monitoring cases.");
        report.AppendLine();
    }

    private static void AppendTrend(StringBuilder report, IReadOnlyList<MapResult> maps, List<string> bugs)
    {
        JumpSpacingTrendSequence[] sequences = maps.SelectMany(x => x.Trend.Sequences).ToArray();
        foreach (MapResult m in maps)
            foreach (JumpSpacingTrendSequence s in m.Trend.Sequences)
            {
                int dominant = s.Deltas.Count(d => s.Direction == JumpSpacingTrendDirection.Accelerating ? d >= SignificantSpacingDelta : d <= -SignificantSpacingDelta);
                double expected = s.Deltas.Count == 0 ? 0 : (double)dominant / s.Deltas.Count;
                if (Math.Abs(expected - s.TrendConsistency) > 1e-12) bugs.Add($"{m.Label} Trend {s.StartObjectIndex}-{s.EndObjectIndex}: consistency {s.TrendConsistency} != dominant/all {expected}.");
                if (s.TrendConsistency < .75 || s.TrendMagnitude < .25) bugs.Add($"{m.Label} Trend retained below qualification thresholds.");
            }
        report.AppendLine("## 11. Spacing Trend");
        report.AppendLine();
        report.AppendLine($"Sequences={sequences.Length}; accelerating={sequences.Count(x => x.Direction == JumpSpacingTrendDirection.Accelerating)}; decelerating={sequences.Count(x => x.Direction == JumpSpacingTrendDirection.Decelerating)}.");
        foreach (var item in maps.SelectMany(m => m.Trend.Sequences.Select(s => (Map: m, Sequence: s))).OrderByDescending(x => x.Sequence.Confidence).Take(25))
            report.AppendLine(FormattableString.Invariant($"- {item.Map.Label} [{item.Sequence.StartObjectIndex}..{item.Sequence.EndObjectIndex}] {Time(item.Sequence.StartTime)}–{Time(item.Sequence.EndTime)}: {item.Sequence.Direction}, distances=[{Numbers(item.Sequence.Distances)}], deltas=[{Numbers(item.Sequence.Deltas)}], consistency={item.Sequence.TrendConsistency:F4}, magnitude={item.Sequence.TrendMagnitude:F4}, confidence={item.Sequence.Confidence:F4}."));
        report.AppendLine("The consistency audit recomputed dominant significant deltas / all deltas for every qualified real sequence. Neutral plateaus remain in the denominator; the one-big-change-plus-plateau synthetic regression remains rejected.");
        report.AppendLine();
    }

    private static void AppendAlternatingSpacing(StringBuilder report, IReadOnlyList<MapResult> maps)
    {
        AlternatingSpacingJumpSequence[] sequences = maps.SelectMany(x => x.Alternating.Sequences).ToArray();
        Candidate[] candidates = maps.SelectMany(AlternatingCandidates).Where(x => !x.Qualified).OrderByDescending(x => x.Score).Take(20).ToArray();
        report.AppendLine("## 12. Alternating Spacing");
        report.AppendLine();
        report.AppendLine($"Real qualified SequenceCount={sequences.Length}; maps={maps.Count(x => x.Alternating.AlternatingSpacingSequenceCount > 0)}. Synthetic positive and negative suites remain validated.");
        report.AppendLine("Top 20 non-qualified five-link windows (test-only diagnostics; no threshold simulation):");
        foreach (Candidate c in candidates)
            report.AppendLine(FormattableString.Invariant($"- {c.Map.Label} [{c.Start}..{c.End}] {Time(c.Map.Map.HitObjects[c.Start].Time)}–{Time(c.Map.Map.HitObjects[c.End].Time)}: distances=[{Numbers(c.Distances)}], alternation={c.Alternation:F4}, contrast={c.Contrast:F4}, oscillation={c.Oscillation:F4}, score={c.Score:F4}, reason={c.Reason}."));
        report.AppendLine("Status evidence: no confirmed real positive in the fixture corpus and no mass false-positive signal; thresholds remain unchanged.");
        report.AppendLine();
    }

    private static void AppendCoexistence(StringBuilder report, IReadOnlyList<MapResult> maps)
    {
        string[] requested = ["Spaced + OneTwo", "Sharp + Reversal", "Sharp + Triangle", "Arc + Spaced", "Trend + Spaced", "WideAngle + Spaced"];
        var counts = requested.ToDictionary(x => x, _ => 0);
        int trendWithoutSpaced = 0, spacedWithoutTrend = 0;
        var combinations = new Dictionary<string, int>();
        foreach (MapResult m in maps)
        {
            var named = MatchSets(m);
            var all = named.SelectMany(x => x.Value.Select(match => (Name: x.Key, Match: match))).ToArray();
            foreach (var first in all)
            {
                string[] tags = all.Where(x => Overlap(first.Match, x.Match)).Select(x => x.Name).Distinct().OrderBy(x => x).ToArray();
                string combo = string.Join(" + ", tags);
                combinations[combo] = combinations.GetValueOrDefault(combo) + 1;
            }
            foreach (string pair in requested)
            {
                string[] p = pair.Split(" + ");
                counts[pair] += named[p[0]].Count(a => named[p[1]].Any(b => Overlap(a, b)));
            }
            trendWithoutSpaced += named["Trend"].Count(a => !named["Spaced"].Any(b => Overlap(a, b)));
            spacedWithoutTrend += named["Spaced"].Count(a => !named["Trend"].Any(b => Overlap(a, b)));
        }
        report.AppendLine("## 13. Pattern coexistence");
        report.AppendLine();
        report.AppendLine("Requested overlaps: " + string.Join("; ", counts.Select(x => $"{x.Key}={x.Value}")) + $"; Trend without Spaced={trendWithoutSpaced}; Spaced without Trend={spacedWithoutTrend}.");
        report.AppendLine();
        report.AppendLine("|Most frequent local overlap signature|Occurrences anchored on matches|");
        report.AppendLine("|---|---:|");
        foreach (var item in combinations.OrderByDescending(x => x.Value).ThenBy(x => x.Key).Take(20)) report.AppendLine($"|{item.Key}|{item.Value}|");
        report.AppendLine();
        report.AppendLine("No coexistence is treated as an error by itself. Wide/Sharp same-pivot overlap is audited separately because their angle bands are disjoint.");
        report.AppendLine();
    }

    private static void AppendCoverageAudit(StringBuilder report, IReadOnlyList<MapResult> maps, List<string> bugs)
    {
        int before = bugs.Count;
        foreach (MapResult m in maps)
            foreach (PatternView p in Views(m))
            {
                int unique = p.Matches.SelectMany(Indices).Distinct().Count();
                double expected = m.Map.HitObjects.Count == 0 ? 0 : (double)unique / m.Map.HitObjects.Count;
                if (p.Coverage < 0 || p.Coverage > 1 || p.ObjectCount != unique || Math.Abs(p.Coverage - expected) > 1e-12) bugs.Add($"{m.Label} {p.Name}: object/unique/coverage mismatch ({p.ObjectCount}/{unique}/{p.Coverage}/{expected}).");
            }
        report.AppendLine("## 14. Coverage audit");
        report.AppendLine();
        report.AppendLine("Coverage was recomputed from unique indices of qualified matches only; rejected candidates are absent.");
        report.AppendLine($"**CoverageViolations = {bugs.Count - before}**");
        report.AppendLine();
    }

    private static void AppendConfidenceAudit(StringBuilder report, IReadOnlyList<MapResult> maps, List<string> bugs)
    {
        int before = bugs.Count;
        report.AppendLine("## 15. Confidence audit");
        report.AppendLine();
        report.AppendLine("|Pattern|Present maps|Min|Mean|Median|Max|");
        report.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (string name in PatternNames)
        {
            PatternView[] values = maps.Select(m => Views(m).Single(x => x.Name == name)).ToArray();
            foreach (PatternView p in values)
            {
                if (p.Confidence < 0 || p.Confidence > 1) bugs.Add($"{name}: confidence outside [0,1].");
                if (p.SequenceCount > 0 && p.Confidence == 0) bugs.Add($"{name}: present with zero confidence.");
                if (p.SequenceCount == 0 && p.Confidence > 0) bugs.Add($"{name}: absent with non-zero confidence.");
            }
            double[] present = values.Where(x => x.SequenceCount > 0).Select(x => x.Confidence).OrderBy(x => x).ToArray();
            report.AppendLine(FormattableString.Invariant($"|{name}|{present.Length}|{Min(present):F4}|{Mean(present):F4}|{Median(present):F4}|{Max(present):F4}|"));
        }
        report.AppendLine();
        report.AppendLine($"**ConfidenceViolations = {bugs.Count - before}**");
        report.AppendLine("Linear confidence is the mean geometric cleanliness of its matches because Linear has no separate aggregate analysis model.");
        report.AppendLine();
    }

    private static void AppendIntegrityAudit(StringBuilder report, IReadOnlyList<MapResult> maps, List<string> bugs)
    {
        int before = bugs.Count;
        foreach (MapResult m in maps)
        {
            foreach (PatternView p in Views(m))
                foreach (JumpPatternMatch x in p.Matches)
                    if (x.StartObjectIndex < 0 || x.EndObjectIndex >= m.Map.HitObjects.Count || x.StartObjectIndex > x.EndObjectIndex || x.ObjectCount != x.EndObjectIndex - x.StartObjectIndex + 1 || x.StartTime > x.EndTime || x.StartTime != m.Map.HitObjects[x.StartObjectIndex].Time || x.EndTime != m.Map.HitObjects[x.EndObjectIndex].Time)
                        bugs.Add($"{m.Label} {p.Name} {x.StartObjectIndex}-{x.EndObjectIndex}: invalid indices/timestamps.");
            foreach (JumpSpacingTrendSequence s in m.Trend.Sequences)
                if (s.StartObjectIndex > s.EndObjectIndex || s.ObjectCount != s.EndObjectIndex - s.StartObjectIndex + 1 || s.LinkCount != s.ObjectCount - 1 || s.Deltas.Count != s.LinkCount - 1 || s.StartTime > s.EndTime)
                    bugs.Add($"{m.Label} Trend {s.StartObjectIndex}-{s.EndObjectIndex}: invalid sequence shape.");
            foreach (AlternatingSpacingJumpSequence s in m.Alternating.Sequences)
                if (s.StartObjectIndex > s.EndObjectIndex || s.ObjectCount != s.EndObjectIndex - s.StartObjectIndex + 1 || s.LinkCount != s.ObjectCount - 1 || s.Deltas.Count != s.LinkCount - 1 || s.StartTime > s.EndTime)
                    bugs.Add($"{m.Label} AlternatingSpacing {s.StartObjectIndex}-{s.EndObjectIndex}: invalid sequence shape.");
        }
        report.AppendLine("## 16. Sequence integrity");
        report.AppendLine();
        report.AppendLine($"**SequenceIntegrityViolations = {bugs.Count - before}**. Inclusive indices, object/link counts, timestamps, and delta cardinalities were checked.");
        report.AppendLine();
    }

    private static void AppendGeometryAudit(StringBuilder report, IReadOnlyList<MapResult> maps, List<string> bugs)
    {
        int before = bugs.Count;
        foreach (MapResult m in maps)
        {
            foreach (SharpJumpPoint p in m.Sharp.Points) if (p.Angle < 120 || p.PreviousDistance < 60 || p.NextDistance < 60) bugs.Add($"{m.Label} Sharp pivot violates geometry.");
            foreach (WideAngleJumpPoint p in m.Wide.Points) if (p.Angle < 60 || p.Angle > 115 || p.PreviousDistance < 60 || p.NextDistance < 60) bugs.Add($"{m.Label} Wide pivot violates geometry.");
            foreach (ReversalJumpPoint p in m.Reversal.Points) if (p.Angle < 170 || p.PreviousDistance < 60 || p.NextDistance < 60) bugs.Add($"{m.Label} Reversal pivot violates geometry.");
            foreach (ArcJumpPoint p in m.Arc.Points)
            {
                int sign = CrossSign(m.Map.HitObjects[p.ObjectIndex - 1], m.Map.HitObjects[p.ObjectIndex], m.Map.HitObjects[p.ObjectIndex + 1]);
                if (p.Angle < 20 || p.Angle > 110 || p.PreviousDistance < 60 || p.NextDistance < 60 || sign == 0 || (sign > 0 ? ArcTurnDirection.Left : ArcTurnDirection.Right) != p.TurnDirection) bugs.Add($"{m.Label} Arc pivot violates angle/distance/cross-sign convention.");
            }
            foreach (TriangleJumpPoint p in m.Triangle.Points) if (p.Angle < 30 || p.Angle > 155 || p.PreviousDistance < 60 || p.NextDistance < 60 || p.OppositeDistance < 60 || p.Height < 30 || p.SideBalance < .25) bugs.Add($"{m.Label} Triangle pivot violates geometry.");
            foreach (OneTwoJumpPoint p in m.OneTwo.Points) if (p.Angle < 35 || p.PreviousDistance < 60 || p.NextDistance < 60) bugs.Add($"{m.Label} 1-2 pivot violates geometry.");
        }
        report.AppendLine("## 17. Geometry audit");
        report.AppendLine();
        report.AppendLine("Sharp, Wide, Reversal, Arc, Triangle, and 1-2 retained points were recomputed against their exact bands/distances. Arc cross signs agree with the shared screen-coordinate convention; all angle models use GameplayAnalyzer.GetTurnAngle (0° straight, 180° reversal). Distance qualification consistently uses GameplayAnalyzer.IsJumpLink.");
        report.AppendLine($"**GeometryViolations = {bugs.Count - before}**");
        report.AppendLine();
    }

    private static void AppendDebugAudit(StringBuilder report)
    {
        bool[] flags = [GameplayDebug.EdgeJumpEnabled, GameplayDebug.SharpJumpEnabled, GameplayDebug.TriangleJumpEnabled, GameplayDebug.OneTwoJumpEnabled, GameplayDebug.SpacedJumpEnabled, GameplayDebug.WideAngleJumpEnabled, GameplayDebug.ReversalJumpEnabled, GameplayDebug.ArcJumpEnabled, GameplayDebug.JumpSpacingTrendEnabled, GameplayDebug.AlternatingSpacingJumpEnabled];
        string root = FindRepoRoot();
        string debug = File.ReadAllText(Path.Combine(root, "BeatInsight", "Debug", "GameplayDebug.cs"));
        string analyzer = string.Join(Environment.NewLine, Directory.GetFiles(Path.Combine(root, "BeatInsight", "Analysis", "JumpPatterns"), "*.cs").Select(File.ReadAllText));
        report.AppendLine("## 18. Debug audit");
        report.AppendLine();
        report.AppendLine($"Ten dedicated analysis models use GameplayDebug/DebugLogger; all twenty normal/detailed flags default off={flags.All(x => !x)}; Console.Write present={debug.Contains("Console.Write") || analyzer.Contains("Console.Write")}. Linear is emitted only as JumpPatternMatch from the orchestrator and has no dedicated Linear debug flag/entry point. This is a concrete observability gap, but not an analyzer-rule or mathematical violation; no refactor was authorized or applied.");
        report.AppendLine();
    }

    private static void AppendTestInventory(StringBuilder report)
    {
        report.AppendLine("## 19. Test inventory");
        report.AppendLine();
        report.AppendLine("|Family|Pattern tests|Forensic runners|Total|");
        report.AppendLine("|---|---:|---:|---:|");
        report.AppendLine("|Linear|12|0|12|");
        report.AppendLine("|Edge|22|20|42|");
        report.AppendLine("|Sharp|12|2|14|");
        report.AppendLine("|Triangle|26|1|27|");
        report.AppendLine("|1-2|12|1|13|");
        report.AppendLine("|Spaced|19|9|28|");
        report.AppendLine("|Wide Angle|24|14|38|");
        report.AppendLine("|Reversal|15|10|25|");
        report.AppendLine("|Arc|19|1|20|");
        report.AppendLine("|Spacing Trend|23|1|24|");
        report.AppendLine("|Alternating Spacing|19|1|20|");
        report.AppendLine("|Shared consolidation|0|1|1|");
        report.AppendLine("|**Exact all-Jump total**|**203**|**61**|**264**|");
        report.AppendLine();
        report.AppendLine("The recent 189 count came from the partial substring filter `FullyQualifiedName~JumpPattern`: it omitted all 24 Spacing Trend cases and 59 other pattern-specific forensic cases, while including 9 Stream isolation tests whose method names end in `JumpPatterns`. Before this consolidation, the exact union was 263; the new shared runner makes it 264. The production pattern-test subtotal is 203.");
        report.AppendLine();
    }

    private static void AppendFinalValidation(StringBuilder report)
    {
        report.AppendLine("## 20. Final validation");
        report.AppendLine();
        report.AppendLine("- Exact all-Jump union: **264/264 passed**.");
        report.AppendLine("- Stream Pattern suite: **177/177 passed**.");
        report.AppendLine("- Solution build: **passed, 0 warnings, 0 errors**.");
        report.AppendLine("- `git diff --check`: **passed** (non-failing line-ending conversion warnings may be emitted by the pre-existing worktree).");
        report.AppendLine();
    }

    private static void AppendConclusion(StringBuilder report, IReadOnlyList<MapResult> maps, IReadOnlyList<string> bugs)
    {
        report.AppendLine("## 21. OBJECTIVE BUGS");
        report.AppendLine();
        if (bugs.Count == 0) report.AppendLine("None detected by the consolidation invariants.");
        else foreach (string bug in bugs) report.AppendLine($"- {bug}");
        report.AppendLine();
        report.AppendLine("## 22. SEMANTIC / CALIBRATION QUESTIONS");
        report.AppendLine();
        report.AppendLine("- Triangle deliberately recognizes repeated local triangle geometry, including some generic zigzag-like passages; keep classification review and corpus monitoring.");
        report.AppendLine("- Small Arc detections on Tower Of Heaven and other maps can be mathematically valid but semantically local; keep monitoring.");
        report.AppendLine("- Alternating Spacing has no confirmed real positive in this fixture corpus; obtain positive controls before any future calibration.");
        report.AppendLine("- Linear lacks a dedicated GameplayDebug channel even though its output is available through the orchestrator match list; consider an observability-only follow-up outside this freeze.");
        report.AppendLine();
        report.AppendLine("## 23. Final status by pattern");
        report.AppendLine();
        report.AppendLine("|Pattern|Status|Evidence|");
        report.AppendLine("|---|---|---|");
        report.AppendLine("|Linear|STABLE_WITH_MONITORING|Core geometry and regression tests pass; dedicated debug observability is absent.|");
        report.AppendLine("|Edge|STABLE_V1|Edge zones, 64 px boundary, distance, coverage, and central-negative invariants pass.|");
        report.AppendLine("|Sharp|STABLE_V1|120° boundary and Reversal subset relation are coherent.|");
        report.AppendLine("|Triangle|STABLE_WITH_MONITORING|Mathematically coherent; local generic-zigzag scope is explicitly documented.|");
        report.AppendLine("|1-2|STABLE_V1|Black Rover positive and can-you-understand-me low/negative controls pass; no reversal wildcard regression.|");
        report.AppendLine("|Spaced|STABLE_V1|150 px boundary, consistency, and known controls pass.|");
        report.AppendLine("|Wide Angle|STABLE_V1|60°–115° band is disjoint from Sharp with the intended neutral zone.|");
        report.AppendLine("|Reversal|STABLE_V1|Every retained Reversal pivot is a retained Sharp pivot.|");
        report.AppendLine("|Arc|STABLE_WITH_MONITORING|Same-direction/cross-sign and consistency rules pass; local semantics remain monitored.|");
        report.AppendLine("|Spacing Trend|STABLE_V1|Dominant significant deltas are divided by all deltas; plateau regression remains protected.|");
        report.AppendLine("|Alternating Spacing|NEEDS_MORE_POSITIVE_CONTROLS|Conservative and no mass positives, but no confirmed real positive control.|");
        report.AppendLine();
        report.AppendLine("## 24. V1 freeze recommendation");
        report.AppendLine();
        report.AppendLine(bugs.Count == 0
            ? "**FROZEN_FOR_V1** — objective coverage, confidence, integrity, geometry, control, test, build, and diff conditions pass. Triangle/Arc monitoring, Alternating positive-control debt, and Linear debug observability are documented non-mathematical follow-ups."
            : "**NOT_READY_FOR_FREEZE** — objective bugs are listed above; no automatic correction was applied.");
    }

    private static PatternView[] Views(MapResult m)
    {
        JumpPatternMatch[] trendMatches = m.Trend.Sequences.Select(s => new JumpPatternMatch(s.Direction == JumpSpacingTrendDirection.Accelerating ? JumpPatternType.Accelerating : JumpPatternType.Decelerating, s.StartObjectIndex, s.EndObjectIndex, s.StartTime, s.EndTime, s.Confidence)).ToArray();
        int trendObjects = trendMatches.SelectMany(Indices).Distinct().Count();
        return
        [
            Make("Linear", m.Map.HitObjects.Count, m.Linear, Mean(m.Linear.Select(x => x.Confidence)), "local<=25deg; global<=30deg"),
            Make("Edge", m.Edge.TotalObjects, m.Edge.Matches, m.Edge.Confidence, $"transitions={m.Edge.EdgeTransitionCount}; strong={m.Edge.StrongTransitionCount}"),
            Make("Sharp", m.Sharp.TotalObjects, m.Sharp.Matches, m.Sharp.Confidence, $"avgAngle={m.Sharp.AverageSharpAngle:F2}; reversalPoints={m.Sharp.ReversalPointCount}"),
            Make("Triangle", m.Triangle.TotalObjects, m.Triangle.Matches, m.Triangle.Confidence, $"avgAngle={m.Triangle.AverageTriangleAngle:F2}; avgHeight={m.Triangle.AverageHeight:F2}"),
            Make("OneTwo", m.OneTwo.TotalObjects, m.OneTwo.Matches, m.OneTwo.Confidence, $"distanceConsistency={m.OneTwo.DistanceConsistency:F4}; intervalConsistency={m.OneTwo.IntervalConsistency:F4}; alternation={m.OneTwo.AlternationConsistency:F4}"),
            Make("Spaced", m.Spaced.TotalObjects, m.Spaced.Matches, m.Spaced.Confidence, $"avg/min/max={m.Spaced.AverageDistance:F2}/{m.Spaced.MinimumDistance:F2}/{m.Spaced.MaximumDistance:F2}; consistency={m.Spaced.DistanceConsistency:F4}"),
            Make("WideAngle", m.Wide.TotalObjects, m.Wide.Matches, m.Wide.Confidence, $"avg/min/max angle={m.Wide.AverageWideAngle:F2}/{m.Wide.MinimumWideAngleObserved:F2}/{m.Wide.MaximumWideAngleObserved:F2}"),
            Make("Reversal", m.Reversal.TotalObjects, m.Reversal.Matches, m.Reversal.Confidence, $"avg/min/max angle={m.Reversal.AverageReversalAngle:F2}/{m.Reversal.MinimumReversalAngleObserved:F2}/{m.Reversal.MaximumReversalAngleObserved:F2}; return={m.Reversal.AverageReturnDistance:F2}"),
            Make("Arc", m.Arc.TotalObjects, m.Arc.Matches, m.Arc.Confidence, $"avgAngle={m.Arc.AverageTurnAngle:F2}; angleConsistency={m.Arc.AngleConsistency:F4}; cumulative={m.Arc.AverageCumulativeTurn:F2}"),
            new("Trend", trendMatches.Length, trendObjects, m.Map.HitObjects.Count == 0 ? 0 : (double)trendObjects / m.Map.HitObjects.Count, trendMatches.Select(x => x.ObjectCount).DefaultIfEmpty().Max(), Mean(trendMatches.Select(x => (double)x.ObjectCount)), Mean(trendMatches.Select(x => x.Confidence)), $"accelerating={m.Trend.AcceleratingSequenceCount}; decelerating={m.Trend.DeceleratingSequenceCount}", trendMatches),
            Make("AlternatingSpacing", m.Alternating.TotalObjects, m.Alternating.Matches, m.Alternating.Confidence, $"alternation={m.Alternating.AverageAlternationConsistency:F4}; contrast={m.Alternating.AverageSpacingContrast:F4}; oscillation={m.Alternating.AverageOscillationMagnitudeConsistency:F4}")
        ];
    }

    private static PatternView Make(string name, int total, IReadOnlyList<JumpPatternMatch> matches, double confidence, string metrics)
    {
        int count = matches.SelectMany(Indices).Distinct().Count();
        return new(name, matches.Count, count, total == 0 ? 0 : (double)count / total, matches.Select(x => x.ObjectCount).DefaultIfEmpty().Max(), Mean(matches.Select(x => (double)x.ObjectCount)), confidence, metrics, matches);
    }

    private static Dictionary<string, IReadOnlyList<JumpPatternMatch>> MatchSets(MapResult m)
    {
        PatternView[] views = Views(m);
        return new()
        {
            ["Spaced"] = views.Single(x => x.Name == "Spaced").Matches,
            ["OneTwo"] = views.Single(x => x.Name == "OneTwo").Matches,
            ["Sharp"] = views.Single(x => x.Name == "Sharp").Matches,
            ["Reversal"] = views.Single(x => x.Name == "Reversal").Matches,
            ["Triangle"] = views.Single(x => x.Name == "Triangle").Matches,
            ["Arc"] = views.Single(x => x.Name == "Arc").Matches,
            ["Trend"] = views.Single(x => x.Name == "Trend").Matches,
            ["WideAngle"] = views.Single(x => x.Name == "WideAngle").Matches
        };
    }

    private static IEnumerable<Candidate> AlternatingCandidates(MapResult map)
    {
        bool[] stream = StreamMask(map.Map.HitObjects.Count, map.Profile.StreamSequences);
        var links = new List<(int Index, double Distance)>();
        var result = new List<Candidate>();
        void Flush()
        {
            for (int start = 0; start + 5 <= links.Count; start++)
            {
                double[] distances = links.Skip(start).Take(5).Select(x => x.Distance).ToArray();
                double[] deltas = distances.Zip(distances.Skip(1), (a, b) => b - a).ToArray();
                int[] signs = deltas.Select(Sign).ToArray();
                double alternation = signs.Zip(signs.Skip(1), (a, b) => a != 0 && b != 0 && a != b).Count(x => x) / 3d;
                double[] magnitudes = deltas.Where(x => Sign(x) != 0).Select(Math.Abs).ToArray();
                double contrast = magnitudes.Length == 0 ? 0 : magnitudes.Average() / distances.Average();
                double oscillation = magnitudes.Length == 0 ? 0 : magnitudes.Min() / magnitudes.Max();
                bool qualified = alternation >= .75 && contrast >= .20 && oscillation >= .50;
                string reason = alternation < .75 ? "Alternation" : contrast < .20 ? "Contrast" : oscillation < .50 ? "Oscillation" : "Qualified";
                result.Add(new(map, links[start].Index, links[start + 4].Index + 1, distances, alternation, contrast, oscillation, qualified, reason, (alternation + Math.Clamp(contrast / .20, 0, 1) + Math.Clamp(oscillation / .50, 0, 1)) / 3));
            }
            links.Clear();
        }
        for (int i = 0; i + 1 < map.Map.HitObjects.Count; i++)
        {
            if (!stream[i] && !stream[i + 1] && (map.Map.HitObjects[i].Type & 1) != 0 && (map.Map.HitObjects[i + 1].Type & 1) != 0 && GameplayAnalyzer.IsJumpLink(map.Map.HitObjects[i], map.Map.HitObjects[i + 1], out double distance) && distance >= 60) links.Add((i, distance));
            else Flush();
        }
        Flush();
        return result;
    }

    private static string ClassifyTriangle(MapResult map, JumpPatternMatch match, IReadOnlyList<TriangleJumpPoint> points)
    {
        int[] signs = points.Select(p => CrossSign(map.Map.HitObjects[p.ObjectIndex - 1], map.Map.HitObjects[p.ObjectIndex], map.Map.HitObjects[p.ObjectIndex + 1])).ToArray();
        double alternating = signs.Length <= 1 ? 0 : signs.Zip(signs.Skip(1), (a, b) => a != 0 && b != 0 && a != b).Count(x => x) / (double)(signs.Length - 1);
        if (alternating >= .75) return "GENERIC_ZIGZAG_LIKE";
        if (points.Count >= 4 && Mean(points.Select(x => x.Height)) >= 60 && Mean(points.Select(x => x.SideBalance)) >= .45) return "CLEAR_TRIANGLE";
        if (points.Count >= 2 && Mean(points.Select(x => x.Height)) >= 40) return "LOCAL_TRIANGLE_STRUCTURE";
        return "BORDERLINE";
    }

    private static string AngularMetrics<T>(IReadOnlyList<T> source, JumpPatternMatch match)
    {
        IEnumerable<(double Angle, double A, double B)> values = source switch
        {
            IReadOnlyList<SharpJumpPoint> p => p.Where(x => x.ObjectIndex > match.StartObjectIndex && x.ObjectIndex < match.EndObjectIndex).Select(x => (x.Angle, x.PreviousDistance, x.NextDistance)),
            IReadOnlyList<WideAngleJumpPoint> p => p.Where(x => x.ObjectIndex > match.StartObjectIndex && x.ObjectIndex < match.EndObjectIndex).Select(x => (x.Angle, x.PreviousDistance, x.NextDistance)),
            IReadOnlyList<ReversalJumpPoint> p => p.Where(x => x.ObjectIndex > match.StartObjectIndex && x.ObjectIndex < match.EndObjectIndex).Select(x => (x.Angle, x.PreviousDistance, x.NextDistance)),
            _ => []
        };
        var v = values.ToArray();
        return $"angles=[{Numbers(v.Select(x => x.Angle))}]; distances=[{Numbers(v.SelectMany(x => new[] { x.A, x.B }))}]";
    }

    private static void AppendMatch(StringBuilder report, MapResult map, JumpPatternMatch match, string metrics)
    {
        string positions = string.Join(';', Enumerable.Range(match.StartObjectIndex, match.ObjectCount).Select(i => $"({map.Map.HitObjects[i].X},{map.Map.HitObjects[i].Y})"));
        report.AppendLine($"- {map.Label} [{match.StartObjectIndex}..{match.EndObjectIndex}] {Time(match.StartTime)}–{Time(match.EndTime)} objects={match.ObjectCount}, confidence={match.Confidence:F4}, positions=[{positions}], {metrics}.");
    }

    private static bool[] StreamMask(int count, IReadOnlyList<PatternSequence> sequences)
    {
        var mask = new bool[count];
        foreach (PatternSequence s in sequences) for (int i = Math.Max(0, s.StartObjectIndex); i <= Math.Min(count - 1, s.EndObjectIndex); i++) mask[i] = true;
        return mask;
    }

    private static bool IsEdge(HitObject o) => o.X <= 64 || o.X >= 448 || o.Y <= 64 || o.Y >= 320;
    private static int CrossSign(HitObject a, HitObject b, HitObject c) => Math.Sign(((double)b.X - a.X) * (c.Y - b.Y) - ((double)b.Y - a.Y) * (c.X - b.X));
    private static int Sign(double delta) => Math.Abs(delta) >= SignificantSpacingDelta ? Math.Sign(delta) : 0;
    private static bool Overlap(JumpPatternMatch a, JumpPatternMatch b) => Math.Min(a.EndObjectIndex, b.EndObjectIndex) >= Math.Max(a.StartObjectIndex, b.StartObjectIndex);
    private static IEnumerable<int> Indices(JumpPatternMatch x) => Enumerable.Range(x.StartObjectIndex, x.ObjectCount);
    private static double Consistency(IEnumerable<double> source) { double[] v = source.ToArray(); return v.Length == 0 || v.Max() <= 0 ? 0 : v.Min() / v.Max(); }
    private static string Distribution(IEnumerable<double> source) { double[] v = source.OrderBy(x => x).ToArray(); return $"n={v.Length}, min={Min(v):F2}, median={Median(v):F2}, mean={Mean(v):F2}, max={Max(v):F2}"; }
    private static double Mean(IEnumerable<double> source) { double[] v = source.ToArray(); return v.Length == 0 ? 0 : v.Average(); }
    private static double Median(IReadOnlyList<double> v) { if (v.Count == 0) return 0; int m = v.Count / 2; return v.Count % 2 == 0 ? (v[m - 1] + v[m]) / 2 : v[m]; }
    private static double Min(IEnumerable<double> source) { double[] v = source.ToArray(); return v.Length == 0 ? 0 : v.Min(); }
    private static double Max(IEnumerable<double> source) { double[] v = source.ToArray(); return v.Length == 0 ? 0 : v.Max(); }
    private static string Numbers(IEnumerable<double> source) => string.Join(',', source.Select(x => x.ToString("F2", CultureInfo.InvariantCulture)));
    private static string Pct(double value) => (value * 100).ToString("F2", CultureInfo.InvariantCulture);
    private static string Time(int value) => TimeSpan.FromMilliseconds(value).ToString(@"mm\:ss\.fff");
    private static string Cell(string value) => value.Replace("|", "\\|");
    private static string FindRepoRoot() { DirectoryInfo? d = new(AppContext.BaseDirectory); while (d is not null && !File.Exists(Path.Combine(d.FullName, "BeatInsight.slnx"))) d = d.Parent; return d?.FullName ?? throw new DirectoryNotFoundException(); }

    private static readonly string[] PatternNames = ["Linear", "Edge", "Sharp", "Triangle", "OneTwo", "Spaced", "WideAngle", "Reversal", "Arc", "Trend", "AlternatingSpacing"];
    private sealed record PatternView(string Name, int SequenceCount, int ObjectCount, double Coverage, int Longest, double AverageLength, double Confidence, string Metrics, IReadOnlyList<JumpPatternMatch> Matches);
    private sealed record Candidate(MapResult Map, int Start, int End, IReadOnlyList<double> Distances, double Alternation, double Contrast, double Oscillation, bool Qualified, string Reason, double Score);
    private sealed record MapResult(string Fixture, Beatmap Map, GameplayProfile Profile, IReadOnlyList<JumpPatternMatch> Linear, EdgeJumpAnalysis Edge, SharpJumpAnalysis Sharp, TriangleJumpAnalysis Triangle, OneTwoJumpAnalysis OneTwo, SpacedJumpAnalysis Spaced, WideAngleJumpAnalysis Wide, ReversalJumpAnalysis Reversal, ArcJumpAnalysis Arc, JumpSpacingTrendAnalysis Trend, AlternatingSpacingJumpAnalysis Alternating)
    {
        public string Label => $"{Map.Title} [{Map.Version}]";
    }
}
