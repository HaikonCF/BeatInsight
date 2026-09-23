using BeatInsight.Analysis;
using BeatInsight.Models;
using BeatInsight.Parser;
using System.Globalization;
using System.Text;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Phase 2.14 audit-only prototype. The signatures and variation metrics live
/// in the test project exclusively; no V2 window, decision, or region result
/// is extended by this code.
/// </summary>
internal static class TechV2SustainedStructuralVariationPrototype
{
    private static readonly IReadOnlySet<string> IncludedKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "Exit", "Kira", "Frozen", "WorldEndsNow", "GloriousDays",
        "Inferno", "Howling", "TaoHuaXiao", "RockOnExpert", "TrustInYou",
        "HappppySong", "RockOnSeAqours", "FinestSky",
    };

    internal static TechV2SustainedStructuralVariationReport Run(string databasePath)
    {
        TechV2HumanGroundedAuditReport source = TechV2HumanGroundedStrongTechAudit.Run(databasePath);
        IReadOnlyList<TechV2StructuralVariationMap> maps = source.Maps
            .Where(map => IncludedKeys.Contains(map.Reference.Key))
            .Select(AnalyzeMap)
            .ToArray();
        IReadOnlyList<TechV2StructuralVariationRun> runs = maps.Where(map => map.Resolved)
            .SelectMany(map => map.Runs)
            .ToArray();

        return new TechV2SustainedStructuralVariationReport(
            maps,
            BuildComparisons(runs),
            SimulateCandidates(runs));
    }

    internal static string FormatReport(TechV2SustainedStructuralVariationReport report)
    {
        StringBuilder text = new();
        text.AppendLine("===== TECH V2 PHASE 2.14 SUSTAINED STRUCTURAL VARIATION PROTOTYPE (TEST-ONLY) =====");
        text.AppendLine("Each value below is a mean absolute delta between successive SustainedTech windows in one run; it is not an in-window magnitude.");
        text.AppendLine("COMPONENT COMPARISON | Component | Tech mean/median/min/max | Non-Tech mean/median/min/max | overlap");
        foreach (TechV2StructuralVariationComparison comparison in report.Comparisons)
            text.AppendLine($"  {comparison.Name} | {Format(comparison.Tech)} | {Format(comparison.NonTech)} | {Overlap(comparison.Tech, comparison.NonTech)}");

        text.AppendLine("SUSTAINED RUN VALUES | Map | Human | Time | Windows | Angle | Distance | Transition Types | Slider/Circle Topology | Structural Pivots | Combined | Spacing | Structural");
        foreach (TechV2StructuralVariationRun run in report.Maps.Where(map => map.Resolved)
                     .SelectMany(map => map.Runs)
                     .OrderBy(run => run.Truth).ThenBy(run => run.MapKey, StringComparer.Ordinal).ThenBy(run => run.StartTime))
        {
            TechV2StructuralVariation value = run.Variation;
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  {0} | {1} | {2:F0}->{3:F0}ms | {4} | {5:F3} | {6:F3} | {7:F3} | {8:F3} | {9:F3} | {10:F3} | {11:F3} | {12:F3}",
                run.MapKey, run.Truth, run.StartTime, run.EndTime, run.WindowCount,
                value.AnglePatternVariation, value.DistancePatternVariation,
                value.TransitionTypeVariation, value.SliderCircleTopologyVariation,
                value.StructuralPivotVariation, value.StructuralVariation,
                run.AverageSpacingVariation, run.AverageStructuralRatio));
        }

        text.AppendLine("OFFLINE PROTOTYPE COMBINATIONS | Candidate | Tech kept/removed | Non-Tech kept/removed | GLORIOUS/Kira/Frozen removed");
        foreach (TechV2StructuralVariationCandidate candidate in report.Candidates)
            text.AppendLine($"  {candidate.Name} | {candidate.TechKept}/{candidate.TechRemoved} | {candidate.NonTechKept}/{candidate.NonTechRemoved} | {candidate.GloriousRemoved}/{candidate.KiraRemoved}/{candidate.FrozenRemoved}");
        text.AppendLine("The three combinations are fixed observation probes only, not fitted or adopted thresholds.");
        return text.ToString();
    }

    private static TechV2StructuralVariationMap AnalyzeMap(TechV2HumanGroundedMap source)
    {
        if (!source.Resolved || source.Path is null)
            return TechV2StructuralVariationMap.Unresolved(source.Reference, source.ResolutionNote);

        Beatmap beatmap = BeatmapParser.Load(source.Path);
        IReadOnlyList<TechV2ExperimentalWindowResult> windows = TechDetectorV2Experimental.Analyze(
            beatmap.HitObjects, GameplayAnalyzer.IsSpinner, GameplayAnalyzer.IsSlider,
            GameplayAnalyzer.IsComplexSlider, GameplayAnalyzer.GetObjectStartPosition,
            GameplayAnalyzer.GetObjectEndPosition, GameplayAnalyzer.GetTechTurnAngle);
        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> byStart = windows.ToDictionary(window => window.StartIndex);
        IReadOnlyDictionary<int, TechV2WindowSignature> signatures = windows.ToDictionary(
            window => window.StartIndex,
            window => BuildSignature(beatmap.HitObjects, window));
        IReadOnlyList<TechV2StructuralVariationRun> runs = BuildRuns(source, byStart, signatures);
        return new TechV2StructuralVariationMap(source.Reference, true, source.ResolutionNote, runs);
    }

    private static IReadOnlyList<TechV2StructuralVariationRun> BuildRuns(
        TechV2HumanGroundedMap map,
        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> windows,
        IReadOnlyDictionary<int, TechV2WindowSignature> signatures)
    {
        List<TechV2StructuralVariationRun> runs = [];
        List<TechV2WindowDecision> active = [];
        foreach (TechV2WindowDecision decision in map.Decisions.OrderBy(item => item.StartIndex))
        {
            bool continuous = active.Count > 0
                && decision.IsSustainedTech
                && decision.StartIndex == active[^1].StartIndex + TechDetectorV2Experimental.Stride
                && decision.StartTime <= active[^1].EndTime;
            if (decision.IsSustainedTech && (active.Count == 0 || continuous))
            {
                active.Add(decision);
                continue;
            }

            AddRun(runs, map.Reference, active, windows, signatures);
            active.Clear();
            if (decision.IsSustainedTech)
                active.Add(decision);
        }

        AddRun(runs, map.Reference, active, windows, signatures);
        return runs;
    }

    private static void AddRun(
        List<TechV2StructuralVariationRun> runs,
        TechV2HumanReference reference,
        IReadOnlyList<TechV2WindowDecision> decisions,
        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> windows,
        IReadOnlyDictionary<int, TechV2WindowSignature> signatures)
    {
        if (decisions.Count == 0)
            return;

        TechV2WindowSignature[] sequence = decisions.Select(decision => signatures[decision.StartIndex]).ToArray();
        TechV2ExperimentalWindowResult[] source = decisions.Select(decision => windows[decision.StartIndex]).ToArray();
        runs.Add(new TechV2StructuralVariationRun(
            reference.Key,
            reference.HumanTruth,
            decisions[0].StartTime,
            decisions[^1].EndTime,
            decisions.Count,
            sequence,
            ComputeVariation(sequence),
            source.Average(window => window.SpacingVariation),
            sequence.Average(signature => signature.TurnTypeRatios[0])));
    }

    private static TechV2WindowSignature BuildSignature(
        IReadOnlyList<HitObject> objects,
        TechV2ExperimentalWindowResult window)
    {
        List<double> angles = [];
        List<double> distances = [];
        int valid = 0, structural = 0, sharp = 0, compact = 0, awkward = 0, spacing = 0;
        for (int index = window.StartIndex + 1; index <= window.EndIndex - 1; index++)
        {
            HitObject previous = objects[index - 1], current = objects[index], next = objects[index + 1];
            if (GameplayAnalyzer.IsSpinner(previous) || GameplayAnalyzer.IsSpinner(current) || GameplayAnalyzer.IsSpinner(next))
                continue;
            if (current.Time - previous.Time <= 0 || next.Time - current.Time <= 0)
                continue;

            double first = Distance(GameplayAnalyzer.GetObjectEndPosition(previous), GameplayAnalyzer.GetObjectStartPosition(current));
            double second = Distance(GameplayAnalyzer.GetObjectEndPosition(current), GameplayAnalyzer.GetObjectStartPosition(next));
            if (first > 160 || second > 160)
                continue;

            valid++;
            double angle = GameplayAnalyzer.GetTechTurnAngle(previous, current, next);
            double averageDistance = (first + second) / 2;
            angles.Add(angle / 180d);
            distances.Add(averageDistance / 160d);
            if (angle >= 110) structural++;
            if (angle >= 150 && averageDistance <= 125) sharp++;
            if (first <= 110 && second <= 110) compact++;
            if (angle >= 45 && angle <= 150 && averageDistance <= 125) awkward++;
            if (Math.Abs(first - second) >= 35) spacing++;
        }

        IReadOnlyList<HitObject> local = objects.Skip(window.StartIndex).Take(TechDetectorV2Experimental.WindowSize).ToArray();
        double[] topology = TransitionTopology(local);
        return new TechV2WindowSignature(
            angles.Count == 0 ? 0 : angles.Average(),
            distances.Count == 0 ? 0 : distances.Average(),
            [Ratio(structural, valid), Ratio(sharp, valid), Ratio(compact, valid), Ratio(awkward, valid), Ratio(spacing, valid)],
            topology,
            structural);
    }

    private static TechV2StructuralVariation ComputeVariation(IReadOnlyList<TechV2WindowSignature> sequence)
    {
        if (sequence.Count < 2)
            return new(0, 0, 0, 0, 0, 0);

        List<double> angle = [], distance = [], transitionType = [], topology = [], pivots = [];
        for (int index = 1; index < sequence.Count; index++)
        {
            TechV2WindowSignature previous = sequence[index - 1], current = sequence[index];
            angle.Add(Math.Abs(current.MeanAngle - previous.MeanAngle));
            distance.Add(Math.Abs(current.MeanDistance - previous.MeanDistance));
            transitionType.Add(MeanAbsoluteDelta(current.TurnTypeRatios, previous.TurnTypeRatios));
            topology.Add(MeanAbsoluteDelta(current.SliderCircleTopology, previous.SliderCircleTopology));
            pivots.Add(Math.Abs(current.StructuralPivotCount - previous.StructuralPivotCount) / (double)(TechDetectorV2Experimental.WindowSize - 2));
        }

        double a = angle.Average(), d = distance.Average(), t = transitionType.Average(), top = topology.Average(), p = pivots.Average();
        return new(a, d, t, top, p, (a + d + t + top + p) / 5d);
    }

    private static IReadOnlyList<TechV2StructuralVariationComparison> BuildComparisons(IReadOnlyList<TechV2StructuralVariationRun> runs) =>
    [
        Comparison("AnglePatternVariation", runs, run => run.Variation.AnglePatternVariation),
        Comparison("DistancePatternVariation", runs, run => run.Variation.DistancePatternVariation),
        Comparison("TransitionTypeVariation", runs, run => run.Variation.TransitionTypeVariation),
        Comparison("SliderCircleTopologyVariation", runs, run => run.Variation.SliderCircleTopologyVariation),
        Comparison("StructuralPivotVariation", runs, run => run.Variation.StructuralPivotVariation),
        Comparison("StructuralVariation", runs, run => run.Variation.StructuralVariation),
    ];

    private static TechV2StructuralVariationComparison Comparison(string name, IReadOnlyList<TechV2StructuralVariationRun> runs, Func<TechV2StructuralVariationRun, double> value) => new(
        name,
        Stats(runs.Where(run => run.Truth == TechV2HumanTruth.Tech).Select(value)),
        Stats(runs.Where(run => run.Truth == TechV2HumanTruth.NonTech).Select(value)));

    // Fixed, deliberately unoptimized observation probes. They are test-only
    // filters used to make retained/removed counts readable, never decisions.
    private static IReadOnlyList<TechV2StructuralVariationCandidate> SimulateCandidates(IReadOnlyList<TechV2StructuralVariationRun> runs) =>
    [
        Simulate("StructuralVariation only (>= .08)", runs, run => run.Variation.StructuralVariation >= .08),
        Simulate("StructuralVariation + SpacingVariation", runs, run => run.Variation.StructuralVariation >= .08 && run.AverageSpacingVariation >= .50),
        Simulate("StructuralVariation + StructuralRatio", runs, run => run.Variation.StructuralVariation >= .08 && run.AverageStructuralRatio >= .30),
    ];

    private static TechV2StructuralVariationCandidate Simulate(string name, IReadOnlyList<TechV2StructuralVariationRun> runs, Func<TechV2StructuralVariationRun, bool> keeps) => new(
        name,
        runs.Count(run => run.Truth == TechV2HumanTruth.Tech && keeps(run)),
        runs.Count(run => run.Truth == TechV2HumanTruth.Tech && !keeps(run)),
        runs.Count(run => run.Truth == TechV2HumanTruth.NonTech && keeps(run)),
        runs.Count(run => run.Truth == TechV2HumanTruth.NonTech && !keeps(run)),
        runs.Count(run => run.MapKey == "GloriousDays" && !keeps(run)),
        runs.Count(run => run.MapKey == "Kira" && !keeps(run)),
        runs.Count(run => run.MapKey == "Frozen" && !keeps(run)));

    private static double[] TransitionTopology(IReadOnlyList<HitObject> objects)
    {
        int ss = 0, sc = 0, cs = 0, cc = 0;
        for (int index = 1; index < objects.Count; index++)
        {
            if (GameplayAnalyzer.IsSpinner(objects[index - 1]) || GameplayAnalyzer.IsSpinner(objects[index])) continue;
            bool previousSlider = GameplayAnalyzer.IsSlider(objects[index - 1]);
            bool currentSlider = GameplayAnalyzer.IsSlider(objects[index]);
            if (previousSlider && currentSlider) ss++;
            else if (previousSlider) sc++;
            else if (currentSlider) cs++;
            else cc++;
        }
        int total = ss + sc + cs + cc;
        return [Ratio(ss, total), Ratio(sc, total), Ratio(cs, total), Ratio(cc, total)];
    }

    private static TechV2StructuralVariationStats Stats(IEnumerable<double> values)
    {
        double[] data = values.OrderBy(value => value).ToArray();
        return data.Length == 0 ? new(0, 0, 0, 0) : new(data.Average(), data[data.Length / 2], data[0], data[^1]);
    }

    private static string Format(TechV2StructuralVariationStats value) => string.Format(CultureInfo.InvariantCulture, "{0:F3}/{1:F3}/{2:F3}/{3:F3}", value.Mean, value.Median, value.Min, value.Max);
    private static string Overlap(TechV2StructuralVariationStats first, TechV2StructuralVariationStats second) => string.Format(CultureInfo.InvariantCulture, "{0:F3}..{1:F3}", Math.Max(first.Min, second.Min), Math.Min(first.Max, second.Max));
    private static double Ratio(int numerator, int denominator) => denominator == 0 ? 0 : (double)numerator / denominator;
    private static double MeanAbsoluteDelta(IReadOnlyList<double> first, IReadOnlyList<double> second) => first.Zip(second).Average(pair => Math.Abs(pair.First - pair.Second));
    private static double Distance((double X, double Y) first, (double X, double Y) second) => Math.Sqrt(Math.Pow(second.X - first.X, 2) + Math.Pow(second.Y - first.Y, 2));
}

internal sealed record TechV2WindowSignature(double MeanAngle, double MeanDistance, IReadOnlyList<double> TurnTypeRatios, IReadOnlyList<double> SliderCircleTopology, int StructuralPivotCount);
internal sealed record TechV2StructuralVariation(double AnglePatternVariation, double DistancePatternVariation, double TransitionTypeVariation, double SliderCircleTopologyVariation, double StructuralPivotVariation, double StructuralVariation);
internal sealed record TechV2StructuralVariationRun(string MapKey, TechV2HumanTruth Truth, double StartTime, double EndTime, int WindowCount, IReadOnlyList<TechV2WindowSignature> WindowSignatures, TechV2StructuralVariation Variation, double AverageSpacingVariation, double AverageStructuralRatio);
internal sealed record TechV2StructuralVariationMap(TechV2HumanReference Reference, bool Resolved, string ResolutionNote, IReadOnlyList<TechV2StructuralVariationRun> Runs) { internal static TechV2StructuralVariationMap Unresolved(TechV2HumanReference reference, string note) => new(reference, false, note, []); }
internal sealed record TechV2StructuralVariationStats(double Mean, double Median, double Min, double Max);
internal sealed record TechV2StructuralVariationComparison(string Name, TechV2StructuralVariationStats Tech, TechV2StructuralVariationStats NonTech);
internal sealed record TechV2StructuralVariationCandidate(string Name, int TechKept, int TechRemoved, int NonTechKept, int NonTechRemoved, int GloriousRemoved, int KiraRemoved, int FrozenRemoved);
internal sealed record TechV2SustainedStructuralVariationReport(IReadOnlyList<TechV2StructuralVariationMap> Maps, IReadOnlyList<TechV2StructuralVariationComparison> Comparisons, IReadOnlyList<TechV2StructuralVariationCandidate> Candidates);
