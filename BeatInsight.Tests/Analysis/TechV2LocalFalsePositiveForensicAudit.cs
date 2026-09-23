using BeatInsight.Analysis;
using BeatInsight.Models;
using BeatInsight.Parser;
using System.Globalization;
using System.Text;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Phase 2.12: human-grounded forensic only. Policy D is consumed exactly as
/// implemented by BuildPresence; no simulated guard reaches runtime code.
/// </summary>
internal static class TechV2LocalFalsePositiveForensicAudit
{
    private static readonly IReadOnlySet<string> IncludedKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "Exit", "Kira", "Frozen", "WorldEndsNow", "GloriousDays",
        "Howling", "TaoHuaXiao", "RockOnExpert", "TrustInYou", "HappppySong",
        "RockOnSeAqours", "Inferno", "FinestSky",
    };

    internal static TechV2LocalFalsePositiveReport Run(string databasePath)
    {
        TechV2HumanGroundedAuditReport baseReport = TechV2HumanGroundedStrongTechAudit.Run(databasePath);
        IReadOnlyList<TechV2LocalFalsePositiveMap> maps = baseReport.Maps
            .Where(map => IncludedKeys.Contains(map.Reference.Key))
            .Select(AnalyzeMap)
            .ToArray();

        return new TechV2LocalFalsePositiveReport(
            maps,
            SimulateStrongGuards(maps),
            SimulateSustainedGuards(maps));
    }

    internal static string FormatReport(TechV2LocalFalsePositiveReport report)
    {
        StringBuilder text = new();
        text.AppendLine("===== TECH V2 PHASE 2.12 LOCAL FALSE-POSITIVE FORENSIC (TEST-ONLY) =====");
        text.AppendLine("POLICY D is the current baseline. Human truth is used only for audit grouping.");

        text.AppendLine("BRANCH PRESENCE | map | human | regions containing Strong | pure Sustained | Strong+Sustained | total presence");
        foreach (TechV2LocalFalsePositiveMap map in report.Maps)
        {
            if (!map.Resolved)
            {
                text.AppendLine($"  {map.Reference.Key} | unresolved: {map.ResolutionNote}");
                continue;
            }
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  {0} | {1} | {2:F2}% | {3:F2}% | {4:F2}% | {5:F2}%",
                map.Reference.Key, map.Reference.HumanTruth,
                map.StrongContainingRegionPresencePercent, map.PureSustainedRegionPresencePercent,
                map.StrongPlusSustainedRegionPresencePercent, map.Presence.PresencePercent));
        }

        AppendStrongWindows(text, report.Maps);
        AppendStrongDistribution(text, "HUMAN TECH STRONGTECH DISTRIBUTION", report.Maps, TechV2HumanTruth.Tech);
        AppendStrongDistribution(text, "HUMAN NON-TECH STRONGTECH DISTRIBUTION", report.Maps, TechV2HumanTruth.NonTech);
        AppendSustainedRuns(text, report.Maps);
        AppendSustainedDistribution(text, "HUMAN TECH SUSTAINEDTECH DISTRIBUTION", report.Maps, TechV2HumanTruth.Tech);
        AppendSustainedDistribution(text, "HUMAN NON-TECH SUSTAINEDTECH DISTRIBUTION", report.Maps, TechV2HumanTruth.NonTech);

        text.AppendLine("NON-TECH MAP-BY-MAP BRANCH ATTRIBUTION | map | dominant branch | Strong contribution | Sustained contribution | main pair | raw cause | archetype");
        foreach (TechV2LocalFalsePositiveMap map in report.Maps.Where(map => map.Resolved && map.Reference.HumanTruth == TechV2HumanTruth.NonTech))
        {
            TechV2StrongWindowForensic? strongest = map.StrongWindows.OrderByDescending(window => window.RawScore).FirstOrDefault();
            TechV2SustainedRunForensic? longestRun = map.SustainedRuns.OrderByDescending(run => run.WindowCount).FirstOrDefault();
            string pair = strongest is not null ? Pair(strongest.Detail) : longestRun?.DominantPair ?? "None";
            string cause = strongest is not null ? DescribeStrongCause(strongest) : DescribeSustainedCause(longestRun);
            string branch = map.StrongPlusSustainedRegionPresencePercent > 0 ? "Strong + Sustained"
                : map.StrongRegionPresencePercent > 0 ? "StrongTech"
                : map.PureSustainedRegionPresencePercent > 0 ? "Pure SustainedTech" : "None";
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  {0} | {1} | {2:F2}% | {3:F2}% | {4} | {5} | {6}",
                map.Reference.Key, branch, map.StrongContainingRegionPresencePercent,
                map.PureSustainedRegionPresencePercent + map.StrongPlusSustainedRegionPresencePercent,
                pair, cause, ClassifyArchetype(strongest, longestRun)));
        }

        AppendGuardResults(text, "OFFLINE STRONGTECH GUARD CANDIDATES", report.StrongGuardResults);
        AppendGuardResults(text, "OFFLINE SUSTAINEDTECH GUARD CANDIDATES", report.SustainedGuardResults);
        text.AppendLine("BEST NEXT HYPOTHESES (offline only):");
        text.AppendLine("  StrongTech: inspect Movement+Slider low-structure survivors; they remain separable from local slider complexity alone.");
        text.AppendLine("  SustainedTech: inspect run-level low-geometry/low-pivot evidence independently; do not reuse a StrongTech guard automatically.");
        return text.ToString();
    }

    private static TechV2LocalFalsePositiveMap AnalyzeMap(TechV2HumanGroundedMap map)
    {
        if (!map.Resolved || map.Path is null)
            return TechV2LocalFalsePositiveMap.Unresolved(map.Reference, map.ResolutionNote);

        Beatmap beatmap = BeatmapParser.Load(map.Path);
        IReadOnlyList<TechV2ExperimentalWindowResult> windows = TechDetectorV2Experimental.Analyze(
            beatmap.HitObjects, GameplayAnalyzer.IsSpinner, GameplayAnalyzer.IsSlider,
            GameplayAnalyzer.IsComplexSlider, GameplayAnalyzer.GetObjectStartPosition,
            GameplayAnalyzer.GetObjectEndPosition, GameplayAnalyzer.GetTechTurnAngle);
        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> sources = windows.ToDictionary(window => window.StartIndex);
        IReadOnlyList<TechV2StrongWindowForensic> strong = map.Decisions.Where(decision => decision.IsStrongTech)
            .Select(decision => new TechV2StrongWindowForensic(
                TechV2StrongTechForensicAudit.DescribeWindow(decision, sources[decision.StartIndex], beatmap.HitObjects),
                decision.RawScore))
            .OrderBy(window => window.Detail.StartTime).ToArray();
        IReadOnlyList<TechV2SustainedRunForensic> sustained = BuildSustainedRuns(map.Decisions, sources, beatmap.HitObjects);

        double strongPresence = 0;
        double pureSustained = 0;
        double strongPlusSustained = 0;
        foreach (TechV2Region region in map.Presence.Regions)
        {
            double contribution = map.Presence.PlayableDuration <= 0 ? 0 : region.Duration / map.Presence.PlayableDuration * 100;
            if (region.StrongTechWindowCount == 0)
                pureSustained += contribution;
            else if (region.SustainedTechWindowCount > 0)
                strongPlusSustained += contribution;
            else
                strongPresence += contribution;
        }
        return new TechV2LocalFalsePositiveMap(map.Reference, true, map.ResolutionNote,
            map.Presence, strongPresence, pureSustained, strongPlusSustained, strong, sustained);
    }

    private static IReadOnlyList<TechV2SustainedRunForensic> BuildSustainedRuns(
        IReadOnlyList<TechV2WindowDecision> decisions,
        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> sources,
        IReadOnlyList<HitObject> objects)
    {
        List<TechV2SustainedRunForensic> runs = [];
        List<TechV2WindowDecision> current = [];
        foreach (TechV2WindowDecision decision in decisions.OrderBy(item => item.StartIndex))
        {
            bool continuous = current.Count > 0
                && decision.IsSustainedTech
                && decision.StartIndex == current[^1].StartIndex + TechDetectorV2Experimental.Stride
                && decision.StartTime <= current[^1].EndTime;
            if (decision.IsSustainedTech && (current.Count == 0 || continuous))
            {
                current.Add(decision);
                continue;
            }
            AddRun(runs, current, sources, objects);
            current.Clear();
            if (decision.IsSustainedTech)
                current.Add(decision);
        }
        AddRun(runs, current, sources, objects);
        return runs;
    }

    private static void AddRun(
        ICollection<TechV2SustainedRunForensic> runs,
        IReadOnlyList<TechV2WindowDecision> decisions,
        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> sources,
        IReadOnlyList<HitObject> objects)
    {
        if (decisions.Count == 0)
            return;
        IReadOnlyList<TechV2StrongTechWindowDetail> details = decisions.Select(decision =>
            TechV2StrongTechForensicAudit.DescribeWindow(decision, sources[decision.StartIndex], objects)).ToArray();
        IGrouping<string, TechV2StrongTechWindowDetail> dominant = details.GroupBy(Pair)
            .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal).First();
        TechV2StrongTechWindowDetail minimum = details.OrderBy(detail => detail.SecondBestFamilyValue).First();
        TechV2StrongTechWindowDetail maximum = details.OrderByDescending(detail => detail.SecondBestFamilyValue).First();
        runs.Add(new TechV2SustainedRunForensic(
            decisions[0].StartTime, decisions[^1].EndTime, decisions.Count,
            decisions.Average(decision => decision.GeometryEvidence),
            decisions.Average(decision => decision.MovementEvidence),
            decisions.Average(decision => decision.SliderEvidence),
            decisions.Average(decision => decision.MotifEvidence),
            $"{minimum.SecondBestFamily}={minimum.SecondBestFamilyValue:F3}",
            $"{maximum.SecondBestFamily}={maximum.SecondBestFamilyValue:F3}",
            details.Average(detail => detail.Support.StructuralPivotCount),
            details.Average(detail => detail.Support.ValidTransitionCount),
            dominant.Key));
    }

    private static IReadOnlyList<TechV2GuardAuditResult> SimulateStrongGuards(IReadOnlyList<TechV2LocalFalsePositiveMap> maps) =>
    [
        Simulate<TechV2StrongWindowForensic>("G3 Movement+Slider with pivots < 3", maps, window =>
            !(IsMovementSliderPair(window.Detail) && window.Detail.Support.StructuralPivotCount < 3), Branch.Strong),
        Simulate<TechV2StrongWindowForensic>("Movement dominant with Geometry < .30", maps, window =>
            !(window.Detail.BestFamily == "Movement" && window.Detail.Geometry < .30), Branch.Strong),
        Simulate<TechV2StrongWindowForensic>("Slider saturation with Geometry < .25", maps, window =>
            !(window.Detail.Slider >= .75 && window.Detail.Geometry < .25), Branch.Strong),
    ];

    private static IReadOnlyList<TechV2GuardAuditResult> SimulateSustainedGuards(IReadOnlyList<TechV2LocalFalsePositiveMap> maps) =>
    [
        Simulate<TechV2SustainedRunForensic>("Run average Geometry >= .20", maps, run => run.AverageGeometry >= .20, Branch.Sustained),
        Simulate<TechV2SustainedRunForensic>("Run Geometry >= .25 or pivots >= 2", maps, run => run.AverageGeometry >= .25 || run.AverageStructuralPivotCount >= 2, Branch.Sustained),
        Simulate<TechV2SustainedRunForensic>("Reject low-geometry Slider-led runs", maps, run => !(run.AverageSlider >= run.AverageMovement && run.AverageGeometry < .25), Branch.Sustained),
    ];

    private static TechV2GuardAuditResult Simulate<T>(
        string name,
        IReadOnlyList<TechV2LocalFalsePositiveMap> maps,
        Func<T, bool> keeps,
        Branch branch)
    {
        IEnumerable<(TechV2HumanTruth Truth, string Key, T Item)> items = maps.Where(map => map.Resolved)
            .SelectMany(map => (branch == Branch.Strong
                ? map.StrongWindows.Cast<T>()
                : map.SustainedRuns.Cast<T>())
                .Select(item => (map.Reference.HumanTruth, map.Reference.Key, item)));
        IReadOnlyList<(TechV2HumanTruth Truth, string Key, T Item)> all = items.ToArray();
        int techKept = all.Count(item => item.Truth == TechV2HumanTruth.Tech && keeps(item.Item));
        int techRemoved = all.Count(item => item.Truth == TechV2HumanTruth.Tech && !keeps(item.Item));
        int nonTechKept = all.Count(item => item.Truth == TechV2HumanTruth.NonTech && keeps(item.Item));
        int nonTechRemoved = all.Count(item => item.Truth == TechV2HumanTruth.NonTech && !keeps(item.Item));
        return new TechV2GuardAuditResult(name, branch, techKept, techRemoved, nonTechKept, nonTechRemoved,
            all.Where(item => item.Key == "GloriousDays" && !keeps(item.Item)).Count(),
            all.Where(item => item.Key == "Kira" && !keeps(item.Item)).Count(),
            all.Where(item => item.Key == "Frozen" && !keeps(item.Item)).Count());
    }

    private static void AppendStrongWindows(StringBuilder text, IReadOnlyList<TechV2LocalFalsePositiveMap> maps)
    {
        text.AppendLine("STRONGTECH WINDOWS | map | truth | time | rule | pair | G/M/S/Mo | pivots/transitions/sliders/complex/awkward/spacing | angle/geometry/rhythm/sliderComplexity");
        foreach (TechV2LocalFalsePositiveMap map in maps.Where(map => map.Resolved))
        foreach (TechV2StrongWindowForensic window in map.StrongWindows)
        {
            TechV2StrongTechWindowDetail d = window.Detail;
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  {0} | {1} | {2:F0}->{3:F0} | {4} | {5} | {6:F3}/{7:F3}/{8:F3}/{9:F3} | {10}/{11}/{12}/{13}/{14}/{15} | {16:F3}/{17:F3}/{18:F3}/{19:F3}",
                map.Reference.Key, map.Reference.HumanTruth, d.StartTime, d.EndTime, d.RuleTriggered, Pair(d),
                d.Geometry, d.Movement, d.Slider, d.Motif,
                d.Support.StructuralPivotCount, d.Support.ValidTransitionCount, d.Support.SliderCount,
                d.Support.ComplexSliderCount, d.Support.AwkwardTransitionCount, d.Support.SpacingVariationTransitionCount,
                d.AngleCoherenceWithSupport, d.GeometryIrregularity, d.RhythmIrregularity, d.SliderComplexity));
        }
    }

    private static void AppendStrongDistribution(StringBuilder text, string title, IReadOnlyList<TechV2LocalFalsePositiveMap> maps, TechV2HumanTruth truth)
    {
        IReadOnlyList<TechV2StrongTechWindowDetail> items = maps.Where(map => map.Resolved && map.Reference.HumanTruth == truth)
            .SelectMany(map => map.StrongWindows).Select(window => window.Detail).ToArray();
        text.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "{0} | windows={1} | avg G/M/S/Mo={2:F3}/{3:F3}/{4:F3}/{5:F3} | avg pivots/transitions={6:F2}/{7:F2} | pairs={8}",
            title, items.Count,
            items.Count == 0 ? 0 : items.Average(item => item.Geometry), items.Count == 0 ? 0 : items.Average(item => item.Movement),
            items.Count == 0 ? 0 : items.Average(item => item.Slider), items.Count == 0 ? 0 : items.Average(item => item.Motif),
            items.Count == 0 ? 0 : items.Average(item => item.Support.StructuralPivotCount),
            items.Count == 0 ? 0 : items.Average(item => item.Support.ValidTransitionCount),
            Pairs(items)));
    }

    private static void AppendSustainedRuns(StringBuilder text, IReadOnlyList<TechV2LocalFalsePositiveMap> maps)
    {
        text.AppendLine("SUSTAINEDTECH RUNS | map | truth | time | windows | avg G/M/S/Mo | min/max second | avg pivots/transitions | dominant pair");
        foreach (TechV2LocalFalsePositiveMap map in maps.Where(map => map.Resolved))
        foreach (TechV2SustainedRunForensic run in map.SustainedRuns)
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  {0} | {1} | {2:F0}->{3:F0} | {4} | {5:F3}/{6:F3}/{7:F3}/{8:F3} | {9}/{10} | {11:F2}/{12:F2} | {13}",
                map.Reference.Key, map.Reference.HumanTruth, run.StartTime, run.EndTime, run.WindowCount,
                run.AverageGeometry, run.AverageMovement, run.AverageSlider, run.AverageMotif,
                run.MinimumSecondBestFamily, run.MaximumSecondBestFamily,
                run.AverageStructuralPivotCount, run.AverageValidTransitionCount, run.DominantPair));
    }

    private static void AppendSustainedDistribution(StringBuilder text, string title, IReadOnlyList<TechV2LocalFalsePositiveMap> maps, TechV2HumanTruth truth)
    {
        IReadOnlyList<TechV2SustainedRunForensic> runs = maps.Where(map => map.Resolved && map.Reference.HumanTruth == truth)
            .SelectMany(map => map.SustainedRuns).ToArray();
        text.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "{0} | runs={1} | avg windows={2:F2} | avg G/M/S/Mo={3:F3}/{4:F3}/{5:F3}/{6:F3} | avg pivots/transitions={7:F2}/{8:F2}",
            title, runs.Count, runs.Count == 0 ? 0 : runs.Average(run => run.WindowCount),
            runs.Count == 0 ? 0 : runs.Average(run => run.AverageGeometry), runs.Count == 0 ? 0 : runs.Average(run => run.AverageMovement),
            runs.Count == 0 ? 0 : runs.Average(run => run.AverageSlider), runs.Count == 0 ? 0 : runs.Average(run => run.AverageMotif),
            runs.Count == 0 ? 0 : runs.Average(run => run.AverageStructuralPivotCount),
            runs.Count == 0 ? 0 : runs.Average(run => run.AverageValidTransitionCount)));
    }

    private static void AppendGuardResults(StringBuilder text, string title, IReadOnlyList<TechV2GuardAuditResult> results)
    {
        text.AppendLine(title + " | candidate | Tech kept/removed | Non-Tech kept/removed | GLORIOUS/Kira/Frozen removed");
        foreach (TechV2GuardAuditResult result in results)
            text.AppendLine($"  {result.Name} | {result.TechKept}/{result.TechRemoved} | {result.NonTechKept}/{result.NonTechRemoved} | {result.GloriousDaysRemoved}/{result.KiraRemoved}/{result.FrozenRemoved}");
    }

    private static bool IsMovementSliderPair(TechV2StrongTechWindowDetail detail) =>
        (detail.BestFamily == "Movement" && detail.SecondBestFamily == "Slider")
        || (detail.BestFamily == "Slider" && detail.SecondBestFamily == "Movement");
    private static string Pair(TechV2StrongTechWindowDetail detail) => $"{detail.BestFamily}+{detail.SecondBestFamily}";
    private static string Pairs(IEnumerable<TechV2StrongTechWindowDetail> details) => string.Join(", ", details.GroupBy(Pair).OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal).Select(group => $"{group.Key}={group.Count()}"));
    private static string DescribeStrongCause(TechV2StrongWindowForensic window) =>
        IsMovementSliderPair(window.Detail) && window.Detail.Support.StructuralPivotCount < 3 ? "Movement+Slider weak structure"
        : window.Detail.Slider >= .75 && window.Detail.Geometry < .25 ? "slider saturation, low geometry"
        : window.Detail.BestFamily == "Movement" && window.Detail.Geometry < .30 ? "movement ratio inflation"
        : "multi-family local evidence";
    private static string DescribeSustainedCause(TechV2SustainedRunForensic? run) => run is null ? "none"
        : run.AverageGeometry < .20 ? "sustained moderate evidence, low geometry"
        : "sustained multi-signal evidence";
    private static string ClassifyArchetype(TechV2StrongWindowForensic? strong, TechV2SustainedRunForensic? sustained) => strong is not null
        ? DescribeStrongCause(strong)
        : DescribeSustainedCause(sustained);
    private enum Branch { Strong, Sustained }
}

internal sealed record TechV2StrongWindowForensic(TechV2StrongTechWindowDetail Detail, double RawScore);
internal sealed record TechV2SustainedRunForensic(double StartTime, double EndTime, int WindowCount, double AverageGeometry, double AverageMovement, double AverageSlider, double AverageMotif, string MinimumSecondBestFamily, string MaximumSecondBestFamily, double AverageStructuralPivotCount, double AverageValidTransitionCount, string DominantPair);
internal sealed record TechV2LocalFalsePositiveMap(TechV2HumanReference Reference, bool Resolved, string ResolutionNote, TechV2PresenceResult Presence, double StrongRegionPresencePercent, double PureSustainedRegionPresencePercent, double StrongPlusSustainedRegionPresencePercent, IReadOnlyList<TechV2StrongWindowForensic> StrongWindows, IReadOnlyList<TechV2SustainedRunForensic> SustainedRuns)
{
    internal double StrongContainingRegionPresencePercent => StrongRegionPresencePercent + StrongPlusSustainedRegionPresencePercent;
    internal static TechV2LocalFalsePositiveMap Unresolved(TechV2HumanReference reference, string note) => new(reference, false, note, new([], 0, 0, 0, 0, 0, 0), 0, 0, 0, [], []);
}
internal sealed record TechV2GuardAuditResult(string Name, object Branch, int TechKept, int TechRemoved, int NonTechKept, int NonTechRemoved, int GloriousDaysRemoved, int KiraRemoved, int FrozenRemoved);
internal sealed record TechV2LocalFalsePositiveReport(IReadOnlyList<TechV2LocalFalsePositiveMap> Maps, IReadOnlyList<TechV2GuardAuditResult> StrongGuardResults, IReadOnlyList<TechV2GuardAuditResult> SustainedGuardResults);
