using BeatInsight.Models;

namespace BeatInsight.Analysis;

internal sealed record TechV2ShadowRatioValue(
    string Name,
    int Numerator,
    int Denominator,
    double Raw,
    double SupportAware);

internal sealed record TechV2ShadowSupportCounts(
    int ValidTransitions,
    int StructuralCount,
    int SharpCount,
    int SpacingVariationCount,
    int AlternatingCount,
    int SliderCount,
    int ComplexSliderCount,
    int TopologyTransitionCount,
    int SliderToSliderTransitionCount);

internal sealed record TechV2ShadowWindowDiagnostics(
    int StartIndex,
    int EndIndex,
    double StartTime,
    double EndTime,
    string DecisionKindBefore,
    string DecisionKindAfter,
    double CurrentRatioValue,
    double CorrectedSupportAwareRatioValue,
    double CurrentLocalContextValue,
    double CorrectedLocalContextValue,
    double CurrentCombinedValue,
    double CorrectedCombinedValue,
    double Delta,
    TechV2ShadowSupportCounts SupportCounts,
    IReadOnlyList<TechV2ShadowRatioValue> Ratios);

internal sealed record TechV2CombinedCorrectionShadowResult(
    IReadOnlyList<TechV2ShadowWindowDiagnostics> Windows,
    int UnchangedDecisionKindCount,
    int ChangedDecisionKindCount);

internal static class TechV2CombinedCorrectionShadowRuntime
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

    internal static TechV2CombinedCorrectionShadowResult Compute(
        IReadOnlyList<HitObject> objects,
        IReadOnlyList<TechV2ExperimentalWindowResult> windows,
        IReadOnlyList<TechV2WindowDecision> decisions)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(decisions);

        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> byStart = windows
            .ToDictionary(window => window.StartIndex);

        List<WindowBase> baseRows = [];
        foreach (TechV2WindowDecision decision in decisions
            .OrderBy(item => item.StartIndex)
            .ThenBy(item => item.EndIndex))
        {
            if (!byStart.TryGetValue(decision.StartIndex, out TechV2ExperimentalWindowResult? source))
                continue;

            WindowMetrics metrics = RecomputeMetrics(objects, decision.StartIndex, decision.EndIndex, source);
            IReadOnlyList<TechV2ShadowRatioValue> ratios =
            [
                BuildRatio("Awkwardness", metrics.AwkwardCount, metrics.ValidTransitions),
                BuildRatio("SpacingVariation", metrics.SpacingVariationCount, metrics.ValidTransitions),
                BuildRatio("StructuralRatio", metrics.StructuralCount, metrics.ValidTransitions),
                BuildRatio("SharpRatio", metrics.SharpCount, metrics.ValidTransitions),
                BuildRatio("AlternatingRatio", metrics.AlternatingCount, metrics.ValidTransitions),
                BuildRatio("ComplexSliderRatio", metrics.ComplexSliderCount, metrics.SliderCount),
                BuildRatio("SliderToSliderRatio", metrics.SliderToSliderTransitionCount, metrics.TopologyTransitionCount),
            ];

            double rawRatioScore = Mean(ratios.Select(ratio => ratio.Raw));
            double supportAwareScore = Mean(ratios.Select(ratio => ratio.SupportAware));

            double currentLocal = ComputeCurrentLocalContext(decision, source, ratios);

            baseRows.Add(new WindowBase(
                decision,
                source,
                ratios,
                metrics,
                rawRatioScore,
                supportAwareScore,
                currentLocal));
        }

        List<TechV2ShadowWindowDiagnostics> rows = [];
        int unchanged = 0;
        int changed = 0;

        for (int index = 0; index < baseRows.Count; index++)
        {
            WindowBase row = baseRows[index];
            double correctedLocal = ComputePrevCurrentSimple(baseRows, index);
            double currentCombined = Compose(row.RawRatioScore, row.CurrentLocalContext);
            double correctedCombined = Compose(row.SupportAwareRatioScore, correctedLocal);

            string before = DecisionKind(row.Decision);
            string after = DecisionKind(row.Decision);
            if (string.Equals(before, after, StringComparison.Ordinal))
                unchanged++;
            else
                changed++;

            rows.Add(new TechV2ShadowWindowDiagnostics(
                row.Decision.StartIndex,
                row.Decision.EndIndex,
                row.Decision.StartTime,
                row.Decision.EndTime,
                before,
                after,
                row.RawRatioScore,
                row.SupportAwareRatioScore,
                row.CurrentLocalContext,
                correctedLocal,
                currentCombined,
                correctedCombined,
                correctedCombined - currentCombined,
                new TechV2ShadowSupportCounts(
                    row.Metrics.ValidTransitions,
                    row.Metrics.StructuralCount,
                    row.Metrics.SharpCount,
                    row.Metrics.SpacingVariationCount,
                    row.Metrics.AlternatingCount,
                    row.Metrics.SliderCount,
                    row.Metrics.ComplexSliderCount,
                    row.Metrics.TopologyTransitionCount,
                    row.Metrics.SliderToSliderTransitionCount),
                row.Ratios));
        }

        return new TechV2CombinedCorrectionShadowResult(rows, unchanged, changed);
    }

    private static TechV2ShadowRatioValue BuildRatio(
        string name,
        int numerator,
        int denominator)
    {
        double raw = Ratio(numerator, denominator);
        // Phase 2.27 / ZERO_A: no observation is no evidence. Laplace
        // smoothing is retained unchanged only when a real denominator exists.
        double supportAware = SupportAwareRatio(numerator, denominator);
        return new TechV2ShadowRatioValue(name, numerator, denominator, raw, supportAware);
    }

    /// <summary>Phase 2.27 ZERO_A ratio policy, exposed for focused tests.</summary>
    internal static double SupportAwareRatio(int numerator, int denominator) => denominator <= 0
        ? 0
        : Math.Clamp((numerator + 0.5) / (denominator + 1.0), 0, 1);

    /// <summary>Phase 2.27 PREV_A local context policy, exposed for focused tests.</summary>
    internal static double ApplyPreviousContext(
        double currentLocal,
        double previousLocal,
        int validTransitions,
        int sliderCount) => validTransitions + sliderCount <= 0
            ? currentLocal
            : Mean([previousLocal, currentLocal]);

    private static double ComputeCurrentLocalContext(
        TechV2WindowDecision decision,
        TechV2ExperimentalWindowResult source,
        IReadOnlyList<TechV2ShadowRatioValue> ratios)
    {
        double structuralRatio = ratios.First(ratio => ratio.Name == "StructuralRatio").Raw;
        double sharpRatio = ratios.First(ratio => ratio.Name == "SharpRatio").Raw;

        return Mean(
        [
            decision.GeometryEvidence,
            decision.MovementEvidence,
            decision.SliderEvidence,
            decision.MotifEvidence,
            structuralRatio,
            sharpRatio,
            source.AngleCoherenceWithSupport,
            source.SpacingVariation,
        ]);
    }

    private static double ComputePrevCurrentSimple(
        IReadOnlyList<WindowBase> rows,
        int index)
    {
        WindowBase current = rows[index];

        // Phase 2.27 / PREV_A: a locally unsupported current window cannot
        // inherit context from its predecessor. The current-window component
        // still participates normally; topology and all weights are unchanged.
        WindowBase previous = index > 0 ? rows[index - 1] : current;

        double previousLocal = ComputeCurrentLocalContext(previous.Decision, previous.Source, previous.Ratios);
        return ApplyPreviousContext(
            current.CurrentLocalContext,
            previousLocal,
            current.Metrics.ValidTransitions,
            current.Metrics.SliderCount);

    }

    private static WindowMetrics RecomputeMetrics(
        IReadOnlyList<HitObject> objects,
        int start,
        int end,
        TechV2ExperimentalWindowResult source)
    {
        int valid = 0;
        int structural = 0;
        int sharp = 0;
        int awkward = 0;
        int spacing = 0;
        int alternating = 0;

        for (int index = start + 1; index <= end - 1; index++)
        {
            HitObject previous = objects[index - 1];
            HitObject current = objects[index];
            HitObject next = objects[index + 1];

            if (GameplayAnalyzer.IsSpinner(previous)
                || GameplayAnalyzer.IsSpinner(current)
                || GameplayAnalyzer.IsSpinner(next))
            {
                continue;
            }

            double firstInterval = current.Time - previous.Time;
            double secondInterval = next.Time - current.Time;
            if (firstInterval <= 0 || secondInterval <= 0)
            {
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
                continue;
            }

            valid++;
            double angle = GameplayAnalyzer.GetTechTurnAngle(previous, current, next);
            double averageDistance = (firstDistance + secondDistance) / 2.0;

            bool isStructural = angle >= 110;
            bool isSharp = angle >= 150 && averageDistance <= 125;
            bool isAwkward = angle is >= 45 and <= 150 && averageDistance <= 125;
            bool isSpacing = Math.Abs(firstDistance - secondDistance) >= 35;

            if (isStructural)
                structural++;
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

        int topologyTransitions = source.ComplexSliderTransitionTopology.TransitionCount;
        int sliderToSlider = topologyTransitions <= 0
            ? 0
            : (int)Math.Round(
                source.ComplexSliderTransitionTopology.SliderToSliderRatio * topologyTransitions,
                MidpointRounding.AwayFromZero);

        return new WindowMetrics(
            valid,
            structural,
            sharp,
            awkward,
            spacing,
            alternating,
            sliderCount,
            complexSliderCount,
            topologyTransitions,
            sliderToSlider);
    }

    private static double Compose(double ratio, double local) =>
        Math.Clamp((ratio + local) / 2.0, 0, 1);

    private static double Ratio(int numerator, int denominator) =>
        denominator <= 0 ? 0 : (double)numerator / denominator;

    private static double Mean(IEnumerable<double> values)
    {
        double[] items = values.ToArray();
        return items.Length == 0 ? 0 : items.Average();
    }

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

    private sealed record WindowMetrics(
        int ValidTransitions,
        int StructuralCount,
        int SharpCount,
        int AwkwardCount,
        int SpacingVariationCount,
        int AlternatingCount,
        int SliderCount,
        int ComplexSliderCount,
        int TopologyTransitionCount,
        int SliderToSliderTransitionCount);

    private sealed record WindowBase(
        TechV2WindowDecision Decision,
        TechV2ExperimentalWindowResult Source,
        IReadOnlyList<TechV2ShadowRatioValue> Ratios,
        WindowMetrics Metrics,
        double RawRatioScore,
        double SupportAwareRatioScore,
        double CurrentLocalContext);
}
