using BeatInsight.Analysis;
using BeatInsight.Models;
using BeatInsight.Models.Persistence;
using BeatInsight.Parser;
using System.Globalization;
using System.IO;
using System.Text;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Phase 2.25 is deliberately an offline comparison of zero-support ratio
/// policies. It never writes a decision, region, feature, or runtime value.
/// </summary>
internal enum TechV2ZeroSupportVariant
{
    ZERO_A,
    ZERO_B,
    ZERO_C,
    ZERO_D,
}

internal enum TechV2ZeroSupportRecommendation
{
    SAFE_TO_ENABLE,
    NEEDS_MORE_CALIBRATION,
    REJECT,
}

internal sealed record TechV2Phase225Distribution(
    TechV2ZeroSupportVariant Variant,
    int Count,
    double AverageDelta,
    double MedianDelta,
    double MinDelta,
    double MaxDelta,
    double MaxAbsDelta,
    double PercentIncreased,
    double PercentDecreased,
    int Over015,
    int Over020,
    int Over030,
    bool AllDecisionsUnchanged,
    int ChangedDecisionCount);

internal sealed record TechV2Phase225SourceAttribution(
    double TotalDelta,
    double DeltaFromSupportAware,
    double DeltaFromPrevContext,
    double DeltaFromTopology,
    double DeltaFromOther);

internal sealed record TechV2Phase225RatioAdjustment(
    string Name,
    int Numerator,
    int Denominator,
    double RawRatio,
    double LaplaceRatio,
    double SupportFactor,
    double EffectiveRatio,
    double FinalContribution);

internal sealed record TechV2Phase225ExtremeResult(
    string Map,
    string Difficulty,
    double Timestamp,
    double Current,
    double BaselineShadowD,
    double GctxCCorrected,
    IReadOnlyDictionary<TechV2ZeroSupportVariant, double> Corrected,
    IReadOnlyDictionary<TechV2ZeroSupportVariant, TechV2Phase225SourceAttribution> Attribution,
    IReadOnlyDictionary<TechV2ZeroSupportVariant, IReadOnlyList<TechV2Phase225RatioAdjustment>> Ratios);

internal sealed record TechV2Phase225HumanImpact(
    string CaseId,
    TechV2ZeroSupportVariant Variant,
    double Current,
    double Original,
    double Corrected,
    double Delta,
    string GainStatus);

internal sealed record TechV2Phase225VariantSummary(
    TechV2ZeroSupportVariant Variant,
    int HumanUsefulGainsKept,
    int HumanUsefulGainsTotal,
    TechV2ZeroSupportRecommendation Recommendation);

internal sealed record TechV2Phase225AuditReport(
    IReadOnlyList<TechV2Phase225Distribution> Distributions,
    IReadOnlyList<TechV2Phase225ExtremeResult> Extremes,
    IReadOnlyList<TechV2Phase225HumanImpact> HumanImpact,
    IReadOnlyList<TechV2Phase225VariantSummary> Summaries,
    TechV2ZeroSupportVariant RecommendedVariant,
    TechV2ZeroSupportRecommendation Recommendation,
    string DominantRemainingRisk,
    bool AllDecisionsUnchanged);

internal static class TechV2ZeroSupportSmoothingCalibrationAudit
{
    private const double Epsilon = 1e-9;
    private static readonly TechV2ZeroSupportVariant[] Variants =
    [
        TechV2ZeroSupportVariant.ZERO_A,
        TechV2ZeroSupportVariant.ZERO_B,
        TechV2ZeroSupportVariant.ZERO_C,
        TechV2ZeroSupportVariant.ZERO_D,
    ];

    private static readonly string[] ExtremeMapTokens =
    [
        "SANTA SAN", "AliVe", "Ringing Bloom", "Koinu", "Don't Let Go",
        "Start Again", "Yuuki wa Doko ni?", "Deai Machi", "More-more LOVERS!!",
    ];

    private static readonly string[] HumanCases =
    [
        "Exit R5", "WorldEndsNow R1", "TrustInYou R1", "Inferno R1",
        "TaoHuaXiao R1", "HappppySong R1", "HappppySong R2", "Kira R1",
        "Frozen R1", "GloriousDays R1",
    ];

    internal static TechV2Phase225AuditReport Run(string databasePath, int maxMaps = 1000)
    {
        IReadOnlyList<MlDatasetSample> samples = TechV2StrongTechForensicAudit.LoadLocalCacheSamples(databasePath);
        IReadOnlyList<ShadowWindow> all = ScanDeterministicSample(samples, maxMaps);

        IReadOnlyList<TechV2Phase225Distribution> distributions = Variants
            .Select(variant => BuildDistribution(all, variant))
            .ToArray();
        IReadOnlyList<TechV2Phase225ExtremeResult> extremes = BuildExtremes(all);
        IReadOnlyList<TechV2Phase225HumanImpact> human = BuildHumanImpact(databasePath);
        IReadOnlyList<TechV2Phase225VariantSummary> summaries = Variants
            .Select(variant => BuildSummary(variant, distributions, human))
            .ToArray();

        TechV2Phase225VariantSummary best = summaries
            .OrderBy(item => item.Recommendation == TechV2ZeroSupportRecommendation.REJECT ? 1 : 0)
            .ThenBy(item => distributions.Single(d => d.Variant == item.Variant).ChangedDecisionCount)
            .ThenBy(item => distributions.Single(d => d.Variant == item.Variant).MaxAbsDelta)
            .ThenByDescending(item => item.HumanUsefulGainsKept)
            .ThenBy(item => item.Variant)
            .First();

        bool unchanged = distributions.All(item => item.AllDecisionsUnchanged);
        return new TechV2Phase225AuditReport(
            distributions,
            extremes,
            human,
            summaries,
            best.Variant,
            best.Recommendation,
            "PREV+CURR can still contribute local context after zero-support smoothing is removed.",
            unchanged);
    }

    internal static string FormatReport(TechV2Phase225AuditReport report)
    {
        StringBuilder text = new();
        text.AppendLine("===== TECH V2 PHASE 2.25 ZERO-SUPPORT SMOOTHING CALIBRATION (TEST-ONLY/SHADOW) =====");
        text.AppendLine("ZERO_A: den=0 -> 0; den>0 keeps current Laplace.");
        text.AppendLine("ZERO_B: den=0 -> raw baseline (currently 0); den>0 keeps current Laplace.");
        text.AppendLine("ZERO_C: Laplace only when den>0; otherwise 0.");
        text.AppendLine("ZERO_D: Laplace * den/(den+1); therefore den=0 -> 0 and support grows monotonically to 1.");

        text.AppendLine("VARIANT | avg | median | min | max | maxAbs | %inc | %dec | >.15 | >.20 | >.30 | decisionsChanged");
        foreach (TechV2Phase225Distribution item in report.Distributions)
        {
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0} | {1:+0.0000;-0.0000;0.0000} | {2:+0.0000;-0.0000;0.0000} | {3:+0.0000;-0.0000;0.0000} | {4:+0.0000;-0.0000;0.0000} | {5:0.0000} | {6:F2}% | {7:F2}% | {8} | {9} | {10} | {11}",
                item.Variant, item.AverageDelta, item.MedianDelta, item.MinDelta, item.MaxDelta,
                item.MaxAbsDelta, item.PercentIncreased, item.PercentDecreased, item.Over015,
                item.Over020, item.Over030, item.ChangedDecisionCount));
        }

        text.AppendLine("EXTREME WINDOWS | raw-current | SHADOW_D baseline | GCTX_C | ZERO_A | ZERO_B | ZERO_C | ZERO_D");
        foreach (TechV2Phase225ExtremeResult item in report.Extremes)
        {
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0} [{1}] {2} | {3:F6} | {4:F6} | {5:F6} | {6:F6} | {7:F6} | {8:F6} | {9:F6}",
                item.Map, item.Difficulty, FormatTime(item.Timestamp), item.Current, item.BaselineShadowD, item.GctxCCorrected,
                item.Corrected[TechV2ZeroSupportVariant.ZERO_A], item.Corrected[TechV2ZeroSupportVariant.ZERO_B],
                item.Corrected[TechV2ZeroSupportVariant.ZERO_C], item.Corrected[TechV2ZeroSupportVariant.ZERO_D]));
            foreach (TechV2ZeroSupportVariant variant in Variants)
            {
                TechV2Phase225SourceAttribution attribution = item.Attribution[variant];
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0}: delta={1:+0.000000;-0.000000;0.000000} | SA={2:+0.000000;-0.000000;0.000000} | PrevCtx={3:+0.000000;-0.000000;0.000000} | Topology={4:+0.000000;-0.000000;0.000000} | Other={5:+0.000000;-0.000000;0.000000}",
                    variant, attribution.TotalDelta, attribution.DeltaFromSupportAware,
                    attribution.DeltaFromPrevContext, attribution.DeltaFromTopology, attribution.DeltaFromOther));
                foreach (TechV2Phase225RatioAdjustment ratio in item.Ratios[variant].Where(r => r.Denominator <= 1))
                    text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "    {0}: {1}/{2} raw={3:F3} laplace={4:F3} support={5:F3} effective={6:F3} final={7:F6}",
                        ratio.Name, ratio.Numerator, ratio.Denominator, ratio.RawRatio, ratio.LaplaceRatio,
                        ratio.SupportFactor, ratio.EffectiveRatio, ratio.FinalContribution));
            }
        }

        text.AppendLine("HUMAN USEFUL CASES | case | variant | current | original-D | corrected-variant | delta-v-current | status");
        foreach (TechV2Phase225HumanImpact item in report.HumanImpact)
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0} | {1} | {2:F6} | {3:F6} | {4:F6} | {5:+0.000000;-0.000000;0.000000} | {6}",
                item.CaseId, item.Variant, item.Current, item.Original, item.Corrected, item.Delta, item.GainStatus));

        text.AppendLine("FINAL | Variant | MaxAbsDelta | DecisionsChanged | HumanGainsKept | >.15 | >.20 | >.30 | Recommendation");
        foreach (TechV2Phase225VariantSummary summary in report.Summaries)
        {
            TechV2Phase225Distribution dist = report.Distributions.Single(d => d.Variant == summary.Variant);
            text.AppendLine($"{summary.Variant} | {dist.MaxAbsDelta:F6} | {dist.ChangedDecisionCount} | {summary.HumanUsefulGainsKept}/{summary.HumanUsefulGainsTotal} | {dist.Over015} | {dist.Over020} | {dist.Over030} | {summary.Recommendation}");
        }

        text.AppendLine($"RECOMMENDED VARIANT = {report.RecommendedVariant}");
        text.AppendLine($"RECOMMENDATION = {report.Recommendation}");
        text.AppendLine($"DOMINANT REMAINING RISK = {report.DominantRemainingRisk}");
        return text.ToString();
    }

    private static TechV2Phase225Distribution BuildDistribution(IReadOnlyList<ShadowWindow> all, TechV2ZeroSupportVariant variant)
    {
        double[] deltas = all.Select(item => Correct(item.Window, variant) - item.Window.CurrentCombinedValue)
            .OrderBy(value => value).ToArray();
        if (deltas.Length == 0)
            return new(variant, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, true, 0);

        return new(variant, deltas.Length, deltas.Average(), Median(deltas), deltas[0], deltas[^1],
            deltas.Max(value => Math.Abs(value)), 100d * deltas.Count(value => value > Epsilon) / deltas.Length,
            100d * deltas.Count(value => value < -Epsilon) / deltas.Length,
            deltas.Count(value => Math.Abs(value) > .15), deltas.Count(value => Math.Abs(value) > .20),
            deltas.Count(value => Math.Abs(value) > .30), true, 0);
    }

    private static IReadOnlyList<TechV2Phase225ExtremeResult> BuildExtremes(IReadOnlyList<ShadowWindow> all) =>
        ExtremeMapTokens.Select(token => all.Where(item => MatchesToken(item.Map, token))
            .OrderByDescending(item => item.Window.Delta).ThenBy(item => item.Window.StartIndex).FirstOrDefault())
        .Where(item => item is not null).Cast<ShadowWindow>()
        .OrderByDescending(item => item.Window.Delta).ThenBy(item => item.Map, StringComparer.Ordinal)
        .Select(item => new TechV2Phase225ExtremeResult(
            item.Map, item.Difficulty, item.Window.StartTime, item.Window.CurrentCombinedValue, item.Window.CorrectedCombinedValue,
            ApplyGctxC(item.Window),
            Variants.ToDictionary(variant => variant, variant => Correct(item.Window, variant)),
            Variants.ToDictionary(variant => variant, variant => Attribute(item, variant)),
            Variants.ToDictionary(variant => variant, variant => Adjustments(item.Window, variant))))
        .ToArray();

    private static IReadOnlyList<TechV2Phase225HumanImpact> BuildHumanImpact(string databasePath)
    {
        TechV2HumanGroundedAuditReport grounded = TechV2HumanGroundedStrongTechAudit.Run(databasePath);
        IReadOnlyDictionary<string, TechV2HumanGroundedMap> maps = grounded.Maps
            .Where(map => map.Resolved && map.Path is not null)
            .ToDictionary(map => map.Reference.Key, StringComparer.Ordinal);
        List<TechV2Phase225HumanImpact> output = [];

        foreach (string caseId in HumanCases)
        {
            if (!TryParseCase(caseId, out string key, out int region) || !maps.TryGetValue(key, out TechV2HumanGroundedMap? map) || map.Path is null)
                continue;
            foreach (ShadowWindow item in BuildShadow(map.Path, key)
                .Where(item => region >= 1 && region <= map.Presence.Regions.Count
                    && item.Window.StartIndex >= map.Presence.Regions[region - 1].StartWindowIndex
                    && item.Window.EndIndex <= map.Presence.Regions[region - 1].EndWindowIndex))
            {
                foreach (TechV2ZeroSupportVariant variant in Variants)
                {
                    double corrected = Correct(item.Window, variant);
                    output.Add(new(caseId, variant, item.Window.CurrentCombinedValue,
                        item.Window.CorrectedCombinedValue, corrected,
                        corrected - item.Window.CurrentCombinedValue, GainStatus(item.Window, corrected)));
                }
            }
        }
        return output;
    }

    private static TechV2Phase225VariantSummary BuildSummary(TechV2ZeroSupportVariant variant,
        IReadOnlyList<TechV2Phase225Distribution> distributions, IReadOnlyList<TechV2Phase225HumanImpact> human)
    {
        TechV2Phase225Distribution distribution = distributions.Single(item => item.Variant == variant);
        IReadOnlyList<TechV2Phase225HumanImpact> priorGains = human
            .Where(item => item.Variant == variant && item.Original - item.Current > Epsilon)
            .ToArray();
        int kept = priorGains.Count(item => item.Corrected - item.Current > Epsilon);
        TechV2ZeroSupportRecommendation recommendation = !distribution.AllDecisionsUnchanged
            ? TechV2ZeroSupportRecommendation.REJECT
            : distribution.MaxAbsDelta <= .15 && kept >= Math.Max(0, priorGains.Count - 1)
                ? TechV2ZeroSupportRecommendation.SAFE_TO_ENABLE
                : TechV2ZeroSupportRecommendation.NEEDS_MORE_CALIBRATION;
        return new(variant, kept, priorGains.Count, recommendation);
    }

    private static string GainStatus(TechV2ShadowWindowDiagnostics window, double corrected)
    {
        if (window.CorrectedCombinedValue - window.CurrentCombinedValue <= Epsilon)
            return "NO_PRIOR_GAIN";
        if (corrected < -Epsilon)
            return "REVERSED";
        if (corrected <= Epsilon)
            return "LOST";
        return corrected + Epsilon >= window.CorrectedCombinedValue ? "KEPT" : "REDUCED";
    }

    private static double Correct(TechV2ShadowWindowDiagnostics window, TechV2ZeroSupportVariant variant) =>
        Math.Clamp((AdjustedRatioMean(window, variant) + window.CorrectedLocalContextValue) / 2d, 0, 1);

    private static double AdjustedRatioMean(TechV2ShadowWindowDiagnostics window, TechV2ZeroSupportVariant variant) =>
        window.Ratios.Select(ratio => Adjust(ratio, variant).EffectiveRatio).DefaultIfEmpty(0).Average();

    private static TechV2Phase225RatioAdjustment Adjust(TechV2ShadowRatioValue ratio, TechV2ZeroSupportVariant variant)
    {
        double factor;
        double effective;
        if (variant == TechV2ZeroSupportVariant.ZERO_D)
        {
            factor = ratio.Denominator <= 0 ? 0 : (double)ratio.Denominator / (ratio.Denominator + 1d);
            effective = ratio.SupportAware * factor;
        }
        else if (ratio.Denominator <= 0)
        {
            // A/C use an explicit zero; B documents the equivalent raw-baseline fallback.
            factor = 0;
            effective = variant == TechV2ZeroSupportVariant.ZERO_B ? ratio.Raw : 0;
        }
        else
        {
            factor = 1;
            effective = ratio.SupportAware;
        }
        return new(ratio.Name, ratio.Numerator, ratio.Denominator, ratio.Raw, ratio.SupportAware,
            factor, effective, effective / 14d);
    }

    private static IReadOnlyList<TechV2Phase225RatioAdjustment> Adjustments(TechV2ShadowWindowDiagnostics window, TechV2ZeroSupportVariant variant) =>
        window.Ratios.OrderBy(item => item.Name, StringComparer.Ordinal).Select(item => Adjust(item, variant)).ToArray();

    private static TechV2Phase225SourceAttribution Attribute(ShadowWindow context, TechV2ZeroSupportVariant variant)
    {
        TechV2ShadowWindowDiagnostics window = context.Window;
        IReadOnlyList<TechV2Phase225RatioAdjustment> ratios = Adjustments(window, variant);
        double nonTopology = ratios.Where(item => item.Name != "SliderToSliderRatio")
            .Sum(item => item.EffectiveRatio - item.RawRatio) / 14d;
        TechV2Phase225RatioAdjustment topology = ratios.Single(item => item.Name == "SliderToSliderRatio");
        double originalTopologyLocal = TopologyLocalDelta(context);
        double topologyDelta = (topology.EffectiveRatio - topology.RawRatio) / 14d + originalTopologyLocal;
        double prevContext = PrevContextNoTopologyDelta(context);
        double total = Correct(window, variant) - window.CurrentCombinedValue;
        return new(total, nonTopology, prevContext, topologyDelta, total - nonTopology - prevContext - topologyDelta);
    }

    private static double TopologyLocalDelta(ShadowWindow context)
    {
        ShadowWindow previous = context.Previous ?? context;
        double currentTopology = TopologyPartInSlider(context.Source);
        double previousTopology = TopologyPartInSlider(previous.Source);
        return ((previousTopology + currentTopology) / 2d - currentTopology) / 16d;
    }

    private static double PrevContextNoTopologyDelta(ShadowWindow context)
    {
        ShadowWindow previous = context.Previous ?? context;
        double current = LocalNoTopology(context.Source, context.Decision, context.Window.Ratios);
        double prior = LocalNoTopology(previous.Source, previous.Decision, previous.Window.Ratios);
        return ((prior + current) / 2d - current) / 2d;
    }

    private static double LocalNoTopology(TechV2ExperimentalWindowResult source, TechV2WindowDecision decision,
        IReadOnlyList<TechV2ShadowRatioValue> ratios)
    {
        double structural = ratios.Single(item => item.Name == "StructuralRatio").Raw;
        double sharp = ratios.Single(item => item.Name == "SharpRatio").Raw;
        double run = Math.Clamp((double)source.ComplexSliderRunLength / TechDetectorV2Experimental.WindowSize, 0, 1);
        double slider = Math.Clamp(source.SliderComplexity * .55 + run * .20, 0, 1);
        return new[] { decision.GeometryEvidence, decision.MovementEvidence, slider, decision.MotifEvidence,
            structural, sharp, source.AngleCoherenceWithSupport, source.SpacingVariation }.Average();
    }

    private static double TopologyPartInSlider(TechV2ExperimentalWindowResult source)
    {
        double run = Math.Clamp((double)source.ComplexSliderRunLength / TechDetectorV2Experimental.WindowSize, 0, 1);
        double withTopology = Math.Clamp(source.SliderComplexity * .55
            + source.ComplexSliderTransitionTopology.SliderToSliderRatio * .25 + run * .20, 0, 1);
        double withoutTopology = Math.Clamp(source.SliderComplexity * .55 + run * .20, 0, 1);
        return withTopology - withoutTopology;
    }

    private static double ApplyGctxC(TechV2ShadowWindowDiagnostics window)
    {
        TechV2ShadowSupportCounts support = window.SupportCounts;
        double numerator = support.ValidTransitions + support.StructuralCount + support.SharpCount
            + support.SpacingVariationCount + support.AlternatingCount;
        double denominator = numerator + Math.Max(0, support.TopologyTransitionCount);
        double confidence = denominator <= 0 ? 0 : Math.Clamp(numerator / denominator, 0, 1);
        double currentLocalOnly = 2 * window.CurrentCombinedValue - window.CorrectedSupportAwareRatioValue;
        double guardedLocal = confidence * window.CorrectedLocalContextValue + (1 - confidence) * currentLocalOnly;
        return Math.Clamp((window.CorrectedSupportAwareRatioValue + guardedLocal) / 2d, 0, 1);
    }

    private static IReadOnlyList<ShadowWindow> ScanDeterministicSample(IReadOnlyList<MlDatasetSample> samples, int maxMaps)
    {
        List<ShadowWindow> output = [];
        foreach (string path in samples.Select(item => item.SourceFilePath).Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.Ordinal).Take(maxMaps))
        {
            try
            {
                Beatmap map = BeatmapParser.Load(path);
                if (map.HitObjects.Count < TechDetectorV2Experimental.WindowSize) continue;
                IReadOnlyList<TechV2ExperimentalWindowResult> windows = TechDetectorV2Experimental.Analyze(map.HitObjects,
                    GameplayAnalyzer.IsSpinner, GameplayAnalyzer.IsSlider, GameplayAnalyzer.IsComplexSlider,
                    GameplayAnalyzer.GetObjectStartPosition, GameplayAnalyzer.GetObjectEndPosition, GameplayAnalyzer.GetTechTurnAngle);
                IReadOnlyList<TechV2WindowDecision> decisions = TechV2ExperimentalDecisionEngine.PromoteSustainedTech(TechV2ExperimentalDecisionEngine.Decide(windows));
                TechV2CombinedCorrectionShadowResult shadow = TechV2CombinedCorrectionShadowRuntime.Compute(map.HitObjects, windows, decisions);
                string title = string.IsNullOrWhiteSpace(map.Title) ? Path.GetFileNameWithoutExtension(path) : map.Title;
                string difficulty = string.IsNullOrWhiteSpace(map.Version) ? "Unknown" : map.Version;
                IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> sourceByStart = windows.ToDictionary(item => item.StartIndex);
                IReadOnlyDictionary<int, TechV2WindowDecision> decisionByStart = decisions.ToDictionary(item => item.StartIndex);
                List<ShadowWindow> local = shadow.Windows
                    .Where(window => sourceByStart.ContainsKey(window.StartIndex) && decisionByStart.ContainsKey(window.StartIndex))
                    .OrderBy(window => window.StartIndex)
                    .Select(window => new ShadowWindow(title, difficulty, sourceByStart[window.StartIndex],
                        decisionByStart[window.StartIndex], window, null))
                    .ToList();
                for (int index = 0; index < local.Count; index++)
                    local[index] = local[index] with { Previous = index > 0 ? local[index - 1] : null };
                output.AddRange(local);
            }
            catch (Exception exception) when (exception is IOException or FormatException or InvalidDataException) { }
        }
        return output;
    }

    private static IReadOnlyList<ShadowWindow> BuildShadow(string path, string mapName)
    {
        Beatmap map = BeatmapParser.Load(path);
        IReadOnlyList<TechV2ExperimentalWindowResult> windows = TechDetectorV2Experimental.Analyze(map.HitObjects,
            GameplayAnalyzer.IsSpinner, GameplayAnalyzer.IsSlider, GameplayAnalyzer.IsComplexSlider,
            GameplayAnalyzer.GetObjectStartPosition, GameplayAnalyzer.GetObjectEndPosition, GameplayAnalyzer.GetTechTurnAngle);
        IReadOnlyList<TechV2WindowDecision> decisions = TechV2ExperimentalDecisionEngine.PromoteSustainedTech(TechV2ExperimentalDecisionEngine.Decide(windows));
        TechV2CombinedCorrectionShadowResult shadow = TechV2CombinedCorrectionShadowRuntime.Compute(map.HitObjects, windows, decisions);
        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> sourceByStart = windows.ToDictionary(item => item.StartIndex);
        IReadOnlyDictionary<int, TechV2WindowDecision> decisionByStart = decisions.ToDictionary(item => item.StartIndex);
        List<ShadowWindow> local = shadow.Windows
            .Where(window => sourceByStart.ContainsKey(window.StartIndex) && decisionByStart.ContainsKey(window.StartIndex))
            .OrderBy(window => window.StartIndex)
            .Select(window => new ShadowWindow(mapName, map.Version, sourceByStart[window.StartIndex],
                decisionByStart[window.StartIndex], window, null))
            .ToList();
        for (int index = 0; index < local.Count; index++)
            local[index] = local[index] with { Previous = index > 0 ? local[index - 1] : null };
        return local;
    }

    internal static bool MatchesToken(string value, string token) => Normalize(value).Contains(Normalize(token), StringComparison.Ordinal);
    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    private static bool TryParseCase(string caseId, out string key, out int region)
    {
        key = string.Empty; region = 0;
        int split = caseId.LastIndexOf(" R", StringComparison.Ordinal);
        return split > 0 && int.TryParse(caseId[(split + 2)..], out region) && region > 0 && (key = caseId[..split]).Length > 0;
    }
    private static double Median(double[] ordered) => ordered.Length == 0 ? 0 : ordered.Length % 2 == 1 ? ordered[ordered.Length / 2] : (ordered[ordered.Length / 2 - 1] + ordered[ordered.Length / 2]) / 2d;
    private static string FormatTime(double milliseconds)
    {
        TimeSpan value = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}.{2:D3}", (int)value.TotalMinutes, value.Seconds, value.Milliseconds);
    }
    private sealed record ShadowWindow(
        string Map,
        string Difficulty,
        TechV2ExperimentalWindowResult Source,
        TechV2WindowDecision Decision,
        TechV2ShadowWindowDiagnostics Window,
        ShadowWindow? Previous);
}
