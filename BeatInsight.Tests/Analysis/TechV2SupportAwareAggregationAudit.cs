using BeatInsight.Models.Persistence;
using System.Globalization;
using System.Text;

namespace BeatInsight.Tests.Analysis;

internal enum TechV2Phase220CaseRole
{
    PositiveUnderestimated,
    NegativeOverestimated,
    Guardrail,
}

internal enum TechV2SupportAwarePrior
{
    Zero,
    NeutralHalf,
    CorpusMean,
}

internal enum TechV2AggregationWindowMode
{
    CurrentOnly,
    CurrentAndPrevious,
    CurrentAndNext,
    PreviousCurrentNext,
}

internal enum TechV2AggregationWeightMode
{
    SimpleMean,
    SupportWeighted,
}

internal sealed record TechV2Phase220CaseDefinition(
    string MapKey,
    int RegionNumber,
    TechV2Phase220CaseRole Role);

internal sealed record TechV2Phase220CaseBaseline(
    string CaseId,
    string MapKey,
    int RegionNumber,
    TechV2Phase220CaseRole Role,
    TechV2LocalHumanLabel HumanLabel,
    double RatioBaseline,
    double AggregationBaselineCurrentOnly,
    bool HasSingleSupportDenominator,
    TechV2LocalFeatureFailureRegion Region);

internal sealed record TechV2SupportAwareCandidate(
    string Name,
    double Alpha,
    TechV2SupportAwarePrior Prior,
    bool MultiplyBySupportConfidence);

internal sealed record TechV2AggregationCandidate(
    string Name,
    TechV2AggregationWindowMode WindowMode,
    TechV2AggregationWeightMode WeightMode);

internal sealed record TechV2PriorEffect(
    string Prior,
    double PositiveMean,
    double NegativeMean,
    double Contrast,
    string Interpretation);

internal sealed record TechV2Phase220CandidateSummary(
    string Candidate,
    string Kind,
    string PositiveCasesImproved,
    string NegativeCasesImproved,
    string KiraImpact,
    string FrozenImpact,
    string GloriousImpact,
    double ContrastBaseline,
    double ContrastCandidate);

internal sealed record TechV2Phase220AuditReport(
    IReadOnlyList<TechV2Phase220CaseBaseline> Cases,
    IReadOnlyList<TechV2PriorEffect> PriorEffects,
    IReadOnlyList<TechV2Phase220CandidateSummary> SupportAwareCandidates,
    IReadOnlyList<TechV2Phase220CandidateSummary> LocalAggregationCandidates,
    IReadOnlyList<string> SliderHeavyFindings,
    IReadOnlyList<string> FalsePositiveDenominatorFindings,
    string MainAnswers,
    string CandidateTable);

internal static class TechV2SupportAwareAggregationAudit
{
    private static readonly string[] RatioNames =
    [
        "Awkwardness",
        "SpacingVariation",
        "StructuralRatio",
        "SharpRatio",
        "AlternatingRatio",
        "ComplexSliderRatio",
        "SliderToSliderRatio",
    ];

    private static readonly string[] AggregationSignalNames =
    [
        "Geometry",
        "Movement",
        "Slider",
        "Motif",
        "StructuralRatio",
        "SharpRatio",
        "AngleSupport",
        "SpacingVariation",
    ];

    private static readonly IReadOnlyList<TechV2Phase220CaseDefinition> RequestedCases =
    [
        new("Exit", 5, TechV2Phase220CaseRole.PositiveUnderestimated),
        new("WorldEndsNow", 1, TechV2Phase220CaseRole.PositiveUnderestimated),
        new("TrustInYou", 1, TechV2Phase220CaseRole.PositiveUnderestimated),
        new("TaoHuaXiao", 1, TechV2Phase220CaseRole.NegativeOverestimated),
        new("HappppySong", 1, TechV2Phase220CaseRole.NegativeOverestimated),
        new("HappppySong", 2, TechV2Phase220CaseRole.NegativeOverestimated),
        new("TrustInYou", 2, TechV2Phase220CaseRole.NegativeOverestimated),
        new("Inferno", 1, TechV2Phase220CaseRole.NegativeOverestimated),
        new("Kira", 1, TechV2Phase220CaseRole.Guardrail),
        new("Frozen", 1, TechV2Phase220CaseRole.Guardrail),
        new("GloriousDays", 1, TechV2Phase220CaseRole.Guardrail),
    ];

    private static readonly IReadOnlyList<TechV2SupportAwareCandidate> SupportAwareCandidates =
    [
        new("SA-LAPLACE-A1-P0.5", 1.0, TechV2SupportAwarePrior.NeutralHalf, false),
        new("SA-SHRINK-A2-PCORPUS", 2.0, TechV2SupportAwarePrior.CorpusMean, false),
        new("SA-SMOOTHxCONF-A1-P0.5", 1.0, TechV2SupportAwarePrior.NeutralHalf, true),
    ];

    private static readonly IReadOnlyList<TechV2AggregationCandidate> AggregationCandidates =
    [
        new("LA-PREV+CURR-SIMPLE", TechV2AggregationWindowMode.CurrentAndPrevious, TechV2AggregationWeightMode.SimpleMean),
        new("LA-CURR+NEXT-WEIGHTED", TechV2AggregationWindowMode.CurrentAndNext, TechV2AggregationWeightMode.SupportWeighted),
        new("LA-PREV+CURR+NEXT-WEIGHTED", TechV2AggregationWindowMode.PreviousCurrentNext, TechV2AggregationWeightMode.SupportWeighted),
    ];

    internal static TechV2Phase220AuditReport Run(string databasePath)
    {
        TechV2LocalFeatureFailureAuditReport phase219 = TechV2LocalFeatureFailureAudit.Run(databasePath);
        IReadOnlyList<TechV2Phase220CaseBaseline> cases = BuildCases(phase219);

        Dictionary<string, double> corpusMeans = BuildCorpusRatioMeans(cases);
        IReadOnlyList<TechV2PriorEffect> priorEffects = BuildPriorEffects(cases, corpusMeans);

        IReadOnlyList<TechV2Phase220CandidateSummary> supportSummaries = SupportAwareCandidates
            .Select(candidate => SummarizeSupportAwareCandidate(cases, corpusMeans, candidate))
            .ToArray();

        IReadOnlyList<TechV2Phase220CandidateSummary> aggregationSummaries = AggregationCandidates
            .Select(candidate => SummarizeAggregationCandidate(cases, candidate))
            .ToArray();

        IReadOnlyList<string> sliderHeavy = BuildSliderHeavyFindings(cases);
        IReadOnlyList<string> denominatorFindings = BuildFalsePositiveDenominatorFindings(cases, corpusMeans);

        string answers = BuildMainAnswers(cases, supportSummaries, aggregationSummaries);
        string table = BuildCandidateTable(supportSummaries, aggregationSummaries);

        return new TechV2Phase220AuditReport(
            cases,
            priorEffects,
            supportSummaries,
            aggregationSummaries,
            sliderHeavy,
            denominatorFindings,
            answers,
            table);
    }

    internal static string FormatReport(TechV2Phase220AuditReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder text = new();
        text.AppendLine("===== TECH V2 PHASE 2.20 SUPPORT-AWARE RATIO & LOCAL AGGREGATION AUDIT (TEST-ONLY) =====");
        text.AppendLine("Aucun changement runtime. Aucun nouveau score final runtime. Aucun seuil runtime.");
        text.AppendLine("CORPUS PRIORITAIRE (réutilisation Phase 2.19):");
        foreach (TechV2Phase220CaseBaseline item in report.Cases)
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0} | role={1} | label={2} | ratio-baseline={3:F4} | agg-current={4:F4}",
                item.CaseId,
                item.Role,
                item.HumanLabel,
                item.RatioBaseline,
                item.AggregationBaselineCurrentOnly));
        }

        text.AppendLine("SUPPORT-AWARE PRIORS (alpha=1, smooth sans confidence):");
        foreach (TechV2PriorEffect effect in report.PriorEffects)
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  prior={0} | positive={1:F4} | negative={2:F4} | contrast={3:F4} | {4}",
                effect.Prior,
                effect.PositiveMean,
                effect.NegativeMean,
                effect.Contrast,
                effect.Interpretation));
        }

        text.AppendLine("SLIDER-HEAVY LINEAR CASES (agrégation locale):");
        foreach (string line in report.SliderHeavyFindings)
            text.AppendLine("  " + line);

        text.AppendLine("FALSE-POSITIVE DENOMINATOR=1 CHECK (smoothing):");
        foreach (string line in report.FalsePositiveDenominatorFindings)
            text.AppendLine("  " + line);

        text.AppendLine("CANDIDATE TABLE");
        text.Append(report.CandidateTable);

        text.AppendLine("MAIN ANSWERS");
        text.AppendLine(report.MainAnswers);

        return text.ToString();
    }

    private static IReadOnlyList<TechV2Phase220CaseBaseline> BuildCases(
        TechV2LocalFeatureFailureAuditReport phase219)
    {
        Dictionary<string, TechV2LocalFeatureFailureRegion> byId = phase219.Regions
            .ToDictionary(RegionId, StringComparer.Ordinal);

        List<TechV2Phase220CaseBaseline> items = [];
        foreach (TechV2Phase220CaseDefinition definition in RequestedCases)
        {
            string id = CaseId(definition.MapKey, definition.RegionNumber);
            if (!byId.TryGetValue(id, out TechV2LocalFeatureFailureRegion? region))
                continue;

            double ratioBaseline = ComputeRawRatioScore(region);
            double aggregationBaseline = ComputeAggregationScore(
                region,
                TechV2AggregationWindowMode.CurrentOnly,
                TechV2AggregationWeightMode.SimpleMean);

            bool hasSingleSupport = region.Windows
                .SelectMany(window => window.Ratios)
                .Any(ratio => ratio.Denominator == 1 && RatioNames.Contains(ratio.Name, StringComparer.Ordinal));

            items.Add(new TechV2Phase220CaseBaseline(
                id,
                definition.MapKey,
                definition.RegionNumber,
                definition.Role,
                region.HumanLabel,
                ratioBaseline,
                aggregationBaseline,
                hasSingleSupport,
                region));
        }

        return items
            .OrderBy(item => item.Role.ToString(), StringComparer.Ordinal)
            .ThenBy(item => item.MapKey, StringComparer.Ordinal)
            .ThenBy(item => item.RegionNumber)
            .ToArray();
    }

    private static Dictionary<string, double> BuildCorpusRatioMeans(
        IReadOnlyList<TechV2Phase220CaseBaseline> cases)
    {
        Dictionary<string, double> means = new(StringComparer.Ordinal);
        foreach (string name in RatioNames)
        {
            IReadOnlyList<double> values = cases
                .SelectMany(item => item.Region.Windows)
                .SelectMany(window => window.Ratios)
                .Where(ratio => string.Equals(ratio.Name, name, StringComparison.Ordinal))
                .Select(ratio => ratio.Ratio)
                .ToArray();

            means[name] = values.Count == 0 ? 0.5 : values.Average();
        }

        return means;
    }

    private static IReadOnlyList<TechV2PriorEffect> BuildPriorEffects(
        IReadOnlyList<TechV2Phase220CaseBaseline> cases,
        IReadOnlyDictionary<string, double> corpusMeans)
    {
        TechV2PriorEffect[] effects =
        [
            BuildPriorEffect(cases, corpusMeans, TechV2SupportAwarePrior.Zero),
            BuildPriorEffect(cases, corpusMeans, TechV2SupportAwarePrior.NeutralHalf),
            BuildPriorEffect(cases, corpusMeans, TechV2SupportAwarePrior.CorpusMean),
        ];

        return effects;
    }

    private static TechV2PriorEffect BuildPriorEffect(
        IReadOnlyList<TechV2Phase220CaseBaseline> cases,
        IReadOnlyDictionary<string, double> corpusMeans,
        TechV2SupportAwarePrior prior)
    {
        double positiveMean = MeanByRole(
            cases,
            TechV2Phase220CaseRole.PositiveUnderestimated,
            item => ComputeSupportAwareScore(item.Region, corpusMeans, 1.0, prior, false));
        double negativeMean = MeanByRole(
            cases,
            TechV2Phase220CaseRole.NegativeOverestimated,
            item => ComputeSupportAwareScore(item.Region, corpusMeans, 1.0, prior, false));
        double contrast = positiveMean - negativeMean;

        string interpretation = prior switch
        {
            TechV2SupportAwarePrior.Zero => "prior strict (pousse les faibles supports vers 0, réduit fortement les extrêmes).",
            TechV2SupportAwarePrior.NeutralHalf => "prior neutre (stabilise sans écraser totalement les ratios élevés/faibles).",
            _ => "prior corpus-mean (recentrage vers la moyenne observée du corpus prioritaire).",
        };

        return new TechV2PriorEffect(
            PriorName(prior),
            positiveMean,
            negativeMean,
            contrast,
            interpretation);
    }

    private static TechV2Phase220CandidateSummary SummarizeSupportAwareCandidate(
        IReadOnlyList<TechV2Phase220CaseBaseline> cases,
        IReadOnlyDictionary<string, double> corpusMeans,
        TechV2SupportAwareCandidate candidate)
    {
        IReadOnlyDictionary<string, double> deltas = cases
            .ToDictionary(
                item => item.CaseId,
                item => ComputeSupportAwareScore(
                    item.Region,
                    corpusMeans,
                    candidate.Alpha,
                    candidate.Prior,
                    candidate.MultiplyBySupportConfidence)
                    - item.RatioBaseline,
                StringComparer.Ordinal);

        int positiveImproved = CountImproved(cases, deltas, TechV2Phase220CaseRole.PositiveUnderestimated, positive: true);
        int negativeImproved = CountImproved(cases, deltas, TechV2Phase220CaseRole.NegativeOverestimated, positive: false);

        double baselineContrast = ContrastByRoles(
            cases,
            item => item.RatioBaseline);
        double candidateContrast = ContrastByRoles(
            cases,
            item => item.RatioBaseline + deltas[item.CaseId]);

        return new TechV2Phase220CandidateSummary(
            candidate.Name,
            "SupportAwareRatio",
            $"{positiveImproved}/{CountRole(cases, TechV2Phase220CaseRole.PositiveUnderestimated)}",
            $"{negativeImproved}/{CountRole(cases, TechV2Phase220CaseRole.NegativeOverestimated)}",
            ImpactText(deltas, "Kira R1"),
            ImpactText(deltas, "Frozen R1"),
            ImpactText(deltas, "GloriousDays R1"),
            baselineContrast,
            candidateContrast);
    }

    private static TechV2Phase220CandidateSummary SummarizeAggregationCandidate(
        IReadOnlyList<TechV2Phase220CaseBaseline> cases,
        TechV2AggregationCandidate candidate)
    {
        IReadOnlyDictionary<string, double> deltas = cases
            .ToDictionary(
                item => item.CaseId,
                item => ComputeAggregationScore(item.Region, candidate.WindowMode, candidate.WeightMode)
                    - item.AggregationBaselineCurrentOnly,
                StringComparer.Ordinal);

        int positiveImproved = CountImproved(cases, deltas, TechV2Phase220CaseRole.PositiveUnderestimated, positive: true);
        int negativeImproved = CountImproved(cases, deltas, TechV2Phase220CaseRole.NegativeOverestimated, positive: false);

        double baselineContrast = ContrastByRoles(cases, item => item.AggregationBaselineCurrentOnly);
        double candidateContrast = ContrastByRoles(cases, item => item.AggregationBaselineCurrentOnly + deltas[item.CaseId]);

        return new TechV2Phase220CandidateSummary(
            candidate.Name,
            "LocalAggregation",
            $"{positiveImproved}/{CountRole(cases, TechV2Phase220CaseRole.PositiveUnderestimated)}",
            $"{negativeImproved}/{CountRole(cases, TechV2Phase220CaseRole.NegativeOverestimated)}",
            ImpactText(deltas, "Kira R1"),
            ImpactText(deltas, "Frozen R1"),
            ImpactText(deltas, "GloriousDays R1"),
            baselineContrast,
            candidateContrast);
    }

    private static string BuildMainAnswers(
        IReadOnlyList<TechV2Phase220CaseBaseline> cases,
        IReadOnlyList<TechV2Phase220CandidateSummary> support,
        IReadOnlyList<TechV2Phase220CandidateSummary> aggregation)
    {
        TechV2Phase220CandidateSummary? bestSupport = support
            .OrderByDescending(item => ScoreSummary(item))
            .ThenBy(item => item.Candidate, StringComparer.Ordinal)
            .FirstOrDefault();
        TechV2Phase220CandidateSummary? bestAggregation = aggregation
            .OrderByDescending(item => ScoreSummary(item))
            .ThenBy(item => item.Candidate, StringComparer.Ordinal)
            .FirstOrDefault();

        StringBuilder text = new();
        text.AppendLine($"- Les vrais Tech sous-évalués remontent: meilleur support-aware={Display(bestSupport)}.");
        text.AppendLine($"- Les faux positifs surévalués descendent: meilleur agrégation locale={Display(bestAggregation)}.");
        text.AppendLine($"- Contraste Tech vs Non-Tech: baseline ratio={ContrastByRoles(cases, item => item.RatioBaseline):F4}, baseline agrégation={ContrastByRoles(cases, item => item.AggregationBaselineCurrentOnly):F4}.");
        text.AppendLine("- Aucun seuil runtime fixé; résultat strictement descriptif offline.");
        return text.ToString();
    }

    private static string BuildCandidateTable(
        IReadOnlyList<TechV2Phase220CandidateSummary> support,
        IReadOnlyList<TechV2Phase220CandidateSummary> aggregation)
    {
        StringBuilder text = new();
        text.AppendLine("Candidate | Positive cases improved | Negative cases improved | Kira impact | Frozen impact | GLORIOUS impact | Contrast baseline->candidate");

        foreach (TechV2Phase220CandidateSummary item in support.Concat(aggregation)
            .OrderBy(item => item.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.Candidate, StringComparer.Ordinal))
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0} | {1} | {2} | {3} | {4} | {5} | {6:F4}->{7:F4}",
                item.Candidate,
                item.PositiveCasesImproved,
                item.NegativeCasesImproved,
                item.KiraImpact,
                item.FrozenImpact,
                item.GloriousImpact,
                item.ContrastBaseline,
                item.ContrastCandidate));
        }

        return text.ToString();
    }

    private static IReadOnlyList<string> BuildSliderHeavyFindings(
        IReadOnlyList<TechV2Phase220CaseBaseline> cases)
    {
        string[] sliderHeavy = ["Exit R5", "WorldEndsNow R1", "GloriousDays R1", "TrustInYou R1"];

        List<string> findings = [];
        foreach (string id in sliderHeavy)
        {
            TechV2Phase220CaseBaseline? item = cases.FirstOrDefault(caseItem =>
                string.Equals(caseItem.CaseId, id, StringComparison.Ordinal));
            if (item is null)
            {
                findings.Add($"{id}: absent du corpus résolu.");
                continue;
            }

            double currentOnly = item.AggregationBaselineCurrentOnly;
            double aggregated = ComputeAggregationScore(
                item.Region,
                TechV2AggregationWindowMode.PreviousCurrentNext,
                TechV2AggregationWeightMode.SupportWeighted);

            findings.Add(string.Format(
                CultureInfo.InvariantCulture,
                "{0}: current={1:F4}, prev+curr+next(weighted)={2:F4}, delta={3:+0.0000;-0.0000;0.0000}",
                id,
                currentOnly,
                aggregated,
                aggregated - currentOnly));
        }

        return findings;
    }

    private static IReadOnlyList<string> BuildFalsePositiveDenominatorFindings(
        IReadOnlyList<TechV2Phase220CaseBaseline> cases,
        IReadOnlyDictionary<string, double> corpusMeans)
    {
        string[] targets = ["Inferno R1", "TaoHuaXiao R1", "HappppySong R1", "HappppySong R2"];
        List<string> findings = [];

        foreach (string id in targets)
        {
            TechV2Phase220CaseBaseline? item = cases.FirstOrDefault(caseItem =>
                string.Equals(caseItem.CaseId, id, StringComparison.Ordinal));
            if (item is null)
                continue;

            IReadOnlyList<TechV2RatioBreakdown> singleSupport = item.Region.Windows
                .SelectMany(window => window.Ratios)
                .Where(ratio => ratio.Denominator == 1 && RatioNames.Contains(ratio.Name, StringComparer.Ordinal))
                .ToArray();

            if (singleSupport.Count == 0)
            {
                findings.Add($"{id}: aucun ratio denominator=1.");
                continue;
            }

            double raw = singleSupport.Average(ratio => ratio.Ratio);
            double smooth = singleSupport.Average(ratio =>
                SmoothRatio(ratio.Numerator, ratio.Denominator, 1.0, TechV2SupportAwarePrior.NeutralHalf, ratio.Name, corpusMeans, false));

            findings.Add(string.Format(
                CultureInfo.InvariantCulture,
                "{0}: single-support={1}, raw={2:F4}, smoothed(P0.5,A1)={3:F4}, delta={4:+0.0000;-0.0000;0.0000}",
                id,
                singleSupport.Count,
                raw,
                smooth,
                smooth - raw));
        }

        return findings;
    }

    private static double ComputeRawRatioScore(TechV2LocalFeatureFailureRegion region)
    {
        IReadOnlyList<double> values = region.Windows
            .SelectMany(window => window.Ratios)
            .Where(ratio => RatioNames.Contains(ratio.Name, StringComparer.Ordinal))
            .Select(ratio => ratio.Ratio)
            .ToArray();

        return values.Count == 0 ? 0 : values.Average();
    }

    private static double ComputeSupportAwareScore(
        TechV2LocalFeatureFailureRegion region,
        IReadOnlyDictionary<string, double> corpusMeans,
        double alpha,
        TechV2SupportAwarePrior prior,
        bool withConfidence)
    {
        List<double> values = [];
        foreach (TechV2LocalFeatureFailureWindow window in region.Windows)
        {
            foreach (TechV2RatioBreakdown ratio in window.Ratios)
            {
                if (!RatioNames.Contains(ratio.Name, StringComparer.Ordinal))
                    continue;

                values.Add(SmoothRatio(
                    ratio.Numerator,
                    ratio.Denominator,
                    alpha,
                    prior,
                    ratio.Name,
                    corpusMeans,
                    withConfidence));
            }
        }

        return values.Count == 0 ? 0 : values.Average();
    }

    private static double SmoothRatio(
        int numerator,
        int denominator,
        double alpha,
        TechV2SupportAwarePrior prior,
        string ratioName,
        IReadOnlyDictionary<string, double> corpusMeans,
        bool withConfidence)
    {
        double priorValue = prior switch
        {
            TechV2SupportAwarePrior.Zero => 0,
            TechV2SupportAwarePrior.NeutralHalf => 0.5,
            _ => corpusMeans.TryGetValue(ratioName, out double mean) ? mean : 0.5,
        };

        double adjusted = (numerator + alpha * priorValue) / (Math.Max(0, denominator) + alpha);
        adjusted = Math.Clamp(adjusted, 0, 1);

        if (!withConfidence)
            return adjusted;

        double confidence = denominator <= 0
            ? 0
            : denominator / (denominator + alpha);
        return Math.Clamp(adjusted * confidence, 0, 1);
    }

    private static double ComputeAggregationScore(
        TechV2LocalFeatureFailureRegion region,
        TechV2AggregationWindowMode windowMode,
        TechV2AggregationWeightMode weightMode)
    {
        if (region.Windows.Count == 0)
            return 0;

        List<double> windowScores = [];
        for (int index = 0; index < region.Windows.Count; index++)
        {
            IReadOnlyList<int> neighborhood = SelectNeighborhoodIndices(index, region.Windows.Count, windowMode);
            if (neighborhood.Count == 0)
                continue;

            double geometry = AggregateSignal(neighborhood, region.Windows, weightMode, window => window.Geometry);
            double movement = AggregateSignal(neighborhood, region.Windows, weightMode, window => window.Movement);
            double slider = AggregateSignal(neighborhood, region.Windows, weightMode, window => window.Slider);
            double motif = AggregateSignal(neighborhood, region.Windows, weightMode, window => window.Motif);
            double structuralRatio = AggregateSignal(neighborhood, region.Windows, weightMode, window => window.StructuralRatio);
            double sharpRatio = AggregateSignal(neighborhood, region.Windows, weightMode, window => window.SharpRatio);
            double angleSupport = AggregateSignal(neighborhood, region.Windows, weightMode, window => window.AngleCoherenceWithSupport);
            double spacingVariation = AggregateSignal(neighborhood, region.Windows, weightMode, window => window.SpacingVariation);

            double signalMean = (geometry + movement + slider + motif + structuralRatio + sharpRatio + angleSupport + spacingVariation)
                / AggregationSignalNames.Length;
            windowScores.Add(Math.Clamp(signalMean, 0, 1));
        }

        return windowScores.Count == 0 ? 0 : windowScores.Average();
    }

    private static IReadOnlyList<int> SelectNeighborhoodIndices(
        int index,
        int count,
        TechV2AggregationWindowMode mode)
    {
        return mode switch
        {
            TechV2AggregationWindowMode.CurrentOnly => [index],
            TechV2AggregationWindowMode.CurrentAndPrevious => DistinctIndices(index - 1, index, count),
            TechV2AggregationWindowMode.CurrentAndNext => DistinctIndices(index, index + 1, count),
            _ => DistinctIndices(index - 1, index, index + 1, count),
        };
    }

    private static IReadOnlyList<int> DistinctIndices(int first, int second, int count)
    {
        List<int> indices = [];
        if (first >= 0 && first < count)
            indices.Add(first);
        if (second >= 0 && second < count && second != first)
            indices.Add(second);
        return indices;
    }

    private static IReadOnlyList<int> DistinctIndices(int first, int second, int third, int count)
    {
        List<int> indices = [];
        if (first >= 0 && first < count)
            indices.Add(first);
        if (second >= 0 && second < count && !indices.Contains(second))
            indices.Add(second);
        if (third >= 0 && third < count && !indices.Contains(third))
            indices.Add(third);
        return indices;
    }

    private static double AggregateSignal(
        IReadOnlyList<int> indices,
        IReadOnlyList<TechV2LocalFeatureFailureWindow> windows,
        TechV2AggregationWeightMode mode,
        Func<TechV2LocalFeatureFailureWindow, double> selector)
    {
        if (mode == TechV2AggregationWeightMode.SimpleMean)
            return indices.Select(index => selector(windows[index])).DefaultIfEmpty(0).Average();

        double weightSum = 0;
        double weightedSum = 0;
        foreach (int index in indices)
        {
            TechV2LocalFeatureFailureWindow window = windows[index];
            double weight = SupportWeight(window);
            weightedSum += selector(window) * weight;
            weightSum += weight;
        }

        return weightSum <= 0 ? 0 : weightedSum / weightSum;
    }

    private static double SupportWeight(TechV2LocalFeatureFailureWindow window)
    {
        int supportMass = window.Support.ValidTransitionCount
            + window.Support.StructuralPivotCount
            + window.Support.TopologyTransitionCount
            + window.Support.SliderCount;
        return Math.Max(1, supportMass);
    }

    private static int CountImproved(
        IReadOnlyList<TechV2Phase220CaseBaseline> cases,
        IReadOnlyDictionary<string, double> deltas,
        TechV2Phase220CaseRole role,
        bool positive)
    {
        const double epsilon = 1e-6;
        return cases
            .Where(item => item.Role == role)
            .Count(item => positive
                ? deltas[item.CaseId] > epsilon
                : deltas[item.CaseId] < -epsilon);
    }

    private static string ImpactText(
        IReadOnlyDictionary<string, double> deltas,
        string caseId)
    {
        if (!deltas.TryGetValue(caseId, out double delta))
            return "n/a";

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:+0.0000;-0.0000;0.0000}",
            delta);
    }

    private static double ContrastByRoles(
        IReadOnlyList<TechV2Phase220CaseBaseline> cases,
        Func<TechV2Phase220CaseBaseline, double> selector)
    {
        IReadOnlyList<TechV2Phase220CaseBaseline> techCases = cases
            .Where(item => item.Role is TechV2Phase220CaseRole.PositiveUnderestimated or TechV2Phase220CaseRole.Guardrail)
            .ToArray();
        IReadOnlyList<TechV2Phase220CaseBaseline> nonTechCases = cases
            .Where(item => item.Role == TechV2Phase220CaseRole.NegativeOverestimated)
            .ToArray();

        double tech = techCases.Count == 0 ? 0 : techCases.Average(selector);
        double nonTech = nonTechCases.Count == 0 ? 0 : nonTechCases.Average(selector);
        return tech - nonTech;
    }

    private static int CountRole(
        IReadOnlyList<TechV2Phase220CaseBaseline> cases,
        TechV2Phase220CaseRole role) =>
        cases.Count(item => item.Role == role);

    private static double MeanByRole(
        IReadOnlyList<TechV2Phase220CaseBaseline> cases,
        TechV2Phase220CaseRole role,
        Func<TechV2Phase220CaseBaseline, double> selector)
    {
        IReadOnlyList<TechV2Phase220CaseBaseline> selected = cases
            .Where(item => item.Role == role)
            .ToArray();
        return selected.Count == 0 ? 0 : selected.Average(selector);
    }

    private static double ScoreSummary(TechV2Phase220CandidateSummary summary)
    {
        int positiveImproved = ParseCount(summary.PositiveCasesImproved);
        int negativeImproved = ParseCount(summary.NegativeCasesImproved);
        double contrastGain = summary.ContrastCandidate - summary.ContrastBaseline;
        return positiveImproved + negativeImproved + contrastGain;
    }

    private static int ParseCount(string token)
    {
        int separator = token.IndexOf('/', StringComparison.Ordinal);
        if (separator <= 0)
            return 0;
        return int.TryParse(token[..separator], out int value) ? value : 0;
    }

    private static string Display(TechV2Phase220CandidateSummary? summary)
    {
        if (summary is null)
            return "aucun candidat exploitable";

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} (pos {1}, neg {2}, contrast {3:F4}->{4:F4})",
            summary.Candidate,
            summary.PositiveCasesImproved,
            summary.NegativeCasesImproved,
            summary.ContrastBaseline,
            summary.ContrastCandidate);
    }

    private static string PriorName(TechV2SupportAwarePrior prior) =>
        prior switch
        {
            TechV2SupportAwarePrior.Zero => "0.0",
            TechV2SupportAwarePrior.NeutralHalf => "0.5",
            _ => "corpus-mean",
        };

    private static string RegionId(TechV2LocalFeatureFailureRegion region) =>
        CaseId(region.MapKey, region.RegionNumber);

    private static string CaseId(string mapKey, int regionNumber) =>
        $"{mapKey} R{regionNumber}";
}
