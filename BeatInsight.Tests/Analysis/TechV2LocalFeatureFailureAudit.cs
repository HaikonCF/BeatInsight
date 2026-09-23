using BeatInsight.Analysis;
using BeatInsight.Models;
using BeatInsight.Models.Persistence;
using BeatInsight.Parser;
using System.Globalization;
using System.IO;
using System.Text;

namespace BeatInsight.Tests.Analysis;

internal enum TechV2FeatureFailureType
{
    SUPPORT_PROBLEM,
    NORMALIZATION_PROBLEM,
    DEFINITION_PROBLEM,
    WINDOWING_PROBLEM,
    SIGNAL_NOT_DISCRIMINANT,
}

internal sealed record TechV2LocalFeatureFailureCaseDefinition(
    string MapKey,
    int RegionNumber,
    TechV2LocalHumanLabel HumanLabel,
    bool Priority,
    string Group);

internal sealed record TechV2RatioBreakdown(
    string Name,
    int Numerator,
    int Denominator,
    double Ratio);

internal sealed record TechV2NormalizationTrace(
    string Feature,
    double RawValue,
    double NormalizedValue,
    bool ClampApplied,
    bool SaturationApplied);

internal sealed record TechV2WindowSupportCounters(
    int HitObjectCount,
    int CircleCount,
    int SliderCount,
    int ComplexSliderCount,
    int ValidTransitionCount,
    int StructuralPivotCount,
    int SharpTransitionCount,
    int AwkwardTransitionCount,
    int SpacingVariationTransitionCount,
    int StructuralTransitionCount,
    int CompactTransitionCount,
    int AlternatingTransitionCount,
    int TopologyTransitionCount,
    int SliderToSliderTransitionCount,
    int RawTripleCount,
    int SpinnerFilteredTripleCount,
    int NonPositiveIntervalTripleCount,
    int DistanceFilteredTripleCount);

internal sealed record TechV2LocalFeatureFailureWindow(
    double StartTime,
    double EndTime,
    string DecisionKind,
    double Geometry,
    double Movement,
    double Slider,
    double Motif,
    double GeometryIrregularity,
    double AngleCoherenceWithSupport,
    double RhythmIrregularity,
    double Awkwardness,
    double SpacingVariation,
    double SliderComplexity,
    double SliderToSliderRatio,
    int ComplexSliderRunLength,
    double CoupledTechnicalMotifContinuity,
    double SharpRatio,
    double StructuralRatio,
    double CompactRatio,
    double AlternatingRatio,
    double OverlapPerSlider,
    TechV2WindowSupportCounters Support,
    IReadOnlyList<TechV2RatioBreakdown> Ratios,
    IReadOnlyList<TechV2NormalizationTrace> Normalization);

internal sealed record TechV2LocalFeatureFailureRegion(
    string MapKey,
    int RegionNumber,
    TechV2LocalHumanLabel HumanLabel,
    string Group,
    double StartTime,
    double EndTime,
    double SupportedProduct,
    double StructuralValidity,
    double TechnicalIntensity,
    double SupportConfidence,
    IReadOnlyList<TechV2LocalFeatureFailureWindow> Windows,
    string ZeroSupportExplanation,
    string WindowAlignmentFinding);

internal sealed record TechV2PerFeatureFailure(
    string Feature,
    string PositiveLowScoringCases,
    string NegativeHighScoringCases,
    string FailureDirection,
    IReadOnlyList<TechV2FeatureFailureType> FailureTypes);

internal sealed record TechV2LocalFeatureFailureAuditReport(
    IReadOnlyList<TechV2LocalFeatureFailureRegion> Regions,
    IReadOnlyList<TechV2PerFeatureFailure> PerFeatureFailures,
    IReadOnlyList<string> DenominatorPathologies,
    IReadOnlyList<string> ZeroSupportExplanations,
    IReadOnlyList<string> WindowAlignmentFindings,
    IReadOnlyList<string> NormalizationFindings,
    IReadOnlyList<string> TopRootCauses,
    string BestNextFixCandidate);

internal static class TechV2LocalFeatureFailureAudit
{
    private const double GeometryIrregularityWeight = 0.65;
    private const double AngleCoherenceWeight = 0.35;
    private const double RhythmIrregularityWeight = 0.45;
    private const double AwkwardnessWeight = 0.35;
    private const double SpacingVariationWeight = 0.20;
    private const double SliderComplexityWeight = 0.55;
    private const double SliderToSliderWeight = 0.25;
    private const double ComplexSliderRunWeight = 0.20;

    private static readonly IReadOnlyList<TechV2LocalFeatureFailureCaseDefinition> PriorityCases =
    [
        new("TrustInYou", 1, TechV2LocalHumanLabel.TechWeak, true, "Positive"),
        new("WorldEndsNow", 1, TechV2LocalHumanLabel.Tech, true, "Positive"),
        new("Exit", 5, TechV2LocalHumanLabel.Tech, true, "Positive"),
        new("Kira", 1, TechV2LocalHumanLabel.Tech, true, "Positive"),
        new("Frozen", 1, TechV2LocalHumanLabel.Tech, true, "Positive"),
        new("GloriousDays", 1, TechV2LocalHumanLabel.Tech, true, "Positive"),
        new("TaoHuaXiao", 1, TechV2LocalHumanLabel.NonTech, true, "Negative"),
        new("Howling", 1, TechV2LocalHumanLabel.NonTech, true, "Negative"),
        new("RockOnExpert", 1, TechV2LocalHumanLabel.NonTech, true, "Negative"),
        new("TrustInYou", 2, TechV2LocalHumanLabel.NonTech, true, "Negative"),
        new("HappppySong", 1, TechV2LocalHumanLabel.Borderline, false, "NegativeOptional"),
        new("HappppySong", 2, TechV2LocalHumanLabel.Borderline, false, "NegativeOptional"),
        new("FinestSky", 1, TechV2LocalHumanLabel.NonTech, false, "NegativeOptional"),
        new("Inferno", 1, TechV2LocalHumanLabel.NonTech, false, "NegativeOptional"),
    ];

    internal static TechV2LocalFeatureFailureAuditReport Run(string databasePath)
    {
        IReadOnlyList<MlDatasetSample> samples =
            TechV2StrongTechForensicAudit.LoadLocalCacheSamples(databasePath);
        TechV2HumanGroundedAuditReport grounded =
            TechV2HumanGroundedStrongTechAudit.Run(databasePath);
        IReadOnlyDictionary<string, TechV2HumanGroundedMap> byKey = grounded.Maps
            .Where(map => map.Resolved)
            .ToDictionary(map => map.Reference.Key, StringComparer.Ordinal);

        List<TechV2LocalFeatureFailureRegion> regions = [];

        foreach (TechV2LocalFeatureFailureCaseDefinition definition in PriorityCases)
        {
            if (!byKey.TryGetValue(definition.MapKey, out TechV2HumanGroundedMap? map)
                || map.Path is null)
            {
                continue;
            }

            if (definition.RegionNumber < 1
                || definition.RegionNumber > map.Presence.Regions.Count)
            {
                continue;
            }

            TechV2LocalFeatureFailureRegion? region = AnalyzeRegion(
                map,
                definition,
                samples);

            if (region is not null)
                regions.Add(region);
        }

        regions = regions
            .OrderBy(region => region.Group, StringComparer.Ordinal)
            .ThenBy(region => region.MapKey, StringComparer.Ordinal)
            .ThenBy(region => region.RegionNumber)
            .ToList();

        IReadOnlyList<TechV2PerFeatureFailure> features = BuildPerFeatureFailures(regions);
        IReadOnlyList<string> denominatorPathologies = BuildDenominatorPathologies(regions);
        IReadOnlyList<string> zeroSupport = regions
            .Where(region => !string.Equals(region.ZeroSupportExplanation, "none", StringComparison.Ordinal))
            .Select(region => $"{region.MapKey} R{region.RegionNumber}: {region.ZeroSupportExplanation}")
            .ToArray();
        IReadOnlyList<string> windowFindings = regions
            .Where(region => !string.Equals(region.WindowAlignmentFinding, "none", StringComparison.Ordinal))
            .Select(region => $"{region.MapKey} R{region.RegionNumber}: {region.WindowAlignmentFinding}")
            .ToArray();
        IReadOnlyList<string> normalizationFindings = BuildNormalizationFindings(regions);

        IReadOnlyList<string> rootCauses =
        [
            "Support insuffisant (pivots/transitions faibles ou nuls) sur plusieurs cas positifs sous-scorés.",
            "Compression de normalisation (clamp/saturation) sur certaines familles, réduisant le contraste positif/négatif.",
            "Désalignement de fenêtre local (signal Tech mieux visible sur voisinage +/-1 ou agrégations 2-3 fenêtres).",
        ];

        return new TechV2LocalFeatureFailureAuditReport(
            regions,
            features,
            denominatorPathologies,
            zeroSupport,
            windowFindings,
            normalizationFindings,
            rootCauses,
            "Support-aware ratio avec dénominateur robuste + agrégation locale adjacente (2-3 fenêtres) en audit, avant toute modification runtime.");
    }

    internal static string FormatReport(TechV2LocalFeatureFailureAuditReport report)
    {
        StringBuilder text = new();
        text.AppendLine("===== TECH V2 PHASE 2.19 LOCAL FEATURE FAILURE AUDIT (TEST-ONLY) =====");
        text.AppendLine("Aucun changement runtime. Aucun nouveau score runtime. Analyse strictement diagnostique.");
        text.AppendLine("POSITIVE CASES UNDER-SCORED (P2 SupportedProduct asc)");
        foreach (TechV2LocalFeatureFailureRegion region in report.Regions
            .Where(region => region.HumanLabel is TechV2LocalHumanLabel.Tech or TechV2LocalHumanLabel.TechWeak)
            .OrderBy(region => region.SupportedProduct)
            .ThenBy(region => region.MapKey, StringComparer.Ordinal))
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0} R{1} {2} | P2={3:F3} | Structural={4:F3} | Intensity={5:F3} | Support={6:F3}",
                region.MapKey,
                region.RegionNumber,
                region.HumanLabel,
                region.SupportedProduct,
                region.StructuralValidity,
                region.TechnicalIntensity,
                region.SupportConfidence));
        }

        text.AppendLine("NEGATIVE CASES OVER-SCORED (P2 SupportedProduct desc)");
        foreach (TechV2LocalFeatureFailureRegion region in report.Regions
            .Where(region => region.HumanLabel is TechV2LocalHumanLabel.NonTech or TechV2LocalHumanLabel.Borderline)
            .OrderByDescending(region => region.SupportedProduct)
            .ThenBy(region => region.MapKey, StringComparer.Ordinal))
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0} R{1} {2} | P2={3:F3} | Structural={4:F3} | Intensity={5:F3} | Support={6:F3}",
                region.MapKey,
                region.RegionNumber,
                region.HumanLabel,
                region.SupportedProduct,
                region.StructuralValidity,
                region.TechnicalIntensity,
                region.SupportConfidence));
        }

        text.AppendLine("PER-REGION / PER-WINDOW FEATURE TRACE");
        foreach (TechV2LocalFeatureFailureRegion region in report.Regions)
        {
            text.AppendLine($"  {region.MapKey} R{region.RegionNumber} ({region.HumanLabel}) | {FormatTime(region.StartTime)}->{FormatTime(region.EndTime)}");
            foreach (TechV2LocalFeatureFailureWindow window in region.Windows)
            {
                text.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "    {0}->{1} | {2} | G/M/S/Mo={3:F3}/{4:F3}/{5:F3}/{6:F3} | GI/AC={7:F3}/{8:F3} | RI/Aw/Sp={9:F3}/{10:F3}/{11:F3} | SC/S2S/CSR/Motif={12:F3}/{13:F3}/{14}/{15:F3} | Sharp/Struct/Compact/Alt/Overlap={16:F3}/{17:F3}/{18:F3}/{19:F3}/{20:F3}",
                    FormatTime(window.StartTime),
                    FormatTime(window.EndTime),
                    window.DecisionKind,
                    window.Geometry,
                    window.Movement,
                    window.Slider,
                    window.Motif,
                    window.GeometryIrregularity,
                    window.AngleCoherenceWithSupport,
                    window.RhythmIrregularity,
                    window.Awkwardness,
                    window.SpacingVariation,
                    window.SliderComplexity,
                    window.SliderToSliderRatio,
                    window.ComplexSliderRunLength,
                    window.CoupledTechnicalMotifContinuity,
                    window.SharpRatio,
                    window.StructuralRatio,
                    window.CompactRatio,
                    window.AlternatingRatio,
                    window.OverlapPerSlider));

                text.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "      support: hit={0} circle={1} slider={2} complexSlider={3} valid={4} pivots={5} sharp={6} awkward={7} spacingVar={8}",
                    window.Support.HitObjectCount,
                    window.Support.CircleCount,
                    window.Support.SliderCount,
                    window.Support.ComplexSliderCount,
                    window.Support.ValidTransitionCount,
                    window.Support.StructuralPivotCount,
                    window.Support.SharpTransitionCount,
                    window.Support.AwkwardTransitionCount,
                    window.Support.SpacingVariationTransitionCount));

                text.AppendLine("      ratio denominators:");
                foreach (TechV2RatioBreakdown ratio in window.Ratios)
                {
                    text.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "        {0}: {1}/{2}={3:F3}",
                        ratio.Name,
                        ratio.Numerator,
                        ratio.Denominator,
                        ratio.Ratio));
                }

                text.AppendLine("      normalization trace:");
                foreach (TechV2NormalizationTrace trace in window.Normalization)
                {
                    text.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "        {0}: raw={1:F6} normalized={2:F6} clamp={3} saturation={4}",
                        trace.Feature,
                        trace.RawValue,
                        trace.NormalizedValue,
                        trace.ClampApplied,
                        trace.SaturationApplied));
                }
            }
        }

        text.AppendLine("PER-FEATURE FAILURE TABLE");
        text.AppendLine("Feature | Positive low-scoring cases | Negative high-scoring cases | Failure direction | Failure type");
        foreach (TechV2PerFeatureFailure failure in report.PerFeatureFailures)
        {
            text.AppendLine($"  {failure.Feature} | {failure.PositiveLowScoringCases} | {failure.NegativeHighScoringCases} | {failure.FailureDirection} | {string.Join(",", failure.FailureTypes)}");
        }

        text.AppendLine("DENOMINATOR PATHOLOGIES");
        foreach (string item in report.DenominatorPathologies)
            text.AppendLine("  " + item);

        text.AppendLine("ZERO-SUPPORT EXPLANATIONS");
        foreach (string item in report.ZeroSupportExplanations)
            text.AppendLine("  " + item);

        text.AppendLine("WINDOW ALIGNMENT FINDINGS");
        foreach (string item in report.WindowAlignmentFindings)
            text.AppendLine("  " + item);

        text.AppendLine("NORMALIZATION FINDINGS");
        foreach (string item in report.NormalizationFindings)
            text.AppendLine("  " + item);

        text.AppendLine("TOP 3 ROOT CAUSES");
        foreach (string cause in report.TopRootCauses)
            text.AppendLine("  " + cause);

        text.AppendLine("BEST NEXT FIX CANDIDATE");
        text.AppendLine("  " + report.BestNextFixCandidate);
        return text.ToString();
    }

    private static TechV2LocalFeatureFailureRegion? AnalyzeRegion(
        TechV2HumanGroundedMap map,
        TechV2LocalFeatureFailureCaseDefinition definition,
        IReadOnlyList<MlDatasetSample> _)
    {
        Beatmap beatmap = BeatmapParser.Load(map.Path!);
        IReadOnlyList<TechV2ExperimentalWindowResult> windows = TechDetectorV2Experimental.Analyze(
            beatmap.HitObjects,
            GameplayAnalyzer.IsSpinner,
            GameplayAnalyzer.IsSlider,
            GameplayAnalyzer.IsComplexSlider,
            GameplayAnalyzer.GetObjectStartPosition,
            GameplayAnalyzer.GetObjectEndPosition,
            GameplayAnalyzer.GetTechTurnAngle);
        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> sources = windows
            .ToDictionary(window => window.StartIndex);
        IReadOnlyList<TechV2WindowDecision> decisions = map.Decisions
            .OrderBy(decision => decision.StartIndex)
            .ThenBy(decision => decision.EndIndex)
            .ToArray();

        TechV2Region region = map.Presence.Regions[definition.RegionNumber - 1];

        TechV2LocalTechStrengthRegion score =
            TechV2LocalTechStrengthPrototype.CreateScoredRegionForTest(
                map.Reference.Key,
                definition.RegionNumber,
                definition.HumanLabel,
                region,
                decisions,
                sources,
                beatmap.HitObjects,
                beatmap.OsuStarRating);

        IReadOnlyList<TechV2WindowDecision> selected = decisions
            .Where(decision =>
                (decision.IsDetectedTech || decision.IsTechSupport)
                && decision.StartIndex >= region.StartWindowIndex
                && decision.EndIndex <= region.EndWindowIndex)
            .OrderBy(decision => decision.StartIndex)
            .ToArray();

        IReadOnlyList<TechV2LocalFeatureFailureWindow> detailed = selected
            .Select(decision => DescribeWindow(
                decision,
                sources[decision.StartIndex],
                beatmap.HitObjects))
            .ToArray();

        string zeroSupport = BuildZeroSupportExplanation(
            map.Reference.Key,
            definition.RegionNumber,
            definition.HumanLabel,
            detailed);
        string windowAlignment = BuildWindowAlignmentFinding(
            selected,
            decisions,
            definition.HumanLabel);

        return new TechV2LocalFeatureFailureRegion(
            map.Reference.Key,
            definition.RegionNumber,
            definition.HumanLabel,
            definition.Group,
            region.StartTime,
            region.EndTime,
            score.Prototypes.SupportedProduct,
            score.StructuralValidity,
            score.TechnicalIntensity,
            score.SupportConfidence,
            detailed,
            zeroSupport,
            windowAlignment);
    }

    private static TechV2LocalFeatureFailureWindow DescribeWindow(
        TechV2WindowDecision decision,
        TechV2ExperimentalWindowResult source,
        IReadOnlyList<HitObject> objects)
    {
        WindowRecomputedMetrics metrics = RecomputeMetrics(
            objects,
            decision.StartIndex,
            decision.EndIndex,
            source);

        double geometryRaw = source.GeometryIrregularity * GeometryIrregularityWeight
            + source.AngleCoherenceWithSupport * AngleCoherenceWeight;
        double movementRaw = source.RhythmIrregularity * RhythmIrregularityWeight
            + source.Awkwardness * AwkwardnessWeight
            + source.SpacingVariation * SpacingVariationWeight;
        double normalizedComplexRun = Math.Clamp(
            (double)source.ComplexSliderRunLength / TechDetectorV2Experimental.WindowSize,
            0,
            1);
        double sliderRaw = source.SliderComplexity * SliderComplexityWeight
            + source.ComplexSliderTransitionTopology.SliderToSliderRatio * SliderToSliderWeight
            + normalizedComplexRun * ComplexSliderRunWeight;
        double motifRaw = source.CoupledTechnicalMotifContinuity;

        IReadOnlyList<TechV2NormalizationTrace> normalization =
        [
            Trace("Geometry", geometryRaw, decision.GeometryEvidence),
            Trace("Movement", movementRaw, decision.MovementEvidence),
            Trace("Slider", sliderRaw, decision.SliderEvidence),
            Trace("Motif", motifRaw, decision.MotifEvidence),
        ];

        IReadOnlyList<TechV2RatioBreakdown> ratios =
        [
            new("Awkwardness", metrics.AwkwardCount, metrics.ValidTransitions, Ratio(metrics.AwkwardCount, metrics.ValidTransitions)),
            new("SpacingVariation", metrics.SpacingVariationCount, metrics.ValidTransitions, Ratio(metrics.SpacingVariationCount, metrics.ValidTransitions)),
            new("StructuralRatio", metrics.StructuralCount, metrics.ValidTransitions, Ratio(metrics.StructuralCount, metrics.ValidTransitions)),
            new("SharpRatio", metrics.SharpCount, metrics.ValidTransitions, Ratio(metrics.SharpCount, metrics.ValidTransitions)),
            new("AlternatingRatio", metrics.AlternatingCount, metrics.ValidTransitions, Ratio(metrics.AlternatingCount, metrics.ValidTransitions)),
            new("ComplexSliderRatio", metrics.ComplexSliderCount, metrics.SliderCount, Ratio(metrics.ComplexSliderCount, metrics.SliderCount)),
            new("SliderToSliderRatio", metrics.SliderToSliderTransitionCount, metrics.TopologyTransitionCount, Ratio(metrics.SliderToSliderTransitionCount, metrics.TopologyTransitionCount)),
        ];

        return new TechV2LocalFeatureFailureWindow(
            decision.StartTime,
            decision.EndTime,
            DecisionKind(decision),
            decision.GeometryEvidence,
            decision.MovementEvidence,
            decision.SliderEvidence,
            decision.MotifEvidence,
            source.GeometryIrregularity,
            source.AngleCoherenceWithSupport,
            source.RhythmIrregularity,
            source.Awkwardness,
            source.SpacingVariation,
            source.SliderComplexity,
            source.ComplexSliderTransitionTopology.SliderToSliderRatio,
            source.ComplexSliderRunLength,
            source.CoupledTechnicalMotifContinuity,
            Ratio(metrics.SharpCount, metrics.ValidTransitions),
            Ratio(metrics.StructuralCount, metrics.ValidTransitions),
            Ratio(metrics.CompactCount, metrics.ValidTransitions),
            Ratio(metrics.AlternatingCount, metrics.ValidTransitions),
            Ratio(metrics.OverlapCount, Math.Max(1, metrics.SliderCount)),
            new TechV2WindowSupportCounters(
                metrics.HitObjectCount,
                metrics.CircleCount,
                metrics.SliderCount,
                metrics.ComplexSliderCount,
                metrics.ValidTransitions,
                source.AngleCoherenceStructuralPivotCount,
                metrics.SharpCount,
                metrics.AwkwardCount,
                metrics.SpacingVariationCount,
                metrics.StructuralCount,
                metrics.CompactCount,
                metrics.AlternatingCount,
                metrics.TopologyTransitionCount,
                metrics.SliderToSliderTransitionCount,
                metrics.RawTripleCount,
                metrics.SpinnerFilteredCount,
                metrics.NonPositiveIntervalCount,
                metrics.DistanceFilteredCount),
            ratios,
            normalization);
    }

    private static IReadOnlyList<TechV2PerFeatureFailure> BuildPerFeatureFailures(
        IReadOnlyList<TechV2LocalFeatureFailureRegion> regions)
    {
        IReadOnlyList<TechV2LocalFeatureFailureRegion> positives = regions
            .Where(region => region.HumanLabel is TechV2LocalHumanLabel.Tech or TechV2LocalHumanLabel.TechWeak)
            .OrderBy(region => region.SupportedProduct)
            .ToArray();
        IReadOnlyList<TechV2LocalFeatureFailureRegion> negatives = regions
            .Where(region => region.HumanLabel is TechV2LocalHumanLabel.NonTech or TechV2LocalHumanLabel.Borderline)
            .OrderByDescending(region => region.SupportedProduct)
            .ToArray();

        IReadOnlyList<TechV2LocalFeatureFailureRegion> positiveLow = positives.Take(Math.Max(1, positives.Count / 2)).ToArray();
        IReadOnlyList<TechV2LocalFeatureFailureRegion> negativeHigh = negatives.Take(Math.Max(1, negatives.Count / 2)).ToArray();

        (string Name, Func<TechV2LocalFeatureFailureWindow, double> Selector)[] features =
        [
            ("StructuralRatio", window => window.StructuralRatio),
            ("SharpRatio", window => window.SharpRatio),
            ("CompactRatio", window => window.CompactRatio),
            ("AlternatingRatio", window => window.AlternatingRatio),
            ("OverlapPerSlider", window => window.OverlapPerSlider),
            ("AngleSupport", window => window.AngleCoherenceWithSupport),
            ("Awkwardness", window => window.Awkwardness),
            ("SpacingVariation", window => window.SpacingVariation),
            ("SliderComplexity", window => window.SliderComplexity),
            ("CoupledMotif", window => window.CoupledTechnicalMotifContinuity),
        ];

        return features.Select(feature =>
        {
            double positiveMean = Mean(positiveLow.SelectMany(region => region.Windows), feature.Selector);
            double negativeMean = Mean(negativeHigh.SelectMany(region => region.Windows), feature.Selector);

            string direction = positiveMean + 1e-9 < negativeMean
                ? "positive<negative"
                : positiveMean > negativeMean + 1e-9
                    ? "positive>negative"
                    : "flat/overlap";

            List<TechV2FeatureFailureType> types = [];
            if (feature.Name is "StructuralRatio" or "SharpRatio" or "AngleSupport"
                && positiveMean < negativeMean)
            {
                types.Add(TechV2FeatureFailureType.DEFINITION_PROBLEM);
            }

            if (feature.Name is "StructuralRatio" or "SharpRatio" or "AlternatingRatio"
                && positiveLow.SelectMany(region => region.Windows)
                    .Count(window => window.Support.ValidTransitionCount <= 1) > 0)
            {
                types.Add(TechV2FeatureFailureType.SUPPORT_PROBLEM);
            }

            if (feature.Name is "SliderComplexity" or "Awkwardness" or "SpacingVariation"
                && Math.Abs(positiveMean - negativeMean) < 0.05)
            {
                types.Add(TechV2FeatureFailureType.SIGNAL_NOT_DISCRIMINANT);
            }

            if (feature.Name is "StructuralRatio" or "AngleSupport"
                && positiveLow.Any(region =>
                    region.WindowAlignmentFinding.Contains("aggregation", StringComparison.OrdinalIgnoreCase)))
            {
                types.Add(TechV2FeatureFailureType.WINDOWING_PROBLEM);
            }

            if (feature.Name is "SliderComplexity" or "CoupledMotif"
                && (positiveLow.SelectMany(region => region.Windows)
                    .SelectMany(window => window.Normalization)
                    .Count(trace => trace.Feature == feature.Name.Replace("CoupledMotif", "Motif")
                        && trace.SaturationApplied) > 0))
            {
                types.Add(TechV2FeatureFailureType.NORMALIZATION_PROBLEM);
            }

            if (types.Count == 0)
                types.Add(TechV2FeatureFailureType.SIGNAL_NOT_DISCRIMINANT);

            return new TechV2PerFeatureFailure(
                feature.Name,
                JoinCases(positiveLow),
                JoinCases(negativeHigh),
                direction,
                types.Distinct().ToArray());
        }).ToArray();
    }

    private static IReadOnlyList<string> BuildDenominatorPathologies(
        IReadOnlyList<TechV2LocalFeatureFailureRegion> regions)
    {
        List<string> items = [];
        foreach (TechV2LocalFeatureFailureRegion region in regions)
        {
            foreach (TechV2LocalFeatureFailureWindow window in region.Windows)
            {
                foreach (TechV2RatioBreakdown ratio in window.Ratios)
                {
                    if (ratio.Denominator == 0)
                    {
                        items.Add($"{region.MapKey} R{region.RegionNumber} {ratio.Name}: {ratio.Numerator}/{ratio.Denominator}");
                        continue;
                    }

                    if (ratio.Denominator == 1)
                    {
                        items.Add($"{region.MapKey} R{region.RegionNumber} {ratio.Name}: single-support {ratio.Numerator}/{ratio.Denominator}={ratio.Ratio:F3}");
                    }
                }
            }
        }

        return items
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> BuildNormalizationFindings(
        IReadOnlyList<TechV2LocalFeatureFailureRegion> regions)
    {
        List<string> findings = [];
        foreach (string feature in new[] { "Geometry", "Movement", "Slider", "Motif" })
        {
            IReadOnlyList<TechV2NormalizationTrace> traces = regions
                .SelectMany(region => region.Windows)
                .SelectMany(window => window.Normalization)
                .Where(trace => trace.Feature == feature)
                .ToArray();

            int clamped = traces.Count(trace => trace.ClampApplied);
            int saturated = traces.Count(trace => trace.SaturationApplied);
            findings.Add($"{feature}: clamp={clamped}/{traces.Count}, saturation={saturated}/{traces.Count}");
        }

        return findings;
    }

    private static string BuildZeroSupportExplanation(
        string mapKey,
        int regionNumber,
        TechV2LocalHumanLabel label,
        IReadOnlyList<TechV2LocalFeatureFailureWindow> windows)
    {
        if (label is not TechV2LocalHumanLabel.Tech and not TechV2LocalHumanLabel.TechWeak)
            return "none";

        bool hasZeroSupport = windows.Any(window =>
            window.Support.StructuralPivotCount == 0
            || window.Support.ValidTransitionCount == 0
            || window.AngleCoherenceWithSupport == 0);
        if (!hasZeroSupport)
            return "none";

        List<string> reasons = [];
        if (windows.Any(window => window.Support.HitObjectCount < 3))
            reasons.Add("objets insuffisants");
        if (windows.Any(window => window.Support.ValidTransitionCount == 0))
            reasons.Add("angles non définissables (valid transitions = 0)");
        if (windows.Any(window => window.Support.CircleCount <= 2 && window.Support.SliderCount >= 6))
            reasons.Add("slider-heavy topology");
        if (windows.Any(window =>
            window.Support.ValidTransitionCount > 0
            && window.Support.StructuralTransitionCount == 0))
        {
            reasons.Add("pattern trop linéaire");
        }
        if (windows.Any(window =>
            window.Support.RawTripleCount > 0
            && window.Support.DistanceFilteredTripleCount + window.Support.SpinnerFilteredTripleCount
                + window.Support.NonPositiveIntervalTripleCount > 0
            && window.Support.ValidTransitionCount == 0))
        {
            reasons.Add("transitions filtrées par seuils/qualité");
        }
        if (windows.Any(window =>
            window.RhythmIrregularity == 0
            && window.Support.ValidTransitionCount > 0))
        {
            reasons.Add("timing trop homogène");
        }

        if (reasons.Count == 0)
            reasons.Add("autre");

        return string.Join(", ", reasons.Distinct(StringComparer.Ordinal));
    }

    private static string BuildWindowAlignmentFinding(
        IReadOnlyList<TechV2WindowDecision> selected,
        IReadOnlyList<TechV2WindowDecision> all,
        TechV2LocalHumanLabel label)
    {
        if (label is not TechV2LocalHumanLabel.Tech and not TechV2LocalHumanLabel.TechWeak)
            return "none";
        if (selected.Count == 0)
            return "none";

        int firstIndex = FindIndexByStartIndex(all, selected[0].StartIndex);
        int lastIndex = FindIndexByStartIndex(all, selected[^1].StartIndex);

        double baseMean = selected.Average(window => window.RawScore);
        double previous = firstIndex > 0 ? all[firstIndex - 1].RawScore : double.NaN;
        double next = lastIndex + 1 < all.Count ? all[lastIndex + 1].RawScore : double.NaN;

        double best2 = BestAggregate(all, Math.Max(0, firstIndex - 1), Math.Min(all.Count - 1, lastIndex + 1), 2);
        double best3 = BestAggregate(all, Math.Max(0, firstIndex - 1), Math.Min(all.Count - 1, lastIndex + 1), 3);

        bool improvedByAdjacent = (double.IsFinite(previous) && previous > baseMean + 0.05)
            || (double.IsFinite(next) && next > baseMean + 0.05);
        bool improvedByAggregation = best2 > baseMean + 0.05 || best3 > baseMean + 0.05;

        if (!improvedByAdjacent && !improvedByAggregation)
            return "none";

        return string.Format(
            CultureInfo.InvariantCulture,
            "base={0:F3}, prev={1}, next={2}, best2-aggregation={3:F3}, best3-aggregation={4:F3}",
            baseMean,
            double.IsFinite(previous) ? previous.ToString("F3", CultureInfo.InvariantCulture) : "n/a",
            double.IsFinite(next) ? next.ToString("F3", CultureInfo.InvariantCulture) : "n/a",
            best2,
            best3);
    }

    private static double BestAggregate(
        IReadOnlyList<TechV2WindowDecision> values,
        int start,
        int end,
        int size)
    {
        if (size <= 0 || end < start)
            return 0;

        double best = double.NegativeInfinity;
        for (int index = start; index + size - 1 <= end; index++)
        {
            best = Math.Max(best, values.Skip(index).Take(size).Average(item => item.RawScore));
        }

        return double.IsNegativeInfinity(best) ? 0 : best;
    }

    private static int FindIndexByStartIndex(
        IReadOnlyList<TechV2WindowDecision> windows,
        int startIndex)
    {
        for (int index = 0; index < windows.Count; index++)
        {
            if (windows[index].StartIndex == startIndex)
                return index;
        }

        return -1;
    }

    private static WindowRecomputedMetrics RecomputeMetrics(
        IReadOnlyList<HitObject> objects,
        int start,
        int end,
        TechV2ExperimentalWindowResult source)
    {
        int rawTriples = 0;
        int spinnerFiltered = 0;
        int nonPositiveInterval = 0;
        int distanceFiltered = 0;
        int valid = 0;
        int structural = 0;
        int compact = 0;
        int sharp = 0;
        int awkward = 0;
        int spacing = 0;
        int alternating = 0;

        for (int index = start + 1; index <= end - 1; index++)
        {
            rawTriples++;
            HitObject previous = objects[index - 1];
            HitObject current = objects[index];
            HitObject next = objects[index + 1];

            if (GameplayAnalyzer.IsSpinner(previous)
                || GameplayAnalyzer.IsSpinner(current)
                || GameplayAnalyzer.IsSpinner(next))
            {
                spinnerFiltered++;
                continue;
            }

            double firstInterval = current.Time - previous.Time;
            double secondInterval = next.Time - current.Time;
            if (firstInterval <= 0 || secondInterval <= 0)
            {
                nonPositiveInterval++;
                continue;
            }

            double firstDistance = Distance(
                GameplayAnalyzer.GetObjectEndPosition(previous),
                GameplayAnalyzer.GetObjectStartPosition(current));
            double secondDistance = Distance(
                GameplayAnalyzer.GetObjectEndPosition(current),
                GameplayAnalyzer.GetObjectStartPosition(next));

            if (firstDistance > 160 || secondDistance > 160)
            {
                distanceFiltered++;
                continue;
            }

            valid++;
            double angle = GameplayAnalyzer.GetTechTurnAngle(previous, current, next);
            double averageDistance = (firstDistance + secondDistance) / 2.0;

            bool isStructural = angle >= 110;
            bool isCompact = firstDistance <= 110 && secondDistance <= 110;
            bool isSharp = angle >= 150 && averageDistance <= 125;
            bool isAwkward = angle is >= 45 and <= 150 && averageDistance <= 125;
            bool isSpacing = Math.Abs(firstDistance - secondDistance) >= 35;

            if (isStructural)
                structural++;
            if (isCompact)
                compact++;
            if (isSharp)
                sharp++;
            if (isAwkward)
                awkward++;
            if (isSpacing)
                spacing++;

            if (index >= start + 2)
            {
                HitObject previousPrevious = objects[index - 2];
                double previousInterval = previous.Time - previousPrevious.Time;
                if (!GameplayAnalyzer.IsSpinner(previousPrevious)
                    && previousInterval > 0
                    && previousInterval <= 220
                    && firstInterval <= 220
                    && secondInterval <= 220
                    && GameplayAnalyzer.GetTechTurnAngle(previousPrevious, previous, current) >= 110
                    && isStructural)
                {
                    alternating++;
                }
            }
        }

        IReadOnlyList<HitObject> local = objects
            .Skip(start)
            .Take(TechDetectorV2Experimental.WindowSize)
            .ToArray();
        int sliderCount = local.Count(GameplayAnalyzer.IsSlider);
        int complexSliderCount = local.Count(GameplayAnalyzer.IsComplexSlider);
        int circleCount = local.Count(hitObject =>
            !GameplayAnalyzer.IsSlider(hitObject) && !GameplayAnalyzer.IsSpinner(hitObject));
        int overlap = CountLocalOverlaps(local.Where(GameplayAnalyzer.IsSlider).ToArray());

        int topologyTransitions = source.ComplexSliderTransitionTopology.TransitionCount;
        int sliderToSlider = topologyTransitions <= 0
            ? 0
            : (int)Math.Round(
                source.ComplexSliderTransitionTopology.SliderToSliderRatio * topologyTransitions,
                MidpointRounding.AwayFromZero);

        return new WindowRecomputedMetrics(
            local.Count,
            circleCount,
            sliderCount,
            complexSliderCount,
            valid,
            structural,
            compact,
            sharp,
            awkward,
            spacing,
            alternating,
            overlap,
            topologyTransitions,
            sliderToSlider,
            rawTriples,
            spinnerFiltered,
            nonPositiveInterval,
            distanceFiltered);
    }

    private static int CountLocalOverlaps(IReadOnlyList<HitObject> sliders)
    {
        int count = 0;
        for (int first = 0; first < sliders.Count; first++)
        {
            for (int second = first + 1; second < sliders.Count; second++)
            {
                if (sliders[second].Time - sliders[first].Time > 700)
                    break;

                if (sliders[first].SliderControlPoints.Any(a => sliders[second]
                    .SliderControlPoints.Any(b => Distance((a.X, a.Y), (b.X, b.Y)) <= 35)))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static TechV2NormalizationTrace Trace(string feature, double raw, double normalized)
    {
        double clamped = Math.Clamp(raw, 0, 1);
        bool clampApplied = Math.Abs(clamped - raw) > 1e-9 || Math.Abs(normalized - raw) > 1e-9;
        bool saturation = normalized <= 1e-6 || normalized >= 1 - 1e-6;
        return new TechV2NormalizationTrace(feature, raw, normalized, clampApplied, saturation);
    }

    private static string JoinCases(IReadOnlyList<TechV2LocalFeatureFailureRegion> regions) =>
        regions.Count == 0
            ? "none"
            : string.Join(", ", regions.Select(region =>
                $"{region.MapKey} R{region.RegionNumber}").Distinct(StringComparer.Ordinal));

    private static double Mean(
        IEnumerable<TechV2LocalFeatureFailureWindow> windows,
        Func<TechV2LocalFeatureFailureWindow, double> selector)
    {
        double[] values = windows.Select(selector).ToArray();
        return values.Length == 0 ? 0 : values.Average();
    }

    private static double Ratio(int numerator, int denominator) =>
        denominator <= 0 ? 0 : (double)numerator / denominator;

    private static string DecisionKind(TechV2WindowDecision decision) =>
        decision.IsStrongTech
            ? "StrongTech"
            : decision.IsSustainedTech
                ? "SustainedTech"
                : decision.IsTechSupport
                    ? "TechSupport"
                    : "None";

    private static double Distance((double X, double Y) first, (double X, double Y) second)
    {
        double x = second.X - first.X;
        double y = second.Y - first.Y;
        return Math.Sqrt(x * x + y * y);
    }

    private static string FormatTime(double ms)
    {
        TimeSpan t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}.{2:D3}", (int)t.TotalMinutes, t.Seconds, t.Milliseconds);
    }

    private sealed record WindowRecomputedMetrics(
        int HitObjectCount,
        int CircleCount,
        int SliderCount,
        int ComplexSliderCount,
        int ValidTransitions,
        int StructuralCount,
        int CompactCount,
        int SharpCount,
        int AwkwardCount,
        int SpacingVariationCount,
        int AlternatingCount,
        int OverlapCount,
        int TopologyTransitionCount,
        int SliderToSliderTransitionCount,
        int RawTripleCount,
        int SpinnerFilteredCount,
        int NonPositiveIntervalCount,
        int DistanceFilteredCount);
}