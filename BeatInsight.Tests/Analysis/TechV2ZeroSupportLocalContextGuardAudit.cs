using BeatInsight.Analysis;
using BeatInsight.Models;
using BeatInsight.Models.Persistence;
using BeatInsight.Parser;
using System.Globalization;
using System.IO;
using System.Text;

namespace BeatInsight.Tests.Analysis;

internal enum TechV2Phase223GuardVariant
{
    BaselineD,
    GCTX_A,
    GCTX_B,
    GCTX_C,
}

internal enum TechV2Phase223Recommendation
{
    SAFE_TO_ENABLE,
    NEEDS_MORE_CALIBRATION,
    REJECT,
}

internal sealed record TechV2Phase223Distribution(
    TechV2Phase223GuardVariant Variant,
    int Count,
    double AverageDelta,
    double MedianDelta,
    double MinDelta,
    double MaxDelta,
    double MaxAbsoluteDelta,
    double PercentageIncreased,
    double PercentageDecreased);

internal sealed record TechV2Phase223HumanImpact(
    TechV2Phase223GuardVariant Variant,
    string CaseId,
    double BaselineD,
    double Guarded,
    double DeltaFromD,
    bool UsefulGainKept);

internal sealed record TechV2Phase223ExtremeReduction(
    TechV2Phase223GuardVariant Variant,
    string Map,
    string Difficulty,
    double Timestamp,
    double BaselineDelta,
    double GuardDelta,
    double Reduction,
    TechV2ShadowSupportCounts SupportCounts);

internal sealed record TechV2Phase223VariantComparison(
    TechV2Phase223GuardVariant Variant,
    double MaxAbsDelta,
    int ExtremeAbovePoint30Count,
    int HumanUsefulGainsKept,
    int HumanUsefulGainsTotal,
    string Verdict);

internal sealed record TechV2Phase223AuditReport(
    IReadOnlyList<TechV2Phase223VariantComparison> VariantComparison,
    IReadOnlyList<TechV2Phase223HumanImpact> HumanCorpusImpact,
    IReadOnlyList<TechV2Phase223Distribution> ThousandMapDeltaDistribution,
    IReadOnlyList<TechV2Phase223ExtremeReduction> ExtremeDeltaReduction,
    TechV2Phase223GuardVariant BestGuardCandidate,
    TechV2Phase223Recommendation Recommendation,
    bool AllDecisionsUnchanged);

internal static class TechV2ZeroSupportLocalContextGuardAudit
{
    private const double Epsilon = 1e-6;

    private static readonly string[] TargetCases =
    [
        "Exit R5",
        "WorldEndsNow R1",
        "TrustInYou R1",
        "Inferno R1",
        "TaoHuaXiao R1",
        "HappppySong R1",
        "HappppySong R2",
        "Kira R1",
        "Frozen R1",
        "GloriousDays R1",
    ];

    private static readonly string[] ExtremeMapTokens =
    [
        "SANTA SAN",
        "AliVe",
        "Ringing Bloom",
        "Koinu",
        "Don't Let Go",
        "Start Again",
        "Yuuki wa Doko ni?",
        "Deai Machi",
        "More-more LOVERS!!",
    ];

    internal static TechV2Phase223AuditReport Run(string databasePath)
    {
        TechV2Phase222AuditReport phase222 = TechV2CombinedCorrectionShadowRuntimeAudit.Run(databasePath);
        IReadOnlyList<MlDatasetSample> samples = TechV2StrongTechForensicAudit.LoadLocalCacheSamples(databasePath);

        IReadOnlyList<ShadowWindowWithMap> allWindows = AnalyzeDeterministicSample(samples, maxMaps: 1000);
        bool allDecisionsUnchanged = allWindows.All(item =>
            string.Equals(item.Window.DecisionKindBefore, item.Window.DecisionKindAfter, StringComparison.Ordinal));

        IReadOnlyList<TechV2Phase223Distribution> distributions =
        [
            BuildDistribution(allWindows, TechV2Phase223GuardVariant.BaselineD),
            BuildDistribution(allWindows, TechV2Phase223GuardVariant.GCTX_A),
            BuildDistribution(allWindows, TechV2Phase223GuardVariant.GCTX_B),
            BuildDistribution(allWindows, TechV2Phase223GuardVariant.GCTX_C),
        ];

        IReadOnlyList<TechV2Phase223HumanImpact> humanImpact = BuildHumanImpact(databasePath);
        IReadOnlyList<TechV2Phase223ExtremeReduction> extremeReduction = BuildExtremeReduction(phase222, allWindows);

        IReadOnlyList<TechV2Phase223VariantComparison> comparison = BuildVariantComparison(
            distributions,
            humanImpact,
            extremeReduction);

        TechV2Phase223GuardVariant best = comparison
            .OrderByDescending(item => VariantScore(item))
            .ThenBy(item => item.Variant.ToString(), StringComparer.Ordinal)
            .First().Variant;

        TechV2Phase223Recommendation recommendation = BuildRecommendation(comparison, allDecisionsUnchanged, best);

        return new TechV2Phase223AuditReport(
            comparison,
            humanImpact,
            distributions,
            extremeReduction,
            best,
            recommendation,
            allDecisionsUnchanged);
    }

    internal static string FormatReport(TechV2Phase223AuditReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder text = new();
        text.AppendLine("===== TECH V2 PHASE 2.23 ZERO-SUPPORT LOCAL CONTEXT GUARD AUDIT (TEST-ONLY/SHADOW-ONLY) =====");
        text.AppendLine("SA-LAPLACE-A1-P0.5 inchangé. Seule l'agrégation locale PREV+CURR est gardée offline.");
        text.AppendLine("Aucun changement de décision runtime.");

        text.AppendLine("VARIANT COMPARISON");
        foreach (TechV2Phase223VariantComparison item in report.VariantComparison)
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0} | maxAbs={1:F4} | peaks>0.30={2} | humanKept={3}/{4} | {5}",
                item.Variant,
                item.MaxAbsDelta,
                item.ExtremeAbovePoint30Count,
                item.HumanUsefulGainsKept,
                item.HumanUsefulGainsTotal,
                item.Verdict));
        }

        text.AppendLine("HUMAN CORPUS IMPACT");
        foreach (TechV2Phase223HumanImpact item in report.HumanCorpusImpact)
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0} {1} | D={2:F4} | guarded={3:F4} | delta={4:+0.0000;-0.0000;0.0000} | keep={5}",
                item.Variant,
                item.CaseId,
                item.BaselineD,
                item.Guarded,
                item.DeltaFromD,
                item.UsefulGainKept));
        }

        text.AppendLine("1000-MAP DELTA DISTRIBUTION");
        foreach (TechV2Phase223Distribution item in report.ThousandMapDeltaDistribution)
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0} | count={1} | avg={2:+0.0000;-0.0000;0.0000} | median={3:+0.0000;-0.0000;0.0000} | min={4:+0.0000;-0.0000;0.0000} | max={5:+0.0000;-0.0000;0.0000} | maxAbs={6:+0.0000;-0.0000;0.0000} | inc={7:F2}% | dec={8:F2}%",
                item.Variant,
                item.Count,
                item.AverageDelta,
                item.MedianDelta,
                item.MinDelta,
                item.MaxDelta,
                item.MaxAbsoluteDelta,
                item.PercentageIncreased,
                item.PercentageDecreased));
        }

        text.AppendLine("EXTREME DELTA REDUCTION (Phase 2.22 top-positive windows)");
        foreach (TechV2Phase223ExtremeReduction item in report.ExtremeDeltaReduction)
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0} | {1} [{2}] {3} | baseline={4:+0.0000;-0.0000;0.0000} | guarded={5:+0.0000;-0.0000;0.0000} | reduction={6:+0.0000;-0.0000;0.0000} | support(valid={7},struct={8},sharp={9},spacing={10},alt={11},topo={12})",
                item.Variant,
                item.Map,
                item.Difficulty,
                FormatTime(item.Timestamp),
                item.BaselineDelta,
                item.GuardDelta,
                item.Reduction,
                item.SupportCounts.ValidTransitions,
                item.SupportCounts.StructuralCount,
                item.SupportCounts.SharpCount,
                item.SupportCounts.SpacingVariationCount,
                item.SupportCounts.AlternatingCount,
                item.SupportCounts.TopologyTransitionCount));
        }

        text.AppendLine($"BEST GUARD CANDIDATE: {report.BestGuardCandidate}");
        text.AppendLine($"RECOMMENDATION: {report.Recommendation}");
        text.AppendLine($"ALL DECISIONS UNCHANGED: {report.AllDecisionsUnchanged}");

        return text.ToString();
    }

    private static IReadOnlyList<ShadowWindowWithMap> AnalyzeDeterministicSample(
        IReadOnlyList<MlDatasetSample> samples,
        int maxMaps)
    {
        IReadOnlyList<string> paths = samples
            .Select(sample => sample.SourceFilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Take(Math.Max(0, maxMaps))
            .ToArray();

        List<ShadowWindowWithMap> all = [];
        foreach (string path in paths)
        {
            Beatmap beatmap;
            try
            {
                beatmap = BeatmapParser.Load(path);
            }
            catch (Exception exception) when (exception is IOException or FormatException or InvalidDataException)
            {
                continue;
            }

            if (beatmap.HitObjects.Count < TechDetectorV2Experimental.WindowSize)
                continue;

            IReadOnlyList<TechV2ExperimentalWindowResult> windows = TechDetectorV2Experimental.Analyze(
                beatmap.HitObjects,
                GameplayAnalyzer.IsSpinner,
                GameplayAnalyzer.IsSlider,
                GameplayAnalyzer.IsComplexSlider,
                GameplayAnalyzer.GetObjectStartPosition,
                GameplayAnalyzer.GetObjectEndPosition,
                GameplayAnalyzer.GetTechTurnAngle);
            IReadOnlyList<TechV2WindowDecision> decisions = TechV2ExperimentalDecisionEngine.PromoteSustainedTech(
                TechV2ExperimentalDecisionEngine.Decide(windows));

            TechV2CombinedCorrectionShadowResult shadow = TechV2CombinedCorrectionShadowRuntime.Compute(
                beatmap.HitObjects,
                windows,
                decisions);

            string map = string.IsNullOrWhiteSpace(beatmap.Title)
                ? Path.GetFileNameWithoutExtension(path)
                : beatmap.Title;
            string difficulty = string.IsNullOrWhiteSpace(beatmap.Version)
                ? "Unknown"
                : beatmap.Version;

            all.AddRange(shadow.Windows.Select(window =>
                new ShadowWindowWithMap(map, difficulty, window)));
        }

        return all;
    }

    private static IReadOnlyList<TechV2Phase223HumanImpact> BuildHumanImpact(string databasePath)
    {
        TechV2HumanGroundedAuditReport grounded = TechV2HumanGroundedStrongTechAudit.Run(databasePath);
        IReadOnlyDictionary<string, TechV2HumanGroundedMap> byKey = grounded.Maps
            .Where(map => map.Resolved && map.Path is not null)
            .ToDictionary(map => map.Reference.Key, StringComparer.Ordinal);

        List<TechV2Phase223HumanImpact> items = [];
        foreach (string caseId in TargetCases)
        {
            if (!TryParseCaseId(caseId, out string mapKey, out int regionNumber))
                continue;
            if (!byKey.TryGetValue(mapKey, out TechV2HumanGroundedMap? map) || map.Path is null)
                continue;

            Beatmap beatmap;
            try
            {
                beatmap = BeatmapParser.Load(map.Path);
            }
            catch (Exception exception) when (exception is IOException or FormatException or InvalidDataException)
            {
                continue;
            }

            IReadOnlyList<TechV2ExperimentalWindowResult> windows = TechDetectorV2Experimental.Analyze(
                beatmap.HitObjects,
                GameplayAnalyzer.IsSpinner,
                GameplayAnalyzer.IsSlider,
                GameplayAnalyzer.IsComplexSlider,
                GameplayAnalyzer.GetObjectStartPosition,
                GameplayAnalyzer.GetObjectEndPosition,
                GameplayAnalyzer.GetTechTurnAngle);
            IReadOnlyList<TechV2WindowDecision> decisions = TechV2ExperimentalDecisionEngine.PromoteSustainedTech(
                TechV2ExperimentalDecisionEngine.Decide(windows));
            TechV2CombinedCorrectionShadowResult shadow = TechV2CombinedCorrectionShadowRuntime.Compute(
                beatmap.HitObjects,
                windows,
                decisions);

            if (regionNumber < 1 || regionNumber > map.Presence.Regions.Count)
                continue;

            TechV2Region region = map.Presence.Regions[regionNumber - 1];
            IReadOnlyList<TechV2ShadowWindowDiagnostics> selected = shadow.Windows
                .Where(window =>
                    window.StartIndex >= region.StartWindowIndex
                    && window.EndIndex <= region.EndWindowIndex)
                .OrderBy(window => window.StartIndex)
                .ToArray();

            if (selected.Count == 0)
                continue;

            double d = selected.Average(window => window.CorrectedCombinedValue);

            foreach (TechV2Phase223GuardVariant variant in new[]
            {
                TechV2Phase223GuardVariant.GCTX_A,
                TechV2Phase223GuardVariant.GCTX_B,
                TechV2Phase223GuardVariant.GCTX_C,
            })
            {
                double guarded = selected.Average(window => ApplyGuard(
                    variant,
                    window.CorrectedCombinedValue,
                    window.CorrectedSupportAwareRatioValue,
                    window.CorrectedLocalContextValue,
                    window.SupportCounts));
                bool usefulKept = Math.Abs(guarded - d) <= 0.05 || guarded >= d - 0.05;
                items.Add(new TechV2Phase223HumanImpact(
                    variant,
                    caseId,
                    d,
                    guarded,
                    guarded - d,
                    usefulKept));
            }
        }

        return items;
    }

    private static IReadOnlyList<TechV2Phase223ExtremeReduction> BuildExtremeReduction(
        TechV2Phase222AuditReport phase222,
        IReadOnlyList<ShadowWindowWithMap> windows)
    {
        List<ShadowWindowWithMap> matching = [];
        foreach (string token in ExtremeMapTokens)
        {
            ShadowWindowWithMap? strongest = windows
                .Where(item => item.Map.Contains(token, StringComparison.OrdinalIgnoreCase))
                .Where(item => item.Window.Delta > 0)
                .OrderByDescending(item => item.Window.Delta)
                .ThenBy(item => item.Map, StringComparer.Ordinal)
                .ThenBy(item => item.Window.StartTime)
                .FirstOrDefault();

            if (strongest is not null)
                matching.Add(strongest);
        }

        Dictionary<(string Map, string Difficulty, double Timestamp), double> baselineDelta = phase222.ExtremePositiveDeltas
            .ToDictionary(item => (item.Map, item.Difficulty, item.Timestamp), item => item.Delta);

        List<TechV2Phase223ExtremeReduction> reductions = [];
        foreach (ShadowWindowWithMap item in matching)
        {
            double before = baselineDelta.TryGetValue((item.Map, item.Difficulty, item.Window.StartTime), out double value)
                ? value
                : item.Window.Delta;
            foreach (TechV2Phase223GuardVariant variant in new[]
            {
                TechV2Phase223GuardVariant.GCTX_A,
                TechV2Phase223GuardVariant.GCTX_B,
                TechV2Phase223GuardVariant.GCTX_C,
            })
            {
                double after = ApplyGuard(
                    variant,
                    item.Window.CorrectedCombinedValue,
                    item.Window.CorrectedSupportAwareRatioValue,
                    item.Window.CorrectedLocalContextValue,
                    item.Window.SupportCounts)
                    - item.Window.CurrentCombinedValue;

                reductions.Add(new TechV2Phase223ExtremeReduction(
                    variant,
                    item.Map,
                    item.Difficulty,
                    item.Window.StartTime,
                    before,
                    after,
                    before - after,
                    item.Window.SupportCounts));
            }
        }

        return reductions
            .OrderBy(item => item.Variant.ToString(), StringComparer.Ordinal)
            .ThenByDescending(item => item.BaselineDelta)
            .ThenBy(item => item.Map, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<TechV2Phase223VariantComparison> BuildVariantComparison(
        IReadOnlyList<TechV2Phase223Distribution> distributions,
        IReadOnlyList<TechV2Phase223HumanImpact> human,
        IReadOnlyList<TechV2Phase223ExtremeReduction> extremes)
    {
        List<TechV2Phase223VariantComparison> items = [];
        foreach (TechV2Phase223GuardVariant variant in new[]
        {
            TechV2Phase223GuardVariant.GCTX_A,
            TechV2Phase223GuardVariant.GCTX_B,
            TechV2Phase223GuardVariant.GCTX_C,
        })
        {
            TechV2Phase223Distribution dist = distributions.Single(item => item.Variant == variant);
            IReadOnlyList<TechV2Phase223HumanImpact> humanForVariant = human
                .Where(item => item.Variant == variant)
                .ToArray();
            int kept = humanForVariant.Count(item => item.UsefulGainKept);
            int total = humanForVariant.Count;

            int peaks = extremes
                .Where(item => item.Variant == variant)
                .Count(item => item.GuardDelta > 0.30);

            string verdict = dist.MaxAbsoluteDelta <= 0.15
                ? "promising-maxabs"
                : peaks == 0 && extremes.Count > 0 && extremes.All(item => item.GuardDelta < item.BaselineDelta - 0.05)
                    ? "promising-peak-reduction"
                    : "calibration-needed";

            items.Add(new TechV2Phase223VariantComparison(
                variant,
                dist.MaxAbsoluteDelta,
                peaks,
                kept,
                total,
                verdict));
        }

        return items;
    }

    private static TechV2Phase223Recommendation BuildRecommendation(
        IReadOnlyList<TechV2Phase223VariantComparison> comparison,
        bool allDecisionsUnchanged,
        TechV2Phase223GuardVariant best)
    {
        if (!allDecisionsUnchanged)
            return TechV2Phase223Recommendation.REJECT;

        TechV2Phase223VariantComparison selected = comparison.Single(item => item.Variant == best);
        if (selected.MaxAbsDelta <= 0.15 && selected.HumanUsefulGainsKept >= Math.Max(1, selected.HumanUsefulGainsTotal - 1))
            return TechV2Phase223Recommendation.SAFE_TO_ENABLE;

        return TechV2Phase223Recommendation.NEEDS_MORE_CALIBRATION;
    }

    private static double ApplyGuard(
        TechV2Phase223GuardVariant variant,
        double baselineCombined,
        double supportAwareOnly,
        double localPrevCurr,
        TechV2ShadowSupportCounts support)
    {
        if (variant == TechV2Phase223GuardVariant.BaselineD)
            return baselineCombined;

        if (variant == TechV2Phase223GuardVariant.GCTX_A)
        {
            if (support.ValidTransitions == 0)
                return supportAwareOnly;
            return baselineCombined;
        }

        if (variant == TechV2Phase223GuardVariant.GCTX_B)
        {
            if (support.ValidTransitions == 0 && support.StructuralCount == 0)
                return supportAwareOnly;
            return baselineCombined;
        }

        double confidence = ComputeCurrentSupportConfidence(support);
        double currentLocalOnly = 2 * baselineCombined - supportAwareOnly;
        double guardedLocal = confidence * localPrevCurr + (1 - confidence) * currentLocalOnly;
        return Math.Clamp((supportAwareOnly + guardedLocal) / 2.0, 0, 1);
    }

    private static double ComputeCurrentSupportConfidence(TechV2ShadowSupportCounts support)
    {
        double numerator = support.ValidTransitions + support.StructuralCount + support.SharpCount + support.SpacingVariationCount + support.AlternatingCount;
        double denominator = numerator + Math.Max(0, support.TopologyTransitionCount);
        if (denominator <= 0)
            return 0;
        return Math.Clamp(numerator / denominator, 0, 1);
    }

    private static bool TryParseCaseId(
        string caseId,
        out string mapKey,
        out int regionNumber)
    {
        mapKey = string.Empty;
        regionNumber = 0;

        int separator = caseId.LastIndexOf(" R", StringComparison.Ordinal);
        if (separator <= 0 || separator >= caseId.Length - 2)
            return false;

        mapKey = caseId[..separator];
        return int.TryParse(caseId[(separator + 2)..], out regionNumber) && regionNumber > 0;
    }

    private static TechV2Phase223Distribution BuildDistribution(
        IReadOnlyList<ShadowWindowWithMap> windows,
        TechV2Phase223GuardVariant variant)
    {
        IReadOnlyList<double> deltas = windows
            .Select(item => ComputeDeltaForVariant(item.Window, variant))
            .OrderBy(value => value)
            .ToArray();

        if (deltas.Count == 0)
            return new TechV2Phase223Distribution(variant, 0, 0, 0, 0, 0, 0, 0, 0);

        int increased = deltas.Count(value => value > Epsilon);
        int decreased = deltas.Count(value => value < -Epsilon);

        return new TechV2Phase223Distribution(
            variant,
            deltas.Count,
            deltas.Average(),
            Median(deltas),
            deltas[0],
            deltas[^1],
            deltas.Max(value => Math.Abs(value)),
            100.0 * increased / deltas.Count,
            100.0 * decreased / deltas.Count);
    }

    private static double ComputeDeltaForVariant(
        TechV2ShadowWindowDiagnostics window,
        TechV2Phase223GuardVariant variant)
    {
        double guarded = ApplyGuard(
            variant,
            window.CorrectedCombinedValue,
            window.CorrectedSupportAwareRatioValue,
            window.CorrectedLocalContextValue,
            window.SupportCounts);

        return guarded - window.CurrentCombinedValue;
    }

    private static double VariantScore(TechV2Phase223VariantComparison item)
    {
        double keepRatio = item.HumanUsefulGainsTotal == 0
            ? 0
            : (double)item.HumanUsefulGainsKept / item.HumanUsefulGainsTotal;
        return keepRatio * 2.0 - item.MaxAbsDelta - item.ExtremeAbovePoint30Count * 0.1;
    }

    private static double Median(IReadOnlyList<double> sorted)
    {
        if (sorted.Count == 0)
            return 0;
        int middle = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2.0;
    }

    private static string FormatTime(double milliseconds)
    {
        TimeSpan value = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}.{2:D3}", (int)value.TotalMinutes, value.Seconds, value.Milliseconds);
    }

    private sealed record ShadowWindowWithMap(
        string Map,
        string Difficulty,
        TechV2ShadowWindowDiagnostics Window);
}
