using BeatInsight.Analysis;
using BeatInsight.Models;
using BeatInsight.Models.Persistence;
using BeatInsight.Parser;
using System.Globalization;
using System.IO;
using System.Text;

namespace BeatInsight.Tests.Analysis;

internal enum TechV2Phase224DominantCause
{
    smoothing_inflation,
    previous_window_contamination,
    topology_inflation,
    normalization_artifact,
    formula_interaction,
    other,
}

internal sealed record TechV2Phase224FeatureTerm(
    string Name,
    double RawFeature,
    double NormalizedFeature,
    double Weight,
    double WeightedContribution);

internal sealed record TechV2Phase224RatioDecomposition(
    string Name,
    int Numerator,
    int Denominator,
    double RawRatio,
    double LaplaceAdjustedRatio,
    double Delta,
    double DeltaContributionToFinal);

internal sealed record TechV2Phase224PrevCurrSignal(
    string Name,
    double Previous,
    double Current,
    double Aggregated,
    double DeltaContributionToFinal);

internal sealed record TechV2Phase224TopologyAudit(
    int TopologyDenominator,
    int SliderToSliderCount,
    int SliderToCircleCount,
    int CircleToSliderCount,
    int CircleToCircleCount,
    double SliderToSlider,
    double SliderToCircle,
    double CircleToSlider,
    double CircleToCircle,
    double CurrentTopologyContribution,
    double AggregatedTopologyContribution,
    double DeltaFromTopology);

internal sealed record TechV2Phase224SourceAttribution(
    double TotalDelta,
    double DeltaFromSupportAware,
    double DeltaFromPrevContext,
    double DeltaFromTopology,
    double DeltaFromOther);

internal sealed record TechV2Phase224ExtremeWindow(
    string Map,
    string Difficulty,
    double Timestamp,
    TechV2ShadowSupportCounts Support,
    double CurrentValue,
    double CorrectedValue,
    IReadOnlyList<TechV2Phase224FeatureTerm> CurrentTerms,
    IReadOnlyList<TechV2Phase224RatioDecomposition> Ratios,
    IReadOnlyList<TechV2Phase224PrevCurrSignal> PrevCurr,
    TechV2Phase224TopologyAudit Topology,
    TechV2Phase224SourceAttribution Attribution);

internal sealed record TechV2Phase224UsefulCaseComparison(
    string CaseId,
    double MeanTotalDelta,
    double MeanSupportAwareDelta,
    double MeanPrevContextDelta,
    double MeanTopologyDelta,
    double MeanOtherDelta);

internal sealed record TechV2Phase224AuditReport(
    IReadOnlyList<TechV2Phase224ExtremeWindow> ExtremeWindows,
    IReadOnlyList<TechV2Phase224UsefulCaseComparison> UsefulCases,
    TechV2Phase224DominantCause DominantCause,
    bool AllDecisionsUnchanged);

internal static class TechV2ExtremeShadowDeltaAttributionAudit
{
    private const double GeometryIrregularityWeight = 0.65;
    private const double AngleCoherenceWeight = 0.35;
    private const double RhythmIrregularityWeight = 0.45;
    private const double AwkwardnessWeight = 0.35;
    private const double SpacingVariationWeight = 0.20;
    private const double SliderComplexityWeight = 0.55;
    private const double SliderToSliderWeight = 0.25;
    private const double ComplexSliderRunWeight = 0.20;

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

    private static readonly string[] UsefulCaseIds =
    [
        "Exit R5",
        "WorldEndsNow R1",
        "TrustInYou R1",
        "Kira R1",
        "Frozen R1",
        "GloriousDays R1",
    ];

    internal static TechV2Phase224AuditReport Run(string databasePath)
    {
        IReadOnlyList<MlDatasetSample> samples = TechV2StrongTechForensicAudit.LoadLocalCacheSamples(databasePath);
        // Reuse the exact deterministic 1,000-map cache slice used by Phase 2.22.
        // Resolving from parsed metadata (rather than only filename text) preserves
        // cases whose local .osu filename differs from its [Metadata] title.
        IReadOnlyList<WindowContext> contexts = ScanDeterministicSample(samples, maxMaps: 1000);

        IReadOnlyList<WindowContext> extremes = ExtremeMapTokens
            .Select(token => contexts
                .Where(ctx => MatchesToken(ctx.Map, token))
                .OrderByDescending(ctx => ctx.Window.Delta)
                .ThenBy(ctx => ctx.Window.StartIndex)
                .FirstOrDefault())
            .Where(ctx => ctx is not null)
            .Cast<WindowContext>()
            .OrderByDescending(ctx => ctx.Window.Delta)
            .ThenBy(ctx => ctx.Map, StringComparer.Ordinal)
            .ToArray();

        IReadOnlyList<TechV2Phase224ExtremeWindow> extremeItems = extremes
            .Select(DescribeExtreme)
            .ToArray();

        IReadOnlyList<TechV2Phase224UsefulCaseComparison> useful = BuildUsefulCaseComparison(databasePath);
        bool unchanged = contexts.All(ctx => string.Equals(ctx.Window.DecisionKindBefore, ctx.Window.DecisionKindAfter, StringComparison.Ordinal));
        TechV2Phase224DominantCause cause = SelectDominantCause(extremeItems);

        return new TechV2Phase224AuditReport(extremeItems, useful, cause, unchanged);
    }

    internal static string FormatReport(TechV2Phase224AuditReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder text = new();
        text.AppendLine("===== TECH V2 PHASE 2.24 EXTREME SHADOW DELTA ATTRIBUTION AUDIT (TEST-ONLY/SHADOW) =====");
        text.AppendLine("Aucun changement runtime. Attribution mathématique des deltas extrêmes Phase 2.22.");

        foreach (TechV2Phase224ExtremeWindow item in report.ExtremeWindows)
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "EXTREME | {0} [{1}] {2} | current={3:F4} -> corrected={4:F4} | delta={5:+0.0000;-0.0000;0.0000}",
                item.Map,
                item.Difficulty,
                FormatTime(item.Timestamp),
                item.CurrentValue,
                item.CorrectedValue,
                item.CorrectedValue - item.CurrentValue));

            text.AppendLine("  CURRENT TERM DECOMPOSITION (raw | normalized | weight | weighted)");
            foreach (TechV2Phase224FeatureTerm term in item.CurrentTerms)
            {
                text.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "    {0}: raw={1:F6} | norm={2:F6} | w={3:F6} | contrib={4:F6}",
                    term.Name,
                    term.RawFeature,
                    term.NormalizedFeature,
                    term.Weight,
                    term.WeightedContribution));
            }

            text.AppendLine("  SA-LAPLACE RATIO DECOMPOSITION");
            foreach (TechV2Phase224RatioDecomposition ratio in item.Ratios)
            {
                text.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "    {0}: {1}/{2} | raw={3:F6} | laplace={4:F6} | delta={5:+0.000000;-0.000000;0.000000} | final-contrib={6:+0.000000;-0.000000;0.000000}",
                    ratio.Name,
                    ratio.Numerator,
                    ratio.Denominator,
                    ratio.RawRatio,
                    ratio.LaplaceAdjustedRatio,
                    ratio.Delta,
                    ratio.DeltaContributionToFinal));
            }

            text.AppendLine("  LA-PREV+CURR DECOMPOSITION");
            foreach (TechV2Phase224PrevCurrSignal signal in item.PrevCurr)
            {
                text.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "    {0}: prev={1:F6} | curr={2:F6} | agg={3:F6} | final-contrib={4:+0.000000;-0.000000;0.000000}",
                    signal.Name,
                    signal.Previous,
                    signal.Current,
                    signal.Aggregated,
                    signal.DeltaContributionToFinal));
            }

            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  TOPOLOGY AUDIT: den={0} | S>S={1}/{0}={5:F6} S>C={2}/{0}={6:F6} C>S={3}/{0}={7:F6} C>C={4}/{0}={8:F6} | currentTopo={9:F6} | aggTopo={10:F6} | deltaTopo={11:+0.000000;-0.000000;0.000000}",
                item.Topology.TopologyDenominator,
                item.Topology.SliderToSliderCount,
                item.Topology.SliderToCircleCount,
                item.Topology.CircleToSliderCount,
                item.Topology.CircleToCircleCount,
                item.Topology.SliderToSlider,
                item.Topology.SliderToCircle,
                item.Topology.CircleToSlider,
                item.Topology.CircleToCircle,
                item.Topology.CurrentTopologyContribution,
                item.Topology.AggregatedTopologyContribution,
                item.Topology.DeltaFromTopology));

            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  SOURCE ATTRIBUTION: Total={0:+0.000000;-0.000000;0.000000} | SA={1:+0.000000;-0.000000;0.000000} | PrevCtx={2:+0.000000;-0.000000;0.000000} | Topology={3:+0.000000;-0.000000;0.000000} | Other={4:+0.000000;-0.000000;0.000000}",
                item.Attribution.TotalDelta,
                item.Attribution.DeltaFromSupportAware,
                item.Attribution.DeltaFromPrevContext,
                item.Attribution.DeltaFromTopology,
                item.Attribution.DeltaFromOther));
        }

        text.AppendLine("USEFUL CASE COMPARISON");
        foreach (TechV2Phase224UsefulCaseComparison item in report.UsefulCases)
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0}: total={1:+0.000000;-0.000000;0.000000} | SA={2:+0.000000;-0.000000;0.000000} | PrevCtx={3:+0.000000;-0.000000;0.000000} | Topology={4:+0.000000;-0.000000;0.000000} | Other={5:+0.000000;-0.000000;0.000000}",
                item.CaseId,
                item.MeanTotalDelta,
                item.MeanSupportAwareDelta,
                item.MeanPrevContextDelta,
                item.MeanTopologyDelta,
                item.MeanOtherDelta));
        }

        text.AppendLine($"DOMINANT CAUSE: {report.DominantCause}");
        text.AppendLine($"ALL DECISIONS UNCHANGED: {report.AllDecisionsUnchanged}");

        return text.ToString();
    }

    private static IReadOnlyList<TechV2Phase224UsefulCaseComparison> BuildUsefulCaseComparison(string databasePath)
    {
        TechV2HumanGroundedAuditReport grounded = TechV2HumanGroundedStrongTechAudit.Run(databasePath);
        IReadOnlyDictionary<string, TechV2HumanGroundedMap> maps = grounded.Maps
            .Where(map => map.Resolved && map.Path is not null)
            .ToDictionary(map => map.Reference.Key, StringComparer.Ordinal);

        List<TechV2Phase224UsefulCaseComparison> items = [];
        foreach (string caseId in UsefulCaseIds)
        {
            if (!TryParseCase(caseId, out string key, out int regionNumber))
                continue;
            if (!maps.TryGetValue(key, out TechV2HumanGroundedMap? map) || map.Path is null)
                continue;

            Beatmap beatmap = BeatmapParser.Load(map.Path);
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
            IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> sources = windows.ToDictionary(item => item.StartIndex);
            IReadOnlyDictionary<int, TechV2WindowDecision> decisionByStart = decisions.ToDictionary(item => item.StartIndex);
            List<WindowContext> allContexts = shadow.Windows
                .Where(window => sources.ContainsKey(window.StartIndex) && decisionByStart.ContainsKey(window.StartIndex))
                .OrderBy(window => window.StartIndex)
                .Select(window => new WindowContext(
                    key,
                    beatmap.Version,
                    sources[window.StartIndex],
                    decisionByStart[window.StartIndex],
                    window,
                    null))
                .ToList();
            for (int index = 0; index < allContexts.Count; index++)
                allContexts[index] = allContexts[index] with { Previous = index > 0 ? allContexts[index - 1] : null };

            IReadOnlyList<WindowContext> contexts = allContexts
                .Where(ctx => ctx.Window.StartIndex >= region.StartWindowIndex && ctx.Window.EndIndex <= region.EndWindowIndex)
                .ToArray();

            if (contexts.Count == 0)
                continue;

            IReadOnlyList<TechV2Phase224SourceAttribution> attributions = contexts
                .Select(ComputeAttribution)
                .ToArray();

            items.Add(new TechV2Phase224UsefulCaseComparison(
                caseId,
                attributions.Average(a => a.TotalDelta),
                attributions.Average(a => a.DeltaFromSupportAware),
                attributions.Average(a => a.DeltaFromPrevContext),
                attributions.Average(a => a.DeltaFromTopology),
                attributions.Average(a => a.DeltaFromOther)));
        }

        return items;
    }

    private static TechV2Phase224ExtremeWindow DescribeExtreme(WindowContext context)
    {
        IReadOnlyList<TechV2Phase224FeatureTerm> terms = BuildCurrentTerms(context);
        IReadOnlyList<TechV2Phase224RatioDecomposition> ratios = context.Window.Ratios
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .Select(item => new TechV2Phase224RatioDecomposition(
                item.Name,
                item.Numerator,
                item.Denominator,
                item.Raw,
                item.SupportAware,
                item.SupportAware - item.Raw,
                (item.SupportAware - item.Raw) / 14.0))
            .ToArray();

        IReadOnlyList<TechV2Phase224PrevCurrSignal> prevCurr = BuildPrevCurrSignals(context);
        TechV2Phase224TopologyAudit topology = BuildTopologyAudit(context);
        TechV2Phase224SourceAttribution attribution = ComputeAttribution(context);

        return new TechV2Phase224ExtremeWindow(
            context.Map,
            context.Difficulty,
            context.Window.StartTime,
            context.Window.SupportCounts,
            context.Window.CurrentCombinedValue,
            context.Window.CorrectedCombinedValue,
            terms,
            ratios,
            prevCurr,
            topology,
            attribution);
    }

    private static IReadOnlyList<TechV2Phase224FeatureTerm> BuildCurrentTerms(WindowContext context)
    {
        double sliderRunNormalized = Math.Clamp((double)context.Source.ComplexSliderRunLength / TechDetectorV2Experimental.WindowSize, 0, 1);

        List<TechV2Phase224FeatureTerm> terms =
        [
            // CurrentCombinedValue = 1/2 * mean(7 raw ratios) + 1/2 * mean(8 local signals).
            // The local signals below expand the production-family composition, so the displayed
            // contributions add up to CurrentCombinedValue without hiding a family-level weight.
            LocalTerm("GeometryIrregularity", context.Source.GeometryIrregularity, GeometryIrregularityWeight),
            LocalTerm("AngleCoherenceWithSupport", context.Source.AngleCoherenceWithSupport, AngleCoherenceWeight),
            LocalTerm("RhythmIrregularity", context.Source.RhythmIrregularity, RhythmIrregularityWeight),
            LocalTerm("Awkwardness", context.Source.Awkwardness, AwkwardnessWeight),
            LocalTerm("Movement:SpacingVariation", context.Source.SpacingVariation, SpacingVariationWeight),
            LocalTerm("SliderComplexity", context.Source.SliderComplexity, SliderComplexityWeight),
            LocalTerm("SliderToSliderTopology", context.Source.ComplexSliderTransitionTopology.SliderToSliderRatio, SliderToSliderWeight),
            LocalTerm("ComplexSliderRunNormalized", sliderRunNormalized, ComplexSliderRunWeight),
            LocalTerm("MotifContinuity", context.Source.CoupledTechnicalMotifContinuity, 1.0),
            LocalTerm("Local:StructuralRatio", Ratio(context.Window, "StructuralRatio"), 1.0),
            LocalTerm("Local:SharpRatio", Ratio(context.Window, "SharpRatio"), 1.0),
            LocalTerm("Local:AngleCoherenceWithSupport", context.Source.AngleCoherenceWithSupport, 1.0),
            LocalTerm("Local:SpacingVariation", context.Source.SpacingVariation, 1.0),
        ];

        terms.AddRange(context.Window.Ratios
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .Select(item => new TechV2Phase224FeatureTerm(
                "Ratio:" + item.Name,
                item.Raw,
                item.Raw,
                1.0 / 14.0,
                item.Raw / 14.0)));

        return terms;
    }

    private static TechV2Phase224FeatureTerm LocalTerm(string name, double raw, double familyWeight) =>
        new(name, raw, raw, familyWeight / 16.0, raw * familyWeight / 16.0);

    private static double Ratio(TechV2ShadowWindowDiagnostics window, string name) =>
        window.Ratios.First(item => item.Name == name).Raw;

    private static IReadOnlyList<TechV2Phase224PrevCurrSignal> BuildPrevCurrSignals(WindowContext context)
    {
        WindowContext previous = context.Previous ?? context;

        double previousStructural = previous.Window.Ratios.First(item => item.Name == "StructuralRatio").Raw;
        double currentStructural = context.Window.Ratios.First(item => item.Name == "StructuralRatio").Raw;
        double previousSharp = previous.Window.Ratios.First(item => item.Name == "SharpRatio").Raw;
        double currentSharp = context.Window.Ratios.First(item => item.Name == "SharpRatio").Raw;

        return
        [
            Signal("Geometry", previous.Decision.GeometryEvidence, context.Decision.GeometryEvidence),
            Signal("Movement", previous.Decision.MovementEvidence, context.Decision.MovementEvidence),
            Signal("Slider", previous.Decision.SliderEvidence, context.Decision.SliderEvidence),
            Signal("Motif", previous.Decision.MotifEvidence, context.Decision.MotifEvidence),
            Signal("StructuralRatio", previousStructural, currentStructural),
            Signal("SharpRatio", previousSharp, currentSharp),
            Signal("AngleSupport", previous.Source.AngleCoherenceWithSupport, context.Source.AngleCoherenceWithSupport),
            Signal("SpacingVariation", previous.Source.SpacingVariation, context.Source.SpacingVariation),
        ];
    }

    private static TechV2Phase224PrevCurrSignal Signal(string name, double previous, double current)
    {
        double aggregated = (previous + current) / 2.0;
        return new TechV2Phase224PrevCurrSignal(
            name,
            previous,
            current,
            aggregated,
            (aggregated - current) / 16.0);
    }

    private static TechV2Phase224TopologyAudit BuildTopologyAudit(WindowContext context)
    {
        WindowContext previous = context.Previous ?? context;

        double currentTopoSlider = TopologyPartInSlider(context.Source);
        double previousTopoSlider = TopologyPartInSlider(previous.Source);
        double aggregatedTopoSlider = (previousTopoSlider + currentTopoSlider) / 2.0;
        double deltaLocal = (aggregatedTopoSlider - currentTopoSlider) / 16.0;

        TechV2ShadowRatioValue currentS2S = context.Window.Ratios.First(item => item.Name == "SliderToSliderRatio");
        double deltaSupportAwareS2S = (currentS2S.SupportAware - currentS2S.Raw) / 14.0;

        int denominator = context.Source.ComplexSliderTransitionTopology.TransitionCount;
        int sliderToSlider = TopologyCount(context.Source.ComplexSliderTransitionTopology.SliderToSliderRatio, denominator);
        int sliderToCircle = TopologyCount(context.Source.ComplexSliderTransitionTopology.SliderToCircleRatio, denominator);
        int circleToSlider = TopologyCount(context.Source.ComplexSliderTransitionTopology.CircleToSliderRatio, denominator);
        int circleToCircle = Math.Max(0, denominator - sliderToSlider - sliderToCircle - circleToSlider);

        return new TechV2Phase224TopologyAudit(
            denominator,
            sliderToSlider,
            sliderToCircle,
            circleToSlider,
            circleToCircle,
            context.Source.ComplexSliderTransitionTopology.SliderToSliderRatio,
            context.Source.ComplexSliderTransitionTopology.SliderToCircleRatio,
            context.Source.ComplexSliderTransitionTopology.CircleToSliderRatio,
            context.Source.ComplexSliderTransitionTopology.CircleToCircleRatio,
            currentTopoSlider / 16.0 + currentS2S.Raw / 14.0,
            aggregatedTopoSlider / 16.0 + currentS2S.SupportAware / 14.0,
            deltaLocal + deltaSupportAwareS2S);
    }

    private static int TopologyCount(double ratio, int denominator) =>
        denominator <= 0 ? 0 : Math.Clamp((int)Math.Round(ratio * denominator, MidpointRounding.AwayFromZero), 0, denominator);

    private static TechV2Phase224SourceAttribution ComputeAttribution(WindowContext context)
    {
        WindowContext previous = context.Previous ?? context;

        IReadOnlyList<TechV2ShadowRatioValue> nonTopologyRatios = context.Window.Ratios
            .Where(item => item.Name != "SliderToSliderRatio")
            .ToArray();
        double deltaSupportAware = nonTopologyRatios.Sum(item => item.SupportAware - item.Raw) / 14.0;

        TechV2ShadowRatioValue s2s = context.Window.Ratios.First(item => item.Name == "SliderToSliderRatio");
        double deltaTopologyRatio = (s2s.SupportAware - s2s.Raw) / 14.0;

        double currentLocalNoTopo = ComputeLocalNoTopology(context.Source, context.Decision, context.Window.Ratios);
        double previousLocalNoTopo = ComputeLocalNoTopology(previous.Source, previous.Decision, previous.Window.Ratios);
        double aggregatedNoTopo = (currentLocalNoTopo + previousLocalNoTopo) / 2.0;
        double deltaPrevContext = (aggregatedNoTopo - currentLocalNoTopo) / 2.0;

        double currentTopoSlider = TopologyPartInSlider(context.Source);
        double previousTopoSlider = TopologyPartInSlider(previous.Source);
        double deltaTopologyLocal = ((previousTopoSlider + currentTopoSlider) / 2.0 - currentTopoSlider) / 16.0;

        double deltaTopology = deltaTopologyRatio + deltaTopologyLocal;

        double total = context.Window.CorrectedCombinedValue - context.Window.CurrentCombinedValue;
        double other = total - deltaSupportAware - deltaPrevContext - deltaTopology;

        return new TechV2Phase224SourceAttribution(total, deltaSupportAware, deltaPrevContext, deltaTopology, other);
    }

    private static double ComputeLocalNoTopology(
        TechV2ExperimentalWindowResult source,
        TechV2WindowDecision decision,
        IReadOnlyList<TechV2ShadowRatioValue> ratios)
    {
        double structural = ratios.First(item => item.Name == "StructuralRatio").Raw;
        double sharp = ratios.First(item => item.Name == "SharpRatio").Raw;
        double sliderNoTopo = SliderEvidenceWithoutTopology(source);

        return new[]
        {
            decision.GeometryEvidence,
            decision.MovementEvidence,
            sliderNoTopo,
            decision.MotifEvidence,
            structural,
            sharp,
            source.AngleCoherenceWithSupport,
            source.SpacingVariation,
        }.Average();
    }

    private static double SliderEvidenceWithoutTopology(TechV2ExperimentalWindowResult source)
    {
        double normalizedComplexRun = Math.Clamp((double)source.ComplexSliderRunLength / TechDetectorV2Experimental.WindowSize, 0, 1);
        double raw = source.SliderComplexity * SliderComplexityWeight
            + normalizedComplexRun * ComplexSliderRunWeight;
        return Math.Clamp(raw, 0, 1);
    }

    private static double TopologyPartInSlider(TechV2ExperimentalWindowResult source)
    {
        double withTopology = Math.Clamp(
            source.SliderComplexity * SliderComplexityWeight
            + source.ComplexSliderTransitionTopology.SliderToSliderRatio * SliderToSliderWeight
            + Math.Clamp((double)source.ComplexSliderRunLength / TechDetectorV2Experimental.WindowSize, 0, 1) * ComplexSliderRunWeight,
            0,
            1);
        double withoutTopology = SliderEvidenceWithoutTopology(source);
        return withTopology - withoutTopology;
    }

    private static TechV2Phase224DominantCause SelectDominantCause(IReadOnlyList<TechV2Phase224ExtremeWindow> windows)
    {
        double sa = windows.Sum(item => Math.Abs(item.Attribution.DeltaFromSupportAware));
        double prev = windows.Sum(item => Math.Abs(item.Attribution.DeltaFromPrevContext));
        double topo = windows.Sum(item => Math.Abs(item.Attribution.DeltaFromTopology));
        double other = windows.Sum(item => Math.Abs(item.Attribution.DeltaFromOther));

        double max = new[] { sa, prev, topo, other }.DefaultIfEmpty(0).Max();
        if (max <= 1e-9)
            return TechV2Phase224DominantCause.other;
        if (Math.Abs(max - sa) <= 1e-9)
            return TechV2Phase224DominantCause.smoothing_inflation;
        if (Math.Abs(max - prev) <= 1e-9)
            return TechV2Phase224DominantCause.previous_window_contamination;
        if (Math.Abs(max - topo) <= 1e-9)
            return TechV2Phase224DominantCause.topology_inflation;

        return TechV2Phase224DominantCause.formula_interaction;
    }

    private static IReadOnlyList<WindowContext> ScanDeterministicSample(
        IReadOnlyList<MlDatasetSample> samples,
        int maxMaps,
        IReadOnlyList<string>? pathTokens = null)
    {
        IEnumerable<string> sourcePaths = samples
            .Select(sample => sample.SourceFilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        if (pathTokens is { Count: > 0 })
        {
            sourcePaths = sourcePaths.Where(path => pathTokens.Any(token => MatchesToken(path, token)));
        }

        IReadOnlyList<string> paths = sourcePaths
            .OrderBy(path => path, StringComparer.Ordinal)
            .Take(Math.Max(0, maxMaps))
            .ToArray();

        List<WindowContext> all = [];
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

            IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> sourceByStart = windows.ToDictionary(item => item.StartIndex);
            IReadOnlyDictionary<int, TechV2WindowDecision> decisionByStart = decisions.ToDictionary(item => item.StartIndex);

            string map = string.IsNullOrWhiteSpace(beatmap.Title)
                ? Path.GetFileNameWithoutExtension(path)
                : beatmap.Title;
            string difficulty = string.IsNullOrWhiteSpace(beatmap.Version) ? "Unknown" : beatmap.Version;

            List<WindowContext> local = shadow.Windows
                .Where(window => sourceByStart.ContainsKey(window.StartIndex) && decisionByStart.ContainsKey(window.StartIndex))
                .OrderBy(window => window.StartIndex)
                .Select(window => new WindowContext(
                    map,
                    difficulty,
                    sourceByStart[window.StartIndex],
                    decisionByStart[window.StartIndex],
                    window,
                    null))
                .ToList();

            for (int index = 0; index < local.Count; index++)
            {
                local[index] = local[index] with
                {
                    Previous = index > 0 ? local[index - 1] : null
                };
            }

            all.AddRange(local);
        }

        return all;
    }

    private static bool TryParseCase(string caseId, out string mapKey, out int region)
    {
        mapKey = string.Empty;
        region = 0;

        int separator = caseId.LastIndexOf(" R", StringComparison.Ordinal);
        if (separator <= 0 || separator >= caseId.Length - 2)
            return false;

        mapKey = caseId[..separator];
        return int.TryParse(caseId[(separator + 2)..], out region) && region > 0;
    }

    // osu! filenames and [Metadata] titles do not always preserve punctuation
    // identically (for example "Don't" vs "Dont"). This only resolves the
    // explicitly requested local audit cases; it never expands discovery.
    internal static bool MatchesToken(string value, string token) =>
        NormalizeComparable(value).Contains(NormalizeComparable(token), StringComparison.Ordinal);

    private static string NormalizeComparable(string value) => new(value
        .Where(char.IsLetterOrDigit)
        .Select(char.ToUpperInvariant)
        .ToArray());

    private static string FormatTime(double milliseconds)
    {
        TimeSpan t = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}.{2:D3}", (int)t.TotalMinutes, t.Seconds, t.Milliseconds);
    }

    private sealed record WindowContext(
        string Map,
        string Difficulty,
        TechV2ExperimentalWindowResult Source,
        TechV2WindowDecision Decision,
        TechV2ShadowWindowDiagnostics Window,
        WindowContext? Previous);
}
