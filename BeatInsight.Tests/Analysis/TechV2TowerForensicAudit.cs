using BeatInsight.Analysis;
using BeatInsight.Models;
using System.Globalization;
using System.Text;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Test-only Phase 2.3 forensic view. It reads current V2 windows and never
/// feeds metrics, labels, or simulated guards back into the runtime engine.
/// </summary>
internal static class TechV2TowerForensicAudit
{
    // Audit-only quantization: 20 ms, 20 px, and 15 degrees are deliberately
    // broad enough to describe repeated local patterns, rather than runtime
    // decision thresholds.
    internal const double IntervalBinMilliseconds = 20;
    internal const double SpacingBinPixels = 20;
    internal const double AngleBinDegrees = 15;

    internal static TechV2ForensicMapAudit Analyze(string mapName, Beatmap map)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapName);
        ArgumentNullException.ThrowIfNull(map);

        IReadOnlyList<TechV2ExperimentalWindowResult> windows =
            TechDetectorV2Experimental.Analyze(
                map.HitObjects,
                GameplayAnalyzer.IsSpinner,
                GameplayAnalyzer.IsSlider,
                GameplayAnalyzer.IsComplexSlider,
                GameplayAnalyzer.GetObjectStartPosition,
                GameplayAnalyzer.GetObjectEndPosition,
                GameplayAnalyzer.GetTechTurnAngle);
        IReadOnlyList<TechV2WindowDecision> before =
            TechV2ExperimentalDecisionEngine.Decide(windows);
        // Preserve the Phase 2.3 forensic baseline: this audit intentionally
        // observes the pre-2.4 candidate runs without applying any guard.
        IReadOnlyList<TechV2WindowDecision> after =
            TechV2ExperimentalDecisionEngine.PromoteSustainedTechWithDiagnostics(
                before,
                applyGeometryGuard: false).Decisions;

        double playableStart = map.HitObjects.FirstOrDefault()?.Time ?? 0;
        double playableEnd = map.HitObjects.LastOrDefault()?.Time ?? 0;
        TechV2PresenceResult beforePresence =
            TechV2ExperimentalDecisionEngine.BuildPresence(before, playableStart, playableEnd);
        TechV2PresenceResult afterPresence =
            TechV2ExperimentalDecisionEngine.BuildPresence(after, playableStart, playableEnd);

        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> byStartIndex =
            windows.ToDictionary(window => window.StartIndex);

        IReadOnlyList<TechV2ForensicRun> sustainedRuns = FindSustainedRuns(
            map.HitObjects,
            after,
            byStartIndex);
        IReadOnlyList<TechV2ForensicWindow> towerWindows = after
            .Where(decision => decision.IsSustainedTech)
            .Select(decision => CreateWindow(
                map.HitObjects,
                decision,
                byStartIndex[decision.StartIndex]))
            .OrderBy(window => window.Decision.StartIndex)
            .ToArray();
        IReadOnlyList<TechV2ForensicWindow> supportControls = after
            .Where(decision => decision.IsTechSupport && !decision.IsSustainedTech)
            .OrderByDescending(decision => decision.RawScore)
            .ThenBy(decision => decision.StartIndex)
            .Take(10)
            .Select(decision => CreateWindow(
                map.HitObjects,
                decision,
                byStartIndex[decision.StartIndex]))
            .ToArray();

        return new TechV2ForensicMapAudit(
            mapName,
            before.Count(decision => decision.IsStrongTech),
            before.Count(decision => decision.IsTechSupport),
            after.Count(decision => decision.IsSustainedTech),
            after.Count(decision => decision.IsDetectedTech),
            beforePresence,
            afterPresence,
            sustainedRuns,
            towerWindows,
            supportControls);
    }

    internal static IReadOnlyList<TechV2ForensicRun> FindSustainedRuns(
        IReadOnlyList<HitObject> objects,
        IReadOnlyList<TechV2WindowDecision> decisions,
        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> byStartIndex)
    {
        List<TechV2ForensicRun> runs = [];
        List<TechV2WindowDecision> active = [];

        foreach (TechV2WindowDecision decision in decisions
            .OrderBy(item => item.StartIndex)
            .ThenBy(item => item.EndIndex))
        {
            bool continuous = active.Count > 0
                && decision.IsSustainedTech
                && decision.StartIndex
                    == active[^1].StartIndex + TechDetectorV2Experimental.Stride
                && decision.StartTime <= active[^1].EndTime;

            if (decision.IsSustainedTech && (active.Count == 0 || continuous))
            {
                active.Add(decision);
                continue;
            }

            AddRun(runs, objects, active, byStartIndex);
            active.Clear();

            if (decision.IsSustainedTech)
                active.Add(decision);
        }

        AddRun(runs, objects, active, byStartIndex);
        return runs;
    }

    internal static TechV2ForensicDistribution Distribution(
        IEnumerable<double> values,
        double binSize)
    {
        double[] finite = values
            .Where(double.IsFinite)
            .ToArray();

        if (finite.Length == 0)
            return new TechV2ForensicDistribution(0, 0, 0, 0, 0, 0, 0);

        double mean = finite.Average();
        double standardDeviation = Math.Sqrt(finite.Average(value => Math.Pow(value - mean, 2)));
        double coefficientOfVariation = mean == 0 ? 0 : standardDeviation / Math.Abs(mean);
        IReadOnlyList<IGrouping<long, double>> bins = finite
            .GroupBy(value => (long)Math.Round(value / binSize, MidpointRounding.AwayFromZero))
            .ToArray();
        int repeated = bins.Where(bin => bin.Count() >= 2).Sum(bin => bin.Count());
        int dominant = bins.Max(bin => bin.Count());

        return new TechV2ForensicDistribution(
            finite.Length,
            mean,
            standardDeviation,
            coefficientOfVariation,
            (double)repeated / finite.Length,
            (double)dominant / finite.Length,
            finite.Max());
    }

    internal static IReadOnlyList<TechV2ForensicGuard> CreateCandidateGuards(
        IReadOnlyDictionary<string, TechV2ForensicMapAudit> audits)
    {
        ArgumentNullException.ThrowIfNull(audits);

        TechV2ForensicMapAudit tower = audits["Tower"];
        TechV2ForensicRunSummary towerSummary = SummarizeRuns(tower.SustainedRuns);
        IReadOnlyList<TechV2ForensicRunSummary> trueTech = [
            SummarizeRuns(audits["Exit"].SustainedRuns),
            SummarizeRuns(audits["Kira"].SustainedRuns),
            SummarizeRuns(audits["Frozen"].SustainedRuns),
        ];

        double geometryFloor = Midpoint(towerSummary.AverageGeometry, trueTech.Min(item => item.AverageGeometry));
        double motifFloor = Midpoint(towerSummary.AverageMotif, trueTech.Min(item => item.AverageMotif));
        double angleCvFloor = Midpoint(towerSummary.Angles.CoefficientOfVariation, trueTech.Min(item => item.Angles.CoefficientOfVariation));
        double repetitionCeiling = Midpoint(towerSummary.Intervals.DominantBinRatio, trueTech.Max(item => item.Intervals.DominantBinRatio));

        return
        [
            CreateGuard(
                "Guard A: geometry floor",
                $"AvgGeometry >= {geometryFloor:F3} (midpoint: Tower vs lowest true-Tech aggregate)",
                run => run.AverageGeometry >= geometryFloor,
                audits),
            CreateGuard(
                "Guard B: motif floor",
                $"AvgMotif >= {motifFloor:F3} (midpoint: Tower vs lowest true-Tech aggregate)",
                run => run.AverageMotif >= motifFloor,
                audits),
            CreateGuard(
                "Guard C: angle variability floor",
                $"Angle CV >= {angleCvFloor:F3} (midpoint: Tower vs lowest true-Tech aggregate)",
                run => run.Angles.CoefficientOfVariation >= angleCvFloor,
                audits),
            CreateGuard(
                "Guard D: dominant interval repetition cap",
                $"Dominant interval-bin ratio <= {repetitionCeiling:F3} (midpoint: Tower vs highest true-Tech aggregate)",
                run => run.Intervals.DominantBinRatio <= repetitionCeiling,
                audits),
            CreateGuard(
                "Guard E: geometry or angle-variability support",
                $"AvgGeometry >= {geometryFloor:F3} OR Angle CV >= {angleCvFloor:F3}",
                run => run.AverageGeometry >= geometryFloor
                    || run.Angles.CoefficientOfVariation >= angleCvFloor,
                audits),
        ];
    }

    internal static string FormatReport(
        IReadOnlyDictionary<string, TechV2ForensicMapAudit> audits)
    {
        StringBuilder text = new();
        text.AppendLine("===== TECH V2 PHASE 2.3 FORENSIC AUDIT =====");
        text.AppendLine("Audit-only bins: intervals=20ms, spacing=20px, angles=15deg.");
        TechV2ForensicSignatureScale scale = TechV2ForensicSignatureScale.Create(
            audits.Values.SelectMany(audit => audit.SustainedRuns));
        text.AppendLine(
            $"Audit-only signature tertiles (low/medium/high): structural CV={scale.StructuralLower:F3}/{scale.StructuralUpper:F3}, " +
            $"rhythm CV={scale.RhythmLower:F3}/{scale.RhythmUpper:F3}, " +
            $"slider topology={scale.SliderLower:F3}/{scale.SliderUpper:F3}, " +
            $"motif={scale.MotifLower:F3}/{scale.MotifUpper:F3}, repetition={scale.RepetitionLower:F3}/{scale.RepetitionUpper:F3}.");
        text.AppendLine("Baseline reproduction:");
        text.AppendLine("Map | Strong | Support | Sustained | Final | Before regions/% | After regions/%");

        foreach ((string key, TechV2ForensicMapAudit audit) in audits)
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0} | {1} | {2} | {3} | {4} | {5}/{6:F2}% | {7}/{8:F2}%",
                key,
                audit.StrongTechWindowCount,
                audit.TechSupportWindowCount,
                audit.SustainedTechWindowCount,
                audit.FinalTechWindowCount,
                audit.BeforePresence.Regions.Count,
                audit.BeforePresence.PresencePercent,
                audit.AfterPresence.Regions.Count,
                audit.AfterPresence.PresencePercent));
        }

        foreach ((string key, TechV2ForensicMapAudit audit) in audits)
        {
            text.AppendLine($"Sustained runs: {key}");
            if (audit.SustainedRuns.Count == 0)
            {
                text.AppendLine("  None");
                continue;
            }

            foreach (TechV2ForensicRun run in audit.SustainedRuns)
                text.AppendLine("  " + FormatRun(run, scale));
        }

        text.AppendLine("Tower sustained windows:");
        TechV2ForensicMapAudit tower = audits["Tower"];
        foreach (TechV2ForensicWindow window in tower.SustainedWindows)
            text.AppendLine("  " + FormatWindow(window));

        text.AppendLine("can you Top TechSupport / NearMiss control:");
        foreach (TechV2ForensicWindow window in audits["CanYou"].SupportControlWindows)
            text.AppendLine("  " + FormatWindow(window));

        text.AppendLine("Aggregate run comparison (all values are audit-only):");
        text.AppendLine("Metric | Exit | Kira | Frozen | Tower");
        IReadOnlyDictionary<string, TechV2ForensicRunSummary> summaries = audits
            .Where(pair => pair.Key is "Exit" or "Kira" or "Frozen" or "Tower")
            .ToDictionary(pair => pair.Key, pair => SummarizeRuns(pair.Value.SustainedRuns));
        foreach ((string metric, Func<TechV2ForensicRunSummary, double> selector) in MetricRows)
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0} | {1:F3} | {2:F3} | {3:F3} | {4:F3}",
                metric,
                selector(summaries["Exit"]),
                selector(summaries["Kira"]),
                selector(summaries["Frozen"]),
                selector(summaries["Tower"])));
        }

        text.AppendLine("Offline candidate guards (never applied to the engine):");
        foreach (TechV2ForensicGuard guard in CreateCandidateGuards(audits))
        {
            text.AppendLine($"  {guard.Name}: {guard.Derivation}");
            text.AppendLine(
                $"    Exit kept={guard.Results["Exit"]}; Kira kept={guard.Results["Kira"]}; " +
                $"Frozen kept={guard.Results["Frozen"]}; Tower kept={guard.Results["Tower"]}; " +
                $"can you unchanged={guard.Results["CanYou"]}");
        }

        return text.ToString();
    }

    private static readonly (string Name, Func<TechV2ForensicRunSummary, double> Select)[] MetricRows =
    [
        ("Avg Geometry", run => run.AverageGeometry),
        ("Avg Movement", run => run.AverageMovement),
        ("Avg Slider", run => run.AverageSlider),
        ("Avg Motif", run => run.AverageMotif),
        ("Avg GeometryIrregularity", run => run.AverageGeometryIrregularity),
        ("Avg RhythmIrregularity", run => run.AverageRhythmIrregularity),
        ("Avg SliderComplexity", run => run.AverageSliderComplexity),
        ("Avg Awkwardness", run => run.AverageAwkwardness),
        ("Avg SpacingVariation", run => run.AverageSpacingVariation),
        ("Avg AngleCoherenceWithSupport", run => run.AverageAngleCoherence),
        ("Avg SliderToSliderRatio", run => run.AverageSliderToSlider),
        ("Avg SliderToCircleRatio", run => run.AverageSliderToCircle),
        ("Avg CircleToSliderRatio", run => run.AverageCircleToSlider),
        ("Avg CircleToCircleRatio", run => run.AverageCircleToCircle),
        ("Avg ComplexSliderRunLength", run => run.AverageComplexSliderRunLength),
        ("Avg CoupledTechnicalMotifContinuity", run => run.AverageCoupledMotif),
        ("Interval CV", run => run.Intervals.CoefficientOfVariation),
        ("Spacing CV", run => run.Spacings.CoefficientOfVariation),
        ("Angle SD", run => run.Angles.StandardDeviation),
        ("Repeated interval ratio", run => run.Intervals.RepeatedBinRatio),
        ("Repeated spacing ratio", run => run.Spacings.RepeatedBinRatio),
        ("Repeated angle-bin ratio", run => run.Angles.RepeatedBinRatio),
        ("Dominant interval ratio", run => run.Intervals.DominantBinRatio),
        ("Dominant spacing ratio", run => run.Spacings.DominantBinRatio),
    ];

    private static void AddRun(
        ICollection<TechV2ForensicRun> runs,
        IReadOnlyList<HitObject> objects,
        IReadOnlyList<TechV2WindowDecision> decisions,
        IReadOnlyDictionary<int, TechV2ExperimentalWindowResult> byStartIndex)
    {
        if (decisions.Count == 0)
            return;

        IReadOnlyList<TechV2ForensicWindow> windows = decisions
            .Select(decision => CreateWindow(objects, decision, byStartIndex[decision.StartIndex]))
            .ToArray();
        int startIndex = decisions.Min(decision => decision.StartIndex);
        int endIndex = decisions.Max(decision => decision.EndIndex);

        runs.Add(new TechV2ForensicRun(
            decisions.Min(decision => decision.StartTime),
            decisions.Max(decision => decision.EndTime),
            decisions.Max(decision => decision.EndTime) - decisions.Min(decision => decision.StartTime),
            decisions.Count,
            decisions.Count(decision => decision.IsStrongTech),
            decisions.Count(decision => decision.IsTechSupport),
            windows,
            CreateRangeMetrics(objects, startIndex, endIndex)));
    }

    private static TechV2ForensicWindow CreateWindow(
        IReadOnlyList<HitObject> objects,
        TechV2WindowDecision decision,
        TechV2ExperimentalWindowResult source) =>
        new(
            decision,
            source,
            CreateRangeMetrics(objects, decision.StartIndex, decision.EndIndex),
            TechV2DecisionCalibrationAudit.Describe(decision));

    private static TechV2ForensicRangeMetrics CreateRangeMetrics(
        IReadOnlyList<HitObject> objects,
        int start,
        int end)
    {
        List<double> intervals = [];
        List<double> spacings = [];
        List<double> angles = [];
        start = Math.Clamp(start, 0, Math.Max(0, objects.Count - 1));
        end = Math.Clamp(end, start, Math.Max(0, objects.Count - 1));

        for (int index = start + 1; index <= end; index++)
        {
            HitObject previous = objects[index - 1];
            HitObject current = objects[index];
            if (GameplayAnalyzer.IsSpinner(previous) || GameplayAnalyzer.IsSpinner(current))
                continue;

            double interval = current.Time - previous.Time;
            if (interval > 0)
                intervals.Add(interval);

            spacings.Add(Distance(
                GameplayAnalyzer.GetObjectEndPosition(previous),
                GameplayAnalyzer.GetObjectStartPosition(current)));
        }

        for (int index = start + 1; index < end; index++)
        {
            HitObject previous = objects[index - 1];
            HitObject current = objects[index];
            HitObject next = objects[index + 1];
            if (GameplayAnalyzer.IsSpinner(previous)
                || GameplayAnalyzer.IsSpinner(current)
                || GameplayAnalyzer.IsSpinner(next)
                || current.Time <= previous.Time
                || next.Time <= current.Time)
            {
                continue;
            }

            angles.Add(GameplayAnalyzer.GetTechTurnAngle(previous, current, next));
        }

        return new TechV2ForensicRangeMetrics(
            Distribution(intervals, IntervalBinMilliseconds),
            Distribution(spacings, SpacingBinPixels),
            Distribution(angles, AngleBinDegrees));
    }

    internal static TechV2ForensicRunSummary SummarizeRuns(
        IReadOnlyList<TechV2ForensicRun> runs)
    {
        IReadOnlyList<TechV2ForensicWindow> windows = runs.SelectMany(run => run.Windows).ToArray();
        return new TechV2ForensicRunSummary(
            Average(windows, window => window.Decision.GeometryEvidence),
            Average(windows, window => window.Decision.MovementEvidence),
            Average(windows, window => window.Decision.SliderEvidence),
            Average(windows, window => window.Decision.MotifEvidence),
            Average(windows, window => window.Source.GeometryIrregularity),
            Average(windows, window => window.Source.RhythmIrregularity),
            Average(windows, window => window.Source.SliderComplexity),
            Average(windows, window => window.Source.Awkwardness),
            Average(windows, window => window.Source.SpacingVariation),
            Average(windows, window => window.Source.AngleCoherenceWithSupport),
            Average(windows, window => window.Source.ComplexSliderTransitionTopology.SliderToSliderRatio),
            Average(windows, window => window.Source.ComplexSliderTransitionTopology.SliderToCircleRatio),
            Average(windows, window => window.Source.ComplexSliderTransitionTopology.CircleToSliderRatio),
            Average(windows, window => window.Source.ComplexSliderTransitionTopology.CircleToCircleRatio),
            Average(windows, window => window.Source.ComplexSliderRunLength),
            Average(windows, window => window.Source.CoupledTechnicalMotifContinuity),
            AggregateDistribution(runs.Select(run => run.Metrics.Intervals)),
            AggregateDistribution(runs.Select(run => run.Metrics.Spacings)),
            AggregateDistribution(runs.Select(run => run.Metrics.Angles)))
        {
            MaxGeometry = Maximum(windows, window => window.Decision.GeometryEvidence),
            MaxMovement = Maximum(windows, window => window.Decision.MovementEvidence),
            MaxSlider = Maximum(windows, window => window.Decision.SliderEvidence),
            MaxMotif = Maximum(windows, window => window.Decision.MotifEvidence),
        };
    }

    private static TechV2ForensicGuard CreateGuard(
        string name,
        string derivation,
        Func<TechV2ForensicRunSummary, bool> keepsRun,
        IReadOnlyDictionary<string, TechV2ForensicMapAudit> audits) =>
        new(
            name,
            derivation,
            audits.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.SustainedRuns.Count == 0
                    // No sustained passage means this simulated run guard has
                    // nothing to remove; the control remains unchanged.
                    ? true
                    : keepsRun(SummarizeRuns(pair.Value.SustainedRuns)),
                StringComparer.Ordinal));

    private static TechV2ForensicDistribution AggregateDistribution(
        IEnumerable<TechV2ForensicDistribution> distributions)
    {
        TechV2ForensicDistribution[] available = distributions
            .Where(distribution => distribution.Count > 0)
            .ToArray();
        return available.Length == 0
            ? new TechV2ForensicDistribution(0, 0, 0, 0, 0, 0, 0)
            : new TechV2ForensicDistribution(
                available.Sum(distribution => distribution.Count),
                available.Average(distribution => distribution.Mean),
                available.Average(distribution => distribution.StandardDeviation),
                available.Average(distribution => distribution.CoefficientOfVariation),
                available.Average(distribution => distribution.RepeatedBinRatio),
                available.Average(distribution => distribution.DominantBinRatio),
                available.Max(distribution => distribution.Maximum));
    }

    private static double Average<T>(IReadOnlyList<T> values, Func<T, double> selector) =>
        values.Count == 0 ? 0 : values.Average(selector);

    private static double Maximum<T>(IReadOnlyList<T> values, Func<T, double> selector) =>
        values.Count == 0 ? 0 : values.Max(selector);

    private static double Midpoint(double left, double right) => (left + right) / 2.0;

    private static double Distance((double X, double Y) first, (double X, double Y) second)
    {
        double x = second.X - first.X;
        double y = second.Y - first.Y;
        return Math.Sqrt(x * x + y * y);
    }

    private static string FormatRun(
        TechV2ForensicRun run,
        TechV2ForensicSignatureScale scale)
    {
        TechV2ForensicRunSummary summary = SummarizeRuns([run]);
        return string.Format(
        CultureInfo.InvariantCulture,
        "{0:F0}->{1:F0}ms | Duration={2:F0}ms | Windows={3} | Strong={4} | Support={5} | " +
        "G/M/S/Mo avg={6:F3}/{7:F3}/{8:F3}/{9:F3} max={10:F3}/{11:F3}/{12:F3}/{13:F3} | " +
        "interval mean/sd/cv={14:F2}/{15:F2}/{16:F3} | spacing mean/sd/cv={17:F2}/{18:F2}/{19:F3} | " +
        "angle mean/sd={20:F2}/{21:F2} | repeat i/s/a={22:F3}/{23:F3}/{24:F3} | {25}",
        run.StartTime, run.EndTime, run.Duration, run.WindowCount, run.StrongTechCount, run.TechSupportCount,
        summary.AverageGeometry, summary.AverageMovement, summary.AverageSlider, summary.AverageMotif,
        summary.MaxGeometry, summary.MaxMovement, summary.MaxSlider, summary.MaxMotif,
        run.Metrics.Intervals.Mean, run.Metrics.Intervals.StandardDeviation, run.Metrics.Intervals.CoefficientOfVariation,
        run.Metrics.Spacings.Mean, run.Metrics.Spacings.StandardDeviation, run.Metrics.Spacings.CoefficientOfVariation,
        run.Metrics.Angles.Mean, run.Metrics.Angles.StandardDeviation,
        run.Metrics.Intervals.RepeatedBinRatio, run.Metrics.Spacings.RepeatedBinRatio, run.Metrics.Angles.RepeatedBinRatio,
        FormatSignature(run, scale));
    }

    private static string FormatSignature(
        TechV2ForensicRun run,
        TechV2ForensicSignatureScale scale)
    {
        TechV2ForensicRunSummary summary = SummarizeRuns([run]);
        string dominantPair = string.Join(
            "+",
            new[]
            {
                ("Geometry", summary.AverageGeometry, 0),
                ("Movement", summary.AverageMovement, 1),
                ("Slider", summary.AverageSlider, 2),
                ("Motif", summary.AverageMotif, 3),
            }
            .OrderByDescending(item => item.Item2)
            .ThenBy(item => item.Item3)
            .Take(2)
            .Select(item => item.Item1));

        return $"Signature Pair={dominantPair}; " +
            $"Structural variability={scale.ClassifyStructural(run.Metrics.Angles.CoefficientOfVariation)}; " +
            $"Rhythm variability={scale.ClassifyRhythm(run.Metrics.Intervals.CoefficientOfVariation)}; " +
            $"Slider topology complexity={scale.ClassifySlider(summary.SliderTopologyComplexity)}; " +
            $"Motif continuity={scale.ClassifyMotif(summary.AverageMotif)}; " +
            $"Repetition={scale.ClassifyRepetition(run.Metrics.Intervals.RepeatedBinRatio)}";
    }

    private static string FormatWindow(TechV2ForensicWindow window) => string.Format(
        CultureInfo.InvariantCulture,
        "{0:F0}->{1:F0}ms | {2} | G/M/S/Mo={3:F3}/{4:F3}/{5:F3}/{6:F3} | Best={7}:{8:F3} Second={9}:{10:F3} | " +
        "raw G/R/S/A/Sp/AC={11:F3}/{12:F3}/{13:F3}/{14:F3}/{15:F3}/{16:F3} | " +
        "topology S>S/S>C/C>S/C>C={17:F3}/{18:F3}/{19:F3}/{20:F3} | ComplexRun={21} Motif={22:F3}",
        window.Decision.StartTime, window.Decision.EndTime,
        window.Decision.IsStrongTech ? "StrongTech" : window.Decision.IsSustainedTech
            ? "SustainedTech"
            : "TechSupport",
        window.Decision.GeometryEvidence, window.Decision.MovementEvidence,
        window.Decision.SliderEvidence, window.Decision.MotifEvidence,
        window.Calibration.BestFamily.Name, window.Calibration.BestFamily.Value,
        window.Calibration.SecondBestFamily.Name, window.Calibration.SecondBestFamily.Value,
        window.Source.GeometryIrregularity, window.Source.RhythmIrregularity,
        window.Source.SliderComplexity, window.Source.Awkwardness,
        window.Source.SpacingVariation, window.Source.AngleCoherenceWithSupport,
        window.Source.ComplexSliderTransitionTopology.SliderToSliderRatio,
        window.Source.ComplexSliderTransitionTopology.SliderToCircleRatio,
        window.Source.ComplexSliderTransitionTopology.CircleToSliderRatio,
        window.Source.ComplexSliderTransitionTopology.CircleToCircleRatio,
        window.Source.ComplexSliderRunLength,
        window.Source.CoupledTechnicalMotifContinuity);
}

internal sealed record TechV2ForensicDistribution(
    int Count,
    double Mean,
    double StandardDeviation,
    double CoefficientOfVariation,
    double RepeatedBinRatio,
    double DominantBinRatio,
    double Maximum);

internal sealed record TechV2ForensicRangeMetrics(
    TechV2ForensicDistribution Intervals,
    TechV2ForensicDistribution Spacings,
    TechV2ForensicDistribution Angles);

internal sealed record TechV2ForensicWindow(
    TechV2WindowDecision Decision,
    TechV2ExperimentalWindowResult Source,
    TechV2ForensicRangeMetrics Metrics,
    TechV2DecisionCalibrationWindow Calibration);

internal sealed record TechV2ForensicRun(
    double StartTime,
    double EndTime,
    double Duration,
    int WindowCount,
    int StrongTechCount,
    int TechSupportCount,
    IReadOnlyList<TechV2ForensicWindow> Windows,
    TechV2ForensicRangeMetrics Metrics);

internal sealed record TechV2ForensicRunSummary(
    double AverageGeometry,
    double AverageMovement,
    double AverageSlider,
    double AverageMotif,
    double AverageGeometryIrregularity,
    double AverageRhythmIrregularity,
    double AverageSliderComplexity,
    double AverageAwkwardness,
    double AverageSpacingVariation,
    double AverageAngleCoherence,
    double AverageSliderToSlider,
    double AverageSliderToCircle,
    double AverageCircleToSlider,
    double AverageCircleToCircle,
    double AverageComplexSliderRunLength,
    double AverageCoupledMotif,
    TechV2ForensicDistribution Intervals,
    TechV2ForensicDistribution Spacings,
    TechV2ForensicDistribution Angles)
{
    internal double MaxGeometry { get; init; }
    internal double MaxMovement { get; init; }
    internal double MaxSlider { get; init; }
    internal double MaxMotif { get; init; }

    // Test-only topology diversity: a window set dominated by one transition
    // type is less topologically varied than a mixed set with equal slider
    // complexity. It is not a V2 feature and never reaches the detector.
    internal double SliderTopologyComplexity => AverageSliderComplexity * (1 - Math.Max(
        Math.Max(AverageSliderToSlider, AverageSliderToCircle),
        Math.Max(AverageCircleToSlider, AverageCircleToCircle)));
}

internal sealed record TechV2ForensicMapAudit(
    string MapName,
    int StrongTechWindowCount,
    int TechSupportWindowCount,
    int SustainedTechWindowCount,
    int FinalTechWindowCount,
    TechV2PresenceResult BeforePresence,
    TechV2PresenceResult AfterPresence,
    IReadOnlyList<TechV2ForensicRun> SustainedRuns,
    IReadOnlyList<TechV2ForensicWindow> SustainedWindows,
    IReadOnlyList<TechV2ForensicWindow> SupportControlWindows);

internal sealed record TechV2ForensicGuard(
    string Name,
    string Derivation,
    IReadOnlyDictionary<string, bool> Results);

/// <summary>
/// Test-only labels derived from the observed lower/upper tertiles of this
/// forensic corpus. They are descriptive output, not candidate thresholds.
/// </summary>
internal sealed record TechV2ForensicSignatureScale(
    double StructuralLower,
    double StructuralUpper,
    double RhythmLower,
    double RhythmUpper,
    double SliderLower,
    double SliderUpper,
    double MotifLower,
    double MotifUpper,
    double RepetitionLower,
    double RepetitionUpper)
{
    internal static TechV2ForensicSignatureScale Create(
        IEnumerable<TechV2ForensicRun> runs)
    {
        TechV2ForensicRun[] values = runs.ToArray();
        return new TechV2ForensicSignatureScale(
            Lower(values.Select(run => run.Metrics.Angles.CoefficientOfVariation)),
            Upper(values.Select(run => run.Metrics.Angles.CoefficientOfVariation)),
            Lower(values.Select(run => run.Metrics.Intervals.CoefficientOfVariation)),
            Upper(values.Select(run => run.Metrics.Intervals.CoefficientOfVariation)),
            Lower(values.Select(run => TechV2TowerForensicAudit.SummarizeRuns([run]).SliderTopologyComplexity)),
            Upper(values.Select(run => TechV2TowerForensicAudit.SummarizeRuns([run]).SliderTopologyComplexity)),
            Lower(values.Select(run => TechV2TowerForensicAudit.SummarizeRuns([run]).AverageMotif)),
            Upper(values.Select(run => TechV2TowerForensicAudit.SummarizeRuns([run]).AverageMotif)),
            Lower(values.Select(run => run.Metrics.Intervals.RepeatedBinRatio)),
            Upper(values.Select(run => run.Metrics.Intervals.RepeatedBinRatio)));
    }

    internal string ClassifyStructural(double value) => Classify(value, StructuralLower, StructuralUpper);
    internal string ClassifyRhythm(double value) => Classify(value, RhythmLower, RhythmUpper);
    internal string ClassifySlider(double value) => Classify(value, SliderLower, SliderUpper);
    internal string ClassifyMotif(double value) => Classify(value, MotifLower, MotifUpper);
    internal string ClassifyRepetition(double value) => Classify(value, RepetitionLower, RepetitionUpper);

    private static double Lower(IEnumerable<double> values) => Quantile(values, 1.0 / 3.0);
    private static double Upper(IEnumerable<double> values) => Quantile(values, 2.0 / 3.0);

    private static double Quantile(IEnumerable<double> source, double probability)
    {
        double[] values = source.OrderBy(value => value).ToArray();
        if (values.Length == 0)
            return 0;

        double index = (values.Length - 1) * probability;
        int lower = (int)Math.Floor(index);
        int upper = (int)Math.Ceiling(index);
        return values[lower] + ((values[upper] - values[lower]) * (index - lower));
    }

    private static string Classify(double value, double lower, double upper) =>
        value < lower ? "Low" : value < upper ? "Medium" : "High";
}
