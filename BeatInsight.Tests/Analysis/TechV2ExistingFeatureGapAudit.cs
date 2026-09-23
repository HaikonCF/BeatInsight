using BeatInsight.Analysis;
using BeatInsight.Models;
using BeatInsight.Parser;
using System.Globalization;
using System.Text;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Phase 2.13 test-only reconstruction of already-existing deterministic
/// signals. It deliberately does not add a property to any runtime result.
/// </summary>
internal static class TechV2ExistingFeatureGapAudit
{
    private static readonly IReadOnlySet<string> IncludedKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "Exit", "Kira", "Frozen", "WorldEndsNow", "GloriousDays",
        "Inferno", "Howling", "TaoHuaXiao", "RockOnExpert", "TrustInYou",
        "HappppySong", "RockOnSeAqours", "FinestSky",
    };

    private static readonly IReadOnlyList<TechV2SignalInventoryItem> Inventory =
    [
        new("Sharp", "GameplayAnalyzer.AnalyzeTech", "compact turn >=150°", false, true),
        new("Structural", "GameplayAnalyzer.AnalyzeTech", "turn >=110° among valid compact transitions", false, true),
        new("Compact", "GameplayAnalyzer.AnalyzeTech", "both branch distances <=110 px", false, true),
        new("Alternating", "GameplayAnalyzer.AnalyzeTech", "two continuous structural turns with <=220 ms intervals", false, true),
        new("Overlap", "GameplayAnalyzer.CountSliderSpatialOverlaps", "slider anchors <=35 px within 700 ms", false, true),
        new("SpacingVariation", "TechDetectorV2Experimental", "branch distance delta >=35 px", true, false),
        new("Awkward", "TechDetectorV2Experimental", "45°..150° turn with compact average spacing", true, false),
        new("RhythmIrregularity", "TechDetectorV2Experimental", "mean relative adjacent-interval variation", true, false),
        new("RhythmRegularity", "derived from RhythmIrregularity", "1 - existing irregularity", true, false),
        new("ComplexSliderRatio", "TechDetectorV2Experimental", "complex sliders / sliders", true, false),
        new("SliderTopology", "TechDetectorV2Experimental", "S>S, S>C, C>S, C>C transition proportions", true, false),
        new("ComplexSliderRun", "TechDetectorV2Experimental", "max consecutive complex sliders", true, false),
        new("AngleSupport", "TechDetectorV2Experimental", "structural-angle coherence multiplied by pivot support", true, false),
        new("StructuralPivotCount", "TechDetectorV2Experimental", "count of valid structural pivots", true, false),
        new("CoupledMotif", "TechDetectorV2Experimental", "longest coupled structural/slider/rhythm local motif", true, false),
        new("LocalDensity", "derived from existing HitObjects", "12 objects over V2 window duration", false, true),
        new("ObjectTypeComposition", "derived from existing HitObjects", "circle/slider composition within V2 window", false, true),
    ];

    internal static TechV2ExistingFeatureGapReport Run(string databasePath)
    {
        TechV2HumanGroundedAuditReport baseReport = TechV2HumanGroundedStrongTechAudit.Run(databasePath);
        IReadOnlyList<TechV2ExistingFeatureMap> maps = baseReport.Maps.Where(map => IncludedKeys.Contains(map.Reference.Key))
            .Select(AnalyzeMap).ToArray();
        IReadOnlyList<TechV2FeatureSample> strong = maps.Where(map => map.Resolved).SelectMany(map => map.Strong).ToArray();
        IReadOnlyList<TechV2FeatureSample> sustained = maps.Where(map => map.Resolved).SelectMany(map => map.SustainedRuns).ToArray();
        return new TechV2ExistingFeatureGapReport(maps, Inventory,
            BuildComparison(strong), BuildComparison(sustained),
            SimulatePairGuards(strong, "Strong"), SimulatePairGuards(sustained, "Sustained"));
    }

    internal static string FormatReport(TechV2ExistingFeatureGapReport report)
    {
        StringBuilder text = new();
        text.AppendLine("===== TECH V2 PHASE 2.13 EXISTING FEATURE GAP AUDIT (TEST-ONLY) =====");
        text.AppendLine("SIGNAL INVENTORY | signal | source | meaning | per-window | recomputation");
        foreach (TechV2SignalInventoryItem item in report.Inventory)
            text.AppendLine($"  {item.Name} | {item.Source} | {item.Meaning} | {item.AvailablePerWindow} | {item.RequiresTestOnlyRecomputation}");

        AppendComparison(text, "STRONGTECH", report.StrongComparison);
        AppendComparison(text, "SUSTAINEDTECH RUNS", report.SustainedComparison);
        AppendPairGuards(text, "OFFLINE STRONGTECH PAIRWISE CANDIDATES", report.StrongPairs);
        AppendPairGuards(text, "OFFLINE SUSTAINEDTECH PAIRWISE CANDIDATES", report.SustainedPairs);

        text.AppendLine("MAXIMUM THREE CANDIDATES PER BRANCH (offline observations only):");
        text.AppendLine("  Strong 1: SharpRatio + OverlapPerSlider — sharp directional changes coupled to local slider interaction.");
        text.AppendLine("  Strong 2: SharpRatio + StructuralRatio — repeated sharpness with structural turns, independent of raw slider amount.");
        text.AppendLine("  Strong 3: StructuralRatio + AngleSupport — conservative existing structural baseline; check weak Tech loss.");
        text.AppendLine("  Sustained 1: SpacingVariation + StructuralRatio — sustained spatial variation with structural turns.");
        text.AppendLine("  Sustained 2: SharpRatio + SpacingVariation — durable sharp directional variation rather than homogeneous movement.");
        text.AppendLine("  Sustained 3: SpacingVariation + AngleSupport — conservative variation/support baseline; current overlap may remain high.");
        text.AppendLine("GUARDRAIL: GLORIOUS DAYS, Kira, and Frozen are reported in every pairwise candidate result.");
        return text.ToString();
    }

    private static TechV2ExistingFeatureMap AnalyzeMap(TechV2HumanGroundedMap map)
    {
        if (!map.Resolved || map.Path is null)
            return TechV2ExistingFeatureMap.Unresolved(map.Reference, map.ResolutionNote);
        Beatmap beatmap = BeatmapParser.Load(map.Path);
        IReadOnlyList<TechV2ExperimentalWindowResult> windows = TechDetectorV2Experimental.Analyze(
            beatmap.HitObjects, GameplayAnalyzer.IsSpinner, GameplayAnalyzer.IsSlider,
            GameplayAnalyzer.IsComplexSlider, GameplayAnalyzer.GetObjectStartPosition,
            GameplayAnalyzer.GetObjectEndPosition, GameplayAnalyzer.GetTechTurnAngle);
        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> sources = windows.ToDictionary(window => window.StartIndex);
        IReadOnlyDictionary<int, TechV2ExistingFeatureMetrics> metrics = windows.ToDictionary(window => window.StartIndex,
            window => ComputeMetrics(beatmap.HitObjects, window));
        IReadOnlyList<TechV2FeatureSample> strong = map.Decisions.Where(decision => decision.IsStrongTech)
            .Select(decision => new TechV2FeatureSample(map.Reference.Key, map.Reference.HumanTruth,
                decision.StartTime, decision.EndTime, 1, metrics[decision.StartIndex], sources[decision.StartIndex])).ToArray();
        IReadOnlyList<TechV2FeatureSample> sustained = BuildSustainedRuns(map.Reference, map.Decisions, metrics, sources);
        return new TechV2ExistingFeatureMap(map.Reference, true, map.ResolutionNote, strong, sustained);
    }

    private static IReadOnlyList<TechV2FeatureSample> BuildSustainedRuns(
        TechV2HumanReference reference, IReadOnlyList<TechV2WindowDecision> decisions,
        IReadOnlyDictionary<int, TechV2ExistingFeatureMetrics> metrics,
        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> sources)
    {
        List<TechV2FeatureSample> runs = [];
        List<TechV2WindowDecision> active = [];
        foreach (TechV2WindowDecision decision in decisions.OrderBy(item => item.StartIndex))
        {
            bool continuous = active.Count > 0 && decision.IsSustainedTech
                && decision.StartIndex == active[^1].StartIndex + TechDetectorV2Experimental.Stride
                && decision.StartTime <= active[^1].EndTime;
            if (decision.IsSustainedTech && (active.Count == 0 || continuous))
            {
                active.Add(decision);
                continue;
            }
            AddRun(runs, reference, active, metrics, sources);
            active.Clear();
            if (decision.IsSustainedTech)
                active.Add(decision);
        }
        AddRun(runs, reference, active, metrics, sources);
        return runs;
    }

    private static void AddRun(List<TechV2FeatureSample> runs, TechV2HumanReference reference,
        IReadOnlyList<TechV2WindowDecision> decisions,
        IReadOnlyDictionary<int, TechV2ExistingFeatureMetrics> metrics,
        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> sources)
    {
        if (decisions.Count == 0)
            return;
        runs.Add(new TechV2FeatureSample(reference.Key, reference.HumanTruth,
            decisions[0].StartTime, decisions[^1].EndTime, decisions.Count,
            Average(decisions.Select(decision => metrics[decision.StartIndex])),
            Average(decisions.Select(decision => sources[decision.StartIndex]))));
    }

    private static TechV2ExistingFeatureMetrics ComputeMetrics(IReadOnlyList<HitObject> objects, TechV2ExperimentalWindowResult window)
    {
        int valid = 0, structural = 0, sharp = 0, compact = 0, alternating = 0;
        int awkward = 0, spacing = 0;
        for (int index = window.StartIndex + 1; index <= window.EndIndex - 1; index++)
        {
            HitObject previous = objects[index - 1], current = objects[index], next = objects[index + 1];
            if (GameplayAnalyzer.IsSpinner(previous) || GameplayAnalyzer.IsSpinner(current) || GameplayAnalyzer.IsSpinner(next)) continue;
            double firstTime = current.Time - previous.Time, secondTime = next.Time - current.Time;
            if (firstTime <= 0 || secondTime <= 0) continue;
            double firstDistance = Distance(GameplayAnalyzer.GetObjectEndPosition(previous), GameplayAnalyzer.GetObjectStartPosition(current));
            double secondDistance = Distance(GameplayAnalyzer.GetObjectEndPosition(current), GameplayAnalyzer.GetObjectStartPosition(next));
            if (firstDistance > 160 || secondDistance > 160) continue;
            valid++;
            double angle = GameplayAnalyzer.GetTechTurnAngle(previous, current, next);
            double averageDistance = (firstDistance + secondDistance) / 2;
            bool isStructural = angle >= 110;
            if (isStructural) structural++;
            if (angle >= 150 && averageDistance <= 125) sharp++;
            if (firstDistance <= 110 && secondDistance <= 110) compact++;
            if (angle >= 45 && angle <= 150 && averageDistance <= 125) awkward++;
            if (Math.Abs(firstDistance - secondDistance) >= 35) spacing++;
            if (index >= window.StartIndex + 2)
            {
                HitObject previousPrevious = objects[index - 2];
                double previousTime = previous.Time - previousPrevious.Time;
                if (!GameplayAnalyzer.IsSpinner(previousPrevious) && previousTime > 0 && previousTime <= 220
                    && firstTime <= 220 && secondTime <= 220
                    && GameplayAnalyzer.GetTechTurnAngle(previousPrevious, previous, current) >= 110 && isStructural)
                    alternating++;
            }
        }
        IReadOnlyList<HitObject> local = objects.Skip(window.StartIndex).Take(TechDetectorV2Experimental.WindowSize).ToArray();
        IReadOnlyList<HitObject> sliders = local.Where(GameplayAnalyzer.IsSlider).ToArray();
        int overlap = CountLocalOverlaps(sliders);
        int circles = local.Count(item => !GameplayAnalyzer.IsSlider(item) && !GameplayAnalyzer.IsSpinner(item));
        double durationSeconds = Math.Max(.001, (window.EndTime - window.StartTime) / 1000);
        return new TechV2ExistingFeatureMetrics(
            Ratio(structural, valid), Ratio(sharp, valid), Ratio(compact, valid), Ratio(alternating, valid),
            Ratio(awkward, valid), Ratio(spacing, valid),
            sliders.Count == 0 ? 0 : (double)overlap / sliders.Count,
            sliders.Count == 0 ? 0 : (double)sliders.Count / local.Count,
            (double)circles / local.Count,
            local.Count / durationSeconds);
    }

    private static int CountLocalOverlaps(IReadOnlyList<HitObject> sliders)
    {
        int count = 0;
        for (int first = 0; first < sliders.Count; first++)
        for (int second = first + 1; second < sliders.Count; second++)
        {
            if (sliders[second].Time - sliders[first].Time > 700) break;
            if (sliders[first].SliderControlPoints.Any(a => sliders[second].SliderControlPoints.Any(b => Distance((a.X, a.Y), (b.X, b.Y)) <= 35))) count++;
        }
        return count;
    }

    private static IReadOnlyList<TechV2SignalComparison> BuildComparison(IReadOnlyList<TechV2FeatureSample> samples) => SignalGetters.Select(signal =>
        new TechV2SignalComparison(signal.Name,
            Stats(samples.Where(sample => sample.Truth == TechV2HumanTruth.Tech).Select(signal.Value)),
            Stats(samples.Where(sample => sample.Truth == TechV2HumanTruth.NonTech).Select(signal.Value)))).ToArray();

    private static IReadOnlyList<TechV2PairGuardResult> SimulatePairGuards(IReadOnlyList<TechV2FeatureSample> samples, string branch) =>
        branch == "Strong"
            ?
            [
                SimulatePair("SharpRatio + OverlapPerSlider", samples, sample => sample.Metrics.SharpRatio >= .15 && sample.Metrics.OverlapPerSlider >= .08),
                SimulatePair("SharpRatio + StructuralRatio", samples, sample => sample.Metrics.SharpRatio >= .15 && sample.Metrics.StructuralRatio >= .30),
                SimulatePair("StructuralRatio + AngleSupport", samples, sample => sample.Metrics.StructuralRatio >= .30 && sample.Window.AngleCoherenceWithSupport >= .20),
            ]
            :
            [
                SimulatePair("SpacingVariation + StructuralRatio", samples, sample => sample.Window.SpacingVariation >= .50 && sample.Metrics.StructuralRatio >= .30),
                SimulatePair("SharpRatio + SpacingVariation", samples, sample => sample.Metrics.SharpRatio >= .15 && sample.Window.SpacingVariation >= .50),
                SimulatePair("SpacingVariation + AngleSupport", samples, sample => sample.Window.SpacingVariation >= .50 && sample.Window.AngleCoherenceWithSupport >= .20),
            ];

    private static TechV2PairGuardResult SimulatePair(string name, IReadOnlyList<TechV2FeatureSample> samples, Func<TechV2FeatureSample, bool> keeps) => new(
        name,
        samples.Count(sample => sample.Truth == TechV2HumanTruth.Tech && keeps(sample)),
        samples.Count(sample => sample.Truth == TechV2HumanTruth.Tech && !keeps(sample)),
        samples.Count(sample => sample.Truth == TechV2HumanTruth.NonTech && keeps(sample)),
        samples.Count(sample => sample.Truth == TechV2HumanTruth.NonTech && !keeps(sample)),
        samples.Count(sample => sample.MapKey == "GloriousDays" && !keeps(sample)),
        samples.Count(sample => sample.MapKey == "Kira" && !keeps(sample)),
        samples.Count(sample => sample.MapKey == "Frozen" && !keeps(sample)));

    private static void AppendComparison(StringBuilder text, string title, IReadOnlyList<TechV2SignalComparison> comparisons)
    {
        text.AppendLine(title + " SIGNAL COMPARISON | signal | Tech mean/median/min/max | Non-Tech mean/median/min/max | overlap");
        foreach (TechV2SignalComparison comparison in comparisons)
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  {0} | {1} | {2} | {3}", comparison.Name, Format(comparison.Tech), Format(comparison.NonTech), Overlap(comparison.Tech, comparison.NonTech)));
    }
    private static void AppendPairGuards(StringBuilder text, string title, IReadOnlyList<TechV2PairGuardResult> results)
    {
        text.AppendLine(title + " | candidate | Tech kept/removed | Non-Tech kept/removed | GLORIOUS/Kira/Frozen removed");
        foreach (TechV2PairGuardResult result in results)
            text.AppendLine($"  {result.Name} | {result.TechKept}/{result.TechRemoved} | {result.NonTechKept}/{result.NonTechRemoved} | {result.GloriousRemoved}/{result.KiraRemoved}/{result.FrozenRemoved}");
    }

    private static readonly IReadOnlyList<(string Name, Func<TechV2FeatureSample, double> Value)> SignalGetters =
    [
        ("StructuralRatio", s => s.Metrics.StructuralRatio), ("SharpRatio", s => s.Metrics.SharpRatio), ("CompactRatio", s => s.Metrics.CompactRatio),
        ("AlternatingRatio", s => s.Metrics.AlternatingRatio), ("OverlapPerSlider", s => s.Metrics.OverlapPerSlider),
        ("SliderDensity", s => s.Metrics.SliderDensity), ("LocalDensity", s => s.Metrics.LocalDensity),
        ("AngleSupport", s => s.Window.AngleCoherenceWithSupport), ("StructuralPivots", s => s.Window.AngleCoherenceStructuralPivotCount),
        ("RhythmIrregularity", s => s.Window.RhythmIrregularity), ("RhythmRegularity", s => 1 - s.Window.RhythmIrregularity),
        ("SliderComplexity", s => s.Window.SliderComplexity), ("TopologyVariation", s => TopologyVariation(s.Window)),
        ("ComplexSliderRun", s => s.Window.ComplexSliderRunLength), ("CoupledMotif", s => s.Window.CoupledTechnicalMotifContinuity),
        ("Awkward", s => s.Window.Awkwardness), ("SpacingVariation", s => s.Window.SpacingVariation),
    ];
    private static TechV2FeatureStats Stats(IEnumerable<double> values)
    {
        double[] data = values.OrderBy(value => value).ToArray();
        return data.Length == 0 ? new(0, 0, 0, 0) : new(data.Average(), data[data.Length / 2], data[0], data[^1]);
    }
    private static string Format(TechV2FeatureStats stats) => string.Format(CultureInfo.InvariantCulture, "{0:F3}/{1:F3}/{2:F3}/{3:F3}", stats.Mean, stats.Median, stats.Min, stats.Max);
    private static string Overlap(TechV2FeatureStats first, TechV2FeatureStats second) => string.Format(CultureInfo.InvariantCulture, "{0:F3}..{1:F3}", Math.Max(first.Min, second.Min), Math.Min(first.Max, second.Max));
    private static double Ratio(int numerator, int denominator) => denominator == 0 ? 0 : (double)numerator / denominator;
    private static double Distance((double X, double Y) first, (double X, double Y) second) => Math.Sqrt(Math.Pow(second.X - first.X, 2) + Math.Pow(second.Y - first.Y, 2));
    private static double TopologyVariation(TechV2ExperimentalWindowResult window) => 1 - new[] { window.ComplexSliderTransitionTopology.SliderToSliderRatio, window.ComplexSliderTransitionTopology.SliderToCircleRatio, window.ComplexSliderTransitionTopology.CircleToSliderRatio, window.ComplexSliderTransitionTopology.CircleToCircleRatio }.Max();
    private static TechV2ExistingFeatureMetrics Average(IEnumerable<TechV2ExistingFeatureMetrics> values) { TechV2ExistingFeatureMetrics[] data = values.ToArray(); return new(data.Average(x => x.StructuralRatio), data.Average(x => x.SharpRatio), data.Average(x => x.CompactRatio), data.Average(x => x.AlternatingRatio), data.Average(x => x.AwkwardRatio), data.Average(x => x.SpacingVariationRatio), data.Average(x => x.OverlapPerSlider), data.Average(x => x.SliderDensity), data.Average(x => x.CircleDensity), data.Average(x => x.LocalDensity)); }
    private static TechV2ExperimentalWindowResult Average(IEnumerable<TechV2ExperimentalWindowResult> values) { TechV2ExperimentalWindowResult[] d = values.ToArray(); TechV2ExperimentalWindowResult f = d[0]; return f with { RhythmIrregularity = d.Average(x => x.RhythmIrregularity), SliderComplexity = d.Average(x => x.SliderComplexity), Awkwardness = d.Average(x => x.Awkwardness), SpacingVariation = d.Average(x => x.SpacingVariation), AngleCoherenceWithSupport = d.Average(x => x.AngleCoherenceWithSupport), AngleCoherenceStructuralPivotCount = (int)Math.Round(d.Average(x => x.AngleCoherenceStructuralPivotCount)), ComplexSliderRunLength = (int)Math.Round(d.Average(x => x.ComplexSliderRunLength)), CoupledTechnicalMotifContinuity = d.Average(x => x.CoupledTechnicalMotifContinuity), ComplexSliderTransitionTopology = new(f.ComplexSliderTransitionTopology.TransitionCount, d.Average(x => x.ComplexSliderTransitionTopology.SliderToSliderRatio), d.Average(x => x.ComplexSliderTransitionTopology.SliderToCircleRatio), d.Average(x => x.ComplexSliderTransitionTopology.CircleToSliderRatio), d.Average(x => x.ComplexSliderTransitionTopology.CircleToCircleRatio)) }; }
}

internal sealed record TechV2SignalInventoryItem(string Name, string Source, string Meaning, bool AvailablePerWindow, bool RequiresTestOnlyRecomputation);
internal sealed record TechV2ExistingFeatureMetrics(double StructuralRatio, double SharpRatio, double CompactRatio, double AlternatingRatio, double AwkwardRatio, double SpacingVariationRatio, double OverlapPerSlider, double SliderDensity, double CircleDensity, double LocalDensity);
internal sealed record TechV2FeatureSample(string MapKey, TechV2HumanTruth Truth, double StartTime, double EndTime, int WindowCount, TechV2ExistingFeatureMetrics Metrics, TechV2ExperimentalWindowResult Window);
internal sealed record TechV2ExistingFeatureMap(TechV2HumanReference Reference, bool Resolved, string ResolutionNote, IReadOnlyList<TechV2FeatureSample> Strong, IReadOnlyList<TechV2FeatureSample> SustainedRuns) { internal static TechV2ExistingFeatureMap Unresolved(TechV2HumanReference reference, string note) => new(reference, false, note, [], []); }
internal sealed record TechV2FeatureStats(double Mean, double Median, double Min, double Max);
internal sealed record TechV2SignalComparison(string Name, TechV2FeatureStats Tech, TechV2FeatureStats NonTech);
internal sealed record TechV2PairGuardResult(string Name, int TechKept, int TechRemoved, int NonTechKept, int NonTechRemoved, int GloriousRemoved, int KiraRemoved, int FrozenRemoved);
internal sealed record TechV2ExistingFeatureGapReport(IReadOnlyList<TechV2ExistingFeatureMap> Maps, IReadOnlyList<TechV2SignalInventoryItem> Inventory, IReadOnlyList<TechV2SignalComparison> StrongComparison, IReadOnlyList<TechV2SignalComparison> SustainedComparison, IReadOnlyList<TechV2PairGuardResult> StrongPairs, IReadOnlyList<TechV2PairGuardResult> SustainedPairs);
