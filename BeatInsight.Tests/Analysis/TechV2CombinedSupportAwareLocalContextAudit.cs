using System.Globalization;
using System.Text;

namespace BeatInsight.Tests.Analysis;

internal enum TechV2Phase221Variant
{
    A_Baseline,
    B_SupportAwareOnly,
    C_LocalAggregationOnly,
    D_SupportAwarePlusLocalAggregation,
}

internal enum TechV2Phase221CombinationEffect
{
    additive,
    redundant,
    conflicting,
}

internal sealed record TechV2Phase221CaseScores(
    string CaseId,
    TechV2Phase220CaseRole Role,
    double BaselineA,
    double SupportAwareB,
    double LocalAggregationC,
    double CombinedD);

internal sealed record TechV2Phase221VariantSummary(
    TechV2Phase221Variant Variant,
    string PositiveCasesImproved,
    string NegativeCasesImproved,
    double TechVsNonTechContrast,
    double KiraDelta,
    double FrozenDelta,
    double GloriousDelta);

internal sealed record TechV2Phase221AuditReport(
    IReadOnlyList<TechV2Phase221VariantSummary> Variants,
    IReadOnlyList<TechV2Phase221CaseScores> CaseScores,
    TechV2Phase221CombinationEffect CombinationEffect,
    TechV2Phase221Variant BestCombinedVariant,
    string PositiveImprovement,
    string NegativeImprovement,
    string GuardrailImpact,
    string ContrastImprovement,
    string RuntimeConsideration);

internal static class TechV2CombinedSupportAwareLocalContextAudit
{
    private const double Epsilon = 1e-6;

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

    private static readonly string[] TargetCases =
    [
        "Exit R5",
        "WorldEndsNow R1",
        "TrustInYou R1",
        "Inferno R1",
        "TaoHuaXiao R1",
        "HappppySong R1",
        "HappppySong R2",
    ];

    internal static TechV2Phase221AuditReport Run(string databasePath)
    {
        TechV2Phase220AuditReport phase220 = TechV2SupportAwareAggregationAudit.Run(databasePath);
        IReadOnlyList<TechV2Phase220CaseBaseline> cases = phase220.Cases;
        Dictionary<string, double> corpusMeans = BuildCorpusRatioMeans(cases);

        IReadOnlyList<TechV2Phase221CaseScores> scores = cases
            .Select(item => BuildCaseScores(item, corpusMeans))
            .ToArray();

        TechV2Phase221VariantSummary variantA = BuildSummary(scores, TechV2Phase221Variant.A_Baseline);
        TechV2Phase221VariantSummary variantB = BuildSummary(scores, TechV2Phase221Variant.B_SupportAwareOnly);
        TechV2Phase221VariantSummary variantC = BuildSummary(scores, TechV2Phase221Variant.C_LocalAggregationOnly);
        TechV2Phase221VariantSummary variantD = BuildSummary(scores, TechV2Phase221Variant.D_SupportAwarePlusLocalAggregation);

        IReadOnlyList<TechV2Phase221VariantSummary> variants =
        [
            variantA,
            variantB,
            variantC,
            variantD,
        ];

        TechV2Phase221CombinationEffect effect = ClassifyCombinationEffect(variantB, variantC, variantD);
        TechV2Phase221Variant best = SelectBestVariant(variants);

        string positiveImprovement = $"B={variantB.PositiveCasesImproved}, C={variantC.PositiveCasesImproved}, D={variantD.PositiveCasesImproved}";
        string negativeImprovement = $"B={variantB.NegativeCasesImproved}, C={variantC.NegativeCasesImproved}, D={variantD.NegativeCasesImproved}";
        string guardrailImpact = string.Format(
            CultureInfo.InvariantCulture,
            "Deltas D | Kira={0:+0.0000;-0.0000;0.0000}, Frozen={1:+0.0000;-0.0000;0.0000}, GLORIOUS={2:+0.0000;-0.0000;0.0000}",
            variantD.KiraDelta,
            variantD.FrozenDelta,
            variantD.GloriousDelta);
        string contrastImprovement = string.Format(
            CultureInfo.InvariantCulture,
            "A={0:F4}, B={1:F4}, C={2:F4}, D={3:F4}",
            variantA.TechVsNonTechContrast,
            variantB.TechVsNonTechContrast,
            variantC.TechVsNonTechContrast,
            variantD.TechVsNonTechContrast);
        string runtimeConsideration = EvaluateRuntimeConsideration(variantB, variantC, variantD, best, effect);

        return new TechV2Phase221AuditReport(
            variants,
            scores,
            effect,
            best,
            positiveImprovement,
            negativeImprovement,
            guardrailImpact,
            contrastImprovement,
            runtimeConsideration);
    }

    internal static string FormatReport(TechV2Phase221AuditReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder text = new();
        text.AppendLine("===== TECH V2 PHASE 2.21 COMBINED SUPPORT-AWARE + LOCAL CONTEXT AUDIT (TEST-ONLY) =====");
        text.AppendLine("Candidats réutilisés à l'identique: SA-LAPLACE-A1-P0.5, LA-PREV+CURR-SIMPLE.");
        text.AppendLine("Aucun changement runtime. Aucun seuil runtime. Aucune formule runtime modifiée.");

        text.AppendLine("VARIANT SUMMARY");
        foreach (TechV2Phase221VariantSummary item in report.Variants)
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0} | pos={1} | neg={2} | contrast={3:F4} | Kira={4:+0.0000;-0.0000;0.0000} | Frozen={5:+0.0000;-0.0000;0.0000} | GLORIOUS={6:+0.0000;-0.0000;0.0000}",
                item.Variant,
                item.PositiveCasesImproved,
                item.NegativeCasesImproved,
                item.TechVsNonTechContrast,
                item.KiraDelta,
                item.FrozenDelta,
                item.GloriousDelta));
        }

        text.AppendLine("TARGET CASES BEFORE/AFTER (A->B/C/D)");
        foreach (string caseId in TargetCases)
        {
            TechV2Phase221CaseScores? item = report.CaseScores.FirstOrDefault(x =>
                string.Equals(x.CaseId, caseId, StringComparison.Ordinal));
            if (item is null)
                continue;

            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0} | A={1:F4} | B={2:F4} ({3:+0.0000;-0.0000;0.0000}) | C={4:F4} ({5:+0.0000;-0.0000;0.0000}) | D={6:F4} ({7:+0.0000;-0.0000;0.0000})",
                item.CaseId,
                item.BaselineA,
                item.SupportAwareB,
                item.SupportAwareB - item.BaselineA,
                item.LocalAggregationC,
                item.LocalAggregationC - item.BaselineA,
                item.CombinedD,
                item.CombinedD - item.BaselineA));
        }

        text.AppendLine($"COMBINATION EFFECT: {report.CombinationEffect}");
        text.AppendLine($"BEST COMBINED VARIANT: {report.BestCombinedVariant}");
        text.AppendLine("RETURN SUMMARY");
        text.AppendLine("  positive improvement: " + report.PositiveImprovement);
        text.AppendLine("  negative improvement: " + report.NegativeImprovement);
        text.AppendLine("  guardrail impact: " + report.GuardrailImpact);
        text.AppendLine("  contrast improvement: " + report.ContrastImprovement);
        text.AppendLine("  worth runtime consideration: " + report.RuntimeConsideration);

        return text.ToString();
    }

    private static TechV2Phase221CaseScores BuildCaseScores(
        TechV2Phase220CaseBaseline item,
        IReadOnlyDictionary<string, double> corpusMeans)
    {
        double supportAwareRatio = ComputeSupportAwareScore(item.Region, corpusMeans);
        double localAggregation = ComputeAggregationScore(item.Region);

        double baseline = Compose(item.RatioBaseline, item.AggregationBaselineCurrentOnly);
        double supportOnly = Compose(supportAwareRatio, item.AggregationBaselineCurrentOnly);
        double localOnly = Compose(item.RatioBaseline, localAggregation);
        double combined = Compose(supportAwareRatio, localAggregation);

        return new TechV2Phase221CaseScores(
            item.CaseId,
            item.Role,
            baseline,
            supportOnly,
            localOnly,
            combined);
    }

    private static TechV2Phase221VariantSummary BuildSummary(
        IReadOnlyList<TechV2Phase221CaseScores> scores,
        TechV2Phase221Variant variant)
    {
        int posTotal = scores.Count(x => x.Role == TechV2Phase220CaseRole.PositiveUnderestimated);
        int negTotal = scores.Count(x => x.Role == TechV2Phase220CaseRole.NegativeOverestimated);

        int posImproved = scores
            .Where(x => x.Role == TechV2Phase220CaseRole.PositiveUnderestimated)
            .Count(x => VariantScore(x, variant) > x.BaselineA + Epsilon);
        int negImproved = scores
            .Where(x => x.Role == TechV2Phase220CaseRole.NegativeOverestimated)
            .Count(x => VariantScore(x, variant) < x.BaselineA - Epsilon);

        double contrast = ComputeContrast(scores, variant);
        double kiraDelta = DeltaByCase(scores, "Kira R1", variant);
        double frozenDelta = DeltaByCase(scores, "Frozen R1", variant);
        double gloriousDelta = DeltaByCase(scores, "GloriousDays R1", variant);

        return new TechV2Phase221VariantSummary(
            variant,
            $"{posImproved}/{posTotal}",
            $"{negImproved}/{negTotal}",
            contrast,
            kiraDelta,
            frozenDelta,
            gloriousDelta);
    }

    private static TechV2Phase221CombinationEffect ClassifyCombinationEffect(
        TechV2Phase221VariantSummary b,
        TechV2Phase221VariantSummary c,
        TechV2Phase221VariantSummary d)
    {
        int bPos = ParseNumerator(b.PositiveCasesImproved);
        int cPos = ParseNumerator(c.PositiveCasesImproved);
        int dPos = ParseNumerator(d.PositiveCasesImproved);

        int bNeg = ParseNumerator(b.NegativeCasesImproved);
        int cNeg = ParseNumerator(c.NegativeCasesImproved);
        int dNeg = ParseNumerator(d.NegativeCasesImproved);

        double bestIndividualContrast = Math.Max(b.TechVsNonTechContrast, c.TechVsNonTechContrast);

        bool additive = dPos >= Math.Max(bPos, cPos)
            && dNeg >= Math.Max(bNeg, cNeg)
            && d.TechVsNonTechContrast >= bestIndividualContrast - Epsilon
            && (dPos > Math.Max(bPos, cPos)
                || dNeg > Math.Max(bNeg, cNeg)
                || d.TechVsNonTechContrast > bestIndividualContrast + Epsilon);

        if (additive)
            return TechV2Phase221CombinationEffect.additive;

        bool conflicting = dPos < Math.Min(bPos, cPos)
            && dNeg < Math.Min(bNeg, cNeg)
            && d.TechVsNonTechContrast < Math.Min(b.TechVsNonTechContrast, c.TechVsNonTechContrast) - Epsilon;

        if (conflicting)
            return TechV2Phase221CombinationEffect.conflicting;

        return TechV2Phase221CombinationEffect.redundant;
    }

    private static TechV2Phase221Variant SelectBestVariant(
        IReadOnlyList<TechV2Phase221VariantSummary> variants)
    {
        return variants
            .Where(v => v.Variant != TechV2Phase221Variant.A_Baseline)
            .OrderByDescending(VariantUtility)
            .ThenBy(v => v.Variant.ToString(), StringComparer.Ordinal)
            .First()
            .Variant;
    }

    private static string EvaluateRuntimeConsideration(
        TechV2Phase221VariantSummary b,
        TechV2Phase221VariantSummary c,
        TechV2Phase221VariantSummary d,
        TechV2Phase221Variant best,
        TechV2Phase221CombinationEffect effect)
    {
        double guardrailPenalty = Math.Min(0, d.KiraDelta) + Math.Min(0, d.FrozenDelta) + Math.Min(0, d.GloriousDelta);
        bool combinedWins = best == TechV2Phase221Variant.D_SupportAwarePlusLocalAggregation;
        bool noMajorGuardrailDrop = guardrailPenalty > -0.05;
        bool betterThanIndividuals = d.TechVsNonTechContrast >= b.TechVsNonTechContrast - Epsilon
            && d.TechVsNonTechContrast >= c.TechVsNonTechContrast - Epsilon;

        return combinedWins && noMajorGuardrailDrop && betterThanIndividuals && effect != TechV2Phase221CombinationEffect.conflicting
            ? "yes (offline evidence only)"
            : "no (offline evidence insufficient)";
    }

    private static double VariantUtility(TechV2Phase221VariantSummary variant)
    {
        int pos = ParseNumerator(variant.PositiveCasesImproved);
        int neg = ParseNumerator(variant.NegativeCasesImproved);
        return pos + neg + variant.TechVsNonTechContrast;
    }

    private static double Compose(double ratioComponent, double aggregationComponent) =>
        Math.Clamp((ratioComponent + aggregationComponent) / 2.0, 0, 1);

    private static int ParseNumerator(string value)
    {
        int separator = value.IndexOf('/', StringComparison.Ordinal);
        if (separator <= 0)
            return 0;
        return int.TryParse(value[..separator], out int parsed) ? parsed : 0;
    }

    private static double VariantScore(TechV2Phase221CaseScores score, TechV2Phase221Variant variant) =>
        variant switch
        {
            TechV2Phase221Variant.A_Baseline => score.BaselineA,
            TechV2Phase221Variant.B_SupportAwareOnly => score.SupportAwareB,
            TechV2Phase221Variant.C_LocalAggregationOnly => score.LocalAggregationC,
            _ => score.CombinedD,
        };

    private static double DeltaByCase(
        IReadOnlyList<TechV2Phase221CaseScores> scores,
        string caseId,
        TechV2Phase221Variant variant)
    {
        TechV2Phase221CaseScores? item = scores.FirstOrDefault(score =>
            string.Equals(score.CaseId, caseId, StringComparison.Ordinal));
        if (item is null)
            return 0;
        return VariantScore(item, variant) - item.BaselineA;
    }

    private static double ComputeContrast(
        IReadOnlyList<TechV2Phase221CaseScores> scores,
        TechV2Phase221Variant variant)
    {
        IReadOnlyList<TechV2Phase221CaseScores> tech = scores
            .Where(score => score.Role is TechV2Phase220CaseRole.PositiveUnderestimated or TechV2Phase220CaseRole.Guardrail)
            .ToArray();
        IReadOnlyList<TechV2Phase221CaseScores> nonTech = scores
            .Where(score => score.Role == TechV2Phase220CaseRole.NegativeOverestimated)
            .ToArray();

        double techMean = tech.Count == 0 ? 0 : tech.Average(score => VariantScore(score, variant));
        double nonTechMean = nonTech.Count == 0 ? 0 : nonTech.Average(score => VariantScore(score, variant));
        return techMean - nonTechMean;
    }

    private static Dictionary<string, double> BuildCorpusRatioMeans(
        IReadOnlyList<TechV2Phase220CaseBaseline> cases)
    {
        Dictionary<string, double> means = new(StringComparer.Ordinal);
        foreach (string ratioName in RatioNames)
        {
            IReadOnlyList<double> values = cases
                .SelectMany(item => item.Region.Windows)
                .SelectMany(window => window.Ratios)
                .Where(ratio => string.Equals(ratio.Name, ratioName, StringComparison.Ordinal))
                .Select(ratio => ratio.Ratio)
                .ToArray();

            means[ratioName] = values.Count == 0 ? 0.5 : values.Average();
        }

        return means;
    }

    private static double ComputeSupportAwareScore(
        TechV2LocalFeatureFailureRegion region,
        IReadOnlyDictionary<string, double> corpusMeans)
    {
        List<double> values = [];
        foreach (TechV2LocalFeatureFailureWindow window in region.Windows)
        {
            foreach (TechV2RatioBreakdown ratio in window.Ratios)
            {
                if (!RatioNames.Contains(ratio.Name, StringComparer.Ordinal))
                    continue;

                values.Add(SmoothRatio(ratio.Numerator, ratio.Denominator, ratio.Name, corpusMeans));
            }
        }

        return values.Count == 0 ? 0 : values.Average();
    }

    private static double SmoothRatio(
        int numerator,
        int denominator,
        string ratioName,
        IReadOnlyDictionary<string, double> corpusMeans)
    {
        const double alpha = 1.0;
        const double prior = 0.5;

        _ = ratioName;
        _ = corpusMeans;

        double adjusted = (numerator + alpha * prior) / (Math.Max(0, denominator) + alpha);
        return Math.Clamp(adjusted, 0, 1);
    }

    private static double ComputeAggregationScore(TechV2LocalFeatureFailureRegion region)
    {
        if (region.Windows.Count == 0)
            return 0;

        List<double> windowScores = [];
        for (int index = 0; index < region.Windows.Count; index++)
        {
            IReadOnlyList<int> neighborhood = SelectCurrentAndPrevious(index, region.Windows.Count);
            if (neighborhood.Count == 0)
                continue;

            double geometry = neighborhood.Select(i => region.Windows[i].Geometry).Average();
            double movement = neighborhood.Select(i => region.Windows[i].Movement).Average();
            double slider = neighborhood.Select(i => region.Windows[i].Slider).Average();
            double motif = neighborhood.Select(i => region.Windows[i].Motif).Average();
            double structuralRatio = neighborhood.Select(i => region.Windows[i].StructuralRatio).Average();
            double sharpRatio = neighborhood.Select(i => region.Windows[i].SharpRatio).Average();
            double angleSupport = neighborhood.Select(i => region.Windows[i].AngleCoherenceWithSupport).Average();
            double spacingVariation = neighborhood.Select(i => region.Windows[i].SpacingVariation).Average();

            double mean = (geometry + movement + slider + motif + structuralRatio + sharpRatio + angleSupport + spacingVariation) / 8.0;
            windowScores.Add(Math.Clamp(mean, 0, 1));
        }

        return windowScores.Count == 0 ? 0 : windowScores.Average();
    }

    private static IReadOnlyList<int> SelectCurrentAndPrevious(int index, int count)
    {
        List<int> values = [];
        if (index - 1 >= 0 && index - 1 < count)
            values.Add(index - 1);
        if (index >= 0 && index < count)
            values.Add(index);
        return values;
    }
}
